namespace LunaPanel.Core.Layouts;

/// <summary>
/// The freshly-recomputed status of a slot's long-press action, produced by
/// <see cref="LayoutAnnotator"/>.
/// </summary>
/// <param name="Label">
/// The long-press action/macro's resolved display label - <c>catalogue
/// DisplayLabel ?? Prettify(elementName)</c> (or <c>Prettify(macroId)</c> for
/// a macro). <see cref="LongPressAction"/> carries no label field of its
/// own (see <c>ref/docs/button-naming.md</c>'s "Undecided" section), so
/// there is no override to consult here - unlike the primary slot's
/// <see cref="SlotAnnotation.Label"/>.
/// </param>
public sealed record LongPressAnnotation(SlotStatus Status, string Reason, string Label);
