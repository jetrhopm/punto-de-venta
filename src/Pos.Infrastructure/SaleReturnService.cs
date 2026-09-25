using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record ReturnLineCommand(Guid ProductId, decimal Quantity);
public sealed record ReturnSaleCommand(Guid OperationId, Guid SaleId, IReadOnlyList<ReturnLineCommand> Lines, string Reason);
public sealed record ReturnSaleResult(Guid ReturnId, Guid SaleId, decimal Amount, bool Existing);
public sealed record SaleLineForReturn(Guid ProductId, string Description, decimal SoldQuantity, decimal ReturnedQuantity, decimal UnitPrice);

public sealed class SaleReturnService(PosDbContext database, KitService kits, MercadoPagoPointService mercadoPago)
{
    public async Task<ReturnSaleResult?> ReturnAsync(string token, ReturnSaleCommand command, CancellationToken cancellationToken)
    {
        if (command.OperationId == Guid.Empty || command.SaleId == Guid.Empty || command.Lines.Count == 0 || string.IsNullOrWhiteSpace(command.Reason)) throw new ArgumentException("La devolucion requiere operacion, venta, partidas y motivo.");
        var authorization = await AuthorizedAsync(token, cancellationToken);
        if (authorization is null) return null;
        var user = authorization.User;
        var existing = await database.Returns.AsNoTracking().SingleOrDefaultAsync(item => item.OperationId == command.OperationId, cancellationToken);
        if (existing is not null) return new ReturnSaleResult(existing.Id, existing.SaleId, existing.Amount, true);
        var saleContext = await FindSaleAsync(command.SaleId, authorization, cancellationToken) ?? throw new InvalidOperationException("La venta no está activa para devolución en esta tienda.");
        var sale = saleContext.Sale;
        if (sale.Status is not ("Completed" or "PartiallyReturned")) throw new InvalidOperationException("La venta ya no está activa para devolución.");
        // Se calcula antes de la modificación local. Con ello, una devolución parcial
        // manda sólo la parte pendiente a Point y no duplica efectivo ni reembolsos.
        var preflightSold = await database.SaleLines.Where(item => item.SaleId == sale.Id).ToDictionaryAsync(item => item.ProductId, cancellationToken);
        var preflightReturned = await database.ReturnLines.Where(item => item.ReturnId != Guid.Empty && database.Returns.Any(ret => ret.Id == item.ReturnId && ret.SaleId == sale.Id)).GroupBy(item => item.ProductId).Select(group => new { ProductId = group.Key, Quantity = group.Sum(item => item.Quantity), Amount = group.Sum(item => item.Amount) }).ToDictionaryAsync(item => item.ProductId, cancellationToken);
        var requestedAmount = 0m;
        foreach (var commandLine in command.Lines)
        {
            if (commandLine.Quantity <= 0m || !preflightSold.TryGetValue(commandLine.ProductId, out var soldLine)) throw new ArgumentException("La partida de devolucion no es valida.");
            var previous = preflightReturned.GetValueOrDefault(commandLine.ProductId);
            if (previous?.Quantity + commandLine.Quantity > soldLine.Quantity) throw new InvalidOperationException("La cantidad devuelta no puede superar la cantidad vendida.");
            requestedAmount += CalculateReturnAmount(soldLine, previous?.Quantity ?? 0m, previous?.Amount ?? 0m, commandLine.Quantity);
        }
        var previousReturnAmount = await database.Returns.Where(item => item.SaleId == sale.Id).SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0m;
        var cashPaidForSale = await database.Payments.Where(item => item.SaleId == sale.Id && item.Method == "Cash").SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0m;
        var pointOrder = await database.MercadoPagoOrders.SingleOrDefaultAsync(item => item.SaleOperationId == sale.OperationId && item.RegisterId == saleContext.SourceRegisterId && item.Status == "Approved", cancellationToken);
        if (pointOrder is not null)
        {
            var confirmedPointRefunds = await database.MercadoPagoRefunds.Where(item => item.MercadoPagoOrderId == pointOrder.Id && item.Status == "Confirmed").SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0m;
            var targetPointRefund = Math.Min(pointOrder.Amount, Math.Max(0m, previousReturnAmount + decimal.Round(requestedAmount, 2) - cashPaidForSale));
            var pointRefundDue = decimal.Round(targetPointRefund - confirmedPointRefunds, 2);
            if (pointRefundDue > 0m) await mercadoPago.EnsureSalePointRefundedAsync(sale.Id, sale.OperationId, saleContext.SourceRegisterId, command.OperationId, pointRefundDue, cancellationToken);
        }
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var shift = await database.Shifts.SingleOrDefaultAsync(item => item.UserId == user.Id && item.RegisterId == authorization.RegisterId && item.Status == "Open", cancellationToken) ?? throw new InvalidOperationException("El usuario no tiene un turno abierto en esta caja.");
        var sold = await database.SaleLines.Where(item => item.SaleId == sale.Id).ToDictionaryAsync(item => item.ProductId, cancellationToken);
        var returned = await database.ReturnLines.Where(item => item.ReturnId != Guid.Empty && database.Returns.Any(ret => ret.Id == item.ReturnId && ret.SaleId == sale.Id)).GroupBy(item => item.ProductId).Select(group => new { ProductId = group.Key, Quantity = group.Sum(item => item.Quantity), Amount = group.Sum(item => item.Amount) }).ToDictionaryAsync(item => item.ProductId, cancellationToken);
        var lines = new List<ReturnLineRecord>(); var amount = 0m;
        foreach (var commandLine in command.Lines)
        {
            if (commandLine.Quantity <= 0m || !sold.TryGetValue(commandLine.ProductId, out var soldLine)) throw new ArgumentException("La partida de devolucion no es valida.");
            var alreadyReturned = returned.GetValueOrDefault(commandLine.ProductId);
            if (alreadyReturned?.Quantity + commandLine.Quantity > soldLine.Quantity) throw new InvalidOperationException("La cantidad devuelta no puede superar la cantidad vendida.");
            var lineAmount = CalculateReturnAmount(soldLine, alreadyReturned?.Quantity ?? 0m, alreadyReturned?.Amount ?? 0m, commandLine.Quantity); amount += lineAmount;
            lines.Add(new ReturnLineRecord { Id = Guid.NewGuid(), ProductId = commandLine.ProductId, Quantity = commandLine.Quantity, UnitPrice = soldLine.UnitPrice, Amount = lineAmount });
        }
        var record = new ReturnRecord { Id = Guid.NewGuid(), SaleId = sale.Id, UserId = user.Id, OperationId = command.OperationId, ProcessedRegisterId = authorization.RegisterId, Amount = decimal.Round(amount, 2), Reason = command.Reason.Trim(), CreatedAtUtc = DateTimeOffset.UtcNow };
        var replenishments = new List<InventoryReplenishment>();
        foreach (var line in lines)
        {
            line.ReturnId = record.Id;
            var parts = await kits.ExpandAsync(line.ProductId, line.Quantity, cancellationToken) ?? throw new KeyNotFoundException("Producto de devolucion no encontrado.");
            replenishments.AddRange(parts.Select(part => new InventoryReplenishment(line.ProductId, part.ProductId, part.Quantity)));
        }
        var replenishmentProductIds = replenishments.Select(item => item.ProductId).Distinct().ToArray();
        await InventoryConcurrency.LockProductsAsync(database, replenishmentProductIds, cancellationToken);
        var products = await database.Products.Where(item => replenishmentProductIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        foreach (var replenishment in replenishments)
        {
            var product = products[replenishment.ProductId];
            if (product.IsTemporary) continue;
            var before = product.Stock;
            product.Stock = decimal.Round(before + replenishment.Quantity, 3, MidpointRounding.AwayFromZero);
            database.InventoryMovements.Add(new InventoryMovementRecord { Id = Guid.NewGuid(), ProductId = product.Id, SaleId = sale.Id, UserId = user.Id, OperationId = command.OperationId, Quantity = replenishment.Quantity, StockBefore = before, StockAfter = product.Stock, Reason = replenishment.SoldProductId == replenishment.ProductId ? "SaleReturn" : "KitReturn", CreatedAtUtc = record.CreatedAtUtc });
        }
        var payments = await database.Payments.Where(item => item.SaleId == sale.Id).ToListAsync(cancellationToken);
        var cashPaid = payments.Where(item => item.Method == "Cash").Sum(item => item.Amount);
        // Una devolución no puede regresar más efectivo que el componente en efectivo
        // de la venta, aun si se procesa en más de una operación.
        var previouslyReturnedAmount = await database.Returns
            .Where(item => item.SaleId == sale.Id)
            .SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0m;
        var cashRefund = Math.Min(record.Amount, Math.Max(0m, cashPaid - previouslyReturnedAmount));
        if (cashRefund > 0m) database.CashMovements.Add(new CashMovementRecord { Id = Guid.NewGuid(), ShiftId = shift.Id, Type = "Out", Amount = cashRefund, Reason = $"Devolucion de venta {sale.Folio}", CreatedAtUtc = record.CreatedAtUtc });
        if (payments.Any(item => item.Method == "Credit") && sale.CustomerId is not null)
        {
            var balance = await database.CreditTransactions.Where(item => item.CustomerId == sale.CustomerId).SumAsync(item => item.Amount, cancellationToken); if (balance < record.Amount) throw new InvalidOperationException("El saldo del cliente no permite devolver este importe.");
            database.CreditTransactions.Add(new CreditTransactionRecord { Id = Guid.NewGuid(), CustomerId = sale.CustomerId.Value, SaleId = sale.Id, UserId = user.Id, OperationId = command.OperationId, Type = "SaleReturn", Amount = -record.Amount, BalanceBefore = balance, BalanceAfter = balance - record.Amount, Reason = command.Reason.Trim(), CreatedAtUtc = record.CreatedAtUtc });
        }
        database.Returns.Add(record); database.ReturnLines.AddRange(lines);
        var totalReturned = returned.Values.Sum(item => item.Quantity) + lines.Sum(item => item.Quantity); if (totalReturned >= sold.Values.Sum(item => item.Quantity)) sale.Status = "Returned"; else sale.Status = "PartiallyReturned";
        await database.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return new ReturnSaleResult(record.Id, sale.Id, record.Amount, false);
    }

    public async Task<IReadOnlyList<SaleLineForReturn>?> LinesAsync(string token, Guid saleId, CancellationToken cancellationToken)
    {
        var authorization = await AuthorizedAsync(token, cancellationToken);
        if (authorization is null) return null;
        var saleContext = await FindSaleAsync(saleId, authorization, cancellationToken);
        if (saleContext is null) return [];
        var sold = await (from line in database.SaleLines.AsNoTracking() join product in database.Products.AsNoTracking() on line.ProductId equals product.Id where line.SaleId == saleId select new SaleLineForReturn(line.ProductId, product.Description, line.Quantity, 0m, line.UnitPrice)).ToListAsync(cancellationToken);
        var returned = await database.ReturnLines.Where(item => database.Returns.Any(ret => ret.SaleId == saleId && ret.Id == item.ReturnId)).GroupBy(item => item.ProductId).Select(group => new { ProductId = group.Key, Quantity = group.Sum(item => item.Quantity) }).ToDictionaryAsync(item => item.ProductId, item => item.Quantity, cancellationToken);
        return sold.Select(item => item with { ReturnedQuantity = returned.GetValueOrDefault(item.ProductId) }).ToArray();
    }
    private async Task<RegisterAuthorization?> AuthorizedAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session?.RegisterId is not Guid registerId) return null;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "ProcessReturns", cancellationToken)
            ? new RegisterAuthorization(user, registerId)
            : null;
    }

    private async Task<SaleAtRegister?> FindSaleAsync(Guid saleId, RegisterAuthorization authorization, CancellationToken cancellationToken)
    {
        var result = await (from sale in database.Sales
                            join saleShift in database.Shifts on sale.ShiftId equals saleShift.Id
                            join sourceRegister in database.Registers on saleShift.RegisterId equals sourceRegister.Id
                            join currentRegister in database.Registers on authorization.RegisterId equals currentRegister.Id
                            where sale.Id == saleId && sourceRegister.StoreId == currentRegister.StoreId
                            select new { Sale = sale, SourceRegisterId = sourceRegister.Id }).SingleOrDefaultAsync(cancellationToken);
        if (result is null) return null;
        if (result.SourceRegisterId != authorization.RegisterId && !authorization.User.IsAdministrator)
            throw new InvalidOperationException("Sólo un administrador puede devolver ventas de otra caja.");
        return new SaleAtRegister(result.Sale, result.SourceRegisterId);
    }

    private static decimal CalculateReturnAmount(SaleLineRecord soldLine, decimal previouslyReturnedQuantity, decimal previouslyReturnedAmount, decimal quantity)
    {
        var totalQuantity = soldLine.Quantity;
        if (previouslyReturnedQuantity + quantity >= totalQuantity)
            return decimal.Round(soldLine.LineTotal - previouslyReturnedAmount, 2, MidpointRounding.AwayFromZero);
        return decimal.Round(soldLine.LineTotal * quantity / totalQuantity, 2, MidpointRounding.AwayFromZero);
    }

    private sealed record RegisterAuthorization(UserRecord User, Guid RegisterId);
    private sealed record SaleAtRegister(SaleRecord Sale, Guid SourceRegisterId);
    private sealed record InventoryReplenishment(Guid SoldProductId, Guid ProductId, decimal Quantity);
}
