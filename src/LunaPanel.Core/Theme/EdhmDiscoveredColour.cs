namespace LunaPanel.Core.Theme;

/// <summary>
/// One distinct colour found in the commander's own EDHM theme, labelled
/// with the (Color/Colour-suffix-trimmed) title of the element that
/// produced it - see <see cref="HudThemeResolver.ExtractDiscoveredColours"/>.
/// Feeds the settings gear's "From your HUD" picker group
/// (<c>ref/docs/web-client.md</c>), never persisted anywhere.
/// </summary>
/// <param name="Label">A human-readable label, e.g. <c>"Main Text"</c> (from EDHM's own <c>"Main Text Color"</c>).</param>
/// <param name="Color">The resolved, display-ready colour.</param>
public sealed record EdhmDiscoveredColour(string Label, HudColor Color);
