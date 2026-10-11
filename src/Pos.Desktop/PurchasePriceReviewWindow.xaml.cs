using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace Pos.Desktop;

public enum PurchasePriceDecision { None, ApplyPercent, KeepPrice, ManualPrice }

/// <summary>
/// Decisión de precio de una partida. Reglas (acordadas 2026-10-10):
/// 1) con % propio se sugiere costo promedio × (1 + %) y el usuario decide actualizar o conservar;
/// 2) sin % pero con precio, se conserva el precio;
/// 3) sin precio, se usa el % general del sistema;
/// 4) sin ninguno, el usuario captura % o precio.
/// Nunca se permite un precio de venta menor al precio del proveedor.
/// </summary>
public sealed class PurchasePriceReviewRow : INotifyPropertyChanged
{
    private PurchasePriceDecision _decision;
    private string _percentText;
    private string _manualPriceText = string.Empty;

    private PurchasePriceReviewRow(PurchaseReceiptLine line, string reason, PurchasePriceDecision decision, decimal? percent)
    {
        Line = line;
        Reason = reason;
        SupplierCost = line.UnitCost ?? 0m;
        EstimatedCost = PurchasePricing.EstimatedAverageCost(line.Product, line.Quantity ?? 0m, SupplierCost);
        _decision = decision;
        _percentText = percent is decimal value && value > 0m ? value.ToString("0.##", CultureInfo.CurrentCulture) : string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PurchaseReceiptLine Line { get; }
    public string GroupName { get; } = Guid.NewGuid().ToString("N");
    public string Description => Line.Description;
    public string Code => Line.Code;
    public string Reason { get; }
    public decimal SupplierCost { get; }
    public decimal EstimatedCost { get; }
    public decimal CurrentPrice => Line.Product.Price;
    public string CostSummary => string.Create(CultureInfo.CurrentCulture, $"Costo anterior {Line.Product.Cost:C2}  →  precio proveedor {SupplierCost:C2}  ·  costo promedio nuevo {EstimatedCost:C2}  ·  piezas {Line.Quantity:0.###}");
    public bool CanKeep => CurrentPrice > 0m && CurrentPrice >= SupplierCost;
    public string KeepText => CurrentPrice > 0m ? string.Create(CultureInfo.CurrentCulture, $"Seguir vendiendo a {CurrentPrice:C2}") : "Seguir vendiendo igual (no tiene precio)";

    public PurchasePriceDecision Decision
    {
        get => _decision;
        set { _decision = value; NotifyAll(); }
    }

    public bool IsApplyPercent { get => Decision == PurchasePriceDecision.ApplyPercent; set { if (value) Decision = PurchasePriceDecision.ApplyPercent; } }
    public bool IsKeepPrice { get => Decision == PurchasePriceDecision.KeepPrice; set { if (value) Decision = PurchasePriceDecision.KeepPrice; } }
    public bool IsManualPrice { get => Decision == PurchasePriceDecision.ManualPrice; set { if (value) Decision = PurchasePriceDecision.ManualPrice; } }

    public string PercentText
    {
        get => _percentText;
        set { _percentText = value ?? string.Empty; _decision = PurchasePriceDecision.ApplyPercent; NotifyAll(); }
    }

    public string ManualPriceText
    {
        get => _manualPriceText;
        set { _manualPriceText = value ?? string.Empty; _decision = PurchasePriceDecision.ManualPrice; NotifyAll(); }
    }

    public decimal? Percent => PurchasePricing.TryParse(PercentText, out var value) && value >= 0m && value <= 999.99m ? value : null;
    public decimal? SuggestedPrice => Percent is decimal percent ? PurchasePricing.PriceWithPercent(EstimatedCost, percent) : null;
    public string SuggestedPriceText => SuggestedPrice is decimal price ? string.Create(CultureInfo.CurrentCulture, $"→ {price:C2}") : "→ escribe el %";
    public decimal? ManualPrice => PurchasePricing.TryParse(ManualPriceText, out var value) ? decimal.Round(value, 2, MidpointRounding.AwayFromZero) : null;

    public decimal? FinalPrice => Decision switch
    {
        PurchasePriceDecision.ApplyPercent => SuggestedPrice,
        PurchasePriceDecision.KeepPrice => CurrentPrice,
        PurchasePriceDecision.ManualPrice => ManualPrice,
        _ => null
    };

    public string ValidationText
    {
        get
        {
            if (Decision == PurchasePriceDecision.None) return "Elige qué hacer con el precio de venta.";
            if (Decision == PurchasePriceDecision.ApplyPercent && Percent is null) return "Escribe un % de ganancia válido (0 a 999.99).";
            if (Decision == PurchasePriceDecision.ManualPrice && ManualPrice is null) return "Escribe el nuevo precio de venta.";
            if (FinalPrice is not decimal price || price <= 0m) return "El precio de venta debe ser mayor que cero.";
            if (price < SupplierCost) return string.Create(CultureInfo.CurrentCulture, $"El precio de venta ({price:C2}) no puede ser menor al precio del proveedor ({SupplierCost:C2}).");
            return string.Empty;
        }
    }

    public bool IsValid => ValidationText.Length == 0;
    public string ResultText => IsValid && FinalPrice is decimal price ? string.Create(CultureInfo.CurrentCulture, $"Precio de venta final: {price:C2}") : string.Empty;

    /// <summary>Valores que se envían a la API. Conservar no envía nada: el precio y el % del producto no cambian.</summary>
    public (decimal? SalePrice, decimal? ProfitPercentToApply) ToCommand() => Decision switch
    {
        PurchasePriceDecision.ApplyPercent => (null, Percent),
        PurchasePriceDecision.ManualPrice => (ManualPrice, null),
        _ => (null, null)
    };

    /// <summary>Partidas que requieren decisión o revisión de precio antes de recibir.</summary>
    public static List<PurchasePriceReviewRow> Build(IEnumerable<PurchaseReceiptLine> lines, decimal storeDefaultPercent)
    {
        var rows = new List<PurchasePriceReviewRow>();
        foreach (var line in lines.Where(item => item.IsValid))
        {
            var product = line.Product;
            var cost = line.UnitCost!.Value;
            var culture = CultureInfo.CurrentCulture;
            if (product.ProfitPercent > 0m)
            {
                if (product.Price <= 0m)
                    rows.Add(new(line, $"No tiene precio de venta: se calculará con su % de ganancia ({product.ProfitPercent:0.##}%).", PurchasePriceDecision.ApplyPercent, product.ProfitPercent));
                else if (line.CostChanged)
                    rows.Add(new(line, string.Create(culture, $"El precio del proveedor cambió ({product.Cost:C2} → {cost:C2}). ¿Ajustas el precio de venta con su % de ganancia o sigues vendiendo al mismo precio?"), PurchasePriceDecision.None, product.ProfitPercent));
                else if (product.Price < cost)
                    rows.Add(new(line, "El precio de venta actual es menor al precio del proveedor. Ajústalo antes de recibir.", PurchasePriceDecision.None, product.ProfitPercent));
            }
            else if (product.Price > 0m)
            {
                // Sin % propio se conserva el precio de venta; sólo se revisa si quedaría por debajo del proveedor.
                if (product.Price < cost)
                    rows.Add(new(line, "No tiene % de ganancia y su precio de venta es menor al precio del proveedor. Indica un % o un nuevo precio.", PurchasePriceDecision.None, storeDefaultPercent));
            }
            else if (storeDefaultPercent > 0m)
            {
                rows.Add(new(line, $"No tiene precio de venta ni % propio: se calculará con el % general del sistema ({storeDefaultPercent:0.##}%).", PurchasePriceDecision.ApplyPercent, storeDefaultPercent));
            }
            else
            {
                rows.Add(new(line, "No tiene precio de venta ni % de ganancia y el sistema no tiene % general. Indica el % de ganancia o el precio de venta.", PurchasePriceDecision.None, null));
            }
        }
        return rows;
    }

    private void NotifyAll()
    {
        foreach (var name in new[] { nameof(Decision), nameof(IsApplyPercent), nameof(IsKeepPrice), nameof(IsManualPrice), nameof(PercentText), nameof(ManualPriceText), nameof(Percent), nameof(SuggestedPrice), nameof(SuggestedPriceText), nameof(ManualPrice), nameof(FinalPrice), nameof(ValidationText), nameof(IsValid), nameof(ResultText) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public partial class PurchasePriceReviewWindow : Window
{
    public PurchasePriceReviewWindow(IReadOnlyList<PurchasePriceReviewRow> rows)
    {
        InitializeComponent();
        Rows = rows;
        RowsList.ItemsSource = rows;
        SummaryText.Text = rows.Count == 1 ? "1 producto necesita una decisión de precio." : $"{rows.Count} productos necesitan una decisión de precio.";
    }

    public IReadOnlyList<PurchasePriceReviewRow> Rows { get; }

    private void OnApplyAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in Rows.Where(item => item.Percent.HasValue)) row.Decision = PurchasePriceDecision.ApplyPercent;
        MessageText.Text = Rows.Any(item => !item.Percent.HasValue) ? "Algunos productos no tienen %; escríbelo o captura su precio." : string.Empty;
    }

    private void OnKeepAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in Rows.Where(item => item.CanKeep)) row.Decision = PurchasePriceDecision.KeepPrice;
        MessageText.Text = Rows.Any(item => !item.CanKeep) ? "Algunos productos no pueden conservar su precio (no tienen precio o es menor al del proveedor)." : string.Empty;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        var pending = Rows.Where(item => !item.IsValid).ToList();
        if (pending.Count > 0)
        {
            MessageText.Text = pending.Count == 1 ? $"Falta resolver: {pending[0].Description}. {pending[0].ValidationText}" : $"Faltan {pending.Count} productos por resolver. El primero: {pending[0].Description}.";
            RowsList.ScrollIntoView(pending[0]);
            return;
        }
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
    }
}
