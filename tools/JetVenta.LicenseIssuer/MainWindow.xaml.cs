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
        RefreshIssuerState();
    }

    private void RefreshIssuerState()
    {
        IssuerStateText.Text = IssuerKeyStore.HasAuthorizedIssuer
            ? "Este equipo está autorizado para emitir licencias. La llave permanece protegida para el usuario actual de Windows."
            : "Este equipo todavía no está autorizado para emitir. Puedes crear una solicitud o recuperar un emisor mediante un respaldo de emergencia.";
    }

    private void OnExpirationChanged(object sender, RoutedEventArgs e) => ExpirationPicker.IsEnabled = ExpirationCheckBox.IsChecked == true;

    private void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        if (!JetVentaLicensing.TryReadRequestCode(RequestCodeTextBox.Text, out var request, out var requestError))
        {
            IssueStatusText.Text = requestError;
            return;
        }
        if (string.IsNullOrWhiteSpace(StoreNameTextBox.Text) || StoreNameTextBox.Text.Trim().Length > 200)
        {
            IssueStatusText.Text = "Escribe un nombre de tienda o titular de hasta 200 caracteres.";
            return;
        }
        if (ExpirationCheckBox.IsChecked == true && ExpirationPicker.SelectedDate is null)
        {
            IssueStatusText.Text = "Selecciona la fecha de vencimiento o desmarca esa opción.";
            return;
        }

        try
        {
            using var privateKey = IssuerKeyStore.Load();
            DateTimeOffset? expiration = ExpirationCheckBox.IsChecked == true
                ? new DateTimeOffset(ExpirationPicker.SelectedDate!.Value.Date.AddDays(1).AddTicks(-1), TimeZoneInfo.Local.GetUtcOffset(ExpirationPicker.SelectedDate!.Value.Date))
                : null;
            var claims = new JetVentaLicenseClaims(JetVentaLicensing.ProtocolVersion, JetVentaLicensing.Product, Guid.NewGuid().ToString(), request!.MachineFingerprint, DateTimeOffset.UtcNow, expiration, StoreNameTextBox.Text.Trim());
            var document = new JetVentaSignedLicense(claims, JetVentaLicensing.Sign(claims, privateKey));
            var dialog = new SaveFileDialog { Title = "Guardar licencia de JetVenta", Filter = "Licencia JetVenta (*.jv)|*.jv", FileName = "licencia.jv", AddExtension = true, DefaultExt = ".jv" };
            if (dialog.ShowDialog(this) != true) return;
            File.WriteAllText(dialog.FileName, JetVentaLicensing.Serialize(document), new UTF8Encoding(false));
            IssueStatusText.Text = $"Licencia creada: {dialog.FileName}";
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or InvalidOperationException)
        {
            IssueStatusText.Text = $"No se pudo generar la licencia: {exception.Message}";
        }
    }

    private void OnCreateEnrollmentRequestClick(object sender, RoutedEventArgs e)
    {
        try
        {
            EnrollmentRequestTextBox.Text = IssuerKeyStore.CreateEnrollmentRequest();
            AuthorizationStatusText.Text = "Solicitud creada. Copia este código y llévalo a un emisor ya autorizado.";
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or InvalidOperationException)
        {
            AuthorizationStatusText.Text = $"No se pudo crear la solicitud: {exception.Message}";
        }
    }

    private void OnCopyEnrollmentRequestClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(EnrollmentRequestTextBox.Text))
        {
            AuthorizationStatusText.Text = "Primero crea una solicitud para esta computadora.";
            return;
        }

        Clipboard.SetText(EnrollmentRequestTextBox.Text);
        AuthorizationStatusText.Text = "Solicitud copiada al portapapeles.";
    }

    private void OnCreateAuthorizationFileClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var document = IssuerKeyStore.CreateAuthorizationDocument(AuthorizationRequestTextBox.Text);
            var dialog = new SaveFileDialog { Title = "Guardar autorización de emisor", Filter = "Autorización de emisor JetVenta (*.jvissuer)|*.jvissuer", FileName = "autorizacion-emisor.jvissuer", AddExtension = true, DefaultExt = ".jvissuer" };
            if (dialog.ShowDialog(this) != true) return;
            File.WriteAllText(dialog.FileName, document, new UTF8Encoding(false));
            AuthorizationStatusText.Text = $"Archivo de autorización creado: {dialog.FileName}";
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or InvalidOperationException)
        {
            AuthorizationStatusText.Text = $"No se pudo crear la autorización: {exception.Message}";
        }
    }

    private void OnImportAuthorizationFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Seleccionar autorización de emisor", Filter = "Autorización de emisor JetVenta (*.jvissuer)|*.jvissuer", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            IssuerKeyStore.ImportAuthorizationDocument(File.ReadAllText(dialog.FileName, Encoding.UTF8));
            AuthorizationStatusText.Text = "Esta computadora quedó autorizada para emitir licencias.";
            RefreshIssuerState();
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or InvalidOperationException)
        {
            AuthorizationStatusText.Text = $"No se pudo importar la autorización: {exception.Message}";
        }
    }

    private void OnCreateRecoveryClick(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(RecoveryPasswordBox.Password, RecoveryConfirmPasswordBox.Password, StringComparison.Ordinal))
        {
            RecoveryStatusText.Text = "Las contraseñas de recuperación no coinciden.";
            return;
        }

        try
        {
            var document = IssuerKeyStore.CreateRecoveryDocument(RecoveryPasswordBox.Password);
            var dialog = new SaveFileDialog { Title = "Guardar respaldo del emisor", Filter = "Respaldo de recuperación JetVenta (*.jvrecovery)|*.jvrecovery", FileName = "respaldo-emisor.jvrecovery", AddExtension = true, DefaultExt = ".jvrecovery" };
            if (dialog.ShowDialog(this) != true) return;
            File.WriteAllText(dialog.FileName, document, new UTF8Encoding(false));
            RecoveryPasswordBox.Clear();
            RecoveryConfirmPasswordBox.Clear();
            RecoveryStatusText.Text = $"Respaldo creado: {dialog.FileName}. Guárdalo fuera de esta computadora.";
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or InvalidOperationException)
        {
            RecoveryStatusText.Text = $"No se pudo crear el respaldo: {exception.Message}";
        }
    }

    private void OnImportRecoveryClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RecoveryPasswordBox.Password))
        {
            RecoveryStatusText.Text = "Escribe la contraseña del respaldo de recuperación.";
            return;
        }

        var dialog = new OpenFileDialog { Title = "Seleccionar respaldo del emisor", Filter = "Respaldo de recuperación JetVenta (*.jvrecovery)|*.jvrecovery", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            IssuerKeyStore.ImportRecoveryDocument(File.ReadAllText(dialog.FileName, Encoding.UTF8), RecoveryPasswordBox.Password);
            RecoveryPasswordBox.Clear();
            RecoveryConfirmPasswordBox.Clear();
            RecoveryStatusText.Text = "Emisor recuperado y protegido para este usuario de Windows.";
            RefreshIssuerState();
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or InvalidOperationException)
        {
            RecoveryStatusText.Text = $"No se pudo recuperar el emisor: {exception.Message}";
        }
    }
}
