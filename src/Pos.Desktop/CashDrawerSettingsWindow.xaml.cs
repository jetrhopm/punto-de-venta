using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class CashDrawerSettingsWindow : Window
{
    private bool _loaded;

    public CashDrawerSettingsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadPrinters();
        try
        {
            var settings = await ApiClient.Client.GetFromJsonAsync<CashDrawerSettingsDto>("api/cash-drawer-settings");
            if (settings is not null)
            {
                EnabledCheck.IsChecked = settings.Enabled;
                PrinterBox.SelectedItem = PrinterBox.Items.Cast<string>().FirstOrDefault(item => string.Equals(item, settings.PrinterName, StringComparison.OrdinalIgnoreCase));
                PortBox.SelectedItem = PortBox.Items.OfType<ComboBoxItem>().FirstOrDefault(candidate => string.Equals(candidate.Content?.ToString(), settings.Port, StringComparison.OrdinalIgnoreCase));
                var item = ModelBox.Items.OfType<ComboBoxItem>().FirstOrDefault(candidate => string.Equals(candidate.Tag?.ToString(), settings.Model, StringComparison.OrdinalIgnoreCase));
                if (item is not null) ModelBox.SelectedItem = item;
            }
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo leer la configuración del cajón: {exception.Message}"; }
        _loaded = true;
        UpdateEnabledState();
    }

    private void LoadPrinters()
    {
        try
        {
            var names = TicketWindowsPrinter.GetInstalledPrinters();
            PrinterBox.ItemsSource = names;
            if (names.Length > 0) PrinterBox.SelectedIndex = 0;
            StatusText.Text = names.Length == 0 ? "Windows no reportó impresoras instaladas." : $"Windows reportó {names.Length} impresora(s).";
        }
        catch (Exception exception) { StatusText.Text = $"No se pudieron consultar las impresoras de Windows: {exception.Message}"; }
    }

    private void OnEnabledChanged(object sender, RoutedEventArgs e) => UpdateEnabledState();

    private void UpdateEnabledState()
    {
        var enabled = EnabledCheck.IsChecked == true;
        PrinterBox.IsEnabled = enabled; PortBox.IsEnabled = enabled; ModelBox.IsEnabled = enabled;
        if (_loaded && !enabled) StatusText.Text = "El cajón está desactivado. No se abrirá durante los cobros.";
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryRead(out var command)) return;
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync("api/cash-drawer-settings", command);
            if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; }
            StatusText.Text = command.Enabled ? $"Configuración guardada para {command.PrinterName}." : "Cajón desactivado.";
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo guardar la configuración: {exception.Message}"; }
    }

    private void OnTestClick(object sender, RoutedEventArgs e)
    {
        if (!TryRead(out var command)) return;
        if (!command.Enabled) { StatusText.Text = "Activa el cajón para ejecutar una prueba."; return; }
        try { TicketWindowsPrinter.OpenCashDrawer(command.PrinterName, command.Model); StatusText.Text = $"Pulso de apertura enviado a {command.PrinterName}."; }
        catch (Exception exception) { StatusText.Text = $"No se pudo abrir el cajón: {exception.Message}"; }
    }

    private bool TryRead(out CashDrawerCommand command)
    {
        var model = (ModelBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "PrinterPulse";
        command = new CashDrawerCommand(EnabledCheck.IsChecked == true, PrinterBox.SelectedItem as string ?? string.Empty, model, PortBox.Text);
        if (command.Enabled && string.IsNullOrWhiteSpace(command.PrinterName)) { StatusText.Text = "Selecciona la impresora de Windows conectada al cajón."; return false; }
        return true;
    }

    private sealed record CashDrawerSettingsDto(bool Enabled, string PrinterName, string Model, string Port);
    private sealed record CashDrawerCommand(bool Enabled, string PrinterName, string Model, string Port);
}
