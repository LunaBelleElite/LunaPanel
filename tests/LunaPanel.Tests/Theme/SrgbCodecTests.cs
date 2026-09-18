using LunaPanel.Core.Theme;

namespace LunaPanel.Tests.Theme;

/// <summary>
/// Pins <see cref="SrgbCodec"/>'s two decode primitives directly, against
/// literal expected values - independent of, but consistent with, the
/// local copy of the same formula pinned in
/// <c>FixtureIntegrityTests.SrgbEncode_ReferencePair_MatchesRealEdhmMeasurement</c>.
/// </summary>
public class SrgbCodecTests
{
    // ---------------------------------------------------------------
    // DecodeLinearToByte
    // ---------------------------------------------------------------

    [Fact]
    public void DecodeLinearToByte_ReferencePair_MatchesRealEdhmMeasurement()
    {
        // Same two points measured against a real EDHM-UI install that
        // FixtureIntegrityTests keeps its own local copy of the formula
        // pinned against - see that test's own remarks for provenance.
        Assert.Equal((byte)0x95, SrgbCodec.DecodeLinearToByte(0.3005));
        Assert.Equal((byte)0xFB, SrgbCodec.DecodeLinearToByte(0.9647));
    }

    [Fact]
    public void DecodeLinearToByte_BelowLinearThreshold_UsesLinearBranch()
    {
        // 0.002 <= 0.0031308, so this exercises 12.92*v rather than the
        // pow() branch. Expected byte computed independently (12.92*0.002 =
        // 0.02584, *255 = 6.589 -> round 7 = 0x07) - matches the same value
        // already pinned in FixtureIntegrityTests for Advanced.sample.ini's x78.
        Assert.Equal((byte)0x07, SrgbCodec.DecodeLinearToByte(0.002));
    }

    [Fact]
    public void DecodeLinearToByte_ZeroAndOne_MapToBlackAndFullWhite()
    {
        Assert.Equal((byte)0x00, SrgbCodec.DecodeLinearToByte(0.0));
        Assert.Equal((byte)0xFF, SrgbCodec.DecodeLinearToByte(1.0));
    }

    [Fact]
    public void DecodeLinearToByte_OutOfRangeInput_IsClampedRatherThanThrowing()
    {
        // Negative and >1 linear values shouldn't occur in a well-formed
        // EDHM file, but a resolver that must never throw needs this
        // clamped rather than producing garbage or an OverflowException.
        Assert.Equal((byte)0x00, SrgbCodec.DecodeLinearToByte(-5.0));
        Assert.Equal((byte)0xFF, SrgbCodec.DecodeLinearToByte(5.0));
    }

    // ---------------------------------------------------------------
    // DecodeArgbInt
    // ---------------------------------------------------------------

    [Fact]
    public void DecodeArgbInt_MinusOne_IsFullWhiteFullAlpha()
    {
        var (a, r, g, b) = SrgbCodec.DecodeArgbInt(-1);
        Assert.Equal((byte)0xFF, a);
        Assert.Equal((byte)0xFF, r);
        Assert.Equal((byte)0xFF, g);
        Assert.Equal((byte)0xFF, b);
    }

    [Fact]
    public void DecodeArgbInt_FixtureThemeSettingsValue_DecodesToIndependentlyComputedBytes()
    {
        // Fixtures/edhm/ThemeSettings.sample.json's "Chat Panel Text Color"
        // element stores Value: -10481012. Decoded here to A=FF,R=60,G=12,B=8C
        // (computed independently via `v = value & 0xFFFFFFFF` then standard
        // ARGB byte extraction) - NOT the same as the live INI-derived colour
        // (0xCC,0x5F,0xF1, from x77/y77/z77 - see HudThemeResolverTests),
        // which is deliberate: this is the exact "stored Value can be stale,
        // read the INI instead" case the fixture exists to prove.
        var (a, r, g, b) = SrgbCodec.DecodeArgbInt(-10481012);
        Assert.Equal((byte)0xFF, a);
        Assert.Equal((byte)0x60, r);
        Assert.Equal((byte)0x12, g);
        Assert.Equal((byte)0x8C, b);
    }
}
