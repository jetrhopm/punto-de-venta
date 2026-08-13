using System.Security.Cryptography;
using System.Text;
using Pos.Infrastructure;

namespace Pos.IntegrationTests;

public sealed class ProtectedConnectionConfigurationTests
{
    [Fact]
    public void ReadsExplicitMachineProtectedConnectionFile()
    {
        if (!OperatingSystem.IsWindows()) return;

        const string expected = "Host=127.0.0.1;Port=5432;Database=punto_venta;Username=pos_app;Password=test";
        var path = Path.Combine(Path.GetTempPath(), $"pos-connection-{Guid.NewGuid():N}.bin");
        try
        {
            var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(expected), null, DataProtectionScope.LocalMachine);
            File.WriteAllBytes(path, encrypted);

            var actual = PosDbContextFactory.ReadConfiguredConnectionString(path);

            Assert.Equal(expected, actual);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
