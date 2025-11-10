using NetArchTest.Rules;
using NUnit.Framework;
using System.Linq;
using System.Reflection;
using System.Text;
using System; // Додано для Array.Empty

namespace NetSdrClientAppTests
{
    // Клас для тестування архітектурних правил
    [TestFixture]
    public class ArchitectureTests
    {
        // ВИПРАВЛЕННЯ CS0116: Поле ClientAssembly оголошено всередині класу і є статичним.
        // Це дозволяє отримати збірку (Assembly) один раз для всіх тестів.
        private static readonly Assembly ClientAssembly = typeof(NetSdrClientApp.NetSdrClient).Assembly;

        [Test]
        public void App_Should_Not_Depend_On_EchoServer()
        {
            var result = Types.InAssembly(ClientAssembly)
                .That()
                .ResideInNamespace("NetSdrClientApp")
                .ShouldNot()
                .HaveDependencyOn("EchoServer")
                .GetResult();

            Assert.That(result.IsSuccessful, Is.True);
        }

        [Test]
        public void Messages_Should_Not_Depend_On_Networking()
        {
            // Перевіряємо, що шар Messages не залежить від шару Networking (низькорівнева залежність)
            var result = Types.InAssembly(ClientAssembly)
                .That()
                .ResideInNamespace("NetSdrClientApp.Messages")
                .ShouldNot()
                .HaveDependencyOn("NetSdrClientApp.Networking")
                .GetResult();

            Assert.That(result.IsSuccessful, Is.True);
        }

        [Test]
        public void Networking_Should_Not_Depend_On_Messages()
        {
            // Перевіряємо, що шар Networking не залежить від шару Messages (уникнення циклічних залежностей)
            var result = Types.InAssembly(ClientAssembly)
                .That()
                .ResideInNamespace("NetSdrClientApp.Networking")
                .ShouldNot()
                .HaveDependencyOn("NetSdrClientApp.Messages")
                .GetResult();

            Assert.That(result.IsSuccessful, Is.True);
        }

        // НОВИЙ ТЕСТ: Заборона залежностей, виправлений для безпечної обробки Assert.Fail
        [Test]
        public void No_Forbidden_Dependencies()
        {
            var forbiddenNamespaces = new[]
            {
                // Це ті простори імен, які ви порушите для демонстрації
                "System.Xml",
                "System.Windows.Forms",
                "System.Data"
            };

            // Забороняємо залежність від цих просторів імен для всієї збірки клієнта
            var result = Types.InAssembly(ClientAssembly)
                .ShouldNot()
                .HaveDependencyOnAny(forbiddenNamespaces)
                .GetResult();

            if (!result.IsSuccessful)
            {
                // Безпечно отримуємо список класів, що порушили правило, 
                // обробляючи можливий null-результат від NetArchTest
                var failingTypes = result.FailingTypes?
                    .Select(t => t.FullName)
                    .ToArray() ?? Array.Empty<string>();

                string message = $"Знайдено заборонену залежність. Порушники: {string.Join(", ", failingTypes)}";

                // Явно провалюємо тест із детальним повідомленням
                Assert.Fail(message);
            }

            // Якщо тест успішний, він просто проходить.
            Assert.Pass();
        }
    }
}