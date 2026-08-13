using System.Diagnostics;
using System.IO.Compression;
using System.Security.Principal;

const string productName = "Punto de Venta";
var installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), productName);
var dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta");
var logPath = Path.Combine(dataRoot, "logs", "setup.log");

Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
void Log(string message)
{
    var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
    Console.WriteLine(line);
    File.AppendAllText(logPath, line + Environment.NewLine);
}

bool IsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

if (!IsAdministrator())
{
    Log("Solicitando permisos de administrador de Windows.");
    var localStage = Path.Combine(Path.GetTempPath(), "PuntoDeVenta-Setup", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(localStage);
    var localExecutable = Path.Combine(localStage, "Setup.exe");
    File.Copy(Environment.ProcessPath!, localExecutable, true);
    Log($"Copiando el instalador a una ruta local antes de elevar: {localExecutable}");
    var elevated = Process.Start(new ProcessStartInfo
    {
        FileName = localExecutable,
        Arguments = string.Join(' ', args.Select(QuoteArgument)),
        UseShellExecute = true,
        Verb = "runas",
        WorkingDirectory = localStage
    });
    if (elevated is null) return 1;
    await elevated.WaitForExitAsync();
    return elevated.ExitCode;
}

var uninstall = args.Any(a => string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase));
Log($"Inicio de {productName}. Modo: {(uninstall ? "desinstalacion de servicios" : "instalacion o reparacion")}");
Log($"Carpeta de instalacion: {installRoot}");
Log($"Carpeta de datos: {dataRoot}");

try
{
    if (uninstall)
    {
        await RunPowerShellAsync(Path.Combine(installRoot, "install-production.ps1"), "-Uninstall", installRoot);
        Log("Servicios eliminados. La base de datos, configuracion y respaldos se conservaron.");
        return 0;
    }

    var tempRoot = Path.Combine(Path.GetTempPath(), $"PuntoDeVenta-Setup-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempRoot);
    try
    {
        await ExtractPayloadAsync(tempRoot);
        await CopyPayloadAsync(tempRoot, installRoot);
    }
    finally
    {
        try { Directory.Delete(tempRoot, true); } catch { Log($"Aviso: no se pudo limpiar temporal: {tempRoot}"); }
    }

    var vcRedist = Path.Combine(installRoot, "vc_redist.x64.exe");
    if (File.Exists(vcRedist))
    {
        Log("Etapa 3/4: comprobando bibliotecas de Microsoft Visual C++.");
        await RunProcessAsync(vcRedist, "/install /quiet /norestart", installRoot);
    }

    Log("Etapa 4/4: inicializando PostgreSQL y registrando la API como servicios.");
    await RunPowerShellAsync(Path.Combine(installRoot, "install-production.ps1"), "", installRoot);
    Log("Instalacion terminada correctamente.");
    Console.WriteLine();
    Console.WriteLine("La instalacion termino. Presiona una tecla para cerrar.");
    Console.ReadKey(true);
    return 0;
}
catch (Exception exception)
{
    Log($"ERROR: {exception.Message}");
    Log($"Diagnostico completo: {exception}");
    Console.WriteLine();
    Console.WriteLine($"La instalacion fallo. Revisa: {logPath}");
    Console.WriteLine("Presiona una tecla para cerrar.");
    Console.ReadKey(true);
    return 1;
}

async Task ExtractPayloadAsync(string destination)
{
    Log("Etapa 1/4: abriendo paquete interno Payload.zip.");
    await using var resource = typeof(Program).Assembly.GetManifestResourceStream("PuntoDeVenta.Payload.zip")
        ?? throw new InvalidOperationException("No se encontro el paquete interno del instalador.");
    using var archive = new ZipArchive(resource, ZipArchiveMode.Read);
    var total = archive.Entries.Sum(entry => Math.Max(0, entry.Length));
    long completed = 0;
    foreach (var entry in archive.Entries)
    {
        var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
        var target = Path.GetFullPath(Path.Combine(destination, relative));
        if (!target.StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Ruta insegura en el paquete: {entry.FullName}");
        if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using var input = entry.Open();
        await using var output = File.Create(target);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read));
            completed += read;
            Console.Write($"\rExtrayendo {entry.FullName} ({Percent(completed, total)}%)   ");
        }
        Log($"Archivo extraido: {entry.FullName} ({entry.Length:N0} bytes)");
    }
    Console.WriteLine();
}

async Task CopyPayloadAsync(string source, string destination)
{
    Log("Etapa 2/4: copiando archivos a la carpeta de instalacion.");
    Directory.CreateDirectory(destination);
    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(source, file);
        var target = Path.Combine(destination, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target, true);
        Log($"Archivo instalado: {relative}");
        await Task.Yield();
    }
}

async Task RunPowerShellAsync(string script, string extraArguments, string workingDirectory)
{
    if (!File.Exists(script)) throw new FileNotFoundException("No existe el script de produccion.", script);
    var arguments = $"-NoProfile -ExecutionPolicy Bypass -File {QuoteArgument(script)} -InstallRoot {QuoteArgument(workingDirectory)} {extraArguments}";
    await RunProcessAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"), arguments, workingDirectory);
}

async Task RunProcessAsync(string fileName, string arguments, string workingDirectory)
{
    Log($"Ejecutando: {Path.GetFileName(fileName)} {arguments}");
    using var process = Process.Start(new ProcessStartInfo { FileName = fileName, Arguments = arguments, WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = false })
        ?? throw new InvalidOperationException($"No se pudo iniciar {fileName}.");
    process.OutputDataReceived += (_, e) => { if (e.Data is not null) Log(e.Data); };
    process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Log(e.Data); };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    await process.WaitForExitAsync();
    if (process.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(fileName)} termino con codigo {process.ExitCode}.");
}

static string QuoteArgument(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
static int Percent(long value, long total) => total <= 0 ? 100 : Math.Clamp((int)(value * 100 / total), 0, 100);
