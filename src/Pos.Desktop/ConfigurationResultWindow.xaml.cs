using System.Windows;

namespace Pos.Desktop;

public partial class ConfigurationResultWindow : Window
{
    public ConfigurationResultWindow(string title, string message, bool restartRequired)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        RestartText.Text = restartRequired
            ? "Para terminar de aplicar este cambio, cierra y vuelve a abrir JetVenta."
            : "El cambio se aplicará en las siguientes operaciones. No es necesario reiniciar JetVenta.";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
