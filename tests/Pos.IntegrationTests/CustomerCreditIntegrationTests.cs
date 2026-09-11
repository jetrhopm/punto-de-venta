using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class CustomerCreditIntegrationTests
{
    [Fact]
    public async Task FiltersCreditCustomersAndPreventsDeactivationWithPendingBalance()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var user = new UserRecord
        {
            Id = Guid.NewGuid(),
            NormalizedUserName = "CUSTOMER_TEST_" + suffix,
            DisplayName = "Administrador de clientes",
            PasswordHash = "test",
            IsAdministrator = true,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var session = new SessionRecord
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
        };
        database.AddRange(user, session);
        await database.SaveChangesAsync();

        try
        {
            var service = new CustomerCreditService(database);
            var standard = await service.CreateAsync(token, new CustomerCommand("Cliente general " + suffix, "5550000000", null, null, 0m, false), CancellationToken.None);
            var credit = await service.CreateAsync(token, new CustomerCommand("Cliente crédito " + suffix, null, null, null, 500m, true), CancellationToken.None);

            Assert.NotNull(standard);
            Assert.NotNull(credit);
            Assert.Equal(2, (await service.ListAsync(token, suffix, false, CancellationToken.None))!.Count);
            Assert.Equal(credit!.Id, Assert.Single((await service.ListAsync(token, suffix, true, CancellationToken.None))!).Id);

            var frozen = await service.UpdateAsync(token, credit.Id, new CustomerCommand(credit.Name, credit.Phone, credit.Email, credit.TaxId, credit.CreditLimit, true, true), CancellationToken.None);
            Assert.NotNull(frozen);
            Assert.True(frozen!.CreditEnabled);
            Assert.True(frozen.CreditFrozen);
            Assert.True((await service.ListAsync(token, suffix, true, CancellationToken.None))!.Single().CreditFrozen);

            database.CreditTransactions.Add(new CreditTransactionRecord
            {
                Id = Guid.NewGuid(),
                CustomerId = credit.Id,
                UserId = user.Id,
                OperationId = Guid.NewGuid(),
                Type = "Sale",
                Amount = 80m,
                BalanceBefore = 0m,
                BalanceAfter = 80m,
                Reason = "Venta a crédito de prueba",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await database.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetStatusAsync(token, credit.Id, new CustomerStatusCommand(false), CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(token, credit.Id, new CustomerCommand(credit.Name, credit.Phone, credit.Email, credit.TaxId, credit.CreditLimit, false), CancellationToken.None));
            Assert.NotNull(await service.SetStatusAsync(token, standard!.Id, new CustomerStatusCommand(false), CancellationToken.None));
            Assert.DoesNotContain((await service.ListAsync(token, suffix, false, CancellationToken.None))!, item => item.Id == standard.Id);
        }
        finally
        {
            var customerIds = await database.Customers.Where(item => item.Name.EndsWith(suffix)).Select(item => item.Id).ToListAsync();
            database.CreditTransactions.RemoveRange(database.CreditTransactions.Where(item => customerIds.Contains(item.CustomerId)));
            await database.SaveChangesAsync();
            database.Customers.RemoveRange(database.Customers.Where(item => customerIds.Contains(item.Id)));
            database.Sessions.RemoveRange(database.Sessions.Where(item => item.UserId == user.Id));
            database.Users.Remove(user);
            await database.SaveChangesAsync();
        }
    }
}
