namespace LunaPanel.Core.GameState;

/// <summary>
/// A caller's own definition of "since when" for an edge condition - the
/// <em>T</em> in "this event has been seen since time T".
///
/// It is a position in <see cref="JournalStateStore"/>'s monotonic record,
/// not a clock reading. That is the whole design decision; see
/// <see cref="JournalStateStore"/>'s remarks for what was rejected and why a
/// clock could not have worked here.
/// </summary>
/// <param name="Sequence">
/// How many events the store had recorded when this mark was taken.
/// <c>default(JournalWatermark)</c> is sequence 0, which means "since the
/// tailer started" and therefore includes everything the startup back-scan
/// replayed. Take a mark from
/// <see cref="JournalStateStore.Mark"/> rather than defaulting one unless
/// that is genuinely what you meant.
/// </param>
public readonly record struct JournalWatermark(long Sequence);
