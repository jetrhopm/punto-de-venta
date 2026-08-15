using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record SalesReportRow(DateTimeOffset CreatedAtUtc, Guid SaleId, string Status, decimal Total, string PaymentMethod, Guid? CustomerId);
public sealed record InventoryReportRow(Guid ProductId, string Code, string Description, decimal Stock, decimal Cost, decimal Value);
public sealed record CreditReportRow(Guid CustomerId, string Name, decimal Balance, decimal CreditLimit, decimal Available);
public sealed record PeriodSummaryRow(string Period, DateTimeOffset FromUtc, DateTimeOffset ToUtc, int SalesCount, decimal Total);
public sealed record ProductAnalysisRow(Guid ProductId, string Code, string Description, string Category, string UnitOfMeasure, decimal QuantitySold, decimal TotalSold, decimal Stock, decimal MinimumStock, decimal MaximumStock);
public sealed record SalesAnalysisResult(IReadOnlyList<PeriodSummaryRow> Periods, IReadOnlyList<ProductAnalysisRow> BestSellers, IReadOnlyList<ProductAnalysisRow> RestockNeeded, IReadOnlyList<ProductAnalysisRow> LowMovement, IReadOnlyList<ProductAnalysisRow> NoMovement);
public sealed record DailySalesDashboardRow(DateTime Date, int SalesCount, decimal Total, decimal EstimatedProfit);
public sealed record PaymentDashboardRow(string Method, decimal Total);
public sealed record DepartmentDashboardRow(string Department, decimal Total, decimal EstimatedProfit);
public sealed record SalesDashboardResult(decimal TotalSales, int SalesCount, decimal AverageSale, decimal EstimatedGrossProfit, decimal MarginPercent, IReadOnlyList<DailySalesDashboardRow> DailySales, IReadOnlyList<PaymentDashboardRow> Payments, IReadOnlyList<DepartmentDashboardRow> Departments);

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

    public async Task<SalesAnalysisResult?> SalesAnalysisAsync(string token, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, "ViewReports", cancellationToken) is null) return null;

        var now = DateTimeOffset.UtcNow;
        var weekStart = now.AddDays(-7);
        var monthStart = now.AddMonths(-1);
        var yearStart = now.AddYears(-1);

        var periods = new List<PeriodSummaryRow>
        {
            await SummaryAsync("Semana", weekStart, now, cancellationToken),
            await SummaryAsync("Mes", monthStart, now, cancellationToken),
            await SummaryAsync("Año", yearStart, now, cancellationToken)
        };

        var soldRows = await (from line in database.SaleLines.AsNoTracking()
                              join sale in database.Sales.AsNoTracking() on line.SaleId equals sale.Id
                              join product in database.Products.AsNoTracking() on line.ProductId equals product.Id
                              where sale.Status == "Completed"
                              where sale.CreatedAtUtc >= yearStart
                              where sale.CreatedAtUtc < now
                              group new { line, product } by new { product.Id, product.Code, product.Description, product.Category, product.UnitOfMeasure, product.Stock, product.MinimumStock, product.MaximumStock } into grouped
                              select new ProductAnalysisRow(grouped.Key.Id, grouped.Key.Code, grouped.Key.Description, grouped.Key.Category, grouped.Key.UnitOfMeasure, grouped.Sum(item => item.line.Quantity), grouped.Sum(item => item.line.LineTotal), grouped.Key.Stock, grouped.Key.MinimumStock, grouped.Key.MaximumStock))
            .ToListAsync(cancellationToken);

        var soldByProduct = soldRows.ToDictionary(item => item.ProductId);
        var products = await database.Products.AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.Description)
            .Select(item => new { item.Id, item.Code, item.Description, item.Category, item.UnitOfMeasure, item.Stock, item.MinimumStock, item.MaximumStock })
            .ToListAsync(cancellationToken);

        var activeRows = products.Select(item =>
            soldByProduct.TryGetValue(item.Id, out var sold)
                ? sold
                : new ProductAnalysisRow(item.Id, item.Code, item.Description, item.Category, item.UnitOfMeasure, 0m, 0m, item.Stock, item.MinimumStock, item.MaximumStock))
            .ToList();

        var restockNeeded = activeRows
            .Where(item => item.MinimumStock > 0m && item.Stock <= item.MinimumStock)
            .OrderBy(item => item.Stock - item.MinimumStock)
            .ThenByDescending(item => item.QuantitySold)
            .Take(60)
            .ToList();

        var lowMovement = soldRows
            .Where(item => item.QuantitySold > 0m && item.QuantitySold <= 2m)
            .OrderBy(item => item.QuantitySold)
            .ThenBy(item => item.Description)
            .Take(60)
            .ToList();

        var noMovement = activeRows
            .Where(item => item.QuantitySold <= 0m)
            .Take(60)
            .ToList();

        return new SalesAnalysisResult(
            periods,
            soldRows.OrderByDescending(item => item.QuantitySold).ThenByDescending(item => item.TotalSold).Take(30).ToList(),
            restockNeeded,
            lowMovement,
            noMovement);
    }

    public async Task<SalesDashboardResult?> SalesDashboardAsync(string token, DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, "ViewReports", cancellationToken) is null) return null;

        var completedSales = database.Sales.AsNoTracking()
            .Where(item => item.Status == "Completed")
            .Where(item => item.CreatedAtUtc >= startDate)
            .Where(item => item.CreatedAtUtc < endDate);

        var totals = await completedSales
            .GroupBy(_ => 1)
            .Select(group => new { SalesCount = group.Count(), Total = group.Sum(item => item.Total) })
            .SingleOrDefaultAsync(cancellationToken);

        var dailySales = await completedSales
            .Select(item => new { Date = item.CreatedAtUtc.Date, item.Total })
            .GroupBy(item => item.Date)
            .Select(group => new { Date = group.Key, SalesCount = group.Count(), Total = group.Sum(item => item.Total) })
            .OrderBy(item => item.Date)
            .ToListAsync(cancellationToken);

        var lineRows = from line in database.SaleLines.AsNoTracking()
                       join sale in completedSales on line.SaleId equals sale.Id
                       join product in database.Products.AsNoTracking() on line.ProductId equals product.Id
                       join department in database.Departments.AsNoTracking() on product.DepartmentId equals department.Id into departmentMatches
                       from department in departmentMatches.DefaultIfEmpty()
                       select new
                       {
                           Date = sale.CreatedAtUtc.Date,
                           line.LineTotal,
                           EstimatedProfit = (line.UnitPrice - product.Cost) * line.Quantity,
                           Department = department == null ? product.Category : department.Name
                       };

        var profitByDay = await lineRows
            .GroupBy(item => item.Date)
            .Select(group => new { Date = group.Key, Profit = group.Sum(item => item.EstimatedProfit) })
            .ToDictionaryAsync(item => item.Date, item => item.Profit, cancellationToken);

        var departments = await lineRows
            .GroupBy(item => item.Department)
            .Select(group => new { Department = group.Key, Total = group.Sum(item => item.LineTotal), EstimatedProfit = group.Sum(item => item.EstimatedProfit) })
            .OrderByDescending(item => item.Total)
            .Take(12)
            .ToListAsync(cancellationToken);

        var payments = await (from payment in database.Payments.AsNoTracking()
                              join sale in completedSales on payment.SaleId equals sale.Id
                              group payment by payment.Method into grouped
                              select new { Method = grouped.Key, Total = grouped.Sum(item => item.Amount) })
            .OrderByDescending(item => item.Total)
            .ToListAsync(cancellationToken);

        var totalProfit = profitByDay.Values.Sum();
        var totalSales = totals?.Total ?? 0m;
        return new SalesDashboardResult(
            totalSales,
            totals?.SalesCount ?? 0,
            totals is null || totals.SalesCount == 0 ? 0m : decimal.Round(totalSales / totals.SalesCount, 2),
            decimal.Round(totalProfit, 2),
            totalSales <= 0m ? 0m : decimal.Round(totalProfit / totalSales * 100m, 2),
            dailySales.Select(item => new DailySalesDashboardRow(item.Date, item.SalesCount, item.Total, decimal.Round(profitByDay.GetValueOrDefault(item.Date), 2))).ToList(),
            payments.Select(item => new PaymentDashboardRow(item.Method, item.Total)).ToList(),
            departments.Select(item => new DepartmentDashboardRow(string.IsNullOrWhiteSpace(item.Department) ? "Sin departamento" : item.Department, item.Total, decimal.Round(item.EstimatedProfit, 2))).ToList());
    }

    public async Task<byte[]?> SalesCsvAsync(string token, DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken cancellationToken)
    {
        var rows = await SalesAsync(token, startDate, endDate, cancellationToken); if (rows is null) return null;
        var builder = new StringBuilder("Fecha,Venta,Estado,Total,FormaPago,Cliente\r\n");
        foreach (var row in rows) builder.Append(string.Join(',', Csv(row.CreatedAtUtc.ToString("O")), Csv(row.SaleId.ToString()), Csv(row.Status), row.Total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), Csv(row.PaymentMethod), Csv(row.CustomerId?.ToString() ?? string.Empty))).Append("\r\n");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
    }

    private async Task<PeriodSummaryRow> SummaryAsync(string period, DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken cancellationToken)
    {
        var summary = await database.Sales.AsNoTracking()
            .Where(item => item.Status == "Completed")
            .Where(item => item.CreatedAtUtc >= startDate)
            .Where(item => item.CreatedAtUtc < endDate)
            .GroupBy(_ => 1)
            .Select(group => new { SalesCount = group.Count(), Total = group.Sum(item => item.Total) })
            .SingleOrDefaultAsync(cancellationToken);

        return new PeriodSummaryRow(period, startDate, endDate, summary?.SalesCount ?? 0, summary?.Total ?? 0m);
    }

    private async Task<UserRecord?> AuthorizedAsync(string token, string permission, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty))); var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken); if (session is null) return null; var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken); return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == permission, cancellationToken) ? user : null;
    }
    private static string Csv(string value) => value.StartsWith('=') || value.StartsWith('+') || value.StartsWith('-') || value.StartsWith('@') ? $"'\"{value.Replace("\"", "\"\"")}\"" : $"\"{value.Replace("\"", "\"\"")}\"";
}
