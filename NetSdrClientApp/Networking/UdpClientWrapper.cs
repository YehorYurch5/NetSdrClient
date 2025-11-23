using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public interface IHashAlgorithm : IDisposable
{
    byte[] ComputeHash(byte[] buffer);
}

public class Md5Adapter : IHashAlgorithm
{
    private readonly MD5 _md5 = MD5.Create();
    public byte[] ComputeHash(byte[] buffer) => _md5.ComputeHash(buffer);
    public void Dispose() => _md5.Dispose();
}

// --------------------------------------------------------------------------------

public class UdpClientWrapper : IUdpClient
{
    private readonly IPEndPoint _localEndPoint;
    private readonly IHashAlgorithm _hashAlgorithm;

    private UdpClient? _udpClient;

    private CancellationTokenSource? _cts;

    public event EventHandler<byte[]>? MessageReceived;

    public UdpClientWrapper(int port)
        : this(port, new Md5Adapter())
    {
    }

    public UdpClientWrapper(int port, IHashAlgorithm hashAlgorithm)
    {
        _localEndPoint = new IPEndPoint(IPAddress.Any, port);
        _hashAlgorithm = hashAlgorithm;
    }

    public async Task StartListeningAsync()
    {
        _cts = new CancellationTokenSource();
        Console.WriteLine("Start listening for UDP messages...");

        try
        {
            _udpClient = new UdpClient(_localEndPoint);
            while (!_cts.Token.IsCancellationRequested)
            {
                UdpReceiveResult result = await _udpClient.ReceiveAsync(_cts.Token);
                MessageReceived?.Invoke(this, result.Buffer);

                Console.WriteLine($"Received from {result.RemoteEndPoint}");
            }
        }
        catch (OperationCanceledException)
        {
            //empty
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error receiving message: {ex.Message}");
        }
    }

    public void StopListening() => Cleanup("Stopped listening for UDP messages.");

    public void Exit() => Cleanup("Stopped listening for UDP messages.");

    private void Cleanup(string message)
    {
        try
        {
            _cts?.Cancel();
            _udpClient?.Close();
            Console.WriteLine(message);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while stopping: {ex.Message}");
        }
    }

    public override int GetHashCode()
    {
        var payload = $"{nameof(UdpClientWrapper)}|{_localEndPoint.Address}|{_localEndPoint.Port}";

        var hash = _hashAlgorithm.ComputeHash(Encoding.UTF8.GetBytes(payload));

        return BitConverter.ToInt32(hash, 0);
    }

    public void Dispose()
    {
        Cleanup("Disposing UdpClientWrapper.");
        _hashAlgorithm.Dispose();
    }
}