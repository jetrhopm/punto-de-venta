using System.Globalization;
using System.IO.Ports;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class ScaleSettingsWindow : Window
{
    private static readonly Regex WeightPattern = new(@"(?<!\d)[+-]?\d+(?:[.,]\d+)?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private bool _loaded;

    public ScaleSettingsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadPorts();
        SelectText(BaudRateBox, "9600"); SelectText(ParityBox, "None"); SelectText(DataBitsBox, "8"); SelectText(StopBitsBox, "One"); SelectText(TerminatorBox, "CRLF"); SelectText(UnitBox, "Kilogramo");
        try
        {
            var settings = await ApiClient.Client.GetFromJsonAsync<ScaleSettingsDto>("api/scale-settings");
            if (settings is not null)
            {
                EnabledCheck.IsChecked = settings.Enabled;
                SelectText(PortBox, settings.Port); SelectText(BaudRateBox, settings.BaudRate.ToString(CultureInfo.InvariantCulture)); SelectText(ParityBox, settings.Parity); SelectText(DataBitsBox, settings.DataBits.ToString(CultureInfo.InvariantCulture)); SelectText(StopBitsBox, settings.StopBits); SelectText(TerminatorBox, settings.Terminator); SelectText(UnitBox, settings.Unit); TimeoutBox.Text = settings.ReadTimeoutMs.ToString(CultureInfo.InvariantCulture);
            }
            StatusText.Text = "Selecciona el puerto y prueba una lectura antes de guardar.";
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo leer la configuración de la báscula: {exception.Message}"; }
        _loaded = true; UpdateEnabledState();
    }

    private void LoadPorts(string? preferred = null)
    {
        try
        {
            var ports = SerialPort.GetPortNames().OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList();
            if (!string.IsNullOrWhiteSpace(preferred) && !ports.Contains(preferred, StringComparer.OrdinalIgnoreCase)) ports.Insert(0, preferred);
            PortBox.ItemsSource = ports;
            if (ports.Count > 0) PortBox.SelectedItem = ports.FirstOrDefault(item => string.Equals(item, preferred, StringComparison.OrdinalIgnoreCase)) ?? ports[0];
            StatusText.Text = ports.Count == 0 ? "No se detectaron puertos COM. Conecta la báscula o instala el controlador del fabricante." : $"Windows reportó {ports.Count} puerto(s) COM.";
        }
        catch (Exception exception) { StatusText.Text = $"No se pudieron consultar los puertos COM: {exception.Message}"; }
    }

    private void OnRefreshPortsClick(object sender, RoutedEventArgs e) => LoadPorts(PortBox.SelectedItem as string);
    private void OnEnabledChanged(object sender, RoutedEventArgs e) => UpdateEnabledState();
    private void UpdateEnabledState()
    {
        var enabled = EnabledCheck.IsChecked == true;
        PortBox.IsEnabled = enabled; BaudRateBox.IsEnabled = enabled; UnitBox.IsEnabled = enabled; ParityBox.IsEnabled = enabled; DataBitsBox.IsEnabled = enabled; StopBitsBox.IsEnabled = enabled; TerminatorBox.IsEnabled = enabled; TimeoutBox.IsEnabled = enabled;
        if (_loaded && !enabled) StatusText.Text = "La báscula está desactivada para esta caja.";
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryRead(out var command)) return;
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync("api/scale-settings", command);
            if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; }
            StatusText.Text = command.Enabled ? $"Báscula guardada en {command.Port}." : "Báscula desactivada.";
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo guardar la configuración: {exception.Message}"; }
    }

    private async void OnTestClick(object sender, RoutedEventArgs e)
    {
        if (!TryRead(out var command)) return;
        if (!command.Enabled) { StatusText.Text = "Activa la báscula para ejecutar una prueba."; return; }
        try
        {
            var reading = await Task.Run(() => ReadSerial(command));
            RawReadingText.Text = $"Texto recibido: {reading.Raw}";
            ParsedReadingText.Text = $"Peso: {reading.Weight:0.###} {command.Unit.ToLowerInvariant()}";
            StatusText.Text = "Lectura recibida. La estabilidad depende del indicador y protocolo de cada modelo.";
        }
        catch (Exception exception) { RawReadingText.Text = "Sin lectura"; ParsedReadingText.Text = "Peso: --"; StatusText.Text = $"No se pudo leer la báscula: {exception.Message}"; }
    }

    private static ScaleReading ReadSerial(ScaleCommand command)
    {
        using var port = new SerialPort(command.Port, command.BaudRate, ParseParity(command.Parity), command.DataBits, ParseStopBits(command.StopBits)) { NewLine = Terminator(command.Terminator), ReadTimeout = command.ReadTimeoutMs, Handshake = Handshake.None, DtrEnable = false, RtsEnable = false };
        port.Open(); port.DiscardInBuffer(); port.DiscardOutBuffer();
        var raw = port.ReadLine().Trim();
        var match = WeightPattern.Match(raw);
        if (!match.Success) throw new InvalidOperationException("La báscula respondió, pero no se encontró un número de peso en la lectura.");
        var number = match.Value.Contains(',') && !match.Value.Contains('.') ? match.Value.Replace(',', '.') : match.Value.Replace(",", "");
        if (!decimal.TryParse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var weight) || weight < 0m) throw new InvalidOperationException("La lectura de peso no es válida.");
        return new ScaleReading(raw, weight);
    }

    private bool TryRead(out ScaleCommand command)
    {
        command = new ScaleCommand(EnabledCheck.IsChecked == true, PortBox.SelectedItem as string ?? PortBox.Text.Trim(), ParseInt(BaudRateBox.Text, 9600), TextOf(ParityBox, "None"), ParseInt(TextOf(DataBitsBox, "8"), 8), TextOf(StopBitsBox, "One"), TextOf(TerminatorBox, "CRLF"), TextOf(UnitBox, "Kilogramo"), ParseInt(TimeoutBox.Text, 1500));
        if (command.Enabled && string.IsNullOrWhiteSpace(command.Port)) { StatusText.Text = "Selecciona el puerto COM de la báscula."; return false; }
        if (command.ReadTimeoutMs is < 200 or > 5000) { StatusText.Text = "El tiempo de espera debe estar entre 200 y 5000 ms."; return false; }
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
    private static Parity ParseParity(string value) => Enum.TryParse<Parity>(value, true, out var parsed) ? parsed : Parity.None;
    private static StopBits ParseStopBits(string value) => Enum.TryParse<StopBits>(value, true, out var parsed) ? parsed : StopBits.One;
    private static string Terminator(string value) => value switch { "CR" => "\r", "LF" => "\n", _ => "\r\n" };
    private sealed record ScaleSettingsDto(bool Enabled, string Port, int BaudRate, string Parity, int DataBits, string StopBits, string Terminator, string Unit, int ReadTimeoutMs);
    private sealed record ScaleCommand(bool Enabled, string Port, int BaudRate, string Parity, int DataBits, string StopBits, string Terminator, string Unit, int ReadTimeoutMs);
    private sealed record ScaleReading(string Raw, decimal Weight);
}
