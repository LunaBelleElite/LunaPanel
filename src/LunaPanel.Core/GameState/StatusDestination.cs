namespace LunaPanel.Core.GameState;

/// <summary>
/// The <c>Destination</c> object Elite writes into <c>Status.json</c> - the
/// commander's currently locked destination. Absent from the file entirely
/// much of the time (LC15, LC17, LC20 all caught it appearing and
/// disappearing), which is why <see cref="StatusSnapshot.Destination"/> is
/// nullable rather than defaulted: absent is not "no destination", it is
/// "the game is not currently reporting one at all".
/// </summary>
/// <param name="SystemAddress">
/// The JSON key is literally <c>System</c>; this property is named for what
/// the value actually is (the same 64-bit system address the journal calls
/// <c>SystemAddress</c>) rather than transliterating a key name that would
/// read as the <c>System</c> namespace at every call site.
/// </param>
/// <param name="Body">The <c>Body</c> id. <c>0</c> for a fleet carrier and also for a system's main star (LC18) - so it is not a carrier detector.</param>
/// <param name="Name">
/// The displayed name, e.g. <c>SELENE'S HAVEN BZK-L9K</c> (LC18) or
/// <c>Eorld Flyao KA-A b20-21 A Belt Cluster 3</c> (LC20). Empty string when
/// the key is absent from an otherwise present <c>Destination</c> object.
/// </param>
/// <remarks>
/// All three components together are the identity used for change detection
/// (see <c>PanelTabTracker.RecordDestinationChange</c>): a record's
/// structural equality compares them as a unit, so selecting a different
/// entry that happens to share a display name with the previous one still
/// registers as a change.
/// </remarks>
public sealed record StatusDestination(long SystemAddress, int Body, string Name);
