namespace LunaPanel.Core.Theme;

/// <summary>
/// The six resolved CSS custom-property values LunaPanel's stylesheet
/// consumes, plus which step of the resolution chain produced them and
/// which EDHM titles (if any) were found but couldn't be resolved to a
/// usable colour. See <c>ref/docs/theme.md</c> for what each of the six
/// roles is chosen to mean and why.
/// </summary>
/// <param name="Ground">The panel's own background - never sourced from EDHM (it doesn't colour a background); a near-black tint of whichever colour won.</param>
/// <param name="Frame">Panel border/line colour. For EDHM per-element resolution this is fed by the main-text-titled element (a deliberate inversion - see <c>ref/docs/theme.md</c>'s "commander's real mapping"); for the matrix-based automatic steps it is derived from <see cref="Text"/> via <c>HudColorRamp</c>; for a manual override it is the commander's own choice, unadjusted.</param>
/// <param name="Text">Main HUD panel text colour as shown to the commander. For EDHM per-element resolution this is fed by the frame/panel-line-titled element, not the "main text"-titled one - see the inversion note on <see cref="Frame"/>. EDHM per-element resolution is abandoned for the whole theme, in favour of the next step, only if NEITHER the main-text nor the frame/panel-line family resolves.</param>
/// <param name="Accent">Highlighted/emphasis colour.</param>
/// <param name="Lit">The brightest "on/active" indicator colour. Fixed white and never title-matched for EDHM per-element resolution (the commander's own spec; also keeps O16 permanently closed); derived from <see cref="Text"/> via <c>HudColorRamp</c> for the matrix-based automatic steps, or the commander's own choice for a manual override.</param>
/// <param name="Dim">Secondary/inactive text colour.</param>
/// <param name="Source">Which resolution-chain step produced this theme.</param>
/// <param name="UnresolvedTitles">EDHM element titles that matched a role but couldn't be resolved to a usable colour (e.g. naming an INI file that wasn't supplied) - logged, not silently dropped.</param>
public sealed record HudTheme(
    HudColor Ground,
    HudColor Frame,
    HudColor Text,
    HudColor Accent,
    HudColor Lit,
    HudColor Dim,
    HudThemeSource Source,
    IReadOnlyList<string> UnresolvedTitles);
