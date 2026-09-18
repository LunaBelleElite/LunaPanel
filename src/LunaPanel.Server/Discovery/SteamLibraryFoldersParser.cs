namespace LunaPanel.Server.Discovery;

/// <summary>
/// Extracts the list of Steam library root paths from an already-read
/// <c>libraryfolders.vdf</c> file's text. Each library is a numbered child
/// ("0", "1", ...) of the top-level "libraryfolders" block, each carrying a
/// "path" string - see <see cref="SteamKeyValues"/> for the underlying
/// format parse.
/// </summary>
public static class SteamLibraryFoldersParser
{
    public static IReadOnlyList<string> ExtractLibraryPaths(string vdfText)
    {
        var root = SteamKeyValues.Parse(vdfText);
        var libraryFolders = root.GetNode("libraryfolders");
        if (libraryFolders is null)
        {
            return Array.Empty<string>();
        }

        var paths = new List<string>();
        foreach (var key in libraryFolders.Keys)
        {
            var entry = libraryFolders.GetNode(key);
            var path = entry?.GetString("path");
            if (!string.IsNullOrEmpty(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }
}
