using LunaPanel.Core.Layouts;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// Pins the fixed template ladder: exact ids and slot counts (permanent -
/// saved user layouts reference a template by id forever, so a renamed or
/// removed rung orphans every layout in the wild), the cols*rows == slots
/// invariant for every template in both orientations, and TryGet's
/// no-throw-on-unknown-id contract.
/// </summary>
public class TemplatesTests
{
    [Fact]
    public void Templates_All_HasExactlyNineRungs()
    {
        Assert.Equal(9, Templates.All.Count);
    }

    [Theory]
    [InlineData("t6", 6)]
    [InlineData("t9", 9)]
    [InlineData("t12", 12)]
    [InlineData("t18", 18)]
    [InlineData("t24", 24)]
    [InlineData("t30", 30)]
    [InlineData("t36", 36)]
    [InlineData("t48", 48)]
    [InlineData("t64", 64)]
    public void Templates_EachRung_HasExactIdAndSlotCount(string expectedId, int expectedSlots)
    {
        Assert.True(Templates.TryGet(expectedId, out var template), $"Expected a rung with id '{expectedId}'.");
        Assert.NotNull(template);
        Assert.Equal(expectedId, template!.Id);
        Assert.Equal(expectedSlots, template.Slots);
    }

    [Fact]
    public void Templates_EveryRung_BothOrientations_ColsTimesRowsEqualsSlots()
    {
        // Sweep, not spot checks: every template, both orientations.
        foreach (var template in Templates.All)
        {
            Assert.True(
                template.Portrait.Cols * template.Portrait.Rows == template.Slots,
                $"{template.Id} portrait {template.Portrait.Cols}x{template.Portrait.Rows} != {template.Slots} slots");
            Assert.True(
                template.Landscape.Cols * template.Landscape.Rows == template.Slots,
                $"{template.Id} landscape {template.Landscape.Cols}x{template.Landscape.Rows} != {template.Slots} slots");
        }
    }

    [Fact]
    public void Templates_TryGet_UnknownId_ReturnsFalse_AndDoesNotThrow()
    {
        var result = Templates.TryGet("t999", out var template);

        Assert.False(result);
        Assert.Null(template);
    }

    [Fact]
    public void PanelTemplate_Constructor_PortraitGeometryMismatchedToSlots_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new PanelTemplate("bad", 12, new TemplateGeometry(3, 3, 0), new TemplateGeometry(4, 3, 0)));
    }

    [Fact]
    public void PanelTemplate_Constructor_LandscapeGeometryMismatchedToSlots_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new PanelTemplate("bad", 12, new TemplateGeometry(3, 4, 0), new TemplateGeometry(4, 4, 0)));
    }

    [Theory]
    [InlineData("t36", 6, 6, 3)]
    [InlineData("t48", 6, 8, 4)]
    [InlineData("t64", 8, 8, 4)]
    public void Templates_NewRungs_PortraitAndLandscapeAreTransposes(
        string id, int portraitCols, int portraitRows, int bandAfter)
    {
        Assert.True(Templates.TryGet(id, out var template));

        Assert.Equal(new TemplateGeometry(portraitCols, portraitRows, bandAfter), template!.Portrait);
        Assert.Equal(new TemplateGeometry(portraitRows, portraitCols, bandAfter), template.Landscape);
    }

    [Fact]
    public void PanelTemplate_Geometry_ReturnsPortraitOrLandscape_BasedOnOrientation()
    {
        Assert.True(Templates.TryGet("t12", out var t12));

        Assert.Equal(t12!.Portrait, t12.Geometry(PanelOrientation.Portrait));
        Assert.Equal(t12.Landscape, t12.Geometry(PanelOrientation.Landscape));
    }
}
