namespace LunaPanel.Core.GameState;

/// <summary>
/// Holds what the journal has reported: a monotonic record of which events
/// have been seen (so an <see cref="EdgeCondition"/> can be evaluated against
/// it) and the vessel type the journal most recently named. The sibling of
/// <see cref="GameStateStore"/> - it knows nothing about files or watchers,
/// is handed parsed events by <see cref="JournalTailer"/> via
/// <see cref="Record"/>, and never discovers anything itself.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The design decision: what "since time T" means, and why T is not
/// a time.</strong> An edge condition asks whether an event has been seen
/// since T. T here is a <see cref="JournalWatermark"/> the caller takes for
/// itself with <see cref="Mark"/> - a position in this store's own
/// monotonically increasing sequence. An edge is observable from the moment
/// it is recorded, to any caller whose mark predates it, <em>forever</em>. It
/// never expires. A caller resets by taking a new mark; nothing else resets
/// anything, and one caller's reading never consumes another's.
/// </para>
/// <para>
/// Three alternatives were considered and rejected, on 2026-09-07:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <strong>Observable only until read (consume-on-read).</strong> Rejected:
/// with two consumers the first read steals the event from the second, and
/// "read" is not a well-defined moment when one consumer is a macro gate and
/// another is a UI that polls. A shared record that any number of callers can
/// interrogate independently is the whole point.
/// </description>
/// </item>
/// <item>
/// <description>
/// <strong>A time window - observable for N seconds.</strong> Rejected, and
/// this is the one that measurement killed. LC17 timed <c>DockSRV</c> at 61 s
/// and 54 s from the keypress, and the commander - who has done it 214 times -
/// says the spread is wide and unbounded in principle, because it is a
/// physical process: the ship has to fly over and collect you. A window
/// shorter than that drops the exact signal this was built for, and no window
/// long enough can be justified, because there is no bound to size it
/// against. Any design whose correctness depends on an event being prompt is
/// wrong for this data.
/// </description>
/// </item>
/// <item>
/// <description>
/// <strong>Reset on a named event.</strong> Rejected as the primitive: it
/// bakes policy into the grammar, and whichever event it named would be wrong
/// for some consumer. It is not lost, though - it is expressible on top of
/// the watermark, since a consumer that wants "since the last
/// <c>FSDJump</c>" simply takes a new mark when it observes one.
/// </description>
/// </item>
/// </list>
/// <para>
/// Two consequences worth stating outright. First, <strong>an edge that fires
/// while nothing is listening is not lost</strong>: it is recorded, and any
/// caller whose mark predates it still sees it whenever it next looks. That
/// is what makes a macro able to press a key, take its mark <em>before</em>
/// the press, and then wait a minute or an hour for the completion. Second,
/// <strong>replaying history is harmless</strong>, which is why
/// <see cref="JournalTailer"/> can back-scan an entire journal at startup
/// without firing stale edges at anyone: every replayed event lands below any
/// mark taken afterwards.
/// </para>
/// <para>
/// The store is bounded regardless of session length: it keeps one sequence
/// number per event <em>name</em>, not a list of occurrences. It answers "has
/// this happened since T", never "how many times".
/// </para>
/// </remarks>
public sealed class JournalStateStore
{
    private readonly object _gate = new();

    /// <summary>The most recent sequence number at which each event was seen.</summary>
    private readonly Dictionary<string, long> _lastSeen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The most recent <see cref="JournalEvent"/> recorded for each event
    /// name - one entry per name, same bound as <see cref="_lastSeen"/>, not
    /// a history. Added so a caller (a macro's <c>waitForEdge</c> failure
    /// branch, above all) can read a property off the event that satisfied
    /// an edge - <c>DockingDenied</c>'s <c>Reason</c> is the motivating case -
    /// without this store growing into a general journal log.
    /// </summary>
    private readonly Dictionary<string, JournalEvent> _lastEventByName = new(StringComparer.OrdinalIgnoreCase);

    private long _sequence;
    private string? _currentVesselType;
    private string? _currentVesselTypeLocalised;
    private TaskCompletionSource _changeSignal = NewSignal();

    /// <summary>
    /// Raised after every recorded LIVE event - never for one recorded with
    /// <c>isBackScan: true</c>. Fired outside the internal lock, so a handler
    /// may call straight back into this store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Added 2026-09-07, alongside <see cref="Record"/>'s
    /// <c>isBackScan</c> parameter.</strong> Before this, nothing marked a
    /// back-scanned event as different from a live one at all - a permanent
    /// subscriber (<c>PanelTabTracker</c>'s <c>FSDJump</c>/<c>Embark</c>
    /// wiring in <c>ServerHostBuilder</c>) only happened to be safe from
    /// replayed history because it subscribes <em>after</em>
    /// <see cref="JournalTailer"/>'s constructor - which runs the whole
    /// back-scan synchronously - already returns. That protection was real
    /// but accidental: nothing stopped a future subscriber from attaching
    /// earlier and silently receiving hours-old events as if they had just
    /// happened. This flag makes the boundary an explicit contract instead of
    /// a registration-order coincidence - see <see cref="JournalTailer"/>'s
    /// remarks for how it threads the flag through.
    /// </para>
    /// <para>
    /// A back-scanned event still updates everything else unconditionally
    /// (the sequence, <see cref="HasSeenSince"/>, <see cref="LastEvent"/>,
    /// vessel type, and the internal wait signal <see cref="WaitForEdgeAsync"/>
    /// uses) - "replaying history is harmless" for a watermark-relative
    /// reader still holds exactly as before. Only this level-triggered
    /// notification is suppressed, because a notification-driven consumer
    /// (like the tab tracker) has no watermark to filter a replay against and
    /// would otherwise treat old history as something that just happened.
    /// </para>
    /// </remarks>
    public event Action<JournalEvent>? Changed;

    /// <summary>
    /// The vessel the journal last saw the commander <em>get into</em>,
    /// lower-cased - set by <c>LoadGame</c>, <c>LaunchVessel</c> and
    /// <c>LaunchSRV</c>, and cleared by <c>DockSRV</c>, which names the
    /// vessel they just got <em>out of</em>. <see langword="null"/> until an
    /// entry event has been seen, and again after every exit.
    ///
    /// <para><b>[2026-09-10, superseded: this was <c>LastKnownVesselType</c>,
    /// "the vessel type the journal most recently named", and <c>DockSRV</c>
    /// set it like the other two.]</b> That value outlived the vessel: after
    /// docking the Nomad it still said <c>lander01</c>, so the next boarding
    /// was decided by whatever the commander drove last time rather than by
    /// what they just climbed into. See
    /// <c>ref/docs/vessel-context.md</c>'s "The first boarding went to the
    /// wrong page". The rename is the point of the change and not
    /// cosmetic - the old name would now be false, and a reader who trusted
    /// it would put the stale read straight back.</para>
    /// </summary>
    public string? CurrentVesselType
    {
        get { lock (_gate) { return _currentVesselType; } }
    }

    /// <summary>The matching <c>*_Localised</c> display name, verbatim, or <see langword="null"/>.</summary>
    public string? CurrentVesselTypeLocalised
    {
        get { lock (_gate) { return _currentVesselTypeLocalised; } }
    }

    /// <summary>Takes a watermark at the current position - the <em>T</em> for a subsequent edge check.</summary>
    public JournalWatermark Mark()
    {
        lock (_gate) { return new JournalWatermark(_sequence); }
    }

    /// <summary>
    /// Whether <paramref name="eventName"/> has been recorded since
    /// <paramref name="since"/> was taken.
    /// </summary>
    public bool HasSeenSince(string eventName, JournalWatermark since)
    {
        ArgumentNullException.ThrowIfNull(eventName);

        lock (_gate)
        {
            return _lastSeen.TryGetValue(eventName, out var seenAt) && seenAt > since.Sequence;
        }
    }

    /// <summary>
    /// The most recent recorded event with this name, or <see langword="null"/>
    /// if it has never been seen. This is the whole event, not just "has it
    /// happened" - a caller inspecting a failure branch (e.g. reading
    /// <c>DockingDenied</c>'s <c>Reason</c>) needs the payload, not the
    /// boolean <see cref="HasSeenSince"/> answers. Unlike <see cref="HasSeenSince"/>,
    /// this takes no watermark: it always answers "most recent ever", since a
    /// caller that just observed <see cref="HasSeenSince"/> return true for
    /// this name wants the event that made it true, not a second, separate
    /// time filter.
    /// </summary>
    public JournalEvent? LastEvent(string eventName)
    {
        ArgumentNullException.ThrowIfNull(eventName);

        lock (_gate)
        {
            return _lastEventByName.TryGetValue(eventName, out var journalEvent) ? journalEvent : null;
        }
    }

    /// <summary>
    /// Records one parsed journal event. Events outside
    /// <see cref="JournalVocabulary"/> are ignored entirely - the journal is
    /// mostly events nothing here consumes, and an event the edge grammar
    /// cannot name is an event nobody can ask about, so keeping it would only
    /// grow the table.
    /// </summary>
    /// <param name="journalEvent">The parsed line.</param>
    /// <param name="isBackScan">
    /// <see langword="true"/> when this event came from <see cref="JournalTailer"/>'s
    /// startup back-scan rather than from something the game just wrote.
    /// Suppresses <see cref="Changed"/> only - see that event's remarks for
    /// why every other effect still applies unconditionally.
    /// </param>
    public void Record(JournalEvent journalEvent, bool isBackScan = false)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);

        if (!JournalVocabulary.IsKnown(journalEvent.EventName))
        {
            return;
        }

        lock (_gate)
        {
            _sequence++;
            _lastSeen[journalEvent.EventName] = _sequence;
            _lastEventByName[journalEvent.EventName] = journalEvent;
            ApplyVesselType(journalEvent);

            var previousSignal = _changeSignal;
            _changeSignal = NewSignal();
            previousSignal.TrySetResult();
        }

        if (!isBackScan)
        {
            Changed?.Invoke(journalEvent);
        }
    }

    /// <summary>
    /// Four events name a vessel, each under its own field name, and the
    /// routing is by <em>event</em> - never by field. <c>Loadout</c> also
    /// carries a <c>Ship</c> property, and it names the main ship rather than
    /// the vessel the commander is in, so a field-name-driven read would
    /// overwrite the SRV type at every session start.
    ///
    /// <para><b>Three of the four are entries and one is an exit, and that
    /// distinction is the whole method.</b> <c>LoadGame</c>,
    /// <c>LaunchVessel</c> and <c>LaunchSRV</c> say which vessel the
    /// commander just got into. <c>DockSRV</c> says which one they got out
    /// of, so it clears rather than sets - see
    /// <see cref="CurrentVesselType"/> for what recording it cost.</para>
    ///
    /// <para><b>The launch event's spelling depends on the vessel, which is
    /// not obvious and was the defect of 2026-09-10.</b> Counted over the
    /// commander's own journal history: <c>LaunchVessel</c> is written for
    /// <c>lander01</c> (the Nomad) and nothing else, 33 times;
    /// <c>LaunchSRV</c> is written only for <c>testbuggy</c> (the Scarab, 15
    /// times) and <c>combat_multicrew_srv_01</c> (the Scorpion, 8). Knowing
    /// only <c>LaunchVessel</c> therefore worked perfectly for the vessel
    /// this feature was designed in and was blind to the other two.
    /// <c>DockSRV</c>, by contrast, is written for all three.</para>
    ///
    /// The lower-casing is the casing trap, handled once, here.
    /// <c>LoadGame</c> writes <c>"Lander01"</c>; <c>LaunchVessel</c>,
    /// <c>LaunchSRV</c> and <c>DockSRV</c> write <c>"lander01"</c>. An
    /// ordinal comparison downstream works perfectly against the launch path
    /// - the path anyone would naturally write first - and fails only for a
    /// commander who logged in already inside their SRV.
    /// </summary>
    private void ApplyVesselType(JournalEvent journalEvent)
    {
        switch (journalEvent.EventName.ToUpperInvariant())
        {
            case "LOADGAME":
                EnterVessel(journalEvent, "Ship", "Ship_Localised");
                return;
            case "LAUNCHVESSEL":
                EnterVessel(journalEvent, "VesselType", "VesselType_Localised");
                return;
            case "LAUNCHSRV":
                EnterVessel(journalEvent, "SRVType", "SRVType_Localised");
                return;
            case "DOCKSRV":
                // The exit. Cleared whatever the line carries, because it is
                // the event's meaning that ends the vessel, not its payload.
                _currentVesselType = null;
                _currentVesselTypeLocalised = null;
                return;
            default:
                return;
        }
    }

    /// <summary>
    /// An entry event, under whichever field name that event uses. A line
    /// missing its type field leaves the current vessel alone rather than
    /// clearing it: the commander has still just got into something, and
    /// forgetting which would be worse than remembering.
    /// </summary>
    private void EnterVessel(JournalEvent journalEvent, string typeField, string localisedField)
    {
        var type = journalEvent.String(typeField);
        if (string.IsNullOrEmpty(type))
        {
            return;
        }

        _currentVesselType = type.ToLowerInvariant();
        _currentVesselTypeLocalised = journalEvent.String(localisedField);
    }

    /// <summary>
    /// Waits until <paramref name="condition"/> is satisfied relative to
    /// <paramref name="since"/>, or until <paramref name="cancellationToken"/>
    /// is cancelled.
    /// </summary>
    /// <remarks>
    /// <strong>There is deliberately no timeout overload.</strong>
    /// <see cref="GameStateStore.WaitForAsync"/> has one because a
    /// Status.json-backed condition is answered within a poll interval (LC6),
    /// so a second or two is a real bound. Journal events are not like that:
    /// LC17 timed <c>DockSRV</c> at 61 s and 54 s from the keypress, and the
    /// gap is the time a ship takes to fly over and collect the commander -
    /// wide, and unbounded in principle. A timeout here would be a guess
    /// wearing a bound's clothing, and worse, a caller that treated the
    /// timeout as "it failed" would re-press a contextual toggle and launch
    /// the vessel it was waiting to dock. Cancellation is how a caller gives
    /// up; nothing here decides that for them.
    /// </remarks>
    public async Task WaitForEdgeAsync(EdgeCondition condition, JournalWatermark since, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The change signal is captured BEFORE the condition is checked,
            // not after. The other order loses an event that lands in the gap
            // between the check and the capture: the waiter would then be
            // parked on a signal that has already been replaced, and since
            // this wait has no timeout to fall back on, that is a permanent
            // hang rather than a delay.
            Task changeTask;
            lock (_gate) { changeTask = _changeSignal.Task; }

            if (condition.Evaluate(this, since))
            {
                return;
            }

            await changeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
