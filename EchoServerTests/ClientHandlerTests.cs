using System.Net.Sockets;
using System.Text;
using EchoTspServer.Application.Interfaces;
using EchoTspServer.Application.Services;
using Moq;
using NUnit.Framework;
using System.Net; // Додано для TcpListener
using System.Threading; // Додано для CancellationTokenSource

namespace EchoTspServer.Tests
{
    [TestFixture]
    public class ClientHandlerTests
    {
        private Mock<ILogger> _loggerMock;
        private ClientHandler _handler;

        [SetUp]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger>();
            _handler = new ClientHandler(_loggerMock.Object);
        }

        [Test]
        public async Task HandleClientAsync_EchoesDataBack()
        {
            // Arrange: створимо два з'єднані TCP сокети
            using var listener = new TcpListener(IPAddress.Loopback, 0); // Використано IPAddress.Loopback
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var clientTask = new TcpClient();
            var connectTask = clientTask.ConnectAsync("127.0.0.1", port);

            var serverClient = await listener.AcceptTcpClientAsync();
            await connectTask;
            listener.Stop();

            // ✅ Виправлення: Створюємо без тайм-ауту для контрольованого скасування
            using var cts = new CancellationTokenSource();
            var token = cts.Token;

            // 🎯 ВИПРАВЛЕННЯ: Оголошення stream та buffer в області видимості методу
            var stream = clientTask.GetStream();
            byte[] buffer = new byte[1024];

            // Act
            var handleTask = _handler.HandleClientAsync(serverClient, token);

            var message = Encoding.UTF8.GetBytes("ping");

            // Виклик WriteAsync з ReadOnlyMemory<byte> та CancellationToken (попередня фіксація)
            await stream.WriteAsync(message.AsMemory(), token);

            // Використовуємо ReadAsync з CancellationToken
            int bytesRead = await stream.ReadAsync(buffer, token);

            // 🎯 НОВИЙ КРОК: Скасовуємо токен після успішного обміну, щоб завершити handleTask
            cts.Cancel();

            // Assert
            Assert.That(Encoding.UTF8.GetString(buffer, 0, bytesRead), Is.EqualTo("ping"));
            _loggerMock.Verify(l => l.Info(It.Is<string>(s => s.Contains("Echoed"))), Times.AtLeastOnce);

            // Очікуємо завершення handleTask та обробляємо очікуваний OperationCanceledException
            try
            {
                await handleTask;
            }
            catch (OperationCanceledException)
            {
                // Очікувана поведінка, оскільки ми викликали cts.Cancel()
            }

            serverClient.Close();
            clientTask.Close();
        }


        //[Test]
        //public async Task HandleClientAsync_HandlesException_LogsError()
        //{
        //    // Arrange
        //    var fakeClient = new Mock<TcpClient>();
        //    fakeClient.Setup(c => c.GetStream()).Throws(new Exception("fake fail"));

        //    // Act
        //    await _handler.HandleClientAsync(fakeClient.Object, CancellationToken.None);

        //    // Assert
        //    _loggerMock.Verify(l => l.Error(It.Is<string>(s => s.Contains("fake fail"))), Times.Once);
        //}
    }
}