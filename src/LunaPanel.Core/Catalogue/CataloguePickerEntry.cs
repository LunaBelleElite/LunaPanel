using LunaPanel.Core.Bindings;

namespace LunaPanel.Core.Catalogue;

/// <summary>
/// One row of the action picker, produced by <see cref="CatalogueMerger.Merge"/>:
/// a raw Frontier action name joined with whatever the curated catalogue and
/// the player's resolved bindings know about it.
/// </summary>
/// <param name="ActionName">The raw Frontier binding element name.</param>
/// <param name="DisplayLabel">The curated label if this action is curated, otherwise <see cref="Prettifier.Prettify"/> applied to <paramref name="ActionName"/>.</param>
/// <param name="Category">The curated category id, or <see langword="null"/> when this action isn't curated.</param>
/// <param name="IsCurated">Whether this action has a curated catalogue entry.</param>
/// <param name="IsBound">Whether <see cref="BindResolver"/> resolved this action to a keyboard chord.</param>
/// <param name="DisplayChord">The resolved chord's display text, or <see langword="null"/> when unbound.</param>
/// <param name="UnboundReason">Why the action is unbound, or <see langword="null"/> when bound.</param>
public sealed record CataloguePickerEntry(
    string ActionName,
    string DisplayLabel,
    string? Category,
    bool IsCurated,
    bool IsBound,
    string? DisplayChord,
    UnboundReason? UnboundReason);
