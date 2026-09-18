using LunaPanel.Core.GameState;

namespace LunaPanel.Tests.GameState;

/// <summary>
/// Pins the edge condition kind - <c>Journal:EventName</c>, "this event has
/// been seen since my watermark". Two things are being held in place here:
/// the parse-time rejection that stops a misspelled event silently never
/// firing, and the observability rule that an edge does not expire, which is
/// the design decision LC17 forced (see <see cref="JournalStateStore"/>'s
/// remarks).
/// </summary>
public class EdgeConditionTests
{
    private static JournalEvent Event(string name) =>
        new(name, null, new Dictionary<string, string>(StringComparer.Ordinal));

    [Fact]
    public void Parse_JournalPrefixedKnownEvent_YieldsTheEventName()
    {
        Assert.Equal("FSDJump", EdgeCondition.Parse("Journal:FSDJump").EventName);
    }

    [Fact]
    public void Parse_IsCaseInsensitive_AndReportsFrontiersSpelling()
    {
        Assert.Equal("DockSRV", EdgeCondition.Parse("Journal:docksrv").EventName);
    }

    [Fact]
    public void Parse_MisspelledEventName_ThrowsFormatException_NamingTheToken()
    {
        // The double-L trap, as a build failure rather than a silent
        // never-fires. This is the single most valuable test in this file.
        var exception = Assert.Throws<FormatException>(() => EdgeCondition.Parse("Journal:DockingCanceled"));

        Assert.Contains("Journal:DockingCanceled", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_UnknownEventName_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => EdgeCondition.Parse("Journal:NoSuchEventHasEverBeenWritten"));
    }

    [Fact]
    public void Parse_WithoutThePrefix_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => EdgeCondition.Parse("FSDJump"));
    }

    [Fact]
    public void Parse_PrefixWithNoName_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => EdgeCondition.Parse("Journal:"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_EmptyToken_ThrowsFormatException(string? token)
    {
        Assert.Throws<FormatException>(() => EdgeCondition.Parse(token!));
    }

    /// <summary>
    /// A small spot-check over the 2026-09-12 vocabulary-expansion pass -
    /// not all 39 new rows, just enough spread across the groups to prove
    /// the table wiring holds for entries added after the original set.
    /// </summary>
    [Theory]
    [InlineData("Interdicted")]
    [InlineData("MarketSell")]
    [InlineData("Touchdown")]
    [InlineData("RefuelAll")]
    [InlineData("JoinACrew")]
    public void Parse_ANewlyAddedEvent_ResolvesSuccessfully(string name)
    {
        Assert.Equal(name, EdgeCondition.Parse($"Journal:{name}").EventName);
    }

    [Fact]
    public void TheLevelGrammar_RefusesAnEdgeToken_RatherThanEvaluatingItFalse()
    {
        // The two grammars are separate types on purpose. If Condition ever
        // grew a silent fall-through for an unrecognised prefix, an edge
        // token pasted into a `lit` list would evaluate false forever and
        // look like a game-state problem. It throws instead.
        var exception = Assert.Throws<FormatException>(() => Condition.Parse("Journal:FSDJump"));

        Assert.Contains("Journal:FSDJump", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_IsFalse_BeforeTheEventHasBeenSeen()
    {
        var store = new JournalStateStore();
        var mark = store.Mark();

        Assert.False(EdgeCondition.Parse("Journal:FSDJump").Evaluate(store, mark));
    }

    [Fact]
    public void Evaluate_IsTrue_AfterTheEventArrives()
    {
        var store = new JournalStateStore();
        var mark = store.Mark();

        store.Record(Event("FSDJump"));

        Assert.True(EdgeCondition.Parse("Journal:FSDJump").Evaluate(store, mark));
    }

    [Fact]
    public void Evaluate_IgnoresADifferentEvent()
    {
        var store = new JournalStateStore();
        var mark = store.Mark();

        store.Record(Event("DockSRV"));

        Assert.False(EdgeCondition.Parse("Journal:FSDJump").Evaluate(store, mark));
    }

    [Fact]
    public void Evaluate_IsFalseAgain_AgainstAMarkTakenAfterTheEvent()
    {
        // "Reset" is taking a new mark. Nothing else resets anything.
        var store = new JournalStateStore();
        var condition = EdgeCondition.Parse("Journal:FSDJump");

        var before = store.Mark();
        store.Record(Event("FSDJump"));
        var after = store.Mark();

        Assert.True(condition.Evaluate(store, before));
        Assert.False(condition.Evaluate(store, after));
    }

    [Fact]
    public void AnEdgeFiredWithNobodyListening_IsStillObservableMuchLater()
    {
        // The LC17 case, in miniature: the mark is taken at the keypress,
        // the completion lands a minute later, and a great many unrelated
        // events land in between. No window, no expiry - the edge is still
        // there when the caller finally looks.
        var store = new JournalStateStore();
        var condition = EdgeCondition.Parse("Journal:DockSRV");
        var atKeypress = store.Mark();

        store.Record(Event("DockSRV"));
        for (var i = 0; i < 50; i++)
        {
            store.Record(Event("SupercruiseEntry"));
            store.Record(Event("SupercruiseExit"));
        }

        Assert.True(condition.Evaluate(store, atKeypress));
    }

    [Fact]
    public void Evaluate_MatchesTheEventNameCaseInsensitively_EndToEnd()
    {
        var store = new JournalStateStore();
        var mark = store.Mark();

        store.Record(Event("docksrv"));

        Assert.True(EdgeCondition.Parse("Journal:DockSRV").Evaluate(store, mark));
    }

    [Fact]
    public void DefaultWatermark_MeansSinceTheTailerStarted()
    {
        // default(JournalWatermark) is sequence 0, so it sees everything the
        // startup back-scan replayed. Documented, and pinned so nobody
        // "fixes" it into meaning now.
        var store = new JournalStateStore();
        store.Record(Event("FSDJump"));

        Assert.True(EdgeCondition.Parse("Journal:FSDJump").Evaluate(store, default));
    }
}
