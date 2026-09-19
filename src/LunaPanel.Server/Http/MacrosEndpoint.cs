using System.Text.Json;
using System.Text.Json.Nodes;
using LunaPanel.Core.Bindings;
using LunaPanel.Core.Catalogue;
using LunaPanel.Core.GameState;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Macros;

namespace LunaPanel.Server.Http;

/// <summary>
/// The macro builder's handler logic (<c>ref/docs/macro-builder.md</c>) -
/// listing macros, saving one, copying a shipped one to edit, and the token
/// vocabulary the builder's pickers are built from. Kept free of any
/// ASP.NET type, same discipline as <see cref="ActionsEndpoint"/>/
/// <see cref="MacroTimingEndpoint"/>.
///
/// Three rules govern everything here and are not negotiable:
///
/// <list type="bullet">
/// <item><b>A macro is never refused for uncertainty.</b> Nothing in this
/// type judges whether a macro will <em>work</em> - not an unbound action,
/// not a large <c>repeat</c>, not a <c>pressUntil</c> on a contextual
/// toggle. A save is refused only when the content is not a macro at all
/// (the grammar cannot read it) or when the request is asking for something
/// that does not exist. Confidence is information to record, never a gate.</item>
/// <item><b>Intent, never resolution.</b> What is stored is action names and
/// condition tokens. The bound state and chord this type reports per step
/// are <em>display</em>, recomputed from live bindings on every request via
/// the same <see cref="BindResolver.Resolve"/> call <c>MacroRunner</c> makes
/// - never written down.</item>
/// <item><b>Shipped macros are read-only.</b> Copy-to-edit, so a later
/// release's fix to a shipped macro cannot be silently shadowed by an
/// override nobody remembers making.</item>
/// </list>
/// </summary>
public static class MacrosEndpoint
{
    /// <param name="Index">The step's position in the macro, so a per-step message can name it.</param>
    /// <param name="Kind">The grammar's own discriminator key (<see cref="MacroDefinition.StepKindKeys"/>), never a prettified spelling.</param>
    /// <param name="Summary">A one-line human reading of the step, for the builder's list.</param>
    /// <param name="Action">
    /// The Frontier action name this step presses, or <see langword="null"/>
    /// for a step that presses nothing. <c>gotoLeftPanelTab</c> reports
    /// <see cref="PanelTabTracker.AdvanceTabAction"/> - the action it
    /// implicitly needs - because a commander looking at a degraded macro
    /// needs to see the thing that is actually unbound, not a blank.
    /// </param>
    /// <param name="IsBound">Whether <see cref="Action"/> currently resolves to a keyboard chord; <see langword="null"/> when the step presses nothing.</param>
    /// <param name="DisplayChord">The resolved chord, for display beside the step - never stored.</param>
    /// <param name="UnboundReason">
    /// The same copy <c>PressEndpoint</c> refuses an unbound slot with, so a
    /// commander reads one wording for one situation - never a third.
    /// </param>
    /// <param name="Then">
    /// [2026-09-17] <see langword="null"/> for every step except
    /// <c>branch</c>, where it carries the <c>then</c> arm's own steps,
    /// recursively projected through this same record - so a degraded action
    /// living inside an arm gets its own real row, indexed within its arm's
    /// own list (arm index 0, 1, ...), rather than the whole branch reporting
    /// only step counts. Read-only display, exactly like <see cref="Steps"/>
    /// itself: this does not make arm contents editable, and does not change
    /// the deliberate no-nested-arm-editor decision
    /// (<c>ref/docs/macro-builder.md</c>).
    /// </param>
    /// <param name="Else">The <c>else</c> arm's steps, on the same terms as <see cref="Then"/>.</param>
    public sealed record StepDto(
        int Index,
        string Kind,
        string Summary,
        string? Action,
        bool? IsBound,
        string? DisplayChord,
        string? UnboundReason,
        IReadOnlyList<StepDto>? Then = null,
        IReadOnlyList<StepDto>? Else = null);

    /// <param name="DisplayLabel">What a slot naming this macro shows with no override - <c>Name</c>, falling back to <c>Prettify(Id)</c>, exactly as <c>LayoutAnnotator</c> resolves it.</param>
    /// <param name="IsShipped">Read-only when true: the builder offers "copy" rather than "edit".</param>
    /// <param name="IsDegraded">From <see cref="MacroKnowledgeBuilder"/>, never a second opinion computed here.</param>
    /// <param name="Steps">Every step, in order, as <em>display</em> - kind, a readable summary, and the bound state of whatever it presses. The picker ignores these; the builder shows them.</param>
    /// <param name="Definition">
    /// The macro exactly as the grammar stores it - <c>{ id, name, steps }</c>,
    /// written by <see cref="MacroJson.Serialize"/>, the same call
    /// <see cref="UserMacroStore"/> persists with.
    ///
    /// [2026-09-09] Added for the builder's client half. <see cref="Steps"/>
    /// above is a projection: it carries a step's <em>summary</em>, not its
    /// <c>repeat</c>, its timeout or its condition tokens, so nothing could
    /// reconstruct an editable macro from it. Without this field the whole
    /// founding use case - "start from Disembark and change one number" -
    /// is unreachable, and copy-to-edit produces a copy nothing can edit.
    /// Carried as parsed JSON rather than a string so there is no second
    /// escaping layer, and produced by the one serializer rather than
    /// re-derived here, so the builder can never be handed a shape
    /// <see cref="MacroDefinition.Parse"/> would reject.
    /// </param>
    /// <param name="SourceMacroId">
    /// [2026-09-12] The id this macro was copied from, or <see langword="null"/>
    /// when it was authored from scratch. <c>ref/docs/macro-builder.md</c>,
    /// question 3's staleness line - previously recorded there as NOT built.
    /// </param>
    /// <param name="SourceMacroName">
    /// The source's current display label, for the staleness note's wording
    /// ("Copied from X, which has changed since"). <see langword="null"/>
    /// when <see cref="SourceMacroId"/> is null, or when the source no
    /// longer exists.
    /// </param>
    /// <param name="IsStale">
    /// <see langword="false"/> when this macro was not copied from anything.
    /// Otherwise: <see langword="true"/> when the source's current steps no
    /// longer hash the same as they did at copy time, OR when the source no
    /// longer exists at all (there is nothing to compare against, so this
    /// side reports "may have changed" rather than silently reporting
    /// fresh).
    /// </param>
    public sealed record MacroDto(
        string Id,
        string? Name,
        string DisplayLabel,
        bool IsShipped,
        bool IsDegraded,
        IReadOnlyList<StepDto> Steps,
        JsonNode? Definition,
        string? SourceMacroId,
        string? SourceMacroName,
        bool IsStale);

    /// <param name="Kind">The grammar key this row authors.</param>
    /// <param name="Label">What the builder shows in its step-kind list.</param>
    /// <param name="Group">Empty for the ungrouped top of the list; otherwise the heading the gated steps sit under.</param>
    /// <param name="Description">One line saying what the step does.</param>
    /// <param name="Warning">A named symptom, never a refusal - see <see cref="BuildVocabulary"/>.</param>
    public sealed record StepKindDto(string Kind, string Label, string Group, string Description, string? Warning);

    /// <param name="Name">The raw token, exactly as <c>Condition.Parse</c> takes it - still carried, and still shown, just not first.</param>
    /// <param name="Label">The plain name the picker leads with (<see cref="MacroVocabularyCopy"/>).</param>
    /// <param name="Meaning">One sentence saying when this flag is true. Empty only if somebody added a flag and not its copy, which a test fails on.</param>
    public sealed record FlagDto(string Name, string Field, string Confidence, string Label, string Meaning);

    /// <param name="Token">The full <c>GuiFocus:Name</c> form the parser accepts, so a picker row cannot produce something that fails to parse.</param>
    /// <param name="Name">The bare value name, as <see cref="StatusVocabulary.GuiFocusValues"/> spells it.</param>
    public sealed record GuiFocusDto(string Token, string Name, string Label, string Meaning);

    public sealed record JournalEventDto(string Token, string Name, string Provenance, string Label, string Meaning);

    /// <param name="Tab">The <see cref="PanelTab"/> name the grammar's <c>gotoLeftPanelTab</c> takes.</param>
    public sealed record LeftPanelTabDto(string Tab, string Label, string Meaning);

    public sealed record VocabularyResponse(
        IReadOnlyList<StepKindDto> StepKinds,
        IReadOnlyList<FlagDto> Flags,
        IReadOnlyList<GuiFocusDto> GuiFocus,
        IReadOnlyList<JournalEventDto> JournalEvents,
        IReadOnlyList<LeftPanelTabDto> LeftPanelTabs);

    public enum SaveOutcome
    {
        Ok,
        InvalidJson,
        MissingName,
        NameDoesNotFit,
        NotAUserMacro,
        NoSuchMacro,
        InvalidMacro,
    }

    /// <param name="Error">Operator-readable, and specific: for <see cref="SaveOutcome.InvalidMacro"/> it is the grammar's own message, which names the macro id and the offending step index.</param>
    /// <param name="SourceMacroId">
    /// [2026-09-12] Set only by <see cref="BuildForCopy"/>: the id the copy
    /// was made from, for <c>UserMacroStore.Save</c> to record beside it.
    /// <see langword="null"/> for an ordinary save - see the caller
    /// (<c>ServerHostBuilder.SaveMacro</c>) for why an ordinary save still
    /// does not erase a copy's provenance on a later edit.
    /// </param>
    /// <param name="SourceStepsHash"><see cref="MacroStepsHash.Compute"/> over the source's steps at copy time.</param>
    /// <param name="AttemptedName">
    /// [2026-09-17] The name the commander actually typed, when a refusal has
    /// one to give - only <see cref="SaveOutcome.NameDoesNotFit"/> does; a
    /// missing name has nothing to log. Carried so <c>ServerHostBuilder</c>'s
    /// refusal log can name what was refused (O34) instead of just why.
    /// </param>
    public sealed record SaveResult(SaveOutcome Outcome, MacroDefinition? Macro, string? Error, string? SourceMacroId = null, string? SourceStepsHash = null, string? AttemptedName = null)
    {
        public static SaveResult Ok(MacroDefinition macro) => new(SaveOutcome.Ok, macro, null);

        public static SaveResult OkFromCopy(MacroDefinition macro, string sourceMacroId, string sourceStepsHash) =>
            new(SaveOutcome.Ok, macro, null, sourceMacroId, sourceStepsHash);

        public static SaveResult Fail(SaveOutcome outcome, string error) => new(outcome, null, error);

        public static SaveResult Fail(SaveOutcome outcome, string error, string attemptedName) =>
            new(outcome, null, error, AttemptedName: attemptedName);
    }

    /// <summary>
    /// Maps macros into the wire shape the picker and the builder consume.
    /// Which macros to pass is the caller's choice
    /// (<c>MacroCatalogue.All()</c>); this type only maps.
    ///
    /// <paramref name="userMacroRecords"/> carries the copy-to-edit
    /// provenance (<see cref="UserMacroRecord.SourceMacroId"/>/
    /// <see cref="UserMacroRecord.SourceStepsHash"/>) that lives beside a
    /// user macro rather than inside its <see cref="MacroDefinition"/> -
    /// optional and defaulting to none, so every existing caller (and every
    /// shipped-only response) is unaffected and reports no macro as stale.
    /// </summary>
    public static IReadOnlyList<MacroDto> BuildResponse(
        IReadOnlyList<MacroDefinition> macros,
        IReadOnlySet<string> shippedIds,
        BindingsFile bindings,
        IReadOnlyList<UserMacroRecord>? userMacroRecords = null)
    {
        ArgumentNullException.ThrowIfNull(macros);
        ArgumentNullException.ThrowIfNull(shippedIds);
        ArgumentNullException.ThrowIfNull(bindings);

        // Degraded state is asked of MacroKnowledgeBuilder rather than
        // recomputed here, so the picker's row, the button the picker
        // produces, and the press path's refusal can never disagree - the
        // second check that could drift is the whole failure this avoids
        // (ref/docs/macro-builder.md, question 5).
        var knowledge = MacroKnowledgeBuilder.Build(macros, bindings);

        var recordsByMacroId = (userMacroRecords ?? Array.Empty<UserMacroRecord>())
            .ToDictionary(r => r.Macro.Id, r => r, StringComparer.Ordinal);
        var macrosById = macros.ToDictionary(m => m.Id, m => m, StringComparer.Ordinal);

        return macros
            .Select(macro =>
            {
                var (sourceMacroId, sourceMacroName, isStale) = StalenessOf(macro.Id, recordsByMacroId, macrosById, knowledge);

                return new MacroDto(
                    macro.Id,
                    macro.Name,
                    knowledge.NameFor(macro.Id) ?? Prettifier.Prettify(macro.Id),
                    shippedIds.Contains(macro.Id),
                    knowledge.DegradedIds.Contains(macro.Id),
                    macro.Steps.Select((step, index) => BuildStep(index, step, bindings)).ToList(),
                    JsonNode.Parse(MacroJson.Serialize(macro)),
                    sourceMacroId,
                    sourceMacroName,
                    isStale);
            })
            .ToList();
    }

    /// <summary>
    /// The staleness line (<c>ref/docs/macro-builder.md</c>, question 3): a
    /// copy is stale when the source it was made from has changed its steps
    /// since, or has been removed outright (nothing to compare against, so
    /// this reports "may have changed" rather than silently reporting
    /// fresh). A macro with no recorded source - shipped, or authored from
    /// scratch - is never stale.
    /// </summary>
    private static (string? SourceMacroId, string? SourceMacroName, bool IsStale) StalenessOf(
        string macroId,
        IReadOnlyDictionary<string, UserMacroRecord> recordsByMacroId,
        IReadOnlyDictionary<string, MacroDefinition> macrosById,
        MacroKnowledge knowledge)
    {
        if (!recordsByMacroId.TryGetValue(macroId, out var record) || record.SourceMacroId is null)
        {
            return (null, null, false);
        }

        if (!macrosById.TryGetValue(record.SourceMacroId, out var currentSource))
        {
            return (record.SourceMacroId, null, true);
        }

        var sourceMacroName = knowledge.NameFor(currentSource.Id) ?? Prettifier.Prettify(currentSource.Id);
        var currentHash = MacroStepsHash.Compute(currentSource.Steps);
        var isStale = !string.Equals(currentHash, record.SourceStepsHash, StringComparison.Ordinal);
        return (record.SourceMacroId, sourceMacroName, isStale);
    }

    private static StepDto BuildStep(int index, MacroStep step, BindingsFile bindings)
    {
        // A branch presses nothing itself (ActionOf returns null for it), but
        // its arms need their own recursively-built rows - see StepDto.Then/
        // Else's remarks. Checked before the ActionOf/ null branch below so a
        // branch does not fall into the "presses nothing" case and lose its
        // arms.
        if (step is BranchStep branch)
        {
            return new StepDto(
                index,
                KindOf(step),
                SummaryOf(step),
                null,
                null,
                null,
                null,
                branch.Then.Select((armStep, armIndex) => BuildStep(armIndex, armStep, bindings)).ToList(),
                branch.Else.Select((armStep, armIndex) => BuildStep(armIndex, armStep, bindings)).ToList());
        }

        var action = ActionOf(step);
        if (action is null)
        {
            return new StepDto(index, KindOf(step), SummaryOf(step), null, null, null, null);
        }

        // The same BindResolver.Resolve call MacroRunner makes to fire this
        // step, and MacroKnowledgeBuilder makes to decide the macro is
        // degraded - so "which step is unbound" always names a step the
        // macro would genuinely have failed on.
        var resolution = BindResolver.Resolve(bindings, action);
        return new StepDto(
            index,
            KindOf(step),
            SummaryOf(step),
            action,
            resolution.IsBound,
            resolution.Chord?.DisplayText,
            resolution.IsBound ? null : PressEndpoint.NotBoundInEliteAdvice);
    }

    /// <summary>
    /// The action a step presses, or <see langword="null"/> for one that
    /// presses nothing. Mirrors <see cref="MacroKnowledgeBuilder"/>'s own
    /// switch exactly, including <c>gotoLeftPanelTab</c>'s implicit
    /// <see cref="PanelTabTracker.AdvanceTabAction"/> - a step that
    /// contributed to a macro being degraded but showed no action here would
    /// leave a commander looking at a degraded macro no step admitted to.
    /// </summary>
    private static string? ActionOf(MacroStep step) => step switch
    {
        PressStep press => press.Action,
        PressUntilStep pressUntil => pressUntil.Action,
        GotoLeftPanelTabStep => PanelTabTracker.AdvanceTabAction,
        // Names a physical key, not a Frontier action - nothing for
        // BindResolver to look up, so this reports no action at all (same
        // "presses nothing" branch as wait/require/waitFor/waitForEdge).
        PressKeyStep => null,
        // A branch presses nothing ITSELF; its arms may. BuildStep handles
        // BranchStep before calling this method at all, building Then/Else
        // recursively (see StepDto.Then/Else's remarks) - so this arm is only
        // ever reached for a BranchStep passed directly to ActionOf, and it
        // correctly reports "presses nothing" for the branch step's own row.
        BranchStep => null,
        _ => null,
    };

    private static string KindOf(MacroStep step) => step switch
    {
        PressStep => "press",
        PressKeyStep => "pressKey",
        WaitStep => "wait",
        RequireStep => "require",
        WaitForStep => "waitFor",
        PressUntilStep => "pressUntil",
        WaitForEdgeStep => "waitForEdge",
        GotoLeftPanelTabStep => "gotoLeftPanelTab",
        BranchStep => "branch",
        _ => throw new InvalidOperationException($"Unhandled macro step kind '{step.GetType().Name}'."),
    };

    private static string SummaryOf(MacroStep step) => step switch
    {
        PressStep press => press.Hold is TimeSpan hold
            ? $"Press {press.Action} {press.Repeat} time(s), holding {(int)hold.TotalMilliseconds} ms"
            : $"Press {press.Action} {press.Repeat} time(s)",
        PressKeyStep pressKey => pressKey.Hold is TimeSpan pressKeyHold
            ? $"Press the {BindResolver.PrettifyKeyName(pressKey.Key)} key {pressKey.Repeat} time(s), holding {(int)pressKeyHold.TotalMilliseconds} ms"
            : $"Press the {BindResolver.PrettifyKeyName(pressKey.Key)} key {pressKey.Repeat} time(s)",
        WaitStep wait => $"Wait {(int)wait.Duration.TotalMilliseconds} ms",
        RequireStep require => $"Only run if {string.Join(" and ", require.ConditionTokens)}",
        WaitForStep waitFor => $"Wait up to {(int)waitFor.Timeout.TotalMilliseconds} ms for {string.Join(" and ", waitFor.ConditionTokens)}",
        PressUntilStep pressUntil =>
            $"Press {pressUntil.Action} until " +
            (pressUntil.JournalCondition is not null
                ? pressUntil.JournalCondition.EventName
                : string.Join(" and ", pressUntil.ConditionTokens!)) +
            $" (up to {pressUntil.MaxAttempts} time(s), {(int)pressUntil.Timeout.TotalMilliseconds} ms each)",
        WaitForEdgeStep edge => edge.FailOnTokens.Count > 0
            ? $"Wait up to {(int)edge.Timeout.TotalMilliseconds} ms for {string.Join(" or ", edge.SucceedOnTokens)}, or {string.Join(" or ", edge.FailOnTokens)}"
            : $"Wait up to {(int)edge.Timeout.TotalMilliseconds} ms for {string.Join(" or ", edge.SucceedOnTokens)}",
        GotoLeftPanelTabStep goTo => $"Go to the left panel's {goTo.Target} tab",
        // The summary itself still reports counts, not content - the arms'
        // own steps are projected separately, onto StepDto.Then/Else (see
        // BuildStep), not restated in this string.
        BranchStep branch =>
            $"If {string.Join(" and ", branch.ConditionTokens)}: {branch.Then.Count} step(s), " +
            $"otherwise {branch.Else.Count} step(s)",
        _ => throw new InvalidOperationException($"Unhandled macro step kind '{step.GetType().Name}'."),
    };

    /// <summary>
    /// The token pickers' data, read off the real
    /// <see cref="StatusVocabulary"/>/<see cref="JournalVocabulary"/> tables
    /// rather than restated in the client - a free-text field is a field
    /// that fails on a typo, since the parser rejects an unknown token
    /// anyway (<c>ref/docs/macro-builder.md</c>, question 1).
    ///
    /// [2026-09-09] Every row now also carries a plain name and a sentence
    /// of explanation from <see cref="MacroVocabularyCopy"/>, and the
    /// <c>GuiFocus</c> and tab lists became records rather than bare strings
    /// to hold them (before: <c>IReadOnlyList&lt;string&gt;</c> of
    /// <c>NoFocus</c>/<c>Galaxy</c> spellings). The picker had names and
    /// confidence and no meanings, which is the open question
    /// <c>ref/docs/macro-builder.md</c>'s "Not decided" list called "how the
    /// vocabulary picker glosses a flag" - and the ruling to offer every
    /// step kind with no advanced tier only holds up if the powerful steps
    /// are explained rather than hidden. <c>GuiFocus</c> gained a
    /// <see cref="GuiFocusDto.Token"/> carrying the <c>GuiFocus:</c> prefix
    /// at the same time, matching what journal events already did, so no
    /// consumer has to know to prepend it.
    /// </summary>
    public static VocabularyResponse BuildVocabulary() => new(
        StepKinds,
        StatusVocabulary.FlagsConditions
            .Concat(StatusVocabulary.Flags2Conditions)
            .Select(f =>
            {
                var gloss = MacroVocabularyCopy.FlagOrFallback(f.Name);
                return new FlagDto(f.Name, f.Field.ToString(), f.Confidence.ToString(), gloss.Label, gloss.Meaning);
            })
            .ToList(),
        StatusVocabulary.GuiFocusValues.Keys
            .Select(name =>
            {
                var gloss = MacroVocabularyCopy.GuiFocusOrFallback(name);
                return new GuiFocusDto($"GuiFocus:{name}", name, gloss.Label, gloss.Meaning);
            })
            .ToList(),
        JournalVocabulary.Events.Values
            .Select(e =>
            {
                var gloss = MacroVocabularyCopy.JournalEventOrFallback(e.Name);
                return new JournalEventDto($"Journal:{e.Name}", e.Name, e.Provenance.ToString(), gloss.Label, gloss.Meaning);
            })
            .ToList(),
        Enum.GetValues<PanelTab>()
            .Select(tab =>
            {
                var gloss = MacroVocabularyCopy.LeftPanelTabOrFallback(tab);
                return new LeftPanelTabDto(tab.ToString(), gloss.Label, gloss.Meaning);
            })
            .ToList());

    /// <summary>
    /// The commander's step-kind list, in the order the ruling chose: press
    /// and wait first (the macro most people want - open a panel, move,
    /// select, close), then the gated steps under one heading. <b>One list,
    /// everything offered, nothing behind an "advanced" toggle</b>
    /// (<c>ref/docs/macro-builder.md</c>, question 1).
    ///
    /// <c>gotoLeftPanelTab</c> is deliberately <b>not</b> in this list, even
    /// though it is a grammar member (<see cref="MacroDefinition.StepKindKeys"/>)
    /// and stays fully editable in a macro that already has one. It is an
    /// internal building block used by macros this project ships (e.g.
    /// <c>request-docking.json</c> reaches CONTACTS by it), not something an
    /// ordinary commander hand-building a "press these buttons in order"
    /// macro needs offered as an authoring primitive - flagged live twice on
    /// 2026-09-12 before this exclusion was made explicit. Removing it here
    /// only removes it from the "add a new step" picker; a copy of a shipped
    /// macro that already contains one still opens, displays and edits it,
    /// because the step editor keys off the step's own kind, not this list.
    ///
    /// <c>pressUntil</c> carries a warning and no gate - name the symptom,
    /// do not block, the same style <c>ref/docs/macro-timing.md</c> uses for
    /// a below-minimum timing value. The symptom is measured, not imagined:
    /// a second <c>UI_Select</c> on the CONTACTS row cancels a docking
    /// clearance the first one just won (<c>tests/notes/live-checks.md</c>
    /// LC18).
    ///
    /// <c>branch</c> [SUPERSEDED 2026-09-17: an interactive arm editor now
    /// exists client-side, so this exclusion no longer applies at the top
    /// level - see the paragraph below the strikethrough for the current
    /// state.] ~~is excluded for a different reason, on the same precedent
    /// (2026-09-17): it IS a grammar member, and a macro that already has one
    /// displays and round-trips fine, but authoring one needs a nested-arm
    /// editor - a step list inside a step - that this pass deliberately did
    /// not build. Offering "add a branch" from the picker would produce a
    /// step with two empty arms and no way to fill either, which
    /// <see cref="MacroDefinition.Parse"/> then rejects on save. The
    /// commander edits arms by hand-editing the macro JSON for now.~~
    ///
    /// <c>branch</c> is now offered here, at the top level, the same as any
    /// other kind: <c>PanelClientEndpoint</c>'s builder gained a real arm
    /// editor (a <c>#armEditor</c> sheet reusing the same step-list/step-
    /// editor machinery the top level uses, parameterized around a small
    /// <c>{ list, isTopLevel }</c> context) so a branch's <c>then</c>/<c>else</c>
    /// arms are genuinely add/remove/reorder/edit-able rather than a raw-JSON-
    /// only affair. The one-level nesting cap the engine itself enforces at
    /// load time (no <c>branch</c> inside a <c>then</c>/<c>else</c> arm) is
    /// mirrored client-side: this server-side list still excludes nothing
    /// depth-aware (it has no notion of depth), so the client itself filters
    /// <c>branch</c> back out of the offered kinds whenever the "add a step"
    /// picker is opened from inside an arm rather than at the top level -
    /// belt-and-suspenders UX on top of the server's own authoritative
    /// rejection, not a second copy of the rule.
    ///
    /// This list lives server-side rather than in the client's JavaScript so
    /// it can be swept against <see cref="MacroDefinition.StepKindKeys"/> -
    /// a grammar addition the builder forgets to offer fails a test instead
    /// of quietly being unauthorable. <see cref="NotOfferedStepKinds"/> is
    /// the documented exception list that sweep subtracts
    /// (see <c>MacrosEndpointTests.BuildVocabulary_OffersEveryStepKindInTheGrammar_NoneHidden</c>).
    /// </summary>
    /// <summary>
    /// The grammar members the "add a step" picker deliberately does NOT
    /// offer, and the whole of that exception - a test sweeps
    /// <see cref="MacroDefinition.StepKindKeys"/> minus this list against
    /// what <see cref="BuildVocabulary"/> actually serves, so a step kind
    /// quietly dropped from the picker without being named here fails rather
    /// than becoming unauthorable in silence.
    ///
    /// [SUPERSEDED 2026-09-17] Before-value: <c>new[] { "gotoLeftPanelTab",
    /// "branch" }</c>. <c>branch</c> came off this list once the client
    /// gained a real arm editor (see <see cref="StepKinds"/>' own remarks) -
    /// it is now offered at the top level like any other kind, and excluded
    /// only client-side, only when the picker is opened from inside an arm.
    /// </summary>
    public static readonly IReadOnlyList<string> NotOfferedStepKinds = new[] { "gotoLeftPanelTab" };

    private static readonly IReadOnlyList<StepKindDto> StepKinds = new[]
    {
        new StepKindDto("press", "Press a control", string.Empty, "Press one of Elite's controls, once or several times.", null),
        // [2026-09-12] "Press a key" (ref/docs/macros.md): starts from a
        // PHYSICAL key rather than a control name. The client special-cases
        // this kind's row (it opens a key picker, then either offers a real
        // "press" step when something is already bound there, or a raw
        // pressKey step when nothing is) rather than creating a step
        // directly the way every other row here does - see
        // PanelClientEndpoint's renderStepKindList. Placed second, right
        // after "press", because it is the other on-ramp to the exact same
        // outcome most of the time (a bound key becomes a normal press
        // step) - not a niche feature buried under the gated group.
        new StepKindDto(
            "pressKey",
            "Press a key",
            string.Empty,
            "Press a physical key directly, whether or not Elite has anything bound to it.",
            null),
        new StepKindDto("wait", "Wait", string.Empty, "Pause for a fixed time before the next step.", null),
        new StepKindDto(
            "require",
            "Only run if...",
            GatedGroup,
            "Check the game's state before anything is pressed. If it does not hold, the macro stops having sent no keys at all.",
            null),
        new StepKindDto(
            "waitFor",
            "Wait for the game to be...",
            GatedGroup,
            "Wait until the game reports a state, up to a time limit. The macro stops if the time runs out.",
            null),
        new StepKindDto(
            "waitForEdge",
            "Wait for something to happen",
            GatedGroup,
            "Wait for the game to log an event - a docking grant, an SRV launch. Can also name the event that means it was refused.",
            null),
        new StepKindDto(
            "pressUntil",
            "Press until the game is...",
            GatedGroup,
            "Press a control repeatedly until the game reports a state, so a toggle that did not register is sent again.",
            "On a control that toggles, pressing again may undo the first press."),
        // [2026-09-17] Added once the client gained a real arm editor - see
        // NotOfferedStepKinds' own remarks for the history. Grouped with the
        // other game-state checks rather than given its own heading: like
        // require/waitFor/pressUntil, it reads the game before deciding what
        // to do, it just decides between two step lists instead of
        // continuing or stopping.
        new StepKindDto(
            "branch",
            "Check the game, then...",
            GatedGroup,
            "Check the game's state, then run one set of steps if it matches, or a different set if it does not. Only one side ever runs.",
            null),
    };

    private const string GatedGroup = "Wait for the game";

    /// <summary>
    /// Turns a save request body into a <see cref="MacroDefinition"/> ready
    /// to persist, or a reported failure.
    ///
    /// The body is <c>{ "id": string|null, "name": string, "steps": [...] }</c>.
    /// <c>steps</c> is copied through <b>verbatim</b> into the assembled
    /// macro and validated by <see cref="MacroDefinition.TryParse"/> - the
    /// one parser that understands the grammar - rather than by a second
    /// reading of it here, so the builder can never accept a step shape the
    /// runner cannot execute.
    ///
    /// The id is decided here and never taken from the commander: absent
    /// means create, and <paramref name="mintedId"/> is used; present means
    /// update, and it must be an existing user macro. That is what makes a
    /// rename a pure name change - the id a layout slot stored does not
    /// move, so no slot is orphaned (<c>ref/docs/macro-builder.md</c>).
    /// </summary>
    public static SaveResult BuildForSave(string rawBody, string mintedId, IReadOnlySet<string> existingUserMacroIds)
    {
        ArgumentNullException.ThrowIfNull(existingUserMacroIds);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawBody ?? string.Empty);
        }
        catch (JsonException ex)
        {
            return SaveResult.Fail(SaveOutcome.InvalidJson, $"That macro could not be read: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return SaveResult.Fail(SaveOutcome.InvalidJson, "A macro must be sent as a JSON object.");
            }

            // The id is decided here, never taken from the commander. An
            // absent id means "create", and the server's minted one is used;
            // a present id means "update", and it has to be an existing user
            // macro. That is the whole reason a rename cannot orphan a slot:
            // the id a layout stored never moves.
            string id;
            if (root.TryGetProperty("id", out var idElement) && idElement.ValueKind != JsonValueKind.Null)
            {
                var requestedId = idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : null;
                if (!UserMacroIds.IsUserMacroId(requestedId))
                {
                    return SaveResult.Fail(
                        SaveOutcome.NotAUserMacro,
                        "That macro came with LunaPanel and cannot be changed. Make a copy of it and edit that instead.");
                }

                if (!existingUserMacroIds.Contains(requestedId!))
                {
                    return SaveResult.Fail(SaveOutcome.NoSuchMacro, "That macro no longer exists.");
                }

                id = requestedId!;
            }
            else
            {
                id = mintedId;
            }

            var name = root.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()
                : null;

            var nameResult = ValidateName(name);
            if (nameResult is not null)
            {
                return nameResult;
            }

            var assembled = Assemble(id, name!, root.TryGetProperty("steps", out var steps) ? steps : (JsonElement?)null);
            return FromGrammar(assembled);
        }
    }

    /// <summary>
    /// Copy-to-edit: a fresh user macro with <paramref name="mintedId"/>,
    /// the same steps as <paramref name="source"/>, and a name of the
    /// commander's choosing (defaulting to the source's, which is why the
    /// name budget is applied to the default too).
    ///
    /// The copy is a plain user macro from that moment: editing it does not
    /// reach back into the source, and a later release changing the source
    /// does not change the copy. The second half of that is a real cost, not
    /// an oversight - see <c>ref/docs/macro-builder.md</c>'s question 3.
    ///
    /// [2026-09-12] The staleness metadata that half of question 3 asked for
    /// is now recorded here: the source's id and a hash of its steps AT COPY
    /// TIME, both carried on the returned <see cref="SaveResult"/> for
    /// <c>UserMacroStore.Save</c> to persist beside the copy. Computed over
    /// <paramref name="source"/>'s steps as given - before the id/name swap
    /// below, though that makes no difference to the hash, which never reads
    /// either.
    /// </summary>
    public static SaveResult BuildForCopy(MacroDefinition source, string mintedId, string? requestedName)
    {
        ArgumentNullException.ThrowIfNull(source);

        var name = requestedName ?? source.Name ?? Prettifier.Prettify(source.Id);

        var nameResult = ValidateName(name);
        if (nameResult is not null)
        {
            return nameResult;
        }

        var sourceStepsHash = MacroStepsHash.Compute(source.Steps);
        return SaveResult.OkFromCopy(source with { Id = mintedId, Name = name }, source.Id, sourceStepsHash);
    }

    /// <summary>
    /// <see langword="null"/> when the name is usable; the refusal
    /// otherwise. A macro's name is what a slot naming it shows on the
    /// button with no override set, so it obeys the identical budget every
    /// other label does - through <see cref="LayoutValidator.LabelFitsBudget"/>
    /// itself, never a second copy of the rule.
    ///
    /// This is a shape rule about a <em>label</em>, and is not the
    /// never-gate rule being bent: nothing here judges what the macro will
    /// do or whether it will work. A save is refused only when the name
    /// could not be drawn on the button it is for.
    /// </summary>
    private static SaveResult? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return SaveResult.Fail(SaveOutcome.MissingName, "Give this macro a name - it is what the button will say.");
        }

        if (!LayoutValidator.LabelFitsBudget(name))
        {
            return SaveResult.Fail(
                SaveOutcome.NameDoesNotFit,
                // [2026-09-09] Reworded (before-value: "...characters each,
                // with no blank line and no spaces at either end."). Both
                // clauses became false: a space at either end is absorbed
                // now, and the wrapping is the part a commander needs told,
                // because it is why they no longer have to find an Enter
                // key their tablet keyboard does not have.
                $"That name will not fit on a button: at most {LayoutValidator.UserLabelMaxLines} line(s) of " +
                $"{LayoutValidator.UserLabelLineCap} characters each, even split at the spaces. Shorter words, or fewer of them.",
                name!);
        }

        return null;
    }

    /// <summary>
    /// Builds the macro JSON the grammar will read, with the server's id and
    /// the commander's name, and the steps <b>copied through verbatim</b>.
    /// Copying rather than re-reading the step objects field by field is
    /// what keeps one grammar in one place: this endpoint cannot accept a
    /// step shape <see cref="MacroDefinition.Parse"/> would reject, and
    /// cannot drop a field it has never heard of.
    /// </summary>
    private static string Assemble(string id, string name, JsonElement? steps)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("id", id);
            writer.WriteString("name", name);

            if (steps is JsonElement stepsElement)
            {
                writer.WritePropertyName("steps");
                stepsElement.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// The one place assembled content meets the grammar. Goes through
    /// <see cref="MacroDefinition.TryParse"/>, so a malformed macro is
    /// reported - carrying the message that already names the offending step
    /// index - rather than thrown out of an HTTP handler.
    /// </summary>
    private static SaveResult FromGrammar(string macroJson)
    {
        var parsed = MacroDefinition.TryParse(macroJson);
        return parsed.Success
            ? SaveResult.Ok(parsed.Macro!)
            : SaveResult.Fail(SaveOutcome.InvalidMacro, parsed.Error!);
    }
}
