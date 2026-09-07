using System.Net.Http.Json;
using System.Net.Http;
using System.Text.Json;
using System.Windows;

namespace Pos.Desktop;

public partial class InitialSetupWindow : Window
{
    public InitialSetupWindow()
    {
        InitializeComponent();
        PasswordBox.Password = "12345";
        StoreNameBox.Focus();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryValidateInputs(out var validationMessage))
        {
            MessageText.Text = validationMessage;
            return;
        }

        MessageText.Text = "Creando la tienda...";
        SaveButton.IsEnabled = false;
        try
        {
            var businessType = (BusinessTypeBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Abarrotes";
            using var response = await ApiClient.Client.PostAsJsonAsync("/api/setup/initial", new
            {
                storeName = StoreNameBox.Text,
                businessType,
                userName = UserNameBox.Text,
                password = PasswordBox.Password,
                administratorName = AdministratorNameBox.Text,
                registerName = RegisterNameBox.Text,
                currencySymbol = CurrencySymbolBox.Text,
                defaultWeightUnit = (WeightUnitBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Kilogramo"
            });
            if (!response.IsSuccessStatusCode)
            {
                MessageText.Text = response.StatusCode == System.Net.HttpStatusCode.Conflict
                    ? "La tienda ya fue configurada. Cierra esta ventana e inicia sesion."
                    : await ReadErrorMessageAsync(response);
                return;
            }
            DialogResult = true;
        }
        catch (HttpRequestException)
        {
            MessageText.Text = "La API local no esta disponible. Verifica el servicio PuntoDeVentaApi.";
        }
        catch (Exception exception)
        {
            MessageText.Text = $"No se pudo crear la tienda: {exception.Message}";
        }
        finally { SaveButton.IsEnabled = true; }
    }

    private bool TryValidateInputs(out string message)
    {
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(StoreNameBox.Text)) message = "Escribe el nombre de la tienda.";
        else if (string.IsNullOrWhiteSpace(UserNameBox.Text)) message = "Escribe el usuario del administrador.";
        else if (string.IsNullOrWhiteSpace(AdministratorNameBox.Text)) message = "Escribe el nombre del administrador.";
        else if (string.IsNullOrWhiteSpace(PasswordBox.Password)) message = "Escribe una contraseña para el administrador.";
        else if (string.IsNullOrWhiteSpace(RegisterNameBox.Text)) message = "Escribe el nombre de la primera caja.";
        else if (string.IsNullOrWhiteSpace(CurrencySymbolBox.Text)) message = "Escribe el símbolo de moneda que utilizará la tienda.";
        else if (BusinessTypeBox.SelectedItem is not System.Windows.Controls.ComboBoxItem) message = "Selecciona el giro del negocio.";
        else if (WeightUnitBox.SelectedItem is not System.Windows.Controls.ComboBoxItem) message = "Selecciona la unidad para productos a granel.";
        return string.IsNullOrEmpty(message);
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                foreach (var item in errors.EnumerateObject())
                {
                    if (item.Value.ValueKind == JsonValueKind.Array && item.Value.GetArrayLength() > 0)
                        return item.Value[0].GetString() ?? "Revisa los datos de la configuración inicial.";
                }
            }
            if (document.RootElement.TryGetProperty("detail", out var detail) && !string.IsNullOrWhiteSpace(detail.GetString())) return detail.GetString()!;
            if (document.RootElement.TryGetProperty("message", out var message) && !string.IsNullOrWhiteSpace(message.GetString())) return message.GetString()!;
        }
        catch (JsonException)
        {
            // A generic message is appropriate for a non-JSON server response.
        }

        return "No se pudo guardar la configuración. Revisa los datos e intenta nuevamente.";
    }
}
