using LunaPanel.Core.Catalogue;

namespace LunaPanel.Tests.Catalogue;

/// <summary>
/// Pins <see cref="Prettifier.Prettify"/> against a dozen raw Frontier-style
/// element names, including every acronym it must keep intact
/// (UI, FSS, SAA, SRV, FSD, SCO, HUD, DSS) rather than splitting letter by
/// letter or title-casing into something like "Ui"/"Fsd".
/// </summary>
public class PrettifierTests
{
    [Theory]
    [InlineData("CamZoomIn", "Cam Zoom In")]
    [InlineData("LandingGearToggle", "Landing Gear Toggle")]
    [InlineData("ToggleCargoScoop_Buggy", "Toggle Cargo Scoop Buggy")]
    [InlineData("SetSpeed75", "Set Speed 75")]
    [InlineData("UIFocus", "UI Focus")]
    [InlineData("ExplorationFSSCameraPitchIncreaseButton", "Exploration FSS Camera Pitch Increase Button")]
    [InlineData("SAAThirdPersonFovInButton", "SAA Third Person Fov In Button")]
    [InlineData("SRVTurretToggleExample", "SRV Turret Toggle Example")]
    [InlineData("FSDChargeButtonExample", "FSD Charge Button Example")]
    [InlineData("SCOEngageExample", "SCO Engage Example")]
    [InlineData("FreeCamToggleHUD", "Free Cam Toggle HUD")]
    [InlineData("DSSScanExample", "DSS Scan Example")]
    public void Prettify_ProducesExpectedReadableForm(string raw, string expected)
    {
        Assert.Equal(expected, Prettifier.Prettify(raw));
    }
}
