using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class RegisterAdministrationWindow : Window
{
    private List<RegisterRow> _registers = [];
    public RegisterAdministrationWindow() { InitializeComponent(); Loaded += async (_, _) => await LoadAsync(); RegistersGrid.SelectionChanged += (_, _) => UpdateButtons(); }

    private async Task LoadAsync()
    {
        try
        {
            var rows = await ApiClient.Client.GetFromJsonAsync<List<RegisterRow>>("api/registers") ?? [];
            _registers = rows.Select(item => item with { Status = item.IsActive ? "Activa" : "Inactiva", LastConnectionDisplay = item.LastConnectionAtUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Sin conexión", ShiftDisplay = string.IsNullOrWhiteSpace(item.OpenShiftUser) ? "Sin turno abierto" : $"{item.OpenShiftUser} desde {item.ShiftOpenedAtUtc?.ToLocalTime():dd/MM HH:mm}" }).ToList();
            RegistersGrid.ItemsSource = _registers;
            StatusText.Text = _registers.Count == 0 ? "No hay cajas registradas." : $"{_registers.Count} caja(s) registrada(s).";
            UpdateButtons();
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo consultar las cajas"); }
    }

    private RegisterRow? Selected => RegistersGrid.SelectedItem as RegisterRow;
    private void UpdateButtons() { var item = Selected; ActiveButton.Content = item?.IsActive == false ? "Reactivar" : "Desactivar"; ActiveButton.Style = (Style)FindResource(item?.IsActive == false ? "SuccessButtonStyle" : "DangerButtonStyle"); }
    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadAsync();
    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        var row = Selected;
        if (row is null) { StatusText.Text = "Selecciona una caja."; return; }
        var prompt = new RenameRegisterWindow(row.Name) { Owner = this };
        if (prompt.ShowDialog() != true) return;
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync($"api/registers/{row.Id}/name", new { name = prompt.NewRegisterName });
            if (!response.IsSuccessStatusCode) { StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo cambiar el nombre."); return; }
            await LoadAsync();
            OperationFeedback.Show(this, "Caja actualizada", "El nombre de la caja se guardó correctamente.", OperationResultKind.Success);
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo cambiar el nombre"); }
    }
    private async void OnToggleActiveClick(object sender, RoutedEventArgs e)
    {
        var row = Selected;
        if (row is null) { StatusText.Text = "Selecciona una caja."; return; }
        var activate = !row.IsActive;
        var confirmation = new OperationConfirmationWindow(activate ? "Reactivar caja" : "Desactivar caja", activate ? $"{row.Name} podrá iniciar sesión nuevamente." : $"{row.Name} no podrá iniciar sesión hasta que la reactives. El historial no se modifica.", activate ? OperationResultKind.Success : OperationResultKind.Warning, MessageBoxButton.YesNo) { Owner = this };
        if (confirmation.ShowDialog() != true || confirmation.Result != MessageBoxResult.Yes) return;
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync($"api/registers/{row.Id}/active", new { isActive = activate });
            if (!response.IsSuccessStatusCode) { StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo actualizar la caja."); return; }
            await LoadAsync();
            OperationFeedback.Show(this, activate ? "Caja reactivada" : "Caja desactivada", activate ? "La caja puede volver a iniciar sesión." : "La caja quedó bloqueada para nuevas sesiones.", activate ? OperationResultKind.Success : OperationResultKind.Information);
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo actualizar la caja"); }
    }
    private async void OnPairAgainClick(object sender, RoutedEventArgs e)
    {
        var row = Selected;
        if (row is null) { StatusText.Text = "Selecciona una caja."; return; }
        if (!row.IsActive) { StatusText.Text = "Reactiva la caja antes de volver a emparejarla."; return; }
        var confirmation = new OperationConfirmationWindow("Volver a emparejar caja", $"Se invalidará la identidad anterior de {row.Name}. Generarás un código de un solo uso para esa misma caja.", OperationResultKind.Warning, MessageBoxButton.YesNo) { Owner = this };
        if (confirmation.ShowDialog() != true || confirmation.Result != MessageBoxResult.Yes) return;
        try
        {
            using var response = await ApiClient.Client.PostAsync($"api/registers/{row.Id}/pairing-code", null);
            if (!response.IsSuccessStatusCode) { StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo generar el código."); return; }
            var result = await response.Content.ReadFromJsonAsync<PairingCodeResult>();
            if (result is null) throw new InvalidOperationException("No se recibió el código.");
            new PairingCodeWindow(result.Code, $"Código para volver a emparejar {row.Name}. Vence en 10 minutos.") { Owner = this }.ShowDialog();
            await LoadAsync();
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo generar el código"); }
    }
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    private sealed record RegisterRow(Guid Id, string Name, bool IsActive, string? DeviceName, DateTimeOffset? LastConnectionAtUtc, string? ActiveUser, string? OpenShiftUser, DateTimeOffset? ShiftOpenedAtUtc, bool IsPaired)
    { public string Status { get; init; } = string.Empty; public string LastConnectionDisplay { get; init; } = string.Empty; public string ShiftDisplay { get; init; } = string.Empty; }
    private sealed record PairingCodeResult(string Code, DateTimeOffset ExpiresAtUtc);
}
