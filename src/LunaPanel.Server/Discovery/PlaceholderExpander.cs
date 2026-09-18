using System.Text.RegularExpressions;

namespace LunaPanel.Server.Discovery;

/// <summary>
/// Expands <c>%VARNAME%</c>-style placeholders in a string against a
/// supplied variable table. Deliberately not
/// <see cref="Environment.ExpandEnvironmentVariables"/>: that reads the real
/// process environment directly, which would make any caller untestable
/// without touching the real OS environment. A placeholder with no matching
/// entry is left exactly as written, rather than removed or throwing - this
/// matters because EDHM-UI-V3's own shipped default settings file was found,
/// on the authoring machine, to still contain the literal, unexpanded
/// <c>%USERPROFILE%\EDHM_UI</c> for a value the running app itself later
/// resolves and rewrites in place - so an unresolvable placeholder here is a
/// real, observed shape, not a hypothetical.
/// </summary>
public static class PlaceholderExpander
{
    private static readonly Regex PlaceholderRegex = new(@"%([^%]+)%", RegexOptions.Compiled);

    public static string Expand(string input, IReadOnlyDictionary<string, string> variables)
    {
        return PlaceholderRegex.Replace(input, match =>
        {
            var name = match.Groups[1].Value;
            foreach (var pair in variables)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }

            return match.Value;
        });
    }
}
