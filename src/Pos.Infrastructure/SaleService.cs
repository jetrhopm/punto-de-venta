using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record SaleLineCommand(Guid ProductId, decimal Quantity);
public sealed record CompleteSaleCommand(Guid OperationId, IReadOnlyList<SaleLineCommand> Lines, decimal CashReceived);
public sealed record CompleteSaleResult(Guid SaleId, Guid OperationId, decimal Total, decimal CashReceived, decimal Change, bool Existing);

public sealed class SaleService(PosDbContext database)
{
    public async Task<CompleteSaleResult?> CompleteAsync(string accessToken, CompleteSaleCommand command, CancellationToken cancellationToken)
    {
        if (command.OperationId == Guid.Empty || command.Lines.Count == 0 || command.CashReceived < 0m) throw new ArgumentException("La operacion, las partidas y el efectivo recibido son obligatorios.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken ?? string.Empty)));
        var session = await database.Sessions.SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        if (!user.IsAdministrator && !await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "Sell", cancellationToken)) return null;
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await database.Sales.AsNoTracking().SingleOrDefaultAsync(sale => sale.OperationId == command.OperationId, cancellationToken);
        if (existing is not null) return new CompleteSaleResult(existing.Id, existing.OperationId, existing.Total, 0m, 0m, true);
        var shift = await database.Shifts.SingleOrDefaultAsync(item => item.UserId == user.Id && item.Status == "Open", cancellationToken) ?? throw new InvalidOperationException("El usuario no tiene un turno abierto.");
        var productIds = command.Lines.Select(line => line.ProductId).Distinct().ToArray();
        var products = await database.Products.Where(product => productIds.Contains(product.Id) && product.IsActive).ToDictionaryAsync(product => product.Id, cancellationToken);
        if (products.Count != productIds.Length) throw new ArgumentException("Una o mas partidas no existen o estan inactivas.");
        var lines = new List<SaleLineRecord>();
        foreach (var line in command.Lines)
        {
            if (line.Quantity <= 0m) throw new ArgumentException("La cantidad debe ser mayor que cero.");
            var product = products[line.ProductId];
            if (product.Stock < line.Quantity) throw new InvalidOperationException($"Existencia insuficiente para {product.Description}.");
            var total = decimal.Round(product.Price * line.Quantity, 2, MidpointRounding.AwayFromZero);
            product.Stock -= line.Quantity;
            lines.Add(new SaleLineRecord { Id = Guid.NewGuid(), ProductId = product.Id, Quantity = line.Quantity, UnitPrice = product.Price, LineTotal = total });
        }
        var totalSale = decimal.Round(lines.Sum(line => line.LineTotal), 2, MidpointRounding.AwayFromZero);
        if (command.CashReceived < totalSale) throw new InvalidOperationException("El efectivo recibido es insuficiente.");
        var sale = new SaleRecord { Id = Guid.NewGuid(), OperationId = command.OperationId, ShiftId = shift.Id, Total = totalSale, CreatedAtUtc = DateTimeOffset.UtcNow };
        foreach (var line in lines) { line.SaleId = sale.Id; database.InventoryMovements.Add(new InventoryMovementRecord { Id = Guid.NewGuid(), ProductId = line.ProductId, SaleId = sale.Id, Quantity = -line.Quantity, CreatedAtUtc = sale.CreatedAtUtc }); }
        database.Sales.Add(sale); database.SaleLines.AddRange(lines); database.Payments.Add(new PaymentRecord { Id = Guid.NewGuid(), SaleId = sale.Id, Amount = command.CashReceived });
        await database.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new CompleteSaleResult(sale.Id, sale.OperationId, totalSale, command.CashReceived, command.CashReceived - totalSale, false);
    }
}
