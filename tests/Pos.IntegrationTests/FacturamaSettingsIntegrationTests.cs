using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class FacturamaSettingsIntegrationTests
{
    [Fact]
    public async Task KeepsFacturamaCredentialsProtectedAndVerifiesOnlyTheStoreAccount()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var createdStore = false;
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstOrDefaultAsync();
        if (store is null)
        {
            createdStore = true;
            store = new StoreRecord { Id = Guid.NewGuid(), Name = "Tienda de pruebas", BusinessType = "Pruebas", CreatedAtUtc = DateTimeOffset.UtcNow };
            database.Stores.Add(store);
            await database.SaveChangesAsync();
        }

        var original = new StoreSnapshot(store);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "FACTURAMA_" + Guid.NewGuid().ToString("N"), DisplayName = "Administrador Facturama", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var session = new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        database.AddRange(user, session);
        await database.SaveChangesAsync();

        try
        {
            var service = new FacturamaSettingsService(database, new HttpClient(new FacturamaSuccessHandler()));
            var settings = await service.UpdateAsync(token, new ConfigureFacturamaCommand(true, "Sandbox", "tienda@example.test", "contraseña-de-prueba"), CancellationToken.None);

            Assert.NotNull(settings);
            Assert.True(settings!.Enabled);
            Assert.True(settings.CredentialsStored);
            Assert.Equal("Sandbox", settings.Environment);
            Assert.Equal("tienda@example.test", settings.AccountEmail);
            Assert.NotEqual("contraseña-de-prueba", store.FacturamaPasswordProtected);

            var verification = await service.TestConnectionAsync(token, CancellationToken.None);
            Assert.NotNull(verification);
            Assert.True(verification!.Connected);
            Assert.NotNull(store.FacturamaLastVerifiedAtUtc);
            Assert.DoesNotContain("contraseña-de-prueba", store.FacturamaLastVerificationMessage, StringComparison.Ordinal);
        }
        finally
        {
            original.Restore(store);
            database.Sessions.Remove(session);
            database.Users.Remove(user);
            if (createdStore) database.Stores.Remove(store);
            await database.SaveChangesAsync();
        }
    }

    private sealed class FacturamaSuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://apisandbox.facturama.mx/api-lite/csds", request.RequestUri?.ToString());
            Assert.NotNull(request.Headers.Authorization);
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        }
    }

    private sealed record StoreSnapshot(bool Enabled, string Environment, string Email, string Password, DateTimeOffset? VerifiedAt, string Message)
    {
        public StoreSnapshot(StoreRecord store) : this(store.FacturamaEnabled, store.FacturamaEnvironment, store.FacturamaAccountEmail, store.FacturamaPasswordProtected, store.FacturamaLastVerifiedAtUtc, store.FacturamaLastVerificationMessage) { }
        public void Restore(StoreRecord store)
        {
            store.FacturamaEnabled = Enabled;
            store.FacturamaEnvironment = Environment;
            store.FacturamaAccountEmail = Email;
            store.FacturamaPasswordProtected = Password;
            store.FacturamaLastVerifiedAtUtc = VerifiedAt;
            store.FacturamaLastVerificationMessage = Message;
        }
    }
}
