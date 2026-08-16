using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record StoreOptionsResult(
    bool InventoryEnabled,
    string InventoryCostMethod,
    bool CreditSalesEnabled,
    bool CommonProductsEnabled,
    bool AutoPriceWithProfit,
    decimal DefaultProfitPercent,
    bool RoundSaleAmounts,
    string RoundingMode,
    string OccasionalNotice,
    int OccasionalNoticeEverySales);

public sealed record SetStoreOptionsCommand(
    bool InventoryEnabled,
    string InventoryCostMethod,
    bool CreditSalesEnabled,
    bool CommonProductsEnabled,
    bool AutoPriceWithProfit,
    decimal DefaultProfitPercent,
    bool RoundSaleAmounts,
    string RoundingMode,
    string OccasionalNotice,
    int OccasionalNoticeEverySales);

public sealed class StoreOptionsService(PosDbContext database)
{
    public async Task<StoreOptionsResult?> GetAsync(string token, CancellationToken cancellationToken)
    {
        if (await GetAuthorizedUserAsync(token, cancellationToken) is null) return null;
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        return ToResult(store);
    }

    public async Task<StoreOptionsResult?> UpdateAsync(string token, SetStoreOptionsCommand command, CancellationToken cancellationToken)
    {
        if (await GetAuthorizedUserAsync(token, cancellationToken) is null) return null;
        Validate(command);
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        store.InventoryEnabled = command.InventoryEnabled;
        store.InventoryCostMethod = command.InventoryCostMethod.Trim();
        store.CreditSalesEnabled = command.CreditSalesEnabled;
        store.CommonProductsEnabled = command.CommonProductsEnabled;
        store.AutoPriceWithProfit = command.AutoPriceWithProfit;
        store.DefaultProfitPercent = decimal.Round(command.DefaultProfitPercent, 2);
        store.RoundSaleAmounts = command.RoundSaleAmounts;
        store.RoundingMode = command.RoundingMode.Trim();
        store.OccasionalNotice = command.OccasionalNotice?.Trim() ?? string.Empty;
        store.OccasionalNoticeEverySales = command.OccasionalNoticeEverySales;
        await database.SaveChangesAsync(cancellationToken);
        return ToResult(store);
    }

    private async Task<Guid?> GetAuthorizedUserAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "ConfigureStore", cancellationToken) ? user.Id : null;
    }

    private static void Validate(SetStoreOptionsCommand command)
    {
        if (!string.Equals(command.InventoryCostMethod, "WeightedAverage", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("El método de costo seleccionado no está disponible todavía.");
        if (command.DefaultProfitPercent is < 0m or > 1000m) throw new ArgumentException("El margen predeterminado debe estar entre 0 y 1000%.");
        if (!string.Equals(command.RoundingMode, "Tenths", StringComparison.OrdinalIgnoreCase) && !string.Equals(command.RoundingMode, "Whole", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("El tipo de redondeo no es válido.");
        if (command.OccasionalNotice?.Length > 300) throw new ArgumentException("El aviso admite hasta 300 caracteres.");
        if (command.OccasionalNoticeEverySales is < 1 or > 100000) throw new ArgumentException("La frecuencia del aviso debe estar entre 1 y 100000 ventas.");
    }

    private static StoreOptionsResult ToResult(StoreRecord store) => new(store.InventoryEnabled, store.InventoryCostMethod, store.CreditSalesEnabled, store.CommonProductsEnabled, store.AutoPriceWithProfit, store.DefaultProfitPercent, store.RoundSaleAmounts, store.RoundingMode, store.OccasionalNotice, store.OccasionalNoticeEverySales);
}
