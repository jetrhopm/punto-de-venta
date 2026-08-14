using System.Diagnostics;
using System.Security.Principal;

namespace Pos.Setup;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            WriteLaunchLog($"Inicio de Setup.exe. Ruta: {Environment.ProcessPath}");
            if (!IsAdministrator())
            {
                var stage = Path.Combine(Path.GetTempPath(), "PuntoDeVenta-Setup", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage);
                var localSetup = Path.Combine(stage, "Setup.exe");
                File.Copy(Environment.ProcessPath!, localSetup, true);
                WriteLaunchLog($"Solicitando permisos de administrador. Copia local: {localSetup}");
                using var elevated = Process.Start(new ProcessStartInfo(localSetup, string.Join(' ', args.Select(QuoteArgument)))
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = stage
                });
                elevated?.WaitForExit();
                return elevated?.ExitCode ?? 1;
            }

            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            using var form = new InstallerForm(args.Any(argument => argument.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)));
            System.Windows.Forms.Application.Run(form);
            return 0;
        }
        catch (Exception exception)
        {
            WriteLaunchLog($"ERROR de arranque: {exception}");
            System.Windows.Forms.MessageBox.Show(
                $"No se pudo abrir el instalador.\r\n\r\n{exception.Message}\r\n\r\nRevisa C:\\ProgramData\\PuntoDeVenta\\logs\\setup-launch.log",
                "JetVenta",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static string QuoteArgument(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static bool IsAdministrator() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    private static void WriteLaunchLog(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta", "logs", "setup-launch.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
