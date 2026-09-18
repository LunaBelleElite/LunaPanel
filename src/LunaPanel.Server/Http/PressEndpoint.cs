using System.Text.Json.Serialization;
using LunaPanel.Core.Bindings;
using LunaPanel.Core.Catalogue;
using LunaPanel.Core.Layouts;

namespace LunaPanel.Server.Http;

/// <summary>
/// <c>POST /api/press</c>'s handler logic, kept free of any ASP.NET or Win32
/// type so it can be driven directly by a test - same discipline as
/// <see cref="PanelEndpoint"/>/<see cref="PairEndpoint"/>.
///
/// <see cref="Evaluate"/> re-annotates the target slot fresh (the exact same
/// <see cref="LayoutAnnotator"/> call <c>GET /api/panel</c> uses) and decides
/// whether firing should even be attempted: anything other than a usable
/// (<see cref="SlotStatus.Ok"/>) slot refuses before any chord is resolved,
/// let alone injected - a degraded macro, an unbound action, or an unknown
/// action never reaches <see cref="Server.Input.Win32KeyInjector"/>. Actually
/// injecting the resolved chord is the caller's job
/// (<see cref="Server.Input.ChordPresser"/>, wired in <see cref="Server.Hosting.ServerHostBuilder"/>) -
/// this type only decides "should we", and resolves what to fire if so.
/// </summary>
public static class PressEndpoint
{
    /// <summary>
    /// What a commander is told when the thing they pressed names an action
    /// Elite has no key for. Actionable, and genuinely reassuring
    /// (<c>ref/docs/editor.md</c>): a layout stores intent, never a resolved
    /// key, and bindings are re-read on every panel request - so binding
    /// this action in Elite heals the slot on the very next load, with no
    /// edit, restart, or re-pair needed here.
    ///
    /// [2026-09-09] Pulled out of <see cref="Evaluate"/>'s body into a
    /// constant so the macro builder's per-step "which step is unbound"
    /// display can show this exact wording rather than a third one
    /// (<c>ref/docs/macro-builder.md</c>, question 5). One situation, one
    /// sentence: a commander who reads it on a button and again on a macro
    /// step should not have to work out whether they mean the same thing.
    /// </summary>
    public const string NotBoundInEliteAdvice =
        "Not bound in Elite. Set a key for it in the game's Controls options and this button starts working - nothing to change here.";

    /// <summary>
    /// [2026-09-12] The hold gesture's two phases - pointerdown (ensure the
    /// key is held) and pointerup (ensure it is released), completely
    /// separate from a tap or a latch toggle. Named rather than a bare bool
    /// for the same reason <see cref="Latching.LatchRegistry.ToggleOutcome"/>
    /// is named rather than a bool - a request body reading
    /// <c>"hold":"down"</c> says what it means without a caller having to
    /// remember which boolean value means which phase.
    /// </summary>
    // [2026-09-12] No project-wide JsonStringEnumConverter is configured
    // (every enum elsewhere in this codebase crosses the wire server->client
    // only, via .ToString() into an anonymous object, never round-tripped
    // back in as JSON). This is the first enum a REQUEST body carries, so
    // there is no existing convention to match - the converter is scoped to
    // this one type rather than changed globally, so "down"/"up" read
    // clearly in the client's fetch body without touching how any other
    // type in this project serializes.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HoldPhase
    {
        Down,
        Up,
    }

    // LongPress defaults to false when absent from the JSON body, so a
    // plain { page, slot } request still resolves to the primary action.
    //
    // [2026-09-12] Hold defaults to null - matching LongPress's "absent
    // means not this kind of request" convention exactly, rather than a
    // bool default of false that could not distinguish "not a hold request"
    // from "the up phase of one" the way Down/Up can't be told apart from a
    // missing field either way. A hold slot's client sends this on every
    // pointerdown/pointerup; every other slot's client never sends it at
    // all, so it stays null for every request this feature does not touch.
    public sealed record Request(int Page, int Slot, bool LongPress = false, HoldPhase? Hold = null);

    public enum EvaluationOutcome
    {
        CanFire,
        NoSuchPage,
        NoSuchSlot,
        NoLongPress,
        NotUsable,
    }

    /// <param name="MacroId">
    /// The macro id to fire, when this slot names a macro rather than an
    /// action - <see langword="null"/> whenever <see cref="Chord"/> is set,
    /// and vice versa. Only the id, never a resolved <c>MacroDefinition</c>:
    /// <see cref="Evaluate"/> only decides whether a slot's already-computed
    /// <see cref="SlotStatus.Ok"/> annotation should be trusted to fire, the
    /// same way it never itself re-derives a chord's scancodes - looking the
    /// definition up (and actually running it) is the caller's job, exactly
    /// as resolving <see cref="Chord"/>'s bytes for injection already is.
    /// </param>
    /// <param name="Action">
    /// The Frontier action name <see cref="Chord"/> was resolved from -
    /// <see langword="null"/> whenever <see cref="Chord"/> is (a macro slot
    /// names no single action). Carried alongside the chord so a caller can
    /// feed it to <c>LunaPanel.Core.GameState.PanelTabPressEffect</c> without
    /// having to re-derive which action this press evaluated - see
    /// <c>LunaPanel.Server.Input.PlainPresser</c>.
    /// </param>
    /// <param name="Latch">
    /// <see langword="true"/> when this slot latches its action rather than
    /// tapping it (<c>ref/docs/latching-keys.md</c>) - the caller toggles
    /// <c>LunaPanel.Core.Latching.LatchRegistry</c> instead of calling
    /// <c>PlainPresser</c>. Only ever true for a primary action press: a
    /// <see cref="LongPressAction"/> carries no latch of its own, so a
    /// long-press on a latching slot is still an ordinary tap of its
    /// long-press action.
    /// </param>
    /// <param name="Hold">
    /// [2026-09-12] <see langword="true"/> when this slot holds its action
    /// for as long as a finger stays down rather than tapping or latching it
    /// (<c>ref/docs/latching-keys.md</c>'s hold-to-thrust extension) - the
    /// caller calls <c>LunaPanel.Core.Latching.LatchRegistry.Hold</c>/
    /// <c>Release</c> instead of <c>Toggle</c> or <c>PlainPresser</c>. Set
    /// from <see cref="LayoutSlot.Hold"/> exactly the way <see cref="Latch"/>
    /// is set from <see cref="LayoutSlot.Latch"/>, and mutually exclusive
    /// with it by construction - <see cref="LayoutValidator"/> never lets a
    /// slot carry both.
    /// </param>
    public sealed record EvaluationResult(EvaluationOutcome Outcome, string? Reason, ResolvedChord? Chord, string? MacroId, string? Action, bool Latch = false, bool Hold = false)
    {
        public static EvaluationResult Fire(ResolvedChord chord, string action, bool latch = false, bool hold = false) => new(EvaluationOutcome.CanFire, null, chord, null, action, latch, hold);
        public static EvaluationResult FireMacro(string macroId) => new(EvaluationOutcome.CanFire, null, null, macroId, null);
        public static EvaluationResult Refuse(EvaluationOutcome outcome, string reason) => new(outcome, reason, null, null, null);
    }

    /// <param name="longPress">
    /// <see langword="false"/> (the default) evaluates the slot's primary
    /// action/macro, exactly as before this parameter existed.
    /// <see langword="true"/> evaluates its <see cref="LongPressAction"/>
    /// instead, refusing with <see cref="EvaluationOutcome.NoLongPress"/> if
    /// the slot has none - a long-press degrades independently of the
    /// primary action (<c>ref/docs/editor.md</c>), so a working primary must
    /// never be disabled just because its long-press is unbound, and the
    /// reverse holds too: this method never looks at the primary's own
    /// status when <paramref name="longPress"/> is <see langword="true"/>.
    /// </param>
    public static EvaluationResult Evaluate(
        Layout layout,
        int pageIndex,
        int slotIndex,
        BindingsFile bindings,
        IReadOnlyList<CataloguePickerEntry> catalogueMerge,
        MacroKnowledge macros,
        IReadOnlySet<string> knownActionNames,
        bool longPress = false)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(catalogueMerge);
        ArgumentNullException.ThrowIfNull(macros);
        ArgumentNullException.ThrowIfNull(knownActionNames);

        if (pageIndex < 0 || pageIndex >= layout.Pages.Count)
        {
            return EvaluationResult.Refuse(EvaluationOutcome.NoSuchPage, "No such page.");
        }

        var page = layout.Pages[pageIndex];
        var slot = page.Slots.FirstOrDefault(s => s.Index == slotIndex);
        if (slot is null)
        {
            return EvaluationResult.Refuse(EvaluationOutcome.NoSuchSlot, "No such slot on this page.");
        }

        if (longPress && slot.LongPress is null)
        {
            return EvaluationResult.Refuse(EvaluationOutcome.NoLongPress, "This button has no long-press action.");
        }

        var singlePageLayout = new Layout(layout.SchemaVersion, new[] { page });
        var annotation = LayoutAnnotator.Annotate(singlePageLayout, catalogueMerge, macros, knownActionNames)
            .First(a => a.Slot.Index == slotIndex);

        var status = longPress ? annotation.Slot.LongPress!.Status : annotation.Slot.Status;
        var reason = longPress ? annotation.Slot.LongPress!.Reason : annotation.Slot.Reason;
        if (status != SlotStatus.Ok)
        {
            return EvaluationResult.Refuse(EvaluationOutcome.NotUsable, reason);
        }

        var targetAction = longPress ? slot.LongPress!.Action : slot.Action;
        if (targetAction is not null)
        {
            var resolution = BindResolver.Resolve(bindings, targetAction);
            if (!resolution.IsBound)
            {
                // Actionable, and genuinely reassuring (ref/docs/editor.md,
                // task 3): a layout stores intent, never a resolved key, and
                // bindings are re-read on every panel request - so binding
                // this action in Elite heals the slot on the very next load,
                // with no edit, restart, or re-pair needed here.
                return EvaluationResult.Refuse(EvaluationOutcome.NotUsable, NotBoundInEliteAdvice);
            }

            // A long-press is never latched, and never a hold -
            // LongPressAction has no latch or hold of its own, and reading
            // the PRIMARY slot's flag here would make a long-press on a
            // latching or holding button hold a completely different key
            // down. This is the "unit of truth is the arm" case: the flag is
            // true of the slot, and false of this branch.
            return EvaluationResult.Fire(resolution.Chord!, targetAction, latch: !longPress && slot.Latch, hold: !longPress && slot.Hold);
        }

        var targetMacro = longPress ? slot.LongPress!.Macro : slot.Macro;
        if (targetMacro is not null)
        {
            // The Ok status already checked above only ever comes from
            // AnnotateMacro finding this id in MacroKnowledge.KnownIds and
            // not in DegradedIds (ref/docs/layouts.md) - firing it (looking
            // up its actual MacroDefinition and running it) is the caller's
            // job, same split as resolving a bound action's chord above.
            return EvaluationResult.FireMacro(targetMacro);
        }

        // Neither set despite an Ok status - LayoutValidator rejects this at
        // save time, but Evaluate has no validator pass to rely on for a
        // layout loaded from disk (see LayoutAnnotator's own remarks), so
        // this stays defensive rather than assuming the invariant holds
        // forever.
        return EvaluationResult.Refuse(EvaluationOutcome.NotUsable, "This slot is not usable.");
    }
}
