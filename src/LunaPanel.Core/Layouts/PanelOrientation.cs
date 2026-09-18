namespace LunaPanel.Core.Layouts;

/// <summary>
/// Which way round the device is held. A <see cref="PanelTemplate"/> carries
/// separate geometry for each - rotating reflows the buttons (row-major
/// reading order is preserved, the grid shape changes), it does not just
/// stretch the same grid.
/// </summary>
public enum PanelOrientation
{
    Portrait,
    Landscape
}
