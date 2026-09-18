namespace LunaPanel.Tests.Pairing;

/// <summary>
/// Source-scan pin (repo root located by walking up to <c>LunaPanel.sln</c>,
/// the same idiom as <c>CoreNeedleGuardTests</c> and
/// <c>PanelClientSourceGuardTests</c>) closing the second half of the orphan
/// marker (<c>ref/docs/layout-import.md</c>).
///
/// <b>Why a source scan and not a behavioural test.</b> There are two forget
/// paths, and only one of them is reachable from this suite:
/// <c>DevicesEndpoint.TryForget</c> is plain logic, while the tray's
/// <c>DevicesForm</c> is a WinForms window in a Windows-only project this
/// test assembly does not reference. A caller that went back to calling
/// <c>DeviceRegistry.Forget</c> directly would still revoke the device, still
/// pass every test in <c>DeviceForgetTests</c> and
/// <c>DevicesEndpointTests</c>, and simply stop leaving a marker - so the
/// layout of a device forgotten from the tray would go back to being a line
/// of hex on the import list, with no failing test anywhere. The symptom
/// would surface days later, on a screen, in a household that had used the
/// wrong window.
///
/// This is the same reasoning <c>ref/docs/pairing.md</c> already records for
/// the <c>FixedTimeEquals</c> pin: when the property is <em>which code path
/// runs</em>, a source-scan pin is the only test that can fail when the wrong
/// one is substituted in.
/// </summary>
public class ForgetPathSourceGuardTests
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

    private static string ReadSource(params string[] relativeParts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(relativeParts).ToArray());
        Assert.True(File.Exists(path), $"Expected {path} to exist.");
        return File.ReadAllText(path);
    }

    [Fact]
    public void HttpForgetPath_GoesThroughDeviceForget_NotTheRegistryDirectly()
    {
        var content = ReadSource("src", "LunaPanel.Server", "Http", "DevicesEndpoint.cs");

        Assert.Contains("DeviceForget.Forget(", content, StringComparison.Ordinal);
        Assert.DoesNotContain("registry.Forget(", content, StringComparison.Ordinal);
    }

    [Fact]
    public void TrayForgetPath_GoesThroughDeviceForget_NotTheRegistryDirectly()
    {
        var content = ReadSource("src", "LunaPanel.Tray", "DevicesForm.cs");

        Assert.Contains("DeviceForget.Forget(", content, StringComparison.Ordinal);
        Assert.DoesNotContain("_registry.Forget(", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The positive half stated once more, from the other end: exactly the
    /// two files above are expected to forget a device, so a third caller
    /// appearing anywhere under <c>src/</c> that reaches for
    /// <c>DeviceRegistry.Forget</c> without going through
    /// <see cref="LunaPanel.Server.Pairing.DeviceForget"/> is a new,
    /// marker-less forget path and fails here rather than in a household.
    /// <c>DeviceForget.cs</c> itself is the one legitimate call site.
    /// </summary>
    [Fact]
    public void NoOtherSourceFile_CallsRegistryForgetDirectly()
    {
        var root = Path.Combine(FindRepoRoot(), "src");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Contains("bin") || segments.Contains("obj"))
            {
                continue;
            }

            // The registry's own declaration, and the one wrapper allowed to
            // call it.
            if (segments[^1] is "DeviceRegistry.cs" or "DeviceForget.cs")
            {
                continue;
            }

            var content = File.ReadAllText(file);
            if (content.Contains(".Forget(", StringComparison.Ordinal) &&
                !content.Contains("DeviceForget.Forget(", StringComparison.Ordinal))
            {
                offenders.Add(relative);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Every forget must go through DeviceForget so the orphan marker is written:\n" + string.Join("\n", offenders));
    }
}
