using System.Net.Http.Json;
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
                registerName = RegisterNameBox.Text
            });
            if (!response.IsSuccessStatusCode)
            {
                MessageText.Text = response.StatusCode == System.Net.HttpStatusCode.Conflict
                    ? "La tienda ya fue configurada. Cierra esta ventana e inicia sesion."
                    : "No se pudo guardar la configuracion. Revisa los datos e intenta nuevamente.";
                return;
            }
            DialogResult = true;
        }
        catch (HttpRequestException)
        {
            MessageText.Text = "La API local no esta disponible. Verifica el servicio PuntoDeVentaApi.";
        }
        finally { SaveButton.IsEnabled = true; }
    }
}
