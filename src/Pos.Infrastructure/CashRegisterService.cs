using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record CashMovementCommand(string Type, decimal Amount, string Reason);
public sealed record CloseShiftCommand(decimal CountedCash);
public sealed record ShiftSummary(Guid ShiftId, decimal ExpectedCash, decimal CountedCash, decimal Difference, DateTimeOffset? ClosedAtUtc);

public sealed class CashRegisterService(PosDbContext database)
{
    public async Task<ShiftSummary?> AddMovementAsync(string token, CashMovementCommand command, CancellationToken cancellationToken)
    {
        var shift = await GetOpenShiftAsync(token, cancellationToken);
        if (shift is null) return null;
        if (command.Type is not ("In" or "Out") || command.Amount <= 0m || string.IsNullOrWhiteSpace(command.Reason)) throw new ArgumentException("El movimiento requiere tipo, importe positivo y concepto.");
        database.CashMovements.Add(new CashMovementRecord { Id = Guid.NewGuid(), ShiftId = shift.Id, Type = command.Type, Amount = decimal.Round(command.Amount, 2), Reason = command.Reason.Trim(), CreatedAtUtc = DateTimeOffset.UtcNow });
        await database.SaveChangesAsync(cancellationToken);
        return await SummaryAsync(shift, null, cancellationToken);
    }

    public async Task<ShiftSummary?> CloseAsync(string token, CloseShiftCommand command, CancellationToken cancellationToken)
    {
        var shift = await GetOpenShiftAsync(token, cancellationToken);
        if (shift is null) return null;
        if (command.CountedCash < 0m) throw new ArgumentException("El efectivo contado no puede ser negativo.");
        var summary = await SummaryAsync(shift, command.CountedCash, cancellationToken);
        shift.Status = "Closed"; shift.ClosedAtUtc = DateTimeOffset.UtcNow; shift.CountedCash = decimal.Round(command.CountedCash, 2); shift.Difference = summary.Difference;
        await database.SaveChangesAsync(cancellationToken);
        return summary with { ClosedAtUtc = shift.ClosedAtUtc };
    }

    private async Task<ShiftRecord?> GetOpenShiftAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        return session is null ? null : await database.Shifts.SingleOrDefaultAsync(item => item.UserId == session.UserId && item.Status == "Open", cancellationToken);
    }

    private async Task<ShiftSummary> SummaryAsync(ShiftRecord shift, decimal? counted, CancellationToken cancellationToken)
    {
        var salesCash = await database.Payments.Where(payment => database.Sales.Any(sale => sale.Id == payment.SaleId && sale.ShiftId == shift.Id)).SumAsync(payment => payment.Amount, cancellationToken);
        var movementsIn = await database.CashMovements.Where(item => item.ShiftId == shift.Id && item.Type == "In").SumAsync(item => item.Amount, cancellationToken);
        var movementsOut = await database.CashMovements.Where(item => item.ShiftId == shift.Id && item.Type == "Out").SumAsync(item => item.Amount, cancellationToken);
        var expected = decimal.Round(shift.InitialCash + salesCash + movementsIn - movementsOut, 2);
        var actual = counted ?? expected;
        return new ShiftSummary(shift.Id, expected, actual, decimal.Round(actual - expected, 2), shift.ClosedAtUtc);
    }
}
