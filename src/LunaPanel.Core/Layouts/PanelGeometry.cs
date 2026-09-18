namespace LunaPanel.Core.Layouts;

/// <summary>
/// Maps a flat slot index to its (column, row) cell within a template's
/// geometry. Indexing is always row-major within the current orientation
/// (<c>col = index % Cols</c>, <c>row = index / Cols</c>) - rotating a panel
/// reflows which cell each index lands in while preserving reading order,
/// which is a deliberate product decision, not a bug. <see cref="TemplateGeometry.BandAfter"/>
/// is purely cosmetic spacing and must never factor into this mapping.
/// </summary>
public static class PanelGeometry
{
    public static (int Col, int Row) IndexToCell(int index, TemplateGeometry geometry)
    {
        var total = geometry.Cols * geometry.Rows;
        if (index < 0 || index >= total)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"Index must be within [0, {total}) for a {geometry.Cols}x{geometry.Rows} geometry, but was {index}.");
        }

        var col = index % geometry.Cols;
        var row = index / geometry.Cols;
        return (col, row);
    }
}
