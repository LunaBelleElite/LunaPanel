namespace LunaPanel.Core.Theme;

/// <summary>
/// The stock, undyed appearance every colour-matrix step (EDHM's own
/// <c>XML-Profile.ini</c>, and Elite's <c>GraphicsConfigurationOverride.xml</c>
/// / <c>GraphicsConfiguration.xml</c>) is defined relative to.
///
/// None of those three files carry an absolute colour by themselves - each
/// is a 3x3 matrix of weights against Elite's own native HUD colour, so an
/// identity matrix (see <c>Fixtures/graphics/guicolour-identity.xml</c>)
/// means "leave the native colour alone," not "there is no colour." This
/// constant supplies that native colour so the matrix steps have something
/// to multiply against. It is chosen as the widely-cited approximation of
/// Elite Dangerous' default HUD orange, <c>#FF8000</c> - deliberately
/// exact fractions of a byte (1.0, 128/255, 0.0) so that applying the
/// identity matrix reproduces <c>#FF8000</c> back exactly, with no rounding
/// drift, which is what makes "identity matrix = stock orange" a clean,
/// literal pin rather than an approximate one. See <c>ref/docs/theme.md</c>.
/// </summary>
public static class HudThemeDefaults
{
    public static readonly double[] StockOrangeNative = { 1.0, 128.0 / 255.0, 0.0 };

    public static readonly HudColor StockOrange = new(0xFF, 0x80, 0x00);

    /// <summary>
    /// Applies a <see cref="ColorMatrix"/> to <see cref="StockOrangeNative"/>,
    /// clamping each channel to [0,1] before scaling to a byte - a matrix
    /// row's weights can sum above 1 (see the EDHM step-2 fixture matrix),
    /// so this clamp is load-bearing, not defensive filler.
    /// </summary>
    public static HudColor ApplyMatrix(ColorMatrix matrix) => ApplyMatrix(matrix, StockOrangeNative);

    /// <summary>Overload taking an explicit native vector, for tests that need to prove the multiply itself is correct independent of which native colour is chosen.</summary>
    public static HudColor ApplyMatrix(ColorMatrix matrix, double[] native)
    {
        return new HudColor(Channel(matrix.Red, native), Channel(matrix.Green, native), Channel(matrix.Blue, native));

        static byte Channel(double[] row, double[] n)
        {
            var v = (row[0] * n[0]) + (row[1] * n[1]) + (row[2] * n[2]);
            v = Math.Clamp(v, 0.0, 1.0);
            return (byte)Math.Round(v * 255.0, MidpointRounding.AwayFromZero);
        }
    }
}
