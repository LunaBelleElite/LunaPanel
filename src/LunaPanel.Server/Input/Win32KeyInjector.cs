using System.Runtime.Versioning;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Input;
using LunaPanel.Core.Macros;

namespace LunaPanel.Server.Input;

/// <summary>
/// The real <see cref="IKeyInjector"/> - every <c>KeyDown</c>/<c>KeyUp</c>
/// Elite Dangerous ever receives from LunaPanel passes through here,
/// whether it came from <c>MacroRunner</c>'s chord choreography or (in
/// future) a single direct tap, because both call sites only ever hold this
/// interface. That is deliberate: it is the one choke point where the
/// foreground guard (see <see cref="InjectionGuard"/>) can protect every
/// caller at once, rather than being duplicated - or, worse, forgotten - at
/// each call site. Without it, a macro or a tap fired while some other
/// window has focus types into whatever does.
///
/// Decision (<see cref="InjectionGuard.Evaluate"/>) and struct-shaping
/// (<see cref="InputStructBuilder.BuildKeyInput"/>) are both pure and
/// tested directly. This class is the thin, largely-untestable glue between
/// them and two injected seams - <c>captureForegroundContext</c> and
/// <c>sendInput</c> - that a test replaces with fakes so every other line
/// here (the guard call, the log lines, the struct that would have been
/// sent) still runs for real without ever reaching an actual Win32 API. See
/// <c>tests/LunaPanel.Tests/Injection/</c> for how.
///
/// <c>KeyUp</c> bypasses the foreground/integrity guard entirely - a release
/// is always sent (see <see cref="Inject"/>). This was a deliberate reversal
/// of an earlier decision to apply the guard identically to both directions
/// - see <c>ref/docs/injection.md</c>'s "Key-up bypasses the foreground
/// guard" section and <c>tests/notes/open-items.md</c>'s O8 for why: a
/// chord takes long enough (~250ms) that focus can leave Elite between a
/// modifier's <c>KeyDown</c> and its <c>KeyUp</c>, and refusing the release
/// in that window leaves Elite believing a modifier is still held - a worse
/// failure than a stray key-up landing on whatever window now has focus
/// (harmless, since nothing acts on a key-up without a matching key-down).
///
/// This class also tracks which keys it has actually sent as "down" and not
/// yet released (<see cref="_heldKeys"/>), purely so a <c>KeyDown</c>
/// refused by the guard can immediately release whatever is already held -
/// otherwise the fix above only helps once <em>something else</em> (e.g.
/// <c>MacroRunner</c>'s own end-of-chord choreography) gets around to
/// calling <c>KeyUp</c> for those keys.
/// </summary>
public sealed class Win32KeyInjector : IKeyInjector
{
    private readonly IDiagnosticLog _log;
    private readonly Func<bool> _diagnosticModeEnabled;
    private readonly Func<ForegroundContext> _captureForegroundContext;
    private readonly Func<NativeMethods.INPUT[], (uint Inserted, int Win32Error)> _sendInput;

    // Scan codes this instance has actually sent as a successful KeyDown and
    // not yet released, in the order they went down. Only ever appended to
    // by a guard-passing, successfully-sent KeyDown, and only ever removed
    // by a matching KeyUp (normal release) or by ReleaseHeldKeys (cleanup
    // after a refused KeyDown) - see both for why this is "what was actually
    // sent", never what a caller merely asked for.
    private readonly List<ScancodeInfo> _heldKeys = new();

    /// <summary>
    /// Production convenience constructor - wires the real foreground
    /// inspector and the real <c>SendInput</c> call. This is the only
    /// member of this class that touches Windows-only code (both
    /// dependencies it wires are themselves <see cref="SupportedOSPlatformAttribute"/>-marked),
    /// which is why it alone carries the attribute rather than the whole
    /// class - see the other constructor, which every test uses instead.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public Win32KeyInjector(IDiagnosticLog log, Func<bool> diagnosticModeEnabled)
        : this(log, diagnosticModeEnabled, Win32ForegroundInspector.Capture, Win32NativeInputSender.Send)
    {
    }

    /// <summary>
    /// Test seam constructor. Neither <paramref name="captureForegroundContext"/>
    /// nor <paramref name="sendInput"/> is required to call into Win32 - a
    /// test supplies fakes for both, which is what lets this class's own
    /// guard/logging/struct-building logic run for real while guaranteeing
    /// no actual keystroke is ever sent.
    /// </summary>
    public Win32KeyInjector(
        IDiagnosticLog log,
        Func<bool> diagnosticModeEnabled,
        Func<ForegroundContext> captureForegroundContext,
        Func<NativeMethods.INPUT[], (uint Inserted, int Win32Error)> sendInput)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _diagnosticModeEnabled = diagnosticModeEnabled ?? throw new ArgumentNullException(nameof(diagnosticModeEnabled));
        _captureForegroundContext = captureForegroundContext ?? throw new ArgumentNullException(nameof(captureForegroundContext));
        _sendInput = sendInput ?? throw new ArgumentNullException(nameof(sendInput));
    }

    public void KeyDown(ScancodeInfo key) => Inject(key, keyUp: false);

    public void KeyUp(ScancodeInfo key) => Inject(key, keyUp: true);

    /// <summary>
    /// Same as <see cref="KeyDown"/>, but returns the <see cref="InjectionGuard"/>
    /// verdict for this specific attempt - <see cref="IKeyInjector.KeyDown"/>
    /// (and <see cref="KeyDown"/> above) discard exactly this same value. A
    /// caller that only needs "was it sent" uses <see cref="KeyDown"/>; a
    /// caller that needs to explain a refusal to the person who pressed the
    /// button (currently <c>POST /api/press</c> - see <c>ref/docs/injection.md</c>)
    /// uses this instead. Exists on this concrete type rather than on
    /// <see cref="IKeyInjector"/> itself, since that interface is shared with
    /// <c>MacroRunner</c>'s chord choreography, which has no use for it.
    /// </summary>
    public InjectionAttemptResult KeyDownWithOutcome(ScancodeInfo key) => Inject(key, keyUp: false);

    /// <summary>
    /// Evaluates the foreground/integrity guard without sending anything -
    /// captures a fresh <see cref="ForegroundContext"/> and runs it through
    /// <see cref="InjectionGuard.Evaluate"/>, exactly what <see cref="Inject"/>
    /// does before a <c>KeyDown</c>, but stops there. Exists for a caller
    /// that must refuse an entire multi-step action (a macro - see
    /// <see cref="MacroPresser"/>) up front, with the same verdict a single
    /// <c>KeyDown</c> would reach, rather than discovering the refusal only
    /// after some of its keys have already silently gone nowhere -
    /// <c>MacroRunner</c>'s <c>IKeyInjector.KeyDown</c>/<c>KeyUp</c> are both
    /// <see langword="void"/> and cannot observe a per-key refusal, same
    /// reasoning as <see cref="KeyDownWithOutcome"/> one level up. Logs
    /// nothing itself - nothing was actually attempted yet, so the caller
    /// decides how (and whether) to report a refusal found this way.
    /// </summary>
    public InjectionAttemptResult CheckGuard()
    {
        var context = _captureForegroundContext();
        return InjectionGuard.Evaluate(context);
    }

    private InjectionAttemptResult Inject(ScancodeInfo key, bool keyUp)
    {
        if (keyUp)
        {
            // A release always bypasses the guard - see this class's own
            // remarks and ref/docs/injection.md. No foreground context is
            // captured at all, since nothing here uses it. There is no real
            // "decision" to report for a release (the guard never runs), so
            // this is a canned Sent - callers of KeyDownWithOutcome only ever
            // call it for a KeyDown, never a KeyUp.
            SendKey(key, keyUp: true, context: null);
            RemoveHeld(key);
            return new InjectionAttemptResult(InjectionOutcome.Sent, "Sent.");
        }

        var context = _captureForegroundContext();
        var decision = InjectionGuard.Evaluate(context);

        if (decision.Outcome != InjectionOutcome.Sent)
        {
            // Failures are always logged, regardless of diagnostic mode -
            // this is the log line that explains a dead button.
            _log.Warn(
                "Injection",
                $"KeyDown scan=0x{key.ScanCode:X2} ext={key.IsExtended} refused: {decision.Outcome}",
                $"reason={decision.Reason}; foreground={context.ForegroundProcessName ?? "(none)"}");

            ReleaseHeldKeys();
            return decision;
        }

        if (SendKey(key, keyUp: false, context))
        {
            _heldKeys.Add(key);
        }

        return decision;
    }

    /// <summary>
    /// Builds and sends one key event, logging a real `SendInput` failure
    /// (distinct from a guard refusal - the guard already passed by the time
    /// this runs) and, when diagnostic mode is on, a successful send.
    /// Returns whether the OS actually accepted it (`inserted != 0`) - the
    /// caller uses that to decide whether the key is now genuinely held.
    /// </summary>
    private bool SendKey(ScancodeInfo key, bool keyUp, ForegroundContext? context)
    {
        var action = keyUp ? "KeyUp" : "KeyDown";
        var input = InputStructBuilder.BuildKeyInput(key, keyUp);
        var (inserted, win32Error) = _sendInput(new[] { input });

        if (inserted == 0)
        {
            // A real, API-reported failure - distinct from UIPI, which
            // reports success and sets no error at all.
            _log.Error(
                "Injection",
                $"{action} scan=0x{key.ScanCode:X2} ext={key.IsExtended}: SendInput returned 0",
                $"GetLastError={win32Error}; foreground={context?.ForegroundProcessName ?? "(none)"}");
            return false;
        }

        if (_diagnosticModeEnabled())
        {
            _log.Info(
                "Injection",
                $"{action} scan=0x{key.ScanCode:X2} ext={key.IsExtended} -> Sent",
                $"foreground={context?.ForegroundProcessName ?? "(none)"}");
        }

        return true;
    }

    /// <summary>
    /// Removes one matching entry from <see cref="_heldKeys"/> (most
    /// recently added first), for a normal release of a key this instance
    /// actually tracked as held. A no-op if the key isn't tracked (e.g. it
    /// was already cleaned up by <see cref="ReleaseHeldKeys"/>, or its
    /// KeyDown was itself refused and never held in the first place) -
    /// releasing an untracked key is harmless.
    /// </summary>
    private void RemoveHeld(ScancodeInfo key)
    {
        for (var i = _heldKeys.Count - 1; i >= 0; i--)
        {
            if (_heldKeys[i].Equals(key))
            {
                _heldKeys.RemoveAt(i);
                return;
            }
        }
    }

    /// <summary>
    /// Releases every key this instance actually sent as "down" and hasn't
    /// yet released, most-recently-pressed first - called when a KeyDown is
    /// refused, so a modifier that already went down doesn't stay stuck just
    /// because the key that would have completed the chord got refused.
    /// Only releases what was tracked as sent, never the refused key itself
    /// (it was never sent, so there is nothing to release), and does nothing
    /// - not even a log line - when nothing is held, so a refusal before any
    /// key went down produces no spurious releases.
    /// </summary>
    private void ReleaseHeldKeys()
    {
        if (_heldKeys.Count == 0)
        {
            return;
        }

        var released = new List<string>();
        for (var i = _heldKeys.Count - 1; i >= 0; i--)
        {
            var held = _heldKeys[i];
            SendKey(held, keyUp: true, context: null);
            released.Add($"0x{held.ScanCode:X2}");
        }

        _heldKeys.Clear();

        _log.Warn(
            "Injection",
            $"Focus changed mid-chord; released {released.Count} already-pressed key(s) to avoid a stuck modifier.",
            $"released={string.Join(",", released)}");
    }
}
