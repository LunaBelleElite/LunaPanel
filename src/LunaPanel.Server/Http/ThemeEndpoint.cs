using System.Linq;
using LunaPanel.Core.Theme;

namespace LunaPanel.Server.Http;

/// <summary>
/// <c>GET/POST /api/theme</c> and <c>POST /api/theme/reset</c>'s handler
/// logic, kept free of any ASP.NET type - same discipline as
/// <see cref="PanelEndpoint"/>/<see cref="PressEndpoint"/>. The settings
/// gear's colour pane reads/writes through this: which of the colour
/// chain's three steps (<c>ref/docs/theme.md</c>) produced the theme
/// currently in effect, the four commander-facing swatches (Border, Text,
/// Lit and, since 2026-09-10, Background), whether a manual override is
/// currently stored for this device, and the advisory - never blocking -
/// list of roles that have come out too close to the background to see.
/// </summary>
public static class ThemeEndpoint
{
    /// <param name="Background">
    /// The commander's chosen panel background, or <see langword="null"/>
    /// for "leave it derived from Text" - the settings pane sends null for
    /// every role it is not currently changing, so picking a Border can
    /// never silently pin a background the commander never chose.
    /// </param>
    public sealed record OverrideRequest(string? Border, string? Text, string? Lit, string? Background = null);

    public sealed record HudColourEntry(string Label, string Hex);

    /// <param name="Background">
    /// The background currently in effect, always present so the pane's
    /// fourth swatch has something to show - derived from Text when the
    /// commander has not chosen one.
    /// </param>
    /// <param name="BackgroundChosen">
    /// Whether that background is the commander's own pick rather than a
    /// derived value. The pane needs this to know whether to send the
    /// background back when the commander changes a different role: a
    /// derived ground must never be posted as though it were chosen
    /// (<c>ref/docs/button-naming.md</c>'s override principle).
    /// </param>
    /// <param name="LowContrast">
    /// Commander-facing names of any roles that have come out too close to
    /// the background to see (<see cref="ThemeContrast.LowContrastRoles"/>).
    /// Advisory only - nothing here ever refuses a colour, and the pane
    /// shows it as a warning line under an already-applied choice.
    /// </param>
    public sealed record StatusResponse(
        string Source,
        string SourceKind,
        string Border,
        string Text,
        string Lit,
        string Background,
        bool OverrideActive,
        bool BackgroundChosen,
        IReadOnlyList<HudColourEntry> HudColours,
        IReadOnlyList<string> LowContrast);

    /// <param name="discoveredColours">
    /// The distinct colours found in the commander's EDHM theme
    /// (<see cref="LunaPanel.Server.Theme.LiveThemeResolver.GetDiscoveredColours"/>)
    /// - feeds the settings gear's "From your HUD" picker group. Always
    /// available regardless of whether an override is currently active,
    /// since the picker offers these as candidates to pick FROM, not as a
    /// description of what's currently in effect. Empty when there is no
    /// usable EDHM theme - the pane still works with just the standard
    /// palette in that case.
    /// </param>
    public static StatusResponse BuildStatusResponse(HudTheme effectiveTheme, bool overrideActive, bool backgroundChosen, IReadOnlyList<EdhmDiscoveredColour> discoveredColours) => new(
        effectiveTheme.Source.ToString(),
        SourceKind(effectiveTheme.Source),
        effectiveTheme.Frame.ToHex(),
        effectiveTheme.Text.ToHex(),
        effectiveTheme.Lit.ToHex(),
        effectiveTheme.Ground.ToHex(),
        overrideActive,
        backgroundChosen,
        discoveredColours.Select(c => new HudColourEntry(c.Label, c.Color.ToHex())).ToArray(),
        ThemeContrast.LowContrastRoles(effectiveTheme));

    /// <summary>
    /// The three-way bucket the settings gear actually shows words for -
    /// "matched from your HUD" / "Elite orange" / "your own choice"
    /// (<c>ref/docs/theme.md</c>'s colour chain) - collapsing the internal
    /// <see cref="HudThemeSource"/> values most of the app never needs to
    /// tell apart. <see cref="HudThemeSource.Edhm"/> and
    /// <see cref="HudThemeSource.EdhmMatrix"/> are both "matched" (both are
    /// the commander's own EDHM data, just from a different EDHM file);
    /// every graphics-config/stock step collapses to "Stock", since none of
    /// those are EDHM data at all - the native colour they apply a matrix
    /// to is stock orange either way.
    /// </summary>
    public static string SourceKind(HudThemeSource source) => source switch
    {
        HudThemeSource.Edhm or HudThemeSource.EdhmMatrix => "Matched",
        HudThemeSource.Override => "Override",
        _ => "Stock",
    };

    /// <summary>
    /// Parses a manual-override request body - border, text and lit must
    /// each be a valid <c>#rrggbb</c> colour
    /// (<see cref="HudColor.TryParseHex"/>); anything else is refused with
    /// a plain, non-throwing error rather than a 500.
    ///
    /// <c>background</c> is the one optional field: absent (or JSON null)
    /// means "the commander is not choosing a background", which leaves the
    /// ground derived from Text. Present but unparseable is still refused -
    /// optional is not the same as ignored, and silently dropping a bad
    /// background would apply three of the four colours the pane sent.
    /// </summary>
    public static bool TryParseOverride(OverrideRequest? request, out ThemeOverride overrideValue, out string error)
    {
        if (request is not null &&
            HudColor.TryParseHex(request.Border, out var border) &&
            HudColor.TryParseHex(request.Text, out var text) &&
            HudColor.TryParseHex(request.Lit, out var lit))
        {
            HudColor? background = null;
            if (request.Background is not null)
            {
                if (!HudColor.TryParseHex(request.Background, out var backgroundColor))
                {
                    overrideValue = null!;
                    error = "background must be a #rrggbb colour when it is given at all.";
                    return false;
                }

                background = backgroundColor;
            }

            overrideValue = new ThemeOverride(border, text, lit, background);
            error = string.Empty;
            return true;
        }

        overrideValue = null!;
        error = "border, text and lit must each be a #rrggbb colour.";
        return false;
    }
}
