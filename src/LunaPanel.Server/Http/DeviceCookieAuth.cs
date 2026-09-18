namespace LunaPanel.Server.Http;

/// <summary>
/// The one name shared between minting the pairing cookie
/// (<c>POST /api/pair</c>) and reading it back
/// (<see cref="DeviceAuthMiddlewareExtensions"/>).
/// </summary>
public static class DeviceCookieAuth
{
    public const string CookieName = "lp_device_token";
}
