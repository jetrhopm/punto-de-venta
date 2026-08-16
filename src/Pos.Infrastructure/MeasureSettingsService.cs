using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record MeasureSettingsResult(string DefaultWeightUnit);
public sealed record SetMeasureSettingsCommand(string DefaultWeightUnit);

public sealed class MeasureSettingsService(PosDbContext database)
{
    private static readonly string[] WeightUnits = ["Kilogramo", "Gramo", "Libra", "Onza"];

    public async Task<MeasureSettingsResult?> GetAsync(string token, CancellationToken cancellationToken)
    {
        if (!await AuthorizedAsync(token, cancellationToken)) return null;
        var store = await database.Stores.AsNoTracking().OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        return new MeasureSettingsResult(Normalize(store.DefaultWeightUnit));
    }

    public async Task<MeasureSettingsResult?> UpdateAsync(string token, SetMeasureSettingsCommand command, CancellationToken cancellationToken)
    {
        if (!await AuthorizedAsync(token, cancellationToken)) return null;
        var unit = Normalize(command.DefaultWeightUnit);
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        store.DefaultWeightUnit = unit;
        await database.SaveChangesAsync(cancellationToken);
        return new MeasureSettingsResult(unit);
    }

    private static string Normalize(string? unit)
    {
        var value = unit?.Trim() ?? string.Empty;
        var result = WeightUnits.FirstOrDefault(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        if (result is null) throw new ArgumentException("Selecciona una unidad de peso válida: kilogramo, gramo, libra u onza.");
        return result;
    }

    private async Task<bool> AuthorizedAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return false;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "ConfigureStore", cancellationToken);
    }
}
