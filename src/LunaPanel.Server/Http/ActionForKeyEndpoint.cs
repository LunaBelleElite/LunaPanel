using LunaPanel.Core.Bindings;
using LunaPanel.Core.Catalogue;
using LunaPanel.Core.Input;

namespace LunaPanel.Server.Http;

/// <summary>
/// <c>GET /api/actions/for-key</c>'s handler logic - the reverse lookup
/// behind the "press a key" builder step's "use that control instead?" offer
/// (<c>ref/docs/macros.md</c>). Maps <see cref="KeyBindingLookup"/>'s bare
/// action name into the wire shape the client needs to show something
/// readable, the same "endpoint only maps, a Core type decides" split
/// <see cref="ActionsEndpoint"/> already follows.
/// </summary>
public static class ActionForKeyEndpoint
{
    /// <param name="Matched">The bound action's name, or <see langword="null"/> when nothing in Elite is bound to this key.</param>
    /// <param name="MatchedLabel">A readable label for <see cref="Matched"/> - <see langword="null"/> exactly when <see cref="Matched"/> is.</param>
    /// <param name="ResolvedKey">The <see cref="Scancodes"/> <c>Key_*</c> name the caller's DOM code resolved to - present on every successful response, matched or not.</param>
    /// <param name="ResolvedKeyLabel">The same "Key_F5" -&gt; "F5" display formatting <c>KeysEndpoint</c> already uses, for <see cref="ResolvedKey"/>.</param>
    public sealed record Response(string? Matched, string? MatchedLabel, string ResolvedKey, string ResolvedKeyLabel);

    /// <summary>
    /// Resolves <paramref name="domCode"/> - a browser
    /// <c>KeyboardEvent.code</c> string, e.g. <c>"KeyW"</c> - via
    /// <see cref="BrowserKeyCodeMap.TryMap"/>, then runs the reverse bound-
    /// action lookup against that key. Returns <see langword="null"/> when
    /// <paramref name="domCode"/> maps to no known key at all, which the
    /// caller (<c>ServerHostBuilder</c>) turns into a 400 - not a program
    /// error, but a DOM code this table does not carry.
    ///
    /// <paramref name="merge"/> is looked up with
    /// <see cref="CatalogueMerger.MergeAll"/>'s output, not <c>Merge</c>'s -
    /// the matched action may be one the curated catalogue never mentions,
    /// and a key genuinely bound to it in Elite must still get a readable
    /// label rather than silently falling through. Falls back to
    /// <see cref="Prettifier.Prettify"/> on the bare action name on the rare
    /// chance it is missing even from the full merge.
    /// </summary>
    public static Response? BuildResponse(string domCode, BindingsFile bindings, IReadOnlyList<CataloguePickerEntry> merge)
    {
        ArgumentNullException.ThrowIfNull(domCode);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(merge);

        if (!BrowserKeyCodeMap.TryMap(domCode, out var keyName) || !Scancodes.TryGet(keyName, out var key))
        {
            return null;
        }

        var resolvedKeyLabel = BindResolver.PrettifyKeyName(keyName);

        var matched = KeyBindingLookup.FindActionBoundTo(key, bindings);
        if (matched is null)
        {
            return new Response(null, null, keyName, resolvedKeyLabel);
        }

        var entry = merge.FirstOrDefault(e => e.ActionName == matched);
        var label = entry?.DisplayLabel ?? Prettifier.Prettify(matched);
        return new Response(matched, label, keyName, resolvedKeyLabel);
    }
}
