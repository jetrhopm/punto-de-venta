using System.Diagnostics;

var script = Path.Combine(AppContext.BaseDirectory, "install-production.ps1");
if (!File.Exists(script))
{
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

var process = Process.Start(new ProcessStartInfo
{
    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe"),
    Arguments = arguments,
    UseShellExecute = false,
    CreateNoWindow = true,
    WorkingDirectory = installRoot
});
if (process is null) return 3;
await process.WaitForExitAsync();
return process.ExitCode;
