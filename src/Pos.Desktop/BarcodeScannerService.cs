using System.IO.Ports;
using System.Text;

namespace Pos.Desktop;

public static class BarcodeScannerService
{
    private static readonly object Sync = new();
    private static readonly StringBuilder Buffer = new();
    private static SerialPort? _port;
    private static BarcodeScannerProfile _profile = BarcodeScannerProfile.Default;
    private static string? _lastCode;
    private static DateTimeOffset _lastReadAt;

    public static event EventHandler<string>? BarcodeScanned;

    public static string ApplyProfile(BarcodeScannerProfile profile)
    {
        lock (Sync)
        {
            StopLocked();
            _profile = profile.Normalize();
            if (_profile.Mode == BarcodeScannerMode.Disabled) return "Lector desactivado para esta caja.";
            if (_profile.Mode == BarcodeScannerMode.Keyboard) return "Modo teclado activo. No se requieren controladores de JetVenta.";

            _port = new SerialPort(_profile.PortName!, _profile.BaudRate, Parity.None, 8, StopBits.One) { Handshake = Handshake.None };
            _port.DataReceived += OnDataReceived;
            _port.Open();
            return $"Lector serial listo en {_profile.PortName} a {_profile.BaudRate} bps.";
        }
    }

    public static void StartConfiguredProfile()
    {
        try { ApplyProfile(ApiClient.BarcodeScanner); }
        catch { /* The startup screen reports service issues; scanner availability must not block the POS. */ }
    }

    public static void Stop()
    {
        lock (Sync) StopLocked();
    }

    private static void StopLocked()
    {
        if (_port is null) return;
        try { _port.DataReceived -= OnDataReceived; if (_port.IsOpen) _port.Close(); _port.Dispose(); }
        finally { _port = null; Buffer.Clear(); }
    }

    private static void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var chunk = _port?.ReadExisting();
            if (string.IsNullOrEmpty(chunk)) return;
            lock (Sync)
            {
                foreach (var character in chunk)
                {
                    if (character is '\r' or '\n') { PublishBufferLocked(); continue; }
                    if (char.IsLetterOrDigit(character) && Buffer.Length < 64) Buffer.Append(character);
                }
            }
        }
        catch { /* A disconnected scanner is reported when configuring it; it must not crash a sale. */ }
    }

    private static void PublishBufferLocked()
    {
        var code = Buffer.ToString();
        Buffer.Clear();
        if (code.Length == 0) return;
        var now = DateTimeOffset.UtcNow;
        if (string.Equals(_lastCode, code, StringComparison.Ordinal) && now - _lastReadAt < TimeSpan.FromMilliseconds(350)) return;
        _lastCode = code;
        _lastReadAt = now;
        BarcodeScanned?.Invoke(null, code);
    }
}
