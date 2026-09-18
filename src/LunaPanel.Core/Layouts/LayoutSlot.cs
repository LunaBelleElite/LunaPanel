namespace LunaPanel.Core.Layouts;

/// <summary>
/// One slot on a page: names an action (by Frontier element name) or a
/// macro (by id) - never a key, a resolved label, or lit state. This is
/// "a layout stores intent, never resolution" (see
/// <c>ref/docs/design-decisions.md</c>) applied to a single slot: whatever
/// this slot resolves to is recomputed fresh on every read by
/// <see cref="LayoutAnnotator"/>, never cached here.
///
/// Exactly one of <see cref="Action"/> / <see cref="Macro"/> is set - this
/// record itself stays lenient about it (matching this codebase's
/// parse-lenient/validate-separately convention, e.g.
/// <c>LunaPanel.Core.Catalogue.Catalogue</c> vs its callers); the rule is
/// enforced by <see cref="LayoutValidator"/>.
///
/// <see cref="Label"/> is present only when the user overrode the curated
/// or prettified label - <see langword="null"/> means "use whatever the
/// catalogue currently says", so a later curated-label improvement flows
/// into every layout that never overrode it.
/// </summary>
/// <param name="Index">Position within the page's template - unique within the page (see <see cref="LayoutValidator"/>).</param>
/// <param name="Action">The Frontier action element name, or <see langword="null"/> when this slot names a macro instead.</param>
/// <param name="Macro">The macro id, or <see langword="null"/> when this slot names an action instead.</param>
/// <param name="Label">The user's own override label - at most <see cref="LayoutValidator.UserLabelMaxLines"/> lines of at most <see cref="LayoutValidator.UserLabelLineCap"/> characters each, stored with an embedded newline (see <see cref="LayoutValidator"/>) - or <see langword="null"/> when not overridden.</param>
/// <param name="LongPress">The optional second action/macro fired on long-press instead of tap.</param>
/// <param name="Latch">
/// Whether tapping this slot <em>latches</em> its action - presses the key
/// and leaves it down until tapped again - instead of tapping it
/// (<c>ref/docs/latching-keys.md</c>). A fourth thing a slot can do, beside a
/// tap, a long press and a macro.
///
/// Only ever <see langword="true"/> alongside <see cref="Action"/>: a macro
/// is a sequence of keystrokes, not one key that can be held, so there is
/// nothing for a latch to hold - enforced by <see cref="LayoutValidator"/>,
/// not by this record, matching this record's existing discipline about
/// action/macro exclusivity.
///
/// Defaulted so every layout written before this feature existed - and
/// every one of this codebase's existing construction sites - keeps meaning
/// exactly what it did.
/// </param>
/// <param name="Hold">
/// [2026-09-12] Whether this slot's action is held for as long as a finger
/// stays on the button, and released the instant it lifts - a genuine held
/// key rather than a quick tap or a fixed-duration chord, for a control like
/// ship thrust where Elite needs to see the key stay down
/// (<c>ref/docs/latching-keys.md</c>'s hold-to-thrust extension). A fifth
/// thing a slot can do, and mutually exclusive with <see cref="Latch"/> - a
/// tap-to-toggle and a press-and-hold are two different gestures over the
/// same one key, and a slot fires exactly one of them - enforced by
/// <see cref="LayoutValidator"/>, not by this record, same discipline as the
/// action/macro and latch/action exclusivity checks already there.
///
/// Defaulted so every layout written before this feature existed - and
/// every one of this codebase's existing construction sites - keeps meaning
/// exactly what it did.
/// </param>
/// <param name="FolderPage">
/// [2026-09-16] The <see cref="LayoutPage.Id"/> of the page this slot OPENS
/// when tapped, or <see langword="null"/> when this slot is not a folder.
/// A sixth thing a slot can do, and the first that is navigation rather than
/// a keystroke.
///
/// Mutually exclusive with <see cref="Action"/> and <see cref="Macro"/> -
/// enforced by <see cref="LayoutValidator"/>'s three-way exactly-one-of
/// check, not by this record, matching this record's existing discipline
/// about action/macro exclusivity. A folder is also never latched or held:
/// both gestures are modes a single KEY is fired in, and a folder fires no
/// key at all.
///
/// Stored as an id, never as a page index or a page name - see
/// <see cref="LayoutPage.Id"/> for why both of those silently repoint or
/// orphan themselves under ordinary page edits.
///
/// Nesting is capped at exactly one level: the page this names is
/// <see cref="LayoutPage.IsFolderOwned"/>, and a folder-owned page's own
/// slots may never carry this field.
///
/// Defaulted so every layout written before folders existed - and every one
/// of this codebase's existing construction sites - keeps meaning exactly
/// what it did.
/// </param>
public sealed record LayoutSlot(int Index, string? Action, string? Macro, string? Label, LongPressAction? LongPress, bool Latch = false, bool Hold = false, string? FolderPage = null);
