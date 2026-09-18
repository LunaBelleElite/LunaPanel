namespace LunaPanel.Core.Theme;

/// <summary>Outcome of <see cref="ThemeOverrideJson.Parse"/>.</summary>
public sealed record ThemeOverrideParseResult(bool Success, ThemeOverride? Override, string? Error)
{
    public static ThemeOverrideParseResult Ok(ThemeOverride overrideValue) => new(true, overrideValue, null);
    public static ThemeOverrideParseResult Fail(string error) => new(false, null, error);
}
