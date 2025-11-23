using NetSdrClientApp.Networking;
using NUnit.Framework;
using Moq;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.IO;
using System.Net.Sockets;

namespace NetSdrClientAppTests.Networking
{
    [TestFixture]
    public class TcpClientWrapperTests
    {
        private Mock<ISystemTcpClient> _clientMock = null!;
        private Mock<INetworkStream> _streamMock = null!;
        private TcpClientWrapper _wrapper = null!;

        [SetUp]
        public void SetUp()
        {
            _streamMock = new Mock<INetworkStream>();
            _clientMock = new Mock<ISystemTcpClient>();

            // Setup basic successful behavior
            _clientMock.Setup(c => c.GetStream()).Returns(_streamMock.Object);
            _clientMock.SetupGet(c => c.Connected).Returns(true);
            _streamMock.SetupGet(s => s.CanRead).Returns(true);
            _streamMock.SetupGet(s => s.CanWrite).Returns(true);

            // Factory returning our mock object for testing
            Func<ISystemTcpClient> factory = () => _clientMock.Object;

            // Initialize the wrapper for testing (using DI constructor)
            _wrapper = new TcpClientWrapper("127.0.0.1", 5000, factory);
        }

        // ------------------------------------------------------------------
        // SCENARIO 1: SUCCESSFUL CONNECTION (Happy Path Coverage)
        // ------------------------------------------------------------------

        [Test]
        public void Connect_WhenNotConnected_ShouldConnectAndStartListening()
        {
            // Arrange: Ensure initial state is not connected
            _clientMock.SetupGet(c => c.Connected).Returns(false);

            // Act
            _wrapper.Connect();

            // Assert
            // 1. Verify that the Connect() method was called
            _clientMock.Verify(c => c.Connect("127.0.0.1", 5000), Times.Once);
            // 2. Verify that the stream was retrieved
            _clientMock.Verify(c => c.GetStream(), Times.Once);

            // NOTE: Connected check will be performed via the mock Get (true)
        }

        // ------------------------------------------------------------------
        // SCENARIO 2: DISCONNECTION (Disconnect Coverage)
        // ------------------------------------------------------------------

        [Test]
        public void Disconnect_WhenConnected_ShouldCloseResources()
        {
            // Arrange: Simulate connected state
            _wrapper.Connect(); // Call to initialize _cts
            _clientMock.Invocations.Clear(); // Clear mock for Disconnect verification

            // Act
            _wrapper.Disconnect();

            // Assert: Verify all Close/Cancel were called
            _streamMock.Verify(s => s.Close(), Times.Once);
            _clientMock.Verify(c => c.Close(), Times.Once);

            // Verify that Connected is now false
            Assert.That(_wrapper.Connected, Is.False);
        }

        [Test]
        public void Disconnect_WhenNotConnected_ShouldDoNothing()
        {
            // Arrange: Initial state (Connected = false)
            _clientMock.SetupGet(c => c.Connected).Returns(false);

            // Act
            _wrapper.Disconnect();

            // Assert: Verify Close/Cancel methods were NOT called
            _streamMock.Verify(s => s.Close(), Times.Never);
        }

        // ------------------------------------------------------------------
        // SCENARIO 3: CONNECTION ERROR HANDLING (Connect Error Coverage)
        // ------------------------------------------------------------------

        [Test]
        public void Connect_WhenFails_ShouldCatchException()
        {
            // Arrange: Set up mock so Connect throws an exception
            _clientMock.Setup(c => c.Connect(It.IsAny<string>(), It.IsAny<int>()))
                       .Throws(new SocketException(10061));

            // Act
            _wrapper.Connect();

            // Assert: Ensure Connected = false after error
            Assert.That(_wrapper.Connected, Is.False);
        }

        // ------------------------------------------------------------------
        // SCENARIO 4: DATA SENDING (Send Message Coverage)
        // ------------------------------------------------------------------

        [Test]
        public async Task SendMessageAsync_WhenConnected_ShouldWriteToStream()
        {
            // Arrange: Ensure Connect was successful
            _wrapper.Connect();
            byte[] testData = { 0x01, 0x02, 0x03 };

            // Act
            await _wrapper.SendMessageAsync(testData);

            // Assert: Verify WriteAsync was called on the stream with correct data
            _streamMock.Verify(s => s.WriteAsync(
                It.Is<byte[]>(arr => arr == testData),
                0,
                testData.Length,
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public void SendMessageAsync_WhenNotConnected_ShouldThrowException()
        {
            // Arrange: Simulate "not connected" state
            _clientMock.SetupGet(c => c.Connected).Returns(false);

            // Act & Assert
            Assert.ThrowsAsync<InvalidOperationException>(
                () => _wrapper.SendMessageAsync(new byte[] { 0x01 }));
        }

        // ------------------------------------------------------------------
        // SCENARIO 5: LISTENING (Listening Coverage - Partial)
        // Full coverage of StartListeningAsync requires ReadAsync simulation
        // ------------------------------------------------------------------

        [Test]
        public async Task StartListeningAsync_WhenCancelled_ShouldStopListeningAndDisconnect()
        {
            // Arrange
            _wrapper.Connect();

            // Simulate ReadAsync concluding with cancellation (OperationCanceledException)
            _streamMock
                .Setup(s => s.ReadAsync(
                    It.IsAny<byte[]>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());

            // Act
            // Since StartListeningAsync starts in Connect, we wait for it to complete
            await Task.Delay(50);

            // Disconnect at the end of StartListeningAsync should be called
            _streamMock.Verify(s => s.Close(), Times.AtLeastOnce);
        }

        [Test]
        public async Task StartListeningAsync_WhenConnectionIsClosed_ShouldStopListeningAndDisconnect()
        {
            // Arrange
            _wrapper.Connect();

            // Simulate ReadAsync returning 0 (end of stream)
            _streamMock
                .Setup(s => s.ReadAsync(
                    It.IsAny<byte[]>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            // Act
            await Task.Delay(50);

            // Assert: Verify that resource closure was called
            _clientMock.Verify(c => c.Close(), Times.AtLeastOnce);
        }
    }
}