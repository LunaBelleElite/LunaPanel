namespace LunaPanel.Server.Discovery;

/// <summary>
/// Works out Elite's live-state directory (<c>Status.json</c> and the
/// journal). Windows lets a user move the Saved Games known folder, so the
/// root is whatever the injected lookup (production:
/// <see cref="Win32KnownFolderLookup.GetSavedGamesPath"/>) reports when that
/// is a usable absolute path; otherwise the conventional
/// <c>&lt;profile&gt;\Saved Games</c>. Pure: no file-system access.
///
/// Deliberately takes no logger - this runs while the host environment is
/// being built, before the diagnostics pipeline exists. The final path is
/// logged later by <c>StatusJsonDiscovery</c>.
/// </summary>
public static class EliteSavedGamesLocator
{
    public static string ResolveStatusDirectory(string userProfile, Func<string?>? readSavedGamesFolder)
    {
        var root = TryReadUsableRoot(readSavedGamesFolder) ?? Path.Combine(userProfile, "Saved Games");
        return Path.Combine(root, "Frontier Developments", "Elite Dangerous");
    }

    private static string? TryReadUsableRoot(Func<string?>? reader)
    {
        if (reader is null)
        {
            return null;
        }

        string? value;
        try
        {
            value = reader();
        }
        catch (Exception)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // NUL and other invalid path characters, relative paths and
        // drive-relative ones ("C:foo") are all rejected; UNC shares are
        // fully qualified and pass.
        if (value.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(value))
        {
            return null;
        }

        return value;
    }
}
