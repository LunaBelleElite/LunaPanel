using LunaPanel.Server.Discovery;
using Xunit.Abstractions;

namespace LunaPanel.Tests.Discovery;

/// <summary>
/// Windows-only smoke test: proves the SHGetKnownFolderPath P/Invoke marshals
/// and answers with a usable path. It deliberately does not assert WHICH
/// location, because a user may legitimately have moved the folder.
/// </summary>
public class Win32KnownFolderLookupTests
{
    private readonly ITestOutputHelper _output;

    public Win32KnownFolderLookupTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void GetSavedGamesPath_OnWindows_ReturnsAStableFullyQualifiedPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var first = Win32KnownFolderLookup.GetSavedGamesPath();
        var second = Win32KnownFolderLookup.GetSavedGamesPath();

        _output.WriteLine("SavedGamesPath=" + first);

        Assert.NotNull(first);
        Assert.True(Path.IsPathFullyQualified(first));
        Assert.DoesNotContain('\0', first);
        Assert.Equal(first, second);
    }
}
