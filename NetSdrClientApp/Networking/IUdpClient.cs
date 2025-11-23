using System;
using System.Threading.Tasks;

namespace NetSdrClientApp.Networking
{
    // Додано IDisposable для коректної роботи з using та Dispose
    public interface IUdpClient : IDisposable
    {
        event EventHandler<byte[]>? MessageReceived;

        Task StartListeningAsync();

        void StopListening();
        void Exit();
    }
}