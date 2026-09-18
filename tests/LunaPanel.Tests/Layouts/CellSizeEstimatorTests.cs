using LunaPanel.Core.Layouts;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// Pins <see cref="CellSizeEstimator.Estimate"/> against numbers computed by
/// hand from the spec's own constants (gutter/band/frame padding/header
/// strip per device class, 44/56/72px comfort thresholds), never by calling
/// the implementation back on itself. Device class: shortest viewport side
/// under 600px is phone, 600px or more is tablet.
/// </summary>
public class CellSizeEstimatorTests
{
    private static PanelTemplate Template(string id)
    {
        Assert.True(Templates.TryGet(id, out var template));
        return template!;
    }

    [Fact]
    public void Estimate_360x640PhonePortraitT30_ClearsTheHardAccessibilityFloorOnBothAxes()
    {
        // Worst realistic case in the ladder: smallest common phone width,
        // densest template, portrait (band gap eats into the row axis).
        // By hand: availableWidth = 360 - 2*12 = 336; availableHeight =
        // 640 - 2*12 - 40(header) - 32(tab strip) = 544. Portrait 5x6, bandAfter=3.
        // horizontalGutterSpace = 4*8 = 32 -> cellWidth = (336-32)/5 = 60.8
        // verticalGutterSpace = 5*8 + 16(band) = 56 -> cellHeight = (544-56)/6 = 81.3333
        //
        // SUPERSEDES this test's own previous cellHeight expectation of
        // 520.0/6.0 (86.6667) - 2026-09-07, adding the page-tab row's own
        // TabStripHeight allowance (ref/docs/panels-and-pages.md's "Tabs")
        // shrinks available height by a further 32px on phone. Not a
        // weakening: the tab row genuinely costs this space now, by design,
        // the same way HeaderStrip always has.
        var result = CellSizeEstimator.Estimate(360, 640, PanelOrientation.Portrait, Template("t30"));

        Assert.Equal(60.8, result.CellWidth, precision: 6);
        Assert.Equal(488.0 / 6.0, result.CellHeight, precision: 6);
        Assert.True(result.CellWidth >= 44, $"cellWidth {result.CellWidth} must be >= 44");
        Assert.True(result.CellHeight >= 44, $"cellHeight {result.CellHeight} must be >= 44");
    }

    [Fact]
    public void Estimate_1280x800TabletLandscapeT6_IsComfortable_WithLargeCells()
    {
        // By hand: availableWidth = 1280 - 2*16 = 1248; availableHeight =
        // 800 - 2*16 - 48(header) - 36(tab strip) = 684. Landscape 3x2, bandAfter=0 (none).
        // horizontalGutterSpace = 2*12 = 24 -> cellWidth = (1248-24)/3 = 408
        // verticalGutterSpace = 1*12 = 12 -> cellHeight = (684-12)/2 = 336
        //
        // SUPERSEDES this test's own previous cellHeight expectation of 354 -
        // 2026-09-07, same TabStripHeight addition as above.
        var result = CellSizeEstimator.Estimate(1280, 800, PanelOrientation.Landscape, Template("t6"));

        Assert.Equal(408.0, result.CellWidth, precision: 6);
        Assert.Equal(336.0, result.CellHeight, precision: 6);
        Assert.Equal(ComfortVerdict.Comfortable, result.Verdict);
    }

    // -----------------------------------------------------------------
    // CellSizeEstimator.Allowances - the server-side source of truth
    // PanelClientEndpoint now consumes instead of restating headerStrip/
    // framePadding as JavaScript literals (ref/docs/panels-and-pages.md's
    // "Tabs": "the client should ask the server for its allowances rather
    // than restating them in JavaScript").
    // -----------------------------------------------------------------

    [Fact]
    public void Allowances_Phone_MatchesEstimatesOwnPhoneConstants()
    {
        var allowances = CellSizeEstimator.Allowances(360, 640);

        Assert.Equal(40, allowances.HeaderStrip);
        Assert.Equal(12, allowances.FramePadding);
        Assert.Equal(32, allowances.TabStripHeight);
    }

    [Fact]
    public void Allowances_Tablet_MatchesEstimatesOwnTabletConstants()
    {
        var allowances = CellSizeEstimator.Allowances(1280, 800);

        Assert.Equal(48, allowances.HeaderStrip);
        Assert.Equal(16, allowances.FramePadding);
        Assert.Equal(36, allowances.TabStripHeight);
    }

    /// <summary>
    /// The same figures <see cref="Allowances"/> reports are exactly what
    /// <see cref="Estimate"/> itself subtracted - proven by construction
    /// (both read the same private consts via <see cref="IsPhone"/>), and
    /// pinned here behaviourally: reconstructing availableHeight from
    /// Allowances' own numbers must reproduce Estimate's real cellHeight for
    /// a template with no BandAfter widening on either axis (t9, both axes
    /// 0), so nothing about gutter/band handling is hidden in the
    /// difference.
    /// </summary>
    [Fact]
    public void Allowances_ReportedFigures_AreExactlyWhatEstimateSubtracted_ForANoBandTemplate()
    {
        var allowances = CellSizeEstimator.Allowances(1280, 800);
        var result = CellSizeEstimator.Estimate(1280, 800, PanelOrientation.Landscape, Template("t9"));

        var availableHeight = 800 - (2 * allowances.FramePadding) - allowances.HeaderStrip - allowances.TabStripHeight;
        var verticalGutterSpace = (3 - 1) * 12; // t9 landscape is 3x3, tablet gutter 12
        var expectedCellHeight = (availableHeight - verticalGutterSpace) / 3;

        Assert.Equal(expectedCellHeight, result.CellHeight, precision: 6);
    }

    [Fact]
    public void Estimate_320x640PhonePortraitT30_IsCompact()
    {
        // By hand: availableWidth = 320 - 24 = 296; availableHeight = 576
        // (as above). horizontalGutterSpace = 32 -> cellWidth = (296-32)/5 = 52.8
        // (phone Compact band is [44, 56)).
        var result = CellSizeEstimator.Estimate(320, 640, PanelOrientation.Portrait, Template("t30"));

        Assert.Equal(52.8, result.CellWidth, precision: 6);
        Assert.Equal(ComfortVerdict.Compact, result.Verdict);
    }

    [Fact]
    public void Estimate_250x640PhonePortraitT30_IsTooSmall()
    {
        // By hand: availableWidth = 250 - 24 = 226. horizontalGutterSpace =
        // 32 -> cellWidth = (226-32)/5 = 38.8, below the 44px hard floor.
        var result = CellSizeEstimator.Estimate(250, 640, PanelOrientation.Portrait, Template("t30"));

        Assert.Equal(38.8, result.CellWidth, precision: 6);
        Assert.Equal(ComfortVerdict.TooSmall, result.Verdict);
    }

    [Fact]
    public void Estimate_ShortestSideExactly600_UsesTabletConstants()
    {
        // By hand, tablet constants: availableWidth = 600 - 2*16 = 568;
        // availableHeight = 600 - 2*16 - 48 = 520. Portrait 2x3 (t6),
        // bandAfter=0. horizontalGutterSpace = 1*12 = 12 -> cellWidth =
        // (568-12)/2 = 278.
        var result = CellSizeEstimator.Estimate(600, 600, PanelOrientation.Portrait, Template("t6"));

        Assert.Equal(278.0, result.CellWidth, precision: 6);
    }

    [Fact]
    public void Estimate_ShortestSideJustUnder600_UsesPhoneConstants()
    {
        // By hand, phone constants: availableWidth = 599 - 2*12 = 575.
        // Portrait 2x3 (t6), bandAfter=0. horizontalGutterSpace = 1*8 = 8
        // -> cellWidth = (575-8)/2 = 283.5. Deliberately different from the
        // tablet-constants case above at 600, proving the boundary is
        // exactly at 600 (under 600 = phone).
        var result = CellSizeEstimator.Estimate(599, 599, PanelOrientation.Portrait, Template("t6"));

        Assert.Equal(283.5, result.CellWidth, precision: 6);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-10, 100)]
    public void Estimate_NonPositiveViewportDimension_Throws(double width, double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CellSizeEstimator.Estimate(width, height, PanelOrientation.Portrait, Template("t6")));
    }

    // -----------------------------------------------------------------
    // The ladder's new top three rungs (t36/t48/t64, appended 2026-09-12).
    // Not hand-derived pins like the t30 cases above - CellSizeEstimator is
    // generic over any rung's Cols/Rows/BandAfter (it reads them off the
    // template, nothing here is hardcoded to the six original ids), so these
    // just prove that genericity holds for the new, denser geometries: no
    // division by zero, no negative or NaN cell sizes, at a small phone and a
    // large tablet viewport.
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("t36", 360, 640, PanelOrientation.Portrait)]
    [InlineData("t36", 1280, 800, PanelOrientation.Landscape)]
    [InlineData("t48", 360, 640, PanelOrientation.Portrait)]
    [InlineData("t48", 1280, 800, PanelOrientation.Landscape)]
    [InlineData("t64", 360, 640, PanelOrientation.Portrait)]
    [InlineData("t64", 1280, 800, PanelOrientation.Landscape)]
    public void Estimate_NewRungs_ProduceSaneCellSizes(string id, double width, double height, PanelOrientation orientation)
    {
        var result = CellSizeEstimator.Estimate(width, height, orientation, Template(id));

        Assert.True(double.IsFinite(result.CellWidth), $"cellWidth {result.CellWidth} must be finite");
        Assert.True(double.IsFinite(result.CellHeight), $"cellHeight {result.CellHeight} must be finite");
        Assert.True(result.CellWidth > 0, $"cellWidth {result.CellWidth} must be positive");
        Assert.True(result.CellHeight > 0, $"cellHeight {result.CellHeight} must be positive");
    }

    [Fact]
    public void Estimate_360x640PhonePortraitT64_IsDenserThanT30_AndStillPositive()
    {
        // t64 is the densest rung in the ladder now. On the smallest common
        // phone width it is expected to be TooSmall or Compact, not comfortable
        // - this test only pins that the arithmetic still produces sane,
        // positive numbers rather than a specific verdict, since no hand
        // derivation for this rung exists yet the way t30's does above.
        var t30 = CellSizeEstimator.Estimate(360, 640, PanelOrientation.Portrait, Template("t30"));
        var t64 = CellSizeEstimator.Estimate(360, 640, PanelOrientation.Portrait, Template("t64"));

        Assert.True(t64.CellWidth > 0 && t64.CellHeight > 0);
        Assert.True(t64.CellWidth < t30.CellWidth, "t64 (8 cols) should yield narrower cells than t30 (5 cols) on the same viewport");
    }
}
