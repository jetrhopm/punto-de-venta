using Microsoft.EntityFrameworkCore;

namespace Pos.Infrastructure;

public static class InventoryConcurrency
{
    public static async Task LockProductsAsync(PosDbContext database, IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        foreach (var productId in productIds.Where(id => id != Guid.Empty).Distinct().OrderBy(id => id))
        {
            await LockAsync(database, $"product:{productId:N}", cancellationToken);
        }
    }

    public static Task LockStoreAsync(PosDbContext database, Guid storeId, CancellationToken cancellationToken) =>
        LockAsync(database, $"store:{storeId:N}", cancellationToken);

    private static Task LockAsync(PosDbContext database, string key, CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
}
