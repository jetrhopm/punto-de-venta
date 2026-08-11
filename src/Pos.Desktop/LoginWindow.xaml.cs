using System.Net.Http.Json;
using System.Net.Http;
using System.Windows;

namespace Pos.Desktop;

public partial class LoginWindow : Window
{
    private static HttpClient Client => ApiClient.Client;

    public LoginWindow()
    {
        InitializeComponent();
        ServerText.Text = $"Servidor: {ApiClient.BaseUrl}";
        PasswordBox.Focus();
    }

    private void OnServerClick(object sender, RoutedEventArgs e)
    {
        var window = new ServerConnectionWindow { Owner = this };
        if (window.ShowDialog() == true) ServerText.Text = $"Servidor: {ApiClient.BaseUrl}";
    }

    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {
        MessageText.Text = "";
        try
        {
            var response = await Client.PostAsJsonAsync("/api/auth/login", new { userName = UserNameTextBox.Text, password = PasswordBox.Password });
            if (!response.IsSuccessStatusCode)
            {
                MessageText.Text = response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "Usuario o contraseña incorrectos." : "No se pudo iniciar sesion.";
                return;
            }
            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
            SessionContext.AccessToken = result?.AccessToken;
            SessionContext.DisplayName = result?.DisplayName;
            SessionContext.Permissions.Clear();
            if (result?.Permissions is not null) SessionContext.Permissions.UnionWith(result.Permissions);
            var mainWindow = new MainWindow();
            System.Windows.Application.Current.MainWindow = mainWindow;
            mainWindow.Show();
            Close();
        }
        catch (HttpRequestException) { MessageText.Text = "La API local no esta disponible."; }
    }

    private sealed record LoginResponse(Guid SessionId, string AccessToken, Guid UserId, string DisplayName, bool IsAdministrator, DateTimeOffset ExpiresAtUtc, List<string> Permissions);
}
