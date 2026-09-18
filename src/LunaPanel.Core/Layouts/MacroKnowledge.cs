namespace LunaPanel.Core.Layouts;

/// <summary>
/// What <see cref="LayoutAnnotator"/> is told about macros, injected rather
/// than read from macro definitions - the macro loader is a later task, and
/// injecting this keeps the annotator fully testable without it.
/// <see cref="DegradedIds"/> need not be a subset of <see cref="KnownIds"/>;
/// <see cref="LayoutAnnotator"/> checks membership in each independently.
/// </summary>
/// <param name="KnownIds">Every macro id that currently exists.</param>
/// <param name="DegradedIds">Macro ids that exist but are currently degraded (e.g. a step's action is unbound).</param>
/// <param name="Names">
/// Macro id -&gt; the macro definition's own optional display name (see
/// <see cref="LunaPanel.Core.Macros.MacroDefinition.Name"/>), for whichever
/// macros actually have one. Optional (defaults to <see langword="null"/>,
/// treated the same as empty by <see cref="NameFor"/>) so every existing
/// 2-argument call site - <see cref="Empty"/> and every hand-built
/// <c>MacroKnowledge</c> in a test that predates this - keeps compiling
/// unchanged. <see cref="LayoutAnnotator"/> consults this before falling
/// back to <see cref="LunaPanel.Core.Catalogue.Prettifier.Prettify"/> on the
/// macro id, exactly the way a curated action's catalogue label is
/// consulted before its own id-prettify fallback - see
/// <c>ref/docs/button-naming.md</c>.
/// </param>
public sealed record MacroKnowledge(IReadOnlySet<string> KnownIds, IReadOnlySet<string> DegradedIds, IReadOnlyDictionary<string, string>? Names = null)
{
    public static MacroKnowledge Empty { get; } = new(new HashSet<string>(), new HashSet<string>());

    private static readonly IReadOnlyDictionary<string, string> NoNames = new Dictionary<string, string>();

    /// <summary>
    /// The macro's own display name, or <see langword="null"/> when it has
    /// none (or isn't known at all) - the caller's cue to fall back to
    /// <see cref="LunaPanel.Core.Catalogue.Prettifier.Prettify"/> on the id
    /// instead.
    /// </summary>
    public string? NameFor(string macroId) => (Names ?? NoNames).TryGetValue(macroId, out var name) ? name : null;
}
