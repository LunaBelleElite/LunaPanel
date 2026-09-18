using LunaPanel.Core.GameState;
using LunaPanel.Core.Latching;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Macros;
using CoreCatalogue = LunaPanel.Core.Catalogue.Catalogue;

namespace LunaPanel.Server.Http;

/// <summary>
/// <c>GET /api/panel/live</c>'s handler logic, kept free of any ASP.NET type -
/// same discipline as <see cref="PanelEndpoint"/>/<see cref="PressEndpoint"/>.
/// The actual server-sent-events streaming (subscribing to
/// <c>GameStateStore.Changed</c>, writing each event, cleaning up on
/// disconnect) lives in <see cref="Hosting.ServerHostBuilder"/>, since that
/// is inherently ASP.NET-shaped; this type only computes <em>what</em> to
/// send - the same split <see cref="PanelEndpoint"/> already draws between
/// pure response-building and the endpoint wiring around it.
/// </summary>
public static class PanelLiveEndpoint
{
    /// <param name="Lit">
    /// How engaged this slot's control currently is, serialized as its
    /// <see cref="SlotLitLevel"/> name ("Off"/"Partial"/"Full" - same
    /// convention as <c>PanelEndpoint.SlotDto.Status</c>).
    /// </param>
    public sealed record SlotLitDto(int Index, string Lit);

    /// <param name="GameRunning">
    /// The coarse connection state a client needs to say "Elite is not
    /// running" instead of showing a panel of unexplained dead buttons - the
    /// same diagnostic principle <c>POST /api/press</c>'s refusal reason
    /// already applies. <see langword="false"/> whenever no snapshot has
    /// arrived yet, not just when one says the game is closed.
    /// </param>
    /// <param name="SwitchToPage">
    /// The page index automatic vessel-context switching wants this device
    /// to move to, or <see langword="null"/> - which is almost always. This
    /// is an <b>edge, not a level</b>: it is non-null on exactly the one push
    /// caused by a context change (see
    /// <see cref="Core.Layouts.AutoPageSwitcher"/>), and null on every other
    /// push. Sending it as a level ("you should be on page 3") would drag a
    /// commander back the instant they tapped away, which is the single
    /// behaviour <c>ref/docs/vessel-context.md</c> exists to prevent.
    /// </param>
    /// <param name="Timing">
    /// The macro timing that was just saved, or <see langword="null"/> -
    /// which is almost always. An <b>edge, not a level</b>, exactly like
    /// <see cref="LiveState.SwitchToPage"/>: non-null on the one push caused
    /// by a <c>MacroTimingSettingsStore.Saved</c>, and null on every other
    /// push, so a client can treat its presence as "this just changed"
    /// rather than diffing it against what it already had.
    ///
    /// <b>Never computed from anything this connection cached.</b> The value
    /// here is the argument <c>MacroTimingSettingsStore.Saved</c> carried -
    /// a payload built from a copy loaded at connect time is the exact
    /// staleness that stopped a latched button ever lighting
    /// (<c>ref/docs/lit-state.md</c>'s "The glow that never arrived").
    /// </param>
    /// <param name="BindingsChanged">
    /// <see langword="true"/> on the one push caused by
    /// <c>Bindings.BindsFileWatcher.Changed</c> - a rebind made in Elite to
    /// the bindings file this server currently reads through - and
    /// <see langword="null"/> on every other push, exactly the same edge
    /// convention as <see cref="SwitchToPage"/> and <see cref="Timing"/>.
    /// Deliberately carries no bindings content of its own: it is a bare
    /// "your bindings changed, re-fetch" signal, and the client's existing
    /// <c>GET /api/panel</c> re-fetch path is the one correct, fully
    /// annotated place that content ever comes from - see
    /// <c>BindsFileWatcher</c>'s own remarks for why threading bindings into
    /// this payload instead would only duplicate that pipeline.
    /// </param>
    /// <param name="LayoutChanged">
    /// <see langword="true"/> on the one push caused by <c>OnLayoutSaved</c>
    /// (<see cref="Hosting.ServerHostBuilder"/>) reaching this device - an
    /// edit made live from the PC to a device's own layout while its live
    /// channel is open - and <see langword="null"/> on every other push,
    /// exactly the same edge convention as <see cref="SwitchToPage"/>,
    /// <see cref="Timing"/> and <see cref="BindingsChanged"/>. Deliberately
    /// carries no layout content of its own: it is a bare "your layout
    /// changed, re-fetch" signal, same reasoning as
    /// <see cref="BindingsChanged"/> - the client's existing
    /// <c>GET /api/panel</c> re-fetch path is the one place that content
    /// ever comes from.
    /// </param>
    /// <param name="MacroFinished">
    /// The outcome of a macro run this device started, on the one push
    /// caused by that run ending (<c>OnMacroFinished</c> in
    /// <see cref="Hosting.ServerHostBuilder"/>, fed by
    /// <see cref="Input.MacroPressOutcomeBroadcaster"/>), and
    /// <see langword="null"/> on every other push - the same edge convention
    /// as the four fields above, and the fifth instance of it (2026-09-17,
    /// O28). This is where the result <c>POST /api/press</c> used to carry
    /// synchronously now arrives: the press answers <c>Started</c> at once,
    /// and everything it used to wait for - outcome, reason,
    /// <c>failedStepIndex</c>, the per-step read-back - comes down here
    /// instead, keyed by <see cref="MacroFinishedDto.MacroId"/> because the
    /// runner already guarantees at most one run per id.
    /// </param>
    public sealed record LiveState(
        bool GameRunning,
        IReadOnlyList<SlotLitDto> Slots,
        int? SwitchToPage = null,
        TimingDto? Timing = null,
        bool? BindingsChanged = null,
        bool? LayoutChanged = null,
        MacroFinishedDto? MacroFinished = null);

    /// <summary>
    /// One finished macro run, as the live channel carries it. Every field
    /// after <see cref="MacroId"/> is exactly what the press response's
    /// macro branch used to carry under the same name - <c>fired</c>,
    /// <c>outcome</c>, <c>reason</c>, <c>failedStepIndex</c>, <c>steps</c> -
    /// so the client's existing run-result sheet consumes this object
    /// unchanged.
    /// </summary>
    public sealed record MacroFinishedDto(
        string MacroId,
        bool Fired,
        string Outcome,
        string? Reason,
        int? FailedStepIndex,
        IReadOnlyList<MacroStepDto> Steps);

    /// <param name="ElapsedMs">Whole-or-fractional milliseconds, the same unit the press response's <c>steps[].elapsedMs</c> used.</param>
    public sealed record MacroStepDto(int StepIndex, string StepKind, string Outcome, double ElapsedMs);

    /// <summary>
    /// The wire shape of a finished run - the same projection
    /// <c>POST /api/press</c>'s macro branch applied to
    /// <see cref="Input.MacroPresser.MacroPressResult"/> before O28 moved the
    /// outcome onto this channel, kept as one function so the two can never
    /// drift.
    /// </summary>
    public static MacroFinishedDto MacroFinishedOf(string macroId, Input.MacroPresser.MacroPressResult result)
    {
        ArgumentNullException.ThrowIfNull(macroId);
        ArgumentNullException.ThrowIfNull(result);

        return new MacroFinishedDto(
            macroId,
            result.Fired,
            result.Outcome,
            result.Reason,
            result.FailedStepIndex,
            result.Steps.Select(s => new MacroStepDto(s.StepIndex, s.StepKind, s.Outcome.ToString(), s.Elapsed.TotalMilliseconds)).ToList());
    }

    /// <param name="HoldMs">
    /// Whole milliseconds, the same units and the same names
    /// <c>GET /api/macro-timing</c>'s own response uses - so the client
    /// applies a pushed value through the same code path it applies a
    /// fetched one.
    /// </param>
    public sealed record TimingDto(int HoldMs, int InterPressGapMs);

    /// <summary>
    /// The wire shape of a <see cref="MacroTimingSettings"/>. The measured
    /// minimums (<c>holdMinMs</c>/<c>interPressGapMinMs</c>) are deliberately
    /// NOT repeated here: they are compile-time constants that no save can
    /// change, and <c>GET /api/macro-timing</c> already hands them to any
    /// client that has opened the Timing pane at all.
    /// </summary>
    public static TimingDto TimingOf(MacroTimingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new TimingDto(
            (int)settings.HoldDuration.TotalMilliseconds,
            (int)settings.InterPressGap.TotalMilliseconds);
    }

    private static readonly IReadOnlySet<string> EmptyLatchedActions = new HashSet<string>(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> EmptyRunningMacroIds = new HashSet<string>(StringComparer.Ordinal);

    public enum BuildOutcome
    {
        Ok,
        NoSuchPage,
        UnknownTemplate,
    }

    public sealed record BuildResult(BuildOutcome Outcome, LiveState? State)
    {
        public static BuildResult Ok(LiveState state) => new(BuildOutcome.Ok, state);
        public static BuildResult Fail(BuildOutcome outcome) => new(outcome, null);
    }

    /// <summary>
    /// Computes the live-state payload for one page - always page 0 today,
    /// same limit as <see cref="PanelEndpoint.BuildResponse"/>. Only active
    /// slots (<see cref="LayoutPage.Slots"/>) are ever reported, same as the
    /// panel response - a parked slot has no control to light up.
    /// </summary>
    public static BuildResult BuildState(
        Layout layout,
        int pageIndex,
        CoreCatalogue catalogue,
        StatusSnapshot? snapshot,
        IReadOnlySet<string>? latchedActions = null,
        IReadOnlySet<string>? runningMacroIds = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(catalogue);

        if (pageIndex < 0 || pageIndex >= layout.Pages.Count)
        {
            return BuildResult.Fail(BuildOutcome.NoSuchPage);
        }

        var page = layout.Pages[pageIndex];
        if (!Templates.TryGet(page.TemplateId, out _))
        {
            return BuildResult.Fail(BuildOutcome.UnknownTemplate);
        }

        // The same two overrides GET /api/panel applies, in the same order,
        // over the same resolver output - see PanelEndpoint for why each is
        // one shared function rather than the same rule written twice. The
        // order between them does not matter: both only ever raise to Full,
        // and no slot can be both a latched action and a running macro (a
        // slot names one or the other).
        var slots = RunningMacroLit
            .Apply(
                LatchedLit.Apply(
                    LitStateResolver.Resolve(page, catalogue, snapshot),
                    page,
                    latchedActions ?? EmptyLatchedActions),
                page,
                runningMacroIds ?? EmptyRunningMacroIds)
            .Select(s => new SlotLitDto(s.Index, s.Level.ToString()))
            .ToList();

        return BuildResult.Ok(new LiveState(snapshot?.GameRunning ?? false, slots));
    }

    /// <summary>
    /// Value equality over <see cref="LiveState"/>. A record's generated
    /// <c>Equals</c> compares <see cref="LiveState.Slots"/> by reference (a
    /// plain <see cref="List{T}"/> doesn't override <c>Equals</c>), so two
    /// independently-built states with identical content would never compare
    /// equal without this - and that comparison is exactly what lets the live
    /// channel skip a push when nothing a button cares about changed, most
    /// importantly Status.json's ~11.8s idle heartbeat (see LC6 in
    /// <c>tests/notes/live-checks.md</c>), which rewrites the file with
    /// unchanged content and would otherwise flood the channel with no-op
    /// events.
    /// </summary>
    public static bool StatesEqual(LiveState a, LiveState b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        return a.GameRunning == b.GameRunning && a.Slots.SequenceEqual(b.Slots);
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is worth writing to the channel
    /// at all, given what was last sent.
    ///
    /// <para><b>A push carrying a page switch is never deduped.</b>
    /// <see cref="StatesEqual"/> compares only what a button looks like, and
    /// a context change very often changes nothing about that - getting into
    /// the SRV does not alter the lit state of a page whose controls are all
    /// off. Running the switch through the lit-state comparison would
    /// therefore drop precisely the pushes this feature exists to send. The
    /// dedupe itself is untouched and still does its own job (LC6's ~11.8s
    /// idle heartbeat rewrites Status.json with unchanged content); this is
    /// a separate question asked alongside it, not a change to it.</para>
    ///
    /// <para><b>A push carrying a timing change is never deduped either</b>
    /// (2026-09-10), for exactly the same reason and with exactly the same
    /// care: saving a new hold duration on the PC changes nothing about how
    /// any button looks, so <see cref="StatesEqual"/> finds the two states
    /// identical and would drop every timing change there is.</para>
    ///
    /// <para><b>A push carrying a bindings-changed signal is never deduped
    /// either</b> (2026-09-10), for the same reason again: a rebind very
    /// often changes nothing about how any button currently looks (the lit
    /// state a snapshot drives is independent of which key an action is
    /// bound to), so <see cref="StatesEqual"/> would find the two states
    /// identical and silently drop the one push this whole feature exists to
    /// deliver.</para>
    ///
    /// <para><b>A push carrying a layout-changed signal is never deduped
    /// either</b> (2026-09-13), for the same reason yet again: editing a
    /// device's layout live from the PC very often changes nothing about how
    /// any button currently looks (a relabel, a reorder, a slot reassigned to
    /// another action with the same lit level), so <see cref="StatesEqual"/>
    /// would find the two states identical and silently drop the one push
    /// this fix exists to deliver - the exact bug it closes ("the panel
    /// stays stale until reloaded").</para>
    ///
    /// <para><b>A push carrying a finished macro's outcome is never deduped
    /// either</b> (2026-09-17, O28), and this one is guaranteed to look
    /// identical rather than merely likely to: by the time a run ends the
    /// runner has already removed its id and the dark push has gone out, so
    /// the finished push's slots are byte-for-byte the last ones sent. Every
    /// outcome, without exception, would be dropped.</para>
    /// </summary>
    public static bool ShouldPush(LiveState candidate, LiveState lastSent)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(lastSent);

        return candidate.SwitchToPage is not null
            || candidate.Timing is not null
            || candidate.BindingsChanged is not null
            || candidate.LayoutChanged is not null
            || candidate.MacroFinished is not null
            || !StatesEqual(candidate, lastSent);
    }
}
