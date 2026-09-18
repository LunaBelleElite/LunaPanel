namespace LunaPanel.Core.Layouts;

/// <summary>
/// Spills a page's surplus forward onto further pages instead of parking it
/// (<c>ref/docs/panels-and-pages.md</c>'s "Spill, not park, as what a
/// commander normally meets"): shrinking a page from 30 slots to 12 no
/// longer preserves the surplus eighteen invisibly - it lands on further
/// pages instead, e.g. <c>12 / 12 / 6</c>, with six empty slots on the last
/// page ready to fill. Growing reverses this, pulling spilled slots back and
/// removing any page left empty by that.
///
/// This is a sibling of <see cref="LayoutParker"/>, not a rewrite of it -
/// <see cref="LayoutParker.ChangeTemplate"/> still exists and is still what
/// the "merging and expanding panels" setting's OFF path uses (see
/// <c>ref/docs/panels-and-pages.md</c>'s "Setting: 'Merging and expanding
/// panels'"), and parking is still the honest answer when a slot genuinely
/// cannot be placed. This type is the ON path: a pure function operating on
/// a whole <see cref="Layout"/> (which holds every page), because spilling
/// can create or remove pages, something a single-page function has no way
/// to express.
///
/// Pure: never touches disk, and the <see cref="Layout"/>/<see cref="LayoutPage"/>
/// values it is handed are never mutated - same "preview and apply are the
/// same call" property <see cref="LayoutParker"/> already has.
/// </summary>
public static class LayoutSpiller
{
    /// <summary>
    /// Changes <paramref name="pageIndex"/>'s template, spilling any surplus
    /// forward onto further pages (shrinking) or pulling spilled slots back,
    /// removing any page left empty by that (growing).
    ///
    /// Only the contiguous run of pages immediately after
    /// <paramref name="pageIndex"/> that share its CURRENT template id are
    /// ever touched - those can only exist because an earlier spill from
    /// this same page created them (spill always gives its own overflow
    /// pages the template it just spilled them onto), so folding them back
    /// into the pool and rebuilding them is safe. A page with a different
    /// template id belongs to the commander's own, separate arrangement and
    /// is left completely alone, exactly like everything before
    /// <paramref name="pageIndex"/> and everything after that run.
    ///
    /// <paramref name="pageIndex"/> itself is never removed, even if it ends
    /// up holding zero slots - it is the page the commander is actively
    /// changing, not a page spill created and can tidy away. Every slot
    /// gathered from the pool (the origin page's own <see cref="LayoutPage.Slots"/>
    /// and <see cref="LayoutPage.Parked"/>, plus each touched descendant
    /// page's own) is placed onto some resulting page - none are ever
    /// dropped, and spilling never uses <see cref="LayoutPage.Parked"/> in
    /// its own output (a stray existing Parked list is absorbed back into
    /// normal circulation, not preserved as parked).
    ///
    /// Slots are packed in original reading order (page by page, then by
    /// ascending index within each page) into sequential indices starting
    /// at 0 on each resulting page, filling one page to
    /// <paramref name="newTemplate"/>'s capacity before spilling onto the
    /// next - this compaction is what turns a sparse or gap-having
    /// arrangement into "12 / 12 / 6" rather than scattering slots across
    /// pages at their original absolute index. A consequence worth stating
    /// plainly: for an arrangement that already has gaps, the exact index a
    /// slot lands at is not preserved across a shrink-then-grow round trip -
    /// only every slot's CONTENT is guaranteed to survive. For a densely
    /// packed arrangement (no gaps - the common case, e.g. the starter
    /// layout), a shrink immediately followed by growing back to the
    /// original template reproduces the original arrangement exactly, index
    /// for index (see <c>LayoutSpillerTests</c>' round-trip pin).
    /// </summary>
    public static Layout ChangeTemplate(Layout layout, int pageIndex, PanelTemplate newTemplate)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(newTemplate);
        if (pageIndex < 0 || pageIndex >= layout.Pages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, "pageIndex must be a valid page in the layout.");
        }

        var origin = layout.Pages[pageIndex];
        var oldTemplateId = origin.TemplateId;

        var descendantEnd = pageIndex + 1;
        while (descendantEnd < layout.Pages.Count && layout.Pages[descendantEnd].TemplateId == oldTemplateId)
        {
            descendantEnd++;
        }

        var pool = new List<LayoutSlot>();
        pool.AddRange(origin.Slots.OrderBy(s => s.Index));
        pool.AddRange(origin.Parked.OrderBy(s => s.Index));
        for (var i = pageIndex + 1; i < descendantEnd; i++)
        {
            pool.AddRange(layout.Pages[i].Slots.OrderBy(s => s.Index));
            pool.AddRange(layout.Pages[i].Parked.OrderBy(s => s.Index));
        }

        var newPages = new List<LayoutPage>();
        var cursor = 0;
        var pageNumber = pageIndex + 1; // 1-based, matches the tab row's own "PANEL n" default naming.
        while (true)
        {
            var chunk = pool.Skip(cursor).Take(newTemplate.Slots)
                .Select((slot, i) => slot with { Index = i })
                .ToList();
            cursor += chunk.Count;

            var name = newPages.Count == 0 ? origin.Name : $"PANEL {pageNumber}";
            // The origin page keeps its declared vessel context on the first
            // resulting page, exactly as it keeps its NAME
            // (ref/docs/vessel-context.md). A page CREATED by spilling is not
            // one the commander declared a context for, so it is context-free
            // - inheriting the origin's would give that context two matching
            // pages and let "first matching page wins" land on a half-empty
            // overflow panel. Note a folded-in later page loses its own
            // showWhen, the same way it already loses its own name.
            var showWhen = newPages.Count == 0 ? origin.ShowWhen : null;
            newPages.Add(new LayoutPage(name, newTemplate.Id, chunk, Array.Empty<LayoutSlot>(), showWhen));
            pageNumber++;

            if (chunk.Count == 0 || cursor >= pool.Count)
            {
                break;
            }
        }

        var result = new List<LayoutPage>(layout.Pages.Take(pageIndex));
        result.AddRange(newPages);
        result.AddRange(layout.Pages.Skip(descendantEnd));

        return layout with { Pages = result };
    }
}
