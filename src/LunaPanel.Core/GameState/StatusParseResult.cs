namespace LunaPanel.Core.GameState;

/// <summary>
/// Outcome of <see cref="StatusJsonParser.Parse"/>. Status.json is read
/// while the game may be mid-rewrite of it, so malformed or truncated input
/// is an expected, routine occurrence - never an exception the caller has
/// to guard against. A failed parse carries a message the caller can log
/// and move on from.
/// </summary>
/// <param name="Success">Whether parsing produced a usable snapshot.</param>
/// <param name="Snapshot">The parsed snapshot, or <see langword="null"/> when <paramref name="Success"/> is false.</param>
/// <param name="Error">A human-readable failure reason, or <see langword="null"/> when <paramref name="Success"/> is true.</param>
public sealed record StatusParseResult(bool Success, StatusSnapshot? Snapshot, string? Error)
{
    public static StatusParseResult Ok(StatusSnapshot snapshot) => new(true, snapshot, null);

    public static StatusParseResult Fail(string error) => new(false, null, error);
}
