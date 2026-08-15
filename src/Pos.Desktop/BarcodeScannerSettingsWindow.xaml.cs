using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class BarcodeScannerSettingsWindow : Window
{
    public BarcodeScannerSettingsWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadProfile();
    }

    private void LoadProfile()
    {
        var profile = ApiClient.BarcodeScanner;
        KeyboardModeButton.IsChecked = profile.Mode == BarcodeScannerMode.Keyboard;
        SerialModeButton.IsChecked = profile.Mode == BarcodeScannerMode.Serial;
        DisabledModeButton.IsChecked = profile.Mode == BarcodeScannerMode.Disabled;
        BaudRateBox.Text = profile.BaudRate.ToString();
        TerminatorBox.Text = profile.Terminator;
        LoadPorts(profile.PortName);
        UpdateMode();
        StatusText.Text = profile.Mode == BarcodeScannerMode.Keyboard
            ? "Modo teclado activo. Conecta el lector y configura Enter como sufijo desde el manual del lector."
            : "Selecciona el modo y guarda la configuración de esta caja.";
    }

    private void LoadPorts(string? preferred = null)
    {
        var ports = SerialPort.GetPortNames().OrderBy(static port => port, StringComparer.OrdinalIgnoreCase).ToArray();
        PortBox.ItemsSource = ports;
        PortBox.SelectedItem = ports.FirstOrDefault(port => string.Equals(port, preferred, StringComparison.OrdinalIgnoreCase));
        if (PortBox.SelectedItem is null && ports.Length > 0) PortBox.SelectedIndex = 0;
    }

    private void OnRefreshPortsClick(object sender, RoutedEventArgs e)
    {
        LoadPorts(PortBox.SelectedItem as string);
        StatusText.Text = PortBox.Items.Count == 0 ? "Windows no detectó puertos COM. Conecta el lector o instala el controlador oficial del adaptador si el fabricante lo requiere." : $"Windows detectó {PortBox.Items.Count} puerto(s) COM.";
    }

    private void OnModeChanged(object sender, RoutedEventArgs e) => UpdateMode();

    private void UpdateMode()
    {
        if (SerialPanel is not null) SerialPanel.IsEnabled = SerialModeButton.IsChecked == true;
    }

    private BarcodeScannerProfile ReadProfile()
    {
        var mode = SerialModeButton.IsChecked == true ? BarcodeScannerMode.Serial : DisabledModeButton.IsChecked == true ? BarcodeScannerMode.Disabled : BarcodeScannerMode.Keyboard;
        var port = PortBox.SelectedItem as string ?? PortBox.Text;
        var baud = int.TryParse(BaudRateBox.Text, out var parsed) ? parsed : 9600;
        var terminator = TerminatorBox.Text;
        var profile = new BarcodeScannerProfile(mode, port, baud, terminator).Normalize();
        if (profile.Mode == BarcodeScannerMode.Serial && string.IsNullOrWhiteSpace(profile.PortName)) throw new InvalidOperationException("Selecciona un puerto COM para el lector serial.");
        return profile;
    }

    private void OnTestClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = ReadProfile();
            if (profile.Mode != BarcodeScannerMode.Serial) { StatusText.Text = "El modo teclado se prueba escaneando en Ventas. No requiere abrir un puerto COM."; return; }
            using var port = CreatePort(profile);
            port.Open();
            StatusText.Text = $"{profile.PortName} está disponible. Guarda para que JetVenta reciba lecturas seriales.";
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo abrir el puerto: {exception.Message}"; }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = ReadProfile();
            var status = BarcodeScannerService.ApplyProfile(profile);
            ApiClient.SetBarcodeScannerProfile(profile);
            StatusText.Text = status;
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private static SerialPort CreatePort(BarcodeScannerProfile profile) => new(profile.PortName!, profile.BaudRate, Parity.None, 8, StopBits.One)
    {
        Handshake = Handshake.None,
        ReadTimeout = 1500,
        NewLine = profile.Terminator switch { "CR" => "\r", "LF" => "\n", _ => "\r\n" }
    };
}
