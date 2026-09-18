namespace LunaPanel.Core.GameState;

/// <summary>
/// Which bitfield a <see cref="FlagCondition"/> reads from.
/// </summary>
public enum FlagsField
{
    Flags,
    Flags2
}

/// <summary>
/// How solid the source is for a given <see cref="FlagCondition"/> entry.
/// Every bit in <c>Flags</c> and bits 0-19 of <c>Flags2</c> are confirmed by
/// Frontier's own Journal Manual. A few higher <c>Flags2</c> bits are not
/// documented anywhere official; this exists so a table entry can carry
/// that distinction instead of it living only in a comment someone can miss.
/// </summary>
public enum FlagConfidence
{
    /// <summary>Confirmed by Frontier's official Journal Manual.</summary>
    Official,

    /// <summary>Reported by the community, not present in the official manual.</summary>
    CommunitySourced,

    /// <summary>Reported by a single source; treat as low confidence.</summary>
    SingleSourceLowConfidence
}

/// <summary>
/// One named condition backed by a single bit of <see cref="StatusSnapshot.Flags"/>
/// or <see cref="StatusSnapshot.Flags2"/>.
/// </summary>
public readonly record struct FlagCondition(string Name, FlagsField Field, int Bit, FlagConfidence Confidence);

/// <summary>
/// The single table mapping condition names to bits, and <c>GuiFocus</c>
/// names to their integer values. Deliberately just data - a later in-game
/// correction (a bit Frontier redefines, a new panel added to
/// <c>GuiFocus</c>) should only ever need to touch the rows here, never
/// logic scattered through <see cref="Condition"/> or callers.
///
/// <c>GuiFocus</c>'s naming is the game's own and is counter-intuitive:
/// <c>InternalPanel</c> is the RIGHT panel and <c>ExternalPanel</c> is the
/// LEFT panel. This table uses Frontier's own names rather than
/// "corrected" ones, so it stays traceable to the Journal Manual.
/// </summary>
public static class StatusVocabulary
{
    /// <summary>
    /// All 32 bits of <c>Flags</c>, per Frontier's Journal Manual v38. Every
    /// entry is <see cref="FlagConfidence.Official"/>.
    /// </summary>
    public static readonly IReadOnlyList<FlagCondition> FlagsConditions = new[]
    {
        new FlagCondition("Docked", FlagsField.Flags, 0, FlagConfidence.Official),
        new FlagCondition("Landed", FlagsField.Flags, 1, FlagConfidence.Official),
        new FlagCondition("LandingGearDown", FlagsField.Flags, 2, FlagConfidence.Official),
        new FlagCondition("ShieldsUp", FlagsField.Flags, 3, FlagConfidence.Official),
        new FlagCondition("Supercruise", FlagsField.Flags, 4, FlagConfidence.Official),
        new FlagCondition("FlightAssistOff", FlagsField.Flags, 5, FlagConfidence.Official),
        new FlagCondition("HardpointsDeployed", FlagsField.Flags, 6, FlagConfidence.Official),
        new FlagCondition("InWing", FlagsField.Flags, 7, FlagConfidence.Official),
        new FlagCondition("LightsOn", FlagsField.Flags, 8, FlagConfidence.Official),
        new FlagCondition("CargoScoopDeployed", FlagsField.Flags, 9, FlagConfidence.Official),
        new FlagCondition("SilentRunning", FlagsField.Flags, 10, FlagConfidence.Official),
        new FlagCondition("ScoopingFuel", FlagsField.Flags, 11, FlagConfidence.Official),
        new FlagCondition("SrvHandbrake", FlagsField.Flags, 12, FlagConfidence.Official),
        new FlagCondition("SrvTurretView", FlagsField.Flags, 13, FlagConfidence.Official),
        new FlagCondition("SrvTurretRetracted", FlagsField.Flags, 14, FlagConfidence.Official),
        new FlagCondition("SrvDriveAssist", FlagsField.Flags, 15, FlagConfidence.Official),
        new FlagCondition("FsdMassLocked", FlagsField.Flags, 16, FlagConfidence.Official),
        new FlagCondition("FsdCharging", FlagsField.Flags, 17, FlagConfidence.Official),
        new FlagCondition("FsdCooldown", FlagsField.Flags, 18, FlagConfidence.Official),
        new FlagCondition("LowFuel", FlagsField.Flags, 19, FlagConfidence.Official),
        new FlagCondition("OverHeating", FlagsField.Flags, 20, FlagConfidence.Official),
        new FlagCondition("HasLatLong", FlagsField.Flags, 21, FlagConfidence.Official),
        new FlagCondition("IsInDanger", FlagsField.Flags, 22, FlagConfidence.Official),
        new FlagCondition("BeingInterdicted", FlagsField.Flags, 23, FlagConfidence.Official),
        new FlagCondition("InMainShip", FlagsField.Flags, 24, FlagConfidence.Official),
        new FlagCondition("InFighter", FlagsField.Flags, 25, FlagConfidence.Official),
        new FlagCondition("InSrv", FlagsField.Flags, 26, FlagConfidence.Official),
        new FlagCondition("HudInAnalysisMode", FlagsField.Flags, 27, FlagConfidence.Official),
        new FlagCondition("NightVision", FlagsField.Flags, 28, FlagConfidence.Official),
        new FlagCondition("AltitudeFromAverageRadius", FlagsField.Flags, 29, FlagConfidence.Official),
        new FlagCondition("FsdJump", FlagsField.Flags, 30, FlagConfidence.Official),
        new FlagCondition("SrvHighBeam", FlagsField.Flags, 31, FlagConfidence.Official),
    };

    /// <summary>
    /// <c>Flags2</c> bits. Bits 0-19 are official (Journal Manual v38).
    /// Bits 20 (<c>SupercruiseOverdrive</c>) and 21 (<c>SupercruiseAssist</c>)
    /// are community-sourced only, and bit 22 (<c>NpcCrewActive</c>) is
    /// single-source and low confidence - see each entry's
    /// <see cref="FlagCondition.Confidence"/> rather than assuming every row
    /// here carries the same weight.
    /// </summary>
    public static readonly IReadOnlyList<FlagCondition> Flags2Conditions = new[]
    {
        new FlagCondition("OnFoot", FlagsField.Flags2, 0, FlagConfidence.Official),
        new FlagCondition("InTaxi", FlagsField.Flags2, 1, FlagConfidence.Official),
        new FlagCondition("InMulticrew", FlagsField.Flags2, 2, FlagConfidence.Official),
        new FlagCondition("OnFootInStation", FlagsField.Flags2, 3, FlagConfidence.Official),
        new FlagCondition("OnFootOnPlanet", FlagsField.Flags2, 4, FlagConfidence.Official),
        new FlagCondition("AimDownSight", FlagsField.Flags2, 5, FlagConfidence.Official),
        new FlagCondition("LowOxygen", FlagsField.Flags2, 6, FlagConfidence.Official),
        new FlagCondition("LowHealth", FlagsField.Flags2, 7, FlagConfidence.Official),
        new FlagCondition("Cold", FlagsField.Flags2, 8, FlagConfidence.Official),
        new FlagCondition("Hot", FlagsField.Flags2, 9, FlagConfidence.Official),
        new FlagCondition("VeryCold", FlagsField.Flags2, 10, FlagConfidence.Official),
        new FlagCondition("VeryHot", FlagsField.Flags2, 11, FlagConfidence.Official),
        new FlagCondition("GlideMode", FlagsField.Flags2, 12, FlagConfidence.Official),
        new FlagCondition("OnFootInHangar", FlagsField.Flags2, 13, FlagConfidence.Official),
        new FlagCondition("OnFootSocialSpace", FlagsField.Flags2, 14, FlagConfidence.Official),
        new FlagCondition("OnFootExterior", FlagsField.Flags2, 15, FlagConfidence.Official),
        new FlagCondition("BreathableAtmosphere", FlagsField.Flags2, 16, FlagConfidence.Official),
        new FlagCondition("TelepresenceMulticrew", FlagsField.Flags2, 17, FlagConfidence.Official),
        new FlagCondition("PhysicalMulticrew", FlagsField.Flags2, 18, FlagConfidence.Official),
        new FlagCondition("FsdHyperdriveCharging", FlagsField.Flags2, 19, FlagConfidence.Official),
        // Unverified below this line - see FlagConfidence on each entry.
        new FlagCondition("SupercruiseOverdrive", FlagsField.Flags2, 20, FlagConfidence.CommunitySourced),
        new FlagCondition("SupercruiseAssist", FlagsField.Flags2, 21, FlagConfidence.CommunitySourced),
        new FlagCondition("NpcCrewActive", FlagsField.Flags2, 22, FlagConfidence.SingleSourceLowConfidence),
    };

    /// <summary>
    /// <c>GuiFocus</c> values, per Frontier's Journal Manual v38. Values 12
    /// and above are known to occur (Odyssey on-foot panels, carrier
    /// management, Livery) but are undocumented, so they are deliberately
    /// not named here - <see cref="Condition"/>'s <c>GuiFocus:Name</c> form
    /// can only ever name a documented value; an undocumented value is
    /// still readable from <see cref="StatusSnapshot.GuiFocus"/> directly.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> GuiFocusValues = new Dictionary<string, int>
    {
        ["NoFocus"] = 0,
        ["InternalPanel"] = 1,
        ["ExternalPanel"] = 2,
        ["CommsPanel"] = 3,
        ["RolePanel"] = 4,
        ["StationServices"] = 5,
        ["GalaxyMap"] = 6,
        ["SystemMap"] = 7,
        ["Orrery"] = 8,
        ["FSS"] = 9,
        ["SAA"] = 10,
        ["Codex"] = 11,
    };

    private static readonly IReadOnlyDictionary<string, FlagCondition> ByName = BuildByName();

    private static IReadOnlyDictionary<string, FlagCondition> BuildByName()
    {
        var byName = new Dictionary<string, FlagCondition>(StringComparer.Ordinal);
        foreach (var condition in FlagsConditions.Concat(Flags2Conditions))
        {
            byName.Add(condition.Name, condition);
        }

        return byName;
    }

    public static bool TryGetFlagCondition(string name, out FlagCondition condition) =>
        ByName.TryGetValue(name, out condition);

    public static bool TryGetGuiFocusValue(string name, out int value) =>
        GuiFocusValues.TryGetValue(name, out value);
}
