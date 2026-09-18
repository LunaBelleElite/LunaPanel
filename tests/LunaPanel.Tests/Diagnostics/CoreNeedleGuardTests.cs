namespace LunaPanel.Tests.Diagnostics;

/// <summary>
/// Turns "LunaPanel.Core never discovers a filesystem path" from a
/// discipline into a build failure: scans every .cs file under
/// src/LunaPanel.Core/ for the literal markers a path-discovering call
/// would need to use. The source directory is located relative to this
/// test assembly (by walking up to the repo root, identified by
/// LunaPanel.sln) rather than hardcoded, so this still works from a clone.
/// </summary>
public class CoreNeedleGuardTests
{
    private static readonly string[] ForbiddenMarkers =
    {
        "GetFolderPath",
        "SpecialFolder",
        "LOCALAPPDATA",
        "USERPROFILE",
        "Saved Games"
    };

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
    public void CoreSourceFiles_ContainNoFilesystemDiscoveryMarkers()
    {
        var coreDir = Path.Combine(FindRepoRoot(), "src", "LunaPanel.Core");
        Assert.True(Directory.Exists(coreDir), $"Expected {coreDir} to exist.");

        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(coreDir, "*.cs", SearchOption.AllDirectories))
        {
            // Generated build artifacts under bin/obj are not hand-written
            // source and are not what this guard exists to police.
            var relative = Path.GetRelativePath(coreDir, file);
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Contains("bin") || segments.Contains("obj"))
            {
                continue;
            }

            var content = File.ReadAllText(file);
            foreach (var marker in ForbiddenMarkers)
            {
                if (content.Contains(marker, StringComparison.Ordinal))
                {
                    violations.Add($"{relative} contains forbidden marker '{marker}'");
                }
            }
        }

        Assert.True(violations.Count == 0, "LunaPanel.Core must never discover a filesystem path:\n" + string.Join("\n", violations));
    }
}
