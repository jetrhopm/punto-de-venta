using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class DepartmentManagerWindow : Window
{
    private DepartmentRow? _selected;
    public DepartmentManagerWindow() { InitializeComponent(); Loaded += async (_, _) => await LoadAsync(); }
    private async Task LoadAsync()
    {
        try { DepartmentsGrid.ItemsSource = await ApiClient.Client.GetFromJsonAsync<List<DepartmentRow>>("/api/departments") ?? []; StatusText.Text = "Los departamentos se pueden cambiar sin afectar el historial."; }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudieron cargar los departamentos"); }
    }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) { if (DepartmentsGrid.SelectedItem is DepartmentRow row) { _selected = row; NameBox.Text = row.Name; } }
    private void OnNewClick(object sender, RoutedEventArgs e) { _selected = null; DepartmentsGrid.SelectedItem = null; NameBox.Clear(); NameBox.Focus(); }
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { StatusText.Text = "Escribe un nombre."; return; }
        try
        {
            using var response = _selected is null ? await ApiClient.Client.PostAsJsonAsync("/api/departments", new { name = NameBox.Text.Trim() }) : await ApiClient.Client.PutAsJsonAsync($"/api/departments/{_selected.Id}", new { name = NameBox.Text.Trim() });
            if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; }
            StatusText.Text = "Departamento guardado."; await LoadAsync(); OnNewClick(sender, e);
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo guardar"); }
    }
    private async void OnDeactivateClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) { StatusText.Text = "Selecciona un departamento."; return; }
        if (MessageBox.Show($"¿Desactivar {_selected.Name}? Los productos conservarán su historial.", "Desactivar departamento", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { using var response = await ApiClient.Client.DeleteAsync($"/api/departments/{_selected.Id}"); if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; } await LoadAsync(); OnNewClick(sender, e); StatusText.Text = "Departamento desactivado."; }
        catch (Exception exception) { StatusText.Text = $"No se pudo desactivar: {exception.Message}"; }
    }
    private sealed record DepartmentRow(Guid Id, string Name, bool IsActive);
}
