using System.Globalization;
using System.Windows;

namespace Pos.Desktop;

public partial class ReceiveShiftHandoffWindow : Window
{
    private readonly decimal _deliveredCash;
    public decimal? ReceivedCash { get; private set; }

    public ReceiveShiftHandoffWindow(string fromUser, decimal expectedCash, decimal deliveredCash, decimal difference, DateTimeOffset deliveredAt)
    {
        InitializeComponent();
        _deliveredCash = decimal.Round(deliveredCash, 2);
        var culture = CultureInfo.GetCultureInfo("es-MX");
        DescriptionText.Text = $"{fromUser} entregó el turno el {deliveredAt.LocalDateTime:g}. Debes contar el efectivo físico antes de continuar.";
        DeliveredCashText.Text = _deliveredCash.ToString("C2", culture);
        ReceivedCashTextBox.Text = _deliveredCash.ToString("0.00", CultureInfo.InvariantCulture);
        ReceivedCashTextBox.TextChanged += (_, _) => UpdateDifference();
        ReceivedCashTextBox.Focus();
        ReceivedCashTextBox.SelectAll();
        UpdateDifference();
    }

    private void UpdateDifference()
    {
        if (!TryParse(ReceivedCashTextBox.Text, out var received)) { DifferenceText.Text = "Captura el efectivo que recibiste"; return; }
        var value = decimal.Round(received - _deliveredCash, 2);
        DifferenceText.Text = value switch { > 0m => $"Sobrante recibido: {value.ToString("C2", CultureInfo.GetCultureInfo("es-MX"))}", < 0m => $"Faltante recibido: {Math.Abs(value).ToString("C2", CultureInfo.GetCultureInfo("es-MX"))}", _ => "Sin diferencia" };
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (!TryParse(ReceivedCashTextBox.Text, out var received) || received < 0m) { MessageText.Text = "Captura el efectivo recibido válido."; return; }
        ReceivedCash = decimal.Round(received, 2);
        DialogResult = true;
    }

    private static bool TryParse(string? text, out decimal amount) => decimal.TryParse(text, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out amount) || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
}
