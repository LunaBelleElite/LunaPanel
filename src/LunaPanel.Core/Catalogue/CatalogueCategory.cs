namespace LunaPanel.Core.Catalogue;

/// <summary>
/// One declared category in the curated action catalogue - just an id and a
/// display label. Ids are what a <see cref="CatalogueAction"/> references;
/// labels are what a category header renders as.
/// </summary>
public sealed record CatalogueCategory(string Id, string Label);
