using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Theme;

namespace LunaPanel.Tests.Theme;

/// <summary>
/// Drives <see cref="HudThemeResolver.Resolve"/> end to end against the
/// committed EDHM and graphics fixtures, plus hand-written malformed
/// content, proving every step of the resolution chain in
/// <c>ref/docs/theme.md</c> is actually reached in the right order with
/// the right reported <see cref="HudThemeSource"/>.
/// </summary>
public class HudThemeResolverTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();

        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static string ReadEdhmFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "edhm", name));

    private static string ReadGraphicsFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "graphics", name));

    private static readonly IReadOnlyDictionary<string, string> NoIniFiles = new Dictionary<string, string>();

    // ---------------------------------------------------------------
    // Step 1: full EDHM resolve
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_FullEdhmFixtureSet_ProducesAllSixVariablesFromEdhm()
    {
        var iniFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Advanced"] = ReadEdhmFixture("Advanced.sample.ini"),
            ["Startup-Profile"] = ReadEdhmFixture("Startup-Profile.sample.ini"),
        };
        var log = new CapturingDiagnosticLog();

        var theme = HudThemeResolver.Resolve(
            ReadEdhmFixture("ThemeSettings.sample.json"),
            iniFiles,
            xmlProfileIni: null,
            graphicsConfigurationOverrideXml: null,
            graphicsConfigurationXml: null,
            log);

        Assert.Equal(HudThemeSource.Edhm, theme.Source);

        // SUPERSEDED 2026-09-06 (second correction - ref/docs/theme.md's
        // "commander's real mapping"). Before this fix (the single-anchor
        // design), Text = 0xCC5FF1 ("Chat Panel Text Color") and Frame was
        // DERIVED from it (0xAD51CD via HudColorRamp.Darken(0.15)) -
        // discarding "Radar Grid Color" (0x077CCB) entirely, which is what
        // produced the commander's "where did my EDHM colors go?" report.
        //
        // Now: Border(Frame) is fed directly by the main-text-titled match
        // ("Chat Panel Text Color", x77/y77/z77 via Advanced.sample.ini -
        // the same bytes already independently pinned in
        // FixtureIntegrityTests.SrgbEncode_AdvancedSampleIniValues_MatchIndependentlyComputedBytes),
        // and Text is fed directly by the frame/panel-line-titled match
        // ("Radar Grid Color", x78/y78/z78) - a deliberate INVERSION of the
        // obvious reading (ref/docs/theme.md explains why: border is the
        // structural element, text is what's read at a glance). Lit is
        // fixed white, never derived, never title-matched.
        Assert.Equal(new HudColor(0xCC, 0x5F, 0xF1), theme.Frame);
        Assert.Equal(new HudColor(0x07, 0x7C, 0xCB), theme.Text);
        Assert.Equal(new HudColor(0xFF, 0xFF, 0xFF), theme.Lit);

        // Accent/Dim/Ground are still always derived from Text (now
        // 0x077CCB, not 0xCC5FF1) via HudColorRamp - independently computed
        // (Python, same formula): Lighten(0.35), Darken(0.45), TintNearBlack(0.08).
        Assert.Equal(new HudColor(0x5E, 0xAA, 0xDD), theme.Accent);
        Assert.Equal(new HudColor(0x04, 0x44, 0x70), theme.Dim);
        Assert.Equal(new HudColor(0x01, 0x0A, 0x10), theme.Ground);
    }

    // ---------------------------------------------------------------
    // 2026-09-06: a role's title family missing entirely must still
    // produce a usable, non-blank theme - "sensible derivation" here means
    // falling back to whichever of Border/Text DID resolve, since there is
    // no meaningful lighten/darken relationship to invent between two
    // otherwise-unrelated HUD elements.
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_OnlyMainTextTitleResolves_BorderAndTextBothUseIt()
    {
        const string themeSettings = """
        {
            "ui_groups": [
                { "Elements": [
                    { "Title": "Main Text Color", "File": "Advanced", "Section": "Constants", "Key": "x1|y1|z1", "Value": 0, "ValueType": "Color" }
                ] }
            ]
        }
        """;
        var iniFiles = new Dictionary<string, string> { ["Advanced"] = "[Constants]\nx1 = 1.0\ny1 = 0.0\nz1 = 0.0\n" };
        var log = new CapturingDiagnosticLog();

        var theme = HudThemeResolver.Resolve(themeSettings, iniFiles, null, null, null, log);

        Assert.Equal(HudThemeSource.Edhm, theme.Source);
        var mainText = new HudColor(0xFF, 0x00, 0x00); // x1=1.0 -> 0xFF, y1/z1=0.0 -> 0x00
        Assert.Equal(mainText, theme.Frame);
        Assert.Equal(mainText, theme.Text);
        Assert.Equal(new HudColor(0xFF, 0xFF, 0xFF), theme.Lit);
    }

    [Fact]
    public void Resolve_OnlyPanelLineTitleResolves_BorderAndTextBothUseIt()
    {
        const string themeSettings = """
        {
            "ui_groups": [
                { "Elements": [
                    { "Title": "Chat Panel Lines Color", "File": "Advanced", "Section": "Constants", "Key": "x1|y1|z1", "Value": 0, "ValueType": "Color" }
                ] }
            ]
        }
        """;
        var iniFiles = new Dictionary<string, string> { ["Advanced"] = "[Constants]\nx1 = 0.0\ny1 = 1.0\nz1 = 0.0\n" };
        var log = new CapturingDiagnosticLog();

        var theme = HudThemeResolver.Resolve(themeSettings, iniFiles, null, null, null, log);

        Assert.Equal(HudThemeSource.Edhm, theme.Source);
        var panelLine = new HudColor(0x00, 0xFF, 0x00); // x1=0.0 -> 0x00, y1=1.0 -> 0xFF, z1=0.0 -> 0x00
        Assert.Equal(panelLine, theme.Frame);
        Assert.Equal(panelLine, theme.Text);
        Assert.Equal(new HudColor(0xFF, 0xFF, 0xFF), theme.Lit);
    }

    /// <summary>
    /// SUPERSEDES <c>Resolve_FullEdhmFixtureSet_ReportsSuitHudAccentAsUnresolved</c>
    /// (retired 2026-09-06, not weakened - it no longer guards anything).
    /// That test pinned "Suit HUD Accent" landing in
    /// <see cref="HudTheme.UnresolvedTitles"/> because it matched the old,
    /// now-deleted Accent candidate list but named an unsupplied INI file.
    /// With Accent no longer independently matched against EDHM titles at
    /// all (see <see cref="HudThemeResolver"/>'s type-level remarks), that
    /// element is never even attempted, so it can never be reported as
    /// unresolved - this test pins the corrected behaviour directly, rather
    /// than leaving the old, now-false claim standing.
    /// </summary>
    [Fact]
    public void Resolve_FullEdhmFixtureSet_SuitHudAccent_NeverAttemptedOrReportedUnresolved()
    {
        var iniFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Advanced"] = ReadEdhmFixture("Advanced.sample.ini"),
            ["Startup-Profile"] = ReadEdhmFixture("Startup-Profile.sample.ini"),
        };
        var log = new CapturingDiagnosticLog();

        var theme = HudThemeResolver.Resolve(
            ReadEdhmFixture("ThemeSettings.sample.json"), iniFiles, null, null, null, log);

        Assert.DoesNotContain("Suit HUD Accent", theme.UnresolvedTitles);
        Assert.DoesNotContain(log.Events, e => e.Category == "Theme" && e.Message.Contains("Suit HUD Accent"));
    }

    [Fact]
    public void Resolve_FullEdhmFixtureSet_LogsEdhmAsWinningSource()
    {
        var iniFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Advanced"] = ReadEdhmFixture("Advanced.sample.ini"),
            ["Startup-Profile"] = ReadEdhmFixture("Startup-Profile.sample.ini"),
        };
        var log = new CapturingDiagnosticLog();

        HudThemeResolver.Resolve(ReadEdhmFixture("ThemeSettings.sample.json"), iniFiles, null, null, null, log);

        Assert.Contains(log.Events, e => e.Category == "Theme" && e.Message.Contains("EDHM per-element"));
    }

    // ---------------------------------------------------------------
    // Step 1 -> Step 2 fall-through
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_NoThemeSettingsJson_FallsThroughToEdhmMatrix()
    {
        var log = new CapturingDiagnosticLog();

        var theme = HudThemeResolver.Resolve(
            themeSettingsJson: null,
            NoIniFiles,
            ReadEdhmFixture("XML-Profile.sample.ini"),
            graphicsConfigurationOverrideXml: null,
            graphicsConfigurationXml: null,
            log);

        Assert.Equal(HudThemeSource.EdhmMatrix, theme.Source);

        // Base colour = XML-Profile.sample.ini's matrix applied to stock
        // orange native (1.0, 128/255, 0.0). Expected bytes computed
        // independently (Python): (56,183,255).
        Assert.Equal(new HudColor(56, 183, 255), theme.Text);
        Assert.Contains(log.Events, e => e.Category == "Theme" && e.Message.Contains("XML-Profile.ini"));
    }

    [Fact]
    public void Resolve_MalformedThemeSettingsJson_FallsThroughToEdhmMatrix_RatherThanThrowing()
    {
        var log = new CapturingDiagnosticLog();

        var theme = HudThemeResolver.Resolve(
            "{ not valid json",
            NoIniFiles,
            ReadEdhmFixture("XML-Profile.sample.ini"),
            null,
            null,
            log);

        Assert.Equal(HudThemeSource.EdhmMatrix, theme.Source);
    }

    // ---------------------------------------------------------------
    // Step 2 -> Step 3 fall-through
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_NoXmlProfileIni_FallsThroughToGraphicsConfigurationOverride()
    {
        var log = new CapturingDiagnosticLog();

        var theme = HudThemeResolver.Resolve(
            null,
            NoIniFiles,
            xmlProfileIni: null,
            ReadGraphicsFixture("guicolour-custom.xml"),
            graphicsConfigurationXml: null,
            log);

        Assert.Equal(HudThemeSource.GraphicsConfigurationOverride, theme.Source);

        // Base colour = guicolour-custom.xml's matrix applied to stock
        // orange native - independently computed (160,115,32), and its
        // four derived roles.
        Assert.Equal(new HudColor(160, 115, 32), theme.Text);
        Assert.Equal(new HudColor(136, 98, 27), theme.Frame);
        Assert.Equal(new HudColor(193, 164, 110), theme.Accent);
        Assert.Equal(new HudColor(222, 206, 177), theme.Lit);
        Assert.Equal(new HudColor(88, 63, 18), theme.Dim);
        Assert.Equal(new HudColor(13, 9, 3), theme.Ground);
    }

    [Fact]
    public void Resolve_MalformedXmlProfileIni_FallsThroughToGraphicsConfigurationOverride()
    {
        var log = new CapturingDiagnosticLog();

        var theme = HudThemeResolver.Resolve(
            null,
            NoIniFiles,
            "[Constants]\nx150 = notanumber",
            ReadGraphicsFixture("guicolour-custom.xml"),
            null,
            log);

        Assert.Equal(HudThemeSource.GraphicsConfigurationOverride, theme.Source);
    }

    // ---------------------------------------------------------------
    // Step 3 -> Step 4 fall-through (the "often empty" real-world case)
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_EmptyGraphicsConfigurationOverride_FallsThroughToDefault_AndReportsStockOrange()
    {
        var log = new CapturingDiagnosticLog();

        var theme = HudThemeResolver.Resolve(
            null,
            NoIniFiles,
            null,
            ReadGraphicsFixture("override-empty.xml"),
            ReadGraphicsFixture("guicolour-identity.xml"),
            log);

        Assert.Equal(HudThemeSource.GraphicsConfigurationDefault, theme.Source);

        // The identity matrix reproduces stock orange exactly.
        Assert.Equal(HudThemeDefaults.StockOrange, theme.Text);
        Assert.Equal(new HudColor(0xFF, 0x80, 0x00), theme.Text);
    }

    [Fact]
    public void Resolve_MissingGraphicsConfigurationOverride_FallsThroughToDefault()
    {
        var log = new CapturingDiagnosticLog();

        var theme = HudThemeResolver.Resolve(
            null, NoIniFiles, null, null, ReadGraphicsFixture("guicolour-identity.xml"), log);

        Assert.Equal(HudThemeSource.GraphicsConfigurationDefault, theme.Source);
        Assert.Equal(HudThemeDefaults.StockOrange, theme.Text);
    }

    // ---------------------------------------------------------------
    // Step 4 -> Stock (nothing usable at all)
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_NothingUsableAtAll_FallsBackToStock()
    {
        var log = new CapturingDiagnosticLog();

        var theme = HudThemeResolver.Resolve(null, NoIniFiles, null, null, null, log);

        Assert.Equal(HudThemeSource.Stock, theme.Source);
        Assert.Equal(HudThemeDefaults.StockOrange, theme.Text);
        Assert.Contains(log.Events, e => e.Category == "Theme" && e.Level == DiagnosticLevel.Warn);
    }

    [Fact]
    public void Resolve_MalformedGraphicsConfigurationXml_FallsBackToStock_RatherThanThrowing()
    {
        var log = new CapturingDiagnosticLog();

        var theme = HudThemeResolver.Resolve(
            null, NoIniFiles, null, null, "<GraphicsConfig><Unclosed>", log);

        Assert.Equal(HudThemeSource.Stock, theme.Source);
    }

    // ---------------------------------------------------------------
    // Malformed EDHM INI content for a resolvable-looking title
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_EdhmTitleNamesFileThatIsMissingRequiredKeys_FallsThroughRatherThanThrowing()
    {
        const string themeSettings = """
        {
            "ui_groups": [
                { "Elements": [
                    { "Title": "Main Text Color", "File": "Advanced", "Section": "Constants", "Key": "x1|y1|z1", "Value": 0, "ValueType": "Color" }
                ] }
            ]
        }
        """;
        var iniFiles = new Dictionary<string, string> { ["Advanced"] = "[Constants]\ny1 = 0.5\nz1 = 0.5\n" }; // x1 missing
        var log = new CapturingDiagnosticLog();

        var theme = HudThemeResolver.Resolve(
            themeSettings, iniFiles, ReadEdhmFixture("XML-Profile.sample.ini"), null, null, log);

        // Only element in the theme is "Main Text Color", and it fails to
        // resolve (x1 missing). It doesn't match FrameCandidates either, so
        // neither family resolves at all -> whole EDHM step abandoned ->
        // falls through to the EDHM matrix step.
        Assert.Equal(HudThemeSource.EdhmMatrix, theme.Source);
    }

    // ---------------------------------------------------------------
    // O16 regression: an "Inactive"-titled element must never win --lp-lit
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_EdhmThemeContainsInactiveTitledElement_DoesNotResolveLitToIt_O16Regression()
    {
        // Originally: LitCandidates led with "Active" - a substring of
        // "Inactive" - so an element titled exactly like this one would
        // have won --lp-lit by accident of substring matching (the real
        // defect measured against the commander's live theme,
        // ref/docs/theme.md). Fixed 2026-09-06 by removing per-title
        // matching for Lit entirely; re-confirmed by the second correction
        // the same day (ref/docs/theme.md's "commander's real mapping"),
        // which keeps Lit fixed white and STILL never title-matched -
        // O16 stays permanently closed under either design, since there is
        // no LitCandidates list left to collide with anything.
        const string themeSettings = """
        {
            "ui_groups": [
                { "Elements": [
                    { "Title": "Main Text Color", "File": "Advanced", "Section": "Constants", "Key": "x1|y1|z1", "Value": 0, "ValueType": "Color" },
                    { "Title": "Inactive Weapons Color", "File": "Advanced", "Section": "Constants", "Key": "x2|y2|z2", "Value": 0, "ValueType": "Color" }
                ] }
            ]
        }
        """;
        var iniFiles = new Dictionary<string, string>
        {
            // x2/y2/z2 deliberately NOT white/1.0, so this test can tell
            // apart "Lit correctly fixed white" from "Lit accidentally
            // resolved to Inactive Weapons Color, which happens to be
            // white" - asserting only equality to white would be vacuous
            // to that distinction.
            ["Advanced"] = "[Constants]\nx1 = 0.5\ny1 = 0.5\nz1 = 0.5\nx2 = 0.1\ny2 = 0.2\nz2 = 0.3\n",
        };
        var log = new CapturingDiagnosticLog();

        var theme = HudThemeResolver.Resolve(themeSettings, iniFiles, null, null, null, log);

        Assert.Equal(HudThemeSource.Edhm, theme.Source);

        // "Inactive Weapons Color" doesn't match TextCandidates or
        // FrameCandidates either (neither "Text" nor any of
        // Border/Panel Line/Line Color/Line Colour/Grid/Corner/Frame is a
        // substring of it), so it plays no part in this theme at all - Lit
        // must be the fixed white value, not the (distinctly non-white)
        // "Inactive Weapons Color" bytes.
        Assert.Equal(new HudColor(0xFF, 0xFF, 0xFF), theme.Lit);
    }

    // ---------------------------------------------------------------
    // ExtractDiscoveredColours - feeds the settings gear's "From your HUD"
    // picker group (ref/docs/web-client.md). Unrelated to which two
    // elements Resolve itself matches for Border/Text - this walks every
    // Color-typed element in the theme, not just those two families.
    // ---------------------------------------------------------------

    [Fact]
    public void ExtractDiscoveredColours_FullFixtureSet_ReturnsOnlyTheResolvableDistinctColours()
    {
        var iniFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Advanced"] = ReadEdhmFixture("Advanced.sample.ini"),
            ["Startup-Profile"] = ReadEdhmFixture("Startup-Profile.sample.ini"),
        };

        var colours = HudThemeResolver.ExtractDiscoveredColours(ReadEdhmFixture("ThemeSettings.sample.json"), iniFiles);

        // The fixture set has exactly 3 Color-typed elements ("Chat Panel
        // Text Color", "Radar Grid Color", "Suit HUD Accent"). The third
        // names File "SuitHud", which isn't supplied, so it can never
        // resolve to a colour and must be skipped rather than appearing as
        // a blank/garbage entry.
        Assert.Equal(2, colours.Count);
        Assert.Equal("Chat Panel Text", colours[0].Label);
        Assert.Equal(new HudColor(0xCC, 0x5F, 0xF1), colours[0].Color);
        Assert.Equal("Radar Grid", colours[1].Label);
        Assert.Equal(new HudColor(0x07, 0x7C, 0xCB), colours[1].Color);
        Assert.DoesNotContain(colours, c => c.Label.Contains("Suit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtractDiscoveredColours_TwoElementsResolveToTheSameColour_DedupedKeepingFirstTitle()
    {
        const string themeSettings = """
        {
            "ui_groups": [
                { "Elements": [
                    { "Title": "First Element Color", "File": "Advanced", "Section": "Constants", "Key": "x1|y1|z1", "Value": 0, "ValueType": "Color" },
                    { "Title": "Second Element Color", "File": "Advanced", "Section": "Constants", "Key": "x2|y2|z2", "Value": 0, "ValueType": "Color" }
                ] }
            ]
        }
        """;
        // Both keys decode to the exact same bytes - a real, if unusual,
        // case (two differently-named HUD elements deliberately painted
        // the same colour).
        var iniFiles = new Dictionary<string, string>
        {
            ["Advanced"] = "[Constants]\nx1 = 0.5\ny1 = 0.5\nz1 = 0.5\nx2 = 0.5\ny2 = 0.5\nz2 = 0.5\n",
        };

        var colours = HudThemeResolver.ExtractDiscoveredColours(themeSettings, iniFiles);

        Assert.Single(colours);
        Assert.Equal("First Element", colours[0].Label);
    }

    [Fact]
    public void ExtractDiscoveredColours_NoThemeSettingsJson_ReturnsEmpty()
    {
        var colours = HudThemeResolver.ExtractDiscoveredColours(null, NoIniFiles);

        Assert.Empty(colours);
    }

    [Fact]
    public void ExtractDiscoveredColours_MalformedThemeSettingsJson_ReturnsEmpty_RatherThanThrowing()
    {
        var colours = HudThemeResolver.ExtractDiscoveredColours("{ not valid json", NoIniFiles);

        Assert.Empty(colours);
    }

    // ---------------------------------------------------------------
    // FromOverride - colour chain step 3 (ref/docs/theme.md): a manual
    // override, not derived from EDHM at all.
    // ---------------------------------------------------------------

    [Fact]
    public void FromOverride_ReturnsOverrideSource_WithBorderTextLitExactlyAsGiven()
    {
        var overrideValue = new ThemeOverride(new HudColor(1, 2, 3), new HudColor(10, 20, 30), new HudColor(200, 210, 220));

        var theme = HudThemeResolver.FromOverride(overrideValue);

        Assert.Equal(HudThemeSource.Override, theme.Source);
        Assert.Equal(overrideValue.Border, theme.Frame);
        Assert.Equal(overrideValue.Text, theme.Text);
        Assert.Equal(overrideValue.Lit, theme.Lit);
        Assert.Empty(theme.UnresolvedTitles);
    }

    [Fact]
    public void FromOverride_DerivesAccentDimGround_FromTextViaHudColorRamp_NotFromBorderOrLit()
    {
        // Border and Lit are deliberately far from Text so a wrong
        // derivation source (Border or Lit instead of Text) would produce a
        // visibly different, wrong result rather than accidentally matching.
        var overrideValue = new ThemeOverride(new HudColor(0, 0, 0), new HudColor(204, 95, 241), new HudColor(255, 255, 255));

        var theme = HudThemeResolver.FromOverride(overrideValue);

        Assert.Equal(HudColorRamp.Lighten(overrideValue.Text, 0.35), theme.Accent);
        Assert.Equal(HudColorRamp.Darken(overrideValue.Text, 0.45), theme.Dim);
        Assert.Equal(HudColorRamp.TintNearBlack(overrideValue.Text, 0.08), theme.Ground);
    }

    [Fact]
    public void FromOverride_NullOverride_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HudThemeResolver.FromOverride(null!));
    }

    // ---------------------------------------------------------------
    // Background as a fourth commander-chosen role (2026-09-10,
    // ref/docs/theme.md). Ground was previously derived from Text and
    // never choosable; it is now derived from Text ONLY while the
    // commander has not chosen one.
    // ---------------------------------------------------------------

    [Fact]
    public void FromOverride_ChosenBackground_IsUsedAsGroundExactly_NotTintedTowardBlack()
    {
        // Deliberately a bright colour: if the chosen background were put
        // through TintNearBlack the way an underived ground is, this would
        // come back as #100813 rather than the colour actually picked -
        // which is the whole failure this pins against. The commander's
        // judgement on their own panel is final (ref/docs/theme.md's
        // contrast section); a picked background is honoured literally.
        var overrideValue = new ThemeOverride(
            new HudColor(0x00, 0xFF, 0x00),
            new HudColor(0xCC, 0x5F, 0xF1),
            new HudColor(0xFF, 0xFF, 0xFF),
            new HudColor(0x33, 0x78, 0xFF));

        var theme = HudThemeResolver.FromOverride(overrideValue);

        Assert.Equal(new HudColor(0x33, 0x78, 0xFF), theme.Ground);
    }

    [Fact]
    public void FromOverride_ChosenBackground_LeavesBorderTextLitAccentDimUntouched()
    {
        var withoutBackground = new ThemeOverride(
            new HudColor(0x00, 0xFF, 0x00),
            new HudColor(0xCC, 0x5F, 0xF1),
            new HudColor(0xFF, 0xFF, 0xFF));
        var withBackground = withoutBackground with { Background = new HudColor(0x33, 0x78, 0xFF) };

        var before = HudThemeResolver.FromOverride(withoutBackground);
        var after = HudThemeResolver.FromOverride(withBackground);

        Assert.Equal(before.Frame, after.Frame);
        Assert.Equal(before.Text, after.Text);
        Assert.Equal(before.Lit, after.Lit);
        Assert.Equal(before.Accent, after.Accent);
        Assert.Equal(before.Dim, after.Dim);
        Assert.NotEqual(before.Ground, after.Ground);
    }

    [Fact]
    public void FromOverride_NoChosenBackground_StillDerivesTheNearBlackGroundFromText()
    {
        // Asserted as a literal rather than by calling TintNearBlack again:
        // 0xCC/0x5F/0xF1 kept at 8% (away-from-zero rounding) is 0x10/0x08/
        // 0x13. This is what --lp-ground has always resolved to for a
        // purple-ish Text, and what an override stored before this feature
        // existed must keep resolving to.
        var overrideValue = new ThemeOverride(
            new HudColor(0x00, 0xFF, 0x00),
            new HudColor(0xCC, 0x5F, 0xF1),
            new HudColor(0xFF, 0xFF, 0xFF));

        var theme = HudThemeResolver.FromOverride(overrideValue);

        Assert.Null(overrideValue.Background);
        Assert.Equal(new HudColor(0x10, 0x08, 0x13), theme.Ground);
    }
}
