using Microsoft.Win32;
using Pos.Domain;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace JetVenta.LicenseIssuer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StatusText.Text = "Pega el código que muestra JetVenta en Configuración > Licencia.";
    }

    private void OnExpirationChanged(object sender, RoutedEventArgs e) => ExpirationPicker.IsEnabled = ExpirationCheckBox.IsChecked == true;

    private void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        if (!JetVentaLicensing.TryReadRequestCode(RequestCodeTextBox.Text, out var request, out var requestError))
        {
            StatusText.Text = requestError;
            return;
        }
        if (string.IsNullOrWhiteSpace(StoreNameTextBox.Text) || StoreNameTextBox.Text.Trim().Length > 200)
        {
            StatusText.Text = "Escribe un nombre de tienda o titular de hasta 200 caracteres.";
            return;
        }
        if (ExpirationCheckBox.IsChecked == true && ExpirationPicker.SelectedDate is null)
        {
            StatusText.Text = "Selecciona la fecha de vencimiento o desmarca esa opción.";
            return;
        }

        try
        {
            using var privateKey = IssuerKeyStore.Load();
            var publicKey = Convert.ToBase64String(privateKey.ExportSubjectPublicKeyInfo());
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(publicKey), Encoding.ASCII.GetBytes(JetVentaLicensing.IssuerPublicKeyBase64)))
                throw new InvalidOperationException("La llave emisora no corresponde a la llave pública incluida en JetVenta. No generes una licencia.");

            DateTimeOffset? expiration = ExpirationCheckBox.IsChecked == true
                ? new DateTimeOffset(ExpirationPicker.SelectedDate!.Value.Date.AddDays(1).AddTicks(-1), TimeZoneInfo.Local.GetUtcOffset(ExpirationPicker.SelectedDate!.Value.Date))
                : null;
            var claims = new JetVentaLicenseClaims(JetVentaLicensing.ProtocolVersion, JetVentaLicensing.Product, Guid.NewGuid().ToString(), request!.MachineFingerprint, DateTimeOffset.UtcNow, expiration, StoreNameTextBox.Text.Trim());
            var document = new JetVentaSignedLicense(claims, JetVentaLicensing.Sign(claims, privateKey));
            var dialog = new SaveFileDialog { Title = "Guardar licencia de JetVenta", Filter = "Licencia JetVenta (*.jv)|*.jv", FileName = "licencia.jv", AddExtension = true, DefaultExt = ".jv" };
            if (dialog.ShowDialog(this) != true) return;
            File.WriteAllText(dialog.FileName, JetVentaLicensing.Serialize(document), new UTF8Encoding(false));
            StatusText.Text = $"Licencia creada: {dialog.FileName}";
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or InvalidOperationException)
        {
            StatusText.Text = $"No se pudo generar la licencia: {exception.Message}";
        }
    }
}
