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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var settings = ApiClient.CashDrawer;
        LoadPrinters(settings.PrinterName);
        EnabledCheck.IsChecked = settings.Enabled;
        PrinterBox.SelectedItem = PrinterBox.Items.Cast<string>().FirstOrDefault(item => string.Equals(item, settings.PrinterName, StringComparison.OrdinalIgnoreCase));
        PortBox.SelectedItem = PortBox.Items.OfType<ComboBoxItem>().FirstOrDefault(candidate => string.Equals(candidate.Content?.ToString(), settings.Port, StringComparison.OrdinalIgnoreCase));
        var item = ModelBox.Items.OfType<ComboBoxItem>().FirstOrDefault(candidate => string.Equals(candidate.Tag?.ToString(), settings.Model, StringComparison.OrdinalIgnoreCase));
        if (item is not null) ModelBox.SelectedItem = item;
        StatusText.Text = "La configuración del cajón se guarda sólo en esta computadora.";
        _loaded = true;
        UpdateEnabledState();
    }

    private void LoadPrinters(string? preferredPrinter)
    {
        try
        {
            var names = TicketWindowsPrinter.GetInstalledPrinters().ToList();
            if (!string.IsNullOrWhiteSpace(preferredPrinter) && !names.Contains(preferredPrinter, StringComparer.OrdinalIgnoreCase)) names.Insert(0, preferredPrinter);
            PrinterBox.ItemsSource = names;
            if (names.Count > 0) PrinterBox.SelectedIndex = 0;
            StatusText.Text = names.Count == 0 ? "Windows no reportó impresoras instaladas." : $"Windows reportó {names.Count} impresora(s).";
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

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryRead(out var command)) return;
        try
        {
            ApiClient.SetCashDrawerProfile(new CashDrawerProfile(command.Enabled, command.PrinterName, command.Model, command.Port));
            StatusText.Text = command.Enabled ? $"Configuración de esta computadora guardada para {command.PrinterName}." : "Cajón desactivado en esta computadora.";
            ConfigurationFeedback.ShowSavedAndClose(this, "Cajón de dinero", StatusText.Text);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"No se pudo guardar la configuración: {exception.Message}";
            OperationFeedback.Show(this, "Cajón de dinero", StatusText.Text, OperationResultKind.Error);
        }
    }

    private void OnTestClick(object sender, RoutedEventArgs e)
    {
        if (!TryRead(out var command)) return;
        if (!command.Enabled)
        {
            StatusText.Text = "Activa el cajón para ejecutar una prueba.";
            OperationFeedback.Show(this, "Cajón de dinero", StatusText.Text, OperationResultKind.Warning);
            return;
        }
        try
        {
            TicketWindowsPrinter.OpenCashDrawer(command.PrinterName, command.Model);
            StatusText.Text = $"Pulso de apertura enviado a {command.PrinterName}.";
            OperationFeedback.Show(this, "Prueba de cajón", StatusText.Text, OperationResultKind.Success);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"No se pudo abrir el cajón: {exception.Message}";
            OperationFeedback.Show(this, "Prueba de cajón", StatusText.Text, OperationResultKind.Error);
        }
    }

    private bool TryRead(out CashDrawerCommand command)
    {
        var model = (ModelBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "PrinterPulse";
        command = new CashDrawerCommand(EnabledCheck.IsChecked == true, PrinterBox.SelectedItem as string ?? string.Empty, model, PortBox.Text);
        if (command.Enabled && string.IsNullOrWhiteSpace(command.PrinterName))
        {
            StatusText.Text = "Selecciona la impresora de Windows conectada al cajón.";
            OperationFeedback.Show(this, "Cajón de dinero", StatusText.Text, OperationResultKind.Warning);
            return false;
        }
        return true;
    }

    private sealed record CashDrawerCommand(bool Enabled, string PrinterName, string Model, string Port);
}
