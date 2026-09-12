using System.Text.Json;
using System.IO;

namespace Pos.Desktop;

internal enum InstallationRole { Server, AdditionalRegister }

internal static class InstallationRoleContext
{
    private static readonly string ProfilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta", "config", "installation-mode.json");
    public static bool IsServer => ReadRole() == InstallationRole.Server;
    public static string Description => IsServer ? "Caja principal / servidor" : "Caja adicional conectada al servidor";

    private static InstallationRole ReadRole()
    {
        try
        {
            if (!File.Exists(ProfilePath)) return InstallationRole.Server;
            var profile = JsonSerializer.Deserialize<InstallationProfile>(File.ReadAllText(ProfilePath));
            return profile is not null && Enum.IsDefined(profile.Mode) ? profile.Mode : InstallationRole.Server;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return InstallationRole.Server;
        }
    }

    private sealed record InstallationProfile(int ProfileVersion, InstallationRole Mode, string? ServerBaseUrl, DateTimeOffset? PairedAtUtc);
}
