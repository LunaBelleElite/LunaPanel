namespace LunaPanel.Core.Discovery;

/// <summary>
/// A commander's manual fallback for the two paths auto-detection sometimes
/// can't find - the Elite Dangerous install root, and the EDHM-UI
/// <c>Settings.json</c> file - set from the Tray app's About window
/// (<c>AboutForm</c>, <c>LunaPanel.Tray</c>) when discovery comes up empty
/// (or simply wrong, e.g. more than one install on a machine). Both default
/// to <see langword="null"/>, meaning "let auto-detection decide" - this
/// record only ever carries an explicit override, never a resolved/guessed
/// value.
/// </summary>
public sealed record PathOverrideSettings(string? EliteInstallPath, string? EdhmSettingsJsonPath)
{
    public static readonly PathOverrideSettings Default = new(null, null);
}
