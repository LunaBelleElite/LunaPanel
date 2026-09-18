namespace LunaPanel.Server.Tray;

/// <summary>
/// "Option A: Windows 11 dark" - the tray Status window's colour palette,
/// picked by the user from three mockups (2026-09-07). Every colour here is
/// a hex string, not a <c>System.Drawing.Color</c>: this project
/// (<c>net10.0</c>) is the testable seam, and <c>LunaPanel.Tray</c>
/// (<c>net10.0-windows</c>, WinForms) is the only place that actually needs
/// a <c>Color</c> - it converts via <c>ColorTranslator.FromHtml</c> - see
/// <c>ref/docs/hosting.md</c>'s tray section.
///
/// Deliberately no red anywhere on this palette: a missing EDHM install is
/// not an error, so <see cref="NotFoundColor"/> is an amber, never a red.
/// </summary>
public static class TrayTheme
{
    public const string Background = "#202020";
    public const string Border = "#2F2F2F";
    public const string PrimaryText = "#F2F2F2";
    public const string LabelText = "#A0A0A0";
    public const string FoundColor = "#6CCB6C";
    public const string NotFoundColor = "#E0A03A";
    public const string LinkText = "#7FD4FF";
    public const string PrimaryButtonFill = "#3B7DD8";
    public const string PrimaryButtonText = "#FFFFFF";
    public const string SecondaryButtonFill = "#2D2D2D";
    public const string SecondaryButtonBorder = "#3D3D3D";

    /// <summary>
    /// The colour a status row's value should render in.
    /// <see cref="StatusRowState.Found"/> -&gt; green,
    /// <see cref="StatusRowState.NotFound"/> -&gt; amber (never red - see
    /// above), and a row with no state (the paired-device count) -&gt; the
    /// plain primary text colour.
    /// </summary>
    public static string ColorFor(StatusRowState? state) => state switch
    {
        StatusRowState.Found => FoundColor,
        StatusRowState.NotFound => NotFoundColor,
        _ => PrimaryText,
    };
}
