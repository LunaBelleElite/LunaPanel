using LunaPanel.Server.Discovery;

namespace LunaPanel.Server.Tray;

/// <summary>
/// Whether a status row's value names something that was located ("Found")
/// or was not ("NotFound"). Deliberately not a bool: <see cref="TrayTheme.ColorFor"/>
/// colours these two states differently (green vs amber, never red - a
/// missing EDHM install is not an error), and a plain bool would leave that
/// choice implicit at every call site instead of decided once, here.
/// </summary>
public enum StatusRowState
{
    Found,
    NotFound,
}

/// <summary>
/// One label/value row in the tray's Status window. <see cref="State"/> is
/// null for a row with no found/not-found colouring (the paired-device
/// count, which is just a number).
/// </summary>
public sealed record TrayStatusRow(string Label, string Value, StatusRowState? State = null);

/// <summary>
/// The tray's Status window body: the URL (shown on its own line above the
/// rows - ref/docs/hosting.md's "Option A: Windows 11 dark" restyle,
/// 2026-09-07) plus every label/value row below it.
///
/// <b>Supersedes the flat, single-string body this window used to show in a
/// read-only textbox.</b> The previous shape (this file's own
/// <c>TrayStatusText.Build</c>, deleted the same day) returned one
/// newline-joined string, e.g. <c>"Elite install found: yes"</c> -
/// sufficient for a plain textbox, but a block of text can't carry the
/// found/not-found colour distinction "Option A" needs.
/// <see cref="TrayStatusModelBuilder"/> is the direct replacement;
/// <c>TrayStatusTextTests</c> (deleted) is now
/// <c>TrayStatusModelBuilderTests</c>.
/// </summary>
public sealed record TrayStatusModel(string Url, IReadOnlyList<TrayStatusRow> Rows);

/// <summary>
/// Builds the tray's Status window model from the same
/// <see cref="PathDiscoveryResult"/> the startup banner in
/// <see cref="LunaPanel.Server.Hosting.ServerHostBuilder"/> already
/// summarises to the console - this is the tray's equivalent of that
/// banner, shown in a window instead of scrollback a WinExe app has no
/// console to write to in the first place.
/// </summary>
public static class TrayStatusModelBuilder
{
    public static TrayStatusModel Build(string url, PathDiscoveryResult discovery, int pairedDeviceCount)
    {
        var rows = new[]
        {
            FoundRow("Elite install found", discovery.EliteInstallations.Count > 0),
            FoundRow("Bindings found", discovery.Bindings.LatestBindsFilePath is not null),
            FoundRow("EDHM theme found", discovery.Edhm.SettingsFound),
            new TrayStatusRow("Paired devices", pairedDeviceCount.ToString()),
        };

        return new TrayStatusModel(url, rows);
    }

    private static TrayStatusRow FoundRow(string label, bool found) =>
        new(label, found ? "Found" : "Not found", found ? StatusRowState.Found : StatusRowState.NotFound);
}
