namespace LunaPanel.Core.Layouts;

/// <summary>
/// A slot's optional second action, fired on long-press instead of tap (see
/// <c>ref/docs/design-decisions.md</c>'s "Long-press as an optional second
/// action per slot"). Exactly one of <see cref="Action"/> / <see cref="Macro"/>
/// is set - enforced by <see cref="LayoutValidator"/>, not by this record,
/// matching <see cref="LayoutSlot"/>'s own discipline. Carries no label of
/// its own; only the primary slot can be labeled.
/// </summary>
/// <param name="Action">The Frontier action element name, or <see langword="null"/> when this names a macro instead.</param>
/// <param name="Macro">The macro id, or <see langword="null"/> when this names an action instead.</param>
public sealed record LongPressAction(string? Action, string? Macro);
