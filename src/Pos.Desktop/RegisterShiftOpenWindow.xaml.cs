using System.Windows;

namespace Pos.Desktop;

public partial class RegisterShiftOpenWindow : Window
{
    public bool ChangeUserRequested { get; private set; }

    public RegisterShiftOpenWindow(string openedBy, DateTimeOffset openedAtUtc)
    {
        InitializeComponent();
        OwnerText.Text = $"Turno abierto por: {openedBy}";
        OpenedAtText.Text = $"Abierto desde: {openedAtUtc.LocalDateTime:dd/MM/yyyy HH:mm}";
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnChangeUserClick(object sender, RoutedEventArgs e)
    {
        ChangeUserRequested = true;
        DialogResult = true;
    }
}
