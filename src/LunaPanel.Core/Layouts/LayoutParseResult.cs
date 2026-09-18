namespace LunaPanel.Core.Layouts;

/// <summary>
/// Outcome of <see cref="LayoutJson.Parse"/>: either a successfully parsed
/// <see cref="Layout"/>, or a reported failure - never throws, same shape
/// as <c>LunaPanel.Core.GameState.StatusParseResult</c> and
/// <c>LunaPanel.Core.Bindings.BindingsParseResult</c>.
/// </summary>
public sealed record LayoutParseResult(bool Success, Layout? Layout, string? Error)
{
    public static LayoutParseResult Ok(Layout layout) => new(true, layout, null);
    public static LayoutParseResult Fail(string error) => new(false, null, error);
}
