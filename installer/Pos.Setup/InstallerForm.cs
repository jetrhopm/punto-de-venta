using System.Diagnostics;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Pos.Setup;

public sealed class InstallerForm : Form
{
    private const string ProductTitle = "JetVenta";
    private const string DesktopExecutableName = "JetVenta.exe";
    private const string LegacyDesktopExecutableName = "Pos.Desktop.exe";
    private const string LicenseFileExtensionKey = @"SOFTWARE\Classes\.jv";
    private const string LicenseFileTypeKey = @"SOFTWARE\Classes\JetVenta.LicenseFile";
    private const string LicenseFileTypeName = "JetVenta.LicenseFile";
    private const string BackupFileExtensionKey = @"SOFTWARE\Classes\.bjv";
    private const string BackupFileTypeKey = @"SOFTWARE\Classes\JetVenta.BackupFile";
    private const string BackupFileTypeName = "JetVenta.BackupFile";
    private readonly string _installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProductTitle);
    private readonly string _dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta");
    private readonly bool _uninstall;
    private readonly bool _existingInstallation;
    private readonly string? _installedVersion;
    private readonly InstallationProfile _installationProfile;
    private InstallationMode _installationMode;
    private readonly CheckBox _terms = CreateCheckBox("Acepto los términos y condiciones de JetVenta");
    private readonly CheckBox _desktopShortcut = CreateCheckBox("Crear acceso directo en el escritorio", true);
    private readonly CheckBox _startShortcut = CreateCheckBox("Crear acceso directo en el menú Inicio", true);
    private readonly CheckBox _startWithWindows = CreateCheckBox("Abrir JetVenta al iniciar Windows");
    private readonly Label _status = new() { AutoEllipsis = true, ForeColor = Color.FromArgb(194, 213, 230), BackColor = Color.Transparent };
    private readonly InstallerProgressBar _progress = new();
    private readonly TextBox _details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(14, 27, 43), ForeColor = Color.FromArgb(209, 225, 239), BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9f) };
    private readonly Button _action = CreateActionButton();
    private readonly Button _cancel = CreateSecondaryButton("Cancelar");
    private readonly List<Control> _summaryControls = [];
    private readonly Label _activityTitle = CreateLabel(string.Empty, Point.Empty, Size.Empty, 19, FontStyle.Bold, Color.White);
    private readonly Label _activityDescription = CreateLabel(string.Empty, Point.Empty, Size.Empty, 10, FontStyle.Regular, Color.FromArgb(161, 193, 219));
    private readonly Label _progressValue = CreateLabel("0%", Point.Empty, Size.Empty, 10, FontStyle.Bold, Color.FromArgb(77, 209, 235));
    private readonly RadioButton _serverMode = CreateModeRadio("Caja principal / servidor");
    private readonly RadioButton _additionalMode = CreateModeRadio("Caja adicional");
    private readonly Label _modeStatus = CreateLabel(string.Empty, Point.Empty, Size.Empty, 9, FontStyle.Regular, Color.FromArgb(137, 169, 195));
    private readonly TextBox _serverAddress = CreateInput("192.168.1.10");
    private readonly TextBox _serverPort = CreateInput("5000");
    private readonly TextBox _pairingCode = CreateInput("Código de 6 dígitos");
    private readonly TextBox _registerName = CreateInput("Nombre de esta caja");
    private readonly List<Control> _additionalControls = [];
    private bool _busy;
    private bool _completed;

    public InstallerForm(bool uninstall)
    {
        _uninstall = uninstall;
        _existingInstallation = !uninstall && HasExistingInstallation();
        _installedVersion = GetInstalledVersion();
        _installationProfile = _existingInstallation ? InstallationProfileStore.ReadOrDefault() : new InstallationProfile(1, InstallationMode.Server);
        _installationMode = _installationProfile.Mode;
        Text = uninstall ? "Desinstalar JetVenta" : "Instalación de JetVenta";
        ClientSize = new Size(1020, 760);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = Color.FromArgb(12, 23, 37);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        FormClosing += OnFormClosing;
        BuildInstallerLayout();
        var summaryStart = Controls.Count;
        if (_uninstall) BuildUninstallLayout(); else BuildInstallLayout();
        _summaryControls.AddRange(Controls.Cast<Control>().Skip(summaryStart));
        _action.Text = uninstall ? "Desinstalar" : _existingInstallation ? "Actualizar" : "Instalar";
        _action.SetBounds(804, 698, 174, 38);
        _cancel.SetBounds(698, 698, 96, 38);
        _action.Click += OnActionClick;
        _cancel.Click += OnCancelClick;
        _activityTitle.Visible = false;
        _activityDescription.Visible = false;
        _progressValue.Visible = false;
        _status.Visible = false;
        _progress.Visible = false;
        _details.Visible = false;
        Controls.AddRange([_activityTitle, _activityDescription, _progressValue, _status, _progress, _details, _cancel, _action]);
        SetProgress(0, uninstall
            ? "Listo para desinstalar. Los datos se conservarán."
            : _existingInstallation
                ? $"Instalación existente detectada{(_installedVersion is null ? string.Empty : $" (versión {_installedVersion})")}. Se actualizarán solo los archivos que cambien."
                : "Instalación nueva: se comprobarán e instalarán los componentes.");
    }

    private void BuildInstallerLayout()
    {
        Controls.Add(new Panel { BackColor = Color.FromArgb(0, 180, 210), Dock = DockStyle.Top, Height = 4 });
        Controls.Add(new PictureBox { Location = new Point(31, 29), Size = new Size(250, 102), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Image = LoadBanner() });
        Controls.Add(CreateLabel("INSTALACIÓN DE JETVENTA", new Point(307, 39), new Size(550, 34), 25, FontStyle.Bold, Color.White));
        Controls.Add(CreateLabel("Instalador autocontenido para Windows 10 y Windows 11 de 64 bits", new Point(309, 78), new Size(550, 24), 11, FontStyle.Regular, Color.FromArgb(158, 192, 220)));
        Controls.Add(CreateLabel("Todo lo necesario para operar se instala y configura en este equipo.", new Point(309, 104), new Size(550, 24), 10, FontStyle.Regular, Color.FromArgb(117, 155, 186)));
        Controls.Add(new Panel { BackColor = Color.FromArgb(34, 71, 101), Location = new Point(30, 156), Size = new Size(960, 1) });
    }

    private void BuildInstallLayout()
    {
        Controls.Add(CreateSectionTitle("Tipo de instalación", new Point(30, 180)));
        _serverMode.SetBounds(30, 213, 390, 28);
        _additionalMode.SetBounds(30, 266, 390, 28);
        var serverHint = CreateLabel("Instala JetVenta, PostgreSQL, API, servicios y acceso de red privada.", new Point(57, 238), new Size(400, 24), 9, FontStyle.Regular, Color.FromArgb(142, 177, 205));
        var additionalHint = CreateLabel("Instala JetVenta y Windows necesarios; se conecta a una caja principal existente.", new Point(57, 291), new Size(400, 28), 9, FontStyle.Regular, Color.FromArgb(142, 177, 205));
        _modeStatus.SetBounds(30, 321, 420, 38);
        _serverMode.CheckedChanged += (_, _) => { if (_serverMode.Checked) SetInstallationMode(InstallationMode.Server); };
        _additionalMode.CheckedChanged += (_, _) => { if (_additionalMode.Checked) SetInstallationMode(InstallationMode.AdditionalRegister); };
        Controls.AddRange([_serverMode, _additionalMode, serverHint, additionalHint, _modeStatus]);

        Controls.Add(CreateSectionTitle("Componentes incluidos", new Point(30, 374)));
        AddComponentRow("Cliente de escritorio", "Ventas, inventario, usuarios y reportes", ComponentIconKind.Desktop, 30, 408);
        AddComponentRow("Compatibilidad de Windows", "Microsoft Visual C++ Redistributable", ComponentIconKind.System, 30, 448);
        AddComponentRow("Base y servicio locales", "Sólo se agregan en caja principal / servidor", ComponentIconKind.Database, 30, 488);

        Controls.Add(CreateSectionTitle("Acuerdo de licencia", new Point(500, 180)));
        _terms.SetBounds(500, 214, 310, 42);
        var viewTerms = new LinkLabel { Text = "Ver términos y condiciones", Location = new Point(816, 225), Size = new Size(174, 30), LinkColor = Color.FromArgb(74, 205, 237), ActiveLinkColor = Color.White, VisitedLinkColor = Color.FromArgb(74, 205, 237), Font = new Font("Segoe UI", 9f, FontStyle.Underline), TextAlign = ContentAlignment.MiddleLeft };
        viewTerms.Click += (_, _) => ShowTerms();
        Controls.AddRange([_terms, viewTerms]);

        AddAdditionalControl(CreateSectionTitle("Conexión de caja adicional", new Point(500, 277)));
        AddAdditionalControl(CreateLabel("Dirección del servidor", new Point(500, 310), new Size(205, 18), 9, FontStyle.Bold, Color.FromArgb(218, 232, 244)));
        _serverAddress.SetBounds(500, 332, 300, 31);
        _serverPort.SetBounds(810, 332, 80, 31);
        AddAdditionalControl(_serverAddress); AddAdditionalControl(_serverPort);
        AddAdditionalControl(CreateLabel("Código temporal generado por el administrador", new Point(500, 373), new Size(390, 18), 9, FontStyle.Bold, Color.FromArgb(218, 232, 244)));
        _pairingCode.SetBounds(500, 395, 185, 31);
        _pairingCode.MaxLength = 6;
        _pairingCode.CharacterCasing = CharacterCasing.Upper;
        AddAdditionalControl(_pairingCode);
        AddAdditionalControl(CreateLabel("Nombre de esta caja", new Point(700, 373), new Size(190, 18), 9, FontStyle.Bold, Color.FromArgb(218, 232, 244)));
        _registerName.SetBounds(700, 395, 190, 31);
        _registerName.MaxLength = 80;
        AddAdditionalControl(_registerName);
        AddAdditionalControl(CreateLabel("Se comprobará la conexión antes de terminar.", new Point(500, 433), new Size(190, 24), 9, FontStyle.Regular, Color.FromArgb(137, 169, 195)));
        AddAdditionalControl(CreateLabel("Puedes cambiarlo: por ejemplo, Caja 2 o Caja mostrador.", new Point(700, 433), new Size(290, 24), 8.5f, FontStyle.Regular, Color.FromArgb(137, 169, 195)));
        var networkHelp = new LinkLabel
        {
            Text = "Ver pasos para preparar la red privada",
            Location = new Point(500, 458),
            Size = new Size(300, 26),
            LinkColor = Color.FromArgb(74, 205, 237),
            ActiveLinkColor = Color.White,
            VisitedLinkColor = Color.FromArgb(74, 205, 237),
            Font = new Font("Segoe UI", 9f, FontStyle.Underline),
            TextAlign = ContentAlignment.MiddleLeft
        };
        networkHelp.Click += (_, _) => ShowAdditionalNetworkHelp();
        AddAdditionalControl(networkHelp);

        Controls.Add(CreateSectionTitle("Opciones de acceso", new Point(30, 540)));
        _desktopShortcut.SetBounds(30, 572, 355, 24);
        _startShortcut.SetBounds(30, 600, 355, 24);
        _startWithWindows.Checked = IsAutomaticStartEnabled();
        _startWithWindows.SetBounds(30, 628, 355, 24);
        Controls.AddRange([_desktopShortcut, _startShortcut, _startWithWindows]);

        Controls.Add(CreateSectionTitle("Ubicación de instalación", new Point(500, 500)));
        Controls.Add(new TextBox { Text = _installRoot, ReadOnly = true, TabStop = false, Location = new Point(500, 532), Size = new Size(490, 31), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(25, 43, 63), ForeColor = Color.FromArgb(219, 234, 247), Font = new Font("Segoe UI", 10f), Padding = new Padding(9, 4, 9, 4) });
        Controls.Add(CreateLabel("La ruta es fija para proteger las actualizaciones y la identidad de esta caja.", new Point(500, 568), new Size(490, 24), 9, FontStyle.Regular, Color.FromArgb(137, 169, 195)));

        _status.SetBounds(30, 660, 960, 22);
        _progress.SetBounds(30, 684, 960, 10);

        _serverMode.Checked = _installationMode == InstallationMode.Server;
        _additionalMode.Checked = _installationMode == InstallationMode.AdditionalRegister;
        _registerName.Text = Environment.MachineName;
        if (_installationProfile.Mode == InstallationMode.AdditionalRegister && Uri.TryCreate(_installationProfile.ServerBaseUrl, UriKind.Absolute, out var savedServer))
        {
            _serverAddress.Text = savedServer.Host;
            _serverPort.Text = savedServer.Port.ToString();
        }
        if (_existingInstallation)
        {
            _serverMode.Enabled = false;
            _additionalMode.Enabled = false;
        }
        UpdateModePresentation();
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

    private static void ShowAdditionalNetworkHelp()
    {
        const string message = "Antes de instalar una caja adicional:\n\n" +
                               "1. Conecta ambas computadoras al mismo router o switch. Por cable es más estable.\n\n" +
                               "2. En ambas abre Configuración de Windows > Red e Internet > Wi-Fi o Ethernet > Propiedades y selecciona Perfil de red: Privada.\n\n" +
                               "3. No uses red Pública, red de invitados ni Wi-Fi con aislamiento de equipos.\n\n" +
                               "4. En la caja principal evita que Windows la suspenda automáticamente.\n\n" +
                               "5. No compartas carpetas, no abras PostgreSQL, no desactives Firewall y no abras puertos en el router.\n\n" +
                               "6. En esta pantalla escribe la IP de la caja principal, no 127.0.0.1. El administrador la muestra en JetVenta > Configuración > Conectar caja.";
        MessageBox.Show(message, "Red privada para caja adicional", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static RadioButton CreateModeRadio(string text) => new()
    {
        Text = text,
        AutoSize = false,
        ForeColor = Color.FromArgb(238, 247, 253),
        BackColor = Color.Transparent,
        Font = new Font("Segoe UI", 11f, FontStyle.Bold),
        UseVisualStyleBackColor = false
    };

    private static TextBox CreateInput(string placeholder) => new()
    {
        PlaceholderText = placeholder,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.FromArgb(25, 43, 63),
        ForeColor = Color.FromArgb(219, 234, 247),
        Font = new Font("Segoe UI", 10f),
        Padding = new Padding(8, 4, 8, 4)
    };

    private void AddAdditionalControl(Control control)
    {
        _additionalControls.Add(control);
        Controls.Add(control);
    }

    private void SetInstallationMode(InstallationMode mode)
    {
        if (_existingInstallation) return;
        _installationMode = mode;
        UpdateModePresentation();
    }

    private void UpdateModePresentation()
    {
        var additional = _installationMode == InstallationMode.AdditionalRegister;
        foreach (var control in _additionalControls) control.Visible = additional && (!_existingInstallation || _installationProfile.PairedAtUtc is null);
        _modeStatus.ForeColor = Color.FromArgb(137, 169, 195);
        _modeStatus.Text = _existingInstallation
            ? additional && _installationProfile.PairedAtUtc is null
                ? "Instalación adicional pendiente de emparejar: captura un código temporal nuevo para terminarla."
                : $"Actualización: se conservará la modalidad {(additional ? "caja adicional" : "caja principal / servidor")}."
            : additional
                ? "Esta computadora no instalará PostgreSQL, API, servicios ni regla de red."
                : "Esta computadora alojará la tienda y atenderá a las cajas adicionales de la red privada.";
    }

    private static Image? LoadBanner()
    {
        using var stream = typeof(InstallerForm).Assembly.GetManifestResourceStream("JetVenta.Assets.jetventa-banner.png");
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
            _terms.Focus();
            MessageBox.Show(
                this,
                "Para instalar o actualizar JetVenta debes leer y aceptar los términos y condiciones.",
                "Aceptación requerida",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (!_uninstall && _installationMode == InstallationMode.AdditionalRegister && (!_existingInstallation || _installationProfile.PairedAtUtc is null))
        {
            try
            {
                var connection = await ValidateAdditionalConnectionAsync();
                InstallationProfileStore.Save(new InstallationProfile(1, InstallationMode.AdditionalRegister, connection.BaseUrl));
            }
            catch (Exception exception)
            {
                _modeStatus.ForeColor = Color.FromArgb(244, 142, 142);
                _modeStatus.Text = exception.Message;
                return;
            }
        }

        _busy = true;
        ShowActivityView();
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

    private void ShowActivityView()
    {
        foreach (var control in _summaryControls)
        {
            control.Visible = false;
        }

        _activityTitle.Text = _uninstall
            ? "Retirando JetVenta"
            : _existingInstallation
                ? "Actualizando JetVenta"
                : "Instalando JetVenta";
        _activityDescription.Text = _uninstall
            ? "Estamos retirando el programa. La tienda, los datos y los respaldos se conservarán."
            : _installationMode == InstallationMode.AdditionalRegister
                ? "Estamos preparando la caja y validando su conexión segura con el servidor."
                : "Estamos preparando los componentes y servicios necesarios para operar la tienda.";
        _activityTitle.SetBounds(30, 190, 960, 34);
        _activityDescription.SetBounds(30, 229, 960, 25);
        _status.SetBounds(30, 274, 860, 27);
        _status.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        _status.ForeColor = Color.FromArgb(233, 244, 252);
        _progressValue.SetBounds(900, 274, 90, 27);
        _progressValue.TextAlign = ContentAlignment.MiddleRight;
        _progress.SetBounds(30, 313, 960, 20);
        _details.SetBounds(30, 361, 960, 250);
        _details.Font = new Font("Consolas", 10f);
        _cancel.SetBounds(698, 682, 96, 38);
        _action.SetBounds(804, 682, 174, 38);
        _activityTitle.Visible = true;
        _activityDescription.Visible = true;
        _progressValue.Visible = true;
        _status.Visible = true;
        _progress.Visible = true;
        _details.Visible = true;
        _activityTitle.BringToFront();
        _activityDescription.BringToFront();
        _progressValue.BringToFront();
        _status.BringToFront();
        _progress.BringToFront();
        _details.BringToFront();
        _cancel.BringToFront();
        _action.BringToFront();
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

            if (_installationMode == InstallationMode.Server)
            {
                await StopServicesForUpdateAsync();
            }
            else
            {
                SetProgress(56, "Caja adicional: no se instalan ni detienen servicios locales.");
            }
            if (_existingInstallation)
            {
                await UpdatePayloadAsync();
                RemoveLegacyDesktopFiles();
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

        if (_installationMode == InstallationMode.Server)
        {
            SetProgress(84, "Verificando PostgreSQL, base de datos y API...");
            await RunPowerShellAsync(Path.Combine(_installRoot, "install-production.ps1"), string.Empty);
            InstallationProfileStore.Save(new InstallationProfile(1, InstallationMode.Server));
        }
        else if (!_existingInstallation || _installationProfile.PairedAtUtc is null)
        {
            SetProgress(84, "Emparejando esta caja con el servidor configurado...");
            var connection = await ValidateAdditionalConnectionAsync();
            await PairAdditionalRegisterAsync(connection);
            InstallationProfileStore.Save(new InstallationProfile(1, InstallationMode.AdditionalRegister, connection.BaseUrl, DateTimeOffset.UtcNow));
        }
        else
        {
            SetProgress(84, "Actualizando archivos de la caja adicional; se conserva su emparejamiento.");
        }
        await GrantClientSettingsAccessAsync();
        RegisterInstallation();
        RegisterLicenseFileType();
        RegisterBackupFileType();
        CreateShortcuts();
        ConfigureAutomaticStart(_startWithWindows.Checked);
        SetProgress(100, _installationMode == InstallationMode.AdditionalRegister
            ? "Caja adicional instalada y emparejada. Ya puedes abrir JetVenta."
            : "Instalación terminada. Ya puedes abrir la configuración inicial.");
    }

    private async Task UninstallAsync()
    {
        EnsureDesktopClosedForUpdate();
        if (_installationProfile.Mode == InstallationMode.Server)
        {
            var script = Path.Combine(_installRoot, "install-production.ps1");
            if (File.Exists(script)) await RunPowerShellAsync(script, "-Uninstall");
        }
        else
        {
            Log("Caja adicional: no se modifican servicios, PostgreSQL ni reglas de red del servidor.");
        }
        Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PuntoDeVenta", false);
        UnregisterLicenseFileType();
        UnregisterBackupFileType();
        DeleteShortcuts();
        ConfigureAutomaticStart(false);
        SetProgress(100, "Desinstalación terminada. Se conservaron datos, respaldos y el historial de demo.");
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
        var running = Process.GetProcessesByName("JetVenta")
            .Concat(Process.GetProcessesByName("Pos.Desktop"))
            .ToArray();
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

    private async Task GrantClientSettingsAccessAsync()
    {
        // The profile belongs to the physical register and must be usable by any
        // Windows account that opens JetVenta on that register. Keep this limited
        // to the client profile; database, license and server secrets stay private.
        var clientDirectory = Path.Combine(_dataRoot, "client");
        Directory.CreateDirectory(clientDirectory);
        var icacls = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "icacls.exe");
        await RunProcessAsync(
            icacls,
            $"{Program.QuoteArgument(clientDirectory)} /grant \"*S-1-5-32-545:(OI)(CI)M\" /T /C",
            Path.GetDirectoryName(icacls)!);
        Log("Permiso de perfil local aplicado para usuarios de esta caja.");
    }

    private async Task CopyPayloadAsync(string source)
    {
        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories)
            .Where(file => ShouldInstallPayloadEntry(Path.GetRelativePath(source, file)))
            .ToArray();
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
            if (!ShouldInstallPayloadEntry(relative))
            {
                Log($"Caja adicional: se omite componente de servidor: {relative}");
                continue;
            }
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

    private bool ShouldInstallPayloadEntry(string relativePath)
    {
        if (_installationMode == InstallationMode.Server) return true;
        var normalized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return !normalized.StartsWith($"api{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
               !normalized.StartsWith($"postgresql{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(normalized, "install-production.ps1", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(normalized, "restore-production-backup.ps1", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<AdditionalServerConnection> ValidateAdditionalConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(_serverAddress.Text)) throw new InvalidOperationException("Indica la dirección de la caja principal.");
        if (!int.TryParse(_serverPort.Text, out var port) || port is < 1 or > 65535) throw new InvalidOperationException("El puerto del servidor debe estar entre 1 y 65535.");
        if (_pairingCode.Text.Trim().Length != 6 || _pairingCode.Text.Any(character => !char.IsDigit(character))) throw new InvalidOperationException("Captura el código temporal de seis dígitos generado por el administrador.");
        if (string.IsNullOrWhiteSpace(_registerName.Text)) throw new InvalidOperationException("Indica el nombre de esta caja.");

        var server = BuildServerUri(_serverAddress.Text, port);
        if (IsLoopback(server)) throw new InvalidOperationException("Una caja adicional debe indicar la dirección de otra computadora de la red, no localhost.");
        using var client = CreatePairingClient(server);
        try
        {
            using var health = await client.GetAsync("health");
            if (!health.IsSuccessStatusCode) throw new InvalidOperationException($"La caja principal respondió {(int)health.StatusCode} al comprobar su estado.");
            using var infoResponse = await client.GetAsync("api/lan/info");
            if (!infoResponse.IsSuccessStatusCode) throw new InvalidOperationException($"La caja principal respondió {(int)infoResponse.StatusCode} al comprobar la comunicación LAN.");
            var info = await infoResponse.Content.ReadFromJsonAsync<InstallerLanInfo>();
            if (info is null) throw new InvalidOperationException("La caja principal no devolvió la información de comunicación LAN.");
            if (info.ProtocolVersion != 2) throw new InvalidOperationException($"La caja principal usa protocolo {info.ProtocolVersion}. Actualiza JetVenta en el servidor antes de instalar esta caja.");
            return new AdditionalServerConnection(server.ToString().TrimEnd('/'), _pairingCode.Text.Trim(), _registerName.Text.Trim());
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException("No se pudo contactar la caja principal. Revisa que ambas computadoras estén en la red privada y que la dirección y el puerto sean correctos.", exception);
        }
        catch (TaskCanceledException exception)
        {
            throw new InvalidOperationException("La caja principal tardó demasiado en responder. Revisa la red y vuelve a intentarlo.", exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("La caja principal devolvió una respuesta inválida. Actualiza JetVenta en el servidor e inténtalo de nuevo.", exception);
        }
    }

    private async Task PairAdditionalRegisterAsync(AdditionalServerConnection connection)
    {
        using var client = CreatePairingClient(new Uri(connection.BaseUrl));
        using var response = await client.PostAsJsonAsync("api/lan/pair", new
        {
            code = connection.PairingCode,
            deviceName = Environment.MachineName,
            registerName = connection.RegisterName
        });
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadServerErrorAsync(response, "No se pudo emparejar la caja. Revisa el código temporal y el nombre de caja."));
        }

        var result = await response.Content.ReadFromJsonAsync<InstallerPairingResult>();
        if (result is null || string.IsNullOrWhiteSpace(result.DeviceToken)) throw new InvalidOperationException("La caja principal no devolvió la identidad segura de esta caja.");
        SavePairedMachineSettings(connection.BaseUrl, result);
        Log($"Caja adicional emparejada correctamente como {result.RegisterName}.");
    }

    private static Uri BuildServerUri(string address, int port)
    {
        var value = address.Trim();
        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) value = $"http://{value}";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host)) throw new InvalidOperationException("La dirección del servidor no es válida.");
        return new UriBuilder(uri) { Port = port }.Uri;
    }

    private static bool IsLoopback(Uri server) =>
        string.Equals(server.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        System.Net.IPAddress.TryParse(server.Host, out var address) && System.Net.IPAddress.IsLoopback(address);

    private static HttpClient CreatePairingClient(Uri server)
    {
        var client = new HttpClient { BaseAddress = new Uri(server.ToString().TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-JetVenta-Lan-Protocol", "2");
        return client;
    }

    private static async Task<string> ReadServerErrorAsync(HttpResponseMessage response, string fallback)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<InstallerError>();
            return string.IsNullOrWhiteSpace(body?.Message) ? fallback : body.Message;
        }
        catch (JsonException) { return fallback; }
    }

    private static void SavePairedMachineSettings(string baseUrl, InstallerPairingResult result)
    {
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta", "client", "machine-settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var protectedToken = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(result.DeviceToken), null, DataProtectionScope.LocalMachine));
        var settings = new InstallerMachineSettings(baseUrl, result.DeviceId, result.StoreId, result.RegisterId, protectedToken);
        var temporary = $"{settingsPath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
        File.Move(temporary, settingsPath, true);
    }

    private sealed record AdditionalServerConnection(string BaseUrl, string PairingCode, string RegisterName);
    private sealed record InstallerLanInfo(int ProtocolVersion, string ApiVersion, string Machine);
    private sealed record InstallerPairingResult(Guid DeviceId, Guid StoreId, Guid RegisterId, string DeviceToken, string RegisterName);
    private sealed record InstallerError(string? Message);
    private sealed record InstallerMachineSettings(
        string BaseUrl,
        Guid DeviceId,
        Guid StoreId,
        Guid RegisterId,
        string DeviceTokenProtected,
        string? PrinterName = null,
        string? PrinterFontFamily = null,
        double PrinterFontSize = 9d,
        bool UseNormalTotals = false,
        int PrinterTicketWidthMm = 80,
        object? BarcodeScanner = null,
        bool? PrintingEnabled = null,
        int SettingsVersion = 4,
        object? CashDrawer = null,
        object? Scale = null);

    private void RemoveLegacyDesktopFiles()
    {
        foreach (var fileName in new[]
                 {
                     LegacyDesktopExecutableName,
                     "Pos.Desktop.dll",
                     "Pos.Desktop.deps.json",
                     "Pos.Desktop.runtimeconfig.json",
                     "Pos.Desktop.pdb"
                 })
        {
            var path = Path.Combine(_installRoot, "client", fileName);
            if (!File.Exists(path)) continue;
            File.Delete(path);
            Log($"Se retiró el archivo anterior del cliente: {fileName}");
        }
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

    private void RegisterLicenseFileType()
    {
        try
        {
            using (var extension = Registry.LocalMachine.CreateSubKey(LicenseFileExtensionKey))
            {
                extension?.SetValue(string.Empty, LicenseFileTypeName);
                extension?.SetValue("Content Type", "application/vnd.jetventa.license");
                extension?.SetValue("PerceivedType", "document");
            }

            using (var fileType = Registry.LocalMachine.CreateSubKey(LicenseFileTypeKey))
            {
                fileType?.SetValue(string.Empty, "Licencia de JetVenta");
                fileType?.SetValue("FriendlyTypeName", "Licencia de JetVenta");
            }

            using var icon = Registry.LocalMachine.CreateSubKey($@"{LicenseFileTypeKey}\DefaultIcon");
            icon?.SetValue(string.Empty, $"\"{Path.Combine(_installRoot, "client", "license-file.ico")}\",0");
            NotifyFileAssociationsChanged();
            Log("Extensión .jv registrada con el icono de licencia de JetVenta.");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log($"No se pudo registrar el icono de archivos .jv: {exception.Message}");
        }
    }

    private void UnregisterLicenseFileType()
    {
        try
        {
            var removeExtension = false;
            using (var extension = Registry.LocalMachine.OpenSubKey(LicenseFileExtensionKey))
            {
                removeExtension = string.Equals(extension?.GetValue(string.Empty)?.ToString(), LicenseFileTypeName, StringComparison.Ordinal);
            }

            if (removeExtension)
            {
                Registry.LocalMachine.DeleteSubKeyTree(LicenseFileExtensionKey, false);
            }

            Registry.LocalMachine.DeleteSubKeyTree(LicenseFileTypeKey, false);
            NotifyFileAssociationsChanged();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log($"No se pudo retirar el icono de archivos .jv: {exception.Message}");
        }
    }

    private void RegisterBackupFileType()
    {
        try
        {
            using (var extension = Registry.LocalMachine.CreateSubKey(BackupFileExtensionKey))
            {
                extension?.SetValue(string.Empty, BackupFileTypeName);
                extension?.SetValue("Content Type", "application/vnd.jetventa.backup");
                extension?.SetValue("PerceivedType", "document");
            }

            using (var fileType = Registry.LocalMachine.CreateSubKey(BackupFileTypeKey))
            {
                fileType?.SetValue(string.Empty, "Respaldo de JetVenta");
                fileType?.SetValue("FriendlyTypeName", "Respaldo de JetVenta");
            }

            using var icon = Registry.LocalMachine.CreateSubKey($@"{BackupFileTypeKey}\DefaultIcon");
            icon?.SetValue(string.Empty, $"\"{Path.Combine(_installRoot, "client", "backup-file.ico")}\",0");
            NotifyFileAssociationsChanged();
            Log("Extensión .bjv registrada con el icono de respaldo de JetVenta.");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log($"No se pudo registrar el icono de archivos .bjv: {exception.Message}");
        }
    }

    private void UnregisterBackupFileType()
    {
        try
        {
            var removeExtension = false;
            using (var extension = Registry.LocalMachine.OpenSubKey(BackupFileExtensionKey))
            {
                removeExtension = string.Equals(extension?.GetValue(string.Empty)?.ToString(), BackupFileTypeName, StringComparison.Ordinal);
            }

            if (removeExtension) Registry.LocalMachine.DeleteSubKeyTree(BackupFileExtensionKey, false);
            Registry.LocalMachine.DeleteSubKeyTree(BackupFileTypeKey, false);
            NotifyFileAssociationsChanged();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log($"No se pudo retirar el icono de archivos .bjv: {exception.Message}");
        }
    }

    private static void NotifyFileAssociationsChanged() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    private void CreateShortcuts()
    {
        var target = DesktopExecutablePath;
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
            var target = DesktopExecutablePath;
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
        var desktop = DesktopExecutablePath;
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

    private string DesktopExecutablePath => Path.Combine(_installRoot, "client", DesktopExecutableName);

    private bool HasExistingInstallation() =>
        File.Exists(DesktopExecutablePath) ||
        File.Exists(Path.Combine(_installRoot, "client", LegacyDesktopExecutableName)) ||
        File.Exists(Path.Combine(_dataRoot, "postgresql", "data", "PG_VERSION"));

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
        void Update()
        {
            _progress.Value = Math.Clamp(value, 0, 100);
            _progressValue.Text = $"{_progress.Value}%";
            _status.Text = message;
            Log(message);
        }
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
        if (ClientSize.Width < 10 || ClientSize.Height < 10)
        {
            return;
        }

        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var track = new SolidBrush(Color.FromArgb(29, 50, 70));
        using var border = new Pen(Color.FromArgb(59, 89, 116));
        graphics.FillRoundedRectangle(track, ClientRectangle, 5);
        graphics.DrawRoundedRectangle(border, ClientRectangle, 5);
        var ratio = Maximum <= Minimum ? 0 : (Value - Minimum) / (float)(Maximum - Minimum);
        var fillWidth = (int)((Width - 2) * ratio);
        if (fillWidth < 2)
        {
            return;
        }

        var fill = new Rectangle(1, 1, fillWidth, Height - 2);
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
