using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;

namespace Pos.Desktop;

public sealed record ScaleReading(string Raw, decimal Weight);

public static class ScaleReadingService
{
    private static readonly Regex WeightPattern = new(@"(?<!\d)[+-]?\d+(?:[.,]\d+)?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ScaleReading Read(ScaleProfile profile)
    {
        var settings = profile.Normalize();
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Port)) throw new InvalidOperationException("Configura y activa la báscula de esta caja antes de tomar el peso.");
        var scanner = ApiClient.BarcodeScanner.Normalize();
        if (scanner.Mode == BarcodeScannerMode.Serial && string.Equals(scanner.PortName, settings.Port, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"El puerto {settings.Port} está configurado también para el lector serial. La báscula y el lector deben usar puertos COM distintos.");
        }

        try
        {
            using var port = new SerialPort(settings.Port, settings.BaudRate, ParseParity(settings.Parity), settings.DataBits, ParseStopBits(settings.StopBits))
            {
                NewLine = Terminator(settings.Terminator), ReadTimeout = settings.ReadTimeoutMs, WriteTimeout = settings.ReadTimeoutMs,
                Handshake = Handshake.None, DtrEnable = false, RtsEnable = false
            };
            port.Open();
            port.DiscardInBuffer();
            port.DiscardOutBuffer();
            var raw = settings.Model switch
            {
                "TorreyPcr40" or "RhinoBar6X" => RequestWeight(port, settings.ReadTimeoutMs),
                _ => port.ReadLine().Trim()
            };
            var match = WeightPattern.Match(raw);
            if (!match.Success) throw new InvalidOperationException("La báscula respondió, pero no se encontró un peso válido. Verifica que no indique NET, NEG, RANGE, OVERLOAD u otro estado.");
            var number = match.Value.Contains(',') && !match.Value.Contains('.') ? match.Value.Replace(',', '.') : match.Value.Replace(",", "");
            if (!decimal.TryParse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var weight) || weight < 0m) throw new InvalidOperationException("La lectura de peso no es válida.");
            return new ScaleReading(raw, weight);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException($"El puerto {settings.Port} está ocupado por otro programa. Cierra el software de la báscula, una terminal de prueba u otra instancia de JetVenta; después espera unos segundos y vuelve a intentar.", exception);
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException($"La báscula no respondió en {settings.ReadTimeoutMs} ms. Verifica el modelo, el cable USB, el puerto COM y los parámetros de comunicación.", exception);
        }
    }

    public static decimal ConvertWeight(decimal value, string sourceUnit, string targetUnit)
    {
        var source = WeightUnit(sourceUnit);
        var target = WeightUnit(targetUnit);
        if (source == target) return value;
        var kilograms = source switch { "Gramo" => value / 1000m, "Libra" => value * 0.45359237m, _ => value };
        return target switch { "Gramo" => kilograms * 1000m, "Libra" => kilograms / 0.45359237m, _ => kilograms };
    }

    private static string WeightUnit(string? value) => value?.TrimStart().StartsWith("gramo", StringComparison.OrdinalIgnoreCase) == true
        ? "Gramo"
        : value?.TrimStart().StartsWith("libra", StringComparison.OrdinalIgnoreCase) == true ? "Libra" : "Kilogramo";

    private static Parity ParseParity(string value) => Enum.TryParse<Parity>(value, true, out var parsed) ? parsed : Parity.None;
    private static StopBits ParseStopBits(string value) => Enum.TryParse<StopBits>(value, true, out var parsed) ? parsed : StopBits.One;
    private static string Terminator(string value) => value switch { "CR" => "\r", "LF" => "\n", _ => "\r\n" };

    private static string RequestWeight(SerialPort port, int timeoutMs)
    {
        port.Write("P");
        var response = new StringBuilder();
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            var chunk = port.ReadExisting();
            if (!string.IsNullOrEmpty(chunk))
            {
                response.Append(chunk);
                if (chunk.IndexOfAny(['\r', '\n']) >= 0) return response.ToString().Trim();
            }

            Thread.Sleep(15);
        }

        var raw = response.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(raw)) return raw;
        throw new TimeoutException();
    }
}
