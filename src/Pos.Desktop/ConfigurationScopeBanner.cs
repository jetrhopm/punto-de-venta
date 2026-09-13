using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Pos.Desktop;

internal enum ConfigurationScope
{
    AllRegisters,
    CurrentRegister,
    ServerOnly,
    StoreAndCurrentRegister
}

internal static class ConfigurationScopeBanner
{
    public static async Task ApplyAsync(Window window, ConfigurationScope scope)
    {
        var registerName = scope is ConfigurationScope.CurrentRegister or ConfigurationScope.StoreAndCurrentRegister
            ? await GetCurrentRegisterNameAsync()
            : string.Empty;
        var (text, color) = scope switch
        {
            ConfigurationScope.AllRegisters => ("Afecta a todas las cajas", "#1D6EA5"),
            ConfigurationScope.CurrentRegister => ($"Sólo esta caja: {registerName}", "#1B8750"),
            ConfigurationScope.ServerOnly => ("Sólo servidor", "#9A661F"),
            _ => ($"Afecta a todas las cajas: datos de tienda\nSólo esta caja: {registerName}", "#5B4B96")
        };

        window.Title = $"{window.Title} | {text.Replace("\n", " - ")}";
        Attach(window, text, color);
    }

    private static async Task<string> GetCurrentRegisterNameAsync()
    {
        try
        {
            var result = await ApiClient.Client.GetFromJsonAsync<CurrentRegisterResult>("api/registers/current");
            return string.IsNullOrWhiteSpace(result?.Name) ? "caja actual" : result.Name.Trim();
        }
        catch
        {
            return "caja actual";
        }
    }

    private static void Attach(Window window, string text, string color)
    {
        if (window.Content is not Panel root || root.Children.OfType<Border>().Any(item => Equals(item.Tag, "ConfigurationScopeBanner"))) return;

        var foreground = (Color)ColorConverter.ConvertFromString(color);
        var banner = new Border
        {
            Tag = "ConfigurationScopeBanner",
            Background = new SolidColorBrush(Color.FromArgb(24, foreground.R, foreground.G, foreground.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(130, foreground.R, foreground.G, foreground.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 5, 10, 5),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 18, 0),
            ToolTip = "Indica el alcance de los cambios que se guarden en esta pantalla.",
            Child = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(foreground),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 270,
                TextAlignment = TextAlignment.Right
            }
        };
        Panel.SetZIndex(banner, 1000);
        root.Children.Add(banner);
    }

    private sealed record CurrentRegisterResult(string Name);
}
