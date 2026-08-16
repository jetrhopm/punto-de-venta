using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;

namespace Pos.Desktop;

public partial class SystemDetailsWindow : Window
{
    public SystemDetailsWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async Task RefreshAsync()
    {
        DetailsText.Text = "Consultando el estado de JetVenta...";
        var apiOk = await ApiClient.WaitUntilAvailableAsync();
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "No disponible";
        var lines = new[]
        {
            "JETVENTA - DETALLES DEL SISTEMA",
            "================================",
            $"Versión de JetVenta: {version}",
            $"Sistema operativo: {RuntimeInformation.OSDescription}",
            $"Arquitectura del proceso: {RuntimeInformation.ProcessArchitecture}",
            $"Runtime .NET: {Environment.Version}",
            $"Equipo: {Environment.MachineName}",
            $"Usuario de Windows: {Environment.UserName}",
            $"Carpeta de la aplicación: {AppContext.BaseDirectory}",
            $"Servidor local: {ApiClient.BaseUrl}",
            $"Servicio de JetVenta: {(apiOk ? "Disponible" : "No disponible")}",
            $"Tienda identificada: {(ApiClient.StoreId?.ToString() ?? "No emparejada")}",
            $"Caja identificada: {(ApiClient.RegisterId?.ToString() ?? "No emparejada")}",
            $"Impresora configurada: {(string.IsNullOrWhiteSpace(ApiClient.PrinterName) ? "No configurada" : ApiClient.PrinterName)}",
            "Báscula serial: Configuración disponible en Dispositivos > Báscula",
            $"Hora local: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}",
            "",
            apiOk ? "Resultado: los servicios principales responden." : "Resultado: el servicio local no responde. Revisa la API y PostgreSQL desde JetVenta o el instalador."
        };
        DetailsText.Text = string.Join(Environment.NewLine, lines);
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetDataObject(DetailsText.Text, true); MessageBox.Show("La información se copió al portapapeles.", "JetVenta", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (Exception exception) { MessageBox.Show($"No se pudo copiar la información: {exception.Message}", "JetVenta", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
