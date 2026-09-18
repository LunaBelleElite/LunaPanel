using LunaPanel.Core.Layouts;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// Pins <see cref="LayoutJson.Parse"/> and <see cref="LayoutJson.Serialize"/>:
/// round-tripping the fixture corpus, structural leniency (missing optional
/// arrays default to empty), and reported (never thrown) failures on
/// malformed structure.
/// </summary>
public class LayoutJsonTests
{
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "layouts", name);
    private static string ReadFixture(string name) => File.ReadAllText(FixturePath(name));

    [Fact]
    public void Parse_ValidMultiPageFixture_Succeeds()
    {
        var result = LayoutJson.Parse(ReadFixture("valid-multi-page.json"));

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, result.Layout!.SchemaVersion);
        Assert.Equal(2, result.Layout.Pages.Count);
        Assert.Equal("SHIP", result.Layout.Pages[0].Name);
        Assert.Equal("t9", result.Layout.Pages[0].TemplateId);
        Assert.Equal("TARGET", result.Layout.Pages[1].Name);
    }

    [Fact]
    public void Parse_ThenSerialize_ThenParseAgain_RoundTrips()
    {
        var first = LayoutJson.Parse(ReadFixture("valid-multi-page.json"));
        Assert.True(first.Success, first.Error);

        var json = LayoutJson.Serialize(first.Layout!);
        var second = LayoutJson.Parse(json);

        Assert.True(second.Success, second.Error);

        // Deliberately field-by-field rather than Assert.Equal(Layout, Layout):
        // Layout/LayoutPage/LayoutSlot are records whose Pages/Slots/Parked
        // properties are IReadOnlyList<T> - two structurally-identical but
        // reference-distinct List<T> instances are NOT equal under the
        // record's compiler-generated Equals (List<T> doesn't override
        // Equals itself), so relying on record equality here would make
        // this pin fail on a correct round-trip.
        Assert.Equal(first.Layout!.SchemaVersion, second.Layout!.SchemaVersion);
        Assert.Equal(first.Layout.Pages.Count, second.Layout.Pages.Count);
        for (var i = 0; i < first.Layout.Pages.Count; i++)
        {
            var expectedPage = first.Layout.Pages[i];
            var actualPage = second.Layout.Pages[i];
            Assert.Equal(expectedPage.Name, actualPage.Name);
            Assert.Equal(expectedPage.TemplateId, actualPage.TemplateId);
            AssertSameSlots(expectedPage.Slots, actualPage.Slots);
            AssertSameSlots(expectedPage.Parked, actualPage.Parked);
        }
    }

    private static void AssertSameSlots(IReadOnlyList<LayoutSlot> expected, IReadOnlyList<LayoutSlot> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Index, actual[i].Index);
            Assert.Equal(expected[i].Action, actual[i].Action);
            Assert.Equal(expected[i].Macro, actual[i].Macro);
            Assert.Equal(expected[i].Label, actual[i].Label);
            Assert.Equal(expected[i].LongPress?.Action, actual[i].LongPress?.Action);
            Assert.Equal(expected[i].LongPress?.Macro, actual[i].LongPress?.Macro);
        }
    }

    [Fact]
    public void Parse_WithLongPressFixture_ParsesLongPressActionAndMacro()
    {
        var result = LayoutJson.Parse(ReadFixture("valid-with-long-press.json"));

        Assert.True(result.Success, result.Error);
        var slot = result.Layout!.Pages[0].Slots.Single(s => s.Index == 3);

        Assert.Equal("IncreaseEnginesPower", slot.Action);
        Assert.NotNull(slot.LongPress);
        Assert.Null(slot.LongPress!.Action);
        Assert.Equal("pips-engines", slot.LongPress.Macro);
    }

    [Fact]
    public void Parse_WithParkedFixture_ParsesParkedSlotsSeparatelyFromActiveSlots()
    {
        var result = LayoutJson.Parse(ReadFixture("valid-with-parked.json"));

        Assert.True(result.Success, result.Error);
        var page = result.Layout!.Pages[0];

        Assert.Equal(2, page.Slots.Count);
        var parked = Assert.Single(page.Parked);
        Assert.Equal(8, parked.Index);
        Assert.Equal("UseBoostJuice", parked.Action);
    }

    [Fact]
    public void Parse_MissingParkedArray_DefaultsToEmpty_NotAnError()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "pages": [
                { "name": "SHIP", "templateId": "t6", "slots": [ { "index": 0, "action": "LandingGearToggle" } ] }
              ]
            }
            """;

        var result = LayoutJson.Parse(json);

        Assert.True(result.Success, result.Error);
        Assert.Empty(result.Layout!.Pages[0].Parked);
    }

    [Fact]
    public void Parse_MissingSchemaVersion_ReturnsFailure_NotException()
    {
        const string json = """{ "pages": [] }""";

        var result = LayoutJson.Parse(json);

        Assert.False(result.Success);
        Assert.Null(result.Layout);
        Assert.Contains("schemaVersion", result.Error);
    }

    [Fact]
    public void Parse_MissingPagesArray_ReturnsFailure()
    {
        const string json = """{ "schemaVersion": 1 }""";

        var result = LayoutJson.Parse(json);

        Assert.False(result.Success);
        Assert.Contains("pages", result.Error);
    }

    [Fact]
    public void Parse_SlotMissingIndex_ReturnsFailure_NamingPage()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "pages": [
                { "name": "SHIP", "templateId": "t6", "slots": [ { "action": "LandingGearToggle" } ] }
              ]
            }
            """;

        var result = LayoutJson.Parse(json);

        Assert.False(result.Success);
        Assert.Contains("SHIP", result.Error);
        Assert.Contains("index", result.Error);
    }

    [Fact]
    public void Parse_NotJsonAtAll_ReturnsFailure_NotException()
    {
        var result = LayoutJson.Parse("this is not json");

        Assert.False(result.Success);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void Serialize_OmitsNullOptionalFields()
    {
        var layout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, "LandingGearToggle", null, null, null) }, Array.Empty<LayoutSlot>())
        });

        var json = LayoutJson.Serialize(layout);

        Assert.DoesNotContain("\"macro\"", json);
        Assert.DoesNotContain("\"label\"", json);
        Assert.DoesNotContain("\"longPress\"", json);
        Assert.Contains("\"action\": \"LandingGearToggle\"", json);
    }

    // ------------------------------------------------------------------
    // showWhen - a page's declared vessel context (ref/docs/vessel-context.md).
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_PageWithShowWhen_CarriesEveryTokenInOrder()
    {
        var json = """
        {
          "schemaVersion": 1,
          "pages": [
            { "name": "NOMAD", "templateId": "t6", "showWhen": ["InSrv", "Vessel:lander01"], "slots": [], "parked": [] }
          ]
        }
        """;

        var result = LayoutJson.Parse(json);

        Assert.True(result.Success, result.Error);
        Assert.Equal(new[] { "InSrv", "Vessel:lander01" }, result.Layout!.Pages[0].ShowWhen);
    }

    /// <summary>
    /// Every layout written before this feature existed has no
    /// <c>showWhen</c> at all, and must keep meaning "context-free" rather
    /// than becoming an empty list that a future AND could read as vacuously
    /// true.
    /// </summary>
    [Fact]
    public void Parse_PageWithoutShowWhen_IsNull_NotAnEmptyList()
    {
        var result = LayoutJson.Parse(ReadFixture("valid-multi-page.json"));

        Assert.True(result.Success, result.Error);
        Assert.All(result.Layout!.Pages, page => Assert.Null(page.ShowWhen));
    }

    [Fact]
    public void Parse_ShowWhenIsNotAnArray_ReportsFailureNamingThePage()
    {
        var result = LayoutJson.Parse("""
        { "schemaVersion": 1, "pages": [ { "name": "NOMAD", "templateId": "t6", "showWhen": "InSrv" } ] }
        """);

        Assert.False(result.Success);
        Assert.Contains("NOMAD", result.Error!, StringComparison.Ordinal);
        Assert.Contains("showWhen", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ShowWhenContainsANonString_ReportsFailureNamingThePage()
    {
        var result = LayoutJson.Parse("""
        { "schemaVersion": 1, "pages": [ { "name": "NOMAD", "templateId": "t6", "showWhen": ["InSrv", 7] } ] }
        """);

        Assert.False(result.Success);
        Assert.Contains("NOMAD", result.Error!, StringComparison.Ordinal);
        Assert.Contains("showWhen", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_PageWithShowWhen_WritesItBack_AndItSurvivesAReparse()
    {
        var layout = new Layout(1, new[]
        {
            new LayoutPage("NOMAD", "t6", Array.Empty<LayoutSlot>(), Array.Empty<LayoutSlot>(), new[] { "InSrv", "Vessel:lander01" })
        });

        var json = LayoutJson.Serialize(layout);
        var reparsed = LayoutJson.Parse(json);

        Assert.True(reparsed.Success, reparsed.Error);
        Assert.Equal(new[] { "InSrv", "Vessel:lander01" }, reparsed.Layout!.Pages[0].ShowWhen);
    }

    [Fact]
    public void Serialize_ContextFreePage_OmitsShowWhenEntirely()
    {
        var layout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", Array.Empty<LayoutSlot>(), Array.Empty<LayoutSlot>())
        });

        Assert.DoesNotContain("showWhen", LayoutJson.Serialize(layout), StringComparison.Ordinal);
    }

    /// <summary>
    /// An empty <c>showWhen</c> is the same statement as no <c>showWhen</c>,
    /// so it must not be written back as <c>"showWhen": []</c> - two spellings
    /// of one meaning is how the two drift apart later.
    /// </summary>
    [Fact]
    public void Serialize_EmptyShowWhen_OmitsItToo_AndReparsesAsContextFree()
    {
        var layout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", Array.Empty<LayoutSlot>(), Array.Empty<LayoutSlot>(), Array.Empty<string>())
        });

        var json = LayoutJson.Serialize(layout);

        Assert.DoesNotContain("showWhen", json, StringComparison.Ordinal);
        Assert.Null(LayoutJson.Parse(json).Layout!.Pages[0].ShowWhen);
    }
}
