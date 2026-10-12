namespace JetVenta.Mobile;

public sealed class MobileSettings
{
    private const string ServerKey = "server-url";
    private const string DeviceTokenKey = "mobile-device-token";
    private const string AccessTokenKey = "mobile-access-token";
    private const string UserNameKey = "mobile-user-name";

    public Task<string?> GetServerAsync() => SecureStorage.Default.GetAsync(ServerKey);
    public Task<string?> GetDeviceTokenAsync() => SecureStorage.Default.GetAsync(DeviceTokenKey);
    public Task<string?> GetAccessTokenAsync() => SecureStorage.Default.GetAsync(AccessTokenKey);
    public Task<string?> GetUserNameAsync() => SecureStorage.Default.GetAsync(UserNameKey);
    public Task SavePairingAsync(string server, string deviceToken) => Task.WhenAll(SecureStorage.Default.SetAsync(ServerKey, server), SecureStorage.Default.SetAsync(DeviceTokenKey, deviceToken));
    public Task SaveLoginAsync(string accessToken, string userName) => Task.WhenAll(SecureStorage.Default.SetAsync(AccessTokenKey, accessToken), SecureStorage.Default.SetAsync(UserNameKey, userName));
    public void ClearLogin() => SecureStorage.Default.Remove(AccessTokenKey);
}
