namespace LunaPanel.Core.Theme;

/// <summary>
/// Which step of the resolution chain actually produced a
/// <see cref="HudTheme"/>. See <c>ref/docs/theme.md</c> for the chain
/// itself; the order below matches it.
/// </summary>
public enum HudThemeSource
{
    /// <summary>EDHM-UI per-element colours from <c>ThemeSettings.json</c> + INI <c>[Constants]</c>.</summary>
    Edhm,

    /// <summary>EDHM-UI's own <c>XML-Profile.ini</c> 3x3 matrix, used when per-element colours weren't usable.</summary>
    EdhmMatrix,

    /// <summary>Elite's player-set <c>GraphicsConfigurationOverride.xml</c> matrix.</summary>
    GraphicsConfigurationOverride,

    /// <summary>Elite's own <c>GraphicsConfiguration.xml</c> default matrix.</summary>
    GraphicsConfigurationDefault,

    /// <summary>Nothing above was usable at all; stock HUD orange with no matrix behind it.</summary>
    Stock,

    /// <summary>
    /// A commander's manual "completely override colours" choice - colour
    /// chain step 3 (see <c>ref/docs/theme.md</c>): a fixed, built-in
    /// palette not derived from EDHM at all, always available regardless of
    /// whether any step above resolved anything.
    /// </summary>
    Override,
}
