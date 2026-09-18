namespace LunaPanel.Core.Macros;

/// <summary>
/// Outcome of <see cref="MacroDefinition.TryParse"/>: either a successfully
/// parsed <see cref="MacroDefinition"/>, or a reported failure - never
/// throws, the same shape as
/// <c>LunaPanel.Core.Layouts.LayoutParseResult</c>,
/// <c>LunaPanel.Core.GameState.StatusParseResult</c> and
/// <c>LunaPanel.Core.Bindings.BindingsParseResult</c>.
///
/// <b>Why this exists alongside <see cref="MacroDefinition.Parse"/>'s
/// throw.</b> <see cref="MacroDefinition.Parse"/> throws on purpose, and
/// that reasoning is still correct for the input it was written for: a
/// shipped macro is build-time content, and a malformed one is a defect that
/// should fail the build loudly rather than ship a macro that cannot run.
/// A macro a commander authored on the device is the opposite category -
/// runtime data read off the player's machine, exactly like a layout file -
/// and the codebase's rule for that category is
/// <c>LayoutJson.Parse</c>'s: never throw, return a result the caller can
/// report. <see cref="Error"/> carries the message
/// <see cref="MacroDefinition.Parse"/> would have thrown, which already
/// names the macro id and the offending step index
/// (<c>ref/docs/macro-builder.md</c>).
/// </summary>
/// <param name="Success">Whether <see cref="Macro"/> is present.</param>
/// <param name="Macro">The parsed macro, or <see langword="null"/> on failure.</param>
/// <param name="Error">Why the parse failed, or <see langword="null"/> on success.</param>
public sealed record MacroParseResult(bool Success, MacroDefinition? Macro, string? Error)
{
    public static MacroParseResult Ok(MacroDefinition macro) => new(true, macro, null);

    public static MacroParseResult Fail(string error) => new(false, null, error);
}
