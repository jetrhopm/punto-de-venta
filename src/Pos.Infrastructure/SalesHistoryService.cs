using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record SalesHistoryFilter(DateTimeOffset From, DateTimeOffset To, Guid? UserId = null, string? Query = null);
public sealed record SalesHistoryRow(Guid SaleId, DateTimeOffset CreatedAtUtc, string Status, decimal Total, string PaymentMethod, decimal Paid, string Cashier, Guid UserId, int Items);
public sealed record SaleHistoryDetail(Guid SaleId, DateTimeOffset CreatedAtUtc, string Status, decimal Total, string Cashier, IReadOnlyList<SaleHistoryLine> Lines, IReadOnlyList<SaleHistoryPayment> Payments);
public sealed record SaleHistoryLine(Guid ProductId, string Code, string Description, decimal Quantity, decimal UnitPrice, decimal Total);
public sealed record SaleHistoryPayment(string Method, decimal Amount, decimal Received, decimal Change);

public sealed class SalesHistoryService(PosDbContext database)
{
    public async Task<IReadOnlyList<SalesHistoryRow>?> ListAsync(string token, SalesHistoryFilter filter, CancellationToken cancellationToken)
    {
        var viewer = await AuthorizedAsync(token, "ViewSalesHistory", cancellationToken);
        if (viewer is null) return null;
        if (filter.To <= filter.From || filter.To - filter.From > TimeSpan.FromDays(366)) throw new ArgumentException("El periodo debe ser válido y no mayor a un año.");

        var query = from sale in database.Sales.AsNoTracking()
                    join shift in database.Shifts.AsNoTracking() on sale.ShiftId equals shift.Id
                    join user in database.Users.AsNoTracking() on shift.UserId equals user.Id
                    join payment in database.Payments.AsNoTracking() on sale.Id equals payment.SaleId into payments
                    where sale.CreatedAtUtc >= filter.From && sale.CreatedAtUtc < filter.To
                    where filter.UserId == null || shift.UserId == filter.UserId
                    select new { sale, shift, user, payments };
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var queryText = filter.Query.Trim().ToUpperInvariant();
            query = query.Where(item => item.sale.Id.ToString().ToUpper().Contains(queryText) || item.user.DisplayName.ToUpper().Contains(queryText) || item.user.NormalizedUserName.Contains(queryText));
        }

        var rows = await query.OrderByDescending(item => item.sale.CreatedAtUtc).Take(5000).ToListAsync(cancellationToken);
        var ids = rows.Select(item => item.sale.Id).ToArray();
        var counts = await database.SaleLines.AsNoTracking().Where(item => ids.Contains(item.SaleId)).GroupBy(item => item.SaleId).Select(group => new { SaleId = group.Key, Items = group.Sum(item => item.Quantity) }).ToDictionaryAsync(item => item.SaleId, item => item.Items, cancellationToken);
        return rows.Select(item => new SalesHistoryRow(item.sale.Id, item.sale.CreatedAtUtc, item.sale.Status, item.sale.Total, string.Join(" + ", item.payments.Select(payment => payment.Method).Distinct()), item.payments.Sum(payment => payment.Amount), item.user.DisplayName, item.user.Id, (int)counts.GetValueOrDefault(item.sale.Id))).ToArray();
    }

    public async Task<SaleHistoryDetail?> DetailAsync(string token, Guid saleId, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, "ViewSalesHistory", cancellationToken) is null) return null;
        var result = await (from sale in database.Sales.AsNoTracking()
                            join shift in database.Shifts.AsNoTracking() on sale.ShiftId equals shift.Id
                            join user in database.Users.AsNoTracking() on shift.UserId equals user.Id
                            where sale.Id == saleId
                            select new { sale, user }).SingleOrDefaultAsync(cancellationToken);
        if (result is null) throw new KeyNotFoundException("Venta no encontrada.");
        var lines = await (from line in database.SaleLines.AsNoTracking()
                           join product in database.Products.AsNoTracking() on line.ProductId equals product.Id
                           where line.SaleId == saleId
                           select new SaleHistoryLine(line.ProductId, product.Code, product.Description, line.Quantity, line.UnitPrice, line.LineTotal)).ToListAsync(cancellationToken);
        var payments = await database.Payments.AsNoTracking().Where(item => item.SaleId == saleId).Select(item => new SaleHistoryPayment(item.Method, item.Amount, item.Received, item.Change)).ToListAsync(cancellationToken);
        return new SaleHistoryDetail(result.sale.Id, result.sale.CreatedAtUtc, result.sale.Status, result.sale.Total, result.user.DisplayName, lines, payments);
    }

    public async Task<IReadOnlyList<UserResult>?> CashiersAsync(string token, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, "ViewSalesHistory", cancellationToken) is null) return null;
        return await database.Users.AsNoTracking().OrderBy(item => item.DisplayName).Select(item => new UserResult(item.Id, item.NormalizedUserName, item.DisplayName, item.IsAdministrator, item.IsActive, Array.Empty<string>())).ToListAsync(cancellationToken);
    }

    private async Task<UserRecord?> AuthorizedAsync(string token, string permission, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == session.UserId && item.IsActive, cancellationToken);
        return user is not null && (user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == permission, cancellationToken)) ? user : null;
    }
}
