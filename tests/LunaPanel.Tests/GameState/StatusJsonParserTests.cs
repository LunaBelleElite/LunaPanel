using LunaPanel.Core.GameState;

namespace LunaPanel.Tests.GameState;

/// <summary>
/// Pins <see cref="StatusJsonParser"/> against the committed Status.json
/// fixtures and hand-written malformed/edge-case JSON strings. Fixtures are
/// read from the test output directory via <see cref="AppContext.BaseDirectory"/>,
/// matching the pattern <c>FixtureIntegrityTests</c> already uses.
/// </summary>
public class StatusJsonParserTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "status");

    private static string ReadFixture(string name) => File.ReadAllText(Path.Combine(FixturesRoot, name));

    // ---------------------------------------------------------------
    // closed.json
    // ---------------------------------------------------------------

    [Fact]
    public void ClosedJson_ParsesSuccessfully()
    {
        var result = StatusJsonParser.Parse(ReadFixture("closed.json"));
        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Snapshot);
    }

    [Fact]
    public void ClosedJson_GameRunning_IsFalse()
    {
        var result = StatusJsonParser.Parse(ReadFixture("closed.json"));
        Assert.False(result.Snapshot!.GameRunning);
    }

    [Fact]
    public void ClosedJson_GuiFocus_IsAbsent()
    {
        var result = StatusJsonParser.Parse(ReadFixture("closed.json"));
        Assert.Null(result.Snapshot!.GuiFocus);
    }

    [Fact]
    public void ClosedJson_Flags2_IsAbsent_NotZero()
    {
        var result = StatusJsonParser.Parse(ReadFixture("closed.json"));
        Assert.Null(result.Snapshot!.Flags2);
    }

    [Fact]
    public void ClosedJson_Flags_IsZero()
    {
        var result = StatusJsonParser.Parse(ReadFixture("closed.json"));
        Assert.Equal(0u, result.Snapshot!.Flags);
    }

    [Fact]
    public void ClosedJson_SignedIn_IsFalse()
    {
        var result = StatusJsonParser.Parse(ReadFixture("closed.json"));
        Assert.False(result.Snapshot!.SignedIn);
    }

    // ---------------------------------------------------------------
    // live-leftpanel.json
    // ---------------------------------------------------------------

    [Fact]
    public void LiveLeftPanelJson_GuiFocus_ResolvesToExternalPanel_TheLeftPanel()
    {
        var result = StatusJsonParser.Parse(ReadFixture("live-leftpanel.json"));
        Assert.True(result.Success, result.Error);
        Assert.Equal(StatusVocabulary.GuiFocusValues["ExternalPanel"], result.Snapshot!.GuiFocus);

        var condition = Condition.Parse("GuiFocus:ExternalPanel");
        Assert.True(condition.Evaluate(result.Snapshot));

        var wrongPanel = Condition.Parse("GuiFocus:InternalPanel");
        Assert.False(wrongPanel.Evaluate(result.Snapshot));
    }

    // ---------------------------------------------------------------
    // live-docked.json
    // ---------------------------------------------------------------

    [Fact]
    public void LiveDockedJson_DockedLandingGearDownLightsOn_AreTrue_SupercruiseIsFalse()
    {
        var result = StatusJsonParser.Parse(ReadFixture("live-docked.json"));
        Assert.True(result.Success, result.Error);
        var snapshot = result.Snapshot!;

        Assert.True(Condition.Parse("Docked").Evaluate(snapshot));
        Assert.True(Condition.Parse("LandingGearDown").Evaluate(snapshot));
        Assert.True(Condition.Parse("LightsOn").Evaluate(snapshot));
        Assert.False(Condition.Parse("Supercruise").Evaluate(snapshot));
    }

    // ---------------------------------------------------------------
    // live-onfoot.json
    // ---------------------------------------------------------------

    [Fact]
    public void LiveOnFootJson_OnFootAndOnFootInStation_AreTrue_FromFlags2()
    {
        var result = StatusJsonParser.Parse(ReadFixture("live-onfoot.json"));
        Assert.True(result.Success, result.Error);
        var snapshot = result.Snapshot!;

        Assert.True(Condition.Parse("OnFoot").Evaluate(snapshot));
        Assert.True(Condition.Parse("OnFootInStation").Evaluate(snapshot));
    }

    [Fact]
    public void LiveOnFootJson_FlagsIsZero_ButFlags2IsNonZero_SoSignedInIsTrue()
    {
        // live-onfoot.json has Flags:0 (an on-foot player isn't "in main
        // ship" etc.) with everything meaningful carried in Flags2 instead.
        // Pins that SignedIn is the AND of both fields being zero, not a
        // check of Flags alone - Flags==0 here must not be mistaken for
        // "not signed in" when Flags2 carries real state.
        var result = StatusJsonParser.Parse(ReadFixture("live-onfoot.json"));
        Assert.True(result.Success, result.Error);
        Assert.Equal(0u, result.Snapshot!.Flags);
        Assert.True(result.Snapshot.SignedIn);
    }

    // ---------------------------------------------------------------
    // Flags == 0 && Flags2 == 0 -> not signed in
    // ---------------------------------------------------------------

    [Fact]
    public void FlagsAndFlags2BothZero_IsNotSignedIn_EvenWithGameRunning()
    {
        // Hand-written, not a fixture: extra keys present (so GameRunning is
        // true) but both bitfields are entirely zero - the game's
        // not-signed-in state (e.g. sat at the main menu), not a real
        // all-off in-game state.
        const string json = """{ "timestamp":"2026-09-05T00:00:00Z", "event":"Status", "Flags":0, "Flags2":0, "GuiFocus":0 }""";

        var result = StatusJsonParser.Parse(json);
        Assert.True(result.Success, result.Error);
        Assert.True(result.Snapshot!.GameRunning);
        Assert.False(result.Snapshot.SignedIn);
    }

    [Fact]
    public void FlagsNonZero_Flags2Zero_IsSignedIn()
    {
        const string json = """{ "timestamp":"2026-09-05T00:00:00Z", "event":"Status", "Flags":1, "Flags2":0 }""";

        var result = StatusJsonParser.Parse(json);
        Assert.True(result.Success, result.Error);
        Assert.True(result.Snapshot!.SignedIn);
    }

    // ---------------------------------------------------------------
    // Missing Flags2 treated as 0 for evaluation, but distinguishable on the snapshot
    // ---------------------------------------------------------------

    [Fact]
    public void MissingFlags2_EvaluatesSameAsPresentAndZero_ForBitConditions()
    {
        const string missing = """{ "timestamp":"2026-09-05T00:00:00Z", "event":"Status", "Flags":0 }""";
        const string presentZero = """{ "timestamp":"2026-09-05T00:00:00Z", "event":"Status", "Flags":0, "Flags2":0 }""";

        var missingSnapshot = StatusJsonParser.Parse(missing).Snapshot!;
        var presentZeroSnapshot = StatusJsonParser.Parse(presentZero).Snapshot!;

        // Distinguishable on the raw snapshot...
        Assert.Null(missingSnapshot.Flags2);
        Assert.Equal(0u, presentZeroSnapshot.Flags2);

        // ...but identical for condition evaluation.
        var condition = Condition.Parse("!OnFoot");
        Assert.True(condition.Evaluate(missingSnapshot));
        Assert.True(condition.Evaluate(presentZeroSnapshot));
    }

    // ---------------------------------------------------------------
    // O21 - GuiFocus absent (on foot) is not the same as GuiFocus:0
    // (NoFocus, in a ship). Status.json OMITS the field entirely on foot
    // (LC15) rather than writing it as 0; StatusSnapshot.GuiFocus is
    // int?, so the two states are distinguishable today, but nothing
    // pinned that distinction before this test - a future simplification
    // to GetValueOrDefault() would silently conflate them.
    // ---------------------------------------------------------------

    [Fact]
    public void LiveOnFootJson_GuiFocusIsOmitted_ParsesAsNull_NotZero()
    {
        // live-onfoot.json is the real fixture LC15 was measured against -
        // Status.json drops GuiFocus entirely while on foot.
        var result = StatusJsonParser.Parse(ReadFixture("live-onfoot.json"));
        Assert.True(result.Success, result.Error);
        Assert.Null(result.Snapshot!.GuiFocus);

        // GuiFocus:NoFocus (value 0) must NOT be considered satisfied by an
        // absent GuiFocus - null == 0 is false, and on foot there is no
        // panel-focus concept to be "NoFocus" about.
        var condition = Condition.Parse("GuiFocus:NoFocus");
        Assert.False(condition.Evaluate(result.Snapshot));
    }

    [Fact]
    public void GuiFocusPresentAndZero_IsDistinctFromAbsent_AndSatisfiesNoFocus()
    {
        const string omitted = """{ "timestamp":"2026-09-05T00:00:00Z", "event":"Status", "Flags":0 }""";
        const string presentZero = """{ "timestamp":"2026-09-05T00:00:00Z", "event":"Status", "Flags":0, "GuiFocus":0 }""";

        var omittedSnapshot = StatusJsonParser.Parse(omitted).Snapshot!;
        var presentZeroSnapshot = StatusJsonParser.Parse(presentZero).Snapshot!;

        // Distinguishable on the raw snapshot...
        Assert.Null(omittedSnapshot.GuiFocus);
        Assert.Equal(0, presentZeroSnapshot.GuiFocus);

        // ...and, unlike Flags2 (MissingFlags2_EvaluatesSameAsPresentAndZero_ForBitConditions),
        // this distinction is meant to reach condition evaluation too:
        // GuiFocus:NoFocus is a real, in-ship value and must not be
        // satisfied by an on-foot snapshot that never wrote GuiFocus at all.
        var condition = Condition.Parse("GuiFocus:NoFocus");
        Assert.False(condition.Evaluate(omittedSnapshot));
        Assert.True(condition.Evaluate(presentZeroSnapshot));
    }

    // ---------------------------------------------------------------
    // Destination (LC20) - the only signal a docked commander's Navigation
    // tab produces. Parsed here so PanelTabTracker can act on a CHANGE in
    // it; see ref/docs/panel-tab-tracking.md, "Measured 2026-09-08".
    // ---------------------------------------------------------------

    [Fact]
    public void Destination_Present_ParsesAllThreeComponents()
    {
        // The exact object LC18 read out of the real Status.json while the
        // commander's own fleet carrier was selected - not a hand-invented
        // shape.
        const string json = """
            { "timestamp":"2026-09-07T21:00:00Z", "event":"Status", "Flags":16777240,
              "Destination":{ "System":46946810740905, "Body":0, "Name":"SELENE'S HAVEN BZK-L9K" } }
            """;

        var snapshot = StatusJsonParser.Parse(json).Snapshot!;

        Assert.NotNull(snapshot.Destination);
        Assert.Equal(46946810740905L, snapshot.Destination!.SystemAddress);
        Assert.Equal(0, snapshot.Destination.Body);
        Assert.Equal("SELENE'S HAVEN BZK-L9K", snapshot.Destination.Name);
    }

    [Fact]
    public void Destination_Absent_IsNull_NotAnEmptyDestination()
    {
        // Absent is not zero, and here it is not an empty object either -
        // Status.json genuinely omits Destination for long stretches (LC15,
        // LC17, and LC20's own "(absent) ->" first transition). The tracker
        // reads absent-to-present as a change, which only works if the two
        // states stay distinguishable on the snapshot.
        var snapshot = StatusJsonParser.Parse(ReadFixture("live-leftpanel.json")).Snapshot!;

        Assert.Null(snapshot.Destination);
    }

    [Fact]
    public void Destination_PresentButNotAnObject_IsIgnored_AndTheWholeParseStillSucceeds()
    {
        // Deliberately unlike Flags2/GuiFocus, which fail the whole parse
        // when malformed. Destination feeds one opportunistic, low-confidence
        // re-sync and nothing else - failing the snapshot over it would take
        // out every lit condition and every macro gate in the process, which
        // is a far worse outcome than losing a re-sync hint.
        const string json = """{ "timestamp":"2026-09-08T00:00:00Z", "event":"Status", "Flags":1, "Destination":"not an object" }""";

        var result = StatusJsonParser.Parse(json);

        Assert.True(result.Success, result.Error);
        Assert.Null(result.Snapshot!.Destination);
        Assert.Equal(1u, result.Snapshot.Flags);
    }

    [Fact]
    public void Destination_WithMissingOrWrongTypedMembers_StillParsesAsPresent_WithDefaultedMembers()
    {
        const string json = """{ "timestamp":"2026-09-08T00:00:00Z", "event":"Status", "Flags":1, "Destination":{ "Name":"Somewhere" } }""";

        var snapshot = StatusJsonParser.Parse(json).Snapshot!;

        Assert.NotNull(snapshot.Destination);
        Assert.Equal(0L, snapshot.Destination!.SystemAddress);
        Assert.Equal(0, snapshot.Destination.Body);
        Assert.Equal("Somewhere", snapshot.Destination.Name);
    }

    [Fact]
    public void TwoSnapshots_DifferingOnlyInDestination_AreNotEqual()
    {
        // The whole change-detection mechanism in ServerHostBuilder is a
        // record inequality check against the previously seen value. LC20's
        // 23:55:23 sample changed nothing else at all, so if StatusSnapshot
        // equality did not see Destination, that change would be invisible.
        const string before = """{ "timestamp":"2026-09-08T23:55:09Z", "event":"Status", "Flags":16777240, "GuiFocus":2, "Destination":{ "System":1, "Body":3, "Name":"Eorld Flyao KA-A b20-21 A Belt Cluster 3" } }""";
        const string after = """{ "timestamp":"2026-09-08T23:55:23Z", "event":"Status", "Flags":16777240, "GuiFocus":2, "Destination":{ "System":1, "Body":0, "Name":"SELENE'S HAVEN BZK-L9K" } }""";

        var beforeSnapshot = StatusJsonParser.Parse(before).Snapshot!;
        var afterSnapshot = StatusJsonParser.Parse(after).Snapshot!;

        Assert.NotEqual(beforeSnapshot, afterSnapshot);
        Assert.NotEqual(beforeSnapshot.Destination, afterSnapshot.Destination);
    }

    // ---------------------------------------------------------------
    // Malformed / truncated JSON never throws
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json at all")]
    [InlineData("""{ "timestamp":"2026-09-05T00:00:00Z", "event":"Status" """)]
    [InlineData("""{ "timestamp":"2026-09-05T00:00:00Z", "event":"Status", "Flags":"oops" }""")]
    [InlineData("""[ "Flags", 0 ]""")]
    [InlineData("""{ "timestamp":"2026-09-05T00:00:00Z", "event":"Status", "Flags":4294967296 }""")]
    public void MalformedOrTruncatedJson_ReturnsFailure_NeverThrows(string json)
    {
        var result = StatusJsonParser.Parse(json);
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void MissingFlagsProperty_ReturnsFailure()
    {
        const string json = """{ "timestamp":"2026-09-05T00:00:00Z", "event":"Status" }""";
        var result = StatusJsonParser.Parse(json);
        Assert.False(result.Success);
        Assert.Contains("Flags", result.Error);
    }
}
