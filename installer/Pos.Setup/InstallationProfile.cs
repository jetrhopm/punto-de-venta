using System.Text.Json;

namespace Pos.Setup;

internal enum InstallationMode
{
    Server,
    AdditionalRegister
}

internal sealed record InstallationProfile(
    int ProfileVersion,
    InstallationMode Mode,
    string? ServerBaseUrl = null,
    DateTimeOffset? PairedAtUtc = null);

internal static class InstallationProfileStore
{
    private static readonly string ProfilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "PuntoDeVenta",
        "config",
        "installation-mode.json");

    public static InstallationProfile ReadOrDefault()
    {
        try
        {
            if (!File.Exists(ProfilePath)) return new InstallationProfile(1, InstallationMode.Server);
            var profile = JsonSerializer.Deserialize<InstallationProfile>(File.ReadAllText(ProfilePath));
            return profile is null || !Enum.IsDefined(profile.Mode)
                ? new InstallationProfile(1, InstallationMode.Server)
                : profile;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new InstallationProfile(1, InstallationMode.Server);
        }
    }

    public static void Save(InstallationProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
        var temporary = $"{ProfilePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(profile));
        File.Move(temporary, ProfilePath, true);
    }
}
