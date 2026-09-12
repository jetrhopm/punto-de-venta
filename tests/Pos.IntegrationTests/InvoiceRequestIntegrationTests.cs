using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class InvoiceRequestIntegrationTests
{
    [Fact]
    public async Task RegistersPendingRequestWithoutChangingTheConfirmedSale()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = ("INVOICE_" + suffix).ToUpperInvariant(), DisplayName = "Administrador de facturas", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var session = new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        var sale = new SaleRecord { Id = Guid.NewGuid(), OperationId = Guid.NewGuid(), ShiftId = Guid.NewGuid(), Folio = Random.Shared.NextInt64(1, long.MaxValue / 2), Total = 245.50m, Status = "Completed", CreatedAtUtc = DateTimeOffset.UtcNow };
        database.AddRange(user, session, sale);
        await database.SaveChangesAsync();

        try
        {
            var service = new InvoiceRequestService(database);
            var found = await service.FindSaleAsync(token, sale.Folio, CancellationToken.None);
            Assert.NotNull(found);
            Assert.Equal(sale.Total, found!.Total);

            var request = await service.CreateAsync(token, new CreateInvoiceRequestCommand(sale.Folio, "XAXX010101000", "Cliente de prueba", "cliente@example.test", "Solicita factura por correo."), CancellationToken.None);
            Assert.NotNull(request);
            Assert.Equal("PendingStamping", request!.Status);
            Assert.Equal(sale.Total, request.SaleTotal);
            Assert.Single(await service.ListAsync(token, sale.Folio.ToString(), CancellationToken.None) ?? []);

            var unchangedSale = await database.Sales.SingleAsync(item => item.Id == sale.Id);
            Assert.Equal("Completed", unchangedSale.Status);
            Assert.Equal(245.50m, unchangedSale.Total);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(token, new CreateInvoiceRequestCommand(sale.Folio, "XAXX010101000", "Cliente de prueba", null, null), CancellationToken.None));
        }
        finally
        {
            database.InvoiceRequests.RemoveRange(database.InvoiceRequests.Where(item => item.SaleId == sale.Id));
            database.Sessions.Remove(session);
            database.Sales.Remove(sale);
            database.Users.Remove(user);
            await database.SaveChangesAsync();
        }
    }
}
