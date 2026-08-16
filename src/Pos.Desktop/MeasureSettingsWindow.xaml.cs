using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class MeasureSettingsWindow : Window
{
    public MeasureSettingsWindow() { InitializeComponent(); Loaded += OnLoaded; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await ApiClient.Client.GetFromJsonAsync<MeasureSettings>("api/measure-settings");
            WeightUnitBox.SelectedItem = WeightUnitBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Content?.ToString(), settings?.DefaultWeightUnit, StringComparison.OrdinalIgnoreCase)) ?? WeightUnitBox.Items[0];
            StatusText.Text = "La unidad predeterminada se aplica al guardar nuevos productos de granel.";
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo cargar la configuración: {exception.Message}"; }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var unit = (WeightUnitBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (string.IsNullOrWhiteSpace(unit)) { StatusText.Text = "Selecciona una unidad de peso."; return; }
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync("api/measure-settings", new { defaultWeightUnit = unit });
            StatusText.Text = response.IsSuccessStatusCode ? "Unidad de peso guardada correctamente." : await response.Content.ReadAsStringAsync();
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo guardar la unidad: {exception.Message}"; }
    }

    private sealed record MeasureSettings(string DefaultWeightUnit);
}
