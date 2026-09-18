using System.Reflection;
using LunaPanel.Core.Catalogue;
using LunaPanel.Core.GameState;

namespace LunaPanel.Tests.Catalogue;

/// <summary>
/// Pins <see cref="LunaPanel.Core.Catalogue.Catalogue.Parse"/> against both
/// hand-crafted malformed JSON (which must fail loudly rather than
/// producing an empty catalogue) and the real shipped
/// <c>catalogue.json</c> - loaded the same way LunaPanel.Server actually
/// ships it, via the embedded resource, never a filesystem path.
///
/// The vocabulary sweep here
/// (<see cref="ShippedCatalogue_EveryCuratedActionName_ExistsInActionNamesTxt"/>)
/// is the single most valuable test in this file: it is what stops a typo
/// like <c>FocusLeftPanle</c> from shipping as a permanently dead catalogue
/// entry, by checking every curated name against a vocabulary independently
/// derived from a real, currently-installed Elite Dangerous bindings file
/// (see <c>Fixtures/bindings/action-names.txt</c>'s own header for
/// provenance).
/// </summary>
public class CatalogueTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static string ReadShippedCatalogueJson()
    {
        var assembly = Assembly.Load("LunaPanel.Server");
        using var stream = assembly.GetManifestResourceStream("LunaPanel.Server.definitions.catalogue.json");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }

    private static IReadOnlySet<string> ReadActionNamesVocabulary()
    {
        var lines = File.ReadAllLines(Path.Combine(FixturesRoot, "bindings", "action-names.txt"));
        return lines
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            .Select(line => line.Trim())
            .ToHashSet(StringComparer.Ordinal);
    }

    // ---------------------------------------------------------------
    // The typo sweep - the most valuable test in this file.
    // ---------------------------------------------------------------

    [Fact]
    public void ShippedCatalogue_EveryCuratedActionName_ExistsInActionNamesTxt()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(ReadShippedCatalogueJson());
        var vocabulary = ReadActionNamesVocabulary();

        var typos = catalogue.Actions.Keys.Where(name => !vocabulary.Contains(name)).ToList();

        Assert.True(typos.Count == 0,
            "Curated action name(s) not found in the real Frontier vocabulary (typo?): " + string.Join(", ", typos));
    }

    [Fact]
    public void ShippedCatalogue_HasAtLeast100CuratedActions()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(ReadShippedCatalogueJson());
        Assert.True(catalogue.Actions.Count >= 100, $"Expected >= 100 curated actions, got {catalogue.Actions.Count}.");
    }

    // ---------------------------------------------------------------
    // Structural sweeps over the shipped catalogue.
    // ---------------------------------------------------------------

    // A curated label is up to two lines, broken explicitly with a newline
    // where the catalogue author wants the break (rendered with
    // white-space: pre-line - see ref/docs/button-naming.md). Ten characters
    // per line is the aim, judged rather than counted; twelve is the hard
    // ceiling here, so a curated line is never wider than a user override is
    // allowed to be. [2026-09-09] These twelve characters no longer EQUAL
    // LayoutValidator.UserLabelLineCap, which the commander raised to 15 -
    // deliberately not raised with it. A curated label is written once, by
    // us, and stays tight; the wider budget is the commander's to spend on
    // their own names. The "never wider than an override" relationship is
    // what matters and still holds.
    private const int MaxLabelLines = 2;
    private const int MaxLabelLineLength = 12;

    // 2026-09-06: this REPLACES ShippedCatalogue_EveryLabel_IsTenCharactersOrFewer,
    // whose flat 10-character cap (before-value: Label.Length <= 10, one line)
    // was the budget for the old squeezed upper-case abbreviations such as
    // HYPRSPACE and SHIELD CL. That cap was not loosened to let longer labels
    // through; it was retired when every label was rewritten as real words
    // over up to two lines, and the thing it actually guarded - a label that
    // fits a button without the renderer choosing where to wrap it - is now
    // pinned directly, per line, below. Do not read this as a relaxed cap.
    [Fact]
    public void ShippedCatalogue_EveryLabel_IsAtMostTwoLines_EachWithinTheLineBudget_NoBlankOrPaddedLines()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(ReadShippedCatalogueJson());

        var offenders = new List<string>();
        foreach (var (name, action) in catalogue.Actions)
        {
            var label = action.Label;
            var shown = label.Replace("\n", "\\n");

            if (label.Contains('\r') || label.Contains('\t'))
            {
                offenders.Add($"{name}='{shown}' contains a control character other than a bare newline");
                continue;
            }

            var lines = label.Split('\n');
            if (lines.Length > MaxLabelLines)
            {
                offenders.Add($"{name}='{shown}' has {lines.Length} lines (max {MaxLabelLines})");
            }

            foreach (var line in lines)
            {
                if (line.Length == 0)
                {
                    offenders.Add($"{name}='{shown}' has an empty line");
                }
                else if (line != line.Trim())
                {
                    offenders.Add($"{name}='{shown}' has leading/trailing whitespace on a line");
                }
                else if (line.Length > MaxLabelLineLength)
                {
                    offenders.Add($"{name}='{shown}' line '{line}' is {line.Length} chars (max {MaxLabelLineLength})");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Label(s) do not fit a two-line button: " + string.Join("; ", offenders));
    }

    [Fact]
    public void ShippedCatalogue_EveryActionCategory_ResolvesToADeclaredCategory()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(ReadShippedCatalogueJson());

        var unresolved = catalogue.Actions
            .Where(kvp => !catalogue.Categories.ContainsKey(kvp.Value.Category))
            .Select(kvp => kvp.Key)
            .ToList();

        Assert.True(unresolved.Count == 0, "Action(s) with undeclared category: " + string.Join(", ", unresolved));
    }

    [Fact]
    public void ShippedCatalogue_DeclaresEveryExpectedCategory()
    {
        // [2026-09-09, renamed from
        // ShippedCatalogue_DeclaresAllNineExpectedCategories: there are ten
        // now. "onfoot" was added with the starter layout's ON FOOT page
        // (ref/docs/vessel-context.md's "The default context pages") -
        // Humanoid* elements had no curated home before that, and an
        // uncurated action shows Prettifier's fallback label, which no
        // button on this panel can fit. A name carrying a count is read by
        // more people than the assertion under it, so it goes rather than
        // becoming quietly false.]
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(ReadShippedCatalogueJson());

        string[] expected = { "ship", "flight", "target", "panels", "weapons", "power", "srv", "onfoot", "camera", "macros" };
        foreach (var id in expected)
        {
            Assert.True(catalogue.Categories.ContainsKey(id), $"Expected category '{id}' to be declared.");
        }
    }

    [Fact]
    public void ShippedCatalogue_EveryLitCondition_EvaluatesAgainstASnapshot_WithoutThrowing()
    {
        // Parse itself already routes every 'lit' condition list through
        // ConditionList.Parse (see Catalogue.Parse) - this test additionally
        // proves the parsed result is a real, evaluable ordered
        // LitCondition list, not merely something that didn't throw.
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(ReadShippedCatalogueJson());
        var snapshot = new StatusSnapshot(Flags: 0, Flags2: 0, GuiFocus: 0, GameRunning: true, SignedIn: true);

        var litActions = catalogue.Actions.Values.Where(a => a.Lit is not null).ToList();
        Assert.True(litActions.Count > 0, "Expected at least one curated action to carry a 'lit' condition.");

        foreach (var action in litActions)
        {
            // Must not throw - the value itself (which SlotLitLevel comes
            // back for an all-zero snapshot) isn't the point being pinned
            // here.
            LitCondition.Evaluate(action.Lit!, snapshot);
        }
    }

    // ---------------------------------------------------------------
    // Backward compatibility: every pre-existing bare-list 'lit' entry
    // (ref/docs/lit-state.md) must keep meaning exactly what it always has
    // - full when its condition list evaluates true, off when it doesn't -
    // driven from the shipped catalogue.json text itself (never a fixture
    // copy) so this cannot silently drift from what actually ships.
    // ---------------------------------------------------------------

    /// <summary>
    /// Classifies every action's raw 'lit' JSON (if any) as the bare
    /// string-array shape or the new levelled object-array shape, reading
    /// the shipped catalogue.json text directly rather than trusting
    /// anything about how Catalogue.Parse happened to normalize it - the
    /// classification and the thing being checked must not share a single
    /// point of failure.
    /// </summary>
    private static (IReadOnlyList<string> BareListActions, IReadOnlyList<string> LevelledActions) ClassifyShippedLitShapes()
    {
        using var document = System.Text.Json.JsonDocument.Parse(ReadShippedCatalogueJson());
        var bareList = new List<string>();
        var levelled = new List<string>();

        foreach (var actionProperty in document.RootElement.GetProperty("actions").EnumerateObject())
        {
            if (!actionProperty.Value.TryGetProperty("lit", out var litElement))
            {
                continue;
            }

            var elements = litElement.EnumerateArray().ToList();
            if (elements.Count > 0 && elements.All(e => e.ValueKind == System.Text.Json.JsonValueKind.String))
            {
                bareList.Add(actionProperty.Name);
            }
            else
            {
                levelled.Add(actionProperty.Name);
            }
        }

        return (bareList, levelled);
    }

    [Fact]
    public void ShippedCatalogue_ExactlySeventeenActions_StillUseTheBareListLitShape()
    {
        // Pins the counts named in ref/docs/lit-state.md: twenty curated
        // actions carried a bare "lit": ["Name", ...] list before this task;
        // only the three named there (HeadlightsBuggyButton,
        // PlayerHUDModeToggle, ToggleFlightAssist) were rewritten into the
        // new levelled shape, leaving seventeen still on the bare shape -
        // every one of those seventeen must be untouched.
        //
        // [2026-09-07, superseded: PlayerHUDModeToggle's lit entry was
        // removed entirely (ref/docs/lit-state.md's "The HUD Mode reversal")
        // - the commander judged it "a complete toggle" that would be lit no
        // matter its state, and asked for it treated like any other unlit
        // button. It now carries no "lit" property at all, so it appears in
        // neither list below - bareList stays at 17 (unaffected; it was
        // never on the bare shape), and levelled drops from three names to
        // two.]
        //
        // [2026-09-10, superseded: levelled was ["HeadlightsBuggyButton",
        // "ToggleFlightAssist"] and is now four names - Hyperspace and
        // Supercruise both needed the levelled shape, which is the only
        // shape in this grammar that can express an OR (ref/docs/lit-state.md's
        // "Two buttons that lit for the wrong thing"). The bare list also
        // gained FocusRadarPanel and FocusRadarPanel_Buggy, which had no
        // lit entry at all.
        //
        // The COUNT is 17 before and after, purely by coincidence: two
        // actions joined the bare list and two left it for the levelled
        // one. That is exactly why the bare names are now asserted
        // individually rather than only counted - a count that survives the
        // change it was meant to notice is pinning nothing.
        //
        // [2026-09-12, superseded: the SRV's own panel-access buggy
        // variants were curated (ref/docs/vessel-context.md's "The SRV
        // page, and why these eight"), and five of the eight mirror a
        // ship-side bare-list panel action's lit condition exactly, the
        // same way FocusRadarPanel_Buggy already did: FocusLeftPanel_Buggy,
        // FocusRightPanel_Buggy, GalaxyMapOpen_Buggy, SystemMapOpen_Buggy,
        // OpenCodexGoToDiscovery_Buggy. The other three (UIFocus_Buggy,
        // PlayerHUDModeToggle_Buggy, PhotoCameraToggle_Buggy) mirror a
        // ship-side action that carries no lit entry at all, so they carry
        // none either. Count rises from 17 to 22.]
        //
        // [2026-09-17, superseded twice in one day: bareList was 22 (as
        // listed below minus ToggleFlightAssist) and levelled was
        // ["HeadlightsBuggyButton", "Hyperspace", "Supercruise",
        // "ToggleFlightAssist"]. The first reversal that day moved
        // ToggleFlightAssist to the bare shape (bareList 23, levelled three
        // names) - see ShippedCatalogue_ToggleFlightAssist_OnIsPartial_OffIsOff's
        // own history. **Superseded again, same day**: the commander asked
        // for assist-ON to glow at Partial rather than Full, which the bare
        // shape cannot express (it is always Full-when-true) - only the
        // levelled shape can, so ToggleFlightAssist moves back from
        // bareList to levelled. bareList returns to 22; levelled returns to
        // four names.]
        var (bareList, levelled) = ClassifyShippedLitShapes();

        Assert.Equal(22, bareList.Count);
        Assert.Equal(
            new[]
            {
                "AutoBreakBuggyButton",
                "DeployHardpointToggle",
                "ExplorationFSSEnter",
                "FocusCommsPanel",
                "FocusLeftPanel",
                "FocusLeftPanel_Buggy",
                "FocusRadarPanel",
                "FocusRadarPanel_Buggy",
                "FocusRightPanel",
                "FocusRightPanel_Buggy",
                "GalaxyMapOpen",
                "GalaxyMapOpen_Buggy",
                "LandingGearToggle",
                "NightVisionToggle",
                "OpenCodexGoToDiscovery",
                "OpenCodexGoToDiscovery_Buggy",
                "ShipSpotLightToggle",
                "SystemMapOpen",
                "SystemMapOpen_Buggy",
                "ToggleBuggyTurretButton",
                "ToggleCargoScoop",
                "ToggleDriveAssist",
            },
            bareList.OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(
            new[] { "HeadlightsBuggyButton", "Hyperspace", "Supercruise", "ToggleFlightAssist" },
            levelled.OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>
    /// The most valuable test in this class (ref/docs/lit-state.md's own
    /// call-out): every bare-list entry, driven from the shipped
    /// catalogue.json itself, still normalizes to exactly one
    /// SlotLitLevel.Full LitCondition whose condition list is the
    /// unmodified original token list - "full when true, off when false"
    /// unchanged, for every one of the twenty, not just the ones this task
    /// happened to touch by hand.
    /// </summary>
    [Fact]
    public void ShippedCatalogue_EveryBareListLitEntry_NormalizesToASingleFullLevelCondition()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(ReadShippedCatalogueJson());
        var (bareList, _) = ClassifyShippedLitShapes();
        Assert.NotEmpty(bareList);

        foreach (var actionName in bareList)
        {
            var lit = catalogue.Actions[actionName].Lit;
            Assert.NotNull(lit);
            var entry = Assert.Single(lit!);
            Assert.Equal(SlotLitLevel.Full, entry.Level);
        }
    }

    /// <summary>
    /// The positive half of the classification above: every action the
    /// classifier calls levelled actually uses the multi-entry ordered
    /// shape (more than one LitCondition), not merely a single Full entry
    /// indistinguishable from the bare-list ones - confirms the
    /// classification itself is discriminating rather than every action
    /// landing in one bucket by coincidence.
    /// </summary>
    [Fact]
    public void ShippedCatalogue_EveryLevelledAction_UsesAMultiEntryLeveledLitList()
    {
        // [2026-09-07, superseded: was "the three fixed actions", including
        // PlayerHUDModeToggle - see ShippedCatalogue_PlayerHUDModeToggle_HasNoLitEntry_ItReadsAsAnOrdinaryUnlitButton
        // for its own reversal.]
        //
        // [2026-09-10, superseded: was named ..._TheTwoRemainingFixedActions_...
        // and iterated a hardcoded pair. Hyperspace and Supercruise became
        // levelled that day and this test stayed green through it without
        // looking at either - a sweep that only reports what it was told
        // about is indistinguishable from one that did not look. It now
        // sweeps whatever ClassifyShippedLitShapes finds, and carries no
        // count in its name.]
        //
        // [2026-09-17, narrowed: "levelled" was, until today, coincidentally
        // synonymous with "needs more than one condition entry" (an OR, or a
        // genuinely graded reading like HeadlightsBuggyButton's three
        // beams). ToggleFlightAssist breaks that coincidence on purpose - it
        // moved to the levelled (object) shape not because it has more than
        // one condition, but because a single condition needs to name
        // Partial rather than the bare shape's fixed Full. It is
        // deliberately exempt from the multi-entry check below; every other
        // levelled action still needs it, since those genuinely rely on more
        // than one condition to grade correctly.
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(ReadShippedCatalogueJson());
        var (_, levelled) = ClassifyShippedLitShapes();
        Assert.NotEmpty(levelled);

        foreach (var actionName in levelled.Where(n => n != "ToggleFlightAssist"))
        {
            var lit = catalogue.Actions[actionName].Lit;
            Assert.NotNull(lit);
            Assert.True(lit!.Count > 1, $"Expected '{actionName}' to carry more than one lit condition entry, found {lit.Count}.");
        }

        Assert.Single(catalogue.Actions["ToggleFlightAssist"].Lit!);
    }

    // ---------------------------------------------------------------
    // The three fixes named in ref/docs/lit-state.md, driven from the real
    // shipped catalogue.json against real flag bits (StatusVocabularyTests
    // pins the bit numbers themselves) - not just the local fixtures above,
    // which prove the parsing mechanism but never touch what actually
    // ships. "The ordinary state is partial, the notable state is full."
    // ---------------------------------------------------------------

    [Fact]
    public void ShippedCatalogue_PlayerHUDModeToggle_HasNoLitEntry_ItReadsAsAnOrdinaryUnlitButton()
    {
        // [2026-09-07, superseded: this test used to pin analysis-mode=Full,
        // combat-mode=Partial. The commander, having used the panel: "HUD
        // Mode should not be constantly lit. it's a complete toggle and no
        // matter what state it's in, it would be constantly lit if we
        // allowed it to be, so treat it like we do a normal 'non lit'
        // button." ref/docs/lit-state.md's "The HUD Mode reversal" records
        // the full reasoning, including why this reverses part of the same
        // day's multi-level lit work deliberately, and why
        // ToggleFlightAssist (a genuine caution) is untouched.
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(ReadShippedCatalogueJson());

        Assert.Null(catalogue.Actions["PlayerHUDModeToggle"].Lit);
    }

    [Fact]
    public void ShippedCatalogue_ToggleFlightAssist_OnIsPartial_OffIsOff()
    {
        // [2026-09-17, superseded again: this test used to pin assist-ON as
        // Full, assist-OFF as Off (the bare single-condition shape,
        // "lit": ["!FlightAssistOff"]), matching LandingGearToggle/
        // ShipSpotLightToggle/ToggleCargoScoop's "lit when actively engaged"
        // convention. The commander asked for one more adjustment on top of
        // that reversal: assist ON should glow at Partial, not Full - Full
        // was too strong for this button specifically - while assist OFF
        // stays completely unlit, unchanged from the prior reversal.
        // ToggleFlightAssist now uses the object shape
        // ("lit": [{ "when": ["!FlightAssistOff"], "level": "partial" }]),
        // the same shape HeadlightsBuggyButton/Supercruise/Hyperspace below
        // already use for a graded glow.]
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(ReadShippedCatalogueJson());
        var lit = catalogue.Actions["ToggleFlightAssist"].Lit!;

        var flightAssistOff = new StatusSnapshot(Flags: 1u << 5, Flags2: 0, GuiFocus: 0, GameRunning: true, SignedIn: true);
        var flightAssistOn = new StatusSnapshot(Flags: 0, Flags2: 0, GuiFocus: 0, GameRunning: true, SignedIn: true);

        Assert.Equal(SlotLitLevel.Off, LitCondition.Evaluate(lit, flightAssistOff));
        Assert.Equal(SlotLitLevel.Partial, LitCondition.Evaluate(lit, flightAssistOn));
    }

    [Fact]
    public void ShippedCatalogue_HeadlightsBuggyButton_HighBeamIsFull_LowBeamIsPartial_LightsOffIsOff()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(ReadShippedCatalogueJson());
        var lit = catalogue.Actions["HeadlightsBuggyButton"].Lit!;

        var highBeam = new StatusSnapshot(Flags: (1u << 8) | (1u << 31), Flags2: 0, GuiFocus: 0, GameRunning: true, SignedIn: true);
        var lowBeam = new StatusSnapshot(Flags: 1u << 8, Flags2: 0, GuiFocus: 0, GameRunning: true, SignedIn: true);
        var lightsOff = new StatusSnapshot(Flags: 0, Flags2: 0, GuiFocus: 0, GameRunning: true, SignedIn: true);

        Assert.Equal(SlotLitLevel.Full, LitCondition.Evaluate(lit, highBeam));
        Assert.Equal(SlotLitLevel.Partial, LitCondition.Evaluate(lit, lowBeam));
        Assert.Equal(SlotLitLevel.Off, LitCondition.Evaluate(lit, lightsOff));
    }

    // ---------------------------------------------------------------
    // The two lit conditions corrected 2026-09-10 after the commander ran
    // the panel against a live game - ref/docs/lit-state.md's "Two buttons
    // that lit for the wrong thing".
    //
    // Every snapshot below is built out of StatusVocabulary itself rather
    // than a re-typed bit number or GuiFocus value, so these tests read the
    // same table the shipped conditions are evaluated against. That is
    // deliberately NOT where a wrong bit number would be caught -
    // StatusVocabularyTests pins every bit and every GuiFocus value against
    // hand-written literals, and that is the only place those numbers are
    // asserted. What these tests pin is the ROUTING: which condition each
    // button carries, and therefore which button lights for which state.
    // ---------------------------------------------------------------

    private static uint FlagsBitFromVocabulary(string name)
    {
        Assert.True(StatusVocabulary.TryGetFlagCondition(name, out var condition), $"'{name}' is not a condition name StatusVocabulary knows.");
        Assert.Equal(FlagsField.Flags, condition.Field);
        return 1u << condition.Bit;
    }

    private static uint Flags2BitFromVocabulary(string name)
    {
        Assert.True(StatusVocabulary.TryGetFlagCondition(name, out var condition), $"'{name}' is not a condition name StatusVocabulary knows.");
        Assert.Equal(FlagsField.Flags2, condition.Field);
        return 1u << condition.Bit;
    }

    private static StatusSnapshot FocusedOn(string guiFocusName)
    {
        Assert.True(StatusVocabulary.TryGetGuiFocusValue(guiFocusName, out var value), $"'{guiFocusName}' is not a GuiFocus name StatusVocabulary knows.");
        return new StatusSnapshot(Flags: 0, Flags2: 0, GuiFocus: value, GameRunning: true, SignedIn: true);
    }

    private static StatusSnapshot Flying(uint flags = 0, uint flags2 = 0) =>
        new(Flags: flags, Flags2: flags2, GuiFocus: 0, GameRunning: true, SignedIn: true);

    private static StatusSnapshot ChargingForSupercruise() =>
        Flying(flags: FlagsBitFromVocabulary("FsdCharging"));

    private static StatusSnapshot ChargingForHyperspace() =>
        Flying(flags: FlagsBitFromVocabulary("FsdCharging"), flags2: Flags2BitFromVocabulary("FsdHyperdriveCharging"));

    // The same hyperspace charge, if it should turn out Frontier does NOT
    // also set the shared Flags bit 17 during one. Nothing available here
    // could measure which of the two it is (see ref/docs/lit-state.md), so
    // both shipped conditions are written to give the same answer either
    // way and both readings are pinned.
    private static StatusSnapshot ChargingForHyperspaceWithoutTheSharedChargingBit() =>
        Flying(flags2: Flags2BitFromVocabulary("FsdHyperdriveCharging"));

    private static StatusSnapshot InSupercruise() =>
        Flying(flags: FlagsBitFromVocabulary("Supercruise"));

    private static StatusSnapshot InTheHyperspaceJump() =>
        Flying(flags: FlagsBitFromVocabulary("FsdJump"));

    /// <summary>
    /// The commander, 2026-09-10: "Role panel didn't highlight when I had
    /// it open." <c>FocusRadarPanel</c> shipped with no <c>lit</c> entry at
    /// all, while each of its three sibling panel-focus buttons carried
    /// one - so the button could not light whatever was on screen.
    /// </summary>
    [Fact]
    public void ShippedCatalogue_FocusRadarPanel_LightsWhenTheRolePanelIsFocused_AndNotForAnotherPanel()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(ReadShippedCatalogueJson());
        var lit = catalogue.Actions["FocusRadarPanel"].Lit;
        Assert.NotNull(lit);

        Assert.Equal(SlotLitLevel.Full, LitCondition.Evaluate(lit!, FocusedOn("RolePanel")));
        Assert.Equal(SlotLitLevel.Off, LitCondition.Evaluate(lit!, FocusedOn("CommsPanel")));
        Assert.Equal(SlotLitLevel.Off, LitCondition.Evaluate(lit!, FocusedOn("ExternalPanel")));
        Assert.Equal(SlotLitLevel.Off, LitCondition.Evaluate(lit!, FocusedOn("InternalPanel")));
        Assert.Equal(SlotLitLevel.Off, LitCondition.Evaluate(lit!, FocusedOn("NoFocus")));
    }

    /// <summary>
    /// The routing pin. A per-button assertion can be perfectly true of the
    /// button it was written for while a sibling reads the same GuiFocus
    /// value, so all four panel-focus buttons are driven against all four
    /// panel GuiFocus values: sixteen cells, <c>Full</c> on the diagonal and
    /// <c>Off</c> everywhere else. Frontier's naming is inverted
    /// (<c>ExternalPanel</c> is the LEFT panel, <c>InternalPanel</c> the
    /// RIGHT one - see ref/docs/gamestate.md), which is exactly the kind of
    /// mistake only a full matrix catches.
    /// </summary>
    [Fact]
    public void ShippedCatalogue_EachPanelFocusButton_LightsOnlyForItsOwnGuiFocusValue()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(ReadShippedCatalogueJson());
        var pairs = new[]
        {
            ("FocusLeftPanel", "ExternalPanel"),
            ("FocusRightPanel", "InternalPanel"),
            ("FocusCommsPanel", "CommsPanel"),
            ("FocusRadarPanel", "RolePanel"),
        };

        foreach (var (action, ownFocus) in pairs)
        {
            var lit = catalogue.Actions[action].Lit;
            Assert.True(lit is not null, $"'{action}' carries no lit condition at all.");

            foreach (var (_, focus) in pairs)
            {
                var expected = focus == ownFocus ? SlotLitLevel.Full : SlotLitLevel.Off;
                var actual = LitCondition.Evaluate(lit!, FocusedOn(focus));
                Assert.True(expected == actual, $"'{action}' against GuiFocus:{focus} - expected {expected}, got {actual}.");
            }
        }
    }

    /// <summary>
    /// The SRV's own role-panel bind lights on the same focus value as the
    /// ship's. This one is an inference rather than an observation: the
    /// documented GuiFocus table has exactly one role-panel value and no
    /// vessel-specific ones, so <c>RolePanel</c> is the only documented
    /// value the SRV's role panel could report. If it turns out to report
    /// an undocumented value instead, this button simply never lights -
    /// exactly what it did before this change - and nothing else regresses.
    /// </summary>
    [Fact]
    public void ShippedCatalogue_FocusRadarPanelBuggy_LightsOnTheSameRolePanelFocus_AsTheShipsOwnButton()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(ReadShippedCatalogueJson());
        var lit = catalogue.Actions["FocusRadarPanel_Buggy"].Lit;
        Assert.NotNull(lit);

        Assert.Equal(SlotLitLevel.Full, LitCondition.Evaluate(lit!, FocusedOn("RolePanel")));
        Assert.Equal(SlotLitLevel.Off, LitCondition.Evaluate(lit!, FocusedOn("CommsPanel")));
        Assert.Equal(SlotLitLevel.Off, LitCondition.Evaluate(lit!, FocusedOn("ExternalPanel")));
        Assert.Equal(SlotLitLevel.Off, LitCondition.Evaluate(lit!, FocusedOn("NoFocus")));
    }

    /// <summary>
    /// The SRV's four other panel-focus/map buggy variants
    /// (ref/docs/vessel-context.md's "The SRV page, and why these eight"),
    /// each mirroring its ship-side sibling's own lit condition on the same
    /// reasoning as <see cref="ShippedCatalogue_FocusRadarPanelBuggy_LightsOnTheSameRolePanelFocus_AsTheShipsOwnButton"/>
    /// above: the documented GuiFocus table has no vessel-specific values,
    /// so this is the only documented value each SRV button's own focus
    /// could report. If Frontier reports something undocumented instead,
    /// the button simply never lights - no regression either way.
    /// </summary>
    [Theory]
    [InlineData("FocusLeftPanel_Buggy", "ExternalPanel")]
    [InlineData("FocusRightPanel_Buggy", "InternalPanel")]
    [InlineData("GalaxyMapOpen_Buggy", "GalaxyMap")]
    [InlineData("SystemMapOpen_Buggy", "SystemMap")]
    [InlineData("OpenCodexGoToDiscovery_Buggy", "Codex")]
    public void ShippedCatalogue_SrvPanelFocusBuggyVariant_LightsOnTheSameFocusValue_AsTheShipsOwnButton(string actionName, string ownFocus)
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(ReadShippedCatalogueJson());
        var lit = catalogue.Actions[actionName].Lit;
        Assert.NotNull(lit);

        Assert.Equal(SlotLitLevel.Full, LitCondition.Evaluate(lit!, FocusedOn(ownFocus)));
        Assert.Equal(SlotLitLevel.Off, LitCondition.Evaluate(lit!, FocusedOn("NoFocus")));
    }

    /// <summary>
    /// The other three curated SRV panel-access buggy variants mirror a
    /// ship-side action that carries no lit entry at all
    /// (<c>UIFocus</c>, <c>PlayerHUDModeToggle</c>, <c>PhotoCameraToggle</c>)
    /// - <c>PlayerHUDModeToggle</c>'s own reversal
    /// (<see cref="ShippedCatalogue_PlayerHUDModeToggle_HasNoLitEntry_ItReadsAsAnOrdinaryUnlitButton"/>)
    /// applies just as much to its SRV variant, so none of the three should
    /// carry one either.
    /// </summary>
    [Theory]
    [InlineData("UIFocus_Buggy")]
    [InlineData("PlayerHUDModeToggle_Buggy")]
    [InlineData("PhotoCameraToggle_Buggy")]
    public void ShippedCatalogue_SrvPanelAccessBuggyVariant_WithNoShipSideLitEntry_CarriesNoneEither(string actionName)
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(ReadShippedCatalogueJson());
        Assert.Null(catalogue.Actions[actionName].Lit);
    }

    /// <summary>
    /// The commander, 2026-09-10: "Frameshift drive charging for the
    /// supercruise first highlighted 'Hyperspace' and then switched to
    /// supercruise once in supercruise. It should highlight super cruise
    /// all the time for supercruise."
    ///
    /// Both buttons were reading state that cannot tell the two charges
    /// apart - Hyperspace on <c>FsdCharging</c> (set for either kind) and
    /// Supercruise on <c>Supercruise</c> (set only once the charge has
    /// finished), which left the whole charge showing the wrong button.
    /// <c>FsdHyperdriveCharging</c> (Flags2 bit 19) is what separates them.
    /// </summary>
    [Fact]
    public void ShippedCatalogue_Supercruise_IsLitForTheWholeSupercruiseOperation_AndNeverForAHyperspaceCharge()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(ReadShippedCatalogueJson());
        var lit = catalogue.Actions["Supercruise"].Lit;
        Assert.NotNull(lit);

        Assert.Equal(SlotLitLevel.Partial, LitCondition.Evaluate(lit!, ChargingForSupercruise()));
        Assert.Equal(SlotLitLevel.Full, LitCondition.Evaluate(lit!, InSupercruise()));
        Assert.Equal(SlotLitLevel.Off, LitCondition.Evaluate(lit!, ChargingForHyperspace()));
        Assert.Equal(SlotLitLevel.Off, LitCondition.Evaluate(lit!, ChargingForHyperspaceWithoutTheSharedChargingBit()));
        Assert.Equal(SlotLitLevel.Off, LitCondition.Evaluate(lit!, Flying()));
    }

    /// <summary>
    /// The other half of the same report: Hyperspace must go dark for a
    /// supercruise charge, which is the state it was wrongly lighting for.
    /// </summary>
    [Fact]
    public void ShippedCatalogue_Hyperspace_LightsOnlyForAHyperspaceCharge_NeverForASupercruiseOne()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(ReadShippedCatalogueJson());
        var lit = catalogue.Actions["Hyperspace"].Lit;
        Assert.NotNull(lit);

        Assert.Equal(SlotLitLevel.Off, LitCondition.Evaluate(lit!, ChargingForSupercruise()));
        Assert.Equal(SlotLitLevel.Partial, LitCondition.Evaluate(lit!, ChargingForHyperspace()));
        Assert.Equal(SlotLitLevel.Partial, LitCondition.Evaluate(lit!, ChargingForHyperspaceWithoutTheSharedChargingBit()));
        Assert.Equal(SlotLitLevel.Full, LitCondition.Evaluate(lit!, InTheHyperspaceJump()));
        Assert.Equal(SlotLitLevel.Off, LitCondition.Evaluate(lit!, Flying()));
    }

    /// <summary>
    /// The pair read together, which is the thing the commander actually
    /// saw: at no moment of either operation are both buttons lit, and at
    /// no moment is the wrong one of the two the lit one.
    /// </summary>
    [Fact]
    public void ShippedCatalogue_SupercruiseAndHyperspace_AreNeverBothLitAtOnce()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(ReadShippedCatalogueJson());
        var supercruise = catalogue.Actions["Supercruise"].Lit;
        var hyperspace = catalogue.Actions["Hyperspace"].Lit;
        Assert.NotNull(supercruise);
        Assert.NotNull(hyperspace);

        var states = new (string Name, StatusSnapshot Snapshot)[]
        {
            ("charging for supercruise", ChargingForSupercruise()),
            ("in supercruise", InSupercruise()),
            ("charging for hyperspace", ChargingForHyperspace()),
            ("charging for hyperspace, shared bit unset", ChargingForHyperspaceWithoutTheSharedChargingBit()),
            ("in the hyperspace jump", InTheHyperspaceJump()),
            ("ordinary flight", Flying()),
        };

        foreach (var (name, snapshot) in states)
        {
            var supercruiseLevel = LitCondition.Evaluate(supercruise!, snapshot);
            var hyperspaceLevel = LitCondition.Evaluate(hyperspace!, snapshot);
            Assert.True(
                supercruiseLevel == SlotLitLevel.Off || hyperspaceLevel == SlotLitLevel.Off,
                $"Both buttons are lit while {name}: Supercruise {supercruiseLevel}, Hyperspace {hyperspaceLevel}.");
        }
    }

    // ---------------------------------------------------------------
    // Catalogue.Parse: malformed JSON fails loudly.
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_NotJson_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => LunaPanel.Core.Catalogue.Catalogue.Parse("this is not json"));
    }

    [Fact]
    public void Parse_MissingCategoriesArray_ThrowsFormatException()
    {
        const string json = """
            {
              "catalogueVersion": 1,
              "actions": {}
            }
            """;

        Assert.Throws<FormatException>(() => LunaPanel.Core.Catalogue.Catalogue.Parse(json));
    }

    [Fact]
    public void Parse_MissingActionsObject_ThrowsFormatException()
    {
        const string json = """
            {
              "catalogueVersion": 1,
              "categories": [ { "id": "ship", "label": "SHIP" } ]
            }
            """;

        Assert.Throws<FormatException>(() => LunaPanel.Core.Catalogue.Catalogue.Parse(json));
    }

    [Fact]
    public void Parse_ActionNamesUndeclaredCategory_ThrowsFormatException()
    {
        const string json = """
            {
              "catalogueVersion": 1,
              "categories": [ { "id": "ship", "label": "SHIP" } ],
              "actions": {
                "SomeAction": { "label": "SOME", "category": "doesNotExist" }
              }
            }
            """;

        Assert.Throws<FormatException>(() => LunaPanel.Core.Catalogue.Catalogue.Parse(json));
    }

    [Fact]
    public void Parse_LitNamesUnknownCondition_ThrowsFormatException()
    {
        const string json = """
            {
              "catalogueVersion": 1,
              "categories": [ { "id": "ship", "label": "SHIP" } ],
              "actions": {
                "SomeAction": { "label": "SOME", "category": "ship", "lit": ["ThisFlagDoesNotExist"] }
              }
            }
            """;

        Assert.Throws<FormatException>(() => LunaPanel.Core.Catalogue.Catalogue.Parse(json));
    }

    [Fact]
    public void Parse_DuplicateActionName_ThrowsFormatException()
    {
        const string json = """
            {
              "catalogueVersion": 1,
              "categories": [ { "id": "ship", "label": "SHIP" } ],
              "actions": {
                "SomeAction": { "label": "ONE", "category": "ship" },
                "SomeAction": { "label": "TWO", "category": "ship" }
              }
            }
            """;

        Assert.Throws<FormatException>(() => LunaPanel.Core.Catalogue.Catalogue.Parse(json));
    }

    [Fact]
    public void Parse_ValidMinimalJson_Succeeds()
    {
        const string json = """
            {
              "catalogueVersion": 1,
              "categories": [ { "id": "ship", "label": "SHIP" } ],
              "actions": {
                "LandingGearToggle": { "label": "GEAR", "category": "ship", "lit": ["LandingGearDown"] }
              }
            }
            """;

        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(json);

        Assert.Equal(1, catalogue.Version);
        Assert.Single(catalogue.Categories);
        Assert.Single(catalogue.Actions);
        Assert.Equal("GEAR", catalogue.Actions["LandingGearToggle"].Label);
        Assert.NotNull(catalogue.Actions["LandingGearToggle"].Lit);
    }

    [Fact]
    public void Parse_ActionWithLitCondition_ParsesIntoAConditionListThatEvaluatesCorrectly()
    {
        const string json = """
            {
              "catalogueVersion": 1,
              "categories": [ { "id": "ship", "label": "SHIP" } ],
              "actions": {
                "LandingGearToggle": { "label": "GEAR", "category": "ship", "lit": ["LandingGearDown"] }
              }
            }
            """;

        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(json);
        var lit = catalogue.Actions["LandingGearToggle"].Lit!;

        var gearDown = new StatusSnapshot(Flags: 1u << 2, Flags2: 0, GuiFocus: 0, GameRunning: true, SignedIn: true);
        var gearUp = new StatusSnapshot(Flags: 0, Flags2: 0, GuiFocus: 0, GameRunning: true, SignedIn: true);

        Assert.Equal(SlotLitLevel.Full, LitCondition.Evaluate(lit, gearDown));
        Assert.Equal(SlotLitLevel.Off, LitCondition.Evaluate(lit, gearUp));
    }

    // ---------------------------------------------------------------
    // Catalogue.Parse: the new levelled 'lit' shape
    // ({ "when": [...], "level": "..." }) - ref/docs/lit-state.md.
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_LevelledLit_FirstMatchingEntryWins_InDeclarationOrder()
    {
        const string json = """
            {
              "catalogueVersion": 1,
              "categories": [ { "id": "srv", "label": "SRV" } ],
              "actions": {
                "HeadlightsBuggyButton": {
                  "label": "SRV Lights", "category": "srv",
                  "lit": [
                    { "when": ["LightsOn", "SrvHighBeam"], "level": "full" },
                    { "when": ["LightsOn"], "level": "partial" }
                  ]
                }
              }
            }
            """;

        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(json);
        var lit = catalogue.Actions["HeadlightsBuggyButton"].Lit!;

        var lightsOnHighBeam = new StatusSnapshot(Flags: (1u << 8) | (1u << 31), Flags2: 0, GuiFocus: 0, GameRunning: true, SignedIn: true);
        var lightsOnLowBeam = new StatusSnapshot(Flags: 1u << 8, Flags2: 0, GuiFocus: 0, GameRunning: true, SignedIn: true);
        var lightsOff = new StatusSnapshot(Flags: 0, Flags2: 0, GuiFocus: 0, GameRunning: true, SignedIn: true);

        Assert.Equal(SlotLitLevel.Full, LitCondition.Evaluate(lit, lightsOnHighBeam));
        Assert.Equal(SlotLitLevel.Partial, LitCondition.Evaluate(lit, lightsOnLowBeam));
        Assert.Equal(SlotLitLevel.Off, LitCondition.Evaluate(lit, lightsOff));
    }

    [Fact]
    public void Parse_LevelledLit_UnrecognizedLevelValue_ThrowsFormatException()
    {
        const string json = """
            {
              "catalogueVersion": 1,
              "categories": [ { "id": "ship", "label": "SHIP" } ],
              "actions": {
                "SomeAction": {
                  "label": "SOME", "category": "ship",
                  "lit": [ { "when": ["LightsOn"], "level": "off" } ]
                }
              }
            }
            """;

        Assert.Throws<FormatException>(() => LunaPanel.Core.Catalogue.Catalogue.Parse(json));
    }

    [Fact]
    public void Parse_LevelledLit_MissingWhenArray_ThrowsFormatException()
    {
        const string json = """
            {
              "catalogueVersion": 1,
              "categories": [ { "id": "ship", "label": "SHIP" } ],
              "actions": {
                "SomeAction": {
                  "label": "SOME", "category": "ship",
                  "lit": [ { "level": "full" } ]
                }
              }
            }
            """;

        Assert.Throws<FormatException>(() => LunaPanel.Core.Catalogue.Catalogue.Parse(json));
    }

    [Fact]
    public void Parse_LevelledLit_UnknownConditionInWhen_ThrowsFormatException()
    {
        const string json = """
            {
              "catalogueVersion": 1,
              "categories": [ { "id": "ship", "label": "SHIP" } ],
              "actions": {
                "SomeAction": {
                  "label": "SOME", "category": "ship",
                  "lit": [ { "when": ["ThisFlagDoesNotExist"], "level": "full" } ]
                }
              }
            }
            """;

        Assert.Throws<FormatException>(() => LunaPanel.Core.Catalogue.Catalogue.Parse(json));
    }

    [Fact]
    public void Parse_LitEntryMixesStringAndObject_ThrowsFormatException()
    {
        const string json = """
            {
              "catalogueVersion": 1,
              "categories": [ { "id": "ship", "label": "SHIP" } ],
              "actions": {
                "SomeAction": {
                  "label": "SOME", "category": "ship",
                  "lit": [ "LightsOn", { "when": ["LightsOn"], "level": "full" } ]
                }
              }
            }
            """;

        Assert.Throws<FormatException>(() => LunaPanel.Core.Catalogue.Catalogue.Parse(json));
    }
}
