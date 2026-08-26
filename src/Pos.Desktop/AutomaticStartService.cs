using Microsoft.Win32;
using System.IO;

namespace Pos.Desktop;

/// <summary>Stores the per-Windows-user automatic startup entry for this JetVenta client.</summary>
public static class AutomaticStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "JetVenta";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (!enabled)
        {
            key?.DeleteValue(ValueName, false);
            return;
        }

        var executable = Environment.ProcessPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            throw new InvalidOperationException("No se encontró el ejecutable de JetVenta para configurar el inicio automático.");

        key?.SetValue(ValueName, Quote(executable), RegistryValueKind.String);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
