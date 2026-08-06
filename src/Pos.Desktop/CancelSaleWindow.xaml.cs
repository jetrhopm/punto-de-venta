using System.Windows;

namespace Pos.Desktop;

public partial class CancelSaleWindow : Window
{
    public string Reason { get; private set; } = string.Empty;
    public CancelSaleWindow() { InitializeComponent(); ReasonTextBox.Focus(); }
    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ReasonTextBox.Text)) { MessageText.Text = "Escribe el motivo de cancelacion."; return; }
        Reason = ReasonTextBox.Text.Trim(); DialogResult = true;
    }
}
