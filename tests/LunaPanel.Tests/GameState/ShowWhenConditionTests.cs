using LunaPanel.Core.GameState;

namespace LunaPanel.Tests.GameState;

/// <summary>
/// The <c>showWhen</c> grammar: the existing <c>lit</c> condition tokens,
/// plus the one term the flags cannot supply (<c>Vessel:&lt;type&gt;</c>).
/// See <c>ref/docs/vessel-context.md</c>.
/// </summary>
public class ShowWhenConditionTests
{
    private const int InSrvBit = 26;
    private const int InMainShipBit = 24;

    private static StatusSnapshot Snapshot(uint flags, uint? flags2 = 0u, int? guiFocus = null) =>
        new(flags, flags2, guiFocus, GameRunning: true, SignedIn: true);

    private static readonly VesselContext Nomad = new(VesselContextKind.Srv, "lander01");
    private static readonly VesselContext Scarab = new(VesselContextKind.Srv, "testbuggy");
    private static readonly VesselContext SrvOfUnknownType = new(VesselContextKind.Srv, null);

    [Fact]
    public void Parse_APlainFlagName_EvaluatesAgainstTheSnapshot()
    {
        var condition = ShowWhenCondition.Parse("InSrv");

        Assert.True(condition.Evaluate(Snapshot(1u << InSrvBit), Nomad));
        Assert.False(condition.Evaluate(Snapshot(1u << InMainShipBit), Nomad));
    }

    [Fact]
    public void Parse_ANegatedFlagName_EvaluatesAgainstTheSnapshot()
    {
        var condition = ShowWhenCondition.Parse("!InSrv");

        Assert.False(condition.Evaluate(Snapshot(1u << InSrvBit), Nomad));
        Assert.True(condition.Evaluate(Snapshot(1u << InMainShipBit), Nomad));
    }

    [Fact]
    public void Parse_AGuiFocusToken_EvaluatesAgainstTheSnapshot()
    {
        var condition = ShowWhenCondition.Parse("GuiFocus:ExternalPanel");

        Assert.True(condition.Evaluate(Snapshot(0u, 0u, guiFocus: 2), Nomad));
        Assert.False(condition.Evaluate(Snapshot(0u, 0u, guiFocus: 1), Nomad));
    }

    [Fact]
    public void Parse_AnUnknownName_ThrowsNamingTheOffendingToken()
    {
        var ex = Assert.Throws<FormatException>(() => ShowWhenCondition.Parse("NotAFlagAtAll"));

        Assert.Contains("NotAFlagAtAll", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_AnEmptyToken_Throws()
    {
        Assert.Throws<FormatException>(() => ShowWhenCondition.Parse(string.Empty));
    }

    [Fact]
    public void Parse_VesselPrefixWithNothingAfterIt_Throws()
    {
        var ex = Assert.Throws<FormatException>(() => ShowWhenCondition.Parse("Vessel:"));

        Assert.Contains("Vessel:", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_VesselTerm_MatchesTheContextsOwnVesselType()
    {
        var condition = ShowWhenCondition.Parse("Vessel:lander01");

        Assert.True(condition.Evaluate(Snapshot(1u << InSrvBit), Nomad));
    }

    /// <summary>
    /// The Nomad and the Scarab are different vehicles the commander asked to
    /// be treated separately, because they play differently. A term that
    /// matched either would make the whole journal side pointless - which is
    /// why this asserts the negative direction as well as the positive.
    /// </summary>
    [Fact]
    public void Evaluate_VesselTerm_DoesNotMatchADifferentSrv()
    {
        var condition = ShowWhenCondition.Parse("Vessel:lander01");

        Assert.False(condition.Evaluate(Snapshot(1u << InSrvBit), Scarab));
    }

    /// <summary>
    /// <b>The casing trap, pinned with the <c>LoadGame</c> spelling
    /// specifically</b> (<c>ref/docs/vessel-context.md</c>): <c>LoadGame</c>
    /// writes <c>"Lander01"</c> while <c>DockSRV</c>/<c>LaunchVessel</c>
    /// write <c>"lander01"</c>. An ordinal comparison works perfectly against
    /// the launch path - the path anyone would naturally write a test for -
    /// and fails only for a commander who logged in already inside their SRV.
    /// Both directions are pinned: the token written in <c>LoadGame</c>'s
    /// casing against a lower-cased context, and a lower-cased token against
    /// a context that was never normalised.
    /// </summary>
    [Fact]
    public void Evaluate_VesselTerm_WrittenInLoadGamesCasing_StillMatchesTheLowerCasedContext()
    {
        var condition = ShowWhenCondition.Parse("Vessel:Lander01");

        Assert.True(condition.Evaluate(Snapshot(1u << InSrvBit), Nomad));
    }

    [Fact]
    public void Evaluate_VesselTerm_MatchesAContextCarryingLoadGamesCasing()
    {
        var condition = ShowWhenCondition.Parse("Vessel:lander01");

        Assert.True(condition.Evaluate(Snapshot(1u << InSrvBit), new VesselContext(VesselContextKind.Srv, "Lander01")));
    }

    /// <summary>
    /// The coordinator's ruling, 2026-09-08: a <c>showWhen</c> naming a
    /// vessel the journal has never mentioned takes the "no page matches"
    /// path. At this level that means the term is simply false - it is
    /// <see cref="Core.Layouts.ContextPageSelector"/> that turns "no term
    /// matched" into "stay where you are".
    /// </summary>
    [Fact]
    public void Evaluate_VesselTerm_AgainstAContextWithNoVesselTypeAtAll_IsFalse()
    {
        var condition = ShowWhenCondition.Parse("Vessel:lander01");

        Assert.False(condition.Evaluate(Snapshot(1u << InSrvBit), SrvOfUnknownType));
    }

    /// <summary>
    /// <see cref="VesselContext.VesselType"/> is null outside the SRV
    /// context, so a <c>Vessel:</c> term can never match a main ship - the
    /// documented limit, pinned rather than left to be rediscovered.
    /// </summary>
    [Fact]
    public void Evaluate_VesselTerm_NeverMatchesTheMainShipContext()
    {
        var condition = ShowWhenCondition.Parse("Vessel:krait_mkii");

        Assert.False(condition.Evaluate(Snapshot(1u << InMainShipBit), new VesselContext(VesselContextKind.MainShip, null)));
    }

    /// <summary>
    /// The reason this type exists at all rather than a
    /// <see cref="StatusVocabulary"/> row: a <c>Vessel:</c> token reaching an
    /// ordinary <see cref="Condition"/> call site must be rejected loudly at
    /// parse time, never evaluate silently false forever. Same pin
    /// <c>Journal:</c> already has.
    /// </summary>
    [Fact]
    public void Condition_Parse_RejectsAVesselToken_SoItCanNeverSilentlyEvaluateFalse()
    {
        var ex = Assert.Throws<FormatException>(() => Condition.Parse("Vessel:lander01"));

        Assert.Contains("Vessel:lander01", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void List_AndsEveryToken_AllTrue()
    {
        var list = ShowWhenConditionList.Parse(new[] { "InSrv", "Vessel:lander01" });

        Assert.True(list.Evaluate(Snapshot(1u << InSrvBit), Nomad));
    }

    [Fact]
    public void List_AndsEveryToken_OneFalseIsEnough()
    {
        var list = ShowWhenConditionList.Parse(new[] { "InSrv", "Vessel:testbuggy" });

        Assert.False(list.Evaluate(Snapshot(1u << InSrvBit), Nomad));
    }

    [Fact]
    public void List_AndsEveryToken_TheFlagHalfCanBeTheFalseOne()
    {
        var list = ShowWhenConditionList.Parse(new[] { "InMainShip", "Vessel:lander01" });

        Assert.False(list.Evaluate(Snapshot(1u << InSrvBit), Nomad));
    }

    [Fact]
    public void List_Parse_RejectsTheWholeListWhenAnyTokenIsUnknown()
    {
        Assert.Throws<FormatException>(() => ShowWhenConditionList.Parse(new[] { "InSrv", "NotAFlagAtAll" }));
    }
}
