using LunaPanel.Core.Layouts;

namespace LunaPanel.Server.Http;

/// <summary>
/// The assign/rename/clear verbs the on-device editor needs
/// (<c>ref/docs/editor.md</c>) - kept free of any ASP.NET type, same
/// discipline as <see cref="PanelTemplateEndpoint"/>/<see cref="PressEndpoint"/>.
/// Every method returns a fresh <see cref="Layout"/> with exactly one page's
/// <see cref="LayoutPage.Slots"/> changed; nothing here writes to disk or
/// validates action names against the player's bindings - the caller
/// (<see cref="Hosting.ServerHostBuilder"/>) is what calls
/// <see cref="LayoutStore.Save"/> with the result, exactly like
/// <see cref="PanelTemplateEndpoint"/>'s own callers do, so a genuinely
/// unknown action name is rejected there by
/// <see cref="LayoutValidator.ValidateForSave"/> rather than a second time
/// here.
/// </summary>
public static class SlotEditEndpoint
{
    public sealed record AssignRequest(int Page, int Slot, string? Action, string? Macro, string? Label);

    public sealed record LabelRequest(int Page, int Slot, string? Label);

    public sealed record ClearRequest(int Page, int Slot);

    public sealed record LongPressRequest(int Page, int Slot, string? Action, string? Macro);

    public sealed record LatchRequest(int Page, int Slot, bool Latch);

    /// <summary>[2026-09-12] The hold gesture's set-or-clear route body - same shape as <see cref="LatchRequest"/>.</summary>
    public sealed record HoldRequest(int Page, int Slot, bool Hold);

    public sealed record MoveRequest(int Page, int From, int To);

    /// <summary>[2026-09-16] "Make this button a folder" (<c>ref/docs/editor.md</c>) - the folder's name is the interior page's own name, and the template id is the panel size that page is created at.</summary>
    public sealed record MakeFolderRequest(int Page, int Slot, string? Name, string? TemplateId);

    public enum EditOutcome
    {
        Ok,
        NoSuchPage,
        UnknownTemplate,
        IndexOutOfRange,
        NoSuchSlot,
        InvalidRequest,
    }

    /// <param name="PageIndex">
    /// [2026-09-16] Set by <see cref="MakeFolder"/> alone: the index of the
    /// brand new folder-owned page it appended, so the caller can navigate
    /// straight into the folder it just made without a second lookup (the
    /// same courtesy <c>PageEditEndpoint.AddPage</c>'s own caller gets from
    /// <c>Pages.Count - 1</c>). <see langword="null"/> for every other verb
    /// here - none of them creates a page.
    /// </param>
    public sealed record EditResult(EditOutcome Outcome, Layout? Layout, int? PageIndex = null)
    {
        public static EditResult Ok(Layout layout) => new(EditOutcome.Ok, layout);
        public static EditResult Ok(Layout layout, int pageIndex) => new(EditOutcome.Ok, layout, pageIndex);
        public static EditResult Fail(EditOutcome outcome) => new(outcome, null);
    }

    /// <summary>
    /// Assigns exactly one of <paramref name="action"/>/<paramref name="macro"/>
    /// to <paramref name="slotIndex"/> on <paramref name="pageIndex"/> -
    /// replacing whatever was there, or creating a new active slot if the
    /// index was previously empty. Any existing long-press on that index is
    /// carried over unchanged; only the primary action/macro and label
    /// change. <paramref name="label"/> is the rename dialog's own result
    /// (<c>ref/docs/button-naming.md</c>) - <see langword="null"/> means "no
    /// override," exactly like every other override in this data model; it
    /// is the CALLER's job to have already decided that (comparing the
    /// dialog's final text against the default it was pre-filled with), not
    /// this method's.
    /// </summary>
    public static EditResult Assign(Layout layout, int pageIndex, int slotIndex, string? action, string? macro, string? label)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var hasAction = !string.IsNullOrEmpty(action);
        var hasMacro = !string.IsNullOrEmpty(macro);
        if (hasAction == hasMacro)
        {
            return EditResult.Fail(EditOutcome.InvalidRequest);
        }

        var located = LocatePageForAssign(layout, pageIndex, slotIndex);
        if (located.Outcome != EditOutcome.Ok)
        {
            return EditResult.Fail(located.Outcome);
        }

        var page = located.Page!;
        var existingLongPress = page.Slots.FirstOrDefault(s => s.Index == slotIndex)?.LongPress;

        // Latch is deliberately NOT carried over the way the long-press
        // above is, and the asymmetry is the point. A long-press is a second
        // action that keeps working regardless of what the primary becomes.
        // A latch is a MODE the primary action is fired in - carrying it
        // over would mean reassigning a latched secondary-fire button to
        // landing gear silently leaves the gear key held down until
        // something releases it, which is precisely the "keystroke that will
        // not stop" this feature's release rules exist to prevent
        // (ref/docs/latching-keys.md). Changing what a button does resets
        // how it fires.
        // [2026-09-12] Hold is dropped on reassign for the exact same
        // reason Latch is (see this method's remarks above it): reassigning
        // a held secondary-fire button to landing gear must not silently
        // leave the gear key held down until something releases it.
        var newSlot = new LayoutSlot(
            slotIndex,
            hasAction ? action : null,
            hasMacro ? macro : null,
            NormalizeLabel(label),
            existingLongPress,
            Latch: false,
            Hold: false);

        var slots = page.Slots.Where(s => s.Index != slotIndex).Append(newSlot).OrderBy(s => s.Index).ToList();
        return EditResult.Ok(ReplacePage(layout, pageIndex, page with { Slots = slots }));
    }

    /// <summary>
    /// [2026-09-16] Turns <paramref name="slotIndex"/> on
    /// <paramref name="pageIndex"/> into a FOLDER - a button that opens a
    /// nested page of its own buttons (<c>ref/docs/editor.md</c>). One verb
    /// does two things that must never come apart: it appends a new
    /// <see cref="LayoutPage.IsFolderOwned"/> page carrying a freshly minted
    /// <see cref="LayoutPage.Id"/>, and writes a slot naming that id. A
    /// folder button with no page, or a folder page with no button, is a
    /// layout <see cref="LayoutValidator"/> refuses - so neither half is ever
    /// written alone.
    ///
    /// The id is a <see cref="Guid"/>, not the folder's name and not its
    /// index: both of those repoint or orphan themselves under ordinary page
    /// edits (see <see cref="LayoutPage.Id"/>), and a rename is exactly what
    /// a commander does to a folder.
    ///
    /// Latch, hold and any long-press on the target index are DROPPED, the
    /// same way <see cref="Assign"/> drops them when a slot's job changes,
    /// and for a sharper version of the same reason: a folder fires no key at
    /// all, so a carried-over hold would leave a key down with nothing left
    /// on the button able to release it.
    ///
    /// <b>Not the reverse of Clear.</b> Nothing here deletes anything -
    /// running this over an existing folder slot mints a SECOND page and
    /// leaves the first as an intact, unreferenced orphan, which is the
    /// commander's own ruling applied consistently: no ordinary edit ever
    /// destroys a page full of buttons. Only an explicit
    /// <c>PageEditEndpoint.DeletePage</c> removes one.
    ///
    /// An empty <paramref name="folderName"/> is the ROUTE handler's refusal,
    /// not this method's - the same division
    /// <c>PageEditEndpoint.AddPage</c> already documents for a page name.
    /// </summary>
    public static EditResult MakeFolder(Layout layout, int pageIndex, int slotIndex, string folderName, string templateId)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(folderName);
        ArgumentNullException.ThrowIfNull(templateId);

        if (!Templates.TryGet(templateId, out var folderTemplate) || folderTemplate is null)
        {
            return EditResult.Fail(EditOutcome.UnknownTemplate);
        }

        var located = LocatePageForAssign(layout, pageIndex, slotIndex);
        if (located.Outcome != EditOutcome.Ok)
        {
            return EditResult.Fail(located.Outcome);
        }

        var page = located.Page!;

        var folderPageId = Guid.NewGuid().ToString("n");
        var folderPage = new LayoutPage(
            folderName,
            templateId,
            Array.Empty<LayoutSlot>(),
            Array.Empty<LayoutSlot>(),
            ShowWhen: null,
            Id: folderPageId,
            IsFolderOwned: true);

        var newSlot = new LayoutSlot(
            slotIndex,
            Action: null,
            Macro: null,
            Label: null,
            LongPress: null,
            Latch: false,
            Hold: false,
            FolderPage: folderPageId);

        var slots = page.Slots.Where(s => s.Index != slotIndex).Append(newSlot).OrderBy(s => s.Index).ToList();

        var pages = layout.Pages.ToList();
        pages[pageIndex] = page with { Slots = slots };
        pages.Add(folderPage);

        return EditResult.Ok(layout with { Pages = pages }, pages.Count - 1);
    }

    /// <summary>
    /// Sets (or, when <paramref name="label"/> is <see langword="null"/> or
    /// blank, clears) an already-assigned slot's label override - the slot
    /// sheet's standalone "rename" action, distinct from re-running the
    /// whole assign flow. Refuses with <see cref="EditOutcome.NoSuchSlot"/>
    /// rather than creating one - a rename can only ever apply to a slot
    /// that already names an action or macro.
    /// </summary>
    public static EditResult SetLabel(Layout layout, int pageIndex, int slotIndex, string? label)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (pageIndex < 0 || pageIndex >= layout.Pages.Count)
        {
            return EditResult.Fail(EditOutcome.NoSuchPage);
        }

        var page = layout.Pages[pageIndex];
        var existing = page.Slots.FirstOrDefault(s => s.Index == slotIndex);
        if (existing is null)
        {
            return EditResult.Fail(EditOutcome.NoSuchSlot);
        }

        var updated = existing with { Label = NormalizeLabel(label) };
        var slots = page.Slots.Select(s => s.Index == slotIndex ? updated : s).ToList();
        return EditResult.Ok(ReplacePage(layout, pageIndex, page with { Slots = slots }));
    }

    /// <summary>
    /// Sets (exactly one of <paramref name="action"/>/<paramref name="macro"/>
    /// non-empty) or clears (both empty/null) <paramref name="slotIndex"/>'s
    /// <see cref="LayoutSlot.LongPress"/> - the slot sheet's "set a
    /// long-press action" verb (<c>ref/docs/editor.md</c>). Same set-or-clear
    /// convention <see cref="SetLabel"/> already uses for the primary label,
    /// rather than a second, dedicated clear endpoint. Refuses with
    /// <see cref="EditOutcome.NoSuchSlot"/> when the slot isn't already
    /// occupied - a long-press attaches to an existing primary slot, exactly
    /// as <see cref="LongPressAction"/>'s own remarks describe (only the
    /// primary slot carries a label; a long-press is not a way to create a
    /// slot from nothing). Passing BOTH a non-empty action and a non-empty
    /// macro is <see cref="EditOutcome.InvalidRequest"/>, matching
    /// <see cref="LayoutValidator"/>'s own "exactly one" rule for a
    /// long-press.
    /// </summary>
    public static EditResult SetLongPress(Layout layout, int pageIndex, int slotIndex, string? action, string? macro)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var hasAction = !string.IsNullOrEmpty(action);
        var hasMacro = !string.IsNullOrEmpty(macro);
        if (hasAction && hasMacro)
        {
            return EditResult.Fail(EditOutcome.InvalidRequest);
        }

        if (pageIndex < 0 || pageIndex >= layout.Pages.Count)
        {
            return EditResult.Fail(EditOutcome.NoSuchPage);
        }

        var page = layout.Pages[pageIndex];
        var existing = page.Slots.FirstOrDefault(s => s.Index == slotIndex);
        if (existing is null)
        {
            return EditResult.Fail(EditOutcome.NoSuchSlot);
        }

        var longPress = hasAction || hasMacro
            ? new LongPressAction(hasAction ? action : null, hasMacro ? macro : null)
            : null;

        var updated = existing with { LongPress = longPress };
        var slots = page.Slots.Select(s => s.Index == slotIndex ? updated : s).ToList();
        return EditResult.Ok(ReplacePage(layout, pageIndex, page with { Slots = slots }));
    }

    /// <summary>
    /// Turns an already-assigned slot's latching on or off - the slot
    /// sheet's "Hold until tapped again" verb (<c>ref/docs/latching-keys.md</c>).
    /// Set-or-clear through one route with a boolean, the same convention
    /// <see cref="SetLabel"/> and <see cref="SetLongPress"/> already use
    /// rather than a second route just to turn it off.
    ///
    /// Refuses with <see cref="EditOutcome.NoSuchSlot"/> for an empty slot
    /// (there is no key to hold) and with <see cref="EditOutcome.InvalidRequest"/>
    /// for a macro slot - a macro is a sequence of keystrokes, not one key
    /// that can be held down, which <see cref="LayoutValidator"/> would
    /// reject at save time anyway. Refusing here means the commander is told
    /// "a macro cannot be latched" rather than being shown a save error
    /// about a layout they did not know they had broken.
    /// </summary>
    public static EditResult SetLatch(Layout layout, int pageIndex, int slotIndex, bool latch)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (pageIndex < 0 || pageIndex >= layout.Pages.Count)
        {
            return EditResult.Fail(EditOutcome.NoSuchPage);
        }

        var page = layout.Pages[pageIndex];
        var existing = page.Slots.FirstOrDefault(s => s.Index == slotIndex);
        if (existing is null)
        {
            return EditResult.Fail(EditOutcome.NoSuchSlot);
        }

        if (latch && existing.Action is null)
        {
            return EditResult.Fail(EditOutcome.InvalidRequest);
        }

        // [2026-09-12] Turning latching ON implies holding turns OFF - a
        // slot fires exactly one of the two gestures over the same key
        // (LayoutValidator rejects both set together), and the sheet's own
        // "vice versa" requirement is enforced here rather than left for a
        // save-time rejection the commander would have to puzzle out.
        // Turning latching OFF never touches Hold - see SetHold's own
        // remarks for why this asymmetry is deliberate.
        var updated = existing with { Latch = latch, Hold = latch ? false : existing.Hold };
        var slots = page.Slots.Select(s => s.Index == slotIndex ? updated : s).ToList();
        return EditResult.Ok(ReplacePage(layout, pageIndex, page with { Slots = slots }));
    }

    /// <summary>
    /// Turns an already-assigned slot's hold-while-pressed gesture on or off
    /// (<c>ref/docs/latching-keys.md</c>'s hold-to-thrust extension) - the
    /// slot sheet's "Hold while pressed" verb, and otherwise the exact
    /// mirror of <see cref="SetLatch"/>: same set-or-clear-through-one-route
    /// convention, same <see cref="EditOutcome.NoSuchSlot"/> refusal for an
    /// empty slot (there is no key to hold), same
    /// <see cref="EditOutcome.InvalidRequest"/> refusal for a macro slot (a
    /// macro is a sequence of keystrokes, not one key that can be held).
    ///
    /// Turning holding ON implies latching turns OFF, the same "vice versa"
    /// <see cref="SetLatch"/> already enforces from the other side - a slot
    /// fires exactly one of the two gestures over the same key. Turning
    /// holding OFF never touches Latch, matching <see cref="SetLatch"/>'s
    /// own asymmetry: clearing the gesture that was just turned off should
    /// never silently revive the other one a commander did not ask for.
    /// </summary>
    public static EditResult SetHold(Layout layout, int pageIndex, int slotIndex, bool hold)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (pageIndex < 0 || pageIndex >= layout.Pages.Count)
        {
            return EditResult.Fail(EditOutcome.NoSuchPage);
        }

        var page = layout.Pages[pageIndex];
        var existing = page.Slots.FirstOrDefault(s => s.Index == slotIndex);
        if (existing is null)
        {
            return EditResult.Fail(EditOutcome.NoSuchSlot);
        }

        if (hold && existing.Action is null)
        {
            return EditResult.Fail(EditOutcome.InvalidRequest);
        }

        var updated = existing with { Hold = hold, Latch = hold ? false : existing.Latch };
        var slots = page.Slots.Select(s => s.Index == slotIndex ? updated : s).ToList();
        return EditResult.Ok(ReplacePage(layout, pageIndex, page with { Slots = slots }));
    }

    /// <summary>
    /// Moves the button at <paramref name="fromIndex"/> to
    /// <paramref name="toIndex"/> on one page - the verb behind the
    /// tablet's edit-mode drag gesture (<c>ref/docs/editor.md</c>).
    ///
    /// <b>It is a SWAP, not an insert-and-push.</b> Dragging onto an
    /// occupied index exchanges the two slots; the alternative - pushing
    /// the occupant along - has no defined direction on a two-dimensional
    /// grid and can overflow the template's last index, which would mean
    /// either destroying a slot or inventing a parking rule. That is
    /// precisely the "interacts with spill and parking in ways nobody has
    /// thought through" <c>editor.md</c> named when it left this undecided.
    /// A swap touches exactly two indices, can never overflow, and is its
    /// own undo - dragging back returns the layout it started from.
    ///
    /// An EMPTY target is the degenerate case of the same swap: the source
    /// vacates and the target becomes occupied, through the same code path
    /// rather than a special case. An empty SOURCE is refused with
    /// <see cref="EditOutcome.NoSuchSlot"/>, matching
    /// <see cref="SetLabel"/>/<see cref="SetLongPress"/>/<see cref="SetLatch"/>'s
    /// own convention for a verb that acts on a slot which already exists,
    /// rather than inventing a swap-with-nothing semantic no screen can
    /// reach. Moving a slot onto ITSELF is <see cref="EditOutcome.Ok"/> and
    /// changes nothing - a drag that ends where it began is a cancel, not
    /// an error.
    ///
    /// Everything about both slots is carried across unchanged; only their
    /// <see cref="LayoutSlot.Index"/> differs. That includes
    /// <see cref="LayoutSlot.Latch"/>, which <see cref="Assign"/>
    /// deliberately drops - the asymmetry is the same one Assign's remarks
    /// explain from the other side: a move changes WHERE a button is, never
    /// what it does or the mode it fires in.
    ///
    /// <b>One page only.</b> Both indices are on <paramref name="pageIndex"/>;
    /// there is no cross-page form of this verb, deliberately (see
    /// <c>ref/docs/editor.md</c>). Both must lie in the template's active
    /// range, which is also what keeps a move from ever landing on a
    /// <see cref="LayoutPage.Parked"/> index - the same guard
    /// <see cref="Assign"/> relies on.
    /// </summary>
    public static EditResult Move(Layout layout, int pageIndex, int fromIndex, int toIndex)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var located = LocatePageForAssign(layout, pageIndex, fromIndex);
        if (located.Outcome != EditOutcome.Ok)
        {
            return EditResult.Fail(located.Outcome);
        }

        var targetLocated = LocatePageForAssign(layout, pageIndex, toIndex);
        if (targetLocated.Outcome != EditOutcome.Ok)
        {
            return EditResult.Fail(targetLocated.Outcome);
        }

        var page = located.Page!;
        var source = page.Slots.FirstOrDefault(s => s.Index == fromIndex);
        if (source is null)
        {
            return EditResult.Fail(EditOutcome.NoSuchSlot);
        }

        if (fromIndex == toIndex)
        {
            return EditResult.Ok(layout);
        }

        var target = page.Slots.FirstOrDefault(s => s.Index == toIndex);

        var slots = page.Slots
            .Where(s => s.Index != fromIndex && s.Index != toIndex)
            .Append(source with { Index = toIndex });

        if (target is not null)
        {
            slots = slots.Append(target with { Index = fromIndex });
        }

        return EditResult.Ok(ReplacePage(layout, pageIndex, page with { Slots = slots.OrderBy(s => s.Index).ToList() }));
    }

    /// <summary>
    /// Empties a slot - removes it from the page's active
    /// <see cref="LayoutPage.Slots"/> entirely, never the page itself
    /// ("must not be confusable with deleting the page,"
    /// <c>ref/docs/editor.md</c>). Idempotent: clearing an already-empty
    /// slot is <see cref="EditOutcome.Ok"/>, not a refusal - a commander
    /// hitting Clear twice shouldn't see an error. Never touches
    /// <see cref="LayoutPage.Parked"/>, which this verb has no reach into.
    /// </summary>
    public static EditResult Clear(Layout layout, int pageIndex, int slotIndex)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (pageIndex < 0 || pageIndex >= layout.Pages.Count)
        {
            return EditResult.Fail(EditOutcome.NoSuchPage);
        }

        var page = layout.Pages[pageIndex];
        var slots = page.Slots.Where(s => s.Index != slotIndex).ToList();
        return EditResult.Ok(ReplacePage(layout, pageIndex, page with { Slots = slots }));
    }

    private static (EditOutcome Outcome, LayoutPage? Page) LocatePageForAssign(Layout layout, int pageIndex, int slotIndex)
    {
        if (pageIndex < 0 || pageIndex >= layout.Pages.Count)
        {
            return (EditOutcome.NoSuchPage, null);
        }

        var page = layout.Pages[pageIndex];

        if (!Templates.TryGet(page.TemplateId, out var template) || template is null)
        {
            return (EditOutcome.UnknownTemplate, null);
        }

        // The active range is exactly [0, template.Slots) - the same range
        // GET /api/panel ever renders a cell for. A parked slot's index is
        // always outside this range by construction (LayoutParker only
        // parks an index once it no longer fits: index >= newTemplate.Slots),
        // so this one check is also what keeps assign from ever landing on
        // a parked index and violating LayoutValidator's "can't be both
        // active and parked at once" rule.
        if (slotIndex < 0 || slotIndex >= template.Slots)
        {
            return (EditOutcome.IndexOutOfRange, null);
        }

        return (EditOutcome.Ok, page);
    }

    private static string? NormalizeLabel(string? label) => string.IsNullOrWhiteSpace(label) ? null : label;

    private static Layout ReplacePage(Layout layout, int pageIndex, LayoutPage newPage)
    {
        var pages = layout.Pages.ToList();
        pages[pageIndex] = newPage;
        return layout with { Pages = pages };
    }
}
