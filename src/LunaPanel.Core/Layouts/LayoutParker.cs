using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Core.Layouts;

/// <summary>
/// Moves slots between <see cref="LayoutPage.Slots"/> and
/// <see cref="LayoutPage.Parked"/> when a page's template changes size:
/// growing restores any parked slot whose index now fits; shrinking moves
/// out-of-range slots to parked rather than deleting them (see
/// <c>ref/docs/design-decisions.md</c>'s "Shrinking a template parks slots
/// rather than deleting them").
///
/// Pure: this produces a new <see cref="LayoutPage"/> and never touches
/// disk, which is exactly what lets the editor call it twice for two
/// different reasons with no special-casing - once to *preview* what a
/// template change would park before the user confirms it, and again
/// (identically) to actually apply it once they do.
/// </summary>
public static class LayoutParker
{
    public static LayoutPage ChangeTemplate(LayoutPage page, PanelTemplate newTemplate)
    {
        var all = page.Slots.Concat(page.Parked);

        var stays = all.Where(s => s.Index >= 0 && s.Index < newTemplate.Slots)
            .OrderBy(s => s.Index)
            .ToArray();

        var parks = all.Where(s => s.Index < 0 || s.Index >= newTemplate.Slots)
            .OrderBy(s => s.Index)
            .ToArray();

        return page with { TemplateId = newTemplate.Id, Slots = stays, Parked = parks };
    }

    /// <summary>
    /// Same as <see cref="ChangeTemplate"/>, plus a single <c>Layout</c>-
    /// category log line when the change actually parks or restores a slot -
    /// same split as <c>LunaPanel.Core.Bindings.BindResolver.Resolve</c> vs
    /// <c>LogSummary</c>: the pure computation stays log-free (and cheap to
    /// call repeatedly for a preview), while this wrapper is what a real,
    /// user-confirmed template change should call. Nothing is logged for a
    /// change that parks or restores nothing.
    /// </summary>
    public static LayoutPage ChangeTemplateAndLog(LayoutPage page, PanelTemplate newTemplate, IDiagnosticLog log)
    {
        var result = ChangeTemplate(page, newTemplate);

        var previouslyActive = page.Slots.Select(s => s.Index).ToHashSet();
        var previouslyParked = page.Parked.Select(s => s.Index).ToHashSet();

        var newlyParked = result.Parked.Count(s => previouslyActive.Contains(s.Index));
        var newlyRestored = result.Slots.Count(s => previouslyParked.Contains(s.Index));

        if (newlyParked > 0 || newlyRestored > 0)
        {
            log.Info(
                "Layout",
                $"Page '{page.Name}' template changed {page.TemplateId} -> {newTemplate.Id}: {newlyParked} slot(s) parked, {newlyRestored} slot(s) restored.");
        }

        return result;
    }
}
