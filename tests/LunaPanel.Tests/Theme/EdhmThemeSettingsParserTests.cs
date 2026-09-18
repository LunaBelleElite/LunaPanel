using LunaPanel.Core.Theme;

namespace LunaPanel.Tests.Theme;

/// <summary>
/// Pins <see cref="EdhmThemeSettingsParser.Parse"/> against the committed
/// <c>Fixtures/edhm/ThemeSettings.sample.json</c> fixture and hand-written
/// malformed/edge-case JSON strings.
/// </summary>
public class EdhmThemeSettingsParserTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "edhm");

    private static string ReadFixture(string name) => File.ReadAllText(Path.Combine(FixturesRoot, name));

    [Fact]
    public void Parse_SampleFixture_ReturnsExactlyThreeColorElements()
    {
        // Fixtures/edhm/ThemeSettings.sample.json has exactly 3 elements
        // with ValueType "Color" across all ui_groups (see
        // FixtureIntegrityTests.ThemeSettingsSampleJson_Parses_AndHasExactlyFourUiGroups
        // for the group count) - the rest are Preset/ONOFF/Brightness and
        // must not appear here.
        var elements = EdhmThemeSettingsParser.Parse(ReadFixture("ThemeSettings.sample.json"));

        Assert.NotNull(elements);
        Assert.Equal(3, elements!.Count);
        Assert.Contains(elements, e => e.Title == "Chat Panel Text Color");
        Assert.Contains(elements, e => e.Title == "Radar Grid Color");
        Assert.Contains(elements, e => e.Title == "Suit HUD Accent");
    }

    [Fact]
    public void Parse_SampleFixture_ChatPanelTextColor_HasExpectedFileAndKeys()
    {
        var elements = EdhmThemeSettingsParser.Parse(ReadFixture("ThemeSettings.sample.json"));
        var element = Assert.Single(elements!, e => e.Title == "Chat Panel Text Color");

        Assert.Equal("Advanced", element.File);
        Assert.Equal(new[] { "x77", "y77", "z77", "w77" }, element.Keys);
    }

    [Fact]
    public void Parse_SampleFixture_SuitHudAccent_NamesAFileNotSuppliedByAnyIni()
    {
        // This is the deliberately-unresolvable title: its File is
        // "SuitHud", which is neither "Advanced" nor "Startup-Profile" -
        // the two INI files this fixture set actually supplies. Proven
        // end-to-end (unresolved, logged, and derived instead) in
        // HudThemeResolverTests.
        var elements = EdhmThemeSettingsParser.Parse(ReadFixture("ThemeSettings.sample.json"));
        var element = Assert.Single(elements!, e => e.Title == "Suit HUD Accent");

        Assert.Equal("SuitHud", element.File);
    }

    [Fact]
    public void Parse_NullElementsGroup_IsSkipped_NotThrown()
    {
        // The real edge case this fixture set exists to prove: a
        // ui_groups entry with "Elements": null. A naive
        // elements.EnumerateArray() over a JSON null throws
        // InvalidOperationException - this must not.
        const string json = """
        {
            "ui_groups": [
                { "Name": "Group_Reserved", "Title": "Reserved", "Elements": null },
                { "Name": "Group_Real", "Title": "Real", "Elements": [
                    { "Title": "Main Text Color", "File": "Advanced", "Section": "Constants", "Key": "x1|y1|z1|w1", "Value": 0, "ValueType": "Color" }
                ] }
            ]
        }
        """;

        var elements = EdhmThemeSettingsParser.Parse(json);

        Assert.NotNull(elements);
        var element = Assert.Single(elements!);
        Assert.Equal("Main Text Color", element.Title);
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsNull()
    {
        Assert.Null(EdhmThemeSettingsParser.Parse("{ not valid json"));
    }

    [Fact]
    public void Parse_ValidJsonButNotAnObject_ReturnsNull()
    {
        Assert.Null(EdhmThemeSettingsParser.Parse("[1, 2, 3]"));
    }

    [Fact]
    public void Parse_MissingUiGroupsProperty_ReturnsNull()
    {
        Assert.Null(EdhmThemeSettingsParser.Parse("{ \"language\": \"en\" }"));
    }

    [Fact]
    public void Parse_EmptyUiGroups_ReturnsEmptyListNotNull()
    {
        // Distinct from "the file is unusable" - this is a well-formed
        // file that simply has nothing to offer, which should let a
        // resolver fall through cleanly rather than treating it as a
        // parse failure.
        var elements = EdhmThemeSettingsParser.Parse("{ \"ui_groups\": [] }");

        Assert.NotNull(elements);
        Assert.Empty(elements!);
    }
}
