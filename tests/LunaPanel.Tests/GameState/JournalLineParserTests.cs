using LunaPanel.Core.GameState;

namespace LunaPanel.Tests.GameState;

/// <summary>
/// Pins <see cref="JournalLineParser"/>'s contract: a torn, blank or
/// oddly-shaped line comes back as <see langword="null"/> rather than as an
/// exception. The journal is appended to while LunaPanel reads it, so a
/// partially-written final line is routine input, not a corrupt file - and
/// <see cref="JournalTailer"/> is separately responsible for never handing
/// one over at all (see <c>JournalTailerTests</c>). Both defences exist on
/// purpose.
/// </summary>
public class JournalLineParserTests
{
    private const string LoadGameLine =
        "{ \"timestamp\":\"2026-01-01T12:00:05Z\", \"event\":\"LoadGame\", \"Commander\":\"Testcmdr\", " +
        "\"Ship\":\"Lander01\", \"Ship_Localised\":\"Nomad\", \"ShipID\":17, \"Credits\":100000 }";

    [Fact]
    public void Parse_LoadGameLine_ReadsTheEventName()
    {
        var parsed = JournalLineParser.Parse(LoadGameLine);

        Assert.NotNull(parsed);
        Assert.Equal("LoadGame", parsed!.EventName);
    }

    [Fact]
    public void Parse_LoadGameLine_ReadsStringPropertiesVerbatim_IncludingCasing()
    {
        var parsed = JournalLineParser.Parse(LoadGameLine);

        // "Lander01" with a capital L is exactly what LoadGame writes, and
        // the parser must not helpfully normalise it - normalisation is the
        // store's job, at one place, where it can be pinned.
        Assert.Equal("Lander01", parsed!.String("Ship"));
        Assert.Equal("Nomad", parsed.String("Ship_Localised"));
    }

    [Fact]
    public void Parse_ReadsTheTimestamp()
    {
        var parsed = JournalLineParser.Parse(LoadGameLine);

        Assert.Equal(DateTimeOffset.Parse("2026-01-01T12:00:05Z", System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime(),
            parsed!.Timestamp!.Value.ToUniversalTime());
    }

    [Fact]
    public void Parse_NumericAndNestedProperties_AreDropped_NotFaithfullyRepresented()
    {
        var parsed = JournalLineParser.Parse(
            "{ \"event\":\"Materials\", \"Raw\":[ { \"Name\":\"iron\", \"Count\":300 } ], \"Encoded\":[], \"Count\":7, \"Flag\":true, \"Nothing\":null }");

        Assert.NotNull(parsed);
        Assert.Equal("Materials", parsed!.EventName);
        Assert.Null(parsed.String("Raw"));
        Assert.Null(parsed.String("Count"));
        Assert.Null(parsed.String("Flag"));
        Assert.Null(parsed.String("Nothing"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Parse_BlankLine_ReturnsNull(string? line)
    {
        Assert.Null(JournalLineParser.Parse(line));
    }

    [Fact]
    public void Parse_TornLine_ReturnsNull_NeverThrows()
    {
        // A half-written append: valid JSON up to the point the game had
        // flushed, and nothing after it.
        var exception = Record.Exception(() =>
            Assert.Null(JournalLineParser.Parse("{ \"timestamp\":\"2026-01-01T12:00:05Z\", \"event\":\"LoadGa")));

        Assert.Null(exception);
    }

    [Fact]
    public void Parse_JsonThatIsNotAnObject_ReturnsNull()
    {
        Assert.Null(JournalLineParser.Parse("[ 1, 2, 3 ]"));
        Assert.Null(JournalLineParser.Parse("\"just a string\""));
        Assert.Null(JournalLineParser.Parse("42"));
    }

    [Fact]
    public void Parse_ObjectWithNoEventProperty_ReturnsNull()
    {
        Assert.Null(JournalLineParser.Parse("{ \"timestamp\":\"2026-01-01T12:00:05Z\", \"Ship\":\"Lander01\" }"));
    }

    [Fact]
    public void Parse_EventPropertyThatIsNotAString_ReturnsNull_RatherThanThrowing()
    {
        // JsonElement.GetString() THROWS when the element is not a string -
        // it does not return null. Same trap gamestate.md records against
        // StatusJsonParser's TryGetUInt32 guard. Removing the ValueKind
        // check here reintroduces a live exception, not a wrong result.
        var exception = Record.Exception(() =>
            Assert.Null(JournalLineParser.Parse("{ \"event\":42, \"Ship\":\"Lander01\" }")));

        Assert.Null(exception);
    }

    [Fact]
    public void Parse_EmptyEventName_ReturnsNull()
    {
        Assert.Null(JournalLineParser.Parse("{ \"event\":\"\", \"Ship\":\"Lander01\" }"));
    }

    [Fact]
    public void Parse_UnparseableTimestamp_StillParsesTheEvent_WithNullTimestamp()
    {
        var parsed = JournalLineParser.Parse("{ \"timestamp\":\"not-a-date\", \"event\":\"FSDJump\" }");

        Assert.NotNull(parsed);
        Assert.Equal("FSDJump", parsed!.EventName);
        Assert.Null(parsed.Timestamp);
    }

    [Fact]
    public void Parse_NonStringTimestamp_DoesNotThrow()
    {
        var exception = Record.Exception(() => JournalLineParser.Parse("{ \"timestamp\":12345, \"event\":\"FSDJump\" }"));

        Assert.Null(exception);
    }
}
