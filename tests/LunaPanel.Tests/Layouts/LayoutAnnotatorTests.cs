using LunaPanel.Core.Bindings;
using LunaPanel.Core.Catalogue;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Layouts;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// Pins <see cref="LayoutAnnotator.Annotate"/>: every one of the five
/// <see cref="SlotStatus"/> values is produced with a sensible reason, and -
/// the guard that protects the whole "layout stores intent, never
/// resolution" design (see <c>ref/docs/design-decisions.md</c>) - the exact
/// same <see cref="Layout"/> annotates as <see cref="SlotStatus.Unbound"/>
/// against one bindings file and <see cref="SlotStatus.Ok"/> against
/// another, with no change to the layout itself.
/// </summary>
public class LayoutAnnotatorTests
{
    private const string CatalogueJson = """
        {
          "catalogueVersion": 1,
          "categories": [ { "id": "ship", "label": "SHIP" } ],
          "actions": {
            "ToggleCargoScoop": { "label": "SCOOP", "category": "ship" },
            "LandingGearToggle": { "label": "GEAR", "category": "ship" }
          }
        }
        """;

    private const string BoundBindingsXml = """
        <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
            <ToggleCargoScoop>
                <Primary Device="Keyboard" Key="Key_A" />
                <Secondary Device="{NoDevice}" Key="" />
            </ToggleCargoScoop>
            <LandingGearToggle>
                <Primary Device="Keyboard" Key="Key_G" />
                <Secondary Device="{NoDevice}" Key="" />
            </LandingGearToggle>
        </Root>
        """;

    private const string UnboundBindingsXml = """
        <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
            <LandingGearToggle>
                <Primary Device="{NoDevice}" Key="" />
                <Secondary Device="{NoDevice}" Key="" />
            </LandingGearToggle>
        </Root>
        """;

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "layouts", name);
    private static Layout LoadFixture(string name)
    {
        var result = LayoutJson.Parse(File.ReadAllText(FixturePath(name)));
        Assert.True(result.Success, result.Error);
        return result.Layout!;
    }

    private static IReadOnlyList<CataloguePickerEntry> MergeAgainst(string bindingsXml)
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(CatalogueJson);
        var bindingsResult = BindingsFile.Parse(bindingsXml);
        Assert.True(bindingsResult.Success, bindingsResult.Error);
        return CatalogueMerger.Merge(catalogue, bindingsResult.File!);
    }

    /// <summary>
    /// The second input the annotator needs to tell "genuinely nonexistent
    /// action" apart from "real, uncurated action that's currently unbound" -
    /// every element name <see cref="BindingsFile.Parse"/> found in
    /// <paramref name="bindingsXml"/>, bound or not.
    /// </summary>
    private static IReadOnlySet<string> KnownActionNamesFrom(string bindingsXml)
    {
        var bindingsResult = BindingsFile.Parse(bindingsXml);
        Assert.True(bindingsResult.Success, bindingsResult.Error);
        return bindingsResult.File!.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
    }

    // Names an action that is real - it has an element in this bindings file -
    // but that element resolves to nothing usable ({NoDevice} on both slots),
    // and is not curated in this test's small catalogue. CatalogueMerger
    // therefore omits it from the merge entirely (uncurated + unbound), which
    // is exactly the ambiguous case LayoutAnnotator must resolve using
    // knownActionNames rather than collapsing to UnknownAction.
    private const string UncuratedRealButUnboundBindingsXml = """
        <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
            <IncreaseEnginesPower>
                <Primary Device="{NoDevice}" Key="" />
                <Secondary Device="{NoDevice}" Key="" />
            </IncreaseEnginesPower>
        </Root>
        """;

    // Same as BoundBindingsXml (ToggleCargoScoop bound, curated) plus an
    // uncurated real-but-unbound element, so a slot's primary and long-press
    // can be checked independently in the same annotate call.
    private const string BoundCargoScoopPlusUncuratedUnboundBindingsXml = """
        <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
            <ToggleCargoScoop>
                <Primary Device="Keyboard" Key="Key_A" />
                <Secondary Device="{NoDevice}" Key="" />
            </ToggleCargoScoop>
            <IncreaseEnginesPower>
                <Primary Device="{NoDevice}" Key="" />
                <Secondary Device="{NoDevice}" Key="" />
            </IncreaseEnginesPower>
        </Root>
        """;

    [Fact]
    public void Annotate_BoundCuratedAction_IsOk_WithReasonNamingTheChord()
    {
        var layout = LoadFixture("degraded-unbound.json"); // names ToggleCargoScoop
        var merge = MergeAgainst(BoundBindingsXml);

        var annotations = LayoutAnnotator.Annotate(layout, merge, MacroKnowledge.Empty, KnownActionNamesFrom(BoundBindingsXml));
        var annotation = Assert.Single(annotations);

        Assert.Equal(SlotStatus.Ok, annotation.Slot.Status);
        Assert.Equal("Bound (A).", annotation.Slot.Reason);
    }

    [Fact]
    public void Annotate_UnboundCuratedAction_IsUnbound_WithSensibleReason()
    {
        var layout = LoadFixture("degraded-unbound.json"); // names ToggleCargoScoop, absent from UnboundBindingsXml
        var merge = MergeAgainst(UnboundBindingsXml);

        var annotations = LayoutAnnotator.Annotate(layout, merge, MacroKnowledge.Empty, KnownActionNamesFrom(UnboundBindingsXml));
        var annotation = Assert.Single(annotations);

        Assert.Equal(SlotStatus.Unbound, annotation.Slot.Status);
        Assert.False(string.IsNullOrWhiteSpace(annotation.Slot.Reason));

        // This string is what the commander actually reads: the client shows
        // slot.reason as a toast when an unbound button is tapped. "Not empty"
        // was the only assertion here, which would have passed for anything at
        // all - the wording was changed on 2026-09-07 from a bare statement of
        // fact to actionable guidance and no test noticed.
        //
        // Both halves are pinned deliberately. Saying what to do is the point;
        // saying the button then starts working is the reassurance, and it is
        // true by construction - a layout stores the element name, never a
        // resolved key, and bindings are re-read on every panel request, so
        // binding the action in Elite heals the slot with no edit or re-pair.
        Assert.Contains("bind it in the game's controls", annotation.Slot.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("start working", annotation.Slot.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Annotate_UnknownAction_IsUnknownAction_WithSensibleReason()
    {
        var layout = LoadFixture("degraded-unknown-action.json");
        var merge = MergeAgainst(BoundBindingsXml);

        var annotations = LayoutAnnotator.Annotate(layout, merge, MacroKnowledge.Empty, KnownActionNamesFrom(BoundBindingsXml));
        var annotation = Assert.Single(annotations);

        Assert.Equal(SlotStatus.UnknownAction, annotation.Slot.Status);
        Assert.Contains("TotallyFictionalAction", annotation.Slot.Reason);
        Assert.Contains("bindings file", annotation.Slot.Reason); // absent from knownActionNames entirely - distinct from the merely-unbound case
    }

    [Fact]
    public void Annotate_KnownMacro_IsOk()
    {
        var layout = LoadFixture("degraded-macro-degraded.json"); // names "request-docking"
        var merge = MergeAgainst(BoundBindingsXml);
        var macros = new MacroKnowledge(new HashSet<string> { "request-docking" }, new HashSet<string>());

        var annotations = LayoutAnnotator.Annotate(layout, merge, macros, KnownActionNamesFrom(BoundBindingsXml));
        var annotation = Assert.Single(annotations);

        Assert.Equal(SlotStatus.Ok, annotation.Slot.Status);
    }

    [Fact]
    public void Annotate_DegradedMacro_IsMacroDegraded_WithSensibleReason()
    {
        var layout = LoadFixture("degraded-macro-degraded.json"); // names "request-docking"
        var merge = MergeAgainst(BoundBindingsXml);
        var macros = new MacroKnowledge(new HashSet<string> { "request-docking" }, new HashSet<string> { "request-docking" });

        var annotations = LayoutAnnotator.Annotate(layout, merge, macros, KnownActionNamesFrom(BoundBindingsXml));
        var annotation = Assert.Single(annotations);

        Assert.Equal(SlotStatus.MacroDegraded, annotation.Slot.Status);
        Assert.Contains("request-docking", annotation.Slot.Reason);
    }

    [Fact]
    public void Annotate_UnknownMacro_IsUnknownMacro_WithSensibleReason()
    {
        var layout = LoadFixture("degraded-unknown-macro.json"); // names "totally-made-up-macro"
        var merge = MergeAgainst(BoundBindingsXml);

        var annotations = LayoutAnnotator.Annotate(layout, merge, MacroKnowledge.Empty, KnownActionNamesFrom(BoundBindingsXml));
        var annotation = Assert.Single(annotations);

        Assert.Equal(SlotStatus.UnknownMacro, annotation.Slot.Status);
        Assert.Contains("totally-made-up-macro", annotation.Slot.Reason);
    }

    [Fact]
    public void Annotate_LongPress_AnnotatedIndependentlyFromThePrimarySlot()
    {
        // action=IncreaseEnginesPower, longPress macro=pips-engines (unknown).
        // BoundBindingsXml has no element for IncreaseEnginesPower at all -
        // not curated in this test's small catalogue AND absent from
        // knownActionNames - so this is genuinely the "doesn't exist"
        // bucket, not the "real but unbound" one; see the two dedicated
        // tests below for that distinction.
        var layout = LoadFixture("valid-with-long-press.json");
        var merge = MergeAgainst(BoundBindingsXml);
        var macros = MacroKnowledge.Empty; // pips-engines is not known

        var annotations = LayoutAnnotator.Annotate(layout, merge, macros, KnownActionNamesFrom(BoundBindingsXml));
        var slotAnnotation = annotations.Single(a => a.Slot.Index == 3).Slot;

        Assert.Equal(SlotStatus.UnknownAction, slotAnnotation.Status); // IncreaseEnginesPower is absent from this test's bindings file entirely
        Assert.NotNull(slotAnnotation.LongPress);
        Assert.Equal(SlotStatus.UnknownMacro, slotAnnotation.LongPress!.Status);
    }

    [Fact]
    public void Annotate_RealButCurrentlyUnboundAction_IsUnbound_ReasonPointsAtBindingItInElite()
    {
        // IncreaseEnginesPower has an element in this bindings file
        // ({NoDevice} on both slots - unbound) but isn't curated, so
        // CatalogueMerger omits it from the merge entirely. Before this fix
        // that omission was indistinguishable from "doesn't exist"; now
        // knownActionNames (from BindingsFile.Parse's own element list)
        // resolves it correctly to Unbound.
        var layout = new Layout(1, new List<LayoutPage>
        {
            new("SHIP", "t6", new List<LayoutSlot> { new(0, "IncreaseEnginesPower", null, null, null) }, new List<LayoutSlot>())
        });
        var merge = MergeAgainst(UncuratedRealButUnboundBindingsXml);
        var knownActionNames = KnownActionNamesFrom(UncuratedRealButUnboundBindingsXml);

        var annotations = LayoutAnnotator.Annotate(layout, merge, MacroKnowledge.Empty, knownActionNames);
        var annotation = Assert.Single(annotations);

        Assert.Equal(SlotStatus.Unbound, annotation.Slot.Status);
        Assert.Contains("IncreaseEnginesPower", annotation.Slot.Reason);
        Assert.Contains("bind it", annotation.Slot.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Annotate_ActionAbsentFromBindingsEntirely_IsUnknownAction_ReasonSaysNotPresentInBindingsFile()
    {
        var layout = new Layout(1, new List<LayoutPage>
        {
            new("SHIP", "t6", new List<LayoutSlot> { new(0, "TotallyFictionalAction", null, null, null) }, new List<LayoutSlot>())
        });
        var merge = MergeAgainst(BoundBindingsXml);
        var knownActionNames = KnownActionNamesFrom(BoundBindingsXml); // ToggleCargoScoop, LandingGearToggle only

        var annotations = LayoutAnnotator.Annotate(layout, merge, MacroKnowledge.Empty, knownActionNames);
        var annotation = Assert.Single(annotations);

        Assert.Equal(SlotStatus.UnknownAction, annotation.Slot.Status);
        Assert.Contains("TotallyFictionalAction", annotation.Slot.Reason);
        Assert.Contains("bindings file", annotation.Slot.Reason);
    }

    [Fact]
    public void Annotate_LongPress_RealButCurrentlyUnboundAction_IsUnbound_IndependentlyOfABoundPrimary()
    {
        var layout = new Layout(1, new List<LayoutPage>
        {
            new("SHIP", "t6", new List<LayoutSlot>
            {
                new(0, "ToggleCargoScoop", null, null, new LongPressAction("IncreaseEnginesPower", null))
            }, new List<LayoutSlot>())
        });
        var merge = MergeAgainst(BoundCargoScoopPlusUncuratedUnboundBindingsXml);
        var knownActionNames = KnownActionNamesFrom(BoundCargoScoopPlusUncuratedUnboundBindingsXml);

        var annotations = LayoutAnnotator.Annotate(layout, merge, MacroKnowledge.Empty, knownActionNames);
        var slot = Assert.Single(annotations).Slot;

        Assert.Equal(SlotStatus.Ok, slot.Status); // the primary, ToggleCargoScoop, is bound
        Assert.NotNull(slot.LongPress);
        Assert.Equal(SlotStatus.Unbound, slot.LongPress!.Status);
        Assert.Contains("IncreaseEnginesPower", slot.LongPress!.Reason);
        Assert.Contains("bind it", slot.LongPress!.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Annotate_LongPress_ActionAbsentFromBindingsEntirely_IsUnknownAction_IndependentlyOfABoundPrimary()
    {
        var layout = new Layout(1, new List<LayoutPage>
        {
            new("SHIP", "t6", new List<LayoutSlot>
            {
                new(0, "ToggleCargoScoop", null, null, new LongPressAction("TotallyFictionalAction", null))
            }, new List<LayoutSlot>())
        });
        var merge = MergeAgainst(BoundBindingsXml);
        var knownActionNames = KnownActionNamesFrom(BoundBindingsXml);

        var annotations = LayoutAnnotator.Annotate(layout, merge, MacroKnowledge.Empty, knownActionNames);
        var slot = Assert.Single(annotations).Slot;

        Assert.Equal(SlotStatus.Ok, slot.Status);
        Assert.NotNull(slot.LongPress);
        Assert.Equal(SlotStatus.UnknownAction, slot.LongPress!.Status);
        Assert.Contains("TotallyFictionalAction", slot.LongPress!.Reason);
        Assert.Contains("bindings file", slot.LongPress!.Reason);
    }

    [Fact]
    public void Annotate_SameLayoutObject_UnboundAgainstOneBindingsFile_ThenOkAgainstAnother_WithNoChangeToTheLayout()
    {
        // The guard for the whole design: a layout stores intent, never
        // resolution, so the exact same in-memory Layout (loaded once from
        // one file) must annotate differently purely because the player's
        // bindings changed - never because anything about the layout did.
        var layout = LoadFixture("degraded-unbound.json");

        var unboundAnnotations = LayoutAnnotator.Annotate(layout, MergeAgainst(UnboundBindingsXml), MacroKnowledge.Empty, KnownActionNamesFrom(UnboundBindingsXml));
        var okAnnotations = LayoutAnnotator.Annotate(layout, MergeAgainst(BoundBindingsXml), MacroKnowledge.Empty, KnownActionNamesFrom(BoundBindingsXml));

        Assert.Equal(SlotStatus.Unbound, Assert.Single(unboundAnnotations).Slot.Status);
        Assert.Equal(SlotStatus.Ok, Assert.Single(okAnnotations).Slot.Status);

        // The layout object itself was never touched between the two calls.
        Assert.Single(layout.Pages);
        Assert.Single(layout.Pages[0].Slots);
        Assert.Equal("ToggleCargoScoop", layout.Pages[0].Slots[0].Action);
    }

    // ---------------------------------------------------------------
    // SlotAnnotation.Label / LongPressAnnotation.Label - slot.Label ??
    // catalogue DisplayLabel ?? Prettify(elementName) (or Prettify(macroId)
    // for a macro slot). See ref/docs/button-naming.md.
    // ---------------------------------------------------------------

    [Fact]
    public void Annotate_BoundCuratedAction_Label_IsCuratedDisplayLabel()
    {
        var layout = LoadFixture("degraded-unbound.json"); // names ToggleCargoScoop, no label override
        var merge = MergeAgainst(BoundBindingsXml);

        var annotations = LayoutAnnotator.Annotate(layout, merge, MacroKnowledge.Empty, KnownActionNamesFrom(BoundBindingsXml));
        var annotation = Assert.Single(annotations);

        Assert.Equal("SCOOP", annotation.Slot.Label); // this test's small CatalogueJson curates ToggleCargoScoop as "SCOOP"
    }

    [Fact]
    public void Annotate_SlotWithOverrideLabel_UsesOverride_NotCatalogueDisplayLabel()
    {
        var layout = new Layout(1, new List<LayoutPage>
        {
            new("SHIP", "t6", new List<LayoutSlot> { new(0, "ToggleCargoScoop", null, "MY OWN LABEL", null) }, new List<LayoutSlot>())
        });
        var merge = MergeAgainst(BoundBindingsXml);

        var annotations = LayoutAnnotator.Annotate(layout, merge, MacroKnowledge.Empty, KnownActionNamesFrom(BoundBindingsXml));
        var annotation = Assert.Single(annotations);

        Assert.Equal("MY OWN LABEL", annotation.Slot.Label);
    }

    [Fact]
    public void Annotate_UnknownAction_Label_IsPrettifiedActionName()
    {
        var layout = LoadFixture("degraded-unknown-action.json"); // names "TotallyFictionalAction"
        var merge = MergeAgainst(BoundBindingsXml);

        var annotations = LayoutAnnotator.Annotate(layout, merge, MacroKnowledge.Empty, KnownActionNamesFrom(BoundBindingsXml));
        var annotation = Assert.Single(annotations);

        Assert.Equal("Totally Fictional Action", annotation.Slot.Label);
    }

    [Fact]
    public void Annotate_RealButCurrentlyUnboundAction_Label_IsPrettifiedActionName_NoCatalogueEntryToConsult()
    {
        // IncreaseEnginesPower is uncurated in this test's small catalogue
        // and unbound, so CatalogueMerger omits it from the merge entirely -
        // there is no CataloguePickerEntry.DisplayLabel to consult here, only
        // Prettify(action).
        var layout = new Layout(1, new List<LayoutPage>
        {
            new("SHIP", "t6", new List<LayoutSlot> { new(0, "IncreaseEnginesPower", null, null, null) }, new List<LayoutSlot>())
        });
        var merge = MergeAgainst(UncuratedRealButUnboundBindingsXml);
        var knownActionNames = KnownActionNamesFrom(UncuratedRealButUnboundBindingsXml);

        var annotations = LayoutAnnotator.Annotate(layout, merge, MacroKnowledge.Empty, knownActionNames);
        var annotation = Assert.Single(annotations);

        Assert.Equal("Increase Engines Power", annotation.Slot.Label);
    }

    [Fact]
    public void Annotate_MacroSlot_Label_IsPrettifiedMacroId()
    {
        var layout = LoadFixture("degraded-macro-degraded.json"); // names "request-docking"
        var merge = MergeAgainst(BoundBindingsXml);
        var macros = new MacroKnowledge(new HashSet<string> { "request-docking" }, new HashSet<string>());

        var annotations = LayoutAnnotator.Annotate(layout, merge, macros, KnownActionNamesFrom(BoundBindingsXml));
        var annotation = Assert.Single(annotations);

        Assert.Equal("Request-docking", annotation.Slot.Label);
    }

    [Fact]
    public void Annotate_MacroSlotWithOverrideLabel_UsesOverride_NotPrettifiedMacroId()
    {
        var layout = new Layout(1, new List<LayoutPage>
        {
            new("SHIP", "t6", new List<LayoutSlot> { new(0, null, "request-docking", "DOCK", null) }, new List<LayoutSlot>())
        });
        var merge = MergeAgainst(BoundBindingsXml);

        var annotations = LayoutAnnotator.Annotate(layout, merge, MacroKnowledge.Empty, KnownActionNamesFrom(BoundBindingsXml));
        var annotation = Assert.Single(annotations);

        Assert.Equal("DOCK", annotation.Slot.Label);
    }

    [Fact]
    public void Annotate_MacroSlot_Label_UsesTheMacrosOwnName_WhenMacroKnowledgeHasOne()
    {
        // 2026-09-07 fix: a macro-referencing slot used to skip straight past
        // the macro's own `name` to Prettify(macroId), which is how
        // "request-docking" rendered as "Request-docking" instead of
        // "Request Docking" - see ref/docs/button-naming.md's "How a macro
        // supplies a default label".
        var layout = LoadFixture("degraded-macro-degraded.json"); // names "request-docking"
        var merge = MergeAgainst(BoundBindingsXml);
        var macros = new MacroKnowledge(
            new HashSet<string> { "request-docking" },
            new HashSet<string>(),
            new Dictionary<string, string> { ["request-docking"] = "Request\nDocking" });

        var annotations = LayoutAnnotator.Annotate(layout, merge, macros, KnownActionNamesFrom(BoundBindingsXml));
        var annotation = Assert.Single(annotations);

        Assert.Equal("Request\nDocking", annotation.Slot.Label);
    }

    [Fact]
    public void Annotate_MacroSlotWithOverrideLabel_BeatsTheMacrosOwnName()
    {
        // Precedence stays slot.Label ?? macroName ?? Prettify(macroId) - an
        // override still wins even when MacroKnowledge has a real name to
        // offer.
        var layout = new Layout(1, new List<LayoutPage>
        {
            new("SHIP", "t6", new List<LayoutSlot> { new(0, null, "request-docking", "DOCK", null) }, new List<LayoutSlot>())
        });
        var merge = MergeAgainst(BoundBindingsXml);
        var macros = new MacroKnowledge(
            new HashSet<string> { "request-docking" },
            new HashSet<string>(),
            new Dictionary<string, string> { ["request-docking"] = "Request\nDocking" });

        var annotations = LayoutAnnotator.Annotate(layout, merge, macros, KnownActionNamesFrom(BoundBindingsXml));
        var annotation = Assert.Single(annotations);

        Assert.Equal("DOCK", annotation.Slot.Label);
    }

    [Fact]
    public void Annotate_LongPress_Label_ResolvesIndependently_FromItsOwnActionOrMacro_WithNoOverridePossible()
    {
        // action=IncreaseEnginesPower (uncurated, absent from this bindings
        // file entirely -> Prettify fallback), longPress macro=pips-engines
        // (unknown -> Prettify fallback). LongPressAction carries no label
        // field, so there is no override to apply here even though the
        // primary slot's own Label field is null too in this fixture.
        var layout = LoadFixture("valid-with-long-press.json");
        var merge = MergeAgainst(BoundBindingsXml);

        var annotations = LayoutAnnotator.Annotate(layout, merge, MacroKnowledge.Empty, KnownActionNamesFrom(BoundBindingsXml));
        var slotAnnotation = annotations.Single(a => a.Slot.Index == 3).Slot;

        Assert.Equal("Increase Engines Power", slotAnnotation.Label);
        Assert.NotNull(slotAnnotation.LongPress);
        Assert.Equal("Pips-engines", slotAnnotation.LongPress!.Label);
    }

    [Fact]
    public void Annotate_SlotNamingNeitherActionNorMacro_Label_IsEmpty()
    {
        // Not a reachable state via LayoutValidator.ValidateForSave, but
        // LayoutStore never validates on load (see LayoutStore's own
        // remarks) - the annotator must still produce something sensible
        // rather than throw for a hand-corrupted or pre-validation layout.
        var layout = new Layout(1, new List<LayoutPage>
        {
            new("SHIP", "t6", new List<LayoutSlot> { new(0, null, null, null, null) }, new List<LayoutSlot>())
        });
        var merge = MergeAgainst(BoundBindingsXml);

        var annotations = LayoutAnnotator.Annotate(layout, merge, MacroKnowledge.Empty, KnownActionNamesFrom(BoundBindingsXml));
        var annotation = Assert.Single(annotations);

        Assert.Equal(string.Empty, annotation.Slot.Label);
    }

    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    [Fact]
    public void AnnotateAndLog_LogsAccurateDegradedCount()
    {
        var layout = LoadFixture("degraded-unbound.json"); // one slot, ToggleCargoScoop
        var merge = MergeAgainst(UnboundBindingsXml); // ToggleCargoScoop unbound here
        var log = new CapturingDiagnosticLog();

        var annotations = LayoutAnnotator.AnnotateAndLog(layout, merge, MacroKnowledge.Empty, KnownActionNamesFrom(UnboundBindingsXml), log);

        Assert.Equal(SlotStatus.Unbound, Assert.Single(annotations).Slot.Status);
        var evt = Assert.Single(log.Events);
        Assert.Equal("Layout", evt.Category);
        Assert.Contains("1 of 1", evt.Message);
    }

    [Fact]
    public void AnnotateAndLog_EverythingOk_LogsZeroDegraded()
    {
        var layout = LoadFixture("degraded-unbound.json");
        var merge = MergeAgainst(BoundBindingsXml); // ToggleCargoScoop bound here
        var log = new CapturingDiagnosticLog();

        LayoutAnnotator.AnnotateAndLog(layout, merge, MacroKnowledge.Empty, KnownActionNamesFrom(BoundBindingsXml), log);

        var evt = Assert.Single(log.Events);
        Assert.Contains("0 of 1", evt.Message);
    }
}
