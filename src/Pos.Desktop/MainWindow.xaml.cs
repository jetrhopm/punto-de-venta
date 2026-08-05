using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Pos.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnNavigateClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string section })
        {
            NavigateTo(section);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var section = e.Key switch
        {
            Key.F1 => "Ventas",
            Key.F2 => "Clientes",
            Key.F3 => "Productos",
            Key.F4 => "Inventario",
            _ => null
        };

        if (section is not null)
        {
            NavigateTo(section);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F12)
        {
            ShowPendingFeature("Cobro");
            e.Handled = true;
        }
    }

    private void OnSalesActionClick(object sender, RoutedEventArgs e) =>
        ShowPendingFeature("Accion de ventas");

    private void OnChargeClick(object sender, RoutedEventArgs e) =>
        ShowPendingFeature("Cobro");

    private void OnMinimizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void NavigateTo(string section)
    {
        CurrentSectionText.Text = section;
        WorkspaceTitleText.Text = section;
        WorkspaceDetailText.Text = $"El modulo {section.ToLowerInvariant()} se implementara en un incremento posterior de la Fase 1.";
        StatusText.Text = $"Navegacion activa: {section}.";
    }

    private void ShowPendingFeature(string feature)
    {
        StatusText.Text = $"{feature} requiere configuracion de tienda, usuario, turno y PostgreSQL. Aun no esta habilitado.";
    }
}
