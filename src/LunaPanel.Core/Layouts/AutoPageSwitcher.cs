using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.GameState;

namespace LunaPanel.Core.Layouts;

/// <summary>
/// The rule that makes automatic page switching a feature rather than an
/// irritation (<c>ref/docs/vessel-context.md</c>): <b>a context change
/// switches the page once, and nothing switches again until the context
/// changes again.</b>
///
/// <para>So docking the Nomad moves the panel to the ship's page. If the
/// commander then taps across to another page, they stay there - this class
/// never re-asserts, because it only ever answers a <em>change</em>, never
/// a level. A panel that re-asserted itself would be fighting the person
/// holding it, and would do so precisely when they had deliberately
/// overridden it.</para>
///
/// <para>One instance per live channel connection, seeded with the context
/// current at the moment that connection opened - so a device that connects
/// mid-session is not immediately dragged anywhere, and a device that
/// switched (by this class or by hand) starts its next connection from where
/// it actually is.</para>
///
/// <para>Thread-safe: a status change and a journal event arrive on
/// different background threads (<c>StatusFileWatcher</c>'s read cycle and
/// <c>JournalTailer</c>'s), and both drive <see cref="Decide"/>.</para>
/// </summary>
public sealed class AutoPageSwitcher
{
    private const string LogCategory = "Layout";

    private readonly object _gate = new();
    private VesselContext _lastContext;

    public AutoPageSwitcher(VesselContext initialContext)
    {
        ArgumentNullException.ThrowIfNull(initialContext);
        _lastContext = initialContext;
    }

    /// <summary>The context this switcher last observed. Exposed for tests and diagnostics; nothing behavioural reads it.</summary>
    public VesselContext LastContext
    {
        get
        {
            lock (_gate)
            {
                return _lastContext;
            }
        }
    }

    /// <summary>
    /// Returns the page index to switch to, or <see langword="null"/> to
    /// leave the commander exactly where they are. Four separate reasons
    /// produce <see langword="null"/>, and they are deliberately not
    /// collapsed:
    /// <list type="number">
    /// <item><description>the context did not change (the common case - <c>Status.json</c> is rewritten on an idle heartbeat as well as on change, LC6);</description></item>
    /// <item><description>there is no snapshot at all yet;</description></item>
    /// <item><description>no page declares a context that matches (<see cref="ContextPageSelector"/>);</description></item>
    /// <item><description>the matching page is the one already showing.</description></item>
    /// </list>
    /// <b>Observing the context is not conditional on any of the other
    /// three.</b> A context change is consumed even when it produces no
    /// switch, so a commander who changes context twice does not get a
    /// backdated switch on the second one.
    /// </summary>
    public int? Decide(Layout layout, int currentPageIndex, StatusSnapshot? snapshot, string? lastKnownVesselType, IDiagnosticLog log)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(log);

        var observed = VesselContextResolver.Resolve(snapshot, lastKnownVesselType);
        lock (_gate)
        {
            if (observed == _lastContext)
            {
                return null;
            }

            _lastContext = observed;
        }

        if (snapshot is null)
        {
            return null;
        }

        var target = ContextPageSelector.Select(layout, snapshot, observed, log);
        if (target is null)
        {
            // Only reached after a REAL context change (the far more common
            // "nothing changed" case already returned above, silently, by
            // design - logging every unchanged tick would flood this
            // category). A context change with nothing to switch to is rare
            // and genuinely worth a line: 2026-09-19, a commander asked "why
            // didn't it switch" with nothing in the log to answer from.
            log.Info(LogCategory, $"Vessel context changed to {observed}, but no page matches - staying put.");
            return null;
        }

        if (target == currentPageIndex)
        {
            return null;
        }

        log.Info(LogCategory, $"Vessel context changed to {observed} - switching to page {target}.");
        return target;
    }
}
