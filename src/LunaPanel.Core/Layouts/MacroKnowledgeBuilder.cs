using LunaPanel.Core.Bindings;
using LunaPanel.Core.GameState;
using LunaPanel.Core.Macros;

namespace LunaPanel.Core.Layouts;

/// <summary>
/// Computes real <see cref="MacroKnowledge"/> from loaded <see cref="MacroDefinition"/>s
/// and the player's current <see cref="BindingsFile"/> - the production
/// counterpart to the hand-built <see cref="MacroKnowledge"/> every
/// <see cref="LayoutAnnotator"/>/<c>PressEndpoint</c> test uses today (see
/// <c>ref/docs/layouts.md</c>'s "not read from real macro definitions, since
/// the macro loader is a later task"). Now that a macro loader exists
/// (<c>LunaPanel.Server.Macros.MacroLoader</c>), this is the one place that
/// turns its output into what the annotator actually consumes.
/// </summary>
public static class MacroKnowledgeBuilder
{
    /// <summary>
    /// Every macro's id becomes a <see cref="MacroKnowledge.KnownIds"/>
    /// entry. A macro is additionally degraded when any <c>press</c>/
    /// <c>pressUntil</c>/<c>gotoLeftPanelTab</c> step names an action that
    /// does not currently resolve to a bound keyboard chord - the exact same
    /// <see cref="BindResolver.Resolve"/> call <c>MacroRunner</c> itself
    /// uses to fire that step, so a degraded macro and a macro that would
    /// actually fail to run always agree with each other. A <c>wait</c>/
    /// <c>require</c>/<c>waitFor</c>/<c>waitForEdge</c> step names no action
    /// at all and is never itself a source of degradation here.
    ///
    /// <c>gotoLeftPanelTab</c> is checked against
    /// <see cref="PanelTabTracker.AdvanceTabAction"/> unconditionally, even
    /// though the step sends zero presses at run time when the tracker
    /// already knows the panel is on the target tab - this class has no
    /// access to that runtime state, and the safe bias is to flag the macro
    /// whenever the underlying action could be needed, not only when a
    /// particular session's tracked position happens to require it.
    ///
    /// Every macro whose definition carries a non-null <c>name</c> also
    /// becomes a <see cref="MacroKnowledge.Names"/> entry, so
    /// <see cref="LayoutAnnotator"/> can label a macro-referencing slot with
    /// its real name instead of falling all the way back to
    /// <see cref="LunaPanel.Core.Catalogue.Prettifier.Prettify"/> on the raw
    /// id (see <c>ref/docs/button-naming.md</c>) - a macro with no name at
    /// all is simply absent from the dictionary, same "absent means fall
    /// back" convention as everything else here.
    /// </summary>
    public static MacroKnowledge Build(IReadOnlyList<MacroDefinition> macros, BindingsFile bindings)
    {
        ArgumentNullException.ThrowIfNull(macros);
        ArgumentNullException.ThrowIfNull(bindings);

        var known = new HashSet<string>(StringComparer.Ordinal);
        var degraded = new HashSet<string>(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var macro in macros)
        {
            known.Add(macro.Id);

            if (macro.Name is not null)
            {
                names[macro.Id] = macro.Name;
            }

            if (IsDegraded(macro, bindings))
            {
                degraded.Add(macro.Id);
            }
        }

        return new MacroKnowledge(known, degraded, names);
    }

    private static bool IsDegraded(MacroDefinition macro, BindingsFile bindings) =>
        IsDegraded(macro.Steps, bindings);

    /// <summary>
    /// [2026-09-17] Takes a step LIST rather than the macro, so a
    /// <c>branch</c>'s two arms go through the identical check. Both arms
    /// count, not just the one today's game state would take: a macro whose
    /// surface-landing arm presses something unbound IS degraded, and
    /// reporting otherwise would leave the commander a button that looks fine
    /// until the one time it matters. This is the same both-arms reasoning
    /// <c>MacroRunner.MightInject</c> applies to the injection lock, for the
    /// same reason - which arm will run is not knowable here at all.
    ///
    /// Without this recursion the presses inside <c>disembark</c>'s arms
    /// would have stopped contributing to its degraded state the moment that
    /// macro gained a branch - a silent regression, since a branch step
    /// itself names no action and the old loop would simply have found
    /// nothing.
    /// </summary>
    private static bool IsDegraded(IReadOnlyList<MacroStep> steps, BindingsFile bindings)
    {
        foreach (var step in steps)
        {
            if (step is BranchStep branch)
            {
                if (IsDegraded(branch.Then, bindings) || IsDegraded(branch.Else, bindings))
                {
                    return true;
                }

                continue;
            }

            var action = step switch
            {
                PressStep p => p.Action,
                PressUntilStep pu => pu.Action,
                GotoLeftPanelTabStep => PanelTabTracker.AdvanceTabAction,
                // A raw key press names no Frontier action at all - there is
                // nothing here for BindResolver to fail to resolve, so this
                // step kind can never contribute to a macro being degraded.
                // Named explicitly (2026-09-12) rather than left to fall
                // through to the default below, per this project's own
                // "sweep every step kind" discipline: the default is
                // correct for this case today, but a future case that landed
                // on it by accident would be silently wrong instead of
                // loudly missing.
                PressKeyStep => null,
                _ => null,
            };

            if (action is not null && !BindResolver.Resolve(bindings, action).IsBound)
            {
                return true;
            }
        }

        return false;
    }
}
