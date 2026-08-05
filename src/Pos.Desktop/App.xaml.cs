using System.Configuration;
using System.Data;
using System.Windows;

namespace Pos.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var login = new LoginWindow();
        MainWindow = login;
        login.Show();
    }
}

