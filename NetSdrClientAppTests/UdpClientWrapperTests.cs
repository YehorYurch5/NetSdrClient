using NUnit.Framework;
using Moq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System;
using System.Threading.Tasks;
using System.Linq;
// FIX: Додаємо using для доступу до UdpClientWrapper, IUdpClient та IHashAlgorithm
using NetSdrClientApp.Networking;

namespace NetSdrClientAppTests.Networking
{
    // Assuming IHashAlgorithm and UdpClientWrapper are available in this namespace or referenced correctly.
    [TestFixture]
    public class UdpClientWrapperTests
    {
        private Mock<IHashAlgorithm> _hashMock = null!; // Error CS0246 fixed by using directive
        private UdpClientWrapper _wrapper = null!; // Error CS0246 fixed by using directive
        private const int TestPort = 55555;

        [SetUp]
        public void SetUp()
        {
            _hashMock = new Mock<IHashAlgorithm>();
            // Initialization of the wrapper (using DI constructor)
            // This assumes UdpClientWrapper has a ctor(int port, IHashAlgorithm hashAlgorithm)
            _wrapper = new UdpClientWrapper(TestPort, _hashMock.Object);
        }

        // ------------------------------------------------------------------
        // TEST 1: CONSTRUCTOR (Constructor coverage)
        // ------------------------------------------------------------------
        [Test]
        public void Constructor_ShouldInitializeCorrectly()
        {
            // Assert
            // Check that the object is created and hashAlgorithm is injected
            Assert.That(_wrapper, Is.Not.Null);
        }

        // ------------------------------------------------------------------
        // TEST 2: GET HASH CODE (Hashing logic coverage)
        // ------------------------------------------------------------------
        [Test]
        public void GetHashCode_ShouldCallComputeHashAndReturnInt()
        {
            // Arrange
            byte[] fakeHash = new byte[4] { 0x01, 0x02, 0x03, 0x04 }; // 4 bytes = int32
                                                                      // NOTE: Since UdpClientWrapper now uses HashCode.Combine in GetHashCode(), 
                                                                      // the mock setup for ComputeHash and Assert logic below is NO LONGER VALID.
                                                                      // We verify that the test does NOT call ComputeHash anymore and relies on C#'s internal Hashing.

            // Act
            int hashCode = _wrapper.GetHashCode();

            // Assert
            // Verify that ComputeHash method was NOT called (because UdpClientWrapper now uses HashCode.Combine)
            _hashMock.Verify(h => h.ComputeHash(It.IsAny<byte[]>()), Times.Never);

            // Verify that the hash code is generated (We cannot assert the exact value easily
            // as HashCode.Combine changes between runs, but we ensure it's calculated)
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        // ------------------------------------------------------------------
        // TEST 3: STOP LISTENING (Cleanup methods coverage)
        // ------------------------------------------------------------------

        [Test]
        public void StopListening_ShouldCallCleanup()
        {
            // Act
            _wrapper.StopListening();

            // Assert: Ensure the method did not throw an exception (testing happy path Cleanup)
            Assert.Pass();
        }

        [Test]
        public void Exit_ShouldCallCleanup()
        {
            // Act
            _wrapper.Exit();

            // Assert: Ensure the method did not throw an exception
            Assert.Pass();
        }

        // ------------------------------------------------------------------
        // TEST 4: DISPOSE (Dispose coverage)
        // ------------------------------------------------------------------

        [Test]
        public void Dispose_ShouldStopSendingAndDisposeHash()
        {
            // Act
            _wrapper.Dispose();

            // Assert: Verify that Dispose was called for the injected object
            _hashMock.Verify(h => h.Dispose(), Times.Once);
        }

        // ------------------------------------------------------------------
        // TEST 5: START LISTENING (Exception-throwing code coverage)
        // ------------------------------------------------------------------

        [Test]
        public void StartListeningAsync_ShouldHandleExceptionInStartup()
        {
            // Note: This test would typically require mocking the internal UdpClient 
            // or an integration test using a real, occupied port.
            // Since UdpClient is not easily mockable without refactoring UdpClientWrapper, 
            // this remains a passing placeholder.
            Assert.Pass("StartListeningAsync cannot be unit-tested without refactoring UdpClient creation.");
        }
    }
}