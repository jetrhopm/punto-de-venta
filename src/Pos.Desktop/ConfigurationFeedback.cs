using System.Net.Http;
using System.Text.Json;
using System.Windows;

namespace Pos.Desktop;

internal static class ConfigurationFeedback
{
    public static void ShowSavedAndClose(Window owner, string title, string message, bool restartRequired = false)
    {
        new ConfigurationResultWindow(title, message, restartRequired) { Owner = owner }.ShowDialog();
        owner.Close();
    }

    public static async Task<string> ReadErrorAsync(HttpResponseMessage response, string fallback)
    {
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content)) return fallback;

        try
        {
            using var json = JsonDocument.Parse(content);
            var root = json.RootElement;
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in errors.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Array) continue;
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String) continue;
                        var error = item.GetString();
                        if (!string.IsNullOrWhiteSpace(error)) return error;
                    }
                }
            }
            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(detail.GetString())) return detail.GetString()!;
            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(message.GetString())) return message.GetString()!;
        }
        catch (JsonException) { }

        return fallback;
    }
}
