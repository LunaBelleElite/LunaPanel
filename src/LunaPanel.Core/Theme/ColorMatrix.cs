namespace LunaPanel.Core.Theme;

/// <summary>
/// Elite's/EDHM's shared 3x3 HUD recolour matrix shape: one row per output
/// channel, each row a weight against the game's native (R,G,B) HUD colour.
/// An identity matrix (Red=[1,0,0], Green=[0,1,0], Blue=[0,0,1]) reproduces
/// the native colour unchanged - see
/// <c>Fixtures/graphics/guicolour-identity.xml</c>, which is exactly this
/// shape and is what stock Elite ships.
/// </summary>
/// <param name="Red">Weights applied to native (R,G,B) to produce the output red channel. Always length 3.</param>
/// <param name="Green">Weights applied to native (R,G,B) to produce the output green channel. Always length 3.</param>
/// <param name="Blue">Weights applied to native (R,G,B) to produce the output blue channel. Always length 3.</param>
public readonly record struct ColorMatrix(double[] Red, double[] Green, double[] Blue);
