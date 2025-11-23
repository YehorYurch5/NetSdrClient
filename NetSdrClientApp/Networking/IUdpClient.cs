using System;
using System.Threading.Tasks;

namespace NetSdrClientApp.Networking
{
    // Interface for hash algorithm abstraction (used by Sha256Adapter)
    public interface IHashAlgorithm : IDisposable
    {
        byte[] ComputeHash(byte[] buffer);
    }

    // Interface for the UDP client wrapper. Implements IDisposable.
    public interface IUdpClient : IDisposable
    {
        event EventHandler<byte[]>? MessageReceived;

        Task StartListeningAsync();

        void StopListening();
        void Exit();
    }
}