namespace LunaPanel.Core.GameState;

/// <summary>
/// Immutable point-in-time picture of Status.json's content. Elite
/// Dangerous rewrites the whole file on every update rather than appending,
/// so a snapshot is never a delta - it is everything known at the moment it
/// was parsed.
/// </summary>
/// <param name="Flags">
/// The raw 32-bit <c>Flags</c> bitfield. Always present in the real file,
/// even when the game process is not running (a closed game still writes
/// <c>Flags: 0</c>).
/// </param>
/// <param name="Flags2">
/// The raw <c>Flags2</c> bitfield, or <see langword="null"/> when the
/// property is absent from the source JSON (pre-Odyssey data, or a closed
/// game). Deliberately nullable rather than defaulting to 0, so "absent"
/// and "present and zero" stay distinguishable to callers that care -
/// evaluation of a condition treats both the same way (see
/// <see cref="Condition"/>), but the snapshot itself does not erase the
/// difference.
/// </param>
/// <param name="GuiFocus">
/// The raw <c>GuiFocus</c> value, or <see langword="null"/> when the
/// property is absent (e.g. a closed game). Deliberately nullable for the
/// same reason as <see cref="Flags2"/>.
/// </param>
/// <param name="GameRunning">
/// True when the source JSON carried more than the three keys a closed game
/// produces (<c>timestamp</c>, <c>event</c>, <c>Flags</c>). This is a
/// key-presence signal, not a flags check - a running game can legitimately
/// have every flag off.
/// </param>
/// <param name="SignedIn">
/// False when both <see cref="Flags"/> and <see cref="Flags2"/> (absent
/// treated as 0) are entirely zero. That combination is the game's
/// not-signed-in state (e.g. sat at the main menu), not a real all-off
/// in-game state, and is called out separately from <see cref="GameRunning"/>
/// because the game process can be running while this is still false.
/// </param>
/// <param name="Destination">
/// The <c>Destination</c> object, or <see langword="null"/> when the
/// property is absent - the same absent-is-not-zero discipline
/// <see cref="Flags2"/> and <see cref="GuiFocus"/> already follow, and the
/// distinction matters here more than anywhere: a change in this value while
/// the left panel is open is the only re-sync signal a docked commander has
/// (LC20, and <c>PanelTabTracker.RecordDestinationChange</c>). Defaulted so
/// the 20-odd hand-built snapshots in the suite that predate this field, and
/// care nothing about it, did not all have to be touched to add it.
/// </param>
public sealed record StatusSnapshot(
    uint Flags,
    uint? Flags2,
    int? GuiFocus,
    bool GameRunning,
    bool SignedIn,
    StatusDestination? Destination = null);
