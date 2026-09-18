namespace LunaPanel.Core.Bindings;

/// <summary>
/// Outcome of <see cref="BindingsFile.Parse"/>. A player's <c>.binds</c>
/// file can be malformed or truncated (same class of hazard as
/// <c>Status.json</c> being read mid-rewrite - see
/// <c>LunaPanel.Core.GameState.StatusParseResult</c>), so parsing never
/// throws; a failed parse carries a message the caller can log and move on
/// from.
/// </summary>
/// <param name="Success">Whether parsing produced a usable <see cref="BindingsFile"/>.</param>
/// <param name="File">The parsed file, or <see langword="null"/> when <paramref name="Success"/> is false.</param>
/// <param name="Error">A human-readable failure reason, or <see langword="null"/> when <paramref name="Success"/> is true.</param>
public sealed record BindingsParseResult(bool Success, BindingsFile? File, string? Error)
{
    public static BindingsParseResult Ok(BindingsFile file) => new(true, file, null);

    public static BindingsParseResult Fail(string error) => new(false, null, error);
}
