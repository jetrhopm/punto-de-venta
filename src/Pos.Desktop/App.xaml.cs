using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace Pos.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        Exit += (_, _) => BarcodeScannerService.Stop();

        var startup = new StartupWindow();
        MainWindow = startup;
        startup.Show();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException(e.Exception);
        MessageBox.Show(
            "Ocurrio un error en la pantalla actual. El programa seguira abierto.\n\nDetalle: " + e.Exception.Message,
            "JetVenta",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception) LogException(exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException(e.Exception);
        e.SetObserved();
    }

    private static void LogException(Exception exception)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta", "logs");
            Directory.CreateDirectory(directory);
            var message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {exception}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(directory, "desktop-error.log"), message, Encoding.UTF8);
        }
        catch
        {
            // Logging must never hide the original UI error.
        }
    }
}
