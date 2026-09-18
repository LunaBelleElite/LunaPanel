using LunaPanel.Core.Theme;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Drives <see cref="ThemeEndpoint"/>'s pure logic directly - the settings
/// gear's colour pane, kept free of any ASP.NET type, same discipline as
/// <see cref="PanelEndpoint"/>/<see cref="PressEndpoint"/>.
/// </summary>
public class ThemeEndpointTests
{
    [Theory]
    [InlineData(HudThemeSource.Edhm, "Matched")]
    [InlineData(HudThemeSource.EdhmMatrix, "Matched")]
    [InlineData(HudThemeSource.GraphicsConfigurationOverride, "Stock")]
    [InlineData(HudThemeSource.GraphicsConfigurationDefault, "Stock")]
    [InlineData(HudThemeSource.Stock, "Stock")]
    [InlineData(HudThemeSource.Override, "Override")]
    public void SourceKind_MapsEveryInternalSourceToTheRightThreeWayBucket(HudThemeSource source, string expectedKind)
    {
        Assert.Equal(expectedKind, ThemeEndpoint.SourceKind(source));
    }

    [Fact]
    public void BuildStatusResponse_ReportsBorderTextLitFromTheEffectiveTheme_AsHex()
    {
        var theme = HudThemeResolver.FromOverride(new ThemeOverride(new HudColor(1, 2, 3), new HudColor(4, 5, 6), new HudColor(7, 8, 9)));

        var response = ThemeEndpoint.BuildStatusResponse(theme, overrideActive: true, backgroundChosen: false, Array.Empty<EdhmDiscoveredColour>());

        Assert.Equal("#010203", response.Border);
        Assert.Equal("#040506", response.Text);
        Assert.Equal("#070809", response.Lit);
        Assert.Equal("Override", response.Source);
        Assert.Equal("Override", response.SourceKind);
        Assert.True(response.OverrideActive);
        Assert.Empty(response.HudColours);
    }

    [Fact]
    public void BuildStatusResponse_MapsDiscoveredColoursToLabelAndHex()
    {
        var theme = HudThemeResolver.FromOverride(new ThemeOverride(new HudColor(1, 2, 3), new HudColor(4, 5, 6), new HudColor(7, 8, 9)));
        var discovered = new[]
        {
            new EdhmDiscoveredColour("Main Text", new HudColor(0x95, 0xFB, 0xFF)),
            new EdhmDiscoveredColour("Chat Panel Lines", new HudColor(0xFE, 0x91, 0xB3)),
        };

        var response = ThemeEndpoint.BuildStatusResponse(theme, overrideActive: false, backgroundChosen: false, discovered);

        Assert.Equal(2, response.HudColours.Count);
        Assert.Equal("Main Text", response.HudColours[0].Label);
        Assert.Equal("#95fbff", response.HudColours[0].Hex, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Chat Panel Lines", response.HudColours[1].Label);
        Assert.Equal("#fe91b3", response.HudColours[1].Hex, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParseOverride_AllThreeValidHexColours_Succeeds()
    {
        var request = new ThemeEndpoint.OverrideRequest("#ff8000", "#00b8ff", "#ffffff");

        var ok = ThemeEndpoint.TryParseOverride(request, out var overrideValue, out var error);

        Assert.True(ok);
        Assert.Equal(new HudColor(0xFF, 0x80, 0x00), overrideValue.Border);
        Assert.Equal(new HudColor(0x00, 0xB8, 0xFF), overrideValue.Text);
        Assert.Equal(new HudColor(0xFF, 0xFF, 0xFF), overrideValue.Lit);
        Assert.Empty(error);
    }

    [Fact]
    public void TryParseOverride_NullRequest_Fails()
    {
        var ok = ThemeEndpoint.TryParseOverride(null, out _, out var error);

        Assert.False(ok);
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData(null, "#00b8ff", "#ffffff")]
    [InlineData("#ff8000", null, "#ffffff")]
    [InlineData("#ff8000", "#00b8ff", null)]
    [InlineData("not-a-colour", "#00b8ff", "#ffffff")]
    public void TryParseOverride_AnyInvalidColour_Fails(string? border, string? text, string? lit)
    {
        var ok = ThemeEndpoint.TryParseOverride(new ThemeEndpoint.OverrideRequest(border, text, lit), out _, out var error);

        Assert.False(ok);
        Assert.NotEmpty(error);
    }

    // -------------------------------------------------------------------
    // Background, the fourth commander-chosen role (2026-09-10,
    // ref/docs/theme.md), and the non-blocking contrast warning that came
    // with it.
    // -------------------------------------------------------------------

    [Fact]
    public void TryParseOverride_WithBackground_CarriesItThrough()
    {
        var request = new ThemeEndpoint.OverrideRequest("#ff8000", "#00b8ff", "#ffffff", "#3378ff");

        var ok = ThemeEndpoint.TryParseOverride(request, out var overrideValue, out _);

        Assert.True(ok);
        Assert.Equal(new HudColor(0x33, 0x78, 0xFF), overrideValue.Background);
    }

    [Fact]
    public void TryParseOverride_NoBackground_SucceedsWithNoneChosen()
    {
        // Omitting it is how the pane says "I am not changing the
        // background" - it is not an error and it is not a black default.
        var request = new ThemeEndpoint.OverrideRequest("#ff8000", "#00b8ff", "#ffffff");

        var ok = ThemeEndpoint.TryParseOverride(request, out var overrideValue, out _);

        Assert.True(ok);
        Assert.Null(overrideValue.Background);
    }

    [Fact]
    public void TryParseOverride_InvalidBackground_Fails()
    {
        var request = new ThemeEndpoint.OverrideRequest("#ff8000", "#00b8ff", "#ffffff", "not-a-colour");

        var ok = ThemeEndpoint.TryParseOverride(request, out _, out var error);

        Assert.False(ok);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void BuildStatusResponse_ReportsTheEffectiveBackground_AndWhetherItWasChosen()
    {
        var chosen = HudThemeResolver.FromOverride(new ThemeOverride(
            new HudColor(1, 2, 3), new HudColor(4, 5, 6), new HudColor(7, 8, 9), new HudColor(0x33, 0x78, 0xFF)));

        var response = ThemeEndpoint.BuildStatusResponse(chosen, overrideActive: true, backgroundChosen: true, Array.Empty<EdhmDiscoveredColour>());

        Assert.Equal("#3378ff", response.Background, StringComparer.OrdinalIgnoreCase);
        Assert.True(response.BackgroundChosen);
    }

    [Fact]
    public void BuildStatusResponse_DerivedBackground_IsStillReported_ButNotAsChosen()
    {
        // The swatch has to show something even before the commander has
        // picked a background - but the pane must be able to tell the two
        // apart, or it would post a derived value back as a choice.
        var derived = HudThemeResolver.FromOverride(new ThemeOverride(
            new HudColor(1, 2, 3), new HudColor(0xCC, 0x5F, 0xF1), new HudColor(7, 8, 9)));

        var response = ThemeEndpoint.BuildStatusResponse(derived, overrideActive: true, backgroundChosen: false, Array.Empty<EdhmDiscoveredColour>());

        Assert.Equal("#100813", response.Background, StringComparer.OrdinalIgnoreCase);
        Assert.False(response.BackgroundChosen);
    }

    [Fact]
    public void BuildStatusResponse_ARoleLostAgainstTheBackground_IsNamedInLowContrast()
    {
        // Lit picked equal to a chosen background: applied exactly as
        // asked (nothing here refuses it) and reported as a warning.
        var theme = HudThemeResolver.FromOverride(new ThemeOverride(
            new HudColor(0x95, 0xFB, 0xFF),
            new HudColor(0xFE, 0x91, 0xB3),
            new HudColor(0x14, 0x0C, 0x0E),
            new HudColor(0x14, 0x0C, 0x0E)));

        var response = ThemeEndpoint.BuildStatusResponse(theme, overrideActive: true, backgroundChosen: true, Array.Empty<EdhmDiscoveredColour>());

        Assert.Equal("#140c0e", response.Lit, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(new[] { "Lit" }, response.LowContrast);
    }

    [Fact]
    public void BuildStatusResponse_EverythingLegible_ReportsNoLowContrastRoles()
    {
        var theme = HudThemeResolver.FromOverride(new ThemeOverride(
            new HudColor(0x95, 0xFB, 0xFF),
            new HudColor(0xFE, 0x91, 0xB3),
            new HudColor(0xFF, 0xFF, 0xFF)));

        var response = ThemeEndpoint.BuildStatusResponse(theme, overrideActive: true, backgroundChosen: false, Array.Empty<EdhmDiscoveredColour>());

        Assert.Empty(response.LowContrast);
    }
}
