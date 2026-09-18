using System.Text;

namespace LunaPanel.Core.Diagnostics;

/// <summary>
/// Rewrites filesystem roots to a variable token (e.g. an absolute
/// per-user app-data path to a placeholder such as "%TOKEN%") so a log
/// never leaks a Windows username. Constructed with the roots to redact
/// rather than discovering them - <c>LunaPanel.Core</c> is never allowed to
/// find a filesystem path itself, so callers in the host process (which is
/// allowed to ask the operating system for well-known folders) supply the
/// roots here.
/// </summary>
public sealed class PathRedactor
{
    private readonly List<(string Root, string Token)> _roots;

    /// <param name="roots">
    /// (actualRoot, replacementToken) pairs. Sorted internally longest-root-first
    /// so that when one root nests inside another (e.g. LocalAppData sits inside
    /// the user profile), the more specific, longer root is substituted first
    /// and the shorter, outer root no longer has anything left to match there.
    /// </param>
    public PathRedactor(IEnumerable<(string ActualRoot, string ReplacementToken)> roots)
    {
        _roots = roots
            .Select(r => (Root: r.ActualRoot.TrimEnd('\\', '/'), r.ReplacementToken))
            .Where(r => r.Root.Length > 0)
            .OrderByDescending(r => r.Root.Length)
            .ToList();
    }

    public string Redact(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var result = input;
        foreach (var (root, token) in _roots)
        {
            result = ReplaceCaseInsensitive(result, root, token);
        }

        return result;
    }

    private static string ReplaceCaseInsensitive(string input, string search, string replacement)
    {
        if (search.Length == 0)
        {
            return input;
        }

        var sb = new StringBuilder();
        var index = 0;
        while (true)
        {
            var found = input.IndexOf(search, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                sb.Append(input, index, input.Length - index);
                break;
            }

            sb.Append(input, index, found - index);
            sb.Append(replacement);
            index = found + search.Length;
        }

        return sb.ToString();
    }
}
