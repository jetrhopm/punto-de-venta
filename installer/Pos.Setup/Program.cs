using System.Diagnostics;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Principal;
using System.Windows;

namespace Pos.Setup;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            WriteLaunchLog($"Inicio del Setup.exe. Ruta: {Environment.ProcessPath}");
            if (!IsAdministrator())
            {
                var stage = Path.Combine(Path.GetTempPath(), "PuntoDeVenta-Setup", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage);
                var local = Path.Combine(stage, "Setup.exe");
                File.Copy(Environment.ProcessPath!, local, true);
                WriteLaunchLog($"Solicitando elevación. Copia local: {local}");
                using var elevated = Process.Start(new ProcessStartInfo(local, string.Join(' ', args.Select(QuoteArgument)))
                { UseShellExecute = true, Verb = "runas", WorkingDirectory = stage });
                elevated?.WaitForExit();
                return elevated?.ExitCode ?? 1;
            }
            WriteLaunchLog("Creando la aplicación WPF.");
            var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            WriteLaunchLog("Creando la ventana del instalador.");
            var window = new SetupWindow(args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)));
            application.MainWindow = window;
            WriteLaunchLog("Mostrando la ventana del instalador.");
            window.Show();
            var result = application.Run();
            WriteLaunchLog($"La ventana terminó. Código: {result}");
            return result;
        }
        catch (Exception exception)
        {
            var diagnostic = Path.Combine(Path.GetTempPath(), "PuntoDeVenta-Setup-error.log");
            try { File.WriteAllText(diagnostic, $"[{DateTime.Now:O}] {exception}\r\n"); } catch { }
            WriteLaunchLog($"ERROR de arranque: {exception}");
            MessageBox.Show($"No se pudo abrir el instalador.\r\n\r\n{exception.Message}\r\n\r\nDiagnóstico: {diagnostic}", "Punto de Venta", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    private static bool IsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    internal static string QuoteArgument(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
    private static void WriteLaunchLog(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta", "logs", "setup-launch.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n");
        }
        catch { }
    }
}

public partial class SetupWindow : Window
{
    private const string ProductName = "Punto de Venta";
    private readonly string _installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProductName);
    private readonly string _dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta");
    private readonly string _logPath;
    private readonly bool _uninstall;

    public SetupWindow(bool uninstall)
    {
        InitializeComponent();
        _uninstall = uninstall;
        _logPath = Path.Combine(_dataRoot, "logs", "setup.log");
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        InstallPathText.Text = _installRoot;
        if (_uninstall)
        {
            Title = "Desinstalar Punto de Venta";
            HeaderText.Text = "Desinstalar Punto de Venta";
            TermsCheck.Visibility = Visibility.Collapsed;
            AdminPanel.Visibility = Visibility.Collapsed;
            DependenciesPanel.Visibility = Visibility.Collapsed;
            InstallButton.Content = "Desinstalar";
            TermsHint.Text = "La base de datos, configuración y respaldos se conservarán.";
        }
    }

    private async void OnInstallClick(object sender, RoutedEventArgs e)
    {
        if (!_uninstall && !TermsCheck.IsChecked.GetValueOrDefault())
        {
            StatusText.Text = "Debes aceptar los términos y condiciones para continuar.";
            return;
        }
        if (!_uninstall && (string.IsNullOrWhiteSpace(AdminNameBox.Text) || string.IsNullOrEmpty(PasswordBox.Password)))
        {
            StatusText.Text = "Escribe el nombre del administrador y la contraseña.";
            return;
        }
        InstallButton.IsEnabled = false;
        TermsCheck.IsEnabled = false;
        try
        {
            await RunInstallationAsync();
            Progress.Value = 100;
            StatusText.Text = _uninstall ? "Desinstalación terminada. Tus datos se conservaron." : "Instalación terminada correctamente.";
            InstallButton.Content = "Cerrar";
            InstallButton.IsEnabled = true;
            InstallButton.Click -= OnInstallClick;
            InstallButton.Click += (_, _) => Close();
        }
        catch (Exception exception)
        {
            Log($"ERROR: {exception}");
            StatusText.Text = $"La operación no terminó. Revisa el registro: {_logPath}";
            InstallButton.IsEnabled = true;
        }
    }

    private async Task RunInstallationAsync()
    {
        if (_uninstall)
        {
            SetProgress(10, "Deteniendo servicios de Punto de Venta...");
            await RunPowerShellAsync(Path.Combine(_installRoot, "install-production.ps1"), "-Uninstall", _installRoot);
            SetProgress(85, "Conservando base de datos, configuración y respaldos...");
            RemoveWindowsInstallerRegistration();
            TryDeleteApplicationFiles();
            SetProgress(100, "Desinstalación terminada.");
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"PuntoDeVenta-Setup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            SetProgress(3, "Preparando el paquete de instalación...");
            await ExtractPayloadAsync(tempRoot);
            await CopyPayloadAsync(tempRoot, _installRoot);
        }
        finally { try { Directory.Delete(tempRoot, true); } catch { } }

        File.Copy(Environment.ProcessPath!, Path.Combine(_installRoot, "Setup.exe"), true);
        RegisterWindowsInstaller();
        CreateShortcuts();
        var vcRedist = Path.Combine(_installRoot, "vc_redist.x64.exe");
        if (File.Exists(vcRedist))
        {
            SetProgress(73, "Instalando dependencia: Microsoft Visual C++ Redistributable...");
            await RunProcessAsync(vcRedist, "/install /quiet /norestart", _installRoot);
        }
        SetProgress(80, "Configurando PostgreSQL y la API como servicios de Windows...");
        await RunPowerShellAsync(Path.Combine(_installRoot, "install-production.ps1"), "", _installRoot);
        await ConfigureInitialAdministratorAsync();
        SetProgress(100, "Instalación terminada.");
    }

    private async Task ConfigureInitialAdministratorAsync()
    {
        SetProgress(94, "Guardando la configuración inicial del administrador...");
        using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5000"), Timeout = TimeSpan.FromSeconds(10) };
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                var status = await client.GetFromJsonAsync<SetupStatus>("/api/setup/status");
                if (status?.Configured == true) return;
                if (status is not null)
                {
                    var response = await client.PostAsJsonAsync("/api/setup/initial", new
                    {
                        storeName = StoreNameBox.Text.Trim(), businessType = "Comercio general", userName = "admin",
                        password = PasswordBox.Password, administratorName = AdminNameBox.Text.Trim(), registerName = "Caja 1"
                    });
                    if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Conflict)
                        throw new InvalidOperationException("No se pudo crear el administrador inicial.");
                    return;
                }
            }
            catch (HttpRequestException) { await Task.Delay(1000); }
        }
        throw new InvalidOperationException("La API no respondió después de configurar los servicios.");
    }

    private async Task ExtractPayloadAsync(string destination)
    {
        await using var resource = typeof(Program).Assembly.GetManifestResourceStream("PuntoDeVenta.Payload.zip") ?? throw new InvalidOperationException("No se encontró el paquete interno.");
        using var archive = new ZipArchive(resource, ZipArchiveMode.Read);
        var total = archive.Entries.Sum(entry => Math.Max(0, entry.Length)); long complete = 0;
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Paquete inválido.");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open(); await using var output = File.Create(target); var buffer = new byte[64 * 1024]; int read;
            while ((read = await input.ReadAsync(buffer)) > 0) { await output.WriteAsync(buffer.AsMemory(0, read)); complete += read; SetProgress(3 + (int)(complete * 55 / Math.Max(1, total)), $"Copiando: {entry.FullName}"); }
        }
    }

    private async Task CopyPayloadAsync(string source, string destination)
    {
        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToList();
        for (var i = 0; i < files.Count; i++) { var relative = Path.GetRelativePath(source, files[i]); var target = Path.Combine(destination, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(files[i], target, true); SetProgress(58 + i * 15 / Math.Max(1, files.Count), $"Instalando archivo: {relative}"); await Task.Yield(); }
    }

    private async Task RunPowerShellAsync(string script, string extraArguments, string workingDirectory)
    {
        if (!File.Exists(script)) throw new FileNotFoundException("No existe el script de producción.", script);
        await RunProcessAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"), $"-NoProfile -ExecutionPolicy Bypass -File {Program.QuoteArgument(script)} -InstallRoot {Program.QuoteArgument(workingDirectory)} {extraArguments}", workingDirectory);
    }

    private async Task RunProcessAsync(string fileName, string arguments, string workingDirectory)
    {
        Log($"Ejecutando {Path.GetFileName(fileName)}");
        using var process = Process.Start(new ProcessStartInfo { FileName = fileName, Arguments = arguments, WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }) ?? throw new InvalidOperationException($"No se pudo iniciar {fileName}.");
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Dispatcher.Invoke(() => { Log(e.Data); StatusText.Text = e.Data; }); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Dispatcher.Invoke(() => Log(e.Data)); };
        process.BeginOutputReadLine(); process.BeginErrorReadLine(); await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(fileName)} terminó con código {process.ExitCode}.");
    }

    private void RegisterWindowsInstaller()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PuntoDeVenta");
        key?.SetValue("DisplayName", ProductName); key?.SetValue("DisplayVersion", "2.1.0"); key?.SetValue("Publisher", ProductName); key?.SetValue("InstallLocation", _installRoot); key?.SetValue("UninstallString", Program.QuoteArgument(Path.Combine(_installRoot, "Setup.exe")) + " /uninstall"); key?.SetValue("ModifyPath", Program.QuoteArgument(Path.Combine(_installRoot, "Setup.exe"))); key?.SetValue("NoModify", 0, Microsoft.Win32.RegistryValueKind.DWord); key?.SetValue("NoRepair", 0, Microsoft.Win32.RegistryValueKind.DWord);
    }

    private static void RemoveWindowsInstallerRegistration()
    {
        try { Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PuntoDeVenta", false); }
        catch { }
    }

    private void CreateShortcuts()
    {
        var target = Path.Combine(_installRoot, "client", "Pos.Desktop.exe");
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
        if (shell is null) return;
        var shellType = shell.GetType();
        void Create(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, [path]);
            shortcut!.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, [target]);
            shortcut.GetType().InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, [_installRoot]);
            shortcut.GetType().InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, shortcut, [Path.Combine(_installRoot, "client", "app.ico")]);
            shortcut.GetType().InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
        }
        if (DesktopShortcutCheck.IsChecked == true) Create(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Punto de Venta.lnk"));
        if (StartMenuShortcutCheck.IsChecked == true) Create(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Punto de Venta.lnk"));
    }

    private void TryDeleteApplicationFiles() { try { if (Directory.Exists(_installRoot)) Directory.Delete(_installRoot, true); } catch (Exception exception) { Log($"Aviso: no se pudo eliminar toda la carpeta de aplicación: {exception.Message}"); } }
    private void SetProgress(int value, string status) { Dispatcher.Invoke(() => { Progress.Value = Math.Clamp(value, 0, 100); StatusText.Text = status; }); Log(status); }
    private void Log(string message) { var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}"; File.AppendAllText(_logPath, line + Environment.NewLine); }
    private sealed record SetupStatus(bool Configured, string? StoreName);
}
