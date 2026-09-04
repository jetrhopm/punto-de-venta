namespace Pos.Desktop;

public static class SessionContext
{
    private static readonly Dictionary<string, int> TemporaryPermissions = new(StringComparer.Ordinal);
    public static string? AccessToken { get; set; }
    public static string? DisplayName { get; set; }
    public static bool IsAdministrator { get; set; }
    public static HashSet<string> Permissions { get; } = new(StringComparer.Ordinal);

    public static bool HasPermission(string permission) => IsAdministrator || Permissions.Contains(permission) || TemporaryPermissions.ContainsKey(permission);

    public static IDisposable BeginTemporaryPermission(string permission)
    {
        TemporaryPermissions.TryGetValue(permission, out var count);
        TemporaryPermissions[permission] = count + 1;
        return new TemporaryPermissionScope(permission);
    }

    public static void Clear()
    {
        AccessToken = null;
        DisplayName = null;
        IsAdministrator = false;
        Permissions.Clear();
        TemporaryPermissions.Clear();
    }

    private sealed class TemporaryPermissionScope(string permission) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!TemporaryPermissions.TryGetValue(permission, out var count)) return;
            if (count <= 1) TemporaryPermissions.Remove(permission);
            else TemporaryPermissions[permission] = count - 1;
        }
    }
}
