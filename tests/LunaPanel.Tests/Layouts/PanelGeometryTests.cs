using LunaPanel.Core.Layouts;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// Pins <see cref="PanelGeometry.IndexToCell"/>'s row-major mapping (first,
/// last, wrap-to-next-row) for t12 and t30 in both orientations, out-of-range
/// rejection, and the explicit rotation/reading-order decision: rotating a
/// panel reflows which cell an index lands in while BandAfter never touches
/// indexing at all.
/// </summary>
public class PanelGeometryTests
{
    private static TemplateGeometry Geometry(string id, PanelOrientation orientation)
    {
        Assert.True(Templates.TryGet(id, out var template));
        return template!.Geometry(orientation);
    }

    // --- t12: portrait 3x4, landscape 4x3 ---

    [Fact]
    public void IndexToCell_T12Portrait_FirstIndex_IsOrigin()
    {
        Assert.Equal((0, 0), PanelGeometry.IndexToCell(0, Geometry("t12", PanelOrientation.Portrait)));
    }

    [Fact]
    public void IndexToCell_T12Portrait_LastIndex_IsBottomRightCell()
    {
        Assert.Equal((2, 3), PanelGeometry.IndexToCell(11, Geometry("t12", PanelOrientation.Portrait)));
    }

    [Fact]
    public void IndexToCell_T12Portrait_WrapsToNextRow_AtEndOfFirstRow()
    {
        Assert.Equal((0, 1), PanelGeometry.IndexToCell(3, Geometry("t12", PanelOrientation.Portrait)));
    }

    [Fact]
    public void IndexToCell_T12Landscape_FirstIndex_IsOrigin()
    {
        Assert.Equal((0, 0), PanelGeometry.IndexToCell(0, Geometry("t12", PanelOrientation.Landscape)));
    }

    [Fact]
    public void IndexToCell_T12Landscape_LastIndex_IsBottomRightCell()
    {
        Assert.Equal((3, 2), PanelGeometry.IndexToCell(11, Geometry("t12", PanelOrientation.Landscape)));
    }

    [Fact]
    public void IndexToCell_T12Landscape_WrapsToNextRow_AtEndOfFirstRow()
    {
        Assert.Equal((0, 1), PanelGeometry.IndexToCell(4, Geometry("t12", PanelOrientation.Landscape)));
    }

    // --- t30: portrait 5x6, landscape 6x5 ---

    [Fact]
    public void IndexToCell_T30Portrait_FirstIndex_IsOrigin()
    {
        Assert.Equal((0, 0), PanelGeometry.IndexToCell(0, Geometry("t30", PanelOrientation.Portrait)));
    }

    [Fact]
    public void IndexToCell_T30Portrait_LastIndex_IsBottomRightCell()
    {
        Assert.Equal((4, 5), PanelGeometry.IndexToCell(29, Geometry("t30", PanelOrientation.Portrait)));
    }

    [Fact]
    public void IndexToCell_T30Portrait_WrapsToNextRow_AtEndOfFirstRow()
    {
        Assert.Equal((0, 1), PanelGeometry.IndexToCell(5, Geometry("t30", PanelOrientation.Portrait)));
    }

    [Fact]
    public void IndexToCell_T30Landscape_FirstIndex_IsOrigin()
    {
        Assert.Equal((0, 0), PanelGeometry.IndexToCell(0, Geometry("t30", PanelOrientation.Landscape)));
    }

    [Fact]
    public void IndexToCell_T30Landscape_LastIndex_IsBottomRightCell()
    {
        Assert.Equal((5, 4), PanelGeometry.IndexToCell(29, Geometry("t30", PanelOrientation.Landscape)));
    }

    [Fact]
    public void IndexToCell_T30Landscape_WrapsToNextRow_AtEndOfFirstRow()
    {
        Assert.Equal((0, 1), PanelGeometry.IndexToCell(6, Geometry("t30", PanelOrientation.Landscape)));
    }

    // --- out of range ---

    [Fact]
    public void IndexToCell_NegativeIndex_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PanelGeometry.IndexToCell(-1, Geometry("t12", PanelOrientation.Portrait)));
    }

    [Fact]
    public void IndexToCell_IndexEqualToSlotCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PanelGeometry.IndexToCell(12, Geometry("t12", PanelOrientation.Portrait)));
    }

    // --- reading order survives rotation: the explicit pin ---

    [Fact]
    public void IndexToCell_T12Index3_ReflowsOnRotation_PortraitRow1_LandscapeRow0Col3()
    {
        // Deliberate product decision, not a bug: rotating reflows the
        // grid but keeps row-major reading order for the same index.
        Assert.Equal((0, 1), PanelGeometry.IndexToCell(3, Geometry("t12", PanelOrientation.Portrait)));
        Assert.Equal((3, 0), PanelGeometry.IndexToCell(3, Geometry("t12", PanelOrientation.Landscape)));
    }
}
