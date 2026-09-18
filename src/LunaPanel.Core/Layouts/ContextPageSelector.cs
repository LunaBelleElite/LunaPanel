using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.GameState;

namespace LunaPanel.Core.Layouts;

/// <summary>
/// Picks the page that suits a <see cref="VesselContext"/>: the first page,
/// in declaration order, whose <see cref="LayoutPage.ShowWhen"/> is non-empty
/// and evaluates true. See <c>ref/docs/vessel-context.md</c>.
///
/// <para><b>A context-free page is never selected.</b> A page with no
/// <c>showWhen</c> is "always available" - reachable by hand from the tab
/// row, exactly as before this feature existed - which is not the same as
/// "matches every context". If it matched, the first page (typically the
/// context-free starter page) would be the answer for every context and
/// automatic switching would do nothing but jump to page 0 forever.</para>
///
/// <para><b>Returning <see langword="null"/> is the "no page matches" rule,
/// not an error.</b> The commander who has built no Scarab page should stay
/// exactly where they are rather than be thrown to a blank one.</para>
/// </summary>
public static class ContextPageSelector
{
    private const string LogCategory = "Layout";

    /// <summary>
    /// <paramref name="log"/> receives a <c>Warn</c> for any page whose
    /// <c>showWhen</c> cannot be parsed (an unknown condition name - a
    /// <c>Vessel:</c> term can never be unknown, since vessel types are not
    /// an enumerable set). That page is skipped rather than taking the whole
    /// layout down: a layout file is runtime data from the player's machine,
    /// and the same parse-lenient discipline <see cref="LayoutJson"/> applies
    /// to it applies here. Silently skipping it is what the log line exists
    /// to prevent.
    /// </summary>
    /// <returns>The index of the first matching page, or <see langword="null"/> when no page declares a context that matches.</returns>
    public static int? Select(Layout layout, StatusSnapshot snapshot, VesselContext context, IDiagnosticLog log)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(log);

        for (var i = 0; i < layout.Pages.Count; i++)
        {
            var page = layout.Pages[i];
            if (page.ShowWhen is null || page.ShowWhen.Count == 0)
            {
                // Context-free: always available by hand, never switched TO.
                continue;
            }

            ShowWhenConditionList conditions;
            try
            {
                conditions = ShowWhenConditionList.Parse(page.ShowWhen);
            }
            catch (FormatException ex)
            {
                log.Warn(
                    LogCategory,
                    $"Page '{page.Name}' declares a context that could not be understood, so it will never be shown automatically",
                    ex.Message);
                continue;
            }

            if (conditions.Evaluate(snapshot, context))
            {
                return i;
            }
        }

        return null;
    }
}
