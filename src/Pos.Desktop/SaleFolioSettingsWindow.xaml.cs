using System.Net.Http.Json;
using System.Windows;

namespace Pos.Desktop;

public partial class SaleFolioSettingsWindow : Window
{
    private long _currentNextFolio;

    public SaleFolioSettingsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await ApiClient.Client.GetFromJsonAsync<SaleFolioSettings>("api/sale-folios");
            if (settings is null) return;
            _currentNextFolio = settings.NextFolio;
            CurrentNextFolioText.Text = settings.NextFolio.ToString("N0");
            LastFolioText.Text = settings.LastIssuedFolio == 0 ? "Sin ventas" : settings.LastIssuedFolio.ToString("N0");
            NextFolioBox.Text = settings.NextFolio.ToString();
            StatusText.Text = "Los cambios se aplican a las próximas ventas confirmadas.";
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudieron cargar los folios"); }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!long.TryParse(NextFolioBox.Text.Trim(), out var nextFolio) || nextFolio < 1)
        {
            StatusText.Text = "Escribe un número de folio válido.";
            return;
        }
        if (nextFolio < _currentNextFolio)
        {
            StatusText.Text = $"No puedes bajar el consecutivo actual ({_currentNextFolio:N0}).";
            return;
        }
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync("api/sale-folios", new { nextFolio });
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await response.Content.ReadAsStringAsync();
                return;
            }
            var settings = await response.Content.ReadFromJsonAsync<SaleFolioSettings>();
            if (settings is null) return;
            _currentNextFolio = settings.NextFolio;
            CurrentNextFolioText.Text = settings.NextFolio.ToString("N0");
            LastFolioText.Text = settings.LastIssuedFolio == 0 ? "Sin ventas" : settings.LastIssuedFolio.ToString("N0");
            NextFolioBox.Text = settings.NextFolio.ToString();
            StatusText.Text = "El siguiente folio fue actualizado correctamente.";
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo guardar el consecutivo"); }
    }

    private sealed record SaleFolioSettings(long NextFolio, long LastIssuedFolio);
}
