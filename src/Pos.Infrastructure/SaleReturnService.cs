using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record ReturnLineCommand(Guid ProductId, decimal Quantity);
public sealed record ReturnSaleCommand(Guid OperationId, Guid SaleId, IReadOnlyList<ReturnLineCommand> Lines, string Reason);
public sealed record ReturnSaleResult(Guid ReturnId, Guid SaleId, decimal Amount, bool Existing);
public sealed record SaleLineForReturn(Guid ProductId, string Description, decimal SoldQuantity, decimal ReturnedQuantity, decimal UnitPrice);

public sealed class SaleReturnService(PosDbContext database)
{
    public async Task<ReturnSaleResult?> ReturnAsync(string token, ReturnSaleCommand command, CancellationToken cancellationToken)
    {
        if (command.OperationId == Guid.Empty || command.SaleId == Guid.Empty || command.Lines.Count == 0 || string.IsNullOrWhiteSpace(command.Reason)) throw new ArgumentException("La devolucion requiere operacion, venta, partidas y motivo.");
        var user = await AuthorizedUserAsync(token, cancellationToken);
        if (user is null) return null;
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await database.Returns.AsNoTracking().SingleOrDefaultAsync(item => item.OperationId == command.OperationId, cancellationToken);
        if (existing is not null) return new ReturnSaleResult(existing.Id, existing.SaleId, existing.Amount, true);
        var sale = await database.Sales.SingleOrDefaultAsync(item => item.Id == command.SaleId && item.Status == "Completed", cancellationToken) ?? throw new InvalidOperationException("La venta no esta activa para devolucion.");
        var shift = await database.Shifts.SingleOrDefaultAsync(item => item.UserId == user.Id && item.Status == "Open", cancellationToken) ?? throw new InvalidOperationException("El usuario no tiene un turno abierto.");
        var sold = await database.SaleLines.Where(item => item.SaleId == sale.Id).ToDictionaryAsync(item => item.ProductId, cancellationToken);
        var returned = await database.ReturnLines.Where(item => item.ReturnId != Guid.Empty && database.Returns.Any(ret => ret.Id == item.ReturnId && ret.SaleId == sale.Id)).GroupBy(item => item.ProductId).Select(group => new { ProductId = group.Key, Quantity = group.Sum(item => item.Quantity) }).ToDictionaryAsync(item => item.ProductId, item => item.Quantity, cancellationToken);
        var lines = new List<ReturnLineRecord>(); var amount = 0m;
        foreach (var commandLine in command.Lines)
        {
            if (commandLine.Quantity <= 0m || !sold.TryGetValue(commandLine.ProductId, out var soldLine)) throw new ArgumentException("La partida de devolucion no es valida.");
            var alreadyReturned = returned.GetValueOrDefault(commandLine.ProductId);
            if (alreadyReturned + commandLine.Quantity > soldLine.Quantity) throw new InvalidOperationException("La cantidad devuelta no puede superar la cantidad vendida.");
            var lineAmount = decimal.Round(commandLine.Quantity * soldLine.UnitPrice, 2, MidpointRounding.AwayFromZero); amount += lineAmount;
            lines.Add(new ReturnLineRecord { Id = Guid.NewGuid(), ProductId = commandLine.ProductId, Quantity = commandLine.Quantity, UnitPrice = soldLine.UnitPrice, Amount = lineAmount });
        }
        var record = new ReturnRecord { Id = Guid.NewGuid(), SaleId = sale.Id, UserId = user.Id, OperationId = command.OperationId, Amount = decimal.Round(amount, 2), Reason = command.Reason.Trim(), CreatedAtUtc = DateTimeOffset.UtcNow };
        foreach (var line in lines)
        {
            line.ReturnId = record.Id; var product = await database.Products.SingleAsync(item => item.Id == line.ProductId, cancellationToken); var before = product.Stock; product.Stock = decimal.Round(before + line.Quantity, 3, MidpointRounding.AwayFromZero); database.InventoryMovements.Add(new InventoryMovementRecord { Id = Guid.NewGuid(), ProductId = line.ProductId, SaleId = sale.Id, UserId = user.Id, OperationId = command.OperationId, Quantity = line.Quantity, StockBefore = before, StockAfter = product.Stock, Reason = "SaleReturn", CreatedAtUtc = record.CreatedAtUtc });
        }
        var payment = await database.Payments.SingleOrDefaultAsync(item => item.SaleId == sale.Id, cancellationToken);
        if (payment?.Method == "Cash") database.CashMovements.Add(new CashMovementRecord { Id = Guid.NewGuid(), ShiftId = shift.Id, Type = "Out", Amount = record.Amount, Reason = $"Devolucion de venta {sale.Id}", CreatedAtUtc = record.CreatedAtUtc });
        if (payment?.Method == "Credit" && sale.CustomerId is not null)
        {
            var balance = await database.CreditTransactions.Where(item => item.CustomerId == sale.CustomerId).SumAsync(item => item.Amount, cancellationToken); if (balance < record.Amount) throw new InvalidOperationException("El saldo del cliente no permite devolver este importe.");
            database.CreditTransactions.Add(new CreditTransactionRecord { Id = Guid.NewGuid(), CustomerId = sale.CustomerId.Value, SaleId = sale.Id, UserId = user.Id, OperationId = command.OperationId, Type = "SaleReturn", Amount = -record.Amount, BalanceBefore = balance, BalanceAfter = balance - record.Amount, Reason = command.Reason.Trim(), CreatedAtUtc = record.CreatedAtUtc });
        }
        database.Returns.Add(record); database.ReturnLines.AddRange(lines);
        var totalReturned = returned.Values.Sum() + lines.Sum(item => item.Quantity); if (totalReturned >= sold.Values.Sum(item => item.Quantity)) sale.Status = "Returned"; else sale.Status = "PartiallyReturned";
        await database.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return new ReturnSaleResult(record.Id, sale.Id, record.Amount, false);
    }

    public async Task<IReadOnlyList<SaleLineForReturn>?> LinesAsync(string token, Guid saleId, CancellationToken cancellationToken)
    {
        if (await AuthorizedUserAsync(token, cancellationToken) is null) return null;
        var sold = await (from line in database.SaleLines.AsNoTracking() join product in database.Products.AsNoTracking() on line.ProductId equals product.Id where line.SaleId == saleId select new SaleLineForReturn(line.ProductId, product.Description, line.Quantity, 0m, line.UnitPrice)).ToListAsync(cancellationToken);
        var returned = await database.ReturnLines.Where(item => database.Returns.Any(ret => ret.SaleId == saleId && ret.Id == item.ReturnId)).GroupBy(item => item.ProductId).Select(group => new { ProductId = group.Key, Quantity = group.Sum(item => item.Quantity) }).ToDictionaryAsync(item => item.ProductId, item => item.Quantity, cancellationToken);
        return sold.Select(item => item with { ReturnedQuantity = returned.GetValueOrDefault(item.ProductId) }).ToArray();
    }
    private async Task<UserRecord?> AuthorizedUserAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty))); var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken); if (session is null) return null; var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken); return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "ProcessReturns", cancellationToken) ? user : null;
    }
}
