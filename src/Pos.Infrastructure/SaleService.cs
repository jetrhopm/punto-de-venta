using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record SaleLineCommand(Guid ProductId, decimal Quantity, bool UseWholesale = false);
public sealed record CompleteSaleCommand(Guid OperationId, IReadOnlyList<SaleLineCommand> Lines, decimal CashReceived, Guid? CustomerId = null, string PaymentMethod = "Cash", decimal CardAmount = 0m, decimal TransferAmount = 0m, Guid? DraftId = null, bool PrintRequested = true);
public sealed record CompleteSaleResult(Guid SaleId, Guid OperationId, decimal Total, decimal CashReceived, decimal Change, bool Existing);

public sealed class SaleService(PosDbContext database, PromotionService promotions, KitService kits)
{
    public async Task<CompleteSaleResult?> CompleteAsync(string accessToken, CompleteSaleCommand command, CancellationToken cancellationToken)
    {
        if (command.OperationId == Guid.Empty || command.Lines.Count == 0 || command.CashReceived < 0m || command.CardAmount < 0m || command.TransferAmount < 0m || command.PaymentMethod is not ("Cash" or "Card" or "Transfer" or "Mixed" or "Credit")) throw new ArgumentException("La operacion, las partidas y la forma de pago son obligatorias.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken ?? string.Empty)));
        var session = await database.Sessions.SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        if (!user.IsAdministrator && !await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "Sell", cancellationToken)) return null;
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        if (command.PaymentMethod == "Credit" && !store.CreditSalesEnabled) throw new InvalidOperationException("Las ventas a crédito están deshabilitadas en Opciones habilitadas.");
        if (command.PaymentMethod == "Credit" && !user.IsAdministrator && !await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "SellOnCredit", cancellationToken)) throw new UnauthorizedAccessException("El usuario no tiene permiso para cobrar a credito.");
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await database.Sales.AsNoTracking().SingleOrDefaultAsync(sale => sale.OperationId == command.OperationId, cancellationToken);
        if (existing is not null) return new CompleteSaleResult(existing.Id, existing.OperationId, existing.Total, 0m, 0m, true);
        var shift = await database.Shifts.SingleOrDefaultAsync(item => item.UserId == user.Id && item.Status == "Open", cancellationToken) ?? throw new InvalidOperationException("El usuario no tiene un turno abierto.");
        SaleDraftRecord? draft = null;
        IReadOnlyDictionary<Guid, SaleDraftLineRecord>? draftLines = null;
        if (command.DraftId is not null)
        {
            draft = await database.SaleDrafts.Include(item => item.Lines).SingleOrDefaultAsync(item => item.Id == command.DraftId && item.ShiftId == shift.Id && item.UserId == user.Id && item.Status == "Open", cancellationToken)
                ?? throw new InvalidOperationException("El ticket en atención no existe o ya fue finalizado.");
            if (draft.OperationId != command.OperationId) throw new InvalidOperationException("El identificador del ticket no coincide con la operación de cobro.");
            draftLines = draft.Lines.ToDictionary(item => item.ProductId);
            if (draftLines.Count != command.Lines.Count || command.Lines.Any(line => !draftLines.TryGetValue(line.ProductId, out var draftLine) || draftLine.Quantity != decimal.Round(line.Quantity, 3)))
                throw new InvalidOperationException("Las partidas del ticket cambiaron. Espera a que se guarden antes de cobrar.");
        }
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
            var stockBefore = product.Stock;
            var unitPrice = product.Price;
            var originalLine = command.Lines.SingleOrDefault(item => item.ProductId == line.ProductId);
            var requestedQuantity = originalLine?.Quantity ?? requested;
            unitPrice = originalLine is not null && originalLine.UseWholesale && product.WholesalePrice > 0m && requestedQuantity >= product.WholesaleMinimumQuantity ? product.WholesalePrice : product.Price;
            // El borrador solo conserva la cantidad y la composición del ticket. El precio autoritativo se recalcula al cobrar para aplicar promociones vigentes.
            var promotionCalculation = await promotions.CalculateAsync(product.Id, unitPrice, DateTimeOffset.UtcNow, cancellationToken, requestedQuantity);
            unitPrice = promotionCalculation.UnitPrice;
            var total = originalLine is null ? 0m : promotionCalculation.Total;
            if (store.InventoryEnabled && !product.IsKit) product.Stock -= requested;
            if (originalLine is not null) lines.Add(new SaleLineRecord { Id = Guid.NewGuid(), ProductId = product.Id, Quantity = requestedQuantity, UnitPrice = unitPrice, LineTotal = total, StockBefore = stockBefore, StockAfter = product.Stock });
        }
        var totalSale = RoundSaleAmount(lines.Sum(line => line.LineTotal), store);
        ValidatePaymentMethodEnabled(store, command, totalSale);
        var mercadoPagoAmount = command.PaymentMethod == "Card" ? totalSale : command.PaymentMethod == "Mixed" ? command.CardAmount : 0m;
        if (store.MercadoPagoEnabled && mercadoPagoAmount > 0m)
        {
            var pointPayment = await database.MercadoPagoOrders.AsNoTracking().SingleOrDefaultAsync(item => item.OperationId == command.OperationId, cancellationToken);
            if (pointPayment is null || pointPayment.Status != "Approved" || pointPayment.Amount != mercadoPagoAmount)
                throw new InvalidOperationException("El cobro con Mercado Pago todavía no está aprobado o no coincide con el total de la venta.");
        }
        CustomerRecord? customer = null;
        var currentCredit = 0m;
        if (command.PaymentMethod == "Credit")
        {
            if (command.CustomerId is null) throw new InvalidOperationException("Una venta a credito requiere cliente.");
            customer = await database.Customers.SingleOrDefaultAsync(item => item.Id == command.CustomerId && item.IsActive && item.CreditEnabled, cancellationToken) ?? throw new InvalidOperationException("El cliente no existe o no tiene credito habilitado.");
            currentCredit = await database.CreditTransactions.Where(item => item.CustomerId == customer.Id).SumAsync(item => item.Amount, cancellationToken);
            if (currentCredit + totalSale > customer.CreditLimit) throw new InvalidOperationException("La venta excede el limite de credito del cliente.");
        }
        else
        {
            var cardAmount = decimal.Round(command.PaymentMethod == "Card" ? totalSale : command.CardAmount, 2, MidpointRounding.AwayFromZero);
            var transferAmount = decimal.Round(command.PaymentMethod == "Transfer" ? totalSale : command.TransferAmount, 2, MidpointRounding.AwayFromZero);
            var cashAmount = decimal.Round(totalSale - cardAmount - transferAmount, 2, MidpointRounding.AwayFromZero);
            if (cashAmount < 0m || decimal.Round(cashAmount + cardAmount + transferAmount, 2) != totalSale) throw new InvalidOperationException("Los importes de pago no cubren exactamente el total.");
            if (command.CashReceived < cashAmount) throw new InvalidOperationException("El efectivo recibido es insuficiente.");
        }
        // El consecutivo se reserva dentro de la misma transacción serializable de la venta.
        // Una venta que se revierte conserva su folio y los folios omitidos no se reutilizan.
        var folio = store.NextSaleFolio;
        store.NextSaleFolio++;
        var sale = new SaleRecord { Id = Guid.NewGuid(), OperationId = command.OperationId, ShiftId = shift.Id, CustomerId = command.CustomerId, Folio = folio, Total = totalSale, CreatedAtUtc = DateTimeOffset.UtcNow };
        foreach (var line in lines) { line.SaleId = sale.Id; if (store.InventoryEnabled && !products[line.ProductId].IsKit) database.InventoryMovements.Add(new InventoryMovementRecord { Id = Guid.NewGuid(), ProductId = line.ProductId, SaleId = sale.Id, UserId = user.Id, OperationId = command.OperationId, Quantity = -line.Quantity, StockBefore = line.StockBefore, StockAfter = line.StockAfter, CreatedAtUtc = sale.CreatedAtUtc }); }
        foreach (var component in expanded.Where(item => !command.Lines.Any(line => line.ProductId == item.Key))) { var product = products[component.Key]; var before = product.Stock; if (store.InventoryEnabled) { product.Stock -= component.Value; database.InventoryMovements.Add(new InventoryMovementRecord { Id = Guid.NewGuid(), ProductId = product.Id, SaleId = sale.Id, UserId = user.Id, OperationId = command.OperationId, Quantity = -component.Value, StockBefore = before, StockAfter = product.Stock, Reason = "KitSale", CreatedAtUtc = sale.CreatedAtUtc }); } }
        var cashAmountToRecord = command.PaymentMethod switch { "Card" or "Transfer" or "Credit" => 0m, _ => decimal.Round(totalSale - (command.PaymentMethod == "Mixed" ? command.CardAmount + command.TransferAmount : 0m), 2, MidpointRounding.AwayFromZero) };
        var cardAmountToRecord = command.PaymentMethod == "Card" ? totalSale : decimal.Round(command.CardAmount, 2, MidpointRounding.AwayFromZero);
        var transferAmountToRecord = command.PaymentMethod == "Transfer" ? totalSale : decimal.Round(command.TransferAmount, 2, MidpointRounding.AwayFromZero);
        var change = command.PaymentMethod is "Cash" or "Mixed" ? decimal.Round(command.CashReceived - cashAmountToRecord, 2, MidpointRounding.AwayFromZero) : 0m;
        database.Sales.Add(sale); database.SaleLines.AddRange(lines);
        if (cashAmountToRecord > 0m) database.Payments.Add(new PaymentRecord { Id = Guid.NewGuid(), SaleId = sale.Id, Method = "Cash", Amount = cashAmountToRecord, Received = command.CashReceived, Change = change });
        if (cardAmountToRecord > 0m) database.Payments.Add(new PaymentRecord { Id = Guid.NewGuid(), SaleId = sale.Id, Method = "Card", Amount = cardAmountToRecord, Received = cardAmountToRecord, Change = 0m });
        if (transferAmountToRecord > 0m) database.Payments.Add(new PaymentRecord { Id = Guid.NewGuid(), SaleId = sale.Id, Method = "Transfer", Amount = transferAmountToRecord, Received = transferAmountToRecord, Change = 0m });
        if (command.PaymentMethod == "Credit") database.Payments.Add(new PaymentRecord { Id = Guid.NewGuid(), SaleId = sale.Id, Method = "Credit", Amount = totalSale, Received = 0m, Change = 0m });
        if (customer is not null) database.CreditTransactions.Add(new CreditTransactionRecord { Id = Guid.NewGuid(), CustomerId = customer.Id, SaleId = sale.Id, UserId = user.Id, OperationId = command.OperationId, Type = "Sale", Amount = totalSale, BalanceBefore = currentCredit, BalanceAfter = currentCredit + totalSale, Reason = "Venta a credito", CreatedAtUtc = sale.CreatedAtUtc });
        if (command.PrintRequested)
            database.PrintJobs.Add(new PrintJobRecord { Id = Guid.NewGuid(), SaleId = sale.Id, PrintRequested = true, CreatedAtUtc = sale.CreatedAtUtc });
        if (draft is not null)
        {
            draft.Status = "Completed";
            draft.CompletedAtUtc = sale.CreatedAtUtc;
            draft.UpdatedAtUtc = sale.CreatedAtUtc;
        }
        await database.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new CompleteSaleResult(sale.Id, sale.OperationId, totalSale, command.CashReceived, change, false);
    }

    private static void ValidatePaymentMethodEnabled(StoreRecord store, CompleteSaleCommand command, decimal total)
    {
        var card = command.PaymentMethod == "Card" ? total : decimal.Round(command.CardAmount, 2, MidpointRounding.AwayFromZero);
        var transfer = command.PaymentMethod == "Transfer" ? total : decimal.Round(command.TransferAmount, 2, MidpointRounding.AwayFromZero);
        var cash = decimal.Round(total - card - transfer, 2, MidpointRounding.AwayFromZero);

        if (command.PaymentMethod == "Credit" && !store.CreditPaymentEnabled) throw new InvalidOperationException("El pago a crédito está desactivado en esta tienda.");
        if (command.PaymentMethod != "Credit" && cash > 0m && !store.CashPaymentEnabled) throw new InvalidOperationException("El pago en efectivo está desactivado en esta tienda.");
        if (card > 0m && !store.CardPaymentEnabled) throw new InvalidOperationException("El pago con tarjeta está desactivado en esta tienda.");
        if (transfer > 0m && !store.TransferPaymentEnabled) throw new InvalidOperationException("El pago por transferencia está desactivado en esta tienda.");
    }

    private static decimal RoundSaleAmount(decimal amount, StoreRecord store)
    {
        var value = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (!store.RoundSaleAmounts) return value;
        return store.RoundingMode.Equals("Whole", StringComparison.OrdinalIgnoreCase)
            ? decimal.Ceiling(value)
            : decimal.Ceiling(value * 10m) / 10m;
    }
}
