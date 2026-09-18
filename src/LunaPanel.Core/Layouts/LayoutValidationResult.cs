namespace LunaPanel.Core.Layouts;

/// <summary>
/// Outcome of <see cref="LayoutValidator"/>: every violation found in one
/// pass, not just the first, so a caller can report (or a test can pin)
/// each rule independently.
/// </summary>
public sealed record LayoutValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static LayoutValidationResult Ok() => new(true, Array.Empty<string>());
    public static LayoutValidationResult Fail(IReadOnlyList<string> errors) => new(false, errors);
}
