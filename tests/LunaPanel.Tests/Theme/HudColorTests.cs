using LunaPanel.Core.Theme;

namespace LunaPanel.Tests.Theme;

/// <summary>Drives <see cref="HudColor.TryParseHex"/> - untrusted client input, so this must never throw.</summary>
public class HudColorTests
{
    [Theory]
    [InlineData("#ff8000", 0xFF, 0x80, 0x00)]
    [InlineData("ff8000", 0xFF, 0x80, 0x00)]
    [InlineData("#FF8000", 0xFF, 0x80, 0x00)]
    [InlineData("#000000", 0x00, 0x00, 0x00)]
    [InlineData("#ffffff", 0xFF, 0xFF, 0xFF)]
    public void TryParseHex_ValidInput_ParsesExactBytes(string input, byte r, byte g, byte b)
    {
        var parsed = HudColor.TryParseHex(input, out var color);

        Assert.True(parsed);
        Assert.Equal(new HudColor(r, g, b), color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#ff80")]        // too short
    [InlineData("#ff800000")]    // too long
    [InlineData("ff800")]        // 5 chars, no #
    [InlineData("#gggggg")]      // not hex digits
    [InlineData("orange")]       // a named colour, not hex
    public void TryParseHex_InvalidInput_ReturnsFalse_NotThrown(string? input)
    {
        var parsed = HudColor.TryParseHex(input, out var color);

        Assert.False(parsed);
        Assert.Equal(default, color);
    }

    [Fact]
    public void ToHex_TryParseHex_RoundTrips()
    {
        var original = new HudColor(0x12, 0x34, 0x56);

        var parsed = HudColor.TryParseHex(original.ToHex(), out var roundTripped);

        Assert.True(parsed);
        Assert.Equal(original, roundTripped);
    }
}
