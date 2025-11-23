using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NetSdrClientApp.Messages
{
    //TODO: analyze possible use of [StructLayout] for better performance and readability 
    public static class NetSdrMessageHelper
    {
        private const short _maxMessageLength = 8191;
        private const short _maxDataItemMessageLength = 8194;
        private const short _msgHeaderLength = 2; // 2 byte, 16 bit
        private const short _msgControlItemLength = 2; // 2 byte, 16 bit
        private const short _msgSequenceNumberLength = 2; // 2 byte, 16 bit

        public enum MsgTypes
        {
            SetControlItem,
            CurrentControlItem,
            ControlItemRange,
            Ack,
            DataItem0,
            DataItem1,
            DataItem2,
            DataItem3
        }

        // Changed base type to ushort (UInt16) to correctly handle conversion from BitConverter.ToUInt16 
        // and prevent System.ArgumentException in Enum.IsDefined.
        public enum ControlItemCodes : ushort
        {
            None = 0,
            IQOutputDataSampleRate = 0x00B8,
            RFFilter = 0x0044,
            ADModes = 0x008A,
            ReceiverState = 0x0018,
            ReceiverFrequency = 0x0020
        }

        public static byte[] GetControlItemMessage(MsgTypes type, ControlItemCodes itemCode, byte[] parameters)
        {
            return GetMessage(type, itemCode, parameters);
        }

        public static byte[] GetDataItemMessage(MsgTypes type, byte[] parameters)
        {
            return GetMessage(type, ControlItemCodes.None, parameters);
        }

        private static byte[] GetMessage(MsgTypes type, ControlItemCodes itemCode, byte[] parameters)
        {
            var itemCodeBytes = Array.Empty<byte>();
            if (itemCode != ControlItemCodes.None)
            {
                // Convert ControlItemCodes (ushort) to bytes
                itemCodeBytes = BitConverter.GetBytes((ushort)itemCode);
            }

            // Total length of data, including the control item code (if present)
            var totalBodyLength = itemCodeBytes.Length + parameters.Length;

            var headerBytes = GetHeader(type, totalBodyLength);

            List<byte> msg = new List<byte>();
            msg.AddRange(headerBytes);
            msg.AddRange(itemCodeBytes);
            msg.AddRange(parameters);

            return msg.ToArray();
        }

        // Refactored to use offset indexing for robust parsing and added boundary checks.
        public static bool TranslateMessage(byte[] msg, out MsgTypes type, out ControlItemCodes itemCode, out ushort sequenceNumber, out byte[] body)
        {
            type = default;
            itemCode = ControlItemCodes.None;
            sequenceNumber = 0;
            body = Array.Empty<byte>();

            int offset = 0;

            // 1. Check minimum message length (header)
            if (msg == null || msg.Length < _msgHeaderLength)
            {
                return false;
            }

            // 2. Parse header
            TranslateHeader(msg.Take(_msgHeaderLength).ToArray(), out type, out int expectedBodyLength);
            offset += _msgHeaderLength;

            // Check if the message has the expected length based on the header value
            if (msg.Length != _msgHeaderLength + expectedBodyLength)
            {
                // Length mismatch (Fix for TranslateMessage_ShouldFailOnInvalidBodyLength)
                return false;
            }

            int remainingLength = expectedBodyLength;

            if (type < MsgTypes.DataItem0) // Process Control Item Code
            {
                // Control Item message must contain at least the control item code
                if (remainingLength < _msgControlItemLength)
                {
                    return false;
                }

                // Read Control Item Code
                ushort value = BitConverter.ToUInt16(msg, offset);
                offset += _msgControlItemLength;
                remainingLength -= _msgControlItemLength;

                if (Enum.IsDefined(typeof(ControlItemCodes), value))
                {
                    itemCode = (ControlItemCodes)value;
                }
                else
                {
                    // Invalid control item code (Fix for TranslateMessage_ShouldFailOnInvalidControlItemCode)
                    return false;
                }
            }
            else // Process Data Item (Sequence Number)
            {
                // Data Item message must contain at least the sequence number
                if (remainingLength < _msgSequenceNumberLength)
                {
                    return false;
                }

                // Read Sequence Number
                sequenceNumber = BitConverter.ToUInt16(msg, offset);
                offset += _msgSequenceNumberLength;
                remainingLength -= _msgSequenceNumberLength;
            }

            // 3. Extract body
            if (remainingLength > 0)
            {
                // Create array for the body
                body = new byte[remainingLength];

                // Copy the body bytes (Fixes issues related to improper length calculation for body)
                Array.Copy(msg, offset, body, 0, remainingLength);
            }

            return true;
        }

        public static IEnumerable<int> GetSamples(ushort sampleSize, byte[] body)
        {
            ushort sampleSizeInBytes = (ushort)(sampleSize / 8); // to bytes

            if (sampleSizeInBytes == 0 || sampleSizeInBytes > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleSize), "SampleSize must be between 8 and 32 bits and a multiple of 8.");
            }

            for (int i = 0; i <= body.Length - sampleSizeInBytes; i += sampleSizeInBytes)
            {
                // Create a 4-byte array for ToInt32
                byte[] sampleBytes = new byte[4];

                // Copy sample bytes (assuming Little Endian)
                Array.Copy(body, i, sampleBytes, 0, sampleSizeInBytes);

                yield return BitConverter.ToInt32(sampleBytes);
            }
        }

        private static byte[] GetHeader(MsgTypes type, int msgLength)
        {
            // msgLength is the length of the message body (itemCode + parameters).
            int lengthWithHeader = msgLength + _msgHeaderLength;

            // Data Items edge case: if length reaches max value, set header length to 0 (for DataItem)
            if (type >= MsgTypes.DataItem0 && lengthWithHeader == _maxDataItemMessageLength)
            {
                lengthWithHeader = 0;
            }

            if (msgLength < 0 || lengthWithHeader > _maxMessageLength)
            {
                throw new ArgumentException("Message length exceeds allowed value", nameof(msgLength));
            }

            // Header format: 3 bits type + 13 bits length
            ushort headerValue = (ushort)(lengthWithHeader | ((ushort)type << 13));

            return BitConverter.GetBytes(headerValue);
        }

        private static void TranslateHeader(byte[] header, out MsgTypes type, out int msgLength)
        {
            var num = BitConverter.ToUInt16(header.ToArray());

            // Extract type (3 bits)
            type = (MsgTypes)(num >> 13);

            // Extract length (13 bits) using a mask for reliability: 0b0001_1111_1111_1111
            msgLength = num & 0x1FFF;

            // Data Items edge case: if DataItem has length 0, it means _maxDataItemMessageLength
            if (type >= MsgTypes.DataItem0 && msgLength == 0)
            {
                msgLength = _maxDataItemMessageLength;
            }

            // The returned msgLength is the total message length including the header.
            // We subtract the header length to get the expected body length (code/sequence + parameters)
            msgLength -= _msgHeaderLength;
        }
    }
}