using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record SalesReportRow(DateTimeOffset CreatedAtUtc, Guid SaleId, string Status, decimal Total, string PaymentMethod, Guid? CustomerId);
public sealed record InventoryReportRow(Guid ProductId, string Code, string Description, decimal Stock, decimal Cost, decimal Value);
public sealed record CreditReportRow(Guid CustomerId, string Name, decimal Balance, decimal CreditLimit, decimal Available);

public sealed class ReportService(PosDbContext database)
{
    public async Task<IReadOnlyList<SalesReportRow>?> SalesAsync(string token, DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, "ViewReports", cancellationToken) is null) return null;
        var query = from sale in database.Sales.AsNoTracking()
                    join payment in database.Payments.AsNoTracking() on sale.Id equals payment.SaleId
                    where sale.CreatedAtUtc >= startDate
                    where sale.CreatedAtUtc < endDate
                    orderby sale.CreatedAtUtc descending
                    select new SalesReportRow(sale.CreatedAtUtc, sale.Id, sale.Status, sale.Total, payment.Method, sale.CustomerId);
        return await query.Take(5000).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryReportRow>?> InventoryAsync(string token, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, "ViewInventory", cancellationToken) is null) return null;
        return await database.Products.AsNoTracking().OrderBy(item => item.Description).Select(item => new InventoryReportRow(item.Id, item.Code, item.Description, item.Stock, item.Cost, decimal.Round(item.Stock * item.Cost, 2))).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CreditReportRow>?> CreditAsync(string token, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, "ManageCustomersAndCredit", cancellationToken) is null) return null;
        var balances = await database.CreditTransactions.AsNoTracking().GroupBy(item => item.CustomerId).Select(group => new { CustomerId = group.Key, Balance = group.Sum(item => item.Amount) }).ToDictionaryAsync(item => item.CustomerId, item => item.Balance, cancellationToken);
        return await database.Customers.AsNoTracking().OrderBy(item => item.Name).Select(item => new CreditReportRow(item.Id, item.Name, balances.GetValueOrDefault(item.Id), item.CreditLimit, item.CreditLimit - balances.GetValueOrDefault(item.Id))).ToListAsync(cancellationToken);
    }

    public async Task<byte[]?> SalesCsvAsync(string token, DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken cancellationToken)
    {
        var rows = await SalesAsync(token, startDate, endDate, cancellationToken); if (rows is null) return null;
        var builder = new StringBuilder("Fecha,Venta,Estado,Total,FormaPago,Cliente\r\n");
        foreach (var row in rows) builder.Append(string.Join(',', Csv(row.CreatedAtUtc.ToString("O")), Csv(row.SaleId.ToString()), Csv(row.Status), row.Total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), Csv(row.PaymentMethod), Csv(row.CustomerId?.ToString() ?? string.Empty))).Append("\r\n");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
    }

    private async Task<UserRecord?> AuthorizedAsync(string token, string permission, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty))); var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken); if (session is null) return null; var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken); return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == permission, cancellationToken) ? user : null;
    }
    private static string Csv(string value) => value.StartsWith('=') || value.StartsWith('+') || value.StartsWith('-') || value.StartsWith('@') ? $"'\"{value.Replace("\"", "\"\"")}\"" : $"\"{value.Replace("\"", "\"\"")}\"";
}
