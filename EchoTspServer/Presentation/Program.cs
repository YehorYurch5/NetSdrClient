using EchoTspServer.Application.Services;
using EchoTspServer.Infrastructure;
using System; // Додано для Console, ConsoleKey
using System.Threading.Tasks;

// ✅ ВИПРАВЛЕННЯ: Додано іменований простір імен, як вимагає SonarCloud (S3903)
namespace EchoTspServer.Presentation
{
    class Program
    {
        static async Task Main()
        {
            var logger = new ConsoleLogger();
            var handler = new ClientHandler(logger);

            // Note: Тут використовується 5000, logger, handler. 
            // Якщо у конструкторі EchoServer немає порту, його варто прибрати.
            // Я залишаю, як у вашому коді, припускаючи, що конструктор правильний.
            var server = new EchoServer(5000, logger, handler);

            // Запускаємо StartAsync у фоновому режимі, щоб не блокувати Main
            // Використовуємо _ = для ігнорування повернення Task, але уникнення попередження
            _ = Task.Run(() => server.StartAsync());

            var sender = new UdpTimedSender("127.0.0.1", 60000, logger);
            sender.StartSending(5000);

            Console.WriteLine("Press 'q' to quit...");

            // Цикл очікування команди на вихід
            while (Console.ReadKey(intercept: true).Key != ConsoleKey.Q) { }

            sender.StopSending();
            server.Stop();
        }
    }
}