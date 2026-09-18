using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.GameState;
using CoreCatalogue = LunaPanel.Core.Catalogue.Catalogue;

namespace LunaPanel.Core.Layouts;

/// <summary>
/// One active slot's current lit state: how engaged its physical control
/// should show as right now.
/// </summary>
/// <param name="Index">The slot's position within the page - matches <see cref="LayoutSlot.Index"/>.</param>
/// <param name="Level">How engaged the slot's action currently is, per its curated ordered <c>lit</c> list - first match wins, <see cref="SlotLitLevel.Off"/> when none match or the action has no <c>lit</c> list at all.</param>
public sealed record SlotLitState(int Index, SlotLitLevel Level);

/// <summary>
/// Computes per-slot lit state for a page's <em>active</em> slots (never
/// <see cref="LayoutPage.Parked"/> - a parked slot has no control on screen
/// to light up) against the shipped <see cref="CoreCatalogue"/> and the most
/// recent <see cref="StatusSnapshot"/>.
///
/// Deliberately its own type, not a field on <see cref="SlotAnnotation"/>:
/// annotation changes only when bindings, catalogue, or layout change, while
/// lit state changes as often as the game itself does - potentially several
/// times a second. Fusing the two would force a full re-annotation on every
/// status tick and rule out a cheap lit-only delta over a live channel. See
/// <c>ref/docs/panel-api.md</c>.
///
/// A slot with no action, an action the catalogue doesn't curate, or a
/// curated action with no <c>lit</c> condition, is never lit - that is its
/// ordinary "no observable on/off state" meaning, not a failure. A
/// <see langword="null"/> snapshot (no Status.json read yet) makes every slot
/// report not-lit rather than guessing.
/// </summary>
public static class LitStateResolver
{
    private const string LogCategory = "Lit";

    /// <summary>
    /// Pure - logs nothing. See <see cref="ResolveAndLog"/> for the version
    /// that reports a lit condition that failed to evaluate, matching the
    /// <c>Annotate</c>/<c>AnnotateAndLog</c> split <see cref="LayoutAnnotator"/>
    /// already uses.
    /// </summary>
    public static IReadOnlyList<SlotLitState> Resolve(LayoutPage page, CoreCatalogue catalogue, StatusSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(catalogue);

        return page.Slots
            .OrderBy(slot => slot.Index)
            .Select(slot => new SlotLitState(slot.Index, TryEvaluate(slot, catalogue, snapshot, out _)))
            .ToList();
    }

    /// <summary>
    /// Same as <see cref="Resolve"/>, plus an <c>Error</c>-level log line for
    /// any slot whose curated <c>lit</c> condition throws while evaluating -
    /// a defect in the shipped catalogue, not a user error, so it is reported
    /// loudly rather than silently swallowed. <see cref="CoreCatalogue.Parse"/>
    /// already validates every <c>lit</c> token against the real condition
    /// grammar at load time (see <c>ref/docs/catalogue.md</c>), so this branch
    /// cannot be reached through that path today; it exists as the same kind
    /// of defence-in-depth <see cref="StatusFileWatcher"/> applies to a torn
    /// read, in case that guarantee is ever loosened.
    /// </summary>
    public static IReadOnlyList<SlotLitState> ResolveAndLog(LayoutPage page, CoreCatalogue catalogue, StatusSnapshot? snapshot, IDiagnosticLog log)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(log);

        var results = new List<SlotLitState>(page.Slots.Count);
        foreach (var slot in page.Slots.OrderBy(s => s.Index))
        {
            var level = TryEvaluate(slot, catalogue, snapshot, out var error);
            if (error is not null)
            {
                log.Error(LogCategory, $"Lit condition failed to evaluate for action '{slot.Action}' at slot {slot.Index}; treating as never-lit", error);
            }

            results.Add(new SlotLitState(slot.Index, level));
        }

        return results;
    }

    private static SlotLitLevel TryEvaluate(LayoutSlot slot, CoreCatalogue catalogue, StatusSnapshot? snapshot, out string? error)
    {
        error = null;

        if (snapshot is null || slot.Action is null)
        {
            return SlotLitLevel.Off;
        }

        if (!catalogue.Actions.TryGetValue(slot.Action, out var action) || action.Lit is null)
        {
            return SlotLitLevel.Off;
        }

        try
        {
            return LitCondition.Evaluate(action.Lit, snapshot);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return SlotLitLevel.Off;
        }
    }
}
