using System.Text;

namespace LunaPanel.Core.Theme;

/// <summary>
/// Renders a resolved <see cref="HudTheme"/> as the six CSS custom
/// properties LunaPanel's stylesheet consumes. Every rule in the
/// stylesheet is expected to reference these via
/// <c>var(--lp-*, &lt;stock fallback&gt;)</c>, so a value missing here
/// degrades to stock orange in the browser rather than to nothing - this
/// renderer's only job is producing the six declarations, not supplying
/// fallbacks itself.
/// </summary>
public static class HudThemeCssRenderer
{
    /// <summary>
    /// Renders <c>:root { --lp-ground: #rrggbb; ... }</c> - always all six
    /// variables, in the fixed order <c>ground, frame, text, accent, lit,
    /// dim</c>, each a lowercase <c>#rrggbb</c> value.
    /// </summary>
    public static string Render(HudTheme theme)
    {
        var builder = new StringBuilder();
        builder.AppendLine(":root {");
        builder.AppendLine($"  --lp-ground: {theme.Ground.ToHex()};");
        builder.AppendLine($"  --lp-frame: {theme.Frame.ToHex()};");
        builder.AppendLine($"  --lp-text: {theme.Text.ToHex()};");
        builder.AppendLine($"  --lp-accent: {theme.Accent.ToHex()};");
        builder.AppendLine($"  --lp-lit: {theme.Lit.ToHex()};");
        builder.AppendLine($"  --lp-dim: {theme.Dim.ToHex()};");
        builder.Append('}');
        return builder.ToString();
    }
}
