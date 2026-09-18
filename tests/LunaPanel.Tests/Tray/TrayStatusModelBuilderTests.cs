using LunaPanel.Server.Discovery;
using LunaPanel.Server.Tray;

namespace LunaPanel.Tests.Tray;

/// <summary>
/// Pins <see cref="TrayStatusModelBuilder"/>'s output for the tray's Status
/// window - the tray's equivalent of <c>ServerHostBuilder</c>'s console
/// startup banner, which a <c>WinExe</c> tray has no console to show it in.
///
/// <b>Supersedes <c>TrayStatusTextTests</c> (deleted 2026-09-07, "Option A:
/// Windows 11 dark" restyle).</b> The previous pin asserted substrings of
/// one flat string returned by the now-deleted <c>TrayStatusText.Build</c>
/// (e.g. <c>Assert.Contains("Elite install found: no", text)</c>). The new
/// design needs found/not-found rows individually coloured, which a flat
/// string can't carry - this file asserts the same four facts (URL,
/// Elite/bindings/EDHM found-or-not, paired device count) against the new
/// <see cref="TrayStatusModel"/> shape instead.
/// </summary>
public class TrayStatusModelBuilderTests
{
    private static PathDiscoveryResult BuildDiscovery(
        bool eliteFound = false,
        string? bindsFilePath = null,
        bool edhmFound = false)
    {
        var eliteInstallations = eliteFound
            ? new[] { new EliteInstallation(EliteEdition.Odyssey, @"C:\fake\Elite", EliteSource.Steam) }
            : Array.Empty<EliteInstallation>();

        return new PathDiscoveryResult(
            EliteInstallations: eliteInstallations,
            Bindings: new BindingsDiscoveryResult(bindsFilePath, bindsFilePath is not null ? new BindsVersion(4, 2) : null, Array.Empty<string>()),
            BindingsSelection: new PresetSelectionResult(null, null, PresetSelectionMethod.Fallback, null, null, false, null),
            Edhm: new EdhmDiscoveryResult(edhmFound, null, null, Array.Empty<EdhmEditionData>()),
            LunaPanelDirectories: new LunaPanelDirectoryLayout(@"C:\fake\Logs", @"C:\fake\Layouts", @"C:\fake\Pairing\device-registry.json"),
            StatusJson: new StatusJsonDiscoveryResult(null));
    }

    [Fact]
    public void Build_NothingFound_EveryProbeRowIsNotFound_AndZeroDevices()
    {
        var model = TrayStatusModelBuilder.Build("http://192.168.1.37:51823/", BuildDiscovery(), pairedDeviceCount: 0);

        Assert.Equal("http://192.168.1.37:51823/", model.Url);
        Assert.Equal(4, model.Rows.Count);

        AssertRow(model.Rows[0], "Elite install found", StatusRowState.NotFound);
        AssertRow(model.Rows[1], "Bindings found", StatusRowState.NotFound);
        AssertRow(model.Rows[2], "EDHM theme found", StatusRowState.NotFound);

        Assert.Equal("Paired devices", model.Rows[3].Label);
        Assert.Equal("0", model.Rows[3].Value);
        Assert.Null(model.Rows[3].State);
    }

    [Fact]
    public void Build_EverythingFound_EveryProbeRowIsFound_AndDeviceCount()
    {
        var discovery = BuildDiscovery(eliteFound: true, bindsFilePath: @"C:\fake\Custom.4.2.binds", edhmFound: true);

        var model = TrayStatusModelBuilder.Build("http://192.168.1.37:51823/", discovery, pairedDeviceCount: 3);

        AssertRow(model.Rows[0], "Elite install found", StatusRowState.Found);
        AssertRow(model.Rows[1], "Bindings found", StatusRowState.Found);
        AssertRow(model.Rows[2], "EDHM theme found", StatusRowState.Found);

        Assert.Equal("3", model.Rows[3].Value);
    }

    private static void AssertRow(TrayStatusRow row, string expectedLabel, StatusRowState expectedState)
    {
        Assert.Equal(expectedLabel, row.Label);
        Assert.Equal(expectedState, row.State);
    }
}
