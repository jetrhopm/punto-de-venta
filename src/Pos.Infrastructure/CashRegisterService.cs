using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record CashMovementCommand(string Type, decimal Amount, string Reason);
public sealed record CloseShiftCommand(decimal? CountedCash);
public sealed record ShiftSummary(Guid ShiftId, decimal ExpectedCash, decimal CountedCash, decimal Difference, DateTimeOffset? ClosedAtUtc);
public sealed record CashCutSummary(decimal InitialCash, decimal TotalSales, int SalesCount, decimal CashSales, decimal CardSales, decimal TransferSales, decimal CreditSales, decimal CashIn, decimal CashOut, decimal CashReturns, decimal Profit, decimal ExpectedCash);
public sealed record CashierCutOption(Guid Id, string Name);

public sealed class CashRegisterService(PosDbContext database)
{
    public async Task<ShiftSummary?> CurrentSummaryAsync(string token, CancellationToken cancellationToken)
    {
        var shift = await GetOpenShiftAsync(token, cancellationToken);
        return shift is null ? null : await SummaryAsync(shift, null, cancellationToken);
    }

    public async Task<CashCutSummary?> CurrentCutAsync(string token, CancellationToken cancellationToken)
    {
        var shift = await GetOpenShiftAsync(token, cancellationToken);
        if (shift is null) return null;
        var sales = await database.Sales.AsNoTracking().Where(item => item.ShiftId == shift.Id && item.Status == "Completed").ToListAsync(cancellationToken);
        var saleIds = sales.Select(item => item.Id).ToArray();
        var payments = await database.Payments.AsNoTracking().Where(item => saleIds.Contains(item.SaleId)).ToListAsync(cancellationToken);
        var movements = await database.CashMovements.AsNoTracking().Where(item => item.ShiftId == shift.Id).ToListAsync(cancellationToken);
        var lines = await (from line in database.SaleLines.AsNoTracking() join product in database.Products.AsNoTracking() on line.ProductId equals product.Id where saleIds.Contains(line.SaleId) select new { line.LineTotal, line.Quantity, product.Cost }).ToListAsync(cancellationToken);
        var cashSales = payments.Where(item => item.Method == "Cash").Sum(item => item.Amount);
        var cashIn = movements.Where(item => item.Type == "In").Sum(item => item.Amount);
        var cashReturns = movements.Where(item => item.Type == "Out" && (item.Reason.StartsWith("Devolucion", StringComparison.OrdinalIgnoreCase) || item.Reason.StartsWith("Cancelacion", StringComparison.OrdinalIgnoreCase))).Sum(item => item.Amount);
        var cashOut = movements.Where(item => item.Type == "Out").Sum(item => item.Amount) - cashReturns;
        var expectedCash = decimal.Round(shift.InitialCash + cashSales + cashIn - cashOut - cashReturns, 2);
        var profit = decimal.Round(lines.Sum(item => item.LineTotal - (item.Quantity * item.Cost)), 2);
        return new CashCutSummary(decimal.Round(shift.InitialCash, 2), decimal.Round(sales.Sum(item => item.Total), 2), sales.Count, decimal.Round(cashSales, 2), decimal.Round(payments.Where(item => item.Method == "Card").Sum(item => item.Amount), 2), decimal.Round(payments.Where(item => item.Method == "Transfer").Sum(item => item.Amount), 2), decimal.Round(payments.Where(item => item.Method == "Credit").Sum(item => item.Amount), 2), decimal.Round(cashIn, 2), decimal.Round(cashOut, 2), decimal.Round(cashReturns, 2), profit, expectedCash);
    }

    public async Task<IReadOnlyList<CashierCutOption>> CashiersForDayAsync(string token, DateOnly date, CancellationToken cancellationToken)
    {
        if (await AuthorizedUserAsync(token, cancellationToken) is null) return [];
        var (fromUtc, toUtc) = DayRangeUtc(date);
        return await (from shift in database.Shifts.AsNoTracking()
                      join user in database.Users.AsNoTracking() on shift.UserId equals user.Id
                      where shift.OpenedAtUtc >= fromUtc && shift.OpenedAtUtc < toUtc
                      orderby user.DisplayName
                      select new CashierCutOption(user.Id, user.DisplayName)).Distinct().ToListAsync(cancellationToken);
    }

    public async Task<CashCutSummary?> CutForDayAsync(string token, DateOnly date, Guid? cashierId, CancellationToken cancellationToken)
    {
        if (await AuthorizedUserAsync(token, cancellationToken) is null) return null;
        var (fromUtc, toUtc) = DayRangeUtc(date);
        var query = database.Shifts.AsNoTracking().Where(shift => shift.OpenedAtUtc >= fromUtc && shift.OpenedAtUtc < toUtc);
        if (cashierId.HasValue) query = query.Where(shift => shift.UserId == cashierId.Value);
        var shifts = await query.ToListAsync(cancellationToken);
        return shifts.Count == 0 ? null : await AggregateCutAsync(shifts, cancellationToken);
    }

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
        var settings = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        if (settings.RequireCashCountOnClose && command.CountedCash is null) throw new ArgumentException("Esta tienda requiere capturar el efectivo contado al cerrar turno.");
        var counted = settings.RequireCashCountOnClose ? command.CountedCash!.Value : (await SummaryAsync(shift, null, cancellationToken)).ExpectedCash;
        var summary = await SummaryAsync(shift, counted, cancellationToken);
        if (settings.RequireCashCountOnClose && settings.AutoAdjustCashDifference && summary.Difference != 0m)
        {
            database.CashMovements.Add(new CashMovementRecord { Id = Guid.NewGuid(), ShiftId = shift.Id, Type = summary.Difference > 0m ? "In" : "Out", Amount = Math.Abs(summary.Difference), Reason = "Ajuste automático por diferencia de corte", CreatedAtUtc = DateTimeOffset.UtcNow });
            await database.SaveChangesAsync(cancellationToken);
            summary = await SummaryAsync(shift, counted, cancellationToken);
        }
        shift.Status = "Closed"; shift.ClosedAtUtc = DateTimeOffset.UtcNow; shift.CountedCash = decimal.Round(counted, 2); shift.Difference = summary.Difference;
        await database.SaveChangesAsync(cancellationToken);
        return summary with { ClosedAtUtc = shift.ClosedAtUtc };
    }

    private async Task<ShiftRecord?> GetOpenShiftAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        return session is null ? null : await database.Shifts.SingleOrDefaultAsync(item => item.UserId == session.UserId && item.Status == "Open", cancellationToken);
    }

    private async Task<UserRecord?> AuthorizedUserAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == session.UserId && item.IsActive, cancellationToken);
        return user is { IsAdministrator: true } || user is not null && await database.Permissions.AnyAsync(item => item.UserId == user.Id && (item.Code == "CloseShift" || item.Code == "ViewPreviousShifts"), cancellationToken) ? user : null;
    }

    private async Task<CashCutSummary> AggregateCutAsync(IReadOnlyCollection<ShiftRecord> shifts, CancellationToken cancellationToken)
    {
        var shiftIds = shifts.Select(item => item.Id).ToArray();
        var sales = await database.Sales.AsNoTracking().Where(item => shiftIds.Contains(item.ShiftId) && item.Status == "Completed").ToListAsync(cancellationToken);
        var saleIds = sales.Select(item => item.Id).ToArray();
        var payments = await database.Payments.AsNoTracking().Where(item => saleIds.Contains(item.SaleId)).ToListAsync(cancellationToken);
        var movements = await database.CashMovements.AsNoTracking().Where(item => shiftIds.Contains(item.ShiftId)).ToListAsync(cancellationToken);
        var lines = await (from line in database.SaleLines.AsNoTracking() join product in database.Products.AsNoTracking() on line.ProductId equals product.Id where saleIds.Contains(line.SaleId) select new { line.LineTotal, line.Quantity, product.Cost }).ToListAsync(cancellationToken);
        var cashSales = payments.Where(item => item.Method == "Cash").Sum(item => item.Amount);
        var cashIn = movements.Where(item => item.Type == "In").Sum(item => item.Amount);
        var cashReturns = movements.Where(item => item.Type == "Out" && (item.Reason.StartsWith("Devolucion", StringComparison.OrdinalIgnoreCase) || item.Reason.StartsWith("Cancelacion", StringComparison.OrdinalIgnoreCase))).Sum(item => item.Amount);
        var cashOut = movements.Where(item => item.Type == "Out").Sum(item => item.Amount) - cashReturns;
        var expectedCash = decimal.Round(shifts.Sum(item => item.InitialCash) + cashSales + cashIn - cashOut - cashReturns, 2);
        var profit = decimal.Round(lines.Sum(item => item.LineTotal - (item.Quantity * item.Cost)), 2);
        return new CashCutSummary(decimal.Round(shifts.Sum(item => item.InitialCash), 2), decimal.Round(sales.Sum(item => item.Total), 2), sales.Count, decimal.Round(cashSales, 2), decimal.Round(payments.Where(item => item.Method == "Card").Sum(item => item.Amount), 2), decimal.Round(payments.Where(item => item.Method == "Transfer").Sum(item => item.Amount), 2), decimal.Round(payments.Where(item => item.Method == "Credit").Sum(item => item.Amount), 2), decimal.Round(cashIn, 2), decimal.Round(cashOut, 2), decimal.Round(cashReturns, 2), profit, expectedCash);
    }

    private static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) DayRangeUtc(DateOnly date)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time (Mexico)"); }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City"); }
        var localStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var nextStart = localStart.AddDays(1);
        return (new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, zone)), new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(nextStart, zone)));
    }

    private async Task<ShiftSummary> SummaryAsync(ShiftRecord shift, decimal? counted, CancellationToken cancellationToken)
    {
        var salesCash = await database.Payments
            .Where(payment => payment.Method == "Cash" && database.Sales.Any(sale => sale.Id == payment.SaleId && sale.ShiftId == shift.Id && sale.Status == "Completed"))
            .SumAsync(payment => payment.Amount, cancellationToken);
        var movementsIn = await database.CashMovements.Where(item => item.ShiftId == shift.Id && item.Type == "In").SumAsync(item => item.Amount, cancellationToken);
        var movementsOut = await database.CashMovements.Where(item => item.ShiftId == shift.Id && item.Type == "Out").SumAsync(item => item.Amount, cancellationToken);
        var expected = decimal.Round(shift.InitialCash + salesCash + movementsIn - movementsOut, 2);
        var actual = counted ?? expected;
        return new ShiftSummary(shift.Id, expected, actual, decimal.Round(actual - expected, 2), shift.ClosedAtUtc);
    }
}
