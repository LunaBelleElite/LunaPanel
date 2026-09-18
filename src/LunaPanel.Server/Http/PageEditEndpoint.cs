using LunaPanel.Core.GameState;
using LunaPanel.Core.Layouts;

namespace LunaPanel.Server.Http;

/// <summary>
/// The add/rename/visibility verbs a device's own page bar needs
/// (<c>ref/docs/panels-and-pages.md</c>) - kept free of any ASP.NET type,
/// same discipline as <see cref="PanelTemplateEndpoint"/>/<see cref="SlotEditEndpoint"/>.
/// Every method returns a fresh <see cref="Layout"/>; nothing here writes to
/// disk - the caller (<see cref="Hosting.ServerHostBuilder"/>) calls
/// <see cref="LayoutStore.Save"/> with the result, exactly like every other
/// layout-mutating endpoint.
/// </summary>
public static class PageEditEndpoint
{
    public sealed record AddPageRequest(string Name, string TemplateId);

    public sealed record RenamePageRequest(int Page, string Name);

    public sealed record SetShowWhenRequest(int Page, IReadOnlyList<string>? ShowWhen, bool Force = false);

    public sealed record DeletePageRequest(int Page);

    public sealed record MovePageRequest(int From, int To);

    public enum EditOutcome
    {
        Ok,
        NoSuchPage,
        UnknownTemplate,
        InvalidShowWhen,
        ShowWhenConflict,
        CannotDeleteLastPage,
    }

    public sealed record EditResult(EditOutcome Outcome, Layout? Layout, string? Error = null, int? ConflictPageIndex = null, string? ConflictPageName = null)
    {
        public static EditResult Ok(Layout layout) => new(EditOutcome.Ok, layout);
        public static EditResult Fail(EditOutcome outcome, string? error = null) => new(outcome, null, error);
        public static EditResult Conflict(int pageIndex, string pageName) => new(EditOutcome.ShowWhenConflict, null, ConflictPageIndex: pageIndex, ConflictPageName: pageName);
    }

    /// <summary>
    /// Appends a brand new, empty page (no slots, no parked slots, no
    /// declared context - "always available by hand" exactly like a
    /// hand-authored context-free page) to the end of <paramref name="layout"/>'s
    /// page list. The new page's index is always <c>layout.Pages.Count - 1</c>
    /// of the RETURNED layout, so the caller (the "+" flow) can switch to it
    /// without a second lookup.
    ///
    /// No length or uniqueness constraint on <paramref name="name"/> -
    /// <see cref="LayoutPage.Name"/> carries none today for any page, and a
    /// page name is drawn in the tab row, not squeezed into a fixed-size
    /// button cell the way a slot label is, so the slot sheet's
    /// <c>LabelFitsBudget</c> wrapping rule has no obvious analogue here.
    /// An empty/whitespace name is refused one layer up, in
    /// <see cref="Hosting.ServerHostBuilder"/>'s route handler, the same
    /// place <see cref="PanelTemplateEndpoint"/>'s own empty-template-id
    /// check lives - this method assumes its caller already decided the
    /// text is worth sending.
    /// </summary>
    public static EditResult AddPage(Layout layout, string name, string templateId)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(templateId);

        if (!Templates.TryGet(templateId, out var template) || template is null)
        {
            return EditResult.Fail(EditOutcome.UnknownTemplate);
        }

        var newPage = new LayoutPage(name, templateId, Array.Empty<LayoutSlot>(), Array.Empty<LayoutSlot>(), ShowWhen: null);
        var pages = layout.Pages.ToList();
        pages.Add(newPage);
        return EditResult.Ok(layout with { Pages = pages });
    }

    /// <summary>
    /// Renames one existing page in place. Never touches
    /// <see cref="LayoutPage.TemplateId"/>/<see cref="LayoutPage.Slots"/>/
    /// <see cref="LayoutPage.Parked"/>/<see cref="LayoutPage.ShowWhen"/> -
    /// same "one verb, one field" discipline <see cref="SlotEditEndpoint.SetLabel"/>
    /// already follows for a slot's own label.
    /// </summary>
    public static EditResult RenamePage(Layout layout, int pageIndex, string name)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(name);

        if (pageIndex < 0 || pageIndex >= layout.Pages.Count)
        {
            return EditResult.Fail(EditOutcome.NoSuchPage);
        }

        var pages = layout.Pages.ToList();
        pages[pageIndex] = pages[pageIndex] with { Name = name };
        return EditResult.Ok(layout with { Pages = pages });
    }

    /// <summary>
    /// Sets (or, when <paramref name="showWhen"/> is <see langword="null"/>
    /// or empty, clears back to context-free) one page's declared vessel
    /// context. Every token is parsed with
    /// <see cref="ShowWhenConditionList.Parse"/> before anything is
    /// accepted - nothing else in this data model validates <c>ShowWhen</c>
    /// at all (<see cref="LayoutValidator"/> does not touch it), so a bad
    /// token saved here would otherwise sit silently unreachable until
    /// <see cref="ContextPageSelector.Select"/> logged a warning and skipped
    /// the page - long after the commander who typo'd it had moved on. This
    /// is the one and only place that check runs for a value reaching this
    /// route.
    ///
    /// <para><b>Exact-set duplicate guard.</b> Two pages declaring the same
    /// token SET (order-independent) both claim the same vessel context, but
    /// <see cref="ContextPageSelector.Select"/> only ever reaches the first
    /// one in declaration order - the second is permanently unreachable by
    /// auto-switch, silently, with no error anywhere. When
    /// <paramref name="showWhen"/> is non-empty and another page already
    /// declares the identical set, this returns
    /// <see cref="EditOutcome.ShowWhenConflict"/> (naming that page) and
    /// saves nothing, unless <paramref name="force"/> is set, in which case
    /// the conflicting page's <see cref="LayoutPage.ShowWhen"/> is cleared
    /// back to context-free in the same edit that sets this page's. A
    /// specific/general overlap (e.g. <c>["InSrv"]</c> against
    /// <c>["InSrv","Vessel:testbuggy"]</c>) is not a set match and is not
    /// flagged - only an identical set is genuinely unreachable.</para>
    /// </summary>
    public static EditResult SetShowWhen(Layout layout, int pageIndex, IReadOnlyList<string>? showWhen, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (pageIndex < 0 || pageIndex >= layout.Pages.Count)
        {
            return EditResult.Fail(EditOutcome.NoSuchPage);
        }

        var normalized = showWhen is null || showWhen.Count == 0 ? null : showWhen;
        if (normalized is not null)
        {
            try
            {
                ShowWhenConditionList.Parse(normalized);
            }
            catch (FormatException ex)
            {
                return EditResult.Fail(EditOutcome.InvalidShowWhen, ex.Message);
            }

            var candidateSet = normalized.ToHashSet(StringComparer.Ordinal);
            for (var i = 0; i < layout.Pages.Count; i++)
            {
                if (i == pageIndex)
                {
                    continue;
                }

                var otherShowWhen = layout.Pages[i].ShowWhen;
                if (otherShowWhen is null || otherShowWhen.Count == 0)
                {
                    continue;
                }

                if (otherShowWhen.Count != candidateSet.Count)
                {
                    continue;
                }

                if (!otherShowWhen.ToHashSet(StringComparer.Ordinal).SetEquals(candidateSet))
                {
                    continue;
                }

                if (!force)
                {
                    return EditResult.Conflict(i, layout.Pages[i].Name);
                }

                var forcedPages = layout.Pages.ToList();
                forcedPages[i] = forcedPages[i] with { ShowWhen = null };
                forcedPages[pageIndex] = forcedPages[pageIndex] with { ShowWhen = normalized };
                return EditResult.Ok(layout with { Pages = forcedPages });
            }
        }

        var pages = layout.Pages.ToList();
        pages[pageIndex] = pages[pageIndex] with { ShowWhen = normalized };
        return EditResult.Ok(layout with { Pages = pages });
    }

    /// <summary>
    /// Removes one page outright. Refused with
    /// <see cref="EditOutcome.CannotDeleteLastPage"/> when <paramref name="layout"/>
    /// carries only one page - a page-less layout breaks
    /// <see cref="ContextPageSelector"/> and rendering entirely, so this is
    /// an authoritative server-side floor, not just a client-side hint. The
    /// caller (the page-settings sheet's own confirm dialog) is responsible
    /// for the "everything on it is lost" warning; nothing here recovers a
    /// deleted page's slots.
    ///
    /// <para>[2026-09-16] <b>Folders (<c>ref/docs/layouts.md</c>).</b> This
    /// is also the mechanism behind the commander's "delete this folder and
    /// everything in it" action - a folder IS its interior page - which
    /// forced two changes:</para>
    /// <list type="bullet">
    /// <item><b>The floor counts only real pages, and only guards them.</b>
    /// A folder's interior is not a page the tab bar can reach, so it can
    /// never stand in for the last real one; deleting the last REAL page is
    /// refused even when a dozen folders remain. The guard deliberately does
    /// not apply to deleting a folder page: a layout of one real page plus
    /// one folder must still be able to delete the folder, and a floor
    /// counted over the raw list would leave that folder permanently
    /// undeletable.</item>
    /// <item><b>Every reference to the removed page is cleared.</b> After the
    /// removal, any slot - active or parked, on any remaining page - naming
    /// the deleted page's id becomes an empty slot. Without this the layout
    /// would carry a dangling folder button that
    /// <see cref="LayoutValidator"/> refuses on the very next save, with
    /// nothing on screen to explain it.</item>
    /// </list>
    /// </summary>
    public static EditResult DeletePage(Layout layout, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (pageIndex < 0 || pageIndex >= layout.Pages.Count)
        {
            return EditResult.Fail(EditOutcome.NoSuchPage);
        }

        var doomed = layout.Pages[pageIndex];

        if (!doomed.IsFolderOwned && layout.Pages.Count(p => !p.IsFolderOwned) <= 1)
        {
            return EditResult.Fail(EditOutcome.CannotDeleteLastPage);
        }

        var pages = layout.Pages.ToList();
        pages.RemoveAt(pageIndex);

        if (doomed.Id is not null)
        {
            for (var i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                var slots = page.Slots.Where(s => !string.Equals(s.FolderPage, doomed.Id, StringComparison.Ordinal)).ToList();
                var parked = page.Parked.Where(s => !string.Equals(s.FolderPage, doomed.Id, StringComparison.Ordinal)).ToList();

                if (slots.Count != page.Slots.Count || parked.Count != page.Parked.Count)
                {
                    pages[i] = page with { Slots = slots, Parked = parked };
                }
            }
        }

        return EditResult.Ok(layout with { Pages = pages });
    }

    /// <summary>
    /// Reorders the page bar: removes the page at <paramref name="from"/> and
    /// re-inserts it at <paramref name="to"/>. A plain
    /// <see cref="List{T}.RemoveAt"/> + <see cref="List{T}.Insert"/> - the
    /// shift every other index needs happens automatically as a side effect
    /// of removing before inserting, the same way <see cref="SlotEditEndpoint"/>'s
    /// own move leaves index bookkeeping to the list itself rather than
    /// computing it by hand.
    /// </summary>
    public static EditResult MovePage(Layout layout, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (from < 0 || from >= layout.Pages.Count || to < 0 || to >= layout.Pages.Count)
        {
            return EditResult.Fail(EditOutcome.NoSuchPage);
        }

        var pages = layout.Pages.ToList();
        var page = pages[from];
        pages.RemoveAt(from);
        pages.Insert(to, page);
        return EditResult.Ok(layout with { Pages = pages });
    }
}
