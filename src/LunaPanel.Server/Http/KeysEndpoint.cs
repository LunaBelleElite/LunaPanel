using LunaPanel.Core.Bindings;
using LunaPanel.Core.Input;

namespace LunaPanel.Server.Http;

/// <summary>
/// <c>GET /api/keys</c>'s handler logic - the physical-key list the "press a
/// key" builder step's key picker grid is built from
/// (<c>ref/docs/macros.md</c>). Kept free of any ASP.NET type, same
/// discipline as <see cref="ActionsEndpoint"/>. Reads directly off
/// <see cref="Scancodes.All"/> rather than a second, hand-typed list, so a
/// key this project can actually inject is exactly a key the picker can
/// offer - no consumer maintains a copy of Frontier's <c>Key_*</c>
/// vocabulary.
/// </summary>
public static class KeysEndpoint
{
    /// <param name="Key">The physical key's <c>Key_*</c> name - what a chosen row round-trips into a <c>pressKey</c> step's own field.</param>
    /// <param name="Label">The same "Key_F5" -&gt; "F5" formatting <see cref="BindResolver"/> already uses for a bound key's display, never a second hand-typed prettifier.</param>
    public sealed record KeyDto(string Key, string Label);

    public static IReadOnlyList<KeyDto> BuildResponse() =>
        Scancodes.All.Keys
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Select(k => new KeyDto(k, BindResolver.PrettifyKeyName(k)))
            .ToList();
}
