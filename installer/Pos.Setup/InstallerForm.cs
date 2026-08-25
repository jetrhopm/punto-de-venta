using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Pos.Setup;

public sealed class InstallerForm : Form
{
    private const string ProductTitle = "JetVenta";
    private readonly string _installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProductTitle);
    private readonly string _dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta");
    private readonly bool _uninstall;
    private readonly bool _existingInstallation;
    private readonly string? _installedVersion;
    private readonly CheckBox _terms = new() { Text = "Acepto los términos y condiciones", AutoSize = true };
    private readonly CheckBox _desktopShortcut = new() { Text = "Crear acceso directo en el escritorio", AutoSize = true, Checked = true };
    private readonly CheckBox _startShortcut = new() { Text = "Crear acceso directo en el menú Inicio", AutoSize = true, Checked = true };
    private readonly Label _status = new() { AutoEllipsis = true };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100 };
    private readonly TextBox _details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White };
    private readonly Button _action = new();
    private bool _busy;
    private bool _completed;

    public InstallerForm(bool uninstall)
    {
        _uninstall = uninstall;
        _existingInstallation = !uninstall && HasExistingInstallation();
        _installedVersion = GetInstalledVersion();
        Text = uninstall ? "Desinstalar JetVenta" : "Instalación de JetVenta";
        ClientSize = new Size(760, 650);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        FormClosing += OnFormClosing;

        var logo = new PictureBox { Location = new Point(30, 24), Size = new Size(64, 64), SizeMode = PictureBoxSizeMode.Zoom };
        logo.Image = Icon?.ToBitmap();
        Controls.Add(logo);
        Controls.Add(new Label { Text = Text, Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = Color.FromArgb(23, 50, 77), Location = new Point(110, 28), AutoSize = true });
        Controls.Add(new Label { Text = "Instalador autocontenido para Windows x64", Location = new Point(112, 70), AutoSize = true, ForeColor = Color.FromArgb(82, 101, 119) });

        if (uninstall)
        {
            Controls.Add(new Label { Text = "Se quitarán la aplicación y sus servicios. La base de datos, configuración y respaldos se conservarán.", Location = new Point(30, 120), Size = new Size(700, 45) });
        }
        else
        {
            Controls.Add(new Label { Text = "Incluye PostgreSQL, API local, cliente de escritorio y Microsoft Visual C++ Redistributable. Las dependencias válidas se detectan y se conservan.", Location = new Point(30, 115), Size = new Size(700, 45) });
            Controls.Add(new Label { Text = "Al terminar podrás abrir el asistente para crear la tienda, el administrador y decidir cómo cargar el inventario.", Location = new Point(30, 160), Size = new Size(700, 40) });
            _terms.SetBounds(30, 210, 350, 25);
            var viewTerms = new Button { Text = "Ver términos y condiciones", AutoSize = true, Location = new Point(390, 207) };
            viewTerms.Click += (_, _) => ShowTerms();
            _desktopShortcut.SetBounds(30, 250, 300, 25);
            _startShortcut.SetBounds(370, 250, 300, 25);
            Controls.AddRange([_terms, viewTerms, _desktopShortcut, _startShortcut]);
            Controls.Add(new Label { Text = $"Carpeta de instalación: {_installRoot}", Location = new Point(30, 288), Size = new Size(700, 25) });
        }

        _status.SetBounds(30, 325, 700, 28);
        _progress.SetBounds(30, 360, 700, 24);
        _details.SetBounds(30, 400, 700, 175);
        _action.Text = uninstall ? "Desinstalar" : _existingInstallation ? "Actualizar" : "Instalar";
        _action.SetBounds(540, 595, 190, 38);
        _action.Click += OnActionClick;
        Controls.AddRange([_status, _progress, _details, _action]);
        SetProgress(0, uninstall
            ? "Listo para desinstalar. Los datos se conservarán."
            : _existingInstallation
                ? $"Instalación existente detectada{(_installedVersion is null ? string.Empty : $" (versión {_installedVersion})")}. Se actualizarán solo los archivos que cambien."
                : "Instalación nueva: se comprobarán e instalarán los componentes.");
    }

    private async void OnActionClick(object? sender, EventArgs e)
    {
        if (_completed)
        {
            if (_uninstall) ScheduleApplicationRemoval(); else StartDesktop();
            Close();
            return;
        }
        if (!_uninstall && !_terms.Checked)
        {
            SetProgress(_progress.Value, "Debes aceptar los términos y condiciones para continuar.");
            return;
        }

        _busy = true;
        _action.Enabled = false;
        _terms.Enabled = false;
        try
        {
            if (_uninstall) await UninstallAsync(); else await InstallAsync();
            _completed = true;
            _action.Text = _uninstall ? "Cerrar" : "Abrir JetVenta";
            _action.Enabled = true;
        }
        catch (Exception exception)
        {
            Log($"ERROR: {exception}");
            SetProgress(_progress.Value, $"No se completó la operación: {exception.Message}");
            _action.Text = "Reintentar";
            _action.Enabled = true;
            _terms.Enabled = true;
        }
        finally { _busy = false; }
    }

    private async Task InstallAsync()
    {
        EnsureDesktopClosedForUpdate();
        var temporaryPayload = (string?)null;
        try
        {
            if (!_existingInstallation || !IsVisualCppInstalled())
            {
                temporaryPayload = Path.Combine(Path.GetTempPath(), "PuntoDeVenta-Setup", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporaryPayload);
                if (_existingInstallation)
                {
                    SetProgress(2, "Visual C++ no está instalado; preparando únicamente esa dependencia...");
                    await ExtractPayloadFileAsync(temporaryPayload, "vc_redist.x64.exe");
                }
                else
                {
                    SetProgress(2, "Extrayendo los componentes de la instalación nueva...");
                    await ExtractPayloadAsync(temporaryPayload);
                }
                await InstallVisualCppIfNeededAsync(temporaryPayload);
            }
            else
            {
                SetProgress(52, "Microsoft Visual C++ ya está instalado; se conserva.");
            }

            await StopServicesForUpdateAsync();
            if (_existingInstallation)
            {
                await UpdatePayloadAsync();
            }
            else
            {
                await CopyPayloadAsync(temporaryPayload!);
            }
            var installedSetup = Path.GetFullPath(Path.Combine(_installRoot, "Setup.exe"));
            if (!string.Equals(Path.GetFullPath(Environment.ProcessPath!), installedSetup, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(Environment.ProcessPath!, installedSetup, true);
            }
            else
            {
                Log("Setup.exe ya se está ejecutando desde la instalación; se conserva el ejecutable actual.");
            }
        }
        finally
        {
            if (temporaryPayload is not null)
            {
                try { Directory.Delete(temporaryPayload, true); } catch { }
            }
        }

        SetProgress(84, "Verificando PostgreSQL, base de datos y API...");
        await RunPowerShellAsync(Path.Combine(_installRoot, "install-production.ps1"), string.Empty);
        RegisterInstallation();
        CreateShortcuts();
        SetProgress(100, "Instalación terminada. Ya puedes abrir la configuración inicial.");
    }

    private async Task UninstallAsync()
    {
        EnsureDesktopClosedForUpdate();
        var script = Path.Combine(_installRoot, "install-production.ps1");
        if (File.Exists(script)) await RunPowerShellAsync(script, "-Uninstall");
        Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PuntoDeVenta", false);
        DeleteShortcuts();
        SetProgress(100, "Desinstalación terminada. Los datos y respaldos se conservaron.");
    }

    private static void ShowTerms()
    {
        using var form = new Form { Text = "Términos y condiciones de JetVenta", ClientSize = new Size(700, 500), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var text = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, BorderStyle = BorderStyle.None, DetectUrls = true };
        using var stream = typeof(InstallerForm).Assembly.GetManifestResourceStream("JetVenta.LICENSE.rtf");
        if (stream is not null) text.LoadFile(stream, RichTextBoxStreamType.RichText);
        else text.Text = "No se pudo cargar el documento de términos y condiciones.";
        form.Controls.Add(text);
        form.ShowDialog();
    }

    private static void EnsureDesktopClosedForUpdate()
    {
        var running = Process.GetProcessesByName("Pos.Desktop");
        try
        {
            if (running.Length > 0)
            {
                throw new InvalidOperationException(
                    "JetVenta está abierto. Cierra primero la ventana del punto de venta y vuelve a iniciar la actualización. " +
                    "Esto protege los tickets en atención y evita copiar archivos mientras el programa está usándolos.");
            }
        }
        finally
        {
            foreach (var process in running) process.Dispose();
        }
    }

    private async Task ExtractPayloadAsync(string destination)
    {
        await using var resource = typeof(InstallerForm).Assembly.GetManifestResourceStream("PuntoDeVenta.Payload.zip") ?? throw new InvalidOperationException("No se encontró el paquete interno.");
        using var archive = new ZipArchive(resource, ZipArchiveMode.Read);
        var total = archive.Entries.Sum(entry => Math.Max(0, entry.Length));
        long complete = 0;
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("El paquete interno contiene una ruta inválida.");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = File.Create(target);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read));
                complete += read;
                SetProgress(2 + (int)(complete * 48 / Math.Max(1, total)), $"Extrayendo: {entry.FullName}");
            }
        }
    }

    private async Task ExtractPayloadFileAsync(string destination, string fileName)
    {
        await using var resource = typeof(InstallerForm).Assembly.GetManifestResourceStream("PuntoDeVenta.Payload.zip") ?? throw new InvalidOperationException("No se encontró el paquete interno.");
        using var archive = new ZipArchive(resource, ZipArchiveMode.Read);
        var entry = archive.GetEntry(fileName) ?? throw new InvalidOperationException($"No se encontró {fileName} dentro del paquete interno.");
        var target = Path.Combine(destination, fileName);
        await using var input = entry.Open();
        await using var output = File.Create(target);
        await input.CopyToAsync(output);
    }

    private async Task InstallVisualCppIfNeededAsync(string payloadRoot)
    {
        if (IsVisualCppInstalled())
        {
            SetProgress(52, "Microsoft Visual C++ ya está instalado; se conserva.");
            return;
        }
        SetProgress(52, "Instalando Microsoft Visual C++ Redistributable...");
        await RunProcessAsync(Path.Combine(payloadRoot, "vc_redist.x64.exe"), "/install /quiet /norestart", payloadRoot);
    }

    private async Task StopServicesForUpdateAsync()
    {
        SetProgress(56, "Deteniendo servicios anteriores para actualizar archivos...");
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        await RunProcessAsync(powershell, "-NoProfile -ExecutionPolicy Bypass -Command \"$ErrorActionPreference='SilentlyContinue'; Get-Service -Name 'PuntoDeVentaApi','PuntoDeVentaPostgreSQL' -ErrorAction SilentlyContinue | Stop-Service -Force -ErrorAction SilentlyContinue; exit 0\"", Path.GetDirectoryName(powershell)!);
        await Task.Delay(TimeSpan.FromSeconds(3));
    }

    private async Task CopyPayloadAsync(string source)
    {
        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
        for (var index = 0; index < files.Length; index++)
        {
            var relative = Path.GetRelativePath(source, files[index]);
            var target = Path.Combine(_installRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await CopyWithRetryAsync(files[index], target, relative);
            SetProgress(58 + index * 24 / Math.Max(1, files.Length), $"Instalando: {relative}");
        }
    }

    private async Task UpdatePayloadAsync()
    {
        await using var resource = typeof(InstallerForm).Assembly.GetManifestResourceStream("PuntoDeVenta.Payload.zip") ?? throw new InvalidOperationException("No se encontró el paquete interno.");
        using var archive = new ZipArchive(resource, ZipArchiveMode.Read);
        var entries = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToArray();
        var changed = 0;

        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(_installRoot, relative));
            var installRoot = Path.GetFullPath(_installRoot) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("El paquete interno contiene una ruta inválida.");

            if (await PayloadEntryMatchesAsync(entry, target))
            {
                SetProgress(58 + index * 24 / Math.Max(1, entries.Length), $"Sin cambios: {relative}");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
            await input.CopyToAsync(output);
            changed++;
            SetProgress(58 + index * 24 / Math.Max(1, entries.Length), $"Actualizando: {relative}");
        }

        Log(changed == 0
            ? "Actualización verificada: todos los archivos instalados ya estaban actualizados."
            : $"Actualización aplicada: se reemplazaron o agregaron {changed} archivo(s); los demás se conservaron.");
    }

    private static async Task<bool> PayloadEntryMatchesAsync(ZipArchiveEntry entry, string target)
    {
        if (!File.Exists(target) || new FileInfo(target).Length != entry.Length) return false;

        await using var payloadStream = entry.Open();
        await using var installedStream = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        var payloadHash = await System.Security.Cryptography.SHA256.HashDataAsync(payloadStream);
        var installedHash = await System.Security.Cryptography.SHA256.HashDataAsync(installedStream);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(payloadHash, installedHash);
    }

    private async Task CopyWithRetryAsync(string source, string target, string relative)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { File.Copy(source, target, true); return; }
            catch (IOException) when (attempt < 16)
            {
                SetProgress(_progress.Value, $"Esperando que Windows libere: {relative}");
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
    }

    private async Task RunPowerShellAsync(string script, string extraArguments)
    {
        if (!File.Exists(script)) throw new FileNotFoundException("No existe el script de configuración.", script);
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        await RunProcessAsync(powershell, $"-NoProfile -ExecutionPolicy Bypass -File {Program.QuoteArgument(script)} -InstallRoot {Program.QuoteArgument(_installRoot)} {extraArguments}", _installRoot);
    }

    private async Task RunProcessAsync(string file, string arguments, string workingDirectory)
    {
        var processName = Path.GetFileName(file);
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        Log($"Ejecutando {processName}");
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException($"No se pudo iniciar {Path.GetFileName(file)}.");
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (string.IsNullOrWhiteSpace(eventArgs.Data)) return;
            standardOutput.AppendLine(eventArgs.Data);
            TryBeginInvoke(() => { Log(eventArgs.Data); _status.Text = eventArgs.Data; });
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (string.IsNullOrWhiteSpace(eventArgs.Data)) return;
            standardError.AppendLine(eventArgs.Data);
            TryBeginInvoke(() => Log(eventArgs.Data));
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        // WaitForExitAsync completes when the process exits, but redirected stream
        // callbacks can still be pending. Flush them before evaluating the result.
        process.WaitForExit();
        if (process.ExitCode is not (0 or 3010))
        {
            var details = BuildFailureDetails(processName, process.ExitCode, standardOutput, standardError);
            throw new InvalidOperationException(details);
        }
        if (process.ExitCode == 3010) Log("El componente solicita reiniciar Windows para completar su actualización.");
    }

    private string BuildFailureDetails(string processName, int exitCode, StringBuilder standardOutput, StringBuilder standardError)
    {
        var lines = new List<string>
        {
            $"{processName} terminó con código {exitCode}.",
            "La operación fue detenida para evitar una instalación incompleta.",
            $"Registro del instalador: {Path.Combine(_dataRoot, "logs", "setup.log")}",
            $"Registro de configuración: {Path.Combine(_dataRoot, "logs", "instalacion.log")}"
        };

        var output = LastLines(standardOutput.ToString(), 12);
        var error = LastLines(standardError.ToString(), 12);
        if (!string.IsNullOrWhiteSpace(output)) lines.Add($"Salida reciente: {SanitizeDiagnostic(output)}");
        if (!string.IsNullOrWhiteSpace(error)) lines.Add($"Error reciente: {SanitizeDiagnostic(error)}");

        var installationLog = Path.Combine(_dataRoot, "logs", "instalacion.log");
        if (File.Exists(installationLog))
        {
            var logTail = LastLines(File.ReadAllText(installationLog), 18);
            if (!string.IsNullOrWhiteSpace(logTail)) lines.Add($"Última etapa registrada: {SanitizeDiagnostic(logTail)}");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    private static string LastLines(string text, int count) =>
        string.Join(Environment.NewLine, text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).TakeLast(count));

    private static string SanitizeDiagnostic(string text)
    {
        var sanitized = Regex.Replace(text, "(?i)(password|token|secret|access[_ -]?token)\\s*([:=])\\s*[^;\\r\\n]+", "$1$2<oculto>");
        return sanitized.Length <= 3200 ? sanitized : sanitized[^3200..];
    }

    private void TryBeginInvoke(Action action)
    {
        try
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke(action);
        }
        catch (InvalidOperationException) { }
    }

    private void RegisterInstallation()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PuntoDeVenta");
        key?.SetValue("DisplayName", ProductTitle);
        key?.SetValue("DisplayVersion", version);
        key?.SetValue("Publisher", ProductTitle);
        key?.SetValue("InstallLocation", _installRoot);
        key?.SetValue("DisplayIcon", Path.Combine(_installRoot, "client", "app.ico"));
        key?.SetValue("UninstallString", $"{Program.QuoteArgument(Path.Combine(_installRoot, "Setup.exe"))} /uninstall");
        key?.SetValue("ModifyPath", Program.QuoteArgument(Path.Combine(_installRoot, "Setup.exe")));
        key?.SetValue("NoRepair", 0, RegistryValueKind.DWord);
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
            var shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [path]);
            shortcut!.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, [target]);
            shortcut.GetType().InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, [Path.GetDirectoryName(target)!]);
            shortcut.GetType().InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, [Path.Combine(_installRoot, "client", "app.ico")]);
            shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
        if (_desktopShortcut.Checked) Create(DesktopShortcutPath);
        if (_startShortcut.Checked) Create(StartShortcutPath);
    }

    private void DeleteShortcuts()
    {
        foreach (var path in new[] { DesktopShortcutPath, StartShortcutPath }) try { File.Delete(path); } catch { }
    }

    private void StartDesktop()
    {
        var desktop = Path.Combine(_installRoot, "client", "Pos.Desktop.exe");
        if (!File.Exists(desktop)) throw new FileNotFoundException("No se encontró la aplicación instalada.", desktop);
        Process.Start(new ProcessStartInfo(desktop) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(desktop)! });
    }

    private void ScheduleApplicationRemoval()
    {
        var expectedRoot = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProductTitle));
        if (!string.Equals(Path.GetFullPath(_installRoot), expectedRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("La carpeta de desinstalación no es válida.");

        var cleanup = Path.Combine(Path.GetTempPath(), $"PuntoDeVenta-remove-{Guid.NewGuid():N}.cmd");
        var commands = $"@echo off\r\ntimeout /t 3 /nobreak >nul\r\nrmdir /s /q \"{expectedRoot}\"\r\ndel \"%~f0\"\r\n";
        File.WriteAllText(cleanup, commands, Encoding.ASCII);
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), $"/d /c {Program.QuoteArgument(cleanup)}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath()
        });
    }

    private bool HasExistingInstallation() => File.Exists(Path.Combine(_installRoot, "client", "Pos.Desktop.exe")) || File.Exists(Path.Combine(_dataRoot, "postgresql", "data", "PG_VERSION"));

    private static string? GetInstalledVersion()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PuntoDeVenta");
        return key?.GetValue("DisplayVersion")?.ToString();
    }

    private static bool IsVisualCppInstalled()
    {
        using var runtime = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
        return runtime?.GetValue("Installed") is not null && Convert.ToInt32(runtime.GetValue("Installed")) == 1;
    }

    private string DesktopShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "JetVenta.lnk");
    private string StartShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "JetVenta.lnk");

    private void SetProgress(int value, string message)
    {
        if (IsDisposed) return;
        void Update() { _progress.Value = Math.Clamp(value, 0, 100); _status.Text = message; Log(message); }
        if (InvokeRequired) BeginInvoke(Update); else Update();
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (!_details.IsDisposed) _details.AppendText(line + Environment.NewLine);
        try
        {
            var path = Path.Combine(_dataRoot, "logs", "setup.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (!_busy) return;
        eventArgs.Cancel = true;
        MessageBox.Show("Espera a que termine la operación antes de cerrar el instalador.", ProductTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
