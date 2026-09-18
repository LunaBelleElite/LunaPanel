using LunaPanel.Core.Layouts;

namespace LunaPanel.Server.Http;

/// <summary>
/// <c>GET/POST /api/panel/settings</c>'s handler logic - the "merging and
/// expanding panels" toggle (<c>ref/docs/panels-and-pages.md</c>), the
/// commander's own name for it, used verbatim. Kept free of any ASP.NET
/// type, same discipline as <see cref="ThemeEndpoint"/>.
/// </summary>
public static class PanelSettingsEndpoint
{
    public sealed record Request(bool MergeExpand, bool ShowMacroStepResults, bool AutoSwitchEnabled = true);

    public sealed record Response(bool MergeExpand, bool ShowMacroStepResults, bool AutoSwitchEnabled);

    public static Response BuildResponse(PanelSettings settings) => new(settings.MergeExpand, settings.ShowMacroStepResults, settings.AutoSwitchEnabled);
}
