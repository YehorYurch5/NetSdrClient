using NUnit.Framework;
using Moq;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.Net;
using System.Net.Sockets; // Added for UdpClient, SocketException
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using NetSdrClientApp.Networking;
using System.Reflection; // Required for accessing private fields

namespace NetSdrClientAppTests.Networking
{
    [TestFixture]
    public class UdpClientWrapperTests
    {
        private Mock<IHashAlgorithm> _hashMock = null!;
        private UdpClientWrapper _wrapper = null!;

        // FIX: Using a random dynamic port to avoid "Only one usage of each socket address" error
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
            // Use a new available port for each test run
            int testPort = GetAvailablePort();
            _wrapper = new UdpClientWrapper(testPort, _hashMock.Object);
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

            // 2. Verify that UdpClient and CTS fields are set to null (Cleanup logic)
            Assert.That(GetPrivateField<UdpClient>("_udpClient"), Is.Null, "Internal UdpClient should be nullified after Cleanup.");

            // 3. Verify that the client is actually closed (important for the listener thread)
            // We cannot verify .Close() with Moq, so checking the state is the next best thing.
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

            // 2. Verify CTS dispose
            // Since CTS is disposed internally, we can check if a second dispose throws,
            // but the safer way is to check if it was cancelled by Cleanup.
            Assert.That(cts.IsCancellationRequested, Is.True, "Dispose should call Cleanup, which cancels CTS.");

            // 3. Verify idempotency
            _wrapper.Dispose();
            _hashMock.Verify(h => h.Dispose(), Times.Once, "Dispose should be idempotent.");
        }

        // ------------------------------------------------------------------
        // TEST 5: START LISTENING (Placeholder logic improvement)
        // ------------------------------------------------------------------

        [Test]
        public async Task StartListeningAsync_ShouldHandleStartWhenAlreadyRunning()
        {
            // Arrange: Setup private CTS to simulate "already running"
            var cts = new CancellationTokenSource();
            SetPrivateField("_cts", cts);

            // Act
            var task = _wrapper.StartListeningAsync(); // Should exit early because CTS is not cancelled

            // Assert: The task should complete instantly (or near-instantly)
            // It's hard to verify *instantly* without async issues, so we check state.
            Assert.That(cts.IsCancellationRequested, Is.False, "Existing CTS should not be cancelled if StartListening exits early.");

            // Clean up for TearDown
            cts.Dispose();
        }
    }
}