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
///
/// <see cref="EliteSetupAcknowledged"/> is unrelated to either path - it is
/// the one-time gate <c>EliteSetupForm</c> (<c>LunaPanel.Tray</c>) sets once
/// a commander has been shown the first-launch Elite Dangerous check and
/// either fixed it or explicitly deferred it, so that screen never shows
/// again on a later launch regardless of whether Elite was actually found.
/// Defaults to <see langword="false"/>, same as a fresh install that has
/// never seen the screen.
/// </summary>
public sealed record PathOverrideSettings(string? EliteInstallPath, string? EdhmSettingsJsonPath, bool EliteSetupAcknowledged = false)
{
    public static readonly PathOverrideSettings Default = new(null, null, false);
}
