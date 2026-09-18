using LunaPanel.Server.Discovery;

namespace LunaPanel.Tests.Discovery;

/// <summary>
/// <see cref="DevBuildDetector.IsDevBuild"/> itself always reads the real
/// running test-host process's own version via
/// <see cref="System.Diagnostics.FileVersionInfo"/>/<see cref="Environment.ProcessPath"/>
/// - not a meaningful thing to assert on in a unit test, since the test
/// host's own version has nothing to do with LunaPanel's <c>-dev</c>
/// convention. What IS meaningfully testable in isolation is the pure
/// string check it wraps, <see cref="DevBuildDetector.IsDevVersion"/> -
/// pinned directly here.
/// </summary>
public class DevBuildDetectorTests
{
    [Theory]
    [InlineData("ver-1.1.1.1-dev", true)]
    [InlineData("ver-1.1.1.1-DEV", true)]
    [InlineData("ver-1.1.1.1", false)]
    [InlineData("ver-1.0.0.0", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsDevVersion_MatchesOnTheDashDevSuffixOnly(string? productVersion, bool expected)
    {
        Assert.Equal(expected, DevBuildDetector.IsDevVersion(productVersion));
    }
}
