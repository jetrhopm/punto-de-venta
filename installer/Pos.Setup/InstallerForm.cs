using System.Diagnostics;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
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
    private readonly CheckBox _terms = CreateCheckBox("Acepto los términos y condiciones de JetVenta");
    private readonly CheckBox _desktopShortcut = CreateCheckBox("Crear acceso directo en el escritorio", true);
    private readonly CheckBox _startShortcut = CreateCheckBox("Crear acceso directo en el menú Inicio", true);
    private readonly CheckBox _startWithWindows = CreateCheckBox("Abrir JetVenta al iniciar Windows");
    private readonly Label _status = new() { AutoEllipsis = true, ForeColor = Color.FromArgb(194, 213, 230), BackColor = Color.Transparent };
    private readonly InstallerProgressBar _progress = new();
    private readonly TextBox _details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(14, 27, 43), ForeColor = Color.FromArgb(209, 225, 239), BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9f) };
    private readonly Button _action = CreateActionButton();
    private readonly Button _cancel = CreateSecondaryButton("Cancelar");
    private bool _busy;
    private bool _completed;

    public InstallerForm(bool uninstall)
    {
        _uninstall = uninstall;
        _existingInstallation = !uninstall && HasExistingInstallation();
        _installedVersion = GetInstalledVersion();
        Text = uninstall ? "Desinstalar JetVenta" : "Instalación de JetVenta";
        ClientSize = new Size(920, 670);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = Color.FromArgb(12, 23, 37);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        FormClosing += OnFormClosing;
        BuildInstallerLayout();
        if (_uninstall) BuildUninstallLayout(); else BuildInstallLayout();
        _action.Text = uninstall ? "Desinstalar" : _existingInstallation ? "Actualizar" : "Instalar";
        _action.SetBounds(704, 608, 174, 38);
        _cancel.SetBounds(598, 608, 96, 38);
        _action.Click += OnActionClick;
        _cancel.Click += OnCancelClick;
        Controls.AddRange([_status, _progress, _details, _cancel, _action]);
        SetProgress(0, uninstall
            ? "Listo para desinstalar. Los datos se conservarán."
            : _existingInstallation
                ? $"Instalación existente detectada{(_installedVersion is null ? string.Empty : $" (versión {_installedVersion})")}. Se actualizarán solo los archivos que cambien."
                : "Instalación nueva: se comprobarán e instalarán los componentes.");
    }

    private void BuildInstallerLayout()
    {
        Controls.Add(new Panel { BackColor = Color.FromArgb(0, 180, 210), Dock = DockStyle.Top, Height = 4 });
        Controls.Add(new PictureBox { Location = new Point(31, 26), Size = new Size(118, 118), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Image = LoadLogo() });
        Controls.Add(CreateLabel("INSTALACIÓN DE JETVENTA", new Point(164, 39), new Size(680, 34), 25, FontStyle.Bold, Color.White));
        Controls.Add(CreateLabel("Instalador autocontenido para Windows 10 y Windows 11 de 64 bits", new Point(166, 78), new Size(650, 24), 11, FontStyle.Regular, Color.FromArgb(158, 192, 220)));
        Controls.Add(CreateLabel("Todo lo necesario para operar se instala y configura en este equipo.", new Point(166, 104), new Size(650, 24), 10, FontStyle.Regular, Color.FromArgb(117, 155, 186)));
        Controls.Add(new Panel { BackColor = Color.FromArgb(34, 71, 101), Location = new Point(30, 156), Size = new Size(860, 1) });
    }

    private void BuildInstallLayout()
    {
        Controls.Add(CreateSectionTitle("Componentes incluidos", new Point(30, 180)));
        AddComponentRow("Base de datos local", "PostgreSQL protegido para la tienda", ComponentIconKind.Database, 30, 215);
        AddComponentRow("Servicio de JetVenta", "API local y procesos de operación", ComponentIconKind.Services, 30, 255);
        AddComponentRow("Cliente de escritorio", "Ventas, inventario, usuarios y reportes", ComponentIconKind.Desktop, 30, 295);
        AddComponentRow("Compatibilidad de Windows", "Microsoft Visual C++ Redistributable", ComponentIconKind.System, 30, 335);

        Controls.Add(CreateSectionTitle("Acuerdo de licencia", new Point(475, 180)));
        _terms.SetBounds(475, 214, 325, 42);
        var viewTerms = new LinkLabel { Text = "Ver términos y condiciones", Location = new Point(806, 225), Size = new Size(84, 30), LinkColor = Color.FromArgb(74, 205, 237), ActiveLinkColor = Color.White, VisitedLinkColor = Color.FromArgb(74, 205, 237), Font = new Font("Segoe UI", 9f, FontStyle.Underline), TextAlign = ContentAlignment.MiddleLeft };
        viewTerms.Click += (_, _) => ShowTerms();
        Controls.AddRange([_terms, viewTerms]);

        Controls.Add(CreateSectionTitle("Opciones de acceso", new Point(30, 385)));
        _desktopShortcut.SetBounds(30, 419, 355, 24);
        _startShortcut.SetBounds(30, 448, 355, 24);
        _startWithWindows.Checked = IsAutomaticStartEnabled();
        _startWithWindows.SetBounds(30, 477, 355, 24);
        Controls.AddRange([_desktopShortcut, _startShortcut, _startWithWindows]);

        Controls.Add(CreateSectionTitle("Ubicación de instalación", new Point(475, 277)));
        Controls.Add(new TextBox { Text = _installRoot, ReadOnly = true, TabStop = false, Location = new Point(475, 310), Size = new Size(415, 31), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(25, 43, 63), ForeColor = Color.FromArgb(219, 234, 247), Font = new Font("Segoe UI", 10f), Padding = new Padding(9, 4, 9, 4) });
        Controls.Add(CreateLabel("La ruta es fija para proteger actualizaciones, servicios y respaldos.", new Point(475, 346), new Size(415, 30), 9, FontStyle.Regular, Color.FromArgb(137, 169, 195)));

        Controls.Add(CreateSectionTitle("Información de la instalación", new Point(475, 390)));
        Controls.Add(CreateLabel("Se revisarán los componentes existentes y solo se instalarán o actualizarán los que hagan falta. Al finalizar, JetVenta abrirá la configuración inicial si la tienda aún no existe.", new Point(475, 424), new Size(415, 70), 10, FontStyle.Regular, Color.FromArgb(206, 221, 234)));

        _status.SetBounds(30, 512, 860, 22);
        _progress.SetBounds(30, 539, 860, 18);
        _details.SetBounds(30, 565, 860, 35);
    }

    private void BuildUninstallLayout()
    {
        Controls.Add(CreateSectionTitle("Desinstalación segura", new Point(30, 180)));
        Controls.Add(CreateLabel("Se retirarán el programa y sus servicios de Windows. La información de la tienda, la base de datos y los respaldos se conservarán para que puedas restaurarlos o reinstalar JetVenta después.", new Point(30, 220), new Size(830, 60), 11, FontStyle.Regular, Color.FromArgb(211, 226, 239)));
        Controls.Add(CreateSectionTitle("Actividad", new Point(30, 320)));
        _status.SetBounds(30, 358, 860, 24);
        _progress.SetBounds(30, 388, 860, 18);
        _details.SetBounds(30, 420, 860, 175);
    }

    private void AddComponentRow(string title, string subtitle, ComponentIconKind kind, int x, int y)
    {
        Controls.Add(new ComponentIcon(kind, Color.FromArgb(57, 179, 221)) { Location = new Point(x, y), Size = new Size(31, 31) });
        Controls.Add(CreateLabel(title, new Point(x + 43, y - 1), new Size(340, 20), 11, FontStyle.Bold, Color.FromArgb(239, 247, 252)));
        Controls.Add(CreateLabel(subtitle, new Point(x + 43, y + 18), new Size(340, 18), 9, FontStyle.Regular, Color.FromArgb(142, 177, 205)));
    }

    private static Label CreateSectionTitle(string text, Point location) =>
        CreateLabel(text, location, new Size(390, 24), 13, FontStyle.Bold, Color.FromArgb(248, 252, 255));

    private static Label CreateLabel(string text, Point location, Size size, float fontSize, FontStyle style, Color color) => new()
    {
        Text = text, Location = location, Size = size, ForeColor = color, BackColor = Color.Transparent,
        Font = new Font("Segoe UI", fontSize, style), AutoEllipsis = true
    };

    private static CheckBox CreateCheckBox(string text, bool isChecked = false) => new()
    {
        Text = text, Checked = isChecked, AutoSize = false, ForeColor = Color.FromArgb(227, 238, 247), BackColor = Color.Transparent,
        Font = new Font("Segoe UI", 10f), UseVisualStyleBackColor = false
    };

    private static Button CreateActionButton() => new()
    {
        FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 141, 194), ForeColor = Color.White,
        Font = new Font("Segoe UI", 10f, FontStyle.Bold), FlatAppearance = { BorderSize = 0 }
    };

    private static Button CreateSecondaryButton(string text) => new()
    {
        Text = text, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(36, 57, 79), ForeColor = Color.FromArgb(226, 238, 248),
        Font = new Font("Segoe UI", 10f), FlatAppearance = { BorderColor = Color.FromArgb(66, 101, 130), BorderSize = 1 }
    };

    private static Image? LoadLogo()
    {
        using var stream = typeof(InstallerForm).Assembly.GetManifestResourceStream("JetVenta.Assets.app-icon-512.png");
        if (stream is null) return null;
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
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
        _cancel.Enabled = false;
        _terms.Enabled = false;
        try
        {
            if (_uninstall) await UninstallAsync(); else await InstallAsync();
            _completed = true;
            _action.Text = _uninstall ? "Cerrar" : "Abrir JetVenta";
            _action.Enabled = true;
            _cancel.Text = "Cerrar";
            _cancel.Enabled = true;
        }
        catch (Exception exception)
        {
            Log($"ERROR: {exception}");
            SetProgress(_progress.Value, $"No se completó la operación: {exception.Message}");
            _action.Text = "Reintentar";
            _action.Enabled = true;
            _cancel.Enabled = true;
            _terms.Enabled = true;
        }
        finally { _busy = false; }
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        if (!_busy) Close();
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
        ConfigureAutomaticStart(_startWithWindows.Checked);
        SetProgress(100, "Instalación terminada. Ya puedes abrir la configuración inicial.");
    }

    private async Task UninstallAsync()
    {
        EnsureDesktopClosedForUpdate();
        var script = Path.Combine(_installRoot, "install-production.ps1");
        if (File.Exists(script)) await RunPowerShellAsync(script, "-Uninstall");
        Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PuntoDeVenta", false);
        DeleteShortcuts();
        ConfigureAutomaticStart(false);
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

    private void ConfigureAutomaticStart(bool enabled)
    {
        const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "JetVenta";
        using var key = Registry.CurrentUser.CreateSubKey(runKeyPath);
        if (enabled)
        {
            var target = Path.Combine(_installRoot, "client", "Pos.Desktop.exe");
            key?.SetValue(valueName, Program.QuoteArgument(target), RegistryValueKind.String);
            Log("Inicio automático de JetVenta activado para este usuario de Windows.");
        }
        else
        {
            key?.DeleteValue(valueName, false);
            Log("Inicio automático de JetVenta desactivado para este usuario de Windows.");
        }
    }

    private static bool IsAutomaticStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("JetVenta") is string value && !string.IsNullOrWhiteSpace(value);
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

internal enum ComponentIconKind { Database, Services, Desktop, System }

internal sealed class ComponentIcon : Control
{
    private readonly ComponentIconKind _kind;
    private readonly Color _accentColor;

    public ComponentIcon(ComponentIconKind kind, Color accentColor)
    {
        _kind = kind;
        _accentColor = accentColor;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(_accentColor, 2f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        var x = 4;
        var y = 4;
        var width = Width - 8;
        var height = Height - 8;

        switch (_kind)
        {
            case ComponentIconKind.Database:
                graphics.DrawEllipse(pen, x + 3, y, width - 6, 8);
                graphics.DrawLine(pen, x + 3, y + 4, x + 3, y + height - 4);
                graphics.DrawLine(pen, x + width - 3, y + 4, x + width - 3, y + height - 4);
                graphics.DrawArc(pen, x + 3, y + height - 8, width - 6, 8, 0, 180);
                graphics.DrawArc(pen, x + 3, y + 8, width - 6, 8, 0, 180);
                break;
            case ComponentIconKind.Services:
                graphics.DrawEllipse(pen, x, y + 11, 6, 6);
                graphics.DrawEllipse(pen, x + width - 6, y + 2, 6, 6);
                graphics.DrawEllipse(pen, x + width - 6, y + height - 8, 6, 6);
                graphics.DrawLine(pen, x + 6, y + 14, x + width - 6, y + 5);
                graphics.DrawLine(pen, x + 6, y + 14, x + width - 6, y + height - 5);
                break;
            case ComponentIconKind.Desktop:
                graphics.DrawRoundedRectangle(pen, new Rectangle(x, y + 2, width, height - 11), 3);
                graphics.DrawLine(pen, x + width / 2, y + height - 9, x + width / 2, y + height - 3);
                graphics.DrawLine(pen, x + 7, y + height - 2, x + width - 7, y + height - 2);
                break;
            case ComponentIconKind.System:
                graphics.DrawEllipse(pen, x + 7, y + 7, width - 14, height - 14);
                for (var index = 0; index < 8; index++)
                {
                    var angle = (float)(Math.PI * 2 * index / 8);
                    var centerX = x + width / 2f;
                    var centerY = y + height / 2f;
                    graphics.DrawLine(pen, centerX + (float)Math.Cos(angle) * 9, centerY + (float)Math.Sin(angle) * 9, centerX + (float)Math.Cos(angle) * 13, centerY + (float)Math.Sin(angle) * 13);
                }
                break;
        }
    }
}

internal sealed class InstallerProgressBar : Control
{
    private int _value;
    private const int Minimum = 0;
    private const int Maximum = 100;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set { _value = Math.Clamp(value, Minimum, Maximum); Invalidate(); }
    }

    public InstallerProgressBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Height = 18;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var track = new SolidBrush(Color.FromArgb(29, 50, 70));
        using var border = new Pen(Color.FromArgb(59, 89, 116));
        graphics.FillRoundedRectangle(track, ClientRectangle, 5);
        graphics.DrawRoundedRectangle(border, ClientRectangle, 5);
        var ratio = Maximum <= Minimum ? 0 : (Value - Minimum) / (float)(Maximum - Minimum);
        var fill = Rectangle.FromLTRB(1, 1, Math.Max(1, (int)((Width - 2) * ratio)), Height - 1);
        using var brush = new LinearGradientBrush(fill, Color.FromArgb(9, 174, 207), Color.FromArgb(21, 207, 149), LinearGradientMode.Horizontal);
        graphics.FillRoundedRectangle(brush, fill, 4);
    }
}

internal static class InstallerGraphicsExtensions
{
    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, Rectangle rectangle, int radius)
    {
        using var path = RoundedPath(rectangle, radius);
        graphics.DrawPath(pen, path);
    }

    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle rectangle, int radius)
    {
        using var path = RoundedPath(rectangle, radius);
        graphics.FillPath(brush, path);
    }

    private static GraphicsPath RoundedPath(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1, radius * 2);
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
