using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Pos.Desktop;

public partial class ShiftWindow : Window
{
    public decimal? InitialCash { get; private set; }

    public ShiftWindow()
    {
        InitializeComponent();
        InitialCashTextBox.Text = "0.00";
        InitialCashTextBox.Focus();
        InitialCashTextBox.SelectAll();
    }

    public void ShowAlreadyOpen(decimal initialCash, DateTimeOffset openedAt)
    {
        Title = "Caja abierta";
        TitleText.Text = "Caja abierta";
        DescriptionText.Text = $"Esta caja ya tiene un turno abierto desde {openedAt.LocalDateTime:g}.";
        InitialCashLabel.Text = "Fondo inicial registrado";
        InitialCashTextBox.Text = initialCash.ToString("0.00", CultureInfo.InvariantCulture);
        InitialCashTextBox.IsReadOnly = true;
        ConfirmButton.Content = "Continuar";
        MessageText.Text = "Para cambiar el fondo inicial, primero realiza el corte y vuelve a abrir caja.";
        MessageText.Foreground = Brushes.Firebrick;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (InitialCashTextBox.IsReadOnly)
        {
            DialogResult = true;
            return;
        }

        if (!decimal.TryParse(InitialCashTextBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount < 0m)
        {
            MessageText.Text = "Escribe un fondo inicial valido.";
            return;
        }

        InitialCash = decimal.Round(amount, 2);
        DialogResult = true;
    }
}
