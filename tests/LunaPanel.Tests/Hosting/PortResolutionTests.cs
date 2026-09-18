using LunaPanel.Core.Network;
using LunaPanel.Server.Hosting;

namespace LunaPanel.Tests.Hosting;

/// <summary>
/// Pins <see cref="PortResolution.Resolve"/>'s precedence - env var (valid
/// int) beats the persisted setting, which beats
/// <see cref="PortSettings.DefaultPort"/> - pulled out of
/// <see cref="RealServerEnvironment"/> specifically so it is testable, the
/// same split <c>LunaPanel.Tests.Http.HostRequestTests</c> documents for
/// <c>HostRequest.ResolveAccessPort</c> against that same untested call site.
/// </summary>
public class PortResolutionTests
{
    [Fact]
    public void EnvVarPresentAndValid_Wins_OverThePersistedSetting()
    {
        var persisted = new PortSettings(9000);

        Assert.Equal(51823, PortResolution.Resolve("51823", persisted));
    }

    [Fact]
    public void EnvVarAbsent_FallsBackToThePersistedSetting()
    {
        var persisted = new PortSettings(9000);

        Assert.Equal(9000, PortResolution.Resolve(null, persisted));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a port")]
    public void EnvVarInvalid_FallsBackToThePersistedSetting(string? envVarValue)
    {
        var persisted = new PortSettings(9000);

        Assert.Equal(9000, PortResolution.Resolve(envVarValue, persisted));
    }

    [Fact]
    public void EnvVarAbsent_AndPersistedIsDefault_ResolvesToDefaultPort()
    {
        Assert.Equal(PortSettings.DefaultPort, PortResolution.Resolve(null, PortSettings.Default));
    }
}
