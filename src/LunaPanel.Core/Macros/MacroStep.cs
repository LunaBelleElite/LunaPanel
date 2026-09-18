namespace LunaPanel.Core.Macros;

/// <summary>
/// One step of a <see cref="MacroDefinition"/> - exactly one of the sealed
/// subtypes below, each corresponding to one row of the macro grammar table
/// in <c>ref/docs/design-decisions.md</c>. <see cref="MacroDefinition.Parse"/>
/// is the only place these are constructed; it rejects a JSON step object
/// that names two kinds, or none, at load time rather than letting an
/// ambiguous or empty step through to run.
/// </summary>
public abstract record MacroStep;
