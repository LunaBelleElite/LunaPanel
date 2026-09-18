using LunaPanel.Core.Layouts;

namespace LunaPanel.Server.Http;

/// <summary>
/// <c>POST /api/panel/template</c>'s handler logic, kept free of any ASP.NET
/// type - same discipline as <see cref="PanelEndpoint"/>/<see cref="PressEndpoint"/>.
/// The template picker (moved into settings - <c>ref/docs/panels-and-pages.md</c>)
/// calls this to change one page's rung on the ladder. This is a switch
/// between two already-implemented behaviours, not a third code path: when
/// <see cref="PanelSettings.MergeExpand"/> is on, surplus spills forward
/// onto further pages (<see cref="LayoutSpiller"/>); when off, surplus is
/// parked in place on the same page (<see cref="LayoutParker"/>).
/// </summary>
public static class PanelTemplateEndpoint
{
    public sealed record Request(int Page, string TemplateId);

    public enum ChangeOutcome
    {
        Ok,
        NoSuchPage,
        UnknownTemplate,
    }

    public sealed record ChangeResult(ChangeOutcome Outcome, Layout? Layout)
    {
        public static ChangeResult Ok(Layout layout) => new(ChangeOutcome.Ok, layout);
        public static ChangeResult Fail(ChangeOutcome outcome) => new(outcome, null);
    }

    public static ChangeResult ChangeTemplate(Layout layout, int pageIndex, string templateId, bool mergeExpand)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(templateId);

        if (pageIndex < 0 || pageIndex >= layout.Pages.Count)
        {
            return ChangeResult.Fail(ChangeOutcome.NoSuchPage);
        }

        if (!Templates.TryGet(templateId, out var template) || template is null)
        {
            return ChangeResult.Fail(ChangeOutcome.UnknownTemplate);
        }

        if (mergeExpand)
        {
            return ChangeResult.Ok(LayoutSpiller.ChangeTemplate(layout, pageIndex, template));
        }

        var newPage = LayoutParker.ChangeTemplate(layout.Pages[pageIndex], template);
        var pages = layout.Pages.ToList();
        pages[pageIndex] = newPage;
        return ChangeResult.Ok(layout with { Pages = pages });
    }
}
