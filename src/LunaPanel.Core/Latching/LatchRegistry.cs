using LunaPanel.Core.Bindings;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Macros;

namespace LunaPanel.Core.Latching;

/// <summary>
/// Every key LunaPanel is currently holding down on purpose, and the five
/// rules that let go of it (<c>ref/docs/latching-keys.md</c>).
///
/// <b>Why this type is where the whole feature lives.</b> The press is
/// trivial - <see cref="IKeyInjector"/> already has exactly the two
/// primitives a latch needs, <c>KeyDown</c> and <c>KeyUp</c>, and nothing new
/// was added at the injection layer. What is not trivial is that every other
/// failure in this product is a keystroke that did not happen, and this one
/// is a keystroke that will not stop: a tablet that sleeps, a dropped wifi
/// connection or a server that exits would otherwise leave the key held in
/// the game with nothing left to release it, and the commander's ship keeps
/// firing, unattended, until they work out why. So this class is mostly
/// bookkeeping in service of release, and every public member below exists
/// for one of the five rules.
///
/// <b>A latch is identified by (device, action name), never by (page, slot).</b>
/// The latch is a property of the key, not of where the button happened to
/// be - so it survives the page moving under it (vessel-context switching
/// can now do that on its own, <c>ref/docs/vessel-context.md</c>), and a
/// second slot naming the same action releases it rather than pressing an
/// already-held key a second time. The <em>chord</em> that actually went
/// down is stored alongside, and that exact chord is what gets released -
/// never a chord re-resolved later, which a bindings change mid-latch would
/// otherwise turn into a release of the wrong key while the real one stayed
/// down.
///
/// <b>Deliberately synchronous, with no settle gap between a chord's
/// modifiers and its main key</b>, unlike <c>MacroRunner.PressChordOnceAsync</c>
/// and <see cref="MacroTimingDefaults.ModifierSettleGap"/>. That gap exists
/// because Elite can miss the modified interpretation of a chord whose main
/// key arrives before the modifiers have settled - a real risk for a press
/// that is over in 150ms. A latch is held for as long as the commander
/// wants it, so DirectInput sees both keys down on the very next frame it
/// polls either way. Staying synchronous is what lets a shutdown hook and a
/// dropped connection's cleanup both release safely without an async
/// continuation that may never get to run.
///
/// Thread-safe: a press (an ASP.NET request thread), a sweep (the
/// <see cref="LatchSweeper"/> loop), a live channel closing, and the
/// shutdown hook can all reach this at once.
/// </summary>
public sealed class LatchRegistry
{
    private const string LogCategory = "Latch";

    /// <param name="ChannelId">
    /// The device's live channel at the moment this latch was made - rule 2
    /// releases exactly the latches carrying the id of the channel that
    /// closed, never simply "this device's". <see cref="Guid.Empty"/> when
    /// no channel was open, which is not refused (this project never blocks
    /// an action for uncertainty) but is a latch rule 2 cannot cover; the
    /// backstop covers it instead.
    /// </param>
    /// <param name="ExpiresAt">
    /// When rule 5 releases this latch. Computed from THIS latch's own press
    /// time, so a second key latched a minute later gets its own full
    /// <see cref="LatchDefaults.Backstop"/> rather than inheriting the first
    /// one's deadline.
    /// </param>
    public sealed record Held(string DeviceId, string Action, ResolvedChord Chord, Guid ChannelId, DateTimeOffset ExpiresAt);

    public enum ToggleOutcome
    {
        /// <summary>The key is now held down and will stay down until one of the five rules releases it.</summary>
        Latched,

        /// <summary>Rule 1 - the key was already held and has now been released.</summary>
        Released,
    }

    private readonly IKeyInjector _injector;
    private readonly TimeProvider _clock;
    private readonly IDiagnosticLog _log;

    private readonly object _gate = new();

    // Keyed by (device, action) - see this class's own remarks for why that
    // is the identity rather than (page, slot).
    private readonly Dictionary<(string DeviceId, string Action), Held> _held = new();

    // The live channel currently open for each device, so a press does not
    // have to be told which one it belongs to. Overwritten on every open,
    // and only removed by a close that names the id still current - a stale
    // close must not unregister the channel that replaced it.
    private readonly Dictionary<string, Guid> _channels = new(StringComparer.Ordinal);

    public LatchRegistry(IKeyInjector injector, TimeProvider clock, IDiagnosticLog log)
    {
        _injector = injector ?? throw new ArgumentNullException(nameof(injector));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Raised after any change to what is held - a latch, or a release by
    /// any of the five rules. The live channel subscribes so a latched
    /// button lights up (and goes dark again) immediately rather than on the
    /// next unrelated game-state tick. Never raised for a call that changed
    /// nothing, so the channel's own dedupe is not handed pointless work.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Whether anything at all is currently held. <see cref="LatchSweeper"/>
    /// reads this to skip its foreground check entirely on an idle server,
    /// so polling rule 3 costs no Win32 call when nothing is latched.
    /// </summary>
    public bool Any
    {
        get
        {
            lock (_gate)
            {
                return _held.Count > 0;
            }
        }
    }

    /// <summary>
    /// Registers a newly-opened live channel for a device and returns its
    /// id, which the caller hands back to <see cref="CloseChannel"/> when
    /// the stream ends.
    /// </summary>
    public Guid OpenChannel(string deviceId)
    {
        ArgumentNullException.ThrowIfNull(deviceId);

        var id = Guid.NewGuid();
        lock (_gate)
        {
            _channels[deviceId] = id;
        }

        return id;
    }

    /// <summary>
    /// Rule 2 - the device's live channel dropped (screen sleep, wifi loss,
    /// the tab closing, or the client reopening the stream for a different
    /// page). Releases exactly the latches made under <paramref name="channelId"/>
    /// and returns how many. A close naming a channel that has already been
    /// replaced releases nothing and leaves the current channel registered,
    /// so a page change cannot drop a latch made immediately after it.
    /// </summary>
    public int CloseChannel(Guid channelId)
    {
        return ReleaseWhere(h => h.ChannelId == channelId, LatchRelease.ChannelClosed, () =>
        {
            foreach (var device in _channels.Where(kv => kv.Value == channelId).Select(kv => kv.Key).ToList())
            {
                _channels.Remove(device);
            }
        });
    }

    public bool IsLatched(string deviceId, string action)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentNullException.ThrowIfNull(action);

        lock (_gate)
        {
            return _held.ContainsKey((deviceId, action));
        }
    }

    /// <summary>
    /// Which of this device's actions are currently held - what the panel
    /// needs to draw a latched button as latched. Another device's latches
    /// are never reported: a held key belongs to the panel that held it, and
    /// lighting it up on a second device would invite a commander to tap a
    /// button that would not release anything.
    /// </summary>
    public IReadOnlySet<string> LatchedActions(string deviceId)
    {
        ArgumentNullException.ThrowIfNull(deviceId);

        lock (_gate)
        {
            return _held.Keys
                .Where(k => string.Equals(k.DeviceId, deviceId, StringComparison.Ordinal))
                .Select(k => k.Action)
                .ToHashSet(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The feature itself: the first tap presses the chord and leaves it
    /// down; the next tap (rule 1) releases it. The caller is responsible
    /// for having established that a keystroke would actually be accepted
    /// before latching - see <c>ServerHostBuilder</c>'s press route, which
    /// checks the injection guard first so a refused <c>KeyDown</c> can never
    /// leave a button lit over a key that never went down. A RELEASE is
    /// never gated that way, deliberately: refusing a release is exactly the
    /// stuck-modifier defect O8 closed (<c>ref/docs/injection.md</c>), and
    /// <c>Win32KeyInjector.KeyUp</c> bypasses the guard for the same reason.
    /// </summary>
    public ToggleOutcome Toggle(string deviceId, string action, ResolvedChord chord)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(chord);

        ToggleOutcome outcome;

        lock (_gate)
        {
            var key = (deviceId, action);
            if (_held.TryGetValue(key, out var existing))
            {
                _held.Remove(key);
                ReleaseChordLocked(existing, LatchRelease.TappedAgain);
                outcome = ToggleOutcome.Released;
            }
            else
            {
                var now = _clock.GetUtcNow();
                _channels.TryGetValue(deviceId, out var channelId);
                var held = new Held(deviceId, action, chord, channelId, now + LatchDefaults.Backstop);

                foreach (var modifier in chord.ModifierKeys)
                {
                    _injector.KeyDown(modifier);
                }

                _injector.KeyDown(chord.MainKey);
                _held[key] = held;

                _log.Info(
                    LogCategory,
                    $"Latched {action} ({chord.DisplayText}) - held until tapped again.",
                    $"deviceId={deviceId}; backstop={LatchDefaults.Backstop.TotalSeconds:0}s; channel={(channelId == Guid.Empty ? "(none)" : "open")}");

                outcome = ToggleOutcome.Latched;
            }
        }

        Changed?.Invoke();
        return outcome;
    }

    /// <summary>
    /// The hold gesture's press (<c>LayoutSlot.Hold</c>,
    /// <c>ref/docs/latching-keys.md</c>'s hold-to-thrust extension) -
    /// idempotent, unlike <see cref="Toggle"/>: a duplicate pointerdown (a
    /// second touch event before the first's response lands) must not
    /// re-send the key-down or, worse, be read as "already held, release
    /// it." Already held for this device+action is a no-op; otherwise this
    /// runs the exact same key-down sequence <see cref="Toggle"/>'s press
    /// branch does and registers the held state in the same dictionary, so
    /// the four safety rules (<see cref="CloseChannel"/>,
    /// <see cref="Sweep"/>, <see cref="ReleaseAll"/>) release a hold exactly
    /// as they already release a latch - nothing about them changes for
    /// this call to exist. The caller is responsible for having checked the
    /// injection guard first, exactly as <c>ServerHostBuilder</c>'s press
    /// route already does before <see cref="Toggle"/>'s press branch.
    /// </summary>
    public void Hold(string deviceId, string action, ResolvedChord chord)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(chord);

        var changed = false;

        lock (_gate)
        {
            var key = (deviceId, action);
            if (!_held.ContainsKey(key))
            {
                var now = _clock.GetUtcNow();
                _channels.TryGetValue(deviceId, out var channelId);
                var held = new Held(deviceId, action, chord, channelId, now + LatchDefaults.Backstop);

                foreach (var modifier in chord.ModifierKeys)
                {
                    _injector.KeyDown(modifier);
                }

                _injector.KeyDown(chord.MainKey);
                _held[key] = held;

                _log.Info(
                    LogCategory,
                    $"Held {action} ({chord.DisplayText}) - held until released.",
                    $"deviceId={deviceId}; backstop={LatchDefaults.Backstop.TotalSeconds:0}s; channel={(channelId == Guid.Empty ? "(none)" : "open")}");

                changed = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// The hold gesture's release - idempotent, the pointerup counterpart to
    /// <see cref="Hold"/>. Not held for this device+action is a no-op
    /// (a pointerup with no matching pointerdown, e.g. a duplicate event,
    /// must never send a stray key-up or release someone else's latch);
    /// otherwise this runs the exact same key-up sequence
    /// <see cref="Toggle"/>'s release branch does, logged under
    /// <see cref="LatchRelease.HoldReleased"/> rather than
    /// <see cref="LatchRelease.TappedAgain"/> - a pointerup ending a hold is
    /// not the same gesture as a second tap toggling a latch off, even
    /// though both end in the same key going up. Never gated on the
    /// injection guard, matching every other release in this class: refusing
    /// a release is exactly the stuck-modifier defect O8 closed.
    /// </summary>
    public void Release(string deviceId, string action)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentNullException.ThrowIfNull(action);

        var released = false;

        lock (_gate)
        {
            var key = (deviceId, action);
            if (_held.TryGetValue(key, out var existing))
            {
                _held.Remove(key);
                ReleaseChordLocked(existing, LatchRelease.HoldReleased);
                released = true;
            }
        }

        if (released)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Rules 3 and 5, the two nothing pushes at us. Called on a timer by
    /// <see cref="LatchSweeper"/>; returns how many latches it released.
    ///
    /// Rule 3 (<paramref name="eliteForeground"/> false) releases
    /// everything: a key already down stays down at the OS level whatever
    /// the injection guard does about new keystrokes, so without this rule a
    /// held key lands in whatever the commander alt-tabbed to. It also
    /// settles the one question research could not: whether Elite keeps
    /// treating a key as held ACROSS an alt-tab is engine-dependent and
    /// nobody has written it down - and this rule makes it moot, because
    /// there is no latch left by the time it could matter. That is why the
    /// rule earns its place rather than being belt-and-braces.
    ///
    /// Rule 5 releases only what has actually reached its own
    /// <see cref="Held.ExpiresAt"/>.
    /// </summary>
    public int Sweep(bool eliteForeground, DateTimeOffset now)
    {
        if (!eliteForeground)
        {
            return ReleaseWhere(_ => true, LatchRelease.ForegroundLost);
        }

        return ReleaseWhere(h => h.ExpiresAt <= now, LatchRelease.Backstop);
    }

    /// <summary>
    /// Rule 4 - release everything, whoever holds it. Called from the
    /// server's shutdown hooks; safe to call twice (the second call releases
    /// nothing and sends nothing), because an unclean exit path and a clean
    /// one can both fire.
    /// </summary>
    public int ReleaseAll(LatchRelease reason) => ReleaseWhere(_ => true, reason);

    private int ReleaseWhere(Func<Held, bool> predicate, LatchRelease reason, Action? alsoUnderLock = null)
    {
        int released;

        lock (_gate)
        {
            alsoUnderLock?.Invoke();

            var doomed = _held.Where(kv => predicate(kv.Value)).ToList();
            foreach (var entry in doomed)
            {
                _held.Remove(entry.Key);
                ReleaseChordLocked(entry.Value, reason);
            }

            released = doomed.Count;
        }

        if (released > 0)
        {
            Changed?.Invoke();
        }

        return released;
    }

    /// <summary>
    /// Sends the actual releases for one held chord: main key first, then
    /// modifiers in reverse order - the same order <c>MacroRunner</c> and
    /// <c>Win32KeyInjector.ReleaseHeldKeys</c> both already use, so nothing
    /// in this project releases a chord two different ways.
    /// </summary>
    private void ReleaseChordLocked(Held held, LatchRelease reason)
    {
        _injector.KeyUp(held.Chord.MainKey);

        for (var i = held.Chord.ModifierKeys.Count - 1; i >= 0; i--)
        {
            _injector.KeyUp(held.Chord.ModifierKeys[i]);
        }

        // The reason is in the MESSAGE, not only the detail: a commander
        // reading the log after a key let go on its own has to be able to
        // tell which of the five rules did it. "Something released it" makes
        // a real defect indistinguishable from the backstop doing its job.
        _log.Info(
            LogCategory,
            $"Released {held.Action} ({held.Chord.DisplayText}) - {reason}.",
            $"deviceId={held.DeviceId}");
    }
}
