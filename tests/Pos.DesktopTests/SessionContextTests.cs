namespace Pos.DesktopTests;

public sealed class SessionContextTests
{
    [Fact]
    public void TemporaryPermissionDoesNotBecomePermanentPermission()
    {
        Pos.Desktop.SessionContext.Clear();

        using (Pos.Desktop.SessionContext.BeginTemporaryPermission("CloseShift"))
        {
            Assert.True(Pos.Desktop.SessionContext.HasPermission("CloseShift"));
            Assert.False(Pos.Desktop.SessionContext.HasPermanentPermission("CloseShift"));
        }

        Assert.False(Pos.Desktop.SessionContext.HasPermission("CloseShift"));
    }

    [Fact]
    public void Clear_RemovesIdentityAndPermissions()
    {
        Pos.Desktop.SessionContext.AccessToken = "session-token";
        Pos.Desktop.SessionContext.DisplayName = "Cajero";
        Pos.Desktop.SessionContext.IsAdministrator = true;
        Pos.Desktop.SessionContext.Permissions.UnionWith(["Sell", "CloseShift"]);

        Pos.Desktop.SessionContext.Clear();

        Assert.Null(Pos.Desktop.SessionContext.AccessToken);
        Assert.Null(Pos.Desktop.SessionContext.DisplayName);
        Assert.False(Pos.Desktop.SessionContext.IsAdministrator);
        Assert.Empty(Pos.Desktop.SessionContext.Permissions);
    }
}
