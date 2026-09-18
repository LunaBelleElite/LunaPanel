namespace LunaPanel.Core.Updates;

/// <summary>
/// Compares two of this project's own version strings
/// (<c>ver-A.B.C.D</c>, optionally with a trailing <c>-suffix</c> such as
/// <c>-dev</c> - see the repo root's CLAUDE.md for the scheme).
///
/// <b>The suffix is ignored, not ordered.</b> A published release's tag comes
/// off <c>main</c> and never carries one; the build a commander is usually
/// running comes off <c>dev</c> and always does. Ordering the suffix would
/// make a dev build that is already ahead of the last release read as an
/// available "update" - offering a downgrade - which is the single most
/// likely way this could misfire in normal use.
///
/// <b>Malformed input is "not newer", never an exception.</b> This is the
/// gate in front of downloading and silently installing an MSI: anything
/// unreadable has to mean "do nothing" rather than throwing out of a click
/// handler or, worse, being treated as newer.
/// </summary>
public static class VersionComparer
{
    /// <summary>
    /// True only when <paramref name="candidate"/> parses, <paramref name="current"/>
    /// parses, and the candidate's four numbers sort strictly above the
    /// current's. Equal numbers are not newer - there is nothing to install.
    /// </summary>
    public static bool IsNewer(string? candidate, string? current)
    {
        if (!TryParse(candidate, out var candidateParts) || !TryParse(current, out var currentParts))
        {
            return false;
        }

        for (var i = 0; i < PartCount; i++)
        {
            if (candidateParts[i] != currentParts[i])
            {
                return candidateParts[i] > currentParts[i];
            }
        }

        return false;
    }

    /// <summary>
    /// How many numbers a version carries. Fixed at four by the scheme -
    /// three or five is malformed, not a shorter/longer version, because
    /// guessing which positions a three-number string meant is exactly the
    /// kind of silent reinterpretation that ends in the wrong install.
    /// </summary>
    private const int PartCount = 4;

    private const string Prefix = "ver-";

    /// <summary>
    /// Parses <c>[ver-]A.B.C.D[-suffix]</c> into its four numbers.
    ///
    /// The <c>ver-</c> prefix is optional: this project's CHANGELOG header
    /// and its git tags both carry it, but a bare four-number string is
    /// unambiguous and refusing it would turn a cosmetic tagging slip into a
    /// silent "no updates ever".
    /// </summary>
    private static bool TryParse(string? version, out long[] parts)
    {
        parts = new long[PartCount];

        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var text = version.Trim();
        if (text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            text = text[Prefix.Length..];
        }

        // The suffix is cut at the FIRST '-', not the last: everything after
        // it is discarded wholesale, so "-dev", "-rc.1" and anything else
        // this project might adopt later all behave identically without this
        // needing to know what they are.
        var suffixStart = text.IndexOf('-');
        if (suffixStart >= 0)
        {
            text = text[..suffixStart];
        }

        var fields = text.Split('.');
        if (fields.Length != PartCount)
        {
            return false;
        }

        for (var i = 0; i < PartCount; i++)
        {
            // long rather than int because the scheme puts no cap on any
            // number, and int.MaxValue is a cap. Anything that overflows even
            // this is malformed rather than enormous.
            if (!long.TryParse(fields[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            parts[i] = value;
        }

        return true;
    }
}
