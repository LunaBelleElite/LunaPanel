namespace LunaPanel.Core.Macros;

/// <summary>
/// One user macro plus the copy-to-edit provenance <see cref="UserMacroStore"/>
/// keeps beside it on disk (<c>ref/docs/macro-builder.md</c>, question 3).
///
/// <see cref="SourceMacroId"/> and <see cref="SourceStepsHash"/> are both
/// <see langword="null"/> for a macro authored from scratch - only a macro
/// created through <c>POST /api/macros/copy</c> ever has them set. They are
/// deliberately carried as sibling fields on this record rather than folded
/// into <see cref="MacroDefinition"/> itself: the grammar
/// <see cref="MacroDefinition"/> stores is the one a shipped macro also
/// uses, and a shipped macro has no source to be stale against.
/// </summary>
/// <param name="Macro">The macro itself, as the grammar stores it.</param>
/// <param name="SourceMacroId">The id this macro was copied from, or <see langword="null"/> if it was authored from scratch.</param>
/// <param name="SourceStepsHash">
/// <see cref="MacroStepsHash.Compute"/> over the source's steps, taken at
/// the moment of the copy. Compared against the source's <em>current</em>
/// steps to decide whether the copy is stale.
/// </param>
public sealed record UserMacroRecord(MacroDefinition Macro, string? SourceMacroId, string? SourceStepsHash);
