using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record SaveSaleDraftLinesCommand(IReadOnlyList<SaleDraftLineCommand> Lines);
public sealed record SaleDraftLineCommand(Guid ProductId, decimal Quantity);
public sealed record SaleDraftLineResult(Guid ProductId, string Code, string Description, decimal UnitPrice, decimal Stock, decimal Quantity);
public sealed record SaleDraftResult(Guid Id, Guid OperationId, int TicketNumber, DateTimeOffset UpdatedAtUtc, IReadOnlyList<SaleDraftLineResult> Lines);

public sealed class SaleDraftService(PosDbContext database)
{
    public async Task<IReadOnlyList<SaleDraftResult>?> ListOpenAsync(string accessToken, CancellationToken cancellationToken)
    {
        var context = await GetOpenShiftContextAsync(accessToken, cancellationToken);
        if (context is null) return null;

        var drafts = await database.SaleDrafts
            .Include(item => item.Lines)
            .Where(item => item.UserId == context.UserId && item.Status == "Open" && item.Lines.Any())
            .OrderBy(item => item.TicketNumber)
            .ToListAsync(cancellationToken);

        // Los tickets en atención no afectan caja ni inventario. Al retomar la sesión
        // del mismo cajero se asocian al turno nuevo para poder continuarlos o cobrarlos.
        var recoveredFromPreviousShift = drafts.Where(item => item.ShiftId != context.ShiftId).ToList();
        if (recoveredFromPreviousShift.Count > 0)
        {
            foreach (var draft in recoveredFromPreviousShift)
            {
                draft.ShiftId = context.ShiftId;
                draft.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            await database.SaveChangesAsync(cancellationToken);
        }
        return await ToResultsAsync(drafts, cancellationToken);
    }

    public async Task<SaleDraftResult?> CreateAsync(string accessToken, CancellationToken cancellationToken)
    {
        var context = await GetOpenShiftContextAsync(accessToken, cancellationToken);
        if (context is null) return null;

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var emptyDrafts = await database.SaleDrafts
            .Where(item => item.ShiftId == context.ShiftId && item.UserId == context.UserId && item.Status == "Open" && !item.Lines.Any())
            .ToListAsync(cancellationToken);
        foreach (var emptyDraft in emptyDrafts)
        {
            emptyDraft.Status = "Discarded";
            emptyDraft.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        var nextTicket = (await database.SaleDrafts
            .Where(item => item.ShiftId == context.ShiftId)
            .Select(item => (int?)item.TicketNumber)
            .MaxAsync(cancellationToken) ?? 0) + 1;
        var now = DateTimeOffset.UtcNow;
        var draft = new SaleDraftRecord
        {
            Id = Guid.NewGuid(),
            OperationId = Guid.NewGuid(),
            ShiftId = context.ShiftId,
            UserId = context.UserId,
            TicketNumber = nextTicket,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        database.SaleDrafts.Add(draft);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new SaleDraftResult(draft.Id, draft.OperationId, draft.TicketNumber, draft.UpdatedAtUtc, []);
    }

    public async Task<SaleDraftResult?> SaveLinesAsync(string accessToken, Guid draftId, SaveSaleDraftLinesCommand command, CancellationToken cancellationToken)
    {
        if (draftId == Guid.Empty || command.Lines is null) throw new ArgumentException("El ticket y sus partidas son obligatorios.");
        if (command.Lines.Any(line => line.ProductId == Guid.Empty || line.Quantity <= 0m)) throw new ArgumentException("Cada partida requiere producto y cantidad positiva.");
        if (command.Lines.Select(line => line.ProductId).Distinct().Count() != command.Lines.Count) throw new ArgumentException("Un producto solo puede aparecer una vez por ticket.");

        var context = await GetOpenShiftContextAsync(accessToken, cancellationToken);
        if (context is null) return null;
        var draft = await database.SaleDrafts.Include(item => item.Lines).SingleOrDefaultAsync(item => item.Id == draftId && item.UserId == context.UserId && item.Status == "Open", cancellationToken)
            ?? throw new KeyNotFoundException("El ticket en atención no existe o ya fue finalizado.");

        var productIds = command.Lines.Select(item => item.ProductId).ToArray();
        var products = await database.Products.Where(item => productIds.Contains(item.Id) && item.IsActive).ToDictionaryAsync(item => item.Id, cancellationToken);
        if (products.Count != productIds.Length) throw new ArgumentException("Una o más partidas ya no están disponibles.");

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        draft.ShiftId = context.ShiftId;
        var existingPrices = draft.Lines.ToDictionary(item => item.ProductId, item => item.UnitPrice);
        database.SaleDraftLines.RemoveRange(draft.Lines);
        await database.SaveChangesAsync(cancellationToken);
        foreach (var line in command.Lines)
        {
            var product = products[line.ProductId];
            database.SaleDraftLines.Add(new SaleDraftLineRecord
            {
                Id = Guid.NewGuid(),
                DraftId = draft.Id,
                ProductId = product.Id,
                Code = product.Code,
                Description = product.Description,
                Quantity = decimal.Round(line.Quantity, 3),
                UnitPrice = existingPrices.GetValueOrDefault(product.Id, product.Price)
            });
        }
        draft.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var refreshed = await database.SaleDrafts.AsNoTracking().Include(item => item.Lines).SingleAsync(item => item.Id == draft.Id, cancellationToken);
        return (await ToResultsAsync([refreshed], cancellationToken)).Single();
    }

    public async Task<bool?> DiscardAsync(string accessToken, Guid draftId, CancellationToken cancellationToken)
    {
        var context = await GetOpenShiftContextAsync(accessToken, cancellationToken);
        if (context is null) return null;
        var draft = await database.SaleDrafts.SingleOrDefaultAsync(item => item.Id == draftId && item.UserId == context.UserId && item.Status == "Open", cancellationToken);
        if (draft is null) return false;
        draft.Status = "Discarded";
        draft.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<IReadOnlyList<SaleDraftResult>> ToResultsAsync(IReadOnlyList<SaleDraftRecord> drafts, CancellationToken cancellationToken)
    {
        var productIds = drafts.SelectMany(item => item.Lines).Select(item => item.ProductId).Distinct().ToArray();
        var stock = productIds.Length == 0
            ? new Dictionary<Guid, decimal>()
            : await database.Products.AsNoTracking().Where(item => productIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => item.Stock, cancellationToken);
        return drafts.Select(draft => new SaleDraftResult(
            draft.Id,
            draft.OperationId,
            draft.TicketNumber,
            draft.UpdatedAtUtc,
            draft.Lines.OrderBy(line => line.Description).Select(line => new SaleDraftLineResult(line.ProductId, line.Code, line.Description, line.UnitPrice, stock.GetValueOrDefault(line.ProductId), line.Quantity)).ToArray()))
            .ToArray();
    }

    private async Task<OpenShiftContext?> GetOpenShiftContextAsync(string accessToken, CancellationToken cancellationToken)
    {
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == tokenHash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == session.UserId && item.IsActive, cancellationToken);
        if (user is null) return null;
        if (!user.IsAdministrator && !await database.Permissions.AsNoTracking().AnyAsync(item => item.UserId == user.Id && item.Code == "Sell", cancellationToken)) return null;
        var shift = await database.Shifts.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == session.UserId && item.Status == "Open", cancellationToken);
        return shift is null ? null : new OpenShiftContext(session.UserId, shift.Id);
    }

    private sealed record OpenShiftContext(Guid UserId, Guid ShiftId);
}
