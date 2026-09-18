using System.Text.Json;
using LunaPanel.Core.Bindings;
using LunaPanel.Core.GameState;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Macros;
using LunaPanel.Server.Http;
using LunaPanel.Server.Macros;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Drives <see cref="MacrosEndpoint"/> directly - the mappings and the save
/// decision the macro builder's routes serialize, kept free of any ASP.NET
/// type, same discipline as <c>ActionsEndpointTests</c>/
/// <c>SlotEditEndpointTests</c>. The real Kestrel wiring (auth, routing,
/// the store actually being written) is proved separately in
/// <c>ServerHostBuilderTests</c>.
/// </summary>
public class MacrosEndpointTests
{
    private const string BindingsXml = """
        <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
            <UI_Down><Primary Device="Keyboard" Key="Key_S" /><Secondary Device="{NoDevice}" Key="" /></UI_Down>
            <UI_Select><Primary Device="Keyboard" Key="Key_Space" /><Secondary Device="{NoDevice}" Key="" /></UI_Select>
            <CycleNextPanel><Primary Device="Keyboard" Key="Key_E" /><Secondary Device="{NoDevice}" Key="" /></CycleNextPanel>
            <UI_Up><Primary Device="{NoDevice}" Key="" /><Secondary Device="{NoDevice}" Key="" /></UI_Up>
        </Root>
        """;

    private static BindingsFile Bindings()
    {
        var result = BindingsFile.Parse(BindingsXml);
        Assert.True(result.Success, result.Error);
        return result.File!;
    }

    private static MacroDefinition Macro(string json) => MacroDefinition.Parse(json);

    private static readonly IReadOnlySet<string> NoShippedIds = new HashSet<string>(StringComparer.Ordinal);

    // -------------------------------------------------------------------
    // BuildResponse - the picker's rows and the builder's step list.
    // -------------------------------------------------------------------

    [Fact]
    public void BuildResponse_ListsShippedAndUserMacrosTogether_EachMarkedForWhichItIs()
    {
        var macros = new[]
        {
            Macro("""{ "id": "disembark", "name": "Disembark", "steps": [ { "press": "UI_Down", "repeat": 3 } ] }"""),
            Macro("""{ "id": "user-mine", "name": "Mine", "steps": [ { "press": "UI_Select", "repeat": 1 } ] }"""),
        };

        var dtos = MacrosEndpoint.BuildResponse(macros, new HashSet<string>(StringComparer.Ordinal) { "disembark" }, Bindings());

        Assert.Equal(2, dtos.Count);
        Assert.True(Assert.Single(dtos, d => d.Id == "disembark").IsShipped);
        Assert.False(Assert.Single(dtos, d => d.Id == "user-mine").IsShipped);
    }

    /// <summary>
    /// <b>The field the builder edits.</b> <c>Steps</c> is a display
    /// projection - kind, summary, bound state - and carries no
    /// <c>repeat</c>, no timeout and no condition tokens, so nothing could
    /// reconstruct an editable macro from it. <c>Definition</c> is the
    /// macro as the grammar stores it.
    ///
    /// Proved by round-tripping it back through the parser, not by reading
    /// fields off it: a definition that <see cref="MacroDefinition.Parse"/>
    /// rejects would be one the builder could load, edit and never save,
    /// with the failure appearing at the end rather than the start.
    /// </summary>
    [Fact]
    public void BuildResponse_CarriesTheStoredDefinition_AndItParsesBackToTheSameMacro()
    {
        var source = Macro("""
            {
              "id": "user-mine",
              "name": "Mine",
              "steps": [
                { "require": ["Docked", "GuiFocus:NoFocus"] },
                { "press": "UI_Down", "repeat": 3 },
                { "pressUntil": "UI_Select", "cond": ["Supercruise"], "timeoutMs": 2000, "maxAttempts": 5 }
              ]
            }
            """);

        var dto = Assert.Single(MacrosEndpoint.BuildResponse(new[] { source }, NoShippedIds, Bindings()));

        Assert.NotNull(dto.Definition);
        var reparsed = MacroDefinition.Parse(dto.Definition!.ToJsonString());

        Assert.Equal(source.Id, reparsed.Id);
        Assert.Equal(source.Name, reparsed.Name);
        Assert.Equal(source.Steps.Count, reparsed.Steps.Count);

        // The numbers the projection drops - the whole reason this field
        // exists. Asserted as literals, not by comparing the reparsed macro
        // to the source it came from, which would hold even if both were
        // wrong together.
        var press = Assert.IsType<PressStep>(reparsed.Steps[1]);
        Assert.Equal(3, press.Repeat);
        var pressUntil = Assert.IsType<PressUntilStep>(reparsed.Steps[2]);
        Assert.Equal(5, pressUntil.MaxAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(2000), pressUntil.Timeout);
        Assert.Equal(new[] { "Docked", "GuiFocus:NoFocus" }, Assert.IsType<RequireStep>(reparsed.Steps[0]).ConditionTokens);
    }

    /// <summary>
    /// A macro with no name of its own falls back to
    /// <c>Prettify(id)</c> - the same formula <c>LayoutAnnotator</c> uses to
    /// label a slot naming it (<c>ref/docs/button-naming.md</c>), so the
    /// picker row and the button it produces read the same.
    /// </summary>
    [Fact]
    public void BuildResponse_DisplayLabel_IsTheNameOrThePrettifiedId()
    {
        var macros = new[]
        {
            Macro("""{ "id": "named", "name": "Real Name", "steps": [ { "wait": 1 } ] }"""),
            Macro("""{ "id": "nomad-dock-launch", "steps": [ { "wait": 1 } ] }"""),
        };

        var dtos = MacrosEndpoint.BuildResponse(macros, NoShippedIds, Bindings());

        Assert.Equal("Real Name", Assert.Single(dtos, d => d.Id == "named").DisplayLabel);
        Assert.Equal(
            LunaPanel.Core.Catalogue.Prettifier.Prettify("nomad-dock-launch"),
            Assert.Single(dtos, d => d.Id == "nomad-dock-launch").DisplayLabel);
    }

    /// <summary>
    /// Degraded state comes from <c>MacroKnowledgeBuilder</c>, which is the
    /// same computation the annotator and the press path use, so the
    /// picker's row can never disagree with the button it produces.
    /// </summary>
    [Fact]
    public void BuildResponse_MacroWithAnUnboundStep_IsDegraded_AndOneWithoutIsNot()
    {
        var macros = new[]
        {
            Macro("""{ "id": "fine", "steps": [ { "press": "UI_Down", "repeat": 1 } ] }"""),
            Macro("""{ "id": "broken", "steps": [ { "press": "UI_Down", "repeat": 1 }, { "press": "UI_Up", "repeat": 1 } ] }"""),
        };

        var dtos = MacrosEndpoint.BuildResponse(macros, NoShippedIds, Bindings());

        Assert.False(Assert.Single(dtos, d => d.Id == "fine").IsDegraded);
        Assert.True(Assert.Single(dtos, d => d.Id == "broken").IsDegraded);
    }

    /// <summary>
    /// The one thing the builder adds over what already existed
    /// (<c>ref/docs/macro-builder.md</c>, question 5): a macro degrades as a
    /// whole today, and a commander cannot see <b>which step</b> is the
    /// unbound one. Per-step, with the identical copy
    /// <c>PressEndpoint</c> refuses an unbound slot with - never a third
    /// wording for one situation.
    /// </summary>
    [Fact]
    public void BuildResponse_NamesWhichStepIsUnbound_ReusingThePressRefusalCopyExactly()
    {
        var macro = Macro("""
            { "id": "m", "steps": [ { "press": "UI_Down", "repeat": 1 }, { "wait": 5 }, { "press": "UI_Up", "repeat": 1 } ] }
            """);

        var dto = Assert.Single(MacrosEndpoint.BuildResponse(new[] { macro }, NoShippedIds, Bindings()));

        Assert.Equal(3, dto.Steps.Count);

        Assert.Equal(0, dto.Steps[0].Index);
        Assert.True(dto.Steps[0].IsBound);
        Assert.Null(dto.Steps[0].UnboundReason);
        Assert.Equal("S", dto.Steps[0].DisplayChord);

        // A step that presses nothing has no bound state at all - not
        // "unbound", which would read as a problem to fix.
        Assert.Null(dto.Steps[1].Action);
        Assert.Null(dto.Steps[1].IsBound);
        Assert.Null(dto.Steps[1].UnboundReason);

        Assert.Equal(2, dto.Steps[2].Index);
        Assert.Equal("UI_Up", dto.Steps[2].Action);
        Assert.False(dto.Steps[2].IsBound);

        // Both halves matter and neither implies the other: that this is the
        // press path's own constant rather than a copied twin, AND that the
        // constant still says the actionable thing. An identity assertion
        // alone would stay green if the sentence became "Nope."
        Assert.Equal(PressEndpoint.NotBoundInEliteAdvice, dto.Steps[2].UnboundReason);
        Assert.Contains("Set a key for it in the game's Controls options", dto.Steps[2].UnboundReason);
    }

    /// <summary>
    /// <c>gotoLeftPanelTab</c> presses <c>CycleNextPanel</c> without naming
    /// it anywhere in the macro's own JSON, and
    /// <c>MacroKnowledgeBuilder</c> already degrades a macro when that
    /// action is unbound. A per-step view that showed a blank there would
    /// leave a commander looking at a macro marked degraded with no step
    /// admitting to it.
    /// </summary>
    [Fact]
    public void BuildResponse_GotoLeftPanelTabStep_ReportsTheActionItImplicitlyPresses()
    {
        var macro = Macro("""{ "id": "m", "steps": [ { "gotoLeftPanelTab": "Contacts" } ] }""");

        var dto = Assert.Single(MacrosEndpoint.BuildResponse(new[] { macro }, NoShippedIds, Bindings()));

        var step = Assert.Single(dto.Steps);
        Assert.Equal("gotoLeftPanelTab", step.Kind);
        Assert.Equal(PanelTabTracker.AdvanceTabAction, step.Action);
        Assert.True(step.IsBound);
    }

    /// <summary>
    /// A <c>pressKey</c> step names a physical key, never a Frontier action -
    /// it always reports no action and no bound state at all, exactly like
    /// wait/require/waitFor/waitForEdge, regardless of whether anything in
    /// <paramref name="Bindings"/> happens to be bound to that key. There is
    /// no <c>BindResolver</c> call in this path to disagree with.
    /// </summary>
    [Fact]
    public void BuildResponse_PressKeyStep_ReportsNoActionAndNoBoundState()
    {
        var macro = Macro("""{ "id": "m", "steps": [ { "pressKey": "Key_F5", "repeat": 2 } ] }""");

        var dto = Assert.Single(MacrosEndpoint.BuildResponse(new[] { macro }, NoShippedIds, Bindings()));

        var step = Assert.Single(dto.Steps);
        Assert.Equal("pressKey", step.Kind);
        Assert.Null(step.Action);
        Assert.Null(step.IsBound);
        Assert.Null(step.UnboundReason);
        Assert.Contains("F5", step.Summary);
        Assert.Contains("2", step.Summary);
    }

    /// <summary>
    /// Every step kind reports the grammar's own discriminator key as its
    /// <c>Kind</c>, in order, with a summary that says what the step will
    /// actually do. Swept across all seven rather than sampled, because a
    /// kind that fell through to a default would produce a plausible-looking
    /// row that named the wrong thing.
    /// </summary>
    [Fact]
    public void BuildResponse_EveryStepKind_ReportsItsGrammarKeyAndAReadableSummary()
    {
        var macro = Macro("""
            {
              "id": "m",
              "steps": [
                { "press": "UI_Down", "repeat": 3 },
                { "wait": 150 },
                { "require": ["Docked", "GuiFocus:NoFocus"] },
                { "waitFor": ["Supercruise"], "timeoutMs": 2000 },
                { "pressUntil": "UI_Select", "cond": ["Docked"], "timeoutMs": 500, "maxAttempts": 4 },
                { "waitForEdge": ["Journal:DockingGranted"], "failOn": ["Journal:DockingDenied"] },
                { "gotoLeftPanelTab": "Contacts" }
              ]
            }
            """);

        var dto = Assert.Single(MacrosEndpoint.BuildResponse(new[] { macro }, NoShippedIds, Bindings()));

        Assert.Equal(
            new[] { "press", "wait", "require", "waitFor", "pressUntil", "waitForEdge", "gotoLeftPanelTab" },
            dto.Steps.Select(s => s.Kind));
        Assert.All(dto.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Summary)));

        Assert.Contains("UI_Down", dto.Steps[0].Summary);
        Assert.Contains("3", dto.Steps[0].Summary);
        Assert.Contains("150", dto.Steps[1].Summary);
        Assert.Contains("Docked", dto.Steps[2].Summary);
        Assert.Contains("Supercruise", dto.Steps[3].Summary);
        Assert.Contains("UI_Select", dto.Steps[4].Summary);
        Assert.Contains("DockingGranted", dto.Steps[5].Summary);
        Assert.Contains("Contacts", dto.Steps[6].Summary);
    }

    // -------------------------------------------------------------------
    // BuildResponse - a branch step's arms, projected recursively
    // (StepDto.Then/Else, closing the gap MacrosEndpoint.ActionOf's old
    // remarks documented: a degraded action inside an arm had no step row
    // pointing at it).
    // -------------------------------------------------------------------

    /// <summary>
    /// Every non-branch step reports <c>null</c> for both new fields - the
    /// additive shape a caller reading only top-level <c>Steps</c> is
    /// unaffected by.
    /// </summary>
    [Fact]
    public void BuildResponse_ANonBranchStep_ReportsNullThenAndElse()
    {
        var macro = Macro("""{ "id": "m", "steps": [ { "press": "UI_Down", "repeat": 1 } ] }""");

        var dto = Assert.Single(MacrosEndpoint.BuildResponse(new[] { macro }, NoShippedIds, Bindings()));

        var step = Assert.Single(dto.Steps);
        Assert.Null(step.Then);
        Assert.Null(step.Else);
    }

    /// <summary>
    /// The gap this closes: <c>disembark</c>'s own shape - a branch whose
    /// <c>then</c> arm presses a bound action and whose <c>else</c> arm
    /// presses an unbound one. Each arm's steps come back as real
    /// <see cref="MacrosEndpoint.StepDto"/> rows, indexed within their own
    /// arm (0, 1, ...) rather than continuing the top-level index, with the
    /// identical bound-state/unbound-reason computation the top level gets -
    /// so a degraded action buried in an arm finally has a row naming it.
    /// </summary>
    [Fact]
    public void BuildResponse_BranchStep_ProjectsBothArmsRecursively_NamingAnUnboundActionInsideAnArm()
    {
        var macro = Macro("""
            {
              "id": "m",
              "steps": [
                {
                  "branch": ["Docked"],
                  "then": [ { "press": "UI_Down", "repeat": 1 }, { "press": "UI_Up", "repeat": 1 } ],
                  "else": [ { "wait": 5 } ]
                }
              ]
            }
            """);

        var dto = Assert.Single(MacrosEndpoint.BuildResponse(new[] { macro }, NoShippedIds, Bindings()));

        var branch = Assert.Single(dto.Steps);
        Assert.Equal("branch", branch.Kind);
        Assert.Null(branch.Action);

        Assert.NotNull(branch.Then);
        Assert.Equal(2, branch.Then!.Count);
        Assert.Equal(0, branch.Then[0].Index);
        Assert.Equal("UI_Down", branch.Then[0].Action);
        Assert.True(branch.Then[0].IsBound);
        Assert.Null(branch.Then[0].UnboundReason);

        Assert.Equal(1, branch.Then[1].Index);
        Assert.Equal("UI_Up", branch.Then[1].Action);
        Assert.False(branch.Then[1].IsBound);
        Assert.Equal(PressEndpoint.NotBoundInEliteAdvice, branch.Then[1].UnboundReason);

        Assert.NotNull(branch.Else);
        var elseStep = Assert.Single(branch.Else!);
        Assert.Equal(0, elseStep.Index);
        Assert.Equal("wait", elseStep.Kind);
        Assert.Null(elseStep.Action);
        Assert.Null(elseStep.Then);
        Assert.Null(elseStep.Else);
    }

    /// <summary>
    /// A branch may take one arm and leave the other empty
    /// (<c>BranchStep</c>'s own rule: not both at once). The empty arm
    /// still reports an empty list, never null - <see langword="null"/> is
    /// reserved for "not a branch at all".
    /// </summary>
    [Fact]
    public void BuildResponse_BranchStep_EmptyArm_ReportsAnEmptyListNotNull()
    {
        var macro = Macro("""
            { "id": "m", "steps": [ { "branch": ["Docked"], "then": [ { "wait": 1 } ] } ] }
            """);

        var dto = Assert.Single(MacrosEndpoint.BuildResponse(new[] { macro }, NoShippedIds, Bindings()));

        var branch = Assert.Single(dto.Steps);
        Assert.NotNull(branch.Else);
        Assert.Empty(branch.Else!);
    }

    // -------------------------------------------------------------------
    // BuildResponse - the copy-to-edit staleness line (question 3).
    // -------------------------------------------------------------------

    /// <summary>
    /// A macro with no recorded source - the default when
    /// <c>userMacroRecords</c> is omitted, and every existing caller's own
    /// case - is never stale, shipped or not.
    /// </summary>
    [Fact]
    public void BuildResponse_NoUserMacroRecordsGiven_NoMacroIsStale()
    {
        var macros = new[]
        {
            Macro("""{ "id": "disembark", "steps": [ { "press": "UI_Down", "repeat": 3 } ] }"""),
            Macro("""{ "id": "user-mine", "steps": [ { "press": "UI_Select", "repeat": 1 } ] }"""),
        };

        var dtos = MacrosEndpoint.BuildResponse(macros, new HashSet<string>(StringComparer.Ordinal) { "disembark" }, Bindings());

        Assert.All(dtos, d => Assert.False(d.IsStale));
        Assert.All(dtos, d => Assert.Null(d.SourceMacroId));
    }

    /// <summary>
    /// A fresh copy - hash recorded equal to the source's current steps -
    /// reports itself as not stale immediately after copying.
    /// </summary>
    [Fact]
    public void BuildResponse_ACopyWhoseHashStillMatchesItsSource_IsNotStale()
    {
        var source = Macro("""{ "id": "disembark", "steps": [ { "press": "UI_Down", "repeat": 3 } ] }""");
        var copy = Macro("""{ "id": "user-copy", "name": "Mine", "steps": [ { "press": "UI_Down", "repeat": 3 } ] }""");
        var records = new[] { new UserMacroRecord(copy, "disembark", MacroStepsHash.Compute(source.Steps)) };

        var dto = Assert.Single(
            MacrosEndpoint.BuildResponse(new[] { source, copy }, new HashSet<string>(StringComparer.Ordinal) { "disembark" }, Bindings(), records),
            d => d.Id == "user-copy");

        Assert.False(dto.IsStale);
        Assert.Equal("disembark", dto.SourceMacroId);
        Assert.Equal("Disembark", dto.SourceMacroName);
    }

    /// <summary>
    /// The whole reason this exists: the source's steps changed after the
    /// copy was made (the exact real-world case - <c>disembark</c> went from
    /// ten presses to three), and the copy's stored hash no longer matches
    /// the source's current one.
    ///
    /// Mutation prediction: mutating the source's step (repeat 3 -> 10,
    /// simulating the shipped macro having changed since the copy) is what
    /// turns this from green to red if the equality check were reversed or
    /// dropped - predicted 1 additional red (this test) with no other test
    /// touching source/hash comparison directly.
    /// </summary>
    [Fact]
    public void BuildResponse_ACopyWhoseSourceHasChangedSteps_IsStale()
    {
        var copiedAt = Macro("""{ "id": "disembark", "steps": [ { "press": "UI_Down", "repeat": 3 } ] }""");
        var sourceNow = Macro("""{ "id": "disembark", "steps": [ { "press": "UI_Down", "repeat": 10 } ] }""");
        var copy = Macro("""{ "id": "user-copy", "name": "Mine", "steps": [ { "press": "UI_Down", "repeat": 3 } ] }""");
        var records = new[] { new UserMacroRecord(copy, "disembark", MacroStepsHash.Compute(copiedAt.Steps)) };

        var dto = Assert.Single(
            MacrosEndpoint.BuildResponse(new[] { sourceNow, copy }, new HashSet<string>(StringComparer.Ordinal) { "disembark" }, Bindings(), records),
            d => d.Id == "user-copy");

        Assert.True(dto.IsStale);
    }

    /// <summary>
    /// The source was removed entirely (deleted, or - for a shipped id - no
    /// longer part of this build). There is nothing left to compare against,
    /// so this reports "may have changed" rather than silently reporting
    /// fresh - the reasonable reading of a case the brief left to judgement.
    /// </summary>
    [Fact]
    public void BuildResponse_ACopyWhoseSourceNoLongerExists_IsStale_WithNoSourceName()
    {
        var copy = Macro("""{ "id": "user-copy", "name": "Mine", "steps": [ { "press": "UI_Down", "repeat": 3 } ] }""");
        var records = new[] { new UserMacroRecord(copy, "gone-macro", "somehash") };

        var dto = Assert.Single(MacrosEndpoint.BuildResponse(new[] { copy }, NoShippedIds, Bindings(), records));

        Assert.True(dto.IsStale);
        Assert.Equal("gone-macro", dto.SourceMacroId);
        Assert.Null(dto.SourceMacroName);
    }

    // -------------------------------------------------------------------
    // BuildVocabulary - the token pickers, and the ruled step-kind list.
    // -------------------------------------------------------------------

    /// <summary>
    /// The ruling from <c>ref/docs/macro-builder.md</c>'s question 1: one
    /// list, everything offered, <b>nothing hidden behind a tier</b>. Swept
    /// against the grammar's own key list, so a step kind added later that
    /// the builder forgets to offer fails here rather than quietly becoming
    /// unauthorable.
    ///
    /// The sweep is against the grammar's keys minus
    /// <see cref="MacrosEndpoint.NotOfferedStepKinds"/>, the documented
    /// exception list.
    ///
    /// [SUPERSEDED 2026-09-17] This test read
    /// <c>.Where(k =&gt; k != "gotoLeftPanelTab")</c> and
    /// <c>Assert.Equal(StepKindKeys.Count - 1, ...)</c> - "exactly one
    /// permanent exception". <c>branch</c> became a second one (a grammar
    /// member with no authoring surface in this pass - see
    /// <see cref="BuildVocabulary_DeliberatelyExcludesBranch_ItHasNoArmEditorYet"/>),
    /// so the literal <c>- 1</c> is now <c>- NotOfferedStepKinds.Count</c>.
    /// The claim is unchanged and is not weakened: every grammar key is
    /// offered <b>except</b> the ones named in a list a test of its own pins,
    /// rather than except a name spelled inline here.
    /// </summary>
    [Fact]
    public void BuildVocabulary_OffersEveryStepKindInTheGrammar_NoneHidden()
    {
        var vocabulary = MacrosEndpoint.BuildVocabulary();

        var offerable = MacroDefinition.StepKindKeys
            .Where(k => !MacrosEndpoint.NotOfferedStepKinds.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal);

        Assert.Equal(
            offerable,
            vocabulary.StepKinds.Select(k => k.Kind).OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(MacroDefinition.StepKindKeys.Count - MacrosEndpoint.NotOfferedStepKinds.Count, vocabulary.StepKinds.Count);
    }

    /// <summary>
    /// The exception list is not a place to quietly park a step kind: every
    /// name in it must be a real grammar member (otherwise it subtracts
    /// nothing and the sweep above silently loosens).
    ///
    /// [SUPERSEDED 2026-09-17] Before-value: <c>new[] { "gotoLeftPanelTab",
    /// "branch" }</c>, and the name asserted "still just these two". A real
    /// arm editor now exists client-side, so <c>branch</c> is offered at the
    /// top level like any other kind and came off this list - see
    /// <see cref="BuildVocabulary_OffersBranch_NowThatAnArmEditorExists"/>.
    /// The list is down to one entry; a third or a re-added second is still a
    /// decision somebody has to make on purpose, not something that lands in
    /// a refactor.
    /// </summary>
    [Fact]
    public void NotOfferedStepKinds_AreRealGrammarMembers_AndStillJustThisOne()
    {
        Assert.Equal(new[] { "gotoLeftPanelTab" }, MacrosEndpoint.NotOfferedStepKinds);
        Assert.All(MacrosEndpoint.NotOfferedStepKinds, k => Assert.Contains(k, MacroDefinition.StepKindKeys));
    }

    /// <summary>
    /// [SUPERSEDED 2026-09-17] This test used to pin the OPPOSITE claim
    /// (<c>BuildVocabulary_DeliberatelyExcludesBranch_ItHasNoArmEditorYet</c>):
    /// that <c>branch</c> was deliberately absent from the offered kinds
    /// because authoring one needed a nested step list inside a step, which
    /// that pass did not build. That arm editor now exists
    /// (<c>PanelClientEndpoint</c>'s <c>#armEditor</c> sheet, reusing the
    /// step-list/step-editor machinery via a <c>{ list, isTopLevel }</c>
    /// context), so a top-level "add a branch" now produces a step whose arms
    /// are genuinely fillable through the UI rather than one
    /// <c>MacroDefinition.Parse</c> would always reject empty. This is not a
    /// weakening of the old claim - it is the ruling the old claim's own
    /// reason (no arm editor) explicitly said would change it.
    /// </summary>
    [Fact]
    public void BuildVocabulary_OffersBranch_NowThatAnArmEditorExists()
    {
        var vocabulary = MacrosEndpoint.BuildVocabulary();

        Assert.Contains("branch", MacroDefinition.StepKindKeys);
        Assert.Contains("branch", vocabulary.StepKinds.Select(k => k.Kind));
    }

    /// <summary>
    /// <c>gotoLeftPanelTab</c> is a grammar member (<see cref="MacroDefinition.StepKindKeys"/>)
    /// but deliberately not offered by the "add a step" picker: it is an
    /// internal building block used by macros this project ships (e.g.
    /// <c>request-docking.json</c> reaching CONTACTS), not something an
    /// ordinary commander hand-building a macro needs to be offered. Flagged
    /// live twice on 2026-09-12 before this exclusion was made an explicit,
    /// pinned invariant rather than an accident of the sweep above.
    /// </summary>
    [Fact]
    public void BuildVocabulary_DeliberatelyExcludesGotoLeftPanelTab_ItIsInternalOnly()
    {
        var vocabulary = MacrosEndpoint.BuildVocabulary();

        Assert.Contains("gotoLeftPanelTab", MacroDefinition.StepKindKeys);
        Assert.DoesNotContain("gotoLeftPanelTab", vocabulary.StepKinds.Select(k => k.Kind));
    }

    /// <summary>
    /// Ordered by how often a step is needed, with the gated ones under
    /// their own heading - the shape the ruling chose over an "advanced"
    /// tier. <c>gotoLeftPanelTab</c> is not among these at all: it is not
    /// offered by the picker (see
    /// <see cref="BuildVocabulary_DeliberatelyExcludesGotoLeftPanelTab_ItIsInternalOnly"/>).
    /// </summary>
    [Fact]
    public void BuildVocabulary_StepKinds_AreOrdered_WithTheGatedOnesGroupedAndTheTabStepNotAmongThem()
    {
        // [2026-09-12] Before-value: `new[] { "press", "wait", "require",
        // "waitFor", "waitForEdge", "pressUntil" }`, with only the first TWO
        // entries ungrouped (`kinds.Take(2)`). Superseded, not weakened: the
        // "press a key" feature added a real grammar member and offered it
        // right after "press" (ref/docs/macros.md) - the ungrouped prefix is
        // now three entries, not two.
        //
        // [SUPERSEDED 2026-09-17] Before-value ended at "pressUntil" with no
        // "branch". Superseded, not weakened: branch is offered now that the
        // client has a real arm editor, grouped under the same "Wait for the
        // game" heading as require/waitFor/pressUntil since it also reads
        // game state before deciding what to do.
        var kinds = MacrosEndpoint.BuildVocabulary().StepKinds;

        Assert.Equal(
            new[] { "press", "pressKey", "wait", "require", "waitFor", "waitForEdge", "pressUntil", "branch" },
            kinds.Select(k => k.Kind));

        Assert.All(kinds.Take(3), k => Assert.Equal(string.Empty, k.Group));
        Assert.All(kinds.Skip(3), k => Assert.Equal("Wait for the game", k.Group));
    }

    /// <summary>
    /// Name the symptom, do not block - the style <c>macro-timing.md</c>
    /// uses for a below-minimum value, applied to the one step kind that can
    /// fire the opposite action on a contextual surface (LC17/LC18).
    /// Pinned by content: a warning that said "be careful" would pass a
    /// non-empty check and tell a commander nothing.
    /// </summary>
    [Fact]
    public void BuildVocabulary_PressUntil_CarriesTheTogglingWarning_AndNoOtherStepDoes()
    {
        var kinds = MacrosEndpoint.BuildVocabulary().StepKinds;

        var pressUntil = Assert.Single(kinds, k => k.Kind == "pressUntil");
        Assert.Equal(
            "On a control that toggles, pressing again may undo the first press.",
            pressUntil.Warning);
        Assert.All(kinds.Where(k => k.Kind != "pressUntil"), k => Assert.Null(k.Warning));
    }

    /// <summary>
    /// The token pickers are built from the real tables, not a copy: every
    /// flag, every <c>GuiFocus</c> name and every journal event the parsers
    /// accept is offered, and nothing that would be rejected is. A picker
    /// missing a token is a token a commander cannot author; a picker
    /// offering one the parser rejects is a field that fails on save.
    ///
    /// [2026-09-09] The two assertions below used to read the bare-string
    /// shapes those two lists had before the builder's client half was
    /// built: <c>vocabulary.GuiFocus.OrderBy(...)</c> over
    /// <c>IReadOnlyList&lt;string&gt;</c>, and
    /// <c>Assert.Equal(Enum.GetNames&lt;PanelTab&gt;(), vocabulary.LeftPanelTabs)</c>.
    /// Both are now records carrying a plain label and a sentence of
    /// explanation (<see cref="MacroVocabularyCopy"/>). <b>The claim is
    /// unchanged</b> - every real table entry is offered and nothing else
    /// is; only the field the names are read out of moved.
    /// </summary>
    [Fact]
    public void BuildVocabulary_FlagsGuiFocusAndJournalEvents_MatchTheRealTablesExactly()
    {
        var vocabulary = MacrosEndpoint.BuildVocabulary();

        Assert.Equal(
            StatusVocabulary.FlagsConditions.Concat(StatusVocabulary.Flags2Conditions).Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal),
            vocabulary.Flags.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(
            StatusVocabulary.GuiFocusValues.Keys.OrderBy(n => n, StringComparer.Ordinal),
            vocabulary.GuiFocus.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(
            JournalVocabulary.Events.Keys.OrderBy(n => n, StringComparer.Ordinal),
            vocabulary.JournalEvents.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(Enum.GetNames<PanelTab>(), vocabulary.LeftPanelTabs.Select(t => t.Tab));
    }

    /// <summary>
    /// A journal token is offered in the exact form
    /// <c>EdgeCondition.Parse</c> accepts, prefix and all - the picker's
    /// whole purpose is that a token it produces parses, and an event name
    /// without its <c>Journal:</c> prefix is rejected by design (the two
    /// grammars are deliberately separate).
    ///
    /// [2026-09-09] The <c>GuiFocus</c> loop used to build the token itself
    /// (<c>Condition.Parse($"GuiFocus:{focus}")</c>) because the response
    /// carried only the bare name. That made the test prove something the
    /// client could not rely on: the prefix under test was the <em>test's
    /// own</em>, not the one shipped. The response now carries
    /// <see cref="MacrosEndpoint.GuiFocusDto.Token"/> and this parses that,
    /// so the thing proved to parse is the thing a picker row actually
    /// emits.
    /// </summary>
    [Fact]
    public void BuildVocabulary_EveryOfferedToken_ParsesWithTheGrammarThatWillReceiveIt()
    {
        var vocabulary = MacrosEndpoint.BuildVocabulary();

        foreach (var flag in vocabulary.Flags)
        {
            Condition.Parse(flag.Name);
        }

        foreach (var focus in vocabulary.GuiFocus)
        {
            Condition.Parse(focus.Token);
        }

        foreach (var journalEvent in vocabulary.JournalEvents)
        {
            EdgeCondition.Parse(journalEvent.Token);
        }

        foreach (var tab in vocabulary.LeftPanelTabs)
        {
            MacroDefinition.Parse($$"""{ "id": "m", "steps": [ { "gotoLeftPanelTab": "{{tab.Tab}}" } ] }""");
        }
    }

    /// <summary>
    /// <b>The sweep that makes the no-tier ruling survivable.</b>
    /// <c>ref/docs/macro-builder.md</c>'s question 1 chose to offer every
    /// step kind with nothing behind an "advanced" toggle, on the argument
    /// that hiding the powerful steps produces worse macros. That only holds
    /// if the powerful steps are explained instead - so every token the
    /// picker offers must carry a plain name and a sentence, and a table
    /// entry added later without copy fails here rather than reaching a
    /// commander as a bare <c>GuiFocus:NoFocus</c>.
    ///
    /// Deliberately checks more than non-emptiness. A label identical to the
    /// raw token is the <see cref="MacroVocabularyCopy"/> fallback showing
    /// through, and a "meaning" that merely repeats the label is the filler
    /// <c>ref/docs/macro-timing.md</c> already ruled out for tooltips.
    /// </summary>
    [Fact]
    public void BuildVocabulary_EveryOfferedToken_CarriesAPlainNameAndASentence()
    {
        var vocabulary = MacrosEndpoint.BuildVocabulary();

        var rows = vocabulary.Flags.Select(f => (Token: f.Name, f.Label, f.Meaning))
            .Concat(vocabulary.GuiFocus.Select(g => (Token: g.Name, g.Label, g.Meaning)))
            .Concat(vocabulary.JournalEvents.Select(e => (Token: e.Name, e.Label, e.Meaning)))
            .Concat(vocabulary.LeftPanelTabs.Select(t => (Token: t.Tab, t.Label, t.Meaning)))
            .ToList();

        Assert.Equal(
            StatusVocabulary.FlagsConditions.Count + StatusVocabulary.Flags2Conditions.Count +
            StatusVocabulary.GuiFocusValues.Count + JournalVocabulary.Events.Count + Enum.GetValues<PanelTab>().Length,
            rows.Count);

        foreach (var (token, label, meaning) in rows)
        {
            Assert.False(string.IsNullOrWhiteSpace(label), $"'{token}' has no plain name.");
            Assert.False(string.IsNullOrWhiteSpace(meaning), $"'{token}' has no sentence of explanation.");
            Assert.NotEqual(token, meaning);
            Assert.NotEqual(label, meaning);
            Assert.EndsWith(".", meaning, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The one token the brief names by hand: a commander must be able to
    /// use a "wait for the game" step without knowing what
    /// <c>GuiFocus:NoFocus</c> means. Pinned by content rather than by being
    /// present, because the whole value of this row is what it says - three
    /// of the four shipped macros open with this condition, and a
    /// commander's own macro that skips it lands its first press in whatever
    /// happened to be on screen.
    /// </summary>
    [Fact]
    public void BuildVocabulary_NoFocus_IsExplainedInPlainWords_NotJustNamed()
    {
        var noFocus = Assert.Single(MacrosEndpoint.BuildVocabulary().GuiFocus, g => g.Name == "NoFocus");

        Assert.Equal("GuiFocus:NoFocus", noFocus.Token);
        Assert.Equal("No panel open", noFocus.Label);
        Assert.Equal(
            "You are looking at the cockpit with none of the ship's panels open. Start here if the macro is going to open a panel itself - otherwise its first press lands in whatever is already on screen.",
            noFocus.Meaning);
    }

    /// <summary>
    /// <c>StatusVocabulary</c>'s own remarks call Frontier's naming
    /// counter-intuitive and keep the raw names traceable to the Journal
    /// Manual rather than "correcting" them. The picker therefore has to
    /// carry the correction in words, on both rows, or a commander reading
    /// the raw token under a friendly label concludes the panel has them
    /// backwards.
    /// </summary>
    [Theory]
    [InlineData("InternalPanel", "Right panel open")]
    [InlineData("ExternalPanel", "Left panel open")]
    public void BuildVocabulary_TheTwoBackwardsPanelNames_SayWhichSideTheyAre_AndWhyEliteCallsThemThat(string name, string expectedLabel)
    {
        var row = Assert.Single(MacrosEndpoint.BuildVocabulary().GuiFocus, g => g.Name == name);

        Assert.Equal(expectedLabel, row.Label);
        Assert.Contains(name, row.Meaning, StringComparison.Ordinal);
    }

    /// <summary>
    /// The commander scanning the waitForEdge picker for a literal "Docked"
    /// row found "Down on the pad" instead and did not recognise it. The
    /// label now says the plain word - the meaning sentence underneath still
    /// carries the "when is this true" detail that made the old label
    /// unnecessary in the first place.
    /// </summary>
    [Fact]
    public void BuildVocabulary_Docked_IsLabelledPlainly_NotDownOnThePad()
    {
        var docked = Assert.Single(MacrosEndpoint.BuildVocabulary().JournalEvents, e => e.Name == "Docked");

        Assert.Equal("Docked", docked.Label);
        Assert.NotEqual("Down on the pad", docked.Label);
    }

    /// <summary>
    /// The measured distinction that costs a macro twenty seconds when it is
    /// got wrong: <c>DockingRequested</c> is written the moment the request
    /// leaves, not when the station agrees (live-checks LC19 -
    /// <c>DockingDenied</c> fires alone, with no grant). A commander waiting
    /// on the wrong one of those two sits until the timeout, so the picker
    /// has to say which is which.
    /// </summary>
    [Fact]
    public void BuildVocabulary_DockingRequested_SaysItMeansAskedNotGranted()
    {
        var requested = Assert.Single(MacrosEndpoint.BuildVocabulary().JournalEvents, e => e.Name == "DockingRequested");

        Assert.Equal("Docking asked for", requested.Label);
        Assert.Equal(
            "Written the moment the request goes out. It says you asked - not that anyone said yes.",
            requested.Meaning);
    }

    /// <summary>
    /// Confidence is carried, not filtered: the three below-<c>Official</c>
    /// <c>Flags2</c> rows are offered like any other, marked for what they
    /// are. Refusing to offer them would be the never-gate rule broken in
    /// the picker instead of the runner.
    /// </summary>
    [Fact]
    public void BuildVocabulary_OffersLowConfidenceFlagsToo_MarkedRatherThanWithheld()
    {
        var vocabulary = MacrosEndpoint.BuildVocabulary();

        var lowConfidence = StatusVocabulary.Flags2Conditions
            .Where(f => f.Confidence != FlagConfidence.Official)
            .Select(f => f.Name)
            .ToList();

        Assert.NotEmpty(lowConfidence);
        foreach (var name in lowConfidence)
        {
            var dto = Assert.Single(vocabulary.Flags, f => f.Name == name);
            Assert.NotEqual(nameof(FlagConfidence.Official), dto.Confidence);
        }
    }

    // -------------------------------------------------------------------
    // BuildForSave - creating, updating, and what is refused.
    // -------------------------------------------------------------------

    private static readonly IReadOnlySet<string> NoExistingUserMacros = new HashSet<string>(StringComparer.Ordinal);

    [Fact]
    public void BuildForSave_NoIdInTheBody_MintsTheServersOwnId_NeverTheCommandersText()
    {
        var body = """{ "name": "Mine", "steps": [ { "press": "UI_Down", "repeat": 1 } ] }""";

        var result = MacrosEndpoint.BuildForSave(body, "user-minted01", NoExistingUserMacros);

        Assert.Equal(MacrosEndpoint.SaveOutcome.Ok, result.Outcome);
        Assert.Equal("user-minted01", result.Macro!.Id);
        Assert.Equal("Mine", result.Macro.Name);
    }

    /// <summary>
    /// The property the whole id/name split exists for: renaming does not
    /// move the id, so every slot naming it keeps working. A builder that
    /// derived the id from the name would orphan every one of them on a
    /// rename, silently, and the layout would still look fine until pressed.
    /// </summary>
    [Fact]
    public void BuildForSave_RenamingAnExistingMacro_KeepsItsId_SoNoSlotIsOrphaned()
    {
        var existing = new HashSet<string>(StringComparer.Ordinal) { "user-keepme" };
        var body = """{ "id": "user-keepme", "name": "Quite\nDifferent", "steps": [ { "wait": 5 } ] }""";

        var result = MacrosEndpoint.BuildForSave(body, "user-freshlyminted", existing);

        Assert.Equal(MacrosEndpoint.SaveOutcome.Ok, result.Outcome);
        Assert.Equal("user-keepme", result.Macro!.Id);
        Assert.Equal("Quite\nDifferent", result.Macro.Name);
    }

    /// <summary>
    /// An id the body supplies is only ever accepted as "update this one";
    /// a shipped id is refused outright, because a shipped macro is
    /// read-only and an override of one would shadow a later fix silently
    /// (<c>ref/docs/macro-builder.md</c>, question 3).
    /// </summary>
    [Fact]
    public void BuildForSave_AShippedId_IsRefused_ShippedMacrosAreReadOnly()
    {
        var body = """{ "id": "disembark", "name": "Mine", "steps": [ { "wait": 5 } ] }""";

        var result = MacrosEndpoint.BuildForSave(body, "user-minted01", new HashSet<string>(StringComparer.Ordinal) { "user-other" });

        Assert.Equal(MacrosEndpoint.SaveOutcome.NotAUserMacro, result.Outcome);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void BuildForSave_AUserIdThatDoesNotExist_IsRefused_RatherThanQuietlyCreatingIt()
    {
        var body = """{ "id": "user-ghost", "name": "Mine", "steps": [ { "wait": 5 } ] }""";

        var result = MacrosEndpoint.BuildForSave(body, "user-minted01", NoExistingUserMacros);

        Assert.Equal(MacrosEndpoint.SaveOutcome.NoSuchMacro, result.Outcome);
    }

    [Fact]
    public void BuildForSave_NotJson_IsReported_NotThrown()
    {
        var result = MacrosEndpoint.BuildForSave("{ not json", "user-minted01", NoExistingUserMacros);

        Assert.Equal(MacrosEndpoint.SaveOutcome.InvalidJson, result.Outcome);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("""{ "steps": [ { "wait": 1 } ] }""")]
    [InlineData("""{ "name": "", "steps": [ { "wait": 1 } ] }""")]
    [InlineData("""{ "name": "   ", "steps": [ { "wait": 1 } ] }""")]
    [InlineData("""{ "name": 7, "steps": [ { "wait": 1 } ] }""")]
    public void BuildForSave_NoUsableName_IsRefused_BecauseTheButtonWouldHaveNothingToShow(string body)
    {
        var result = MacrosEndpoint.BuildForSave(body, "user-minted01", NoExistingUserMacros);

        Assert.Equal(MacrosEndpoint.SaveOutcome.MissingName, result.Outcome);
    }

    /// <summary>
    /// A macro's name is what a slot naming it shows on the button, so it
    /// obeys the identical budget every other label does - checked through
    /// <c>LayoutValidator.LabelFitsBudget</c> itself, not a second copy of
    /// the rule. This is a shape rule about a label, NOT a judgement about
    /// whether the macro will work: nothing here refuses a save over what
    /// the steps do.
    ///
    /// [2026-09-09] SUPERSEDED fixture and assertions (before-value: the
    /// name <c>"An extremely long macro name"</c>, and
    /// <c>Assert.Contains("12", ...)</c> - a re-typed cap). That name now
    /// wraps into two lines that fit and is deliberately accepted, so it can
    /// no longer prove a refusal; the fixture is a name that still overflows
    /// AFTER wrapping, and the budget in the message is read from the
    /// constants rather than typed a second time.
    /// </summary>
    [Fact]
    public void BuildForSave_NameThatCannotFitAButton_IsRefused_WithAMessageSayingTheActualBudget()
    {
        var word = new string('A', LayoutValidator.UserLabelLineCap - 2);
        var body = $$"""{ "name": "{{word}} {{word}} {{word}}", "steps": [ { "wait": 1 } ] }""";

        var result = MacrosEndpoint.BuildForSave(body, "user-minted01", NoExistingUserMacros);

        Assert.Equal(MacrosEndpoint.SaveOutcome.NameDoesNotFit, result.Outcome);
        Assert.Contains(LayoutValidator.UserLabelMaxLines.ToString(), result.Error!);
        Assert.Contains(LayoutValidator.UserLabelLineCap.ToString(), result.Error);
    }

    /// <summary>
    /// THE TEN-CHARACTER REFUSAL (2026-09-09), on the surface it was
    /// actually reported from: the macro builder's name field. An on-screen
    /// keyboard appends a space when a word suggestion is tapped, and the
    /// old rule's no-leading-or-trailing-whitespace clause refused the
    /// result - so a ten-character name came back "will not fit on a
    /// button", with the offending character invisible. The builder's name
    /// field is a single-line <c>&lt;input&gt;</c> with no client-side
    /// check, so this server refusal was the whole of what the commander
    /// saw.
    /// </summary>
    [Fact]
    public void BuildForSave_ANameWithTheSpaceAnOnScreenKeyboardAppends_IsAccepted_TheTenCharacterRefusal()
    {
        var body = """{ "name": "Set Speed ", "steps": [ { "wait": 1 } ] }""";

        var result = MacrosEndpoint.BuildForSave(body, "user-minted01", NoExistingUserMacros);

        Assert.Equal(MacrosEndpoint.SaveOutcome.Ok, result.Outcome);
    }

    [Fact]
    public void BuildForSave_TwoLineName_IsAccepted_TheSameWayAShippedMacrosOwnNameIs()
    {
        var body = """{ "name": "Request\nDocking", "steps": [ { "wait": 1 } ] }""";

        var result = MacrosEndpoint.BuildForSave(body, "user-minted01", NoExistingUserMacros);

        Assert.Equal(MacrosEndpoint.SaveOutcome.Ok, result.Outcome);
        Assert.Equal("Request\nDocking", result.Macro!.Name);
    }

    /// <summary>
    /// Malformed step content is refused with the grammar's own message -
    /// which names the offending step index - rather than a generic "invalid
    /// macro". The commander has to know which step to fix.
    /// </summary>
    [Fact]
    public void BuildForSave_MalformedSteps_ReportsWhichStepAndWhy_NotJustThatItFailed()
    {
        var body = """{ "name": "Mine", "steps": [ { "wait": 5 }, { "press": "UI_Down" } ] }""";

        var result = MacrosEndpoint.BuildForSave(body, "user-minted01", NoExistingUserMacros);

        Assert.Equal(MacrosEndpoint.SaveOutcome.InvalidMacro, result.Outcome);
        Assert.Contains("step 1", result.Error!);
        Assert.Contains("repeat", result.Error);
    }

    [Fact]
    public void BuildForSave_NoStepsAtAll_IsRefused_AMacroHasToDoSomething()
    {
        var result = MacrosEndpoint.BuildForSave("""{ "name": "Mine" }""", "user-minted01", NoExistingUserMacros);

        Assert.Equal(MacrosEndpoint.SaveOutcome.InvalidMacro, result.Outcome);
        Assert.Contains("steps", result.Error!);
    }

    /// <summary>
    /// The never-gate rule at the save path
    /// (<c>.claude-memory/never-gate-a-macro.md</c>): a macro that presses an
    /// action bound to nothing, repeats it ten thousand times, waits an
    /// hour, and re-presses a contextual toggle is saved without argument.
    /// Every one of those is a thing a commander may legitimately want, and
    /// none of them is this endpoint's to refuse.
    /// </summary>
    [Fact]
    public void BuildForSave_AnUnwiseMacro_IsAccepted_NoAuthoringCapAndNoSafetyReview()
    {
        var body = """
            {
              "name": "Reckless",
              "steps": [
                { "press": "NotBoundToAnything", "repeat": 100000 },
                { "wait": 3600000 },
                { "pressUntil": "UI_Select", "cond": ["Docked"], "timeoutMs": 500, "maxAttempts": 50 }
              ]
            }
            """;

        var result = MacrosEndpoint.BuildForSave(body, "user-minted01", NoExistingUserMacros);

        Assert.Equal(MacrosEndpoint.SaveOutcome.Ok, result.Outcome);
        Assert.Equal(100000, Assert.IsType<PressStep>(result.Macro!.Steps[0]).Repeat);
    }

    /// <summary>
    /// Steps are copied through verbatim into the assembled macro, not
    /// re-read field by field here - so a grammar field this endpoint has
    /// never heard of still survives a save. Driven with every optional
    /// field the grammar has, since those are the ones a partial
    /// re-implementation would drop.
    /// </summary>
    [Fact]
    public void BuildForSave_PassesEveryOptionalStepFieldThrough_NeverReinterpretingTheGrammar()
    {
        var body = """
            {
              "name": "Full",
              "steps": [
                { "press": "UI_Down", "repeat": 2, "holdMs": 275 },
                { "waitForEdge": ["Journal:DockingGranted"], "failOn": ["Journal:DockingDenied"], "failureDetailField": "Reason", "timeoutMs": 45000 }
              ]
            }
            """;

        var result = MacrosEndpoint.BuildForSave(body, "user-minted01", NoExistingUserMacros);

        Assert.Equal(MacrosEndpoint.SaveOutcome.Ok, result.Outcome);
        Assert.Equal(TimeSpan.FromMilliseconds(275), Assert.IsType<PressStep>(result.Macro!.Steps[0]).Hold);
        var edge = Assert.IsType<WaitForEdgeStep>(result.Macro.Steps[1]);
        Assert.Equal("Reason", edge.FailureDetailField);
        Assert.Equal(TimeSpan.FromMilliseconds(45000), edge.Timeout);
        Assert.Equal(new[] { "Journal:DockingDenied" }, edge.FailOnTokens);
    }

    /// <summary>
    /// The stored macro carries the id the server decided on, in the file
    /// itself - not only in its file name. A definition whose own <c>id</c>
    /// disagreed with the name it was saved under would resolve differently
    /// depending on which of the two something read.
    /// </summary>
    [Fact]
    public void BuildForSave_TheAssembledMacroCarriesTheServersIdInternally_NotJustInItsFileName()
    {
        var result = MacrosEndpoint.BuildForSave(
            """{ "id": "user-ignored-if-lying", "name": "Mine", "steps": [ { "wait": 1 } ] }""",
            "user-minted01",
            new HashSet<string>(StringComparer.Ordinal) { "user-ignored-if-lying" });

        Assert.Equal(MacrosEndpoint.SaveOutcome.Ok, result.Outcome);
        using var document = JsonDocument.Parse(MacroJson.Serialize(result.Macro!));
        Assert.Equal("user-ignored-if-lying", document.RootElement.GetProperty("id").GetString());
    }

    // -------------------------------------------------------------------
    // BuildForCopy - copy-to-edit.
    // -------------------------------------------------------------------

    [Fact]
    public void BuildForCopy_TakesTheSourcesStepsUnderAFreshMintedId()
    {
        var source = MacroLoader.LoadShipped().Single(m => m.Id == "disembark");

        var result = MacrosEndpoint.BuildForCopy(source, "user-copy01", "My Steps");

        Assert.Equal(MacrosEndpoint.SaveOutcome.Ok, result.Outcome);
        Assert.Equal("user-copy01", result.Macro!.Id);
        Assert.Equal("My Steps", result.Macro.Name);
        Assert.Equal(MacroJson.Serialize(source with { Id = "user-copy01", Name = "My Steps" }), MacroJson.Serialize(result.Macro));
    }

    /// <summary>
    /// [2026-09-12] The staleness metadata question 3 left NOT built: a copy
    /// records its source's id and a hash of its steps at copy time, so
    /// <c>ServerHostBuilder.SaveMacro</c> has something to persist beside it.
    /// </summary>
    [Fact]
    public void BuildForCopy_RecordsTheSourceIdAndAHashOfItsSteps()
    {
        var source = MacroLoader.LoadShipped().Single(m => m.Id == "disembark");

        var result = MacrosEndpoint.BuildForCopy(source, "user-copy01", "My Steps");

        Assert.Equal("disembark", result.SourceMacroId);
        Assert.Equal(MacroStepsHash.Compute(source.Steps), result.SourceStepsHash);
    }

    [Fact]
    public void BuildForCopy_NoNameGiven_KeepsTheSourcesOwnName()
    {
        var source = MacroLoader.LoadShipped().Single(m => m.Id == "disembark");

        var result = MacrosEndpoint.BuildForCopy(source, "user-copy01", requestedName: null);

        Assert.Equal(MacrosEndpoint.SaveOutcome.Ok, result.Outcome);
        Assert.Equal(source.Name, result.Macro!.Name);
    }

    /// <summary>
    /// A copy is a user macro from the moment it is made - never an
    /// override, never a reference back. Pinned as the id, because that is
    /// the whole mechanism: a layout slot naming the copy names something
    /// the shipped set does not contain, so a later release changing the
    /// shipped macro cannot reach it.
    /// </summary>
    [Fact]
    public void BuildForCopy_NeverKeepsTheSourceId()
    {
        var source = MacroLoader.LoadShipped().Single(m => m.Id == "request-docking");

        var result = MacrosEndpoint.BuildForCopy(source, "user-copy01", null);

        Assert.NotEqual(source.Id, result.Macro!.Id);
        Assert.True(UserMacroIds.IsUserMacroId(result.Macro.Id));
    }

    /// <summary>
    /// [2026-09-09] SUPERSEDED fixture (before-value: the requested name
    /// <c>"An extremely long macro name"</c>). That name wraps into two
    /// lines that fit and is now deliberately accepted, so it could no
    /// longer prove a refusal - a name that still overflows AFTER wrapping
    /// replaces it.
    /// </summary>
    [Fact]
    public void BuildForCopy_ANameThatCannotFitAButton_IsRefused_SameBudgetAsASave()
    {
        var source = MacroLoader.LoadShipped().Single(m => m.Id == "disembark");
        var word = new string('A', LayoutValidator.UserLabelLineCap - 2);

        var result = MacrosEndpoint.BuildForCopy(source, "user-copy01", $"{word} {word} {word}");

        Assert.Equal(MacrosEndpoint.SaveOutcome.NameDoesNotFit, result.Outcome);
    }

    /// <summary>
    /// Copy-to-edit defaults the copy's name to the shipped macro's own
    /// name, and that default is put through the identical budget - so a
    /// shipped name the budget refuses makes "Make a copy I can edit"
    /// impossible, with a refusal about a name the commander never typed.
    /// Found 2026-09-09: <c>pip-preset-weapons</c> ships as
    /// <c>"Pip Preset: Weapons"</c>, nineteen characters on one line, which
    /// the pre-wrapping rule refused outright. Swept over every shipped
    /// macro rather than that one, because the next shipped macro to be
    /// written is exactly as able to reintroduce it.
    /// </summary>
    [Fact]
    public void BuildForCopy_EveryShippedMacrosOwnName_IsUsableAsTheCopysDefault()
    {
        var offenders = new List<string>();

        foreach (var source in MacroLoader.LoadShipped())
        {
            var result = MacrosEndpoint.BuildForCopy(source, "user-copy01", requestedName: null);
            if (result.Outcome != MacrosEndpoint.SaveOutcome.Ok)
            {
                offenders.Add($"{source.Id} ('{source.Name?.Replace("\n", "\\n")}'): {result.Outcome} - {result.Error}");
            }
        }

        Assert.Empty(offenders);
    }
}
