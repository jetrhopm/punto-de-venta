using System.Globalization;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class ScaleSettingsWindow : Window
{
    private bool _loaded;

    public ScaleSettingsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var settings = ApiClient.Scale;
        LoadPorts(settings.Port);
        EnabledCheck.IsChecked = settings.Enabled;
        SelectModel(settings.Model); SelectText(BaudRateBox, settings.BaudRate.ToString(CultureInfo.InvariantCulture)); SelectText(ParityBox, settings.Parity); SelectText(DataBitsBox, settings.DataBits.ToString(CultureInfo.InvariantCulture)); SelectText(StopBitsBox, settings.StopBits); SelectText(TerminatorBox, settings.Terminator); SelectText(UnitBox, settings.Unit); TimeoutBox.Text = settings.ReadTimeoutMs.ToString(CultureInfo.InvariantCulture);
        StatusText.Text = "Selecciona el puerto y prueba una lectura antes de guardar. La configuración se conserva sólo en esta computadora.";
        _loaded = true; UpdateEnabledState();
    }

    private void LoadPorts(string? preferred = null, bool showResult = false)
    {
        try
        {
            var ports = SerialPort.GetPortNames().OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList();
            if (!string.IsNullOrWhiteSpace(preferred) && !ports.Contains(preferred, StringComparer.OrdinalIgnoreCase)) ports.Insert(0, preferred);
            PortBox.ItemsSource = ports;
            if (ports.Count > 0) PortBox.SelectedItem = ports.FirstOrDefault(item => string.Equals(item, preferred, StringComparison.OrdinalIgnoreCase)) ?? ports[0];
            StatusText.Text = ports.Count == 0 ? "No se detectaron puertos COM. Conecta la báscula o instala el controlador del fabricante." : $"Windows reportó {ports.Count} puerto(s) COM.";
            if (showResult) OperationFeedback.Show(this, "Puertos de báscula", StatusText.Text, ports.Count == 0 ? OperationResultKind.Warning : OperationResultKind.Success);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"No se pudieron consultar los puertos COM: {exception.Message}";
            if (showResult) OperationFeedback.Show(this, "Puertos de báscula", StatusText.Text, OperationResultKind.Error);
        }
    }

    private void OnRefreshPortsClick(object sender, RoutedEventArgs e) => LoadPorts(PortBox.SelectedItem as string, showResult: true);
    private void OnEnabledChanged(object sender, RoutedEventArgs e) => UpdateEnabledState();
    private void UpdateEnabledState()
    {
        var enabled = EnabledCheck.IsChecked == true;
        PortBox.IsEnabled = enabled; BaudRateBox.IsEnabled = enabled; UnitBox.IsEnabled = enabled; ParityBox.IsEnabled = enabled; DataBitsBox.IsEnabled = enabled; StopBitsBox.IsEnabled = enabled; TerminatorBox.IsEnabled = enabled; TimeoutBox.IsEnabled = enabled;
        if (_loaded && !enabled) StatusText.Text = "La báscula está desactivada para esta caja.";
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryRead(out var command)) return;
        try
        {
            ApiClient.SetScaleProfile(command.ToProfile());
            StatusText.Text = command.Enabled ? $"Báscula guardada en {command.Port} para esta computadora." : "Báscula desactivada en esta computadora.";
            ConfigurationFeedback.ShowSavedAndClose(this, "Báscula", StatusText.Text);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"No se pudo guardar la configuración: {exception.Message}";
            OperationFeedback.Show(this, "Báscula", StatusText.Text, OperationResultKind.Error);
        }
    }

    private async void OnTestClick(object sender, RoutedEventArgs e)
    {
        if (!TryRead(out var command)) return;
        if (!command.Enabled)
        {
            StatusText.Text = "Activa la báscula para ejecutar una prueba.";
            OperationFeedback.Show(this, "Báscula", StatusText.Text, OperationResultKind.Warning);
            return;
        }
        try
        {
            var reading = await Task.Run(() => ScaleReadingService.Read(command.ToProfile()));
            RawReadingText.Text = $"Texto recibido: {reading.Raw}";
            ParsedReadingText.Text = $"Peso: {reading.Weight:0.###} {command.Unit.ToLowerInvariant()}";
            StatusText.Text = "Lectura recibida. La estabilidad depende del indicador y protocolo de cada modelo.";
            OperationFeedback.Show(this, "Prueba de báscula", ParsedReadingText.Text, OperationResultKind.Success);
        }
        catch (Exception exception)
        {
            RawReadingText.Text = "Sin lectura";
            ParsedReadingText.Text = "Peso: --";
            StatusText.Text = $"No se pudo leer la báscula: {exception.Message}";
            OperationFeedback.Show(this, "Prueba de báscula", StatusText.Text, OperationResultKind.Error);
        }
    }

    private bool TryRead(out ScaleCommand command)
    {
        command = new ScaleCommand(EnabledCheck.IsChecked == true, PortBox.SelectedItem as string ?? PortBox.Text.Trim(), ParseInt(BaudRateBox.Text, 9600), TextOf(ParityBox, "None"), ParseInt(TextOf(DataBitsBox, "8"), 8), TextOf(StopBitsBox, "One"), TextOf(TerminatorBox, "CRLF"), TextOf(UnitBox, "Kilogramo"), ParseInt(TimeoutBox.Text, 1500), ModelOf());
        if (command.Enabled && string.IsNullOrWhiteSpace(command.Port))
        {
            StatusText.Text = "Selecciona el puerto COM de la báscula.";
            OperationFeedback.Show(this, "Báscula", StatusText.Text, OperationResultKind.Warning);
            return false;
        }
        if (command.ReadTimeoutMs is < 200 or > 5000)
        {
            StatusText.Text = "El tiempo de espera debe estar entre 200 y 5000 ms.";
            OperationFeedback.Show(this, "Báscula", StatusText.Text, OperationResultKind.Warning);
            return false;
        }
        return true;
    }

    private static int ParseInt(string? value, int fallback) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : fallback;
    private static string TextOf(ComboBox box, string fallback)
    {
        var selected = (box.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (!string.IsNullOrWhiteSpace(selected)) return selected;
        return string.IsNullOrWhiteSpace(box.Text) ? fallback : box.Text.Trim();
    }

    private static void SelectText(ComboBox box, string value)
    {
        var item = box.Items.OfType<ComboBoxItem>().FirstOrDefault(candidate => string.Equals(candidate.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase));
        if (item is not null) { box.SelectedItem = item; return; }
        var text = box.Items.OfType<string>().FirstOrDefault(candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase));
        if (text is not null) { box.SelectedItem = text; return; }
        box.Text = value;
    }
    private string ModelOf() => (ModelBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch { "TorreyPcr40" => "TorreyPcr40", "RhinoBar6X" => "RhinoBar6X", _ => "Generic" };
    private void SelectModel(string model) => ModelBox.SelectedIndex = model switch { "TorreyPcr40" => 1, "RhinoBar6X" => 2, _ => 0 };
    private void OnModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        if (ModelOf() == "TorreyPcr40")
        {
            SelectText(BaudRateBox, "115200"); SelectText(ParityBox, "None"); SelectText(DataBitsBox, "8"); SelectText(StopBitsBox, "One"); SelectText(TerminatorBox, "CR"); SelectText(UnitBox, "Kilogramo");
            StatusText.Text = "Perfil Torrey PCR-40 aplicado: 115200, 8N1, CR. Confirma el puerto COM detectado por Windows y prueba la lectura.";
        }
        else if (ModelOf() == "RhinoBar6X")
        {
            SelectText(BaudRateBox, "9600"); SelectText(ParityBox, "None"); SelectText(DataBitsBox, "8"); SelectText(StopBitsBox, "One"); SelectText(TerminatorBox, "CRLF"); SelectText(UnitBox, "Kilogramo");
            StatusText.Text = "Perfil Rhino BAR-6X aplicado: 9600, 8N1. JetVenta solicita el peso con el comando P.";
        }
    }
    private sealed record ScaleCommand(bool Enabled, string Port, int BaudRate, string Parity, int DataBits, string StopBits, string Terminator, string Unit, int ReadTimeoutMs, string Model)
    {
        public ScaleProfile ToProfile() => new(Enabled, Port, BaudRate, Parity, DataBits, StopBits, Terminator, Unit, ReadTimeoutMs, Model);
    }
}
