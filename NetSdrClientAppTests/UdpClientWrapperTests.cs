using NUnit.Framework;
using Moq;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using NetSdrClientApp.Networking;
using System.Net.Sockets; // Required for UdpClient and related types
using System.Reflection; // Required for mocking internal UdpClient field

namespace NetSdrClientAppTests.Networking
{
    [TestFixture]
    public class UdpClientWrapperTests
    {
        private Mock<IHashAlgorithm> _hashMock = null!;
        // Mock of the internal UdpClient to test Cleanup/Dispose logic
        private Mock<UdpClient>? _udpClientMock;
        private UdpClientWrapper _wrapper = null!;
        private const int TestPort = 55555;

        [SetUp]
        public void SetUp()
        {
            _hashMock = new Mock<IHashAlgorithm>();
            _wrapper = new UdpClientWrapper(TestPort, _hashMock.Object);
            _udpClientMock = null; // Ensure it's null before tests that don't need it
        }

        [TearDown]
        public void TearDown()
        {
            _wrapper?.Dispose();
            // Dispose of the mock if it was created
            _udpClientMock?.VerifyAll();
        }


        // ------------------------------------------------------------------
        // TEST 1: CONSTRUCTOR (Coverage: Constructor)
        // ------------------------------------------------------------------
        [Test]
        public void Constructor_ShouldInitializeCorrectly()
        {
            // Assert
            Assert.That(_wrapper, Is.Not.Null);
        }

        // ------------------------------------------------------------------
        // TEST 2: GET HASH CODE (Coverage: GetHashCode method)
        // ------------------------------------------------------------------
        [Test]
        public void GetHashCode_ShouldReturnConsistentHash()
        {
            // Arrange
            var wrapper2 = new UdpClientWrapper(TestPort, _hashMock.Object); // Same port/address

            // Act
            int hashCode1 = _wrapper.GetHashCode();
            int hashCode2 = wrapper2.GetHashCode();

            // Assert
            // HashCode for IPEndPoint(IPAddress.Any, port) must be consistent
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
            Assert.That(hashCode1, Is.Not.EqualTo(0), "Hash code should not be default 0.");
        }

        // ------------------------------------------------------------------
        // TEST 3: STOP LISTENING/CLEANUP LOGIC (Coverage: StopListening, Exit, Cleanup)
        // ------------------------------------------------------------------

        [Test]
        public void StopListening_ShouldCancelTokenAndCloseUdpClient()
        {
            // Arrange: We need to set up the internal _udpClient and _cts fields manually for testing Cleanup logic
            _udpClientMock = new Mock<UdpClient>(SocketType.Dgram); // Mock UdpClient
            var cts = new CancellationTokenSource();

            // Use reflection to set private fields, as UdpClientWrapper is not designed for easy mocking.
            // WARNING: This is a hack due to UdpClientWrapper's structure.
            var wrapperType = typeof(UdpClientWrapper);
            wrapperType.GetField("_cts", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_wrapper, cts);
            wrapperType.GetField("_udpClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_wrapper, _udpClientMock.Object);

            // Set up expectations
            _udpClientMock.Setup(c => c.Close()).Verifiable();
            _udpClientMock.Setup(c => c.Dispose()).Verifiable();

            // Act
            _wrapper.StopListening();

            // Assert
            // 1. Verify that the cancellation was requested
            Assert.That(cts.IsCancellationRequested, Is.True, "Cancellation token should be cancelled.");

            // 2. Verify that the UdpClient was closed and disposed
            _udpClientMock.Verify(c => c.Close(), Times.Once);
            _udpClientMock.Verify(c => c.Dispose(), Times.Once);
        }

        [Test]
        public void Exit_ShouldCancelTokenAndCloseUdpClient()
        {
            // This test reuses the same logic as StopListening, ensuring 'Exit' calls 'Cleanup'.
            // Arrange
            _udpClientMock = new Mock<UdpClient>(SocketType.Dgram);
            var cts = new CancellationTokenSource();

            var wrapperType = typeof(UdpClientWrapper);
            wrapperType.GetField("_cts", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_wrapper, cts);
            wrapperType.GetField("_udpClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_wrapper, _udpClientMock.Object);

            _udpClientMock.Setup(c => c.Close()).Verifiable();
            _udpClientMock.Setup(c => c.Dispose()).Verifiable();

            // Act
            _wrapper.Exit();

            // Assert
            Assert.That(cts.IsCancellationRequested, Is.True);
            _udpClientMock.Verify(c => c.Close(), Times.Once);
            _udpClientMock.Verify(c => c.Dispose(), Times.Once);
        }

        // ------------------------------------------------------------------
        // TEST 4: DISPOSE (Coverage: Dispose(bool), IDisposable implementation)
        // ------------------------------------------------------------------

        [Test]
        public void Dispose_ShouldStopSendingDisposeHashAndMarkAsDisposed()
        {
            // Arrange
            // We need to set up internal _cts field for cleanup during dispose
            var cts = new CancellationTokenSource();
            typeof(UdpClientWrapper).GetField("_cts", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_wrapper, cts);

            // Act
            _wrapper.Dispose();

            // Assert: Verify that Dispose was called for the injected object
            _hashMock.Verify(h => h.Dispose(), Times.Once, "IHashAlgorithm should be disposed.");

            // Verify that CTS was disposed
            Assert.That(cts.IsCancellationRequested, Is.True, "Dispose should call Cleanup, which cancels CTS.");

            // Try disposing again (should do nothing)
            _wrapper.Dispose();
            _hashMock.Verify(h => h.Dispose(), Times.Once, "Dispose should be idempotent.");
        }

        // ------------------------------------------------------------------
        // TEST 5: START LISTENING (Placeholder logic improvement)
        // ------------------------------------------------------------------

        [Test]
        public void StartListeningAsync_ShouldHandleStartWhenAlreadyRunning()
        {
            // Arrange: Setup private CTS to simulate "already running"
            var cts = new CancellationTokenSource();
            typeof(UdpClientWrapper).GetField("_cts", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_wrapper, cts);

            // Act
            var task = _wrapper.StartListeningAsync(); // Should exit early because CTS is not cancelled

            // Assert: The task should complete instantly or not throw, and the existing CTS should remain un-disposed
            Assert.That(task.IsCompleted, Is.True);
            Assert.That(cts.IsCancellationRequested, Is.False);

            // Clean up for TearDown
            cts.Dispose();
        }
    }
}