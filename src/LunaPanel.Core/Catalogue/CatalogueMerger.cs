using LunaPanel.Core.Bindings;

namespace LunaPanel.Core.Catalogue;

/// <summary>
/// Joins a curated <see cref="Catalogue"/> with a player's resolved
/// <see cref="BindingsFile"/> into the rows the action picker consumes.
///
/// Every curated action is included, bound or not - an unbound curated
/// action is still shown (greyed, with its <see cref="UnboundReason"/>) so
/// the player can see why a familiar button isn't available rather than
/// wondering where it went. An uncurated action is only included when it
/// resolves to a keyboard chord: nobody needs to scroll past hundreds of
/// entries for actions they can neither see labeled nor actually use.
///
/// <see cref="MergeAll"/> is the commander's "show everything" mode
/// (<c>ref/docs/editor.md</c>, 2026-09-07) - an additional method rather
/// than a change to <see cref="Merge"/> itself, so every existing caller
/// (<c>PanelEndpoint</c>, <c>PressEndpoint</c>) keeps its current, shorter
/// list unchanged.
/// </summary>
public static class CatalogueMerger
{
    public static IReadOnlyList<CataloguePickerEntry> Merge(Catalogue catalogue, BindingsFile bindingsFile)
    {
        var entries = BuildCuratedEntries(catalogue, bindingsFile);

        foreach (var element in bindingsFile.Elements)
        {
            if (catalogue.Actions.ContainsKey(element.Name))
            {
                continue;
            }

            var resolution = BindResolver.Resolve(element);
            if (!resolution.IsBound)
            {
                continue;
            }

            entries.Add(BuildUncuratedEntry(element, resolution));
        }

        return entries;
    }

    /// <summary>
    /// Every curated action, bound or not (identical to <see cref="Merge"/>),
    /// plus EVERY uncurated element the game exposes - unbound ones included
    /// and visibly marked as such, rather than omitted. This is what lets
    /// the action picker's "show everything" toggle offer "any single
    /// control available, even if it's not mapped" (the commander's own
    /// words, <c>ref/docs/editor.md</c>) with the unbound ones marked before
    /// assignment rather than only discovered after.
    /// </summary>
    public static IReadOnlyList<CataloguePickerEntry> MergeAll(Catalogue catalogue, BindingsFile bindingsFile)
    {
        var entries = BuildCuratedEntries(catalogue, bindingsFile);

        foreach (var element in bindingsFile.Elements)
        {
            if (catalogue.Actions.ContainsKey(element.Name))
            {
                continue;
            }

            entries.Add(BuildUncuratedEntry(element, BindResolver.Resolve(element)));
        }

        return entries;
    }

    private static List<CataloguePickerEntry> BuildCuratedEntries(Catalogue catalogue, BindingsFile bindingsFile)
    {
        var entries = new List<CataloguePickerEntry>();

        foreach (var (actionName, action) in catalogue.Actions)
        {
            var resolution = BindResolver.Resolve(bindingsFile, actionName);
            entries.Add(new CataloguePickerEntry(
                actionName,
                action.Label,
                action.Category,
                IsCurated: true,
                IsBound: resolution.IsBound,
                DisplayChord: resolution.Chord?.DisplayText,
                UnboundReason: resolution.IsBound ? null : resolution.Reason));
        }

        return entries;
    }

    private static CataloguePickerEntry BuildUncuratedEntry(BindingElement element, BindResolution resolution) =>
        new(
            element.Name,
            Prettifier.Prettify(element.Name),
            Category: null,
            IsCurated: false,
            IsBound: resolution.IsBound,
            DisplayChord: resolution.Chord?.DisplayText,
            UnboundReason: resolution.IsBound ? null : resolution.Reason);
}
