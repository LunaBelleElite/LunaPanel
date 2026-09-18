namespace LunaPanel.Core.Layouts;

/// <summary>Outcome of <see cref="LayoutStore.Save"/>.</summary>
public sealed record LayoutSaveResult(LayoutSaveOutcome Outcome, IReadOnlyList<string> ValidationErrors)
{
    public static LayoutSaveResult Saved() => new(LayoutSaveOutcome.Saved, Array.Empty<string>());
    public static LayoutSaveResult Invalid(IReadOnlyList<string> errors) => new(LayoutSaveOutcome.ValidationFailed, errors);
}
