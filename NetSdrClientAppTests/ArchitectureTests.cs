using NetArchTest.Rules;
using NUnit.Framework;
using System.Linq;
using System.Reflection;
using System.Text;

namespace NetSdrClientAppTests
{
    // Отримуємо збірку NetSdrClientApp один раз
    // ПРИМІТКА: typeof(NetSdrClientApp.NetSdrClient) має знаходитися в збірці NetSdrClientApp
    private readonly Assembly ClientAssembly = typeof(NetSdrClientApp.NetSdrClient).Assembly;

    public class ArchitectureTests
    {
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
            // Arrange
            var result = Types.InAssembly(ClientAssembly)
                .That()
                .ResideInNamespace("NetSdrClientApp.Messages")
                .ShouldNot()
                .HaveDependencyOn("NetSdrClientApp.Networking")
                .GetResult();

            // Assert
            Assert.That(result.IsSuccessful, Is.True);
        }

        [Test]
        public void Networking_Should_Not_Depend_On_Messages()
        {
            // Arrange
            var result = Types.InAssembly(ClientAssembly)
                .That()
                .ResideInNamespace("NetSdrClientApp.Networking")
                .ShouldNot()
                .HaveDependencyOn("NetSdrClientApp.Messages")
                .GetResult();

            // Assert
            Assert.That(result.IsSuccessful, Is.True);
        }

        // 🎯 НОВИЙ ТЕСТ: Заборона залежностей від небажаних системних бібліотек
        [Test]
        public void No_Forbidden_Dependencies()
        {
            var forbiddenNamespaces = new[]
            {
                "System.Xml",
                "System.Windows.Forms",
                "System.Data"
                // Можна додати інші важкі або UI-орієнтовані бібліотеки
            };

            // Забороняємо залежність від цих просторів імен
            var result = Types.InAssembly(ClientAssembly)
                .ShouldNot()
                .HaveDependencyOnAny(forbiddenNamespaces)
                .GetResult();

            // Важливо: Якщо тест провалюється, повідомлення Assert покаже, 
            // який саме клас порушив правило.
            Assert.That(result.IsSuccessful, Is.True,
                $"Знайдено заборонену залежність: {result.FailingTypes.FirstOrDefault()?.FullName}");
        }
    }
}
