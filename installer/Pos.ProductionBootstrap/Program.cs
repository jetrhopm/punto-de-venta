using System.Diagnostics;

var bootstrapLog = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "PuntoDeVenta",
    "logs",
    "instalador-bootstrap.log");

Directory.CreateDirectory(Path.GetDirectoryName(bootstrapLog)!);
void Log(string message) => File.AppendAllText(bootstrapLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");

var script = Path.Combine(AppContext.BaseDirectory, "install-production.ps1");
if (!File.Exists(script))
{
    Log($"ERROR: no existe el bootstrap de produccion: {script}");
    Console.Error.WriteLine($"No existe el bootstrap de produccion: {script}");
    return 2;
}

var candidates = new[]
{
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Punto de Venta"),
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Punto de Venta")
};
var installRoot = candidates.FirstOrDefault(path => File.Exists(Path.Combine(path, "api", "Pos.Api.exe")))
    ?? candidates[0];
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
