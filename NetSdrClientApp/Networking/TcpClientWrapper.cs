using System;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetSdrClientApp.Networking
{
    // Adapter for the real TcpClient
    public class SystemTcpClientAdapter : ISystemTcpClient
    {
        private readonly TcpClient _client;

        public SystemTcpClientAdapter(TcpClient client) => _client = client;
        public bool Connected => _client.Connected;
        public void Close() => _client.Close();
        public void Connect(string host, int port) => _client.Connect(host, port);
        public void Dispose() => _client.Dispose();

        // Return adapter for NetworkStream
        public INetworkStream GetStream() => new NetworkStreamAdapter(_client.GetStream());
    }

    // Adapter for NetworkStream
    public class NetworkStreamAdapter : INetworkStream
    {
        private readonly NetworkStream _stream;

        public NetworkStreamAdapter(NetworkStream stream) => _stream = stream;

        public bool CanRead => _stream.CanRead;
        public bool CanWrite => _stream.CanWrite;
        public void Close() => _stream.Close();
        public void Dispose() => _stream.Dispose();

        public Task<int> ReadAsync(byte[] buffer, int offset, int size, CancellationToken cancellationToken)
            => _stream.ReadAsync(buffer, offset, size, cancellationToken);

        public Task WriteAsync(byte[] buffer, int offset, int size, CancellationToken cancellationToken)
            => _stream.WriteAsync(buffer, offset, size, cancellationToken);
    }

    // -------------------------------------------------------------

    // Main wrapper class
    public class TcpClientWrapper : ITcpClient
    {
        private readonly string _host;
        private readonly int _port;

        private ISystemTcpClient? _tcpClient;
        private INetworkStream? _stream;
        private CancellationTokenSource _cts;

        public bool Connected => _tcpClient != null && _tcpClient.Connected && _stream != null;

        public event EventHandler<byte[]>? MessageReceived;

        // Factory for creating actual clients
        private readonly Func<ISystemTcpClient> _clientFactory;

        // Constructor for Production code
        public TcpClientWrapper(string host, int port)
            : this(host, port, () => new SystemTcpClientAdapter(new TcpClient())) { }

        // Constructor for DI and testing
        public TcpClientWrapper(string host, int port, Func<ISystemTcpClient> clientFactory)
        {
            _host = host;
            _port = port;
            _clientFactory = clientFactory;
            _cts = new CancellationTokenSource();
        }

        public void Connect()
        {
            if (Connected)
            {
                Console.WriteLine($"Already connected to {_host}:{_port}");
                return;
            }

            _tcpClient = _clientFactory();

            try
            {
                // Create a new CTS only if needed
                if (_cts == null || _cts.IsCancellationRequested)
                {
                    _cts?.Dispose();
                    _cts = new CancellationTokenSource();
                }

                _tcpClient.Connect(_host, _port);
                _stream = _tcpClient.GetStream();
                Console.WriteLine($"Connected to {_host}:{_port}");
                _ = StartListeningAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect: {ex.Message}");
                // Ensure resources are nullified on failure
                _tcpClient = null;
                _stream = null;
            }
        }

        public void Disconnect()
        {
            if (Connected)
            {
                _cts?.Cancel();
                _stream?.Close();
                _tcpClient?.Close();

                // Dispose and reset CTS to a known state
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                _tcpClient = null;
                _stream = null;
                Console.WriteLine("Disconnected.");
            }
            else
            {
                Console.WriteLine("No active connection to disconnect.");
            }
        }

        public async Task SendMessageAsync(byte[] data)
        {
            if (Connected && _stream != null && _stream.CanWrite)
            {
                Console.WriteLine($"Message sent: " + data.Select(b => Convert.ToString(b, toBase: 16)).Aggregate((l, r) => $"{l} {r}"));
                await _stream.WriteAsync(data, 0, data.Length, _cts.Token);
            }
            else
            {
                throw new InvalidOperationException("Not connected to a server.");
            }
        }

        public async Task SendMessageAsync(string str)
        {
            var data = Encoding.UTF8.GetBytes(str);
            if (Connected && _stream != null && _stream.CanWrite)
            {
                Console.WriteLine($"Message sent: " + data.Select(b => Convert.ToString(b, toBase: 16)).Aggregate((l, r) => $"{l} {r}"));
                await _stream.WriteAsync(data, 0, data.Length, _cts.Token);
            }
            else
            {
                throw new InvalidOperationException("Not connected to a server.");
            }
        }

        private async Task StartListeningAsync()
        {
            var token = _cts.Token;

            if (Connected && _stream != null && _stream.CanRead)
            {
                try
                {
                    Console.WriteLine($"Starting listening for incomming messages.");

                    while (!token.IsCancellationRequested)
                    {
                        byte[] buffer = new byte[8194];

                        int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, token);

                        if (bytesRead == 0)
                        {
                            break; // Connection closed by remote host
                        }

                        if (bytesRead > 0)
                        {
                            MessageReceived?.Invoke(this, buffer.AsSpan(0, bytesRead).ToArray());
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Cancellation requested
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in listening loop: {ex.Message}");
                }
                finally
                {
                    Console.WriteLine("Listener stopped.");
                    // Disconnect is called here, which closes resources and cleans up state.
                    Disconnect();
                }
            }
            else
            {
                // Internal method, no action needed if not connected
            }
        }
    }
}