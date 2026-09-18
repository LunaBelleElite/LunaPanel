namespace LunaPanel.Core.Layouts;

/// <summary>
/// The freshly-recomputed status of one slot's primary action/macro, and of
/// its long-press action/macro if it has one. Produced by
/// <see cref="LayoutAnnotator"/>; never persisted back to the layout.
/// </summary>
/// <param name="Label">
/// The slot's resolved display label - <c>slot.Label ?? catalogue
/// DisplayLabel ?? Prettify(elementName)</c> (or <c>Prettify(macroId)</c> for
/// a macro slot). Resolved fresh on every annotate, exactly like
/// <see cref="Status"/> and <see cref="Reason"/> - never persisted back to
/// the layout. See <c>ref/docs/button-naming.md</c>.
/// </param>
public sealed record SlotAnnotation(int Index, string Label, SlotStatus Status, string Reason, LongPressAnnotation? LongPress);
