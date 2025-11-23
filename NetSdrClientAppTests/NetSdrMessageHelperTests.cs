using NetSdrClientApp.Messages;
using NUnit.Framework;
using System.Linq;
using System;
using System.Text;
using System.Collections.Generic;

namespace NetSdrClientAppTests
{
    [TestFixture]
    public class NetSdrMessageHelperTests
    {
        // ------------------------------------------------------------------
        // GET MESSAGE TESTS
        // ------------------------------------------------------------------

        [Test]
        public void GetControlItemMessageTest_WithItemCode()
        {
            // Arrange
            var type = NetSdrMessageHelper.MsgTypes.Ack;
            var code = NetSdrMessageHelper.ControlItemCodes.ReceiverState;
            int parametersLength = 100;

            // Act
            byte[] msg = NetSdrMessageHelper.GetControlItemMessage(type, code, new byte[parametersLength]);

            // Assert
            // 2 bytes (header) + 2 (code) + 100 (params) = 104
            Assert.That(msg.Length, Is.EqualTo(104));

            // Check code (2 bytes)
            var actualCode = BitConverter.ToUInt16(msg.Skip(2).Take(2).ToArray());
            Assert.That(actualCode, Is.EqualTo((ushort)code));
        }

        [Test]
        public void GetControlItemMessageTest_WithoutItemCode()
        {
            // Arrange
            var type = NetSdrMessageHelper.MsgTypes.Ack;
            var code = NetSdrMessageHelper.ControlItemCodes.None;
            int parametersLength = 100;

            // Act
            byte[] msg = NetSdrMessageHelper.GetControlItemMessage(type, code, new byte[parametersLength]);

            // Assert
            // 2 bytes (header) + 100 bytes parameters = 102
            Assert.That(msg.Length, Is.EqualTo(102));
        }

        [Test]
        public void GetDataItemMessageTest_NormalLength()
        {
            // Arrange
            var type = NetSdrMessageHelper.MsgTypes.DataItem2;
            int parametersLength = 7500;

            // Act
            byte[] msg = NetSdrMessageHelper.GetDataItemMessage(type, new byte[parametersLength]);

            // Assert (Check if the header is correct)
            var headerBytes = msg.Take(2);
            var num = BitConverter.ToUInt16(headerBytes.ToArray());
            var actualType = (NetSdrMessageHelper.MsgTypes)(num >> 13);
            var actualLength = num & 0x1FFF; // Use mask for clarity

            // TranslateHeader logic:
            // msgLength = 7500 + 2 = 7502 (Length in header)
            Assert.That(msg.Length, Is.EqualTo(7502));
            Assert.That(actualLength, Is.EqualTo(7502));
            Assert.That(type, Is.EqualTo(actualType));
        }

        [Test]
        public void GetHeader_ThrowsExceptionOnTooLongMessage()
        {
            // Arrange / Act / Assert
            // _maxMessageLength = 8191. (8190 + 2) = 8192 > 8191
            int tooLongLength = 8190;

            Assert.Throws<ArgumentException>(() =>
                NetSdrMessageHelper.GetControlItemMessage(
                    NetSdrMessageHelper.MsgTypes.SetControlItem,
                    NetSdrMessageHelper.ControlItemCodes.None,
                    new byte[tooLongLength]));
        }

        [Test]
        public void GetHeader_DataItemEdgeCaseZeroLength()
        {
            // _maxDataItemMessageLength = 8194. lengthWithHeader = msgLength + 2. msgLength = 8192
            int msgLength = 8192;

            // Act: Call GetMessage, which calls GetHeader
            byte[] msg = NetSdrMessageHelper.GetDataItemMessage(
                NetSdrMessageHelper.MsgTypes.DataItem0,
                new byte[msgLength]);

            // Assert: Check that the actual length in the header is 0
            var headerBytes = msg.Take(2).ToArray();
            var num = BitConverter.ToUInt16(headerBytes);

            // Extract type and length from header
            var actualType = (NetSdrMessageHelper.MsgTypes)(num >> 13);
            var actualLengthInHeader = num & 0x1FFF;

            Assert.That(actualLengthInHeader, Is.EqualTo(0)); // Length field in header should be 0
            Assert.That(msg.Length, Is.EqualTo(8194)); // Actual physical length is 8194
            Assert.That(actualType, Is.EqualTo(NetSdrMessageHelper.MsgTypes.DataItem0));
        }

        // ------------------------------------------------------------------

        [Test]
        public void TranslateMessage_ShouldDecodeControlItemCorrectly()
        {
            // Arrange: Create a test message with ControlItemCode
            var type = NetSdrMessageHelper.MsgTypes.SetControlItem;
            var code = NetSdrMessageHelper.ControlItemCodes.IQOutputDataSampleRate;
            byte[] parameters = { 0xAA, 0xBB };
            byte[] msg = NetSdrMessageHelper.GetControlItemMessage(type, code, parameters);

            // Act
            bool success = NetSdrMessageHelper.TranslateMessage(msg, out var actualType, out var actualCode, out var sequenceNumber, out var body);

            // Assert
            Assert.That(success, Is.True);
            Assert.That(actualType, Is.EqualTo(type));
            Assert.That(actualCode, Is.EqualTo(code));
            Assert.That(body, Is.EqualTo(parameters));
            Assert.That(sequenceNumber, Is.EqualTo(0));
        }

        [Test]
        public void TranslateMessage_ShouldDecodeDataItemCorrectly()
        {
            // Arrange: Create a test message with DataItem (DataItem0)
            var type = NetSdrMessageHelper.MsgTypes.DataItem0;
            ushort expectedSequenceNumber = 0xABCD;
            byte[] dataPayload = { 0xAA, 0xBB, 0xCC };

            // Combine SequenceNumber (2 bytes) and Payload (3 bytes) into the parameters for GetMessage
            byte[] parameters = BitConverter.GetBytes(expectedSequenceNumber).Concat(dataPayload).ToArray();

            byte[] msg = NetSdrMessageHelper.GetDataItemMessage(type, parameters);

            // Act
            bool success = NetSdrMessageHelper.TranslateMessage(msg, out var actualType, out var actualCode, out var sequenceNumber, out var body);

            // Assert
            Assert.That(success, Is.True);
            Assert.That(actualType, Is.EqualTo(type));
            Assert.That(actualCode, Is.EqualTo(NetSdrMessageHelper.ControlItemCodes.None));
            Assert.That(sequenceNumber, Is.EqualTo(expectedSequenceNumber));

            // The decoded body should only contain the dataPayload (3 bytes)
            Assert.That(body, Is.EqualTo(dataPayload));
            Assert.That(body.Length, Is.EqualTo(dataPayload.Length));
        }

        [Test]
        public void TranslateMessage_ShouldFailOnInvalidBodyLength()
        {
            // Arrange: Create a correct header but truncate 1 byte from the body
            byte[] parameters = { 0xAA, 0xBB };
            byte[] correctMsg = NetSdrMessageHelper.GetControlItemMessage(NetSdrMessageHelper.MsgTypes.Ack, NetSdrMessageHelper.ControlItemCodes.None, parameters);
            byte[] corruptedMsg = correctMsg.Take(correctMsg.Length - 1).ToArray();

            // Act
            bool success = NetSdrMessageHelper.TranslateMessage(corruptedMsg, out var actualType, out var actualCode, out var sequenceNumber, out var body);

            // Assert: Should return false due to length mismatch
            Assert.That(success, Is.False);
        }

        [Test]
        public void TranslateMessage_ShouldFailOnInvalidControlItemCode()
        {
            // Arrange: Generate a Control message type but insert a non-existent code (0xFFFF)
            var type = NetSdrMessageHelper.MsgTypes.SetControlItem;
            // Total length: Header (2) + Invalid Code (2) + Parameters (2) = 6
            byte[] header = BitConverter.GetBytes((ushort)((int)type << 13 | (6)));
            byte[] invalidCode = BitConverter.GetBytes((ushort)0xFFFF); // Code not defined in Enum
            byte[] msg = header.Concat(invalidCode).Concat(new byte[2]).ToArray();

            // Act
            bool success = NetSdrMessageHelper.TranslateMessage(msg, out var actualType, out var actualCode, out var sequenceNumber, out var body);

            // Assert: Should return false because the code is not defined
            Assert.That(success, Is.False);
        }

        [Test]
        public void TranslateMessage_ShouldFailOnMessageShorterThanHeader()
        {
            // Arrange: Only 1 byte is provided (min header is 2 bytes)
            byte[] shortMsg = { 0x01 };

            // Act
            bool success = NetSdrMessageHelper.TranslateMessage(shortMsg, out var actualType, out var actualCode, out var sequenceNumber, out var body);

            // Assert
            Assert.That(success, Is.False);
        }

        [Test]
        public void TranslateMessage_ShouldFailOnControlBodyTooShort()
        {
            // Arrange: Control item type, but body length is 1 byte (requires 2 for code)
            var type = NetSdrMessageHelper.MsgTypes.SetControlItem;
            byte[] header = BitConverter.GetBytes((ushort)((int)type << 13 | (2 + 1))); // Total message length 3 (header + 1 byte body)
            byte[] msg = header.Concat(new byte[] { 0xAA }).ToArray();

            // Act
            bool success = NetSdrMessageHelper.TranslateMessage(msg, out var actualType, out var actualCode, out var sequenceNumber, out var body);

            // Assert: Should fail because remainingLength < _msgControlItemLength
            Assert.That(success, Is.False);
        }

        [Test]
        public void TranslateMessage_ShouldFailOnDataBodyTooShort()
        {
            // Arrange: Data item type, but body length is 1 byte (requires 2 for sequence number)
            var type = NetSdrMessageHelper.MsgTypes.DataItem0;
            byte[] header = BitConverter.GetBytes((ushort)((int)type << 13 | (2 + 1))); // Total message length 3 (header + 1 byte body)
            byte[] msg = header.Concat(new byte[] { 0xAA }).ToArray();

            // Act
            bool success = NetSdrMessageHelper.TranslateMessage(msg, out var actualType, out var actualCode, out var sequenceNumber, out var body);

            // Assert: Should fail because remainingLength < _msgSequenceNumberLength
            Assert.