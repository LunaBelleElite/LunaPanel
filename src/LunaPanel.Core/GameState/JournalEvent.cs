namespace LunaPanel.Core.GameState;

/// <summary>
/// One parsed line of Elite's journal: the event name, the timestamp the
/// game wrote, and every top-level <em>string</em> property on the line.
///
/// Deliberately not a full model of the journal. Nested objects and arrays
/// are dropped, and numeric properties are dropped, because nothing in
/// LunaPanel consumes either yet - the two things that do consume this
/// (the edge-condition ledger, which needs only <see cref="EventName"/>,
/// and vessel-type tracking, which needs three string fields) are both
/// covered by what is here. Add a field when a caller actually needs it,
/// not before.
/// </summary>
/// <param name="EventName">The value of the line's <c>event</c> property, verbatim.</param>
/// <param name="Timestamp">
/// The line's <c>timestamp</c> property, or <see langword="null"/> when it
/// is absent or unparseable. Never used for edge observability - see
/// <see cref="JournalStateStore"/> for why that is watermark-based rather
/// than clock-based.
/// </param>
/// <param name="Strings">
/// Every top-level property whose JSON value is a string, keyed by the
/// exact property name (ordinal). Frontier's property casing is stable, so
/// this does not need to be forgiving; the casing hazard in this data is in
/// the <em>values</em>, not the keys - see <see cref="JournalStateStore"/>.
/// </param>
public sealed record JournalEvent(
    string EventName,
    DateTimeOffset? Timestamp,
    IReadOnlyDictionary<string, string> Strings)
{
    /// <summary>The named string property, or <see langword="null"/> if the line did not carry it.</summary>
    public string? String(string name) => Strings.TryGetValue(name, out var value) ? value : null;
}
