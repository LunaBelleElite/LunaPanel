namespace LunaPanel.Core.Layouts;

/// <summary>
/// One named page on a device: its own template plus the slots assigned to
/// it (see <c>ref/docs/design-decisions.md</c>'s "Pages from the start").
/// <see cref="Parked"/> holds slots that no longer fit the current
/// template's slot count - never deleted, so shrinking a template can't
/// destroy an assigned action; see <see cref="LayoutParker"/> for the pure
/// function that moves slots between <see cref="Slots"/> and
/// <see cref="Parked"/> when the template changes.
/// </summary>
/// <param name="Name">The page's display name (e.g. "SHIP").</param>
/// <param name="TemplateId">The <see cref="PanelTemplate.Id"/> this page currently uses.</param>
/// <param name="Slots">Slots currently placed on the page's template.</param>
/// <param name="Parked">Slots that don't currently fit the template, each keeping the index it held when it was parked.</param>
/// <param name="ShowWhen">
/// The page's declared vessel context, as <c>showWhen</c> condition tokens
/// ANDed together (see <see cref="LunaPanel.Core.GameState.ShowWhenConditionList"/>
/// and <c>ref/docs/vessel-context.md</c>). <see langword="null"/> or empty
/// means the page is <em>context-free</em>: always available by hand, and
/// never a page automatic switching will move TO. A page is only ever
/// switched to automatically when it declares a context that matches - see
/// <see cref="ContextPageSelector"/>.
/// </param>
/// <param name="Id">
/// [2026-09-16] A stable, server-minted identifier for this page, or
/// <see langword="null"/> when nothing has ever needed one (which is every
/// page written before folders existed, and every page no folder button
/// points at).
///
/// It exists for exactly one reason: a folder slot
/// (<see cref="LayoutSlot.FolderPage"/>) has to name the page it opens, and
/// neither of the two things a page already had is safe to name it by.
/// <see cref="Layout.Pages"/> is a flat list addressed only by POSITION, so
/// any add/delete/reorder elsewhere silently repoints an index; and
/// <see cref="Name"/> is explicitly non-unique AND mutable, so a rename
/// orphans the reference and a duplicate name makes it ambiguous. An id
/// minted once and never rewritten is the only reference that survives both
/// - which is also why <c>AddPage</c>/<c>MovePage</c>/<c>RenamePage</c>
/// needed no folder-awareness at all.
///
/// Minted by <see cref="LunaPanel.Server"/>'s slot editor when a folder is
/// first created against this page, never by a load or a parse.
/// </param>
/// <param name="IsFolderOwned">
/// [2026-09-16] Whether this page is a folder's INTERIOR - the page a folder
/// button opens into - rather than one of the device's own top-level pages.
///
/// Stored rather than inferred by rescanning every other page's slots for a
/// reference to this one, because it is asked on every single panel response
/// (the tab bar must exclude these pages) and because the two questions are
/// genuinely different: an interior page whose folder button was cleared is
/// STILL a folder's interior - an intact, unreferenced orphan, never
/// destroyed by an ordinary edit - and must stay out of the tab bar rather
/// than reappearing there as a mystery page the moment nothing points at it.
///
/// Also the one-level nesting cap's anchor:
/// <see cref="LayoutValidator"/> rejects any <see cref="LayoutSlot.FolderPage"/>
/// on a page that is itself folder-owned.
/// </param>
public sealed record LayoutPage(string Name, string TemplateId, IReadOnlyList<LayoutSlot> Slots, IReadOnlyList<LayoutSlot> Parked, IReadOnlyList<string>? ShowWhen = null, string? Id = null, bool IsFolderOwned = false);
