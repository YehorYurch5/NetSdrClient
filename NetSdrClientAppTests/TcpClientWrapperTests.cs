using NetSdrClientApp.Networking;
using NUnit.Framework;
using Moq;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.Net.Sockets;
using System.Collections.Generic;

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
            // Default setup for Connected: it's true after Connect() is called
            _clientMock.SetupGet(c => c.Connected).Returns(true);
            _streamMock.SetupGet(s => s.CanRead).Returns(true);
            _streamMock.SetupGet(s => s.CanWrite).Returns(true);

            // Setup ReadAsync to block indefinitely unless cancelled, 
            // simulating an active connection that doesn't immediately close.
            // This prevents the background listener task from immediately ending in most tests.
            _streamMock
                .Setup(s => s.ReadAsync(
                    It.IsAny<byte[]>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns<byte[], int, int, CancellationToken>((buffer, offset, size, token) =>
                {
                    var tcs = new TaskCompletionSource<int>();
                    token.Register(() => tcs.TrySetCanceled());
                    return tcs.Task;
                });

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
            // Arrange: Initial state relies on _tcpClient being null, so _wrapper.Connected is false.

            // Act
            _wrapper.Connect();

            // Assert
            // 1. Verify that the Connect() method was called
            _clientMock.Verify(c => c.Connect("127.0.0.1", 5000), Times.Once);
            // 2. Verify that the stream was retrieved
            _clientMock.Verify(c => c.GetStream(), Times.Once);
            // FIX: Assert must pass if Connect() was successful and set internal fields
            Assert.That(_wrapper.Connected, Is.True);
        }

        // ------------------------------------------------------------------
        // SCENARIO 2: DISCONNECTION (Disconnect Coverage)
        // ------------------------------------------------------------------

        [Test]
        public async Task Disconnect_WhenConnected_ShouldCloseResources()
        {
            // Arrange: 
            _wrapper.Connect();
            // Allow a small delay for the background listener task to start.
            await Task.Delay(50);
            _clientMock.Invocations.Clear(); // Clear Connect invocations

            // Act
            _wrapper.Disconnect();
            // Allow a small delay for the cancellation/closure logic to complete.
            await Task.Delay(50);

            // Assert: Verify all Close/Cancel were called
            _streamMock.Verify(s => s.Close(), Times.Once);
            _clientMock.Verify(c => c.Close(), Times.Once);

            // Verify that Connected is now false
            Assert.That(_wrapper.Connected, Is.False);
        }

        [Test]
        public void Disconnect_WhenNotConnected_ShouldDoNothing()
        {
            // Arrange: The wrapper starts disconnected

            // Act
            _wrapper.Disconnect();

            // Assert: Verify Close/Cancel methods were NOT called
            _streamMock.Verify(s => s.Close(), Times.Never);
            _clientMock.Verify(c => c.Close(), Times.Never);
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

            // Allow time for listener to start/stabilize 
            await Task.Delay(50);

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
            // Arrange: The wrapper starts in a disconnected state (Connected = false)

            // Act & Assert
            Assert.ThrowsAsync<InvalidOperationException>(
                () => _wrapper.SendMessageAsync(new byte[] { 0x01 }));
        }

        // ------------------------------------------------------------------
        // SCENARIO 5: LISTENING (Listening Coverage - Partial)
        // ------------------------------------------------------------------

        [Test]
        public async Task StartListeningAsync_WhenCancelled_ShouldStopListeningAndDisconnect()
        {
            // Arrange
            _wrapper.Connect();

            // Allow a small delay for the background listener task to start.
            await Task.Delay(50);

            // Simulate ReadAsync concluding with cancellation (OperationCanceledException)
            _streamMock
                .Setup(s => s.ReadAsync(
                    It.IsAny<byte[]>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());

            // Act: We explicitly call Disconnect to trigger cancellation/cleanup
            _wrapper.Disconnect();

            // Wait for the listening task to catch the cancellation and complete its finally block.
            await Task.Delay(100);

            // Assert: Verify that stream closure was called
            _streamMock.Verify(s => s.Close(), Times.AtLeastOnce);

            // Verify that client closure was called (part of Disconnect cleanup)
            _clientMock.Verify(c => c.Close(), Times.AtLeastOnce);
        }

        [Test]
        public async Task StartListeningAsync_WhenConnectionIsClosed_ShouldStopListeningAndDisconnect()
        {
            // Arrange
            _wrapper.Connect();

            // Allow a small delay for the background listener task to start.
            await Task.Delay(50);

            // FIX: Ensure that the mock returns 0 bytes read on the first attempt 
            // after the listener starts to properly simulate remote closure.
            _streamMock
                .SetupSequence(s => s.ReadAsync(
                    It.IsAny<byte[]>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                // Use ReturnsAsync(int) for the first call (EOF)
                .ReturnsAsync(0)
                // Subsequent calls must return a Task<int> via a function returning Task.
                // FIX CS0308: Remove type arguments from Returns
                .Returns((byte[] buffer, int offset, int size, CancellationToken token) =>
                {
                    var tcs = new TaskCompletionSource<int>();
                    token.Register(() => tcs.TrySetCanceled());
                    return tcs.Task;
                });

            // Act
            // We wait for the ReadAsync(0) to complete and trigger the Disconnect call in the finally block.
            await Task.Delay(100);

            // Assert: Verify that resource closure was called by the listener's finally block -> Disconnect()
            _clientMock.Verify(c => c.Close(), Times.AtLeastOnce);
            _streamMock.Verify(s => s.Close(), Times.AtLeastOnce);
            Assert.That(_wrapper.Connected, Is.False);
        }
    }
}