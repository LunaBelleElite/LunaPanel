using LunaPanel.Core.GameState;

namespace LunaPanel.Tests.GameState;

/// <summary>
/// Pins every entry in <see cref="StatusVocabulary"/> against the bit
/// position claimed for it in Frontier's Journal Manual v38 (as given in
/// this task's brief), independent of <c>StatusVocabulary</c>'s own source
/// listing - the expected table below is typed out by hand here rather
/// than read back out of <c>StatusVocabulary</c>, so a transcription error
/// in the production table cannot mark its own homework.
/// </summary>
public class StatusVocabularyTests
{
    // Name, Field, Bit - transcribed directly from the Journal Manual v38
    // facts given in the brief, not derived from StatusVocabulary itself.
    private static readonly (string Name, FlagsField Field, int Bit)[] ExpectedFlags =
    {
        ("Docked", FlagsField.Flags, 0),
        ("Landed", FlagsField.Flags, 1),
        ("LandingGearDown", FlagsField.Flags, 2),
        ("ShieldsUp", FlagsField.Flags, 3),
        ("Supercruise", FlagsField.Flags, 4),
        ("FlightAssistOff", FlagsField.Flags, 5),
        ("HardpointsDeployed", FlagsField.Flags, 6),
        ("InWing", FlagsField.Flags, 7),
        ("LightsOn", FlagsField.Flags, 8),
        ("CargoScoopDeployed", FlagsField.Flags, 9),
        ("SilentRunning", FlagsField.Flags, 10),
        ("ScoopingFuel", FlagsField.Flags, 11),
        ("SrvHandbrake", FlagsField.Flags, 12),
        ("SrvTurretView", FlagsField.Flags, 13),
        ("SrvTurretRetracted", FlagsField.Flags, 14),
        ("SrvDriveAssist", FlagsField.Flags, 15),
        ("FsdMassLocked", FlagsField.Flags, 16),
        ("FsdCharging", FlagsField.Flags, 17),
        ("FsdCooldown", FlagsField.Flags, 18),
        ("LowFuel", FlagsField.Flags, 19),
        ("OverHeating", FlagsField.Flags, 20),
        ("HasLatLong", FlagsField.Flags, 21),
        ("IsInDanger", FlagsField.Flags, 22),
        ("BeingInterdicted", FlagsField.Flags, 23),
        ("InMainShip", FlagsField.Flags, 24),
        ("InFighter", FlagsField.Flags, 25),
        ("InSrv", FlagsField.Flags, 26),
        ("HudInAnalysisMode", FlagsField.Flags, 27),
        ("NightVision", FlagsField.Flags, 28),
        ("AltitudeFromAverageRadius", FlagsField.Flags, 29),
        ("FsdJump", FlagsField.Flags, 30),
        ("SrvHighBeam", FlagsField.Flags, 31),

        ("OnFoot", FlagsField.Flags2, 0),
        ("InTaxi", FlagsField.Flags2, 1),
        ("InMulticrew", FlagsField.Flags2, 2),
        ("OnFootInStation", FlagsField.Flags2, 3),
        ("OnFootOnPlanet", FlagsField.Flags2, 4),
        ("AimDownSight", FlagsField.Flags2, 5),
        ("LowOxygen", FlagsField.Flags2, 6),
        ("LowHealth", FlagsField.Flags2, 7),
        ("Cold", FlagsField.Flags2, 8),
        ("Hot", FlagsField.Flags2, 9),
        ("VeryCold", FlagsField.Flags2, 10),
        ("VeryHot", FlagsField.Flags2, 11),
        ("GlideMode", FlagsField.Flags2, 12),
        ("OnFootInHangar", FlagsField.Flags2, 13),
        ("OnFootSocialSpace", FlagsField.Flags2, 14),
        ("OnFootExterior", FlagsField.Flags2, 15),
        ("BreathableAtmosphere", FlagsField.Flags2, 16),
        ("TelepresenceMulticrew", FlagsField.Flags2, 17),
        ("PhysicalMulticrew", FlagsField.Flags2, 18),
        ("FsdHyperdriveCharging", FlagsField.Flags2, 19),
        ("SupercruiseOverdrive", FlagsField.Flags2, 20),
        ("SupercruiseAssist", FlagsField.Flags2, 21),
        ("NpcCrewActive", FlagsField.Flags2, 22),
    };

    [Fact]
    public void FlagConditions_TableHasExactly55Entries()
    {
        // 32 (Flags) + 23 (Flags2, 0-22 inclusive) = 55.
        Assert.Equal(55, StatusVocabulary.FlagsConditions.Count + StatusVocabulary.Flags2Conditions.Count);
        Assert.Equal(55, ExpectedFlags.Length);
    }

    [Fact]
    public void StatusVocabulary_EveryExpectedEntry_HasMatchingFieldAndBit()
    {
        foreach (var (name, field, bit) in ExpectedFlags)
        {
            Assert.True(StatusVocabulary.TryGetFlagCondition(name, out var actual), $"'{name}' is missing from StatusVocabulary.");
            Assert.Equal(field, actual.Field);
            Assert.Equal(bit, actual.Bit);
        }
    }

    /// <summary>
    /// The round-trip proof: for every expected (name, field, bit), build a
    /// snapshot with ONLY that single bit set in ONLY that field, then
    /// assert that name's condition evaluates true and every OTHER name's
    /// condition evaluates false. This catches both a wrong bit/field for
    /// the entry under test, and a collision where two different names
    /// resolve to the same (field, bit).
    /// </summary>
    [Fact]
    public void StatusVocabulary_BitRoundTrip_EveryEntryMapsToExactlyItsClaimedBit()
    {
        foreach (var (name, field, bit) in ExpectedFlags)
        {
            var snapshot = field == FlagsField.Flags
                ? new StatusSnapshot(1u << bit, 0, null, GameRunning: true, SignedIn: true)
                : new StatusSnapshot(0, (uint)(1u << bit), null, GameRunning: true, SignedIn: true);

            foreach (var (otherName, _, _) in ExpectedFlags)
            {
                var evaluated = Condition.Parse(otherName).Evaluate(snapshot);
                if (otherName == name)
                {
                    Assert.True(evaluated, $"'{name}' did not evaluate true with only {field} bit {bit} set.");
                }
                else
                {
                    Assert.False(evaluated, $"'{otherName}' unexpectedly evaluated true when only '{name}'s bit ({field} bit {bit}) was set - possible bit/field collision.");
                }
            }
        }
    }

    [Fact]
    public void GuiFocusValues_MatchJournalManual()
    {
        var expected = new Dictionary<string, int>
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

        Assert.Equal(expected.Count, StatusVocabulary.GuiFocusValues.Count);
        foreach (var (name, value) in expected)
        {
            Assert.True(StatusVocabulary.TryGetGuiFocusValue(name, out var actual), $"'{name}' is missing from StatusVocabulary.GuiFocusValues.");
            Assert.Equal(value, actual);
        }
    }

    [Fact]
    public void UnverifiedFlags2Bits_AreMarkedNotOfficial()
    {
        Assert.True(StatusVocabulary.TryGetFlagCondition("SupercruiseOverdrive", out var sco));
        Assert.Equal(FlagConfidence.CommunitySourced, sco.Confidence);

        Assert.True(StatusVocabulary.TryGetFlagCondition("SupercruiseAssist", out var sca));
        Assert.Equal(FlagConfidence.CommunitySourced, sca.Confidence);

        Assert.True(StatusVocabulary.TryGetFlagCondition("NpcCrewActive", out var npc));
        Assert.Equal(FlagConfidence.SingleSourceLowConfidence, npc.Confidence);
    }

    [Fact]
    public void OfficialBits_AreAllMarkedOfficial()
    {
        var officialNames = ExpectedFlags
            .Select(f => f.Name)
            .Except(new[] { "SupercruiseOverdrive", "SupercruiseAssist", "NpcCrewActive" });

        foreach (var name in officialNames)
        {
            Assert.True(StatusVocabulary.TryGetFlagCondition(name, out var condition));
            Assert.Equal(FlagConfidence.Official, condition.Confidence);
        }
    }
}
