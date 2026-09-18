using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Core.Theme;

/// <summary>
/// Resolves a <see cref="HudTheme"/> from already-read file contents,
/// walking the four-step chain documented in <c>ref/docs/theme.md</c>:
/// EDHM per-element colours, EDHM's own matrix, Elite's player-set matrix
/// override, then Elite's stock default matrix. This type never discovers
/// any of those files itself - every input arrives as a string (or
/// <see langword="null"/> for "this file wasn't found/read"), matching the
/// rest of <c>LunaPanel.Core</c>'s discipline that finding files on disk is
/// the host process's job, never Core's.
/// </summary>
public static class HudThemeResolver
{
    // 2026-09-06 (second correction - ref/docs/theme.md's "The commander's
    // real mapping", superseding the single-anchor fix immediately below).
    // The single-anchor design deleted FrameCandidates and derived
    // Border/Accent/Lit/Dim/Ground ALL from whichever element matched
    // TextCandidates, discarding every other colour in the commander's own
    // theme - "where did my EDHM colors go?" was the direct complaint this
    // produced. FrameCandidates is restored here, but the assignment is a
    // deliberate INVERSION of the obvious reading: TextCandidates (the
    // "main text" family) now feeds Border, and FrameCandidates (the
    // "frame/panel-line" family) now feeds Text. On the panel the border is
    // the structural element and the text is what's actually read at a
    // glance, so the commander's structural HUD colour belongs on the
    // frame and the more prominent one belongs on the label - do not "fix"
    // this back to the naive mapping. Accent/Dim/Ground are still always
    // derived from Text via HudColorRamp, unchanged. Lit is fixed white and
    // is NEVER title-matched at all (not even via a restored LitCandidates)
    // - this is the commander's own spec, and it also keeps O16
    // permanently closed, since nothing can collide with "Inactive" if
    // nothing is ever matched for Lit in the first place.
    //
    // Original 2026-09-06 note this supersedes, kept for history: "Fixed
    // (ref/docs/theme.md's 'First contact with a real theme', O16 in
    // tests/notes/open-items.md): Border(Frame)/Accent/Lit/Dim used to be
    // matched independently against their own EDHM titles
    // (FrameCandidates/AccentCandidates/LitCandidates/DimCandidates, all
    // deleted here rather than repaired) ... Deriving every non-Text role
    // from one anchor via HudColorRamp ... fixes both" - the "two things
    // went wrong" diagnosis (incoherent theme; Lit-vs-Inactive substring
    // race) was correct, but discarding every EDHM colour except one was
    // the wrong fix for the first problem. The commander's own report
    // ("where did my EDHM colors go?") is what corrected it.
    private static readonly string[] TextCandidates = { "Main Text", "Text Color", "Text Colour", "Text" };
    private static readonly string[] FrameCandidates = { "Border", "Panel Line", "Line Color", "Line Colour", "Grid", "Corner", "Frame" };

    // Lit is never title-matched (see the remarks above) - always this
    // fixed value for EDHM per-element resolution. The matrix-based
    // automatic steps (EdhmMatrix/GraphicsConfigurationOverride/
    // GraphicsConfigurationDefault/Stock) are unaffected by this dispatch
    // and keep deriving Lit from their single anchor colour via
    // HudColorRamp - there is no separate "main text" vs "frame/panel-line"
    // split possible from a single matrix-derived colour, so there is
    // nothing here for the commander's mapping to apply to.
    private static readonly HudColor FixedLitColor = new(0xFF, 0xFF, 0xFF);

    // Ramp factors used to derive a role from a single anchor colour - see
    // HudColorRamp. Chosen, not measured: Accent/Lit lighten progressively
    // more, Dim darkens, Ground nearly to black. See ref/docs/theme.md.
    private const double FrameDarkenT = 0.15;
    private const double AccentLightenT = 0.35;
    private const double LitLightenT = 0.65;
    private const double DimDarkenT = 0.45;
    private const double GroundKeepFraction = 0.08;

    /// <param name="themeSettingsJson">EDHM's <c>ThemeSettings.json</c> content, or <see langword="null"/> if not present.</param>
    /// <param name="edhmIniFilesByName">EDHM INI file contents keyed by the <c>File</c> name a <c>ThemeSettings.json</c> element names (e.g. <c>"Advanced"</c>, <c>"Startup-Profile"</c>). A name an element references but that isn't a key here is exactly the "title failed to resolve" case.</param>
    /// <param name="xmlProfileIni">EDHM's own <c>XML-Profile.ini</c> content, or <see langword="null"/>.</param>
    /// <param name="graphicsConfigurationOverrideXml">Elite's <c>GraphicsConfigurationOverride.xml</c> content, or <see langword="null"/>.</param>
    /// <param name="graphicsConfigurationXml">Elite's <c>GraphicsConfiguration.xml</c> content, or <see langword="null"/>.</param>
    /// <param name="log">Where the winning source and any unresolved EDHM titles are logged, category <c>Theme</c>.</param>
    public static HudTheme Resolve(
        string? themeSettingsJson,
        IReadOnlyDictionary<string, string> edhmIniFilesByName,
        string? xmlProfileIni,
        string? graphicsConfigurationOverrideXml,
        string? graphicsConfigurationXml,
        IDiagnosticLog log)
    {
        var unresolvedTitles = new List<string>();

        var elements = themeSettingsJson is not null
            ? EdhmThemeSettingsParser.Parse(themeSettingsJson)
            : null;

        if (elements is not null)
        {
            var mainTextFound = TryResolveRole(elements, TextCandidates, edhmIniFilesByName, unresolvedTitles, out var mainTextColor);
            var panelLineFound = TryResolveRole(elements, FrameCandidates, edhmIniFilesByName, unresolvedTitles, out var panelLineColor);

            // The whole EDHM per-element step is usable as soon as EITHER
            // family resolves - unlike the old single-anchor design (and
            // the design before it), there is no single mandatory anchor
            // any more. If only one resolved, the other role falls back to
            // that same colour directly: there is no meaningful lighten/
            // darken relationship between two otherwise-unrelated HUD
            // elements to invent, so "derive" here means "never leave a
            // role blank", not a ramp step.
            if (mainTextFound || panelLineFound)
            {
                var border = mainTextFound ? mainTextColor : panelLineColor;
                var text = panelLineFound ? panelLineColor : mainTextColor;

                log.Info("Theme", "Resolved HUD theme from EDHM per-element colours (ThemeSettings.json).");
                LogUnresolved(log, unresolvedTitles);

                // Deliberate inversion (see the type-level remarks above):
                // border is fed by the main-text match, text by the
                // frame/panel-line match. Lit is always fixed white here,
                // never derived and never title-matched.
                return BuildFromRoles(border, text, FixedLitColor, HudThemeSource.Edhm, unresolvedTitles);
            }
        }

        var edhmMatrix = xmlProfileIni is not null ? EdhmXmlProfileMatrixParser.Parse(xmlProfileIni) : null;
        if (edhmMatrix is not null)
        {
            log.Info("Theme", "Resolved HUD theme from EDHM's XML-Profile.ini colour matrix (per-element colours unusable).");
            LogUnresolved(log, unresolvedTitles);
            return BuildFromAnchor(HudThemeDefaults.ApplyMatrix(edhmMatrix.Value), HudThemeSource.EdhmMatrix, unresolvedTitles);
        }

        var overrideMatrix = graphicsConfigurationOverrideXml is not null
            ? GraphicsConfigMatrixParser.ParseGuiColourDefault(graphicsConfigurationOverrideXml)
            : null;
        if (overrideMatrix is not null)
        {
            log.Info("Theme", "Resolved HUD theme from GraphicsConfigurationOverride.xml's colour matrix.");
            LogUnresolved(log, unresolvedTitles);
            return BuildFromAnchor(HudThemeDefaults.ApplyMatrix(overrideMatrix.Value), HudThemeSource.GraphicsConfigurationOverride, unresolvedTitles);
        }

        var defaultMatrix = graphicsConfigurationXml is not null
            ? GraphicsConfigMatrixParser.ParseGuiColourDefault(graphicsConfigurationXml)
            : null;
        if (defaultMatrix is not null)
        {
            log.Info("Theme", "Resolved HUD theme from GraphicsConfiguration.xml's default colour matrix.");
            LogUnresolved(log, unresolvedTitles);
            return BuildFromAnchor(HudThemeDefaults.ApplyMatrix(defaultMatrix.Value), HudThemeSource.GraphicsConfigurationDefault, unresolvedTitles);
        }

        log.Warn("Theme", "No usable theme source found at all (EDHM, XML-Profile matrix, and both Elite graphics config files all unusable); falling back to stock HUD orange.");
        return BuildFromAnchor(HudThemeDefaults.StockOrange, HudThemeSource.Stock, unresolvedTitles);
    }

    private static HudTheme BuildFromAnchor(HudColor anchor, HudThemeSource source, IReadOnlyList<string> unresolvedTitles) =>
        BuildFromRoles(
            HudColorRamp.Darken(anchor, FrameDarkenT),
            anchor,
            HudColorRamp.Lighten(anchor, LitLightenT),
            source,
            unresolvedTitles);

    /// <summary>
    /// The one place all six CSS roles are ever assembled, for either kind
    /// of theme this resolver produces: automatic (<see cref="BuildFromAnchor"/>,
    /// where <paramref name="border"/> and <paramref name="lit"/> are
    /// themselves derived from <paramref name="text"/>) or a commander's
    /// manual override (<see cref="FromOverride"/>, where they are the
    /// commander's own choices, unadjusted). Accent and Dim are always
    /// derived from <paramref name="text"/> via <see cref="HudColorRamp"/>
    /// and are never exposed as a fifth or sixth choice - "four choices is
    /// a feature, six is a paint program" (<c>ref/docs/theme.md</c>).
    /// Ground is derived from <paramref name="text"/> the same way
    /// <em>unless</em> <paramref name="ground"/> is supplied, which only a
    /// manual override with a commander-chosen Background ever does; every
    /// automatic step passes <see langword="null"/> and gets the near-black
    /// tint it always got.
    /// </summary>
    private static HudTheme BuildFromRoles(HudColor border, HudColor text, HudColor lit, HudThemeSource source, IReadOnlyList<string> unresolvedTitles, HudColor? ground = null) => new(
        ground ?? HudColorRamp.TintNearBlack(text, GroundKeepFraction),
        border,
        text,
        HudColorRamp.Lighten(text, AccentLightenT),
        lit,
        HudColorRamp.Darken(text, DimDarkenT),
        source,
        unresolvedTitles);

    /// <summary>
    /// Builds a <see cref="HudTheme"/> straight from a commander's manual
    /// override - colour chain step 3 (<c>ref/docs/theme.md</c>): a fixed,
    /// built-in choice not derived from EDHM at all, always available
    /// regardless of whether EDHM is installed, discoverable, or resolvable.
    /// Border, Lit and (when chosen) Background are exactly the commander's
    /// own choices, honoured literally and never tinted or adjusted;
    /// Accent and Dim are still derived from Text, same as automatic
    /// resolution, and so is Ground while no Background has been chosen.
    /// </summary>
    public static HudTheme FromOverride(ThemeOverride overrideValue)
    {
        ArgumentNullException.ThrowIfNull(overrideValue);
        return BuildFromRoles(overrideValue.Border, overrideValue.Text, overrideValue.Lit, HudThemeSource.Override, Array.Empty<string>(), overrideValue.Background);
    }

    /// <summary>
    /// Every distinct colour the commander's EDHM theme actually defines,
    /// deduplicated by resolved value and labelled with the title of the
    /// first element (in document order) to produce it, trimming a
    /// trailing " Color"/" Colour" for readability (e.g. "Main Text Color"
    /// -&gt; "Main Text"). Feeds the settings gear's "From your HUD" picker
    /// group (<c>ref/docs/web-client.md</c>) - unrelated to which two
    /// elements <see cref="Resolve"/> itself matches for Border/Text; this
    /// walks every <c>Color</c>-typed element in the theme, not just the
    /// two role families above. Returns an empty list, never
    /// <see langword="null"/>, whenever there is no usable EDHM theme at
    /// all - "absent or empty" is the commander-facing contract, not a
    /// special case a caller has to branch on.
    /// </summary>
    public static IReadOnlyList<EdhmDiscoveredColour> ExtractDiscoveredColours(
        string? themeSettingsJson,
        IReadOnlyDictionary<string, string> edhmIniFilesByName)
    {
        var elements = themeSettingsJson is not null ? EdhmThemeSettingsParser.Parse(themeSettingsJson) : null;
        if (elements is null)
        {
            return Array.Empty<EdhmDiscoveredColour>();
        }

        var seen = new HashSet<HudColor>();
        var results = new List<EdhmDiscoveredColour>();

        foreach (var element in elements)
        {
            if (!TryResolveElement(element, edhmIniFilesByName, out var color) || !seen.Add(color))
            {
                continue;
            }

            results.Add(new EdhmDiscoveredColour(TrimColorSuffix(element.Title), color));
        }

        return results;
    }

    private static string TrimColorSuffix(string title)
    {
        const string colorSuffix = " Color";
        const string colourSuffix = " Colour";

        if (title.EndsWith(colorSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return title[..^colorSuffix.Length];
        }

        if (title.EndsWith(colourSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return title[..^colourSuffix.Length];
        }

        return title;
    }

    private static void LogUnresolved(IDiagnosticLog log, List<string> unresolvedTitles)
    {
        foreach (var title in unresolvedTitles)
        {
            log.Warn("Theme", $"EDHM title \"{title}\" matched a role but could not be resolved to a usable colour; a derived value was used instead.");
        }
    }

    private static bool TryResolveRole(
        IReadOnlyList<EdhmColorElement> elements,
        string[] candidateKeywords,
        IReadOnlyDictionary<string, string> iniFilesByName,
        List<string> unresolvedTitles,
        out HudColor color)
    {
        foreach (var keyword in candidateKeywords)
        {
            foreach (var element in elements)
            {
                if (!element.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryResolveElement(element, iniFilesByName, out color))
                {
                    return true;
                }

                unresolvedTitles.Add(element.Title);
            }
        }

        color = default;
        return false;
    }

    private static bool TryResolveElement(EdhmColorElement element, IReadOnlyDictionary<string, string> iniFilesByName, out HudColor color)
    {
        color = default;

        if (!iniFilesByName.TryGetValue(element.File, out var iniContent))
        {
            return false;
        }

        var constants = IniConstants.Parse(iniContent);
        var bytes = new byte[3];

        for (var i = 0; i < 3; i++)
        {
            if (!constants.TryGetValue(element.Keys[i], out var linear))
            {
                return false;
            }

            bytes[i] = SrgbCodec.DecodeLinearToByte(linear);
        }

        color = new HudColor(bytes[0], bytes[1], bytes[2]);
        return true;
    }
}
