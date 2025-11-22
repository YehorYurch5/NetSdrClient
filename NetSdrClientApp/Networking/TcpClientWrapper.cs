using System;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetSdrClientApp.Networking
{
    public interface INetworkStream : IDisposable
    {
        bool CanRead { get; }
        bool CanWrite { get; }
        void Close();
        Task<int> ReadAsync(byte[] buffer, int offset, int size, CancellationToken cancellationToken);
        Task WriteAsync(byte[] buffer, int offset, int size, CancellationToken cancellationToken);
    }

    public interface ITcpClient
    {
        bool Connected { get; }
        event EventHandler<byte[]>? MessageReceived;
        void Connect();
        void Disconnect();
        Task SendMessageAsync(byte[] data);
        Task SendMessageAsync(string str);
    }

    public interface ISystemTcpClient : IDisposable
    {
        bool Connected { get; }
        INetworkStream GetStream();
        void Connect(string host, int port);
        void Close();
    }

    public class SystemTcpClientAdapter : ISystemTcpClient
    {
        private readonly TcpClient _client;

        public SystemTcpClientAdapter(TcpClient client) => _client = client;
        public bool Connected => _client.Connected;
        public void Close() => _client.Close();
        public void Connect(string host, int port) => _client.Connect(host, port);
        public void Dispose() => _client.Dispose();

        public INetworkStream GetStream() => new NetworkStreamAdapter(_client.GetStream());
    }

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

    public class TcpClientWrapper : ITcpClient
    {
        private readonly string _host;
        private readonly int _port;
        private ISystemTcpClient? _tcpClient;
        private INetworkStream? _stream;
        private CancellationTokenSource? _cts;

        public bool Connected => _tcpClient != null && _tcpClient.Connected && _stream != null;

        public event EventHandler<byte[]>? MessageReceived;

        private readonly Func<ISystemTcpClient> _clientFactory;

        public TcpClientWrapper(string host, int port)
            : this(host, port, () => new SystemTcpClientAdapter(new TcpClient())) { }

        public TcpClientWrapper(string host, int port, Func<ISystemTcpClient> clientFactory)
        {
            _host = host;
            _port = port;
            _clientFactory = clientFactory;
            // Removed CS8618 fix by making _cts nullable
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
                _cts = new CancellationTokenSource();
                _tcpClient.Connect(_host, _port);
                _stream = _tcpClient.GetStream();
                Console.WriteLine($"Connected to {_host}:{_port}");
                _ = StartListeningAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            if (Connected)
            {
                _cts?.Cancel();
                _stream?.Close();
                _tcpClient?.Close();

                _cts = null;
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
            if (Connected && _stream != null && _stream.CanWrite && _cts != null)
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
            if (Connected && _stream != null && _stream.CanWrite && _cts != null)
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
            if (_cts == null)
            {
                throw new InvalidOperationException("Cancellation token source is not initialized.");
            }
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
                            break;
                        }

                        if (bytesRead > 0)
                        {
                            MessageReceived?.Invoke(this, buffer.AsSpan(0, bytesRead).ToArray());
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    //empty
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in listening loop: {ex.Message}");
                }
                finally
                {
                    Console.WriteLine("Listener stopped.");
                    Disconnect();
                }
            }
            else
            {
                throw new InvalidOperationException("Not connected to a server.");
            }
        }
    }
}