namespace LunaPanel.Core.Layouts;

/// <summary>
/// Pure function telling the template picker, honestly, whether a rung will
/// be usable on the device in the user's hand: given a viewport size,
/// orientation and template, computes the resulting per-cell size and a
/// comfort verdict. No rendering, no UI - just the arithmetic both the
/// server and the editor will consume.
///
/// Device class is decided by the shortest viewport side: under 600px is a
/// phone, 600px or more is a tablet. Each class has its own gutter, band
/// extra gap, frame padding and header strip constants (see the fields
/// below), and its own comfort thresholds. The 44px "too small" floor is a
/// hard accessibility floor shared by both classes and must never be
/// softened.
/// </summary>
public static class CellSizeEstimator
{
    /// <summary>
    /// The shortest-viewport-side threshold that separates a phone from a
    /// tablet, in CSS pixels: under this is a phone.
    ///
    /// Public because the web client needs the same number to report its own
    /// class at pairing (<c>ref/docs/layout-import.md</c>), and this project
    /// has already paid twice for restating one of this type's constants as a
    /// hand-copied JavaScript literal (<c>tests/notes/live-checks.md</c> LC9,
    /// and its opposite). <c>PanelClientEndpoint</c> substitutes this value
    /// into the page rather than spelling 600 a second time, so the two
    /// cannot drift.
    /// </summary>
    public const double PhoneTabletBoundary = 600;

    private const double PhoneGutter = 8;
    private const double PhoneBandExtraGap = 16;
    private const double PhoneFramePadding = 12;
    private const double PhoneHeaderStrip = 40;
    private const double PhoneComfortableFloor = 56;

    private const double TabletGutter = 12;
    private const double TabletBandExtraGap = 24;
    private const double TabletFramePadding = 16;
    private const double TabletHeaderStrip = 48;
    private const double TabletComfortableFloor = 72;

    // The page-tab row (ref/docs/panels-and-pages.md's "Tabs") - a second,
    // independent allowance from HeaderStrip, always subtracted regardless
    // of how many pages a device's layout currently holds (see
    // CellAllowances.TabStripHeight's own remarks: making this conditional
    // on page count would let a spill-triggered page creation silently
    // resize every cell underneath the commander). Not yet measured against
    // real hardware the way HeaderStrip/FramePadding were (device-
    // calibration.md, LC9/LC10) - a reasoned starting value, smaller than
    // HeaderStrip since a row of plain text tab buttons needs less vertical
    // padding than one carrying icon controls. Revisit against a real
    // device once the tab row actually ships to one.
    private const double PhoneTabStrip = 32;
    private const double TabletTabStrip = 36;

    private const double CompactFloor = 44;

    /// <summary>
    /// The same phone/tablet split <see cref="Estimate"/> uses internally,
    /// exposed once so it can back both <see cref="Estimate"/> and
    /// <see cref="Allowances"/> without the two ever computing it
    /// differently.
    /// </summary>
    private static bool IsPhone(double viewportWidth, double viewportHeight) =>
        Math.Min(viewportWidth, viewportHeight) < PhoneTabletBoundary;

    /// <summary>
    /// The edge/chrome allowances <see cref="Estimate"/> reserves for this
    /// viewport's device class, surfaced by value rather than left for a
    /// caller to restate as a second, hand-copied set of literals - see
    /// <see cref="CellAllowances"/>'s own remarks.
    /// </summary>
    public static CellAllowances Allowances(double viewportWidth, double viewportHeight)
    {
        var isPhone = IsPhone(viewportWidth, viewportHeight);
        return new CellAllowances(
            HeaderStrip: isPhone ? PhoneHeaderStrip : TabletHeaderStrip,
            FramePadding: isPhone ? PhoneFramePadding : TabletFramePadding,
            TabStripHeight: isPhone ? PhoneTabStrip : TabletTabStrip);
    }

    public static CellSizeEstimate Estimate(
        double viewportWidth,
        double viewportHeight,
        PanelOrientation orientation,
        PanelTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (viewportWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportWidth), viewportWidth, "Viewport width must be positive.");
        }

        if (viewportHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportHeight), viewportHeight, "Viewport height must be positive.");
        }

        var isPhone = IsPhone(viewportWidth, viewportHeight);

        var gutter = isPhone ? PhoneGutter : TabletGutter;
        var bandExtraGap = isPhone ? PhoneBandExtraGap : TabletBandExtraGap;
        var framePadding = isPhone ? PhoneFramePadding : TabletFramePadding;
        var headerStrip = isPhone ? PhoneHeaderStrip : TabletHeaderStrip;
        var tabStrip = isPhone ? PhoneTabStrip : TabletTabStrip;
        var comfortableFloor = isPhone ? PhoneComfortableFloor : TabletComfortableFloor;

        var geometry = template.Geometry(orientation);

        var availableWidth = viewportWidth - (2 * framePadding);
        var availableHeight = viewportHeight - (2 * framePadding) - headerStrip - tabStrip;

        var horizontalGutterSpace = (geometry.Cols - 1) * gutter;
        var verticalGutterSpace = (geometry.Rows - 1) * gutter;

        // BandAfter widens the gap along the axis that grows as the panel
        // reflows: rows in portrait, columns in landscape. It never touches
        // slot indexing (see PanelGeometry.IndexToCell) - this is spacing
        // only.
        if (geometry.BandAfter > 0)
        {
            if (orientation == PanelOrientation.Portrait)
            {
                verticalGutterSpace += bandExtraGap;
            }
            else
            {
                horizontalGutterSpace += bandExtraGap;
            }
        }

        var cellWidth = (availableWidth - horizontalGutterSpace) / geometry.Cols;
        var cellHeight = (availableHeight - verticalGutterSpace) / geometry.Rows;

        var smaller = Math.Min(cellWidth, cellHeight);
        var verdict = smaller >= comfortableFloor
            ? ComfortVerdict.Comfortable
            : smaller >= CompactFloor
                ? ComfortVerdict.Compact
                : ComfortVerdict.TooSmall;

        return new CellSizeEstimate(cellWidth, cellHeight, verdict);
    }
}
