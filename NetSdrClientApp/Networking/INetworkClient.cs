using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System;

namespace NetSdrClientApp.Networking
{
    // 1. Інтерфейс для реального системного клієнта (для DI/Mocking)
    public interface ISystemTcpClient : IDisposable
    {
        bool Connected { get; }
        INetworkStream GetStream();
        void Connect(string host, int port);
        void Close();
    }

    // 2. Інтерфейс для мережевого потоку (для Mocking Read/Write)
    public interface INetworkStream : IDisposable
    {
        bool CanRead { get; }
        bool CanWrite { get; }
        // Методи, що використовуються в TcpClientWrapper
        Task WriteAsync(byte[] buffer, int offset, int size, CancellationToken cancellationToken);
        Task<int> ReadAsync(byte[] buffer, int offset, int size, CancellationToken cancellationToken);
        void Close();
    }

    // 3. Інтерфейс для TcpClientWrapper (загальна поведінка)
    public interface ITcpClient
    {
        void Connect();
        void Disconnect();
        Task SendMessageAsync(byte[] data);
        Task SendMessageAsync(string str);
        event EventHandler<byte[]> MessageReceived;
        public bool Connected { get; }
    }
}