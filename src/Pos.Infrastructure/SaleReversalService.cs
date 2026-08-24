using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record CancelSaleCommand(Guid OperationId, Guid SaleId, string Reason);
public sealed record CancelSaleResult(Guid SaleId, Guid OperationId, decimal Amount, bool Existing);

public sealed class SaleReversalService(PosDbContext database, KitService kits)
{
    public async Task<CancelSaleResult?> CancelAsync(string token, CancelSaleCommand command, CancellationToken cancellationToken)
    {
        if (command.OperationId == Guid.Empty || command.SaleId == Guid.Empty || string.IsNullOrWhiteSpace(command.Reason)) throw new ArgumentException("La cancelacion requiere operacion, venta y motivo.");
        var user = await GetAuthorizedUserAsync(token, cancellationToken);
        if (user is null) return null;
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await database.SaleReversals.AsNoTracking().SingleOrDefaultAsync(item => item.OperationId == command.OperationId, cancellationToken);
        if (existing is not null) return new CancelSaleResult(existing.SaleId, existing.OperationId, 0m, true);
        var sale = await database.Sales.SingleOrDefaultAsync(item => item.Id == command.SaleId, cancellationToken) ?? throw new KeyNotFoundException("Venta no encontrada.");
        if (sale.Status != "Completed") throw new InvalidOperationException("La venta ya no esta activa.");
        if (await database.SaleReversals.AnyAsync(item => item.SaleId == sale.Id, cancellationToken)) throw new InvalidOperationException("La venta ya fue cancelada.");
        var shift = await database.Shifts.SingleOrDefaultAsync(item => item.UserId == user.Id && item.Status == "Open", cancellationToken) ?? throw new InvalidOperationException("El usuario no tiene un turno abierto.");
        var lines = await database.SaleLines.Where(item => item.SaleId == sale.Id).ToListAsync(cancellationToken);
        foreach (var line in lines)
        {
            var parts = await kits.ExpandAsync(line.ProductId, line.Quantity, cancellationToken) ?? throw new KeyNotFoundException("Producto de la venta no encontrado.");
            foreach (var part in parts) { var product = await database.Products.SingleAsync(item => item.Id == part.ProductId, cancellationToken); if (product.IsTemporary) continue; var before = product.Stock; product.Stock = decimal.Round(before + part.Quantity, 3, MidpointRounding.AwayFromZero); database.InventoryMovements.Add(new InventoryMovementRecord { Id = Guid.NewGuid(), ProductId = product.Id, SaleId = sale.Id, UserId = user.Id, OperationId = command.OperationId, Quantity = part.Quantity, StockBefore = before, StockAfter = product.Stock, Reason = line.ProductId == part.ProductId ? "SaleCancellation" : "KitCancellation", CreatedAtUtc = DateTimeOffset.UtcNow }); }
        }
        var payment = await database.Payments.SingleOrDefaultAsync(item => item.SaleId == sale.Id, cancellationToken);
        if (payment?.Method == "Cash") database.CashMovements.Add(new CashMovementRecord { Id = Guid.NewGuid(), ShiftId = shift.Id, Type = "Out", Amount = payment.Amount, Reason = $"Cancelacion de venta {sale.Id}", CreatedAtUtc = DateTimeOffset.UtcNow });
        if (payment?.Method == "Credit" && sale.CustomerId is not null)
        {
            var balance = await database.CreditTransactions.Where(item => item.CustomerId == sale.CustomerId).SumAsync(item => item.Amount, cancellationToken);
            if (balance < sale.Total) throw new InvalidOperationException("No se puede cancelar: el cliente ya tiene abonos relacionados por un importe superior al saldo disponible.");
            database.CreditTransactions.Add(new CreditTransactionRecord { Id = Guid.NewGuid(), CustomerId = sale.CustomerId.Value, SaleId = sale.Id, UserId = user.Id, OperationId = command.OperationId, Type = "SaleCancellation", Amount = -sale.Total, BalanceBefore = balance, BalanceAfter = balance - sale.Total, Reason = command.Reason.Trim(), CreatedAtUtc = DateTimeOffset.UtcNow });
        }
        sale.Status = "Cancelled";
        database.SaleReversals.Add(new SaleReversalRecord { Id = Guid.NewGuid(), SaleId = sale.Id, UserId = user.Id, OperationId = command.OperationId, Reason = command.Reason.Trim(), CreatedAtUtc = DateTimeOffset.UtcNow });
        await database.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new CancelSaleResult(sale.Id, command.OperationId, sale.Total, false);
    }

    private async Task<UserRecord?> GetAuthorizedUserAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "CancelSales", cancellationToken) ? user : null;
    }
}
