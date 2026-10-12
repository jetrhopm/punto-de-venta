using System.Globalization;

namespace JetVenta.Mobile;

public sealed record MobilePairRequest(string Code, string DeviceName);
public sealed record MobilePairResponse(Guid DeviceId, string DeviceToken);
public sealed record MobileLoginRequest(string UserName, string Password);
public sealed record MobileLoginResponse(string AccessToken, string DisplayName, DateTimeOffset ExpiresAtUtc, bool CanViewInventory, bool CanAdjustInventory);
public sealed record MobileCapabilities(bool CanViewInventory, bool CanAdjustInventory);
public sealed record MobileProduct(Guid Id, string Code, string Description, decimal Price, decimal Stock, string UnitOfMeasure, decimal MinimumStock, decimal MaximumStock)
{
    public string StockStatus => Stock <= 0m ? "Agotado" : MinimumStock > 0m && Stock <= MinimumStock ? "Inventario bajo" : "Disponible";
    public Color StockColor => Stock <= 0m ? Color.FromArgb("#C44343") : MinimumStock > 0m && Stock <= MinimumStock ? Color.FromArgb("#C58A09") : Color.FromArgb("#148A4B");
    public string StockDisplay => $"{Stock:0.###} {UnitOfMeasure}";
}
public sealed record MobileCountSession(Guid SessionId, DateTimeOffset StartedAtUtc);
public sealed record MobileCountRequest(Guid SessionId, Guid OperationId, Guid ProductId, decimal ExpectedStock, decimal CountedStock);
public sealed record MobileCountResult(Guid OperationId, Guid ProductId, string Code, string Description, decimal SystemStock, decimal CountedStock, decimal Difference, decimal CurrentStock, DateTimeOffset CreatedAtUtc)
{
    public string DifferenceDisplay => Difference == 0m ? "Sin diferencia" : $"{(Difference < 0m ? "Faltan" : "Sobran")} {Math.Abs(Difference):0.###}";
    public Color DifferenceColor => Difference == 0m ? Color.FromArgb("#148A4B") : Color.FromArgb("#C58A09");
    public string CountDisplay => $"Sistema {SystemStock:0.###} · Contado {CountedStock:0.###}";
}
public sealed record MobileCountHistoryRow(Guid SessionId, Guid ProductId, string Code, string Description, decimal SystemStock, decimal CountedStock, decimal Difference, string UserName, string DeviceName, DateTimeOffset CreatedAtUtc)
{
    public string DifferenceDisplay => Difference == 0m ? "Sin diferencia" : $"{(Difference < 0m ? "Faltan" : "Sobran")} {Math.Abs(Difference):0.###}";
    public Color DifferenceColor => Difference == 0m ? Color.FromArgb("#148A4B") : Color.FromArgb("#C58A09");
    public string CountDisplay => $"Sistema {SystemStock:0.###} · Contado {CountedStock:0.###}";
    public string AuditDisplay => $"{UserName} · {DeviceName} · {CreatedAtUtc.ToLocalTime():g}";
}
public sealed record MobilePurchaseSupplier(Guid? Id, string Name);
public sealed record MobilePurchaseProduct(Guid Id, string Code, string Description, string UnitOfMeasure);
public sealed record MobilePurchaseRequestLineCommand(Guid ProductId, decimal Quantity, decimal UnitCost);
public sealed record CreateMobilePurchaseRequestCommand(Guid OperationId, Guid? SupplierId, bool PayFromCashRegister, decimal? CashWithdrawn, IReadOnlyList<MobilePurchaseRequestLineCommand> Lines);
public sealed record MobilePurchaseRequestLineResult(Guid ProductId, string Code, string Description, string UnitOfMeasure, decimal Quantity, decimal UnitCost, decimal LineTotal);
public sealed record MobilePurchaseRequestResult(Guid Id, Guid OperationId, Guid? SupplierId, string Supplier, string CreatedBy, string DeviceName, string Status, decimal Total, bool ProposedPayFromCashRegister, decimal? ProposedCashWithdrawn, DateTimeOffset CreatedAtUtc, DateTimeOffset? ReviewedAtUtc, string? RejectionReason, IReadOnlyList<MobilePurchaseRequestLineResult> Lines);
public sealed class MobilePurchaseReceiptLine : System.ComponentModel.INotifyPropertyChanged
{
    private decimal _quantity;

    public MobilePurchaseReceiptLine(MobilePurchaseProduct product, decimal quantity, decimal unitCost)
    {
        ProductId = product.Id;
        Code = product.Code;
        Description = product.Description;
        UnitOfMeasure = product.UnitOfMeasure;
        _quantity = quantity;
        UnitCost = unitCost;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public Guid ProductId { get; }
    public string Code { get; }
    public string Description { get; }
    public string UnitOfMeasure { get; }
    public decimal Quantity
    {
        get => _quantity;
        set
        {
            if (_quantity == value) return;
            _quantity = value;
            PropertyChanged?.Invoke(this, new(nameof(Quantity)));
            PropertyChanged?.Invoke(this, new(nameof(LineTotal)));
            PropertyChanged?.Invoke(this, new(nameof(QuantityDisplay)));
            PropertyChanged?.Invoke(this, new(nameof(DetailDisplay)));
            PropertyChanged?.Invoke(this, new(nameof(TotalDisplay)));
        }
    }
    public decimal UnitCost { get; }
    public decimal LineTotal => decimal.Round(Quantity * UnitCost, 2, MidpointRounding.AwayFromZero);
    public string QuantityDisplay => $"{Quantity:0.###} {UnitOfMeasure}".Trim();
    public string DetailDisplay => $"{QuantityDisplay} · costo {UnitCostDisplay}";
    public string UnitCostDisplay => UnitCost.ToString("C2", CultureInfo.CurrentCulture);
    public string TotalDisplay => LineTotal.ToString("C2", CultureInfo.CurrentCulture);
}
