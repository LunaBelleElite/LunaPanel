using System.Text.RegularExpressions;
using LunaPanel.Core.Theme;

namespace LunaPanel.Tests.Theme;

/// <summary>Pins <see cref="HudThemeCssRenderer.Render"/>'s output shape.</summary>
public class HudThemeCssRendererTests
{
    private static readonly HudTheme SampleTheme = new(
        new HudColor(0x10, 0x08, 0x13),
        new HudColor(0x07, 0x7C, 0xCB),
        new HudColor(0xCC, 0x5F, 0xF1),
        new HudColor(0xDE, 0x97, 0xF6),
        new HudColor(0xED, 0xC7, 0xFA),
        new HudColor(0x70, 0x34, 0x85),
        HudThemeSource.Edhm,
        Array.Empty<string>());

    [Fact]
    public void Render_ContainsAllSixVariables_AsValidRrggbbHex()
    {
        var css = HudThemeCssRenderer.Render(SampleTheme);

        foreach (var variable in new[] { "--lp-ground", "--lp-frame", "--lp-text", "--lp-accent", "--lp-lit", "--lp-dim" })
        {
            var match = Regex.Match(css, $@"{Regex.Escape(variable)}:\s*(#[0-9a-f]{{6}});");
            Assert.True(match.Success, $"Expected {variable} to appear as a valid #rrggbb declaration in:\n{css}");
        }
    }

    [Fact]
    public void Render_WrapsDeclarationsInRootBlock()
    {
        var css = HudThemeCssRenderer.Render(SampleTheme);

        Assert.StartsWith(":root {", css);
        Assert.EndsWith("}", css.TrimEnd());
    }

    [Fact]
    public void Render_ExactValues_MatchTheme()
    {
        var css = HudThemeCssRenderer.Render(SampleTheme);

        Assert.Contains("--lp-ground: #100813;", css);
        Assert.Contains("--lp-frame: #077ccb;", css);
        Assert.Contains("--lp-text: #cc5ff1;", css);
        Assert.Contains("--lp-accent: #de97f6;", css);
        Assert.Contains("--lp-lit: #edc7fa;", css);
        Assert.Contains("--lp-dim: #703485;", css);
    }
}
