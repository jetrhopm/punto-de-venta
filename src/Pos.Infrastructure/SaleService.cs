using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record SaleLineCommand(Guid ProductId, decimal Quantity, bool UseWholesale = false);
public sealed record CompleteSaleCommand(Guid OperationId, IReadOnlyList<SaleLineCommand> Lines, decimal CashReceived, Guid? CustomerId = null, string PaymentMethod = "Cash");
public sealed record CompleteSaleResult(Guid SaleId, Guid OperationId, decimal Total, decimal CashReceived, decimal Change, bool Existing);

public sealed class SaleService(PosDbContext database, PromotionService promotions, KitService kits)
{
    public async Task<CompleteSaleResult?> CompleteAsync(string accessToken, CompleteSaleCommand command, CancellationToken cancellationToken)
    {
        if (command.OperationId == Guid.Empty || command.Lines.Count == 0 || command.CashReceived < 0m || command.PaymentMethod is not ("Cash" or "Credit")) throw new ArgumentException("La operacion, las partidas, forma de pago y efectivo recibido son obligatorios.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken ?? string.Empty)));
        var session = await database.Sessions.SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        if (!user.IsAdministrator && !await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "Sell", cancellationToken)) return null;
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await database.Sales.AsNoTracking().SingleOrDefaultAsync(sale => sale.OperationId == command.OperationId, cancellationToken);
        if (existing is not null) return new CompleteSaleResult(existing.Id, existing.OperationId, existing.Total, 0m, 0m, true);
        var shift = await database.Shifts.SingleOrDefaultAsync(item => item.UserId == user.Id && item.Status == "Open", cancellationToken) ?? throw new InvalidOperationException("El usuario no tiene un turno abierto.");
        var expanded = new Dictionary<Guid, decimal>();
        foreach (var line in command.Lines) { var parts = await kits.ExpandAsync(line.ProductId, line.Quantity, cancellationToken) ?? throw new KeyNotFoundException("Producto no encontrado."); foreach (var part in parts) expanded[part.ProductId] = expanded.GetValueOrDefault(part.ProductId) + part.Quantity; }
        var productIds = expanded.Keys.Concat(command.Lines.Select(item => item.ProductId)).Distinct().ToArray();
        var products = await database.Products.Where(product => productIds.Contains(product.Id) && product.IsActive).ToDictionaryAsync(product => product.Id, cancellationToken);
        if (products.Count != productIds.Length) throw new ArgumentException("Una o mas partidas no existen o estan inactivas.");
        var lines = new List<SaleLineRecord>();
        foreach (var line in command.Lines)
        {
            if (line.Quantity <= 0m) throw new ArgumentException("La cantidad debe ser mayor que cero.");
            var product = products[line.ProductId];
            var requested = expanded[line.ProductId];
            if (!product.IsKit && product.Stock < requested) throw new InvalidOperationException($"Existencia insuficiente para {product.Description}.");
            var stockBefore = product.Stock;
            var unitPrice = product.Price;
            var originalLine = command.Lines.SingleOrDefault(item => item.ProductId == line.ProductId);
            var requestedQuantity = originalLine?.Quantity ?? requested;
            unitPrice = originalLine is not null && originalLine.UseWholesale && product.WholesalePrice > 0m && requestedQuantity >= product.WholesaleMinimumQuantity ? product.WholesalePrice : product.Price;
            unitPrice = await promotions.DiscountedPriceAsync(product.Id, unitPrice, DateTimeOffset.UtcNow, cancellationToken);
            var total = originalLine is null ? 0m : decimal.Round(unitPrice * requestedQuantity, 2, MidpointRounding.AwayFromZero);
            if (!product.IsKit) product.Stock -= requested;
            if (originalLine is not null) lines.Add(new SaleLineRecord { Id = Guid.NewGuid(), ProductId = product.Id, Quantity = requestedQuantity, UnitPrice = unitPrice, LineTotal = total, StockBefore = stockBefore, StockAfter = product.Stock });
        }
        var totalSale = decimal.Round(lines.Sum(line => line.LineTotal), 2, MidpointRounding.AwayFromZero);
        CustomerRecord? customer = null;
        var currentCredit = 0m;
        if (command.PaymentMethod == "Credit")
        {
            if (command.CustomerId is null) throw new InvalidOperationException("Una venta a credito requiere cliente.");
            customer = await database.Customers.SingleOrDefaultAsync(item => item.Id == command.CustomerId && item.IsActive && item.CreditEnabled, cancellationToken) ?? throw new InvalidOperationException("El cliente no existe o no tiene credito habilitado.");
            currentCredit = await database.CreditTransactions.Where(item => item.CustomerId == customer.Id).SumAsync(item => item.Amount, cancellationToken);
            if (currentCredit + totalSale > customer.CreditLimit) throw new InvalidOperationException("La venta excede el limite de credito del cliente.");
        }
        else if (command.CashReceived < totalSale) throw new InvalidOperationException("El efectivo recibido es insuficiente.");
        var sale = new SaleRecord { Id = Guid.NewGuid(), OperationId = command.OperationId, ShiftId = shift.Id, CustomerId = command.CustomerId, Total = totalSale, CreatedAtUtc = DateTimeOffset.UtcNow };
        foreach (var line in lines) { line.SaleId = sale.Id; if (!products[line.ProductId].IsKit) database.InventoryMovements.Add(new InventoryMovementRecord { Id = Guid.NewGuid(), ProductId = line.ProductId, SaleId = sale.Id, UserId = user.Id, OperationId = command.OperationId, Quantity = -line.Quantity, StockBefore = line.StockBefore, StockAfter = line.StockAfter, CreatedAtUtc = sale.CreatedAtUtc }); }
        foreach (var component in expanded.Where(item => !command.Lines.Any(line => line.ProductId == item.Key))) { var product = products[component.Key]; if (product.Stock < component.Value) throw new InvalidOperationException($"Existencia insuficiente para {product.Description}."); var before = product.Stock; product.Stock -= component.Value; database.InventoryMovements.Add(new InventoryMovementRecord { Id = Guid.NewGuid(), ProductId = product.Id, SaleId = sale.Id, UserId = user.Id, OperationId = command.OperationId, Quantity = -component.Value, StockBefore = before, StockAfter = product.Stock, Reason = "KitSale", CreatedAtUtc = sale.CreatedAtUtc }); }
        var change = command.PaymentMethod == "Cash" ? decimal.Round(command.CashReceived - totalSale, 2, MidpointRounding.AwayFromZero) : 0m;
        database.Sales.Add(sale); database.SaleLines.AddRange(lines); database.Payments.Add(new PaymentRecord { Id = Guid.NewGuid(), SaleId = sale.Id, Method = command.PaymentMethod, Amount = totalSale, Received = command.PaymentMethod == "Cash" ? command.CashReceived : 0m, Change = change });
        if (customer is not null) database.CreditTransactions.Add(new CreditTransactionRecord { Id = Guid.NewGuid(), CustomerId = customer.Id, SaleId = sale.Id, UserId = user.Id, OperationId = command.OperationId, Type = "Sale", Amount = totalSale, BalanceBefore = currentCredit, BalanceAfter = currentCredit + totalSale, Reason = "Venta a credito", CreatedAtUtc = sale.CreatedAtUtc });
        database.PrintJobs.Add(new PrintJobRecord { Id = Guid.NewGuid(), SaleId = sale.Id, CreatedAtUtc = sale.CreatedAtUtc });
        await database.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new CompleteSaleResult(sale.Id, sale.OperationId, totalSale, command.CashReceived, change, false);
    }
}
