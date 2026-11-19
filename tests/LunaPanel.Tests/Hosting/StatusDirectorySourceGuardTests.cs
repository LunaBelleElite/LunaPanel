namespace LunaPanel.Tests.Hosting;

/// <summary>
/// Source-scan pin: <c>RealServerEnvironment</c> is deliberately never
/// constructed by this suite, so nothing else notices if the Status.json /
/// journal directory goes back to a hard-coded profile path and a commander
/// who moved Windows' Saved Games folder is silently looking in the wrong place.
/// </summary>
public class StatusDirectorySourceGuardTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LunaPanel.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate repo root (a directory containing LunaPanel.sln) above {AppContext.BaseDirectory}.");
    }

    [Fact]
    public void RealServerEnvironment_AsksWindowsForTheSavedGamesFolder_NotAHardCodedProfilePath()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "LunaPanel.Server", "Hosting", "RealServerEnvironment.cs");
        Assert.True(File.Exists(path), $"Expected {path} to exist.");
        var content = File.ReadAllText(path);

        Assert.Contains("EliteSavedGamesLocator.ResolveStatusDirectory(", content, StringComparison.Ordinal);
        Assert.Contains("Win32KnownFolderLookup.GetSavedGamesPath", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Path.Combine(userProfile, \"Saved Games\"", content, StringComparison.Ordinal);
    }
}
