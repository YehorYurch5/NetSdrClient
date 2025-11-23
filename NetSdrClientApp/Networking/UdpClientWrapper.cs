using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace NetSdrClientApp.Networking
{
    // Оголошення інтерфейсів IHashAlgorithm та IUdpClient видалено,
    // оскільки вони знаходяться у файлі IUdpClient.cs і спричиняють помилки дублювання.

    // Replacement for weak MD5 with SHA256
    public class Sha256Adapter : IHashAlgorithm
    {
        private readonly HashAlgorithm _sha256 = SHA256.Create();

        public byte[] ComputeHash(byte[] buffer) => _sha256.ComputeHash(buffer);

        public void Dispose()
        {
            _sha256.Dispose();
        }
    }

    // --------------------------------------------------------------------------------

    // UdpClientWrapper implementation
    public class UdpClientWrapper : IUdpClient
    {
        private readonly IPEndPoint _localEndPoint;
        private readonly IHashAlgorithm _hashAlgorithm;

        private UdpClient? _udpClient;
        private CancellationTokenSource? _cts;
        private bool _disposed = false;

        public event EventHandler<byte[]>? MessageReceived;

        public UdpClientWrapper(int port)
            : this(port, new Sha256Adapter())
        {
        }

        public UdpClientWrapper(int port, IHashAlgorithm hashAlgorithm)
        {
            _localEndPoint = new IPEndPoint(IPAddress.Any, port);
            _hashAlgorithm = hashAlgorithm;
        }

        public async Task StartListeningAsync()
        {
            if (_cts != null && !_cts.IsCancellationRequested) return;
            if (_disposed) return;

            // Dispose previous CTS if present
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            Console.WriteLine("Start listening for UDP messages...");

            try
            {
                _udpClient = new UdpClient(_localEndPoint);

                CancellationToken token = _cts.Token;

                while (!token.IsCancellationRequested)
                {
                    UdpReceiveResult result = await _udpClient.ReceiveAsync(token);
                    MessageReceived?.Invoke(this, result.Buffer);

                    Console.WriteLine($"Received from {result.RemoteEndPoint}");
                }
            }
            catch (OperationCanceledException)
            {
                // Expected exception on cancellation
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving message: {ex.Message}");
            }
            finally
            {
                Cleanup("Listener stopped.");
            }
        }

        public void StopListening() => Cleanup("Stopped listening for UDP messages.");

        public void Exit() => Cleanup("Stopped listening for UDP messages.");

        private void Cleanup(string message)
        {
            if (_disposed) return;

            try
            {
                // 1. Cancel the token to stop the ReceiveAsync loop
                _cts?.Cancel();

                // 2. Close and dispose the UDP client
                if (_udpClient != null)
                {
                    _udpClient.Close();
                    _udpClient.Dispose();
                    _udpClient = null;
                }

                Console.WriteLine(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while stopping: {ex.Message}");
            }
        }

        // Use standard logic for GetHashCode.
        public override int GetHashCode()
        {
            // Generate hash code based on immutable fields (port and address)
            return HashCode.Combine(_localEndPoint.Address, _localEndPoint.Port);
        }

        // Implementation of IDisposable standard pattern
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Stop listener and clean up UdpClient
                    Cleanup("Disposing UdpClientWrapper.");

                    // Dispose IHashAlgorithm (SHA256) and CTS
                    _hashAlgorithm.Dispose();
                    _cts?.Dispose();
                }

                _disposed = true;
            }
        }
    }
}