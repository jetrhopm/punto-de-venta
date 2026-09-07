using System.Windows;

namespace Pos.Desktop;

public partial class RegisterShiftOpenWindow : Window
{
    public RegisterShiftOpenWindow(string openedBy, DateTimeOffset openedAtUtc)
    {
        InitializeComponent();
        OwnerText.Text = $"Turno abierto por: {openedBy}";
        OpenedAtText.Text = $"Abierto desde: {openedAtUtc.LocalDateTime:dd/MM/yyyy HH:mm}";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
