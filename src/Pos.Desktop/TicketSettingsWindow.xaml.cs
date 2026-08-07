using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Windows;
namespace Pos.Desktop;
public partial class TicketSettingsWindow : Window
{
    private static readonly HttpClient Client = new() { BaseAddress = new Uri("http://127.0.0.1:5000") };
    public TicketSettingsWindow() { InitializeComponent(); Loaded += OnLoaded; }
    private async void OnLoaded(object? sender, RoutedEventArgs e) { Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SessionContext.AccessToken); try { var settings = await Client.GetFromJsonAsync<TicketSettings>("/api/ticket-settings"); if (settings is not null) { HeaderBox.Text = settings.TicketHeader; FooterBox.Text = settings.TicketFooter; WidthBox.SelectedIndex = settings.TicketWidthMm == 58 ? 0 : 1; } } catch (Exception exception) { StatusText.Text = exception.Message; } }
    private async void OnSaveClick(object sender, RoutedEventArgs e) { var width = (WidthBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString(); if (!int.TryParse(width, out var widthMm)) { StatusText.Text = "Selecciona un ancho valido."; return; } try { var response = await Client.PutAsJsonAsync("/api/ticket-settings", new { header = HeaderBox.Text, footer = FooterBox.Text, widthMm }); response.EnsureSuccessStatusCode(); StatusText.Text = "Configuracion guardada."; } catch (Exception exception) { StatusText.Text = exception.Message; } }
    private sealed record TicketSettings(string TicketHeader, string TicketFooter, int TicketWidthMm);
}
