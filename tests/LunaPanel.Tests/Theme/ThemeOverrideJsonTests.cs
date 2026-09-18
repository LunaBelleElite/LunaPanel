using LunaPanel.Core.Theme;

namespace LunaPanel.Tests.Theme;

/// <summary>
/// Drives <see cref="ThemeOverrideJson"/> directly - never throws on
/// malformed input, same discipline as
/// <c>LunaPanel.Core.Layouts.LayoutJson</c>.
/// </summary>
public class ThemeOverrideJsonTests
{
    [Fact]
    public void Parse_WellFormed_RoundTripsExactColours()
    {
        var overrideValue = new ThemeOverride(new HudColor(0xFF, 0x80, 0x00), new HudColor(0x00, 0xB8, 0xFF), new HudColor(0xFF, 0xFF, 0xFF));

        var json = ThemeOverrideJson.Serialize(overrideValue);
        var result = ThemeOverrideJson.Parse(json);

        Assert.True(result.Success);
        Assert.Equal(overrideValue, result.Override);
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsFailure_NotThrown()
    {
        var result = ThemeOverrideJson.Parse("{ not valid json");

        Assert.False(result.Success);
        Assert.Null(result.Override);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_ValidJsonButNotAnObject_ReturnsFailure()
    {
        var result = ThemeOverrideJson.Parse("[1,2,3]");

        Assert.False(result.Success);
    }

    [Theory]
    [InlineData("""{ "text": "#ff8000", "lit": "#ff8000" }""")]     // border missing
    [InlineData("""{ "border": "#ff8000", "lit": "#ff8000" }""")]   // text missing
    [InlineData("""{ "border": "#ff8000", "text": "#ff8000" }""")]  // lit missing
    public void Parse_MissingRequiredColour_ReturnsFailure(string json)
    {
        var result = ThemeOverrideJson.Parse(json);

        Assert.False(result.Success);
    }

    [Fact]
    public void Parse_InvalidHexValue_ReturnsFailure()
    {
        var result = ThemeOverrideJson.Parse("""{ "border": "not-a-colour", "text": "#ff8000", "lit": "#ff8000" }""");

        Assert.False(result.Success);
    }

    // -------------------------------------------------------------------
    // Background, the fourth commander-chosen role (2026-09-10,
    // ref/docs/theme.md). Optional on purpose: absent means "derive the
    // ground from Text", which is what every override file written before
    // this feature says, and what one whose Background was never picked
    // still says.
    // -------------------------------------------------------------------

    [Fact]
    public void Parse_WellFormedWithBackground_RoundTripsIt()
    {
        var overrideValue = new ThemeOverride(
            new HudColor(0xFF, 0x80, 0x00),
            new HudColor(0x00, 0xB8, 0xFF),
            new HudColor(0xFF, 0xFF, 0xFF),
            new HudColor(0x33, 0x78, 0xFF));

        var json = ThemeOverrideJson.Serialize(overrideValue);
        var result = ThemeOverrideJson.Parse(json);

        Assert.True(result.Success);
        Assert.Equal(overrideValue, result.Override);
        Assert.Equal(new HudColor(0x33, 0x78, 0xFF), result.Override!.Background);
    }

    [Fact]
    public void Serialize_NoChosenBackground_OmitsTheKeyEntirely()
    {
        // Not "writes null", and not "writes the derived ground": a stored
        // override records intent, never resolution (ref/docs/layouts.md,
        // ref/docs/button-naming.md). A key that is simply absent is the
        // only shape that cannot later be misread as a choice.
        var json = ThemeOverrideJson.Serialize(new ThemeOverride(
            new HudColor(0xFF, 0x80, 0x00),
            new HudColor(0x00, 0xB8, 0xFF),
            new HudColor(0xFF, 0xFF, 0xFF)));

        Assert.DoesNotContain("background", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_NoBackgroundKey_SucceedsWithNullBackground()
    {
        // The exact three-colour shape every override file on a real
        // commander's machine holds today. It must keep loading, not be
        // renamed aside as corrupt.
        var result = ThemeOverrideJson.Parse("""{ "border": "#ff8000", "text": "#00b8ff", "lit": "#ffffff" }""");

        Assert.True(result.Success);
        Assert.Null(result.Override!.Background);
    }

    [Fact]
    public void Parse_InvalidBackground_ReturnsFailure()
    {
        // Optional is not the same as ignored: a background that IS present
        // has to be a real colour, exactly like the other three.
        var result = ThemeOverrideJson.Parse("""{ "border": "#ff8000", "text": "#00b8ff", "lit": "#ffffff", "background": "not-a-colour" }""");

        Assert.False(result.Success);
    }
}
