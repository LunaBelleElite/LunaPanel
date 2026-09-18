namespace LunaPanel.Core.Layouts;

/// <summary>Outcome of <see cref="LayoutStore.Load"/>.</summary>
public sealed record LayoutLoadResult(LayoutLoadOutcome Outcome, Layout? Layout)
{
    public static LayoutLoadResult NotFound() => new(LayoutLoadOutcome.NotFound, null);
    public static LayoutLoadResult Loaded(Layout layout) => new(LayoutLoadOutcome.Loaded, layout);
    public static LayoutLoadResult Corrupt() => new(LayoutLoadOutcome.Corrupt, null);
    public static LayoutLoadResult TooNewSchema() => new(LayoutLoadOutcome.TooNewSchema, null);
}
