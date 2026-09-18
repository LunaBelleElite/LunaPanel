using LunaPanel.Core.Diagnostics;
using LunaPanel.Server.Discovery;

namespace LunaPanel.Tests.Discovery;

public class StatusJsonDiscoveryTests
{
    [Fact]
    public void Discover_DirectoryExists_ReturnsItAndLogsFound()
    {
        using var temp = TempDirectory.Create();
        var statusDir = temp.CreateSubdirectory("StatusDir");
        var log = new DiagnosticRingBuffer(10);

        var result = StatusJsonDiscovery.Discover(statusDir, log);

        Assert.Equal(statusDir, result.Directory);
        Assert.Contains(log.Snapshot(), e => e.Message.Contains(statusDir, StringComparison.Ordinal) && e.Message.Contains("found", StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_DirectoryDoesNotExist_ReturnsNull_NotAnError()
    {
        using var temp = TempDirectory.Create();
        var missing = temp.Combine("DoesNotExist");
        var log = new DiagnosticRingBuffer(10);

        var result = StatusJsonDiscovery.Discover(missing, log);

        Assert.Null(result.Directory);
        Assert.Contains(log.Snapshot(), e => e.Message.Contains("not found", StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_NullDirectory_ReturnsNull_WithoutThrowing()
    {
        var log = new DiagnosticRingBuffer(10);

        var result = StatusJsonDiscovery.Discover(null, log);

        Assert.Null(result.Directory);
        Assert.Contains(log.Snapshot(), e => e.Message.Contains("not configured", StringComparison.Ordinal));
    }
}
