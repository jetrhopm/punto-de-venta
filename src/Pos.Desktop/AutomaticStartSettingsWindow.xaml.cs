using System.Windows;

namespace Pos.Desktop;

public partial class AutomaticStartSettingsWindow : Window
{
    public AutomaticStartSettingsWindow()
    {
        InitializeComponent();
        EnabledCheck.IsChecked = AutomaticStartService.IsEnabled();
        StatusText.Text = EnabledCheck.IsChecked == true
            ? "JetVenta se abrirá al iniciar sesión en Windows."
            : "JetVenta no se abre automáticamente al iniciar Windows.";
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            AutomaticStartService.SetEnabled(EnabledCheck.IsChecked == true);
            StatusText.Text = EnabledCheck.IsChecked == true
                ? "Inicio automático activado correctamente."
                : "Inicio automático desactivado correctamente.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"No se pudo guardar el inicio automático: {exception.Message}";
        }
    }
}
