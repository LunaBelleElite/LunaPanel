namespace LunaPanel.Core.Layouts;

/// <summary>
/// The grid shape a <see cref="PanelTemplate"/> uses in one orientation:
/// column and row counts, plus an optional wider gap ("band") after a given
/// row (portrait) or column (landscape). <see cref="BandAfter"/> is purely
/// cosmetic spacing - it never changes how slot indices map to cells; see
/// <see cref="PanelGeometry.IndexToCell"/>. A value of 0 means no band.
/// </summary>
public readonly record struct TemplateGeometry(int Cols, int Rows, int BandAfter);
