namespace LunaPanel.Core.Layouts;

/// <summary>
/// The edge/chrome space <see cref="CellSizeEstimator"/> reserves for a given
/// viewport before dividing what's left into cells - surfaced by value so a
/// caller (<c>PanelEndpoint.PanelResponse</c>) can hand these numbers to a
/// client rather than the client restating them.
///
/// This is what closes the duplication <c>tests/notes/live-checks.md</c>
/// and <c>ref/docs/panels-and-pages.md</c> both name as already having caused
/// two shipped defects (LC9: chrome too tall, bottom row pushed off screen;
/// and its opposite, buttons flush to the screen edge) - a client-side
/// <c>headerStripFor()</c>/<c>framePadFor()</c> re-implementation of these same
/// numbers can silently drift from the real ones this record was built from.
/// See <see cref="CellSizeEstimator.Allowances"/>.
/// </summary>
/// <param name="HeaderStrip">The top chrome row's height (title, gear, fullscreen controls) - phone/tablet, already spent before any cell is sized.</param>
/// <param name="FramePadding">The padding reserved on every edge of the grid itself, before gutters between cells.</param>
/// <param name="TabStripHeight">The page-tab row's height - a second, independent allowance from <see cref="HeaderStrip"/>, added for the tab row (<c>ref/docs/panels-and-pages.md</c>'s "Tabs"). Always reserved, whether or not a device's layout currently holds more than one page: subtracting it only when there happen to be 2+ pages would let creating a page via spill silently change every cell's size underneath the commander, which is the same allowance-mismatch failure this record exists to prevent, one level up.</param>
public readonly record struct CellAllowances(double HeaderStrip, double FramePadding, double TabStripHeight);
