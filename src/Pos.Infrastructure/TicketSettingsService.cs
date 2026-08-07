using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
namespace Pos.Infrastructure;
public sealed record TicketSettingsCommand(string Header, string Footer, int WidthMm);
public sealed class TicketSettingsService(PosDbContext database)
{
    public async Task<StoreRecord?> GetAsync(string token, CancellationToken cancellationToken) => await AuthorizedAsync(token, cancellationToken) is null ? null : await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
    public async Task<StoreRecord?> UpdateAsync(string token, TicketSettingsCommand command, CancellationToken cancellationToken)
    { if (await AuthorizedAsync(token, cancellationToken) is null) return null; if (command.WidthMm is not (58 or 80) || command.Header.Length > 300 || command.Footer.Length > 300) throw new ArgumentException("El formato debe ser 58 u 80 mm y los textos no deben superar 300 caracteres."); var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken); store.TicketHeader = command.Header.Trim(); store.TicketFooter = command.Footer.Trim(); store.TicketWidthMm = command.WidthMm; await database.SaveChangesAsync(cancellationToken); return store; }
    private async Task<UserRecord?> AuthorizedAsync(string token, CancellationToken cancellationToken) { var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty))); var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken); if (session is null) return null; var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken); return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "ConfigureStore", cancellationToken) ? user : null; }
}
