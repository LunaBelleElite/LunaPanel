using LunaPanel.Core.GameState;

namespace LunaPanel.Tests.GameState;

/// <summary>
/// Pins <see cref="Condition"/> and <see cref="ConditionList"/> grammar
/// parsing/evaluation independent of any Status.json fixture.
/// </summary>
public class ConditionTests
{
    private static StatusSnapshot Snapshot(uint flags, uint? flags2 = null, int? guiFocus = null) =>
        new(flags, flags2, guiFocus, GameRunning: true, SignedIn: true);

    [Fact]
    public void Parse_PlainName_EvaluatesTrue_WhenBitSet()
    {
        var snapshot = Snapshot(flags: 1); // bit 0 = Docked
        Assert.True(Condition.Parse("Docked").Evaluate(snapshot));
    }

    [Fact]
    public void Parse_PlainName_EvaluatesFalse_WhenBitClear()
    {
        var snapshot = Snapshot(flags: 0);
        Assert.False(Condition.Parse("Docked").Evaluate(snapshot));
    }

    [Fact]
    public void Parse_NegatedName_InvertsEvaluation()
    {
        var docked = Snapshot(flags: 1);
        var notDocked = Snapshot(flags: 0);

        var condition = Condition.Parse("!Docked");
        Assert.False(condition.Evaluate(docked));
        Assert.True(condition.Evaluate(notDocked));
    }

    [Fact]
    public void Parse_UnknownName_ThrowsFormatException_NamingTheToken()
    {
        var ex = Assert.Throws<FormatException>(() => Condition.Parse("NotARealFlagName"));
        Assert.Contains("NotARealFlagName", ex.Message);
    }

    [Fact]
    public void Parse_UnknownNegatedName_ThrowsFormatException_NamingTheToken()
    {
        var ex = Assert.Throws<FormatException>(() => Condition.Parse("!NotARealFlagName"));
        Assert.Contains("NotARealFlagName", ex.Message);
    }

    [Fact]
    public void Parse_UnknownGuiFocusName_ThrowsFormatException_NamingTheToken()
    {
        var ex = Assert.Throws<FormatException>(() => Condition.Parse("GuiFocus:NotARealPanel"));
        Assert.Contains("NotARealPanel", ex.Message);
    }

    [Fact]
    public void Parse_EmptyToken_Throws()
    {
        Assert.Throws<FormatException>(() => Condition.Parse(""));
    }

    [Fact]
    public void GuiFocusCondition_EvaluatesFalse_WhenGuiFocusIsAbsent()
    {
        var snapshot = Snapshot(flags: 0, guiFocus: null);
        Assert.False(Condition.Parse("GuiFocus:NoFocus").Evaluate(snapshot));
    }

    [Fact]
    public void GuiFocusCondition_EvaluatesTrue_OnExactMatch()
    {
        var snapshot = Snapshot(flags: 0, guiFocus: 6); // GalaxyMap
        Assert.True(Condition.Parse("GuiFocus:GalaxyMap").Evaluate(snapshot));
        Assert.False(Condition.Parse("GuiFocus:SystemMap").Evaluate(snapshot));
    }

    // ---------------------------------------------------------------
    // ConditionList - AND semantics, no OR, no nesting
    // ---------------------------------------------------------------

    [Fact]
    public void ConditionList_Evaluate_IsTrue_OnlyWhenEveryConditionIsTrue()
    {
        var snapshot = Snapshot(flags: 0b101); // Docked (bit0) + LandingGearDown (bit2)

        var allTrue = ConditionList.Parse(new[] { "Docked", "LandingGearDown" });
        Assert.True(allTrue.Evaluate(snapshot));

        var oneFalse = ConditionList.Parse(new[] { "Docked", "ShieldsUp" });
        Assert.False(oneFalse.Evaluate(snapshot));
    }

    [Fact]
    public void ConditionList_Parse_PropagatesUnknownNameFailure()
    {
        Assert.Throws<FormatException>(() => ConditionList.Parse(new[] { "Docked", "NotARealFlagName" }));
    }

    [Fact]
    public void ConditionList_MixesNegationAndGuiFocus()
    {
        var snapshot = Snapshot(flags: 0, guiFocus: 2); // ExternalPanel, nothing else set

        var list = ConditionList.Parse(new[] { "!Docked", "GuiFocus:ExternalPanel" });
        Assert.True(list.Evaluate(snapshot));
    }
}
