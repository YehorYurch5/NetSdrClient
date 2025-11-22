using NetSdrClientApp.Messages;
using NUnit.Framework;
using System.Linq;
using System;
using System.Text;

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
            // 4 bytes (header + code) + 100 bytes parameters = 104
            Assert.That(msg.Length, Is.EqualTo(104));

            // Check code (4 bytes)
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
            var actualLength = num - ((int)actualType << 13);

            Assert.That(msg.Length, Is.EqualTo(actualLength));
            Assert.That(type, Is.EqualTo(actualType));
        }

        // ------------------------------------------------------------------
        // GET HEADER TESTS (Length edge case coverage)
        // ------------------------------------------------------------------

        [Test]
        public void GetHeader_ThrowsExceptionOnNegativeLength()
        {
            // Arrange / Act / Assert
            Assert.Throws<ArgumentException>(() =>
                NetSdrMessageHelper.GetControlItemMessage(
                    NetSdrMessageHelper.MsgTypes.SetControlItem,
                    NetSdrMessageHelper.ControlItemCodes.None,
                    new byte[-1]));
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
            var actualLength = num - ((int)NetSdrMessageHelper.MsgTypes.DataItem0 << 13);

            Assert.That(actualLength, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------
        // TRANSLATE MESSAGE TESTS (Decoding coverage)
        // ------------------------------------------------------------------

        [Test]
        public void TranslateMessage_ShouldDecodeControlItem()
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
            Assert.That(body.Length, Is.EqualTo(parameters.Length));
        }

        [Test]
        public void TranslateMessage_ShouldDecodeDataItem()
        {
            // Arrange: Create a test message with DataItem (DataItem0)
            var type = NetSdrMessageHelper.MsgTypes.DataItem0;
            byte[] parameters = { 0xAA, 0