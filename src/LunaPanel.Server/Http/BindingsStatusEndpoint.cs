using LunaPanel.Core.Bindings;
using LunaPanel.Server.Discovery;

namespace LunaPanel.Server.Http;

/// <summary>
/// <c>GET/POST /api/bindings/status</c> and <c>POST /api/bindings/refresh</c>'s
/// handler logic, kept free of any ASP.NET type - same discipline as
/// <see cref="ThemeEndpoint"/>/<see cref="PanelSettingsEndpoint"/>. This is
/// "the actual feature" <c>ref/docs/bindings-source.md</c> describes: making
/// a wrong or empty pick visible, rather than presenting a silently dead
/// panel as if nothing were wrong.
/// </summary>
public static class BindingsStatusEndpoint
{
    public sealed record StatusResponse(
        string? PresetName,
        string SourceKind,
        string? Version,
        int BoundActionCount,
        int TotalActionCount,
        bool PresetNameMismatch);

    /// <summary>
    /// The three-way (four, counting "nothing found at all") bucket the
    /// settings pane shows words for. Fallback is surfaced as its own kind
    /// rather than folded into CommanderAuthored, even though a fallback
    /// pick is always a commander file when one exists at all - the whole
    /// point of flagging a fallback (rule step 4) is that it was NOT the
    /// StartPreset-driven choice the rule prefers, and collapsing that
    /// distinction away would silently undo the visibility this task exists
    /// to add.
    /// </summary>
    public static string SourceKind(PresetSelectionResult selection) => selection switch
    {
        { SelectedFilePath: null } => "NotFound",
        { Method: PresetSelectionMethod.Fallback } => "Fallback",
        { Origin: PresetOrigin.Stock } => "Stock",
        _ => "CommanderAuthored",
    };

    /// <param name="selection">The current rule outcome (<c>PathDiscoveryResult.BindingsSelection</c>).</param>
    /// <param name="file">The same file <see cref="LunaPanel.Server.Bindings.LiveBindingsReader.Read"/> just parsed - reused here rather than re-read a second time, so the count reported can never disagree with what the panel itself just rendered from.</param>
    public static StatusResponse BuildResponse(PresetSelectionResult selection, BindingsFile file)
    {
        var boundCount = file.Elements.Count(e => BindResolver.Resolve(e).IsBound);

        // The file's own parsed PresetName is preferred over the selecting
        // StartPreset name (SelectedPresetName) - it is what a commander
        // will recognize from Elite's own preset list, and step 5's
        // verification means the two normally agree anyway; on the rare
        // mismatch, showing the file's own name plus the mismatch flag is
        // more honest than silently showing the name that was actually
        // wrong for this file.
        var presetName = file.PresetName ?? selection.SelectedPresetName;
        var version = selection.Version?.ToString();

        return new StatusResponse(
            presetName,
            SourceKind(selection),
            version,
            boundCount,
            file.Elements.Count,
            selection.PresetNameMismatch);
    }
}
