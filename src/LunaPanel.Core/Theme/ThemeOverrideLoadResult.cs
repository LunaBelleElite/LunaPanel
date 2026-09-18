namespace LunaPanel.Core.Theme;

/// <summary>Outcome of <see cref="ThemeOverrideStore.Load"/>.</summary>
public sealed record ThemeOverrideLoadResult(ThemeOverrideLoadOutcome Outcome, ThemeOverride? Override)
{
    public static ThemeOverrideLoadResult NotFound() => new(ThemeOverrideLoadOutcome.NotFound, null);
    public static ThemeOverrideLoadResult Loaded(ThemeOverride overrideValue) => new(ThemeOverrideLoadOutcome.Loaded, overrideValue);
    public static ThemeOverrideLoadResult Corrupt() => new(ThemeOverrideLoadOutcome.Corrupt, null);
}
