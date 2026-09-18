using LunaPanel.Server.Discovery;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

public class HealthEndpointTests
{
    private static PathDiscoveryResult DiscoveryWith(bool bindsFound, bool eliteFound) => new(
        EliteInstallations: eliteFound
            ? new[] { new EliteInstallation(EliteEdition.Odyssey, @"C:\fake\Products\od", EliteSource.Steam) }
            : Array.Empty<EliteInstallation>(),
        Bindings: new BindingsDiscoveryResult(
            bindsFound ? @"C:\fake\Custom.4.9.binds" : null,
            bindsFound ? new BindsVersion(4, 9) : null,
            Array.Empty<string>()),
        BindingsSelection: new PresetSelectionResult(null, null, PresetSelectionMethod.Fallback, null, null, false, null),
        Edhm: new EdhmDiscoveryResult(false, null, null, Array.Empty<EdhmEditionData>()),
        LunaPanelDirectories: new LunaPanelDirectoryLayout(@"C:\fake\Logs", @"C:\fake\Layouts", @"C:\fake\Pairing\device-registry.json"),
        StatusJson: new StatusJsonDiscoveryResult(null));

    [Fact]
    public void BuildResponse_EverythingFound_ReportsAllTrue()
    {
        var response = HealthEndpoint.BuildResponse(DiscoveryWith(bindsFound: true, eliteFound: true));

        Assert.True(response.Live);
        Assert.True(response.BindsFound);
        Assert.True(response.EliteInstallFound);
    }

    [Fact]
    public void BuildResponse_NothingFound_StillLive_ButOtherFieldsFalse()
    {
        var response = HealthEndpoint.BuildResponse(DiscoveryWith(bindsFound: false, eliteFound: false));

        Assert.True(response.Live);
        Assert.False(response.BindsFound);
        Assert.False(response.EliteInstallFound);
    }
}
