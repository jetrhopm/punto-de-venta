using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record CashMovementCommand(string Type, decimal Amount, string Reason);
public sealed record CloseShiftCommand(decimal? CountedCash, Guid? AuthorizationGrantId = null);
public sealed record ShiftSummary(Guid ShiftId, decimal ExpectedCash, decimal CountedCash, decimal Difference, DateTimeOffset? ClosedAtUtc);
public sealed record HandoffShiftCommand(decimal DeliveredCash);
public sealed record ShiftHandoffResult(Guid HandoffId, Guid FromShiftId, string FromUser, decimal ExpectedCash, decimal DeliveredCash, decimal Difference, DateTimeOffset DeliveredAtUtc);
public sealed record DailyShiftHandoffRow(string Cashier, DateTimeOffset OpenedAtUtc, DateTimeOffset? ClosedAtUtc, decimal InitialCash, decimal? ExpectedCash, decimal? DeliveredCash, decimal? Difference, string CloseReason, decimal? ReceivedCash, string? ReceivedBy, DateTimeOffset? ReceivedAtUtc);
public sealed record CashCutSummary(decimal InitialCash, decimal TotalSales, int SalesCount, decimal CashSales, decimal CardSales, decimal TransferSales, decimal CreditSales, decimal CashIn, decimal CashOut, decimal CashReturns, decimal Profit, decimal ExpectedCash, decimal SupplierPayments = 0m);
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
        return new CashCutSummary(decimal.Round(shift.InitialCash, 2), decimal.Round(sales.Sum(item => item.Total), 2), sales.Count, decimal.Round(cashSales, 2), decimal.Round(payments.Where(item => item.Method == "Card").Sum(item => item.Amount), 2), decimal.Round(payments.Where(item => item.Method == "Transfer").Sum(item => item.Amount), 2), decimal.Round(payments.Where(item => item.Method == "Credit").Sum(item => item.Amount), 2), decimal.Round(cashIn, 2), decimal.Round(cashOut, 2), decimal.Round(cashReturns, 2), profit, expectedCash, SupplierPaymentsOf(movements));
    }

    public async Task<IReadOnlyList<CashierCutOption>> CashiersForDayAsync(string token, DateOnly date, CancellationToken cancellationToken)
    {
        if (await AuthorizedUserAsync(token, cancellationToken) is null) return [];
        var (fromUtc, toUtc) = await DayRangeUtcAsync(date, cancellationToken);
        return await (from shift in database.Shifts.AsNoTracking()
                      join user in database.Users.AsNoTracking() on shift.UserId equals user.Id
                      where shift.OpenedAtUtc >= fromUtc && shift.OpenedAtUtc < toUtc
                      orderby user.DisplayName
                      select new CashierCutOption(user.Id, user.DisplayName)).Distinct().ToListAsync(cancellationToken);
    }

    public async Task<CashCutSummary?> CutForDayAsync(string token, DateOnly date, Guid? cashierId, CancellationToken cancellationToken)
    {
        if (await AuthorizedUserAsync(token, cancellationToken) is null) return null;
        var (fromUtc, toUtc) = await DayRangeUtcAsync(date, cancellationToken);
        var query = database.Shifts.AsNoTracking().Where(shift => shift.OpenedAtUtc >= fromUtc && shift.OpenedAtUtc < toUtc);
        if (cashierId.HasValue) query = query.Where(shift => shift.UserId == cashierId.Value);
        var shifts = await query.ToListAsync(cancellationToken);
        return shifts.Count == 0 ? null : await AggregateCutAsync(shifts, cancellationToken);
    }

    public async Task<ShiftSummary?> AddMovementAsync(string token, CashMovementCommand command, CancellationToken cancellationToken)
    {
        if (!await HasPermissionAsync(token, "RecordCashMovements", cancellationToken)) return null;
        var shift = await GetOpenShiftAsync(token, cancellationToken);
        if (shift is null) return null;
        if (command.Type is not ("In" or "Out") || command.Amount <= 0m || string.IsNullOrWhiteSpace(command.Reason)) throw new ArgumentException("El movimiento requiere tipo, importe positivo y concepto.");
        database.CashMovements.Add(new CashMovementRecord { Id = Guid.NewGuid(), ShiftId = shift.Id, Type = command.Type, Amount = decimal.Round(command.Amount, 2), Reason = command.Reason.Trim(), CreatedAtUtc = DateTimeOffset.UtcNow });
        await database.SaveChangesAsync(cancellationToken);
        return await SummaryAsync(shift, null, cancellationToken);
    }

    public async Task<ShiftSummary?> CloseAsync(string token, CloseShiftCommand command, CancellationToken cancellationToken)
    {
        var closeAuthorization = await GetCloseAuthorizationAsync(token, command.AuthorizationGrantId, cancellationToken);
        if (closeAuthorization is null) return null;
        var shift = await GetOpenShiftAsync(token, cancellationToken);
        if (shift is null) return null;
        if (command.CountedCash < 0m) throw new ArgumentException("El efectivo contado no puede ser negativo.");
        var settings = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        if (settings.RequireCashCountOnClose && command.CountedCash is null) throw new ArgumentException("Esta tienda requiere capturar el efectivo contado al cerrar turno.");

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var counted = settings.RequireCashCountOnClose ? command.CountedCash!.Value : (await SummaryAsync(shift, null, cancellationToken)).ExpectedCash;
        var summary = await SummaryAsync(shift, counted, cancellationToken);
        if (settings.RequireCashCountOnClose && settings.AutoAdjustCashDifference && summary.Difference != 0m)
        {
            database.CashMovements.Add(new CashMovementRecord { Id = Guid.NewGuid(), ShiftId = shift.Id, Type = summary.Difference > 0m ? "In" : "Out", Amount = Math.Abs(summary.Difference), Reason = "Ajuste automático por diferencia de corte", CreatedAtUtc = DateTimeOffset.UtcNow });
            await database.SaveChangesAsync(cancellationToken);
            summary = await SummaryAsync(shift, counted, cancellationToken);
        }
        shift.Status = "Closed"; shift.ClosedAtUtc = DateTimeOffset.UtcNow; shift.CountedCash = decimal.Round(counted, 2); shift.Difference = summary.Difference; shift.ExpectedCashAtClose = summary.ExpectedCash; shift.CloseReason = "Cut";
        if (closeAuthorization.TemporaryGrant is not null) database.Permissions.Remove(closeAuthorization.TemporaryGrant);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return summary with { ClosedAtUtc = shift.ClosedAtUtc };
    }

    public async Task<ShiftHandoffResult?> HandoffAsync(string token, HandoffShiftCommand command, CancellationToken cancellationToken)
    {
        if (command.DeliveredCash < 0m) throw new ArgumentException("El efectivo entregado no puede ser negativo.");
        var session = await GetSessionAsync(token, cancellationToken);
        if (session?.RegisterId is not Guid registerId) return null;

        await using var transaction = await database.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var shift = await database.Shifts.SingleOrDefaultAsync(item => item.UserId == session.UserId && item.RegisterId == registerId && item.Status == "Open", cancellationToken);
        if (shift is null) return null;
        if (await database.ShiftHandoffs.AnyAsync(item => item.RegisterId == registerId && item.Status == "Pending", cancellationToken))
            throw new InvalidOperationException("Ya existe una entrega pendiente de confirmar en esta caja.");

        var summary = await SummaryAsync(shift, decimal.Round(command.DeliveredCash, 2), cancellationToken);
        var deliveredAt = DateTimeOffset.UtcNow;
        shift.Status = "Closed";
        shift.ClosedAtUtc = deliveredAt;
        shift.CountedCash = summary.CountedCash;
        shift.Difference = summary.Difference;
        shift.ExpectedCashAtClose = summary.ExpectedCash;
        shift.CloseReason = "Handoff";
        var handoff = new ShiftHandoffRecord
        {
            Id = Guid.NewGuid(), RegisterId = registerId, FromShiftId = shift.Id,
            ExpectedCash = summary.ExpectedCash, DeliveredCash = summary.CountedCash,
            Status = "Pending", CreatedAtUtc = deliveredAt
        };
        database.ShiftHandoffs.Add(handoff);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var cashier = await database.Users.AsNoTracking().Where(item => item.Id == session.UserId).Select(item => item.DisplayName).SingleAsync(cancellationToken);
        return new ShiftHandoffResult(handoff.Id, shift.Id, cashier, summary.ExpectedCash, summary.CountedCash, summary.Difference, deliveredAt);
    }

    public async Task<IReadOnlyList<DailyShiftHandoffRow>?> HandoffsForDayAsync(string token, DateOnly date, Guid? cashierId, CancellationToken cancellationToken)
    {
        if (await AuthorizedUserAsync(token, cancellationToken) is null) return null;
        var (fromUtc, toUtc) = await DayRangeUtcAsync(date, cancellationToken);
        var shifts = database.Shifts.AsNoTracking().Where(item => item.OpenedAtUtc >= fromUtc && item.OpenedAtUtc < toUtc);
        if (cashierId.HasValue) shifts = shifts.Where(item => item.UserId == cashierId.Value);
        return await (
            from shift in shifts
            join user in database.Users.AsNoTracking() on shift.UserId equals user.Id
            join handoffValue in database.ShiftHandoffs.AsNoTracking() on shift.Id equals handoffValue.FromShiftId into handoffs
            from handoff in handoffs.DefaultIfEmpty()
            join receiverValue in database.Users.AsNoTracking() on (handoff == null ? (Guid?)null : (database.Shifts.Where(next => next.Id == handoff.ToShiftId).Select(next => (Guid?)next.UserId).FirstOrDefault())) equals receiverValue.Id into receivers
            from receiver in receivers.DefaultIfEmpty()
            orderby shift.OpenedAtUtc
            select new DailyShiftHandoffRow(user.DisplayName, shift.OpenedAtUtc, shift.ClosedAtUtc, shift.InitialCash, shift.ExpectedCashAtClose, shift.CountedCash, shift.Difference, shift.CloseReason, handoff == null ? null : handoff.ReceivedCash, receiver == null ? null : receiver.DisplayName, handoff == null ? null : handoff.ReceivedAtUtc)
        ).ToListAsync(cancellationToken);
    }

    private async Task<CloseAuthorization?> GetCloseAuthorizationAsync(string token, Guid? temporaryGrantId, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(
            item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow,
            cancellationToken);
        if (session is null) return null;

        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == session.UserId && item.IsActive, cancellationToken);
        if (user is null) return null;
        if (user.IsAdministrator || await database.Permissions.IgnoreQueryFilters().AnyAsync(
                item => item.UserId == user.Id && item.Code == "CloseShift" && item.ExpiresAtUtc == null,
                cancellationToken))
        {
            return new CloseAuthorization(null);
        }

        if (temporaryGrantId is null) return null;
        var temporaryGrant = await database.Permissions.IgnoreQueryFilters().SingleOrDefaultAsync(
            item => item.Id == temporaryGrantId.Value &&
                    item.UserId == user.Id &&
                    item.Code == "CloseShift" &&
                    item.ExpiresAtUtc != null &&
                    item.ExpiresAtUtc > DateTimeOffset.UtcNow,
            cancellationToken);
        return temporaryGrant is null ? null : new CloseAuthorization(temporaryGrant);
    }

    private async Task<ShiftRecord?> GetOpenShiftAsync(string token, CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(token, cancellationToken);
        return session?.RegisterId is not Guid registerId
            ? null
            : await database.Shifts.SingleOrDefaultAsync(item => item.UserId == session.UserId && item.RegisterId == registerId && item.Status == "Open", cancellationToken);
    }

    private async Task<UserRecord?> AuthorizedUserAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == session.UserId && item.IsActive, cancellationToken);
        return user is { IsAdministrator: true } || user is not null && await database.Permissions.AnyAsync(item => item.UserId == user.Id && (item.Code == "CloseShift" || item.Code == "ViewPreviousShifts"), cancellationToken) ? user : null;
    }

    private async Task<bool> HasPermissionAsync(string token, string permission, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return false;
        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == session.UserId && item.IsActive, cancellationToken);
        return user is { IsAdministrator: true } || user is not null && await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == permission, cancellationToken);
    }

    private async Task<CashCutSummary> AggregateCutAsync(IReadOnlyCollection<ShiftRecord> shifts, CancellationToken cancellationToken)
    {
        var shiftIds = shifts.Select(item => item.Id).ToArray();
        // An amount received from another shift on the same report is a transfer,
        // not additional cash for the day. Count only the first physical fund.
        var internalReceipts = await database.ShiftHandoffs.AsNoTracking()
            .Where(item => item.ToShiftId.HasValue && shiftIds.Contains(item.FromShiftId) && shiftIds.Contains(item.ToShiftId.Value))
            .Select(item => item.ToShiftId!.Value)
            .ToListAsync(cancellationToken);
        var initialCash = shifts.Where(item => !internalReceipts.Contains(item.Id)).Sum(item => item.InitialCash);
        var sales = await database.Sales.AsNoTracking().Where(item => shiftIds.Contains(item.ShiftId) && item.Status == "Completed").ToListAsync(cancellationToken);
        var saleIds = sales.Select(item => item.Id).ToArray();
        var payments = await database.Payments.AsNoTracking().Where(item => saleIds.Contains(item.SaleId)).ToListAsync(cancellationToken);
        var movements = await database.CashMovements.AsNoTracking().Where(item => shiftIds.Contains(item.ShiftId)).ToListAsync(cancellationToken);
        var lines = await (from line in database.SaleLines.AsNoTracking() join product in database.Products.AsNoTracking() on line.ProductId equals product.Id where saleIds.Contains(line.SaleId) select new { line.LineTotal, line.Quantity, product.Cost }).ToListAsync(cancellationToken);
        var cashSales = payments.Where(item => item.Method == "Cash").Sum(item => item.Amount);
        var cashIn = movements.Where(item => item.Type == "In").Sum(item => item.Amount);
        var cashReturns = movements.Where(item => item.Type == "Out" && (item.Reason.StartsWith("Devolucion", StringComparison.OrdinalIgnoreCase) || item.Reason.StartsWith("Cancelacion", StringComparison.OrdinalIgnoreCase))).Sum(item => item.Amount);
        var cashOut = movements.Where(item => item.Type == "Out").Sum(item => item.Amount) - cashReturns;
        var expectedCash = decimal.Round(initialCash + cashSales + cashIn - cashOut - cashReturns, 2);
        var profit = decimal.Round(lines.Sum(item => item.LineTotal - (item.Quantity * item.Cost)), 2);
        return new CashCutSummary(decimal.Round(initialCash, 2), decimal.Round(sales.Sum(item => item.Total), 2), sales.Count, decimal.Round(cashSales, 2), decimal.Round(payments.Where(item => item.Method == "Card").Sum(item => item.Amount), 2), decimal.Round(payments.Where(item => item.Method == "Transfer").Sum(item => item.Amount), 2), decimal.Round(payments.Where(item => item.Method == "Credit").Sum(item => item.Amount), 2), decimal.Round(cashIn, 2), decimal.Round(cashOut, 2), decimal.Round(cashReturns, 2), profit, expectedCash, SupplierPaymentsOf(movements));
    }

    /// <summary>Parte de las salidas de efectivo que corresponde a pagos a proveedores (ya incluida en CashOut).</summary>
    private static decimal SupplierPaymentsOf(IEnumerable<CashMovementRecord> movements) =>
        decimal.Round(movements.Where(item => item.Type == "Out" && item.Reason.StartsWith("Pago a proveedor", StringComparison.OrdinalIgnoreCase)).Sum(item => item.Amount), 2);

    private async Task<(DateTimeOffset FromUtc, DateTimeOffset ToUtc)> DayRangeUtcAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var timeZoneId = await database.Stores.AsNoTracking()
            .OrderBy(store => store.CreatedAtUtc)
            .Select(store => store.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);
        return StoreTimeZone.DayRangeUtc(date, StoreTimeZone.Resolve(timeZoneId));
    }

    private async Task<SessionRecord?> GetSessionAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        return await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
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

    private sealed record CloseAuthorization(PermissionRecord? TemporaryGrant);
}
