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
    // Інтерфейс для абстрагування алгоритму хешування
    public interface IHashAlgorithm : IDisposable
    {
        byte[] ComputeHash(byte[] buffer);
    }

    // FIX 1: Заміна слабкого MD5 на SHA256
    // FIX 3: Перейменування класу відповідно до нового алгоритму
    public class Sha256Adapter : IHashAlgorithm
    {
        // Використовуємо SHA256 замість MD5
        private readonly HashAlgorithm _sha256 = SHA256.Create();

        public byte[] ComputeHash(byte[] buffer) => _sha256.ComputeHash(buffer);

        public void Dispose()
        {
            _sha256.Dispose();
        }
    }

    // --------------------------------------------------------------------------------

    public interface IUdpClient : IDisposable // Додаємо IDisposable, якщо він відсутній
    {
        Task StartListeningAsync();
        void StopListening();
        void Exit();
        event EventHandler<byte[]>? MessageReceived;
    }

    // FIX 3: Додано клас до простору імен
    // FIX 2: Реалізовано IDisposable для коректної утилізації ресурсів
    public class UdpClientWrapper : IUdpClient, IDisposable
    {
        private readonly IPEndPoint _localEndPoint;
        private readonly IHashAlgorithm _hashAlgorithm;

        private UdpClient? _udpClient;
        private CancellationTokenSource? _cts;
        private bool _disposed = false;

        public event EventHandler<byte[]>? MessageReceived;

        public UdpClientWrapper(int port)
            // FIX: Змінено конструктор за замовчуванням на використання SHA256
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
            // FIX: Перевіряємо, чи вже запущено або утилізовано
            if (_cts != null && !_cts.IsCancellationRequested) return;
            if (_disposed) return;

            // Замінюємо старий CTS, якщо він був
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            Console.WriteLine("Start listening for UDP messages...");

            try
            {
                // Створюємо клієнт тут, щоб його можна було закрити в Cleanup
                _udpClient = new UdpClient(_localEndPoint);

                // Використовуємо CancellationToken у циклі
                CancellationToken token = _cts.Token;

                while (!token.IsCancellationRequested)
                {
                    // FIX: Використовуємо ReceiveAsync з токеном
                    UdpReceiveResult result = await _udpClient.ReceiveAsync(token);
                    MessageReceived?.Invoke(this, result.Buffer);

                    Console.WriteLine($"Received from {result.RemoteEndPoint}");
                }
            }
            catch (OperationCanceledException)
            {
                // Очікуваний виняток при скасуванні
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
                // 1. Скасовуємо токен, щоб зупинити цикл ReceiveAsync
                _cts?.Cancel();

                // 2. Закриваємо UDP клієнт
                if (_udpClient != null)
                {
                    _udpClient.Close();
                    // Додатково утилізуємо, якщо можливо (UdpClient реалізує IDisposable)
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

        // FIX 4: Видалення криптографічного хешування з GetHashCode
        // Використовуємо стандартну логіку для GetHashCode.
        public override int GetHashCode()
        {
            // Генеруємо хеш-код на основі незмінних полів (порт і адреса)
            return HashCode.Combine(_localEndPoint.Address, _localEndPoint.Port);
        }

        // FIX 2: Реалізація IDisposable за стандартним шаблоном
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
                    // Зупиняємо слухача та очищуємо UdpClient
                    Cleanup("Disposing UdpClientWrapper.");

                    // Утилізуємо IHashAlgorithm (MD5/SHA256) та CTS
                    _hashAlgorithm.Dispose();
                    _cts?.Dispose();
                }

                _disposed = true;
            }
        }
    }
}