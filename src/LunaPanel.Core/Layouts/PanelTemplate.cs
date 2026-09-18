namespace LunaPanel.Core.Layouts;

/// <summary>
/// One rung of the fixed template ladder (see <see cref="Templates"/>):
/// a permanent id, a slot count, and the grid geometry for each orientation.
/// <see cref="Id"/> is persisted in saved user layouts forever - it must
/// never be renamed once shipped.
///
/// The constructor guards that each orientation's <c>Cols * Rows</c> equals
/// <see cref="Slots"/>; a mismatch is a programming error in the ladder
/// table itself, not a runtime condition a caller should need to handle.
/// </summary>
public sealed class PanelTemplate
{
    public string Id { get; }
    public int Slots { get; }
    public TemplateGeometry Portrait { get; }
    public TemplateGeometry Landscape { get; }

    public PanelTemplate(string id, int slots, TemplateGeometry portrait, TemplateGeometry landscape)
    {
        if (portrait.Cols * portrait.Rows != slots)
        {
            throw new ArgumentException(
                $"Template '{id}': portrait geometry {portrait.Cols}x{portrait.Rows} does not equal {slots} slots.",
                nameof(portrait));
        }

        if (landscape.Cols * landscape.Rows != slots)
        {
            throw new ArgumentException(
                $"Template '{id}': landscape geometry {landscape.Cols}x{landscape.Rows} does not equal {slots} slots.",
                nameof(landscape));
        }

        Id = id;
        Slots = slots;
        Portrait = portrait;
        Landscape = landscape;
    }

    /// <summary>The geometry for the given orientation.</summary>
    public TemplateGeometry Geometry(PanelOrientation orientation) =>
        orientation == PanelOrientation.Portrait ? Portrait : Landscape;
}
