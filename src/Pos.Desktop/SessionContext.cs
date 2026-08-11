namespace Pos.Desktop;

public static class SessionContext
{
    public static string? AccessToken { get; set; }
    public static string? DisplayName { get; set; }
    public static bool IsAdministrator { get; set; }
    public static HashSet<string> Permissions { get; } = new(StringComparer.Ordinal);

    public static bool HasPermission(string permission) => Permissions.Contains(permission);
}
