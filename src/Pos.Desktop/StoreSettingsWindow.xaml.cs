using System.Net.Http.Json;
using System.Windows;

namespace Pos.Desktop;

public partial class StoreSettingsWindow : Window
{
    public StoreSettingsWindow() { InitializeComponent(); Loaded += OnLoaded; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await ApiClient.Client.GetFromJsonAsync<StoreSettings>("api/store-settings");
            if (settings is null) return;
            NameBox.Text = settings.Name; BusinessTypeBox.Text = settings.BusinessType; LegalNameBox.Text = settings.LegalName; TaxIdBox.Text = settings.TaxId; AddressBox.Text = settings.Address; PhoneBox.Text = settings.Phone; TimeZoneBox.Text = settings.TimeZoneId;
        }
        catch (Exception exception) { StatusText.Text = $"No se pudieron cargar los datos: {exception.Message}"; }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync("api/store-settings", new { name = NameBox.Text, businessType = BusinessTypeBox.Text, legalName = LegalNameBox.Text, taxId = TaxIdBox.Text, address = AddressBox.Text, phone = PhoneBox.Text, timeZoneId = TimeZoneBox.Text });
            StatusText.Text = response.IsSuccessStatusCode ? "Datos de la tienda guardados correctamente." : await response.Content.ReadAsStringAsync();
        }
        catch (Exception exception) { StatusText.Text = $"No se pudieron guardar los datos: {exception.Message}"; }
    }

    private sealed record StoreSettings(Guid Id, string Name, string BusinessType, string LegalName, string TaxId, string Address, string Phone, string TimeZoneId);
}
