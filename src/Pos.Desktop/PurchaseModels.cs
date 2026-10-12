using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Pos.Desktop;

/// <summary>Producto tal como lo devuelve /api/products/search.</summary>
public sealed record PurchaseProduct(Guid Id, string Code, string Description, decimal Price, decimal Cost, decimal Stock, string UnitOfMeasure, decimal ProfitPercent);

public sealed record PurchaseSupplier(Guid? Id, string Name, string? Phone, string? Email)
{
    public static PurchaseSupplier None { get; } = new(null, "Sin proveedor", null, null);
    public string DisplayText => Name;
}

public sealed record PurchasePricingDefaults(decimal DefaultProfitPercent, bool AutoPriceWithProfit);
public sealed record MobilePurchaseRequestQueueItem(Guid Id, string Supplier, string CreatedBy, string DeviceName, decimal Total, int LineCount, DateTimeOffset CreatedAtUtc, bool ProposedPayFromCashRegister, decimal? ProposedCashWithdrawn)
{
    public string Summary => $"{CreatedAtUtc.ToLocalTime():g} · {CreatedBy} · {DeviceName} · {LineCount} producto(s) · {Total:C2}";
}
public sealed record MobilePurchaseRequestLineReview(Guid ProductId, string Code, string Description, string UnitOfMeasure, decimal Quantity, decimal UnitCost, decimal LineTotal, decimal Price, decimal Cost, decimal Stock, decimal ProfitPercent);
public sealed record MobilePurchaseRequestReview(Guid Id, Guid OperationId, Guid? SupplierId, string Supplier, string CreatedBy, string DeviceName, decimal Total, DateTimeOffset CreatedAtUtc, bool ProposedPayFromCashRegister, decimal? ProposedCashWithdrawn, IReadOnlyList<MobilePurchaseRequestLineReview> Lines);
public sealed record MobilePurchaseApprovalResult(Guid RequestId, PurchaseReceiveApiResult Purchase);
public sealed record PurchaseReceiveApiResult(Guid PurchaseId, Guid OperationId, decimal Total, bool Existing, bool PaidFromCashRegister, decimal? CashWithdrawn);

/// <summary>Partida de la lista de recepción. Piezas y precio del proveedor se editan en la lista.</summary>
public sealed class PurchaseReceiptLine : INotifyPropertyChanged
{
    private string _quantityText;
    private string _unitCostText;

    public PurchaseReceiptLine(PurchaseProduct product)
    {
        Product = product;
        _quantityText = "1";
        _unitCostText = product.Cost.ToString("0.00", CultureInfo.CurrentCulture);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Changed;

    public PurchaseProduct Product { get; }
    public string Code => Product.Code;
    public string Description => Product.Description;
    public string UnitOfMeasure => Product.UnitOfMeasure;
    public string CurrentCostText => Product.Cost.ToString("C2", CultureInfo.CurrentCulture);
    public string CurrentPriceText => Product.Price > 0m ? Product.Price.ToString("C2", CultureInfo.CurrentCulture) : "Sin precio";
    public string ProfitPercentText => Product.ProfitPercent > 0m ? $"{Product.ProfitPercent:0.##}%" : "Sin %";

    public string QuantityText
    {
        get => _quantityText;
        set { _quantityText = value ?? string.Empty; Notify(nameof(QuantityText)); NotifyComputed(); }
    }

    public string UnitCostText
    {
        get => _unitCostText;
        set { _unitCostText = value ?? string.Empty; Notify(nameof(UnitCostText)); NotifyComputed(); }
    }

    public decimal? Quantity => PurchasePricing.TryParse(QuantityText, out var value) && value > 0m ? value : null;
    public decimal? UnitCost => PurchasePricing.TryParse(UnitCostText, out var value) && value >= 0m ? decimal.Round(value, 2, MidpointRounding.AwayFromZero) : null;
    public bool IsValid => Quantity.HasValue && UnitCost.HasValue;
    public decimal LineTotal => IsValid ? decimal.Round(Quantity!.Value * UnitCost!.Value, 2, MidpointRounding.AwayFromZero) : 0m;
    public string LineTotalText => IsValid ? LineTotal.ToString("C2", CultureInfo.CurrentCulture) : "Revisa";
    public bool CostChanged => UnitCost is decimal cost && cost != decimal.Round(Product.Cost, 2, MidpointRounding.AwayFromZero);
    public string CostChangeText => !UnitCost.HasValue ? string.Empty : CostChanged ? (UnitCost > Product.Cost ? "▲ subió" : "▼ bajó") : string.Empty;

    public void AddPieces(decimal pieces)
    {
        var current = Quantity ?? 0m;
        QuantityText = (current + pieces).ToString("0.###", CultureInfo.CurrentCulture);
    }

    private void NotifyComputed()
    {
        foreach (var name in new[] { nameof(Quantity), nameof(UnitCost), nameof(IsValid), nameof(LineTotal), nameof(LineTotalText), nameof(CostChanged), nameof(CostChangeText) }) Notify(name);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class PurchasePricing
{
    public static bool TryParse(string? value, out decimal result)
    {
        var text = (value ?? string.Empty).Trim().Replace("$", string.Empty).Replace("%", string.Empty).Trim();
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out result)
            || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>Mismo cálculo que la API: promedio ponderado entre la existencia actual y lo recibido.</summary>
    public static decimal EstimatedAverageCost(PurchaseProduct product, decimal quantity, decimal unitCost)
    {
        var after = product.Stock + quantity;
        return after == 0m ? 0m : decimal.Round(((product.Stock * product.Cost) + (quantity * unitCost)) / after, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal PriceWithPercent(decimal cost, decimal percent) =>
        decimal.Round(cost * (1m + decimal.Round(percent, 2, MidpointRounding.AwayFromZero) / 100m), 2, MidpointRounding.AwayFromZero);

    /// <summary>Convierte una respuesta de error de la API en un mensaje legible.</summary>
    public static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String) return message.GetString() ?? body;
                if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                {
                    var messages = errors.EnumerateObject().SelectMany(item => item.Value.ValueKind == JsonValueKind.Array ? item.Value.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : null) : Enumerable.Empty<string?>()).Where(item => !string.IsNullOrWhiteSpace(item));
                    var joined = string.Join(" ", messages);
                    if (joined.Length > 0) return joined;
                }
                if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String) return title.GetString() ?? body;
            }
        }
        catch (JsonException) { }
        return string.IsNullOrWhiteSpace(body) ? $"La API respondió {(int)response.StatusCode}." : body;
    }
}
