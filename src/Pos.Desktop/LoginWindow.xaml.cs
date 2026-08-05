using System.Net.Http.Json;
using System.Net.Http;
using System.Windows;

namespace Pos.Desktop;

public partial class LoginWindow : Window
{
    private static readonly HttpClient Client = new() { BaseAddress = new Uri("http://127.0.0.1:5000") };

    public LoginWindow()
    {
        InitializeComponent();
        PasswordBox.Focus();
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
