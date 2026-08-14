using System.Diagnostics;

var bootstrapLog = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "PuntoDeVenta",
    "logs",
    "instalador-bootstrap.log");

Directory.CreateDirectory(Path.GetDirectoryName(bootstrapLog)!);
void Log(string message) => File.AppendAllText(bootstrapLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");

var candidates = new[]
{
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "JetVenta"),
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "JetVenta")
};
var installRoot = candidates.FirstOrDefault(path => File.Exists(Path.Combine(path, "api", "Pos.Api.exe")))
    ?? candidates[0];
var scriptCandidates = new[]
{
    Path.Combine(installRoot, "install-production.ps1"),
    Path.Combine(AppContext.BaseDirectory, "install-production.ps1")
};
var script = scriptCandidates.FirstOrDefault(File.Exists);
if (script is null)
{
    Log($"ERROR: no existe install-production.ps1. Rutas revisadas: {string.Join("; ", scriptCandidates)}");
    Console.Error.WriteLine("No existe el script de configuracion de produccion instalado por el MSI.");
    return 2;
}

Log($"Script de configuracion encontrado en: {script}");
var arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -InstallRoot \"{installRoot}\"";
if (args.Any(argument => string.Equals(argument, "--uninstall", StringComparison.OrdinalIgnoreCase)))
{
    arguments += " -Uninstall";
}

Log($"Iniciando configuracion de produccion en: {installRoot}");
Log("Se abrira una consola visible para mostrar cada etapa y comando.");

var process = Process.Start(new ProcessStartInfo
{
    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe"),
    Arguments = arguments,
    UseShellExecute = false,
    CreateNoWindow = false,
    WindowStyle = ProcessWindowStyle.Normal,
    WorkingDirectory = installRoot
});
if (process is null)
{
    Log("ERROR: no se pudo iniciar PowerShell para ejecutar el bootstrap.");
    return 3;
}
await process.WaitForExitAsync();
Log($"Bootstrap finalizado con codigo {process.ExitCode}.");
return process.ExitCode;
