using NetSdrClientApp.Networking;
using NUnit.Framework;
using Moq;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Text; // Added for string sending tests

namespace NetSdrClientAppTests.Networking
{
    [TestFixture]
    public class TcpClientWrapperTests
    {
        private Mock<ISystemTcpClient> _clientMock = null!;
        private Mock<INetworkStream> _streamMock = null!;
        private TcpClientWrapper _wrapper = null!;

        // TaskCompletionSource для контролю ReadAsync в більшості тестів
        private TaskCompletionSource<int> _readTcs = null!;

        [SetUp]
        public void SetUp()
        {
            _readTcs = new TaskCompletionSource<int>();
            _streamMock = new Mock<INetworkStream>();
            _clientMock = new Mock<ISystemTcpClient>();

            // Setup basic successful behavior
            _clientMock.Setup(c => c.GetStream()).Returns(_streamMock.Object);
            _clientMock.SetupGet(c => c.Connected).Returns(true);
            _streamMock.SetupGet(s => s.CanRead).Returns(true);
            _streamMock.SetupGet(s => s.CanWrite).Returns(true);

            // Default setup for ReadAsync: blocks indefinitely unless cancelled (via token) or set manually (via _readTcs)
            _streamMock
                .Setup(s => s.ReadAsync(
                    It.IsAny<byte[]>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns<byte[], int, int, CancellationToken>((buffer, offset, size, token) =>
                {
                    // Реєструємо токен скасування, щоб ReadAsync завершився, коли викликається Disconnect
                    token.Register(() => _readTcs.TrySetCanceled());
                    return _readTcs.Task;
                });

            // Factory returning our mock object for testing
            Func<ISystemTcpClient> factory = () => _clientMock.Object;

            // Initialize the wrapper for testing (using DI constructor)
            _wrapper = new TcpClientWrapper("127.0.0.1", 5000, factory);
        }

        // FIX: Додаємо TearDown для безпечного очищення
        [TearDown]
        public void TearDown()
        {
            // Утилізуємо TCS, щоб запобігти попередженням/витокам
            if (_readTcs.Task.Status == TaskStatus.Running)
            {
                _readTcs.TrySetCanceled();
            }
            // Це викликає Disconnect() у більшості випадків
            _wrapper.Disconnect();
        }


        // ------------------------------------------------------------------
        // SCENARIO 1: SUCCESSFUL CONNECTION
        // ------------------------------------------------------------------

        [Test]
        public void Connect_WhenNotConnected_ShouldConnectAndStartListening()
        {
            // Act
            _wrapper.Connect();

            // Assert
            _clientMock.Verify(c => c.Connect("127.0.0.1", 5000), Times.Once);
            _clientMock.Verify(c => c.GetStream(), Times.Once);
            Assert.That(_wrapper.Connected, Is.True);
        }

        // --- NEW TEST 1: Connect when already connected ---
        [Test]
        public void Connect_WhenAlreadyConnected_ShouldDoNothing()
        {
            // Arrange
            _wrapper.Connect();
            _clientMock.Invocations.Clear(); // Clear first connect invocation

            // Act
            _wrapper.Connect();

            // Assert
            // Verify Connect() was NOT called again
            _clientMock.Verify(c => c.Connect(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
            Assert.That(_wrapper.Connected, Is.True);
        }

        // ------------------------------------------------------------------
        // SCENARIO 2: DISCONNECTION
        // ------------------------------------------------------------------

        [Test]
        public async Task Disconnect_WhenConnected_ShouldCloseResources()
        {
            // Arrange: 
            _wrapper.Connect();
            await Task.Delay(50); // Allow listener task to start

            // Act
            _wrapper.Disconnect();
            await Task.Delay(50);

            // Assert: Verify all Close/Cancel were called
            _streamMock.Verify(s => s.Close(), Times.Once);
            _clientMock.Verify(c => c.Close(), Times.Once);
            Assert.That(_wrapper.Connected, Is.False);
        }

        [Test]
        public void Disconnect_WhenNotConnected_ShouldDoNothing()
        {
            // Act
            _wrapper.Disconnect();

            // Assert: Verify Close/Cancel methods were NOT called
            _streamMock.Verify(s => s.Close(), Times.Never);
            _clientMock.Verify(c => c.Close(), Times.Never);
        }

        // ------------------------------------------------------------------
        // SCENARIO 3: CONNECTION ERROR HANDLING
        // ------------------------------------------------------------------

        [Test]
        public void Connect_WhenFails_ShouldCatchExceptionAndCleanUp()
        {
            // Arrange: Set up mock so Connect throws an exception
            _clientMock.Setup(c => c.Connect(It.IsAny<string>(), It.IsAny<int>()))
                         .Throws(new SocketException(10061));

            // Act
            _wrapper.Connect();

            // Assert: Ensure Connected = false after error
            Assert.That(_wrapper.Connected, Is.False);
            // Verify that Close was NOT called (no resources to close yet)
            _clientMock.Verify(c => c.Close(), Times.Never);
        }

        // ------------------------------------------------------------------
        // SCENARIO 4: DATA SENDING 
        // ------------------------------------------------------------------

        [Test]
        public async Task SendMessageAsync_WhenConnected_ShouldWriteToStream()
        {
            // Arrange: Ensure Connect was successful
            _wrapper.Connect();
            await Task.Delay(50);
            byte[] testData = { 0x01, 0x02, 0x03 };

            // Act
            await _wrapper.SendMessageAsync(testData);

            // Assert
            _streamMock.Verify(s => s.WriteAsync(
                It.Is<byte[]>(arr => arr == testData), 0, testData.Length, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // --- NEW TEST 2: Send string message ---
        [Test]
        public async Task SendMessageAsyncString_WhenConnected_ShouldWriteConvertedBytes()
        {
            // Arrange
            _wrapper.Connect();
            await Task.Delay(50);
            string testString = "Hello";
            byte[] expectedData = Encoding.UTF8.GetBytes(testString);

            // Act
            await _wrapper.SendMessageAsync(testString);

            // Assert: Verify WriteAsync was called with the UTF8-encoded bytes
            _streamMock.Verify(s => s.WriteAsync(
                It.Is<byte[]>(arr => arr.SequenceEqual(expectedData)),
                0,
                expectedData.Length,
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public void SendMessageAsync_WhenNotConnected_ShouldThrowException()
        {
            // Act & Assert
            Assert.ThrowsAsync<InvalidOperationException>(
                () => _wrapper.SendMessageAsync(new byte[] { 0x01 }));
        }

        // ------------------------------------------------------------------
        // SCENARIO 5: LISTENING LOGIC (Advanced Coverage)
        // ------------------------------------------------------------------

        [Test]
        public async Task StartListeningAsync_WhenCancelled_ShouldStopListeningAndDisconnect()
        {
            // Arrange
            _wrapper.Connect();
            await Task.Delay(50);

            // Act: We explicitly call Disconnect, which sets up cancellation
            _wrapper.Disconnect();

            // Wait for the listening task to catch the cancellation and complete its finally block.
            await Task.Delay(100);

            // Assert
            _streamMock.Verify(s => s.Close(), Times.AtLeastOnce);
            _clientMock.Verify(c => c.Close(), Times.AtLeastOnce);
            Assert.That(_wrapper.Connected, Is.False);
        }

        // --- NEW TEST 3: Connection closed by remote host (bytesRead == 0) ---
        [Test]
        public async Task StartListeningAsync_WhenRemoteCloses_ShouldExitLoopAndDisconnect()
        {
            // Arrange: Setup ReadAsync to return 0 on the first call
            _streamMock.Setup(s => s.ReadAsync(
                It.IsAny<byte[]>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(0); // Simulates remote closure

            _wrapper.Connect();

            // Allow listening loop to run once and hit the ReadAsync=0 line
            await Task.Delay(100);

            // Assert: Should have called Disconnect internally
            _streamMock.Verify(s => s.Close(), Times.Once);
            _clientMock.Verify(c => c.Close(), Times.Once);
            Assert.That(_wrapper.Connected, Is.False);
        }

        // --- NEW TEST 4: Message received and event invoked (bytesRead > 0) ---
        [Test]
        public async Task StartListeningAsync_WhenDataReceived_ShouldRaiseEvent()
        {
            // Arrange
            byte[] testData = { 0xAA, 0xBB, 0xCC };
            byte[] receivedData = Array.Empty<byte>();

            // Setup ReadAsync to return data on the first call, then block indefinitely
            var callCount = 0;
            _streamMock.Setup(s => s.ReadAsync(
                It.IsAny<byte[]>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
                .Returns<byte[], int, int, CancellationToken>((buffer, offset, size, token) =>
                {
                    if (Interlocked.Increment(ref callCount) == 1)
                    {
                        // Simulate data reception
                        Array.Copy(testData, buffer, testData.Length);
                        return Task.FromResult(testData.Length);
                    }
                    // Block subsequent calls
                    token.Register(() => _readTcs.TrySetCanceled());
                    return _readTcs.Task;
                });

            // Subscribe to the event
            _wrapper.MessageReceived += (sender, data) => receivedData = data;

            _wrapper.Connect();

            // Wait long enough for the loop to execute the first ReadAsync call
            await Task.Delay(100);

            // Assert
            Assert.That(receivedData.SequenceEqual(testData), Is.True, "Received data should match the test data.");
            Assert.That(_wrapper.Connected, Is.True, "Connection should still be active.");

            // Cleanup: ensure the block is cancelled
            _wrapper.Disconnect();
        }

        // --- NEW TEST 5: General exception in listening loop ---
        [Test]
        public async Task StartListeningAsync_WhenGeneralExceptionOccurs_ShouldExitLoopAndDisconnect()
        {
            // Arrange: Setup ReadAsync to throw a generic exception
            _streamMock.Setup(s => s.ReadAsync(
                It.IsAny<byte[]>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Simulated stream error"));

            _wrapper.Connect();

            // Allow listening loop to run once and hit the exception handler
            await Task.Delay(100);

            // Assert: Should have called Disconnect internally
            _streamMock.Verify(s => s.Close(), Times.Once);
            _clientMock.Verify(c => c.Close(), Times.Once);
            Assert.That(_wrapper.Connected, Is.False);
        }
    }
}