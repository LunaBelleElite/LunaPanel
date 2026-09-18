using LunaPanel.Core.Catalogue;

namespace LunaPanel.Server.Http;

/// <summary>
/// <c>GET /api/actions</c>'s handler logic - exposes
/// <see cref="CatalogueMerger.Merge"/>/<see cref="CatalogueMerger.MergeAll"/>'s
/// rows to the on-device editor's action picker (<c>ref/docs/editor.md</c>).
/// Kept free of any ASP.NET type, same discipline as every other endpoint
/// type in this namespace. Which merge mode to build is
/// <see cref="Hosting.ServerHostBuilder"/>'s own choice (the <c>all</c> query
/// flag, the picker's "show everything" toggle) - this type only maps
/// whatever list it's handed into the wire shape.
/// </summary>
public static class ActionsEndpoint
{
    public sealed record ActionDto(
        string ActionName,
        string DisplayLabel,
        string? Category,
        bool IsCurated,
        bool IsBound,
        string? DisplayChord,
        string? UnboundReason);

    public static IReadOnlyList<ActionDto> BuildResponse(IReadOnlyList<CataloguePickerEntry> merge) =>
        merge
            .Select(e => new ActionDto(
                e.ActionName,
                e.DisplayLabel,
                e.Category,
                e.IsCurated,
                e.IsBound,
                e.DisplayChord,
                e.UnboundReason?.ToString()))
            .ToList();
}
