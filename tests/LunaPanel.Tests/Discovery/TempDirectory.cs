namespace LunaPanel.Tests.Discovery;

/// <summary>
/// A disposable scratch directory tree for discovery tests to build a
/// synthetic environment in - deleted on dispose. This is what lets every
/// discovery test "drive the whole search over a temp directory tree" (see
/// this task's brief) with zero dependence on any real install on the
/// machine running the suite.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    private TempDirectory(string path)
    {
        Path = path;
    }

    public static TempDirectory Create()
    {
        var info = Directory.CreateTempSubdirectory("lunapanel-discovery-tests-");
        return new TempDirectory(info.FullName);
    }

    public string Combine(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

    public string CreateFile(string relativePath, string contents = "")
    {
        var fullPath = Combine(relativePath.Split('/', '\\'));
        var directory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, contents);
        return fullPath;
    }

    public string CreateSubdirectory(string relativePath)
    {
        var fullPath = Combine(relativePath.Split('/', '\\'));
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup - a locked handle lingering past the test
            // shouldn't fail the test itself.
        }
    }
}
