using NUnit.Framework;
using Moq;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using NetSdrClientApp.Networking;
using System.Reflection;

namespace NetSdrClientAppTests.Networking
{
    [TestFixture]
    public class UdpClientWrapperTests
    {
        private Mock<IHashAlgorithm> _hashMock = null!;
        private UdpClientWrapper _wrapper = null!;
        private int _testPort; // Instance field for the port

        // Helper to get an available dynamic port to avoid conflicts
        private int GetAvailablePort()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                // Bind to 0, which tells OS to find a free port
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                return ((IPEndPoint)socket.LocalEndPoint!).Port;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _hashMock = new Mock<IHashAlgorithm>();
            _testPort = GetAvailablePort(); // Get a unique port for the test fixture
            _wrapper = new UdpClientWrapper(_testPort, _hashMock.Object);
        }

        [TearDown]
        public void TearDown()
        {
            _wrapper?.Dispose();
        }

        // Helper to access private fields for testing internal state
        private T? GetPrivateField<T>(string fieldName) where T : class
        {
            var field = typeof(UdpClientWrapper).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            return (T?)field?.GetValue(_wrapper);
        }

        private void SetPrivateField(string fieldName, object? value)
        {
            var field = typeof(UdpClientWrapper).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(_wrapper, value);
        }


        // ------------------------------------------------------------------
        // TEST 1: CONSTRUCTOR 
        // ------------------------------------------------------------------
        [Test]
        public void Constructor_ShouldInitializeCorrectly()
        {
            Assert.That(_wrapper, Is.Not.Null);
        }

        // ------------------------------------------------------------------
        // TEST 2: GET HASH CODE (FIXED: Reliable hash check)
        // ------------------------------------------------------------------
        [Test]
        public void GetHashCode_ShouldReturnConsistentHash()
        {
            // Arrange
            // Using a new, temporary wrapper for comparison
            var wrapper2 = new UdpClientWrapper(GetPrivateField<IPEndPoint>("_localEndPoint")!.Port, _hashMock.Object);

            // Act
            int hashCode1 = _wrapper.GetHashCode();
            int hashCode2 = wrapper2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
            Assert.That(hashCode1, Is.Not.EqualTo(0), "Hash code should not be default 0.");
        }

        // ------------------------------------------------------------------
        // TEST 3: STOP LISTENING/CLEANUP LOGIC (FIXED: Avoids Moq limitations and SocketException)
        // ------------------------------------------------------------------

        [Test]
        public void StopListening_ShouldCancelTokenAndCleanupUdpClient()
        {
            // Arrange: Simulate StartListeningAsync having run successfully
            var cts = new CancellationTokenSource();

            // We must create a real UdpClient to ensure Cleanup can call .Close() and .Dispose() without NRE, 
            // but we use a *different* port than the wrapper's main port.
            var tempClient = new UdpClient(GetAvailablePort());

            SetPrivateField("_cts", cts);
            SetPrivateField("_udpClient", tempClient);

            // Act
            _wrapper.StopListening();

            // Assert
            // 1. Verify that the cancellation was requested
            Assert.That(cts.IsCancellationRequested, Is.True, "Cancellation token should be cancelled.");

            // 2. Verify that UdpClient field is nullified after Cleanup.
            Assert.That(GetPrivateField<UdpClient>("_udpClient"), Is.Null, "Internal UdpClient should be nullified after Cleanup.");
        }

        [Test]
        public void Exit_ShouldCancelTokenAndCleanupUdpClient()
        {
            // Arrange: Simulate running state
            var cts = new CancellationTokenSource();
            var tempClient = new UdpClient(GetAvailablePort());

            SetPrivateField("_cts", cts);
            SetPrivateField("_udpClient", tempClient);

            // Act
            _wrapper.Exit();

            // Assert
            Assert.That(cts.IsCancellationRequested, Is.True);
            Assert.That(GetPrivateField<UdpClient>("_udpClient"), Is.Null, "Internal UdpClient should be nullified after Exit/Cleanup.");
        }

        // ------------------------------------------------------------------
        // TEST 4: DISPOSE (Coverage: Dispose(bool), IDisposable implementation)
        // ------------------------------------------------------------------

        [Test]
        public void Dispose_ShouldCallCleanupDisposeHashAndMarkAsDisposed()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            SetPrivateField("_cts", cts);

            // Act
            _wrapper.Dispose();

            // Assert
            // 1. Verify HashAlgorithm dispose
            _hashMock.Verify(h => h.Dispose(), Times.Once, "IHashAlgorithm should be disposed.");

            // 2. Verify cancellation
            Assert.That(cts.IsCancellationRequested, Is.True, "Dispose should call Cleanup, which cancels CTS.");

            // 3. Verify idempotency
            _wrapper.Dispose();
            _hashMock.Verify(h => h.Dispose(), Times.Once, "Dispose should be idempotent.");
        }

        // ------------------------------------------------------------------
        // TEST 5: START LISTENING (Asynchronous logic coverage - NEW TESTS)
        // ------------------------------------------------------------------

        [Test]
        public async Task StartListeningAsync_ShouldReceiveDataAndRaiseEvent()
        {
            // Arrange
            byte[] expectedData = Encoding.ASCII.GetBytes("TestPacket");
            byte[] receivedData = Array.Empty<byte>();
            var receivedTcs = new TaskCompletionSource<bool>();

            _wrapper.MessageReceived += (sender, data) =>
            {
                receivedData = data;
                receivedTcs.SetResult(true);
            };

            var listeningTask = _wrapper.StartListeningAsync();

            // Allow a small delay for UdpClient to initialize and start listening
            await Task.Delay(100);

            // Act: Send data using a separate client
            using (var sender = new UdpClient())
            {
                var targetEndpoint = new IPEndPoint(IPAddress.Loopback, _testPort);
                await sender.SendAsync(expectedData, targetEndpoint);
            }

            // Assert: Wait for the event to be raised (or timeout after 1 second)
            Assert.That(await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(1)), Is.True, "MessageReceived event was not raised.");
            Assert.That(receivedData, Is.EqualTo(expectedData), "Received data does not match expected data.");
        }

        [Test]
        public async Task StartListeningAsync_ShouldStopListeningOnCancellation()
        {
            // Arrange
            var listeningTask = _wrapper.StartListeningAsync();

            // Allow a small delay for UdpClient to initialize
            await Task.Delay(100);

            // Act: Stop listening which cancels the CTS and breaks the ReceiveAsync loop
            _wrapper.StopListening();

            // Assert
            // 1. Task should complete within a short time (OperationCanceledException is expected internally)
            Assert.That(async () => await listeningTask.WaitAsync(TimeSpan.FromSeconds(1)), Throws.Nothing,
                "Listening task should complete gracefully (no exceptions thrown outside) on StopListening.");

            // 2. Verify that UdpClient is null after cleanup
            Assert.That(GetPrivateField<UdpClient>("_udpClient"), Is.Null, "UdpClient should be nullified after cancellation.");
        }

        [Test]
        public async Task StartListeningAsync_ShouldHandleStartWhenAlreadyRunning()
        {
            // Arrange: Setup private CTS to simulate "already running"
            var cts = new CancellationTokenSource();
            SetPrivateField("_cts", cts);

            // Act
            var task = _wrapper.StartListeningAsync(); // Should exit early because CTS is not cancelled

            // Assert: The task should complete instantly (or near-instantly)
            Assert.That(cts.IsCancellationRequested, Is.False, "Existing CTS should not be cancelled if StartListening exits early.");

            // Clean up for TearDown
            cts.Dispose();
        }
    }
}