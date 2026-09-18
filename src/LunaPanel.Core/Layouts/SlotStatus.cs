namespace LunaPanel.Core.Layouts;

/// <summary>
/// A slot's current, freshly-recomputed status - never persisted (see "A
/// layout stores intent, never resolution" in
/// <c>ref/docs/design-decisions.md</c>). Produced by <see cref="LayoutAnnotator"/>.
/// </summary>
public enum SlotStatus
{
    /// <summary>Resolves cleanly: a bound action, or a fully-resolvable macro.</summary>
    Ok,

    /// <summary>A recognized action that isn't currently bound to a key.</summary>
    Unbound,

    /// <summary>The action name isn't recognized at all - most likely renamed or removed by a game update.</summary>
    UnknownAction,

    /// <summary>The macro id is recognized, but is currently degraded.</summary>
    MacroDegraded,

    /// <summary>The macro id isn't recognized at all.</summary>
    UnknownMacro
}
