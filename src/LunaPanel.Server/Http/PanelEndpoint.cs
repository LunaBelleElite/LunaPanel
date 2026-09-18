using LunaPanel.Core.Catalogue;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.GameState;
using LunaPanel.Core.Latching;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Macros;
using LunaPanel.Core.Theme;
using CoreCatalogue = LunaPanel.Core.Catalogue.Catalogue;

namespace LunaPanel.Server.Http;

/// <summary>
/// <c>GET /api/panel</c>'s handler logic, kept free of any ASP.NET type - same
/// discipline as <see cref="HealthEndpoint"/>/<see cref="PairEndpoint"/>.
/// Builds everything the paired device needs to draw its (currently only)
/// page: the template id, the geometry/cell size for the viewport's
/// orientation (<see cref="CellSizeEstimator"/>, <see cref="Templates"/>),
/// every active slot's resolved label/status/reason/chord
/// (<see cref="LayoutAnnotator"/>), and the resolved HUD theme as CSS custom
/// properties (<see cref="HudThemeCssRenderer.Render"/>).
///
/// Each active slot also carries its current <see cref="SlotDto.Lit"/> level
/// (see <see cref="LitStateResolver"/>), serialized as its <see cref="SlotLitLevel"/>
/// name ("Off"/"Partial"/"Full" - same convention as <see cref="SlotDto.Status"/>)
/// - a snapshot of the moment this response was built, not a live value; a
/// paired device gets live updates to it from <c>GET /api/panel/live</c>
/// instead of re-polling this endpoint.
///
/// <see cref="SlotDto.LongPress"/> is <see langword="null"/> when the slot
/// has no long-press action at all, and otherwise carries its own
/// status/reason/chord (<see cref="LongPressDto"/>) - it degrades
/// independently of the primary action, so a working primary is never
/// disabled just because its long-press is unbound, and the reverse holds
/// too (<c>ref/docs/editor.md</c>).
///
/// <b>No filesystem paths ever appear in the response</b> - every field here
/// is a bare value derived from already-resolved data (an index, a label, an
/// enum name, a chord's display text, a CSS string), matching
/// <see cref="HealthEndpoint"/>'s own "true by construction" discipline
/// rather than a separate redaction step.
/// </summary>
public static class PanelEndpoint
{
    /// <summary>
    /// A slot's own long-press action, mirrored the same way the primary
    /// action is (<see cref="SlotDto"/>) - <see cref="Status"/>/
    /// <see cref="Reason"/> from <see cref="Core.Layouts.LongPressAnnotation"/>,
    /// <see cref="Chord"/> resolved the same way <see cref="SlotDto.Chord"/>
    /// is. No label field - a long-press carries no override of its own
    /// (<c>ref/docs/button-naming.md</c>'s own "Undecided", not yet
    /// answered), and this is the read-time projection of that, not a place
    /// to invent one. <see langword="null"/> on <see cref="SlotDto.LongPress"/>
    /// itself (not this record) means the slot has none at all.
    /// </summary>
    public sealed record LongPressDto(string Status, string Reason, string? Chord);

    /// <param name="Latch">
    /// Whether tapping this button holds its key down until tapped again
    /// (<c>ref/docs/latching-keys.md</c>) rather than tapping it. The client
    /// needs this for the affordance (a latch has to be discoverable on a
    /// touch surface with no tooltips, same reasoning as
    /// <c>.slot.has-long-press</c>) and for the slot sheet's own toggle -
    /// NOT for the press itself, which stays a plain <c>POST /api/press</c>
    /// with the server deciding what that means.
    /// </param>
    /// <param name="MacroId">
    /// The macro this slot names, or <see langword="null"/> when it names a
    /// plain action (<c>ref/docs/layouts.md</c>: a slot names exactly one of
    /// the two). This is still <b>intent, never resolution</b> - the id a
    /// layout stored, not anything resolved from it.
    ///
    /// [2026-09-09] Added. <c>ref/docs/editor.md</c> called the absence of
    /// this field "the roughest edge in the feature": the slot sheet offered
    /// the latch toggle on a macro slot because the response gave it no way
    /// to tell the two apart, and the commander found out by tapping it and
    /// reading a refusal. It also lets the slot sheet offer "Edit this
    /// macro..." on a macro slot, which is the shortest path from a button
    /// that does nearly the right thing to changing it. The server-side
    /// refusal in <c>SlotEditEndpoint.SetLatch</c> stays exactly as it was -
    /// the button being absent is a courtesy, not the enforcement, the same
    /// division the Devices pane's own Forget button already follows.
    /// </param>
    /// <param name="Hold">
    /// [2026-09-12] Whether this button holds its action for as long as a
    /// finger stays down, rather than tapping or latching it
    /// (<c>ref/docs/latching-keys.md</c>'s hold-to-thrust extension). Same
    /// reason the client needs <see cref="Latch"/>: the affordance has to be
    /// discoverable on a touch surface with no tooltips, and the slot sheet
    /// needs it for its own toggle - not for the press itself, which stays a
    /// plain <c>POST /api/press</c> carrying the hold phase instead.
    /// </param>
    /// <param name="IsFolder">
    /// [2026-09-16] Whether tapping this button opens a nested page of its
    /// own buttons rather than firing anything (<c>ref/docs/layouts.md</c>'s
    /// folders). The client needs it for two things: the navigation branch in
    /// <c>onSlotTap</c>, and the visual affordance that makes a folder
    /// distinguishable from a plain button on a touch surface with no
    /// tooltips - the same reasoning <see cref="Latch"/>/<see cref="Hold"/>
    /// already carry.
    /// </param>
    /// <param name="FolderPageIndex">
    /// [2026-09-16] The folder's interior page resolved to a real
    /// <see cref="Layout.Pages"/> INDEX, or <see langword="null"/> when this
    /// slot is not a folder (or names a page that no longer exists). The
    /// layout stores a stable id precisely because an index is unsafe to
    /// STORE - but the client only ever works in flat indices, so the
    /// translation happens here, once per slot, on the way out.
    /// </param>
    public sealed record SlotDto(int Index, string Label, string Status, string Reason, string? Chord, string Lit, LongPressDto? LongPress, bool Latch, string? MacroId, bool Hold = false, bool IsFolder = false, int? FolderPageIndex = null);

    /// <param name="ShowWhen">
    /// [2026-09-12] The REQUESTED page's own <see cref="LayoutPage.ShowWhen"/>,
    /// verbatim, or an empty list when it is <see langword="null"/>/empty
    /// (context-free) - never <see langword="null"/> itself, so the client's
    /// "page settings" visibility picker (<c>ref/docs/panels-and-pages.md</c>)
    /// always has an array to check tokens against without a null guard.
    /// Added so that picker can pre-select whichever tokens the page already
    /// declares - nothing before this field existed read <c>ShowWhen</c> at
    /// all from this response.
    /// </param>
    /// <param name="PageNames">
    /// The names the tab row draws - EXCLUDING any page that is a folder's
    /// interior (<see cref="LayoutPage.IsFolderOwned"/>), which is reached
    /// through its button and never through a tab.
    /// </param>
    /// <param name="PageIndices">
    /// [2026-09-16] The real <see cref="Layout.Pages"/> index of each name in
    /// <see cref="PageNames"/>, same order, same length. Exists because
    /// excluding folder pages broke the "array position == page index"
    /// assumption the tab row was built on - every tab addresses
    /// <c>pageIndices[i]</c>, never its own position, so a layout whose first
    /// page owns a folder still switches to the right page when its second
    /// tab is tapped.
    /// </param>
    /// <param name="ParentPageIndex">
    /// [2026-09-16] The page that owns the folder currently being viewed, or
    /// <see langword="null"/> when the requested page is not a folder's
    /// interior. This single field is the client's whole signal to replace
    /// the ordinary tab row with the "up one level" control - so it is also
    /// <see langword="null"/> for an ORPHANED interior (its button cleared),
    /// which correctly falls back to the normal tabs rather than offering a
    /// way back to a button that no longer points anywhere.
    /// </param>
    /// <param name="ParentPageName">
    /// The owning page's name, for the up-one-level control's own text.
    /// Always set exactly when <see cref="ParentPageIndex"/> is.
    /// </param>
    /// <param name="CurrentPageName">
    /// [2026-09-16] The REQUESTED page's own name. Redundant for an ordinary
    /// page (it is <c>PageNames[PageIndices.IndexOf(PageIndex)]</c>) and the
    /// only source for a folder's: <see cref="PageNames"/> deliberately
    /// excludes folder pages, so the page being viewed right now is the one
    /// name that array can never contain - and the non-tappable
    /// "which folder am I in" label needs exactly that name.
    /// </param>
    public sealed record PanelResponse(
        string TemplateId,
        int Cols,
        int Rows,
        double CellWidth,
        double CellHeight,
        string Verdict,
        IReadOnlyList<SlotDto> Slots,
        string ThemeCss,
        double HeaderStrip,
        double FramePadding,
        double TabStripHeight,
        int PageIndex,
        int PageCount,
        IReadOnlyList<string> PageNames,
        IReadOnlyList<string> ShowWhen,
        IReadOnlyList<int> PageIndices,
        string CurrentPageName,
        int? ParentPageIndex = null,
        string? ParentPageName = null);

    private static readonly IReadOnlySet<string> EmptyLatchedActions = new HashSet<string>(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> EmptyRunningMacroIds = new HashSet<string>(StringComparer.Ordinal);

    public enum BuildOutcome
    {
        Ok,
        NoSuchPage,
        UnknownTemplate,
    }

    public sealed record BuildResult(BuildOutcome Outcome, PanelResponse? Response)
    {
        public static BuildResult Ok(PanelResponse response) => new(BuildOutcome.Ok, response);
        public static BuildResult Fail(BuildOutcome outcome) => new(outcome, null);
    }

    public static BuildResult BuildResponse(
        Layout layout,
        int pageIndex,
        double viewportWidth,
        double viewportHeight,
        CoreCatalogue catalogue,
        IReadOnlyList<CataloguePickerEntry> catalogueMerge,
        MacroKnowledge macros,
        IReadOnlySet<string> knownActionNames,
        HudTheme theme,
        StatusSnapshot? snapshot,
        IDiagnosticLog log,
        IReadOnlySet<string>? latchedActions = null,
        IReadOnlySet<string>? runningMacroIds = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(catalogueMerge);
        ArgumentNullException.ThrowIfNull(macros);
        ArgumentNullException.ThrowIfNull(knownActionNames);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(log);

        if (pageIndex < 0 || pageIndex >= layout.Pages.Count)
        {
            return BuildResult.Fail(BuildOutcome.NoSuchPage);
        }

        var page = layout.Pages[pageIndex];
        if (!Templates.TryGet(page.TemplateId, out var template) || template is null)
        {
            return BuildResult.Fail(BuildOutcome.UnknownTemplate);
        }

        var orientation = viewportWidth >= viewportHeight ? PanelOrientation.Landscape : PanelOrientation.Portrait;
        var geometry = template.Geometry(orientation);
        var estimate = CellSizeEstimator.Estimate(viewportWidth, viewportHeight, orientation, template);
        var allowances = CellSizeEstimator.Allowances(viewportWidth, viewportHeight);

        // A single-page synthetic Layout: LayoutAnnotator loops over every
        // page in whatever Layout it's handed, tagging each result with that
        // page's name - handing it only the requested page means the result
        // can never be confused with another page sharing the same name.
        var singlePageLayout = new Layout(layout.SchemaVersion, new[] { page });
        // layout.Pages, not singlePageLayout's: a folder opens a page that is
        // by construction NOT the page being annotated, so without the whole
        // list every folder button on every real response would annotate as a
        // dangling reference.
        var annotations = LayoutAnnotator.Annotate(singlePageLayout, catalogueMerge, macros, knownActionNames, layout.Pages);

        var byActionName = catalogueMerge.ToDictionary(e => e.ActionName, e => e, StringComparer.Ordinal);
        var actionByIndex = page.Slots.ToDictionary(s => s.Index, s => s.Action);
        var longPressByIndex = page.Slots.ToDictionary(s => s.Index, s => s.LongPress);

        // Only page.Slots (active slots) are ever rendered - a parked slot
        // has no cell in the current template to draw itself into.
        var activeIndices = actionByIndex.Keys.ToHashSet();

        // Kept apart from LayoutAnnotator/SlotAnnotation on purpose - see
        // LitStateResolver's own remarks. Computed once per response, keyed
        // by index for the per-slot lookup below.
        // The latch override runs through LatchedLit.Apply, the one place
        // it is spelled - GET /api/panel/live applies exactly the same call
        // to exactly the same resolver output, so the first paint and every
        // later push can never disagree about whether a held button looks
        // held (ref/docs/latching-keys.md, ref/docs/lit-state.md).
        // RunningMacroLit.Apply is the second override, in the same place and
        // for the same reason (ref/docs/lit-state.md): a macro's button glows
        // for as long as its run lasts, and GET /api/panel/live layers the
        // identical pair over the identical resolver output, so the first
        // paint and every later push agree.
        var litByIndex = RunningMacroLit
            .Apply(
                LatchedLit.Apply(
                    LitStateResolver.ResolveAndLog(page, catalogue, snapshot, log),
                    page,
                    latchedActions ?? EmptyLatchedActions),
                page,
                runningMacroIds ?? EmptyRunningMacroIds)
            .ToDictionary(s => s.Index, s => s.Level);

        var slots = annotations
            .Where(a => activeIndices.Contains(a.Slot.Index))
            .OrderBy(a => a.Slot.Index)
            .Select(a =>
            {
                var action = actionByIndex[a.Slot.Index];
                var chord = action is not null && byActionName.TryGetValue(action, out var entry) && entry.IsBound
                    ? entry.DisplayChord
                    : null;

                LongPressDto? longPress = null;
                if (a.Slot.LongPress is not null)
                {
                    var longPressAction = longPressByIndex[a.Slot.Index]?.Action;
                    var longPressChord = longPressAction is not null && byActionName.TryGetValue(longPressAction, out var lpEntry) && lpEntry.IsBound
                        ? lpEntry.DisplayChord
                        : null;
                    longPress = new LongPressDto(a.Slot.LongPress.Status.ToString(), a.Slot.LongPress.Reason, longPressChord);
                }

                var stored = page.Slots.First(s => s.Index == a.Slot.Index);
                // The stored id translated to the flat index the client works
                // in. IndexOf over a handful of pages, once per folder slot -
                // and null when it resolves to nothing, so an orphaned
                // reference reaches the client as "not a folder" rather than
                // as a tap that navigates somewhere arbitrary.
                var folderPageIndex = stored.FolderPage is null
                    ? null
                    : FindPageIndexById(layout, stored.FolderPage);
                return new SlotDto(a.Slot.Index, a.Slot.Label, a.Slot.Status.ToString(), a.Slot.Reason, chord, litByIndex[a.Slot.Index].ToString(), longPress, stored.Latch, stored.Macro, stored.Hold, stored.FolderPage is not null, folderPageIndex);
            })
            .ToList();

        // A folder's interior is reached through its button, never through a
        // tab (the commander's own ruling) - so it is excluded from the names
        // the tab row draws. That breaks the "array position == page index"
        // assumption every tab was wired with, which is exactly what
        // PageIndices repairs: the tab at position i addresses
        // pageIndices[i], not i.
        var visiblePages = layout.Pages
            .Select((p, i) => (Page: p, Index: i))
            .Where(p => !p.Page.IsFolderOwned)
            .Select(p => (p.Index, p.Page.Name))
            .ToList();

        var parent = FindParentOfFolderPage(layout, page);

        var response = new PanelResponse(
            page.TemplateId,
            geometry.Cols,
            geometry.Rows,
            Math.Round(estimate.CellWidth, 1),
            Math.Round(estimate.CellHeight, 1),
            estimate.Verdict.ToString(),
            slots,
            HudThemeCssRenderer.Render(theme),
            allowances.HeaderStrip,
            allowances.FramePadding,
            allowances.TabStripHeight,
            pageIndex,
            layout.Pages.Count,
            visiblePages.Select(p => p.Name).ToList(),
            page.ShowWhen ?? Array.Empty<string>(),
            visiblePages.Select(p => p.Index).ToList(),
            page.Name,
            parent?.Index,
            parent?.Name);

        return BuildResult.Ok(response);
    }

    private static int? FindPageIndexById(Layout layout, string id)
    {
        for (var i = 0; i < layout.Pages.Count; i++)
        {
            if (string.Equals(layout.Pages[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>
    /// [2026-09-16] Which page owns the folder currently being viewed, found
    /// by the only route there is: scanning every slot in the layout for the
    /// one whose <see cref="LayoutSlot.FolderPage"/> names this page.
    /// <see cref="LayoutValidator"/> guarantees at most one such slot, which
    /// is what makes a reverse lookup an answer rather than a guess.
    ///
    /// <see langword="null"/> when the page is not a folder's interior at
    /// all, and ALSO when it is an ORPHAN (its button was cleared - an
    /// ordinary edit never destroys the page behind it). The client shows
    /// the up-one-level bar on exactly this signal, so an orphan correctly
    /// falls back to the normal tab row rather than offering a way "back" to
    /// a button that no longer points anywhere.
    /// </summary>
    private static (int Index, string Name)? FindParentOfFolderPage(Layout layout, LayoutPage page)
    {
        if (!page.IsFolderOwned || page.Id is null)
        {
            return null;
        }

        for (var i = 0; i < layout.Pages.Count; i++)
        {
            var candidate = layout.Pages[i];
            foreach (var slot in candidate.Slots.Concat(candidate.Parked))
            {
                if (string.Equals(slot.FolderPage, page.Id, StringComparison.Ordinal))
                {
                    return (i, candidate.Name);
                }
            }
        }

        return null;
    }
}
