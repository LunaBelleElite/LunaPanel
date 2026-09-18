namespace LunaPanel.Core.Network;

/// <summary>
/// The device listener's port, persisted so a commander can pick or type one
/// without an environment variable (<c>PortSettingsForm</c>,
/// <c>LunaPanel.Tray</c>). Mirrors <c>LunaPanel.Core.Macros.MacroTimingSettings</c>'
/// shape and the store discipline it documents - server-wide, one file, no
/// device id.
/// </summary>
public sealed record PortSettings(int Port)
{
    /// <summary>
    /// The shipped default port. Single source of truth - previously a
    /// separately-declared constant on <c>RealServerEnvironment</c>
    /// (<c>51823</c>), replaced here so nothing duplicates it. Chosen because
    /// a work Wi-Fi network was observed blocking the old port outright,
    /// while 8443 is the kind of port a network already expects to see
    /// (HTTPS-alternate) and is more likely to be let through.
    /// </summary>
    public const int DefaultPort = 8443;

    /// <summary>
    /// Lowest port this dispatch will accept. Below 1024 needs admin
    /// elevation on Windows to bind at all, so refusing it here means a
    /// commander sees a clear reason in the UI rather than a Kestrel bind
    /// failure with no explanation.
    /// </summary>
    public const int MinPort = 1024;

    /// <summary>Highest valid TCP port.</summary>
    public const int MaxPort = 65535;

    /// <summary>
    /// The Ports dialog's preset list, in display order - 8443 first because
    /// it is also <see cref="DefaultPort"/>.
    /// </summary>
    public static readonly int[] FriendlyPorts = { 8443, 8080, 8000, 8888, 9443 };

    public static readonly PortSettings Default = new(DefaultPort);

    /// <summary>
    /// Whether <paramref name="port"/> falls within [<see cref="MinPort"/>,
    /// <see cref="MaxPort"/>].
    /// </summary>
    public static bool IsValid(int port) => port is >= MinPort and <= MaxPort;
}
