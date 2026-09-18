using LunaPanel.Core.Layouts;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Drives <see cref="PanelTemplateEndpoint.ChangeTemplate"/> directly - kept
/// free of any ASP.NET type, same discipline as <see cref="PanelEndpointTests"/>.
/// Proves this is a SWITCH between <see cref="LayoutSpiller"/> and
/// <see cref="LayoutParker"/> - both already independently pinned - not a
/// third implementation of the spilling/parking logic itself.
/// </summary>
public class PanelTemplateEndpointTests
{
    private static LayoutSlot Slot(int index, string action) => new(index, action, null, null, null);

    private static Layout DenseLayout(string templateId, int count) =>
        new(1, new[] { new LayoutPage("SHIP", templateId, Enumerable.Range(0, count).Select(i => Slot(i, $"Action{i}")).ToArray(), Array.Empty<LayoutSlot>()) });

    [Fact]
    public void ChangeTemplate_MergeExpandOn_Shrink_SpillsSurplusOntoFurtherPages()
    {
        var layout = DenseLayout("t30", 30);

        var result = PanelTemplateEndpoint.ChangeTemplate(layout, 0, "t12", mergeExpand: true);

        Assert.Equal(PanelTemplateEndpoint.ChangeOutcome.Ok, result.Outcome);
        Assert.Equal(3, result.Layout!.Pages.Count);
        Assert.Empty(result.Layout.Pages[0].Parked); // spill never leaves a Parked list
    }

    [Fact]
    public void ChangeTemplate_MergeExpandOff_Shrink_ParksSurplusOnTheSamePage_NoNewPage()
    {
        var layout = DenseLayout("t30", 30);

        var result = PanelTemplateEndpoint.ChangeTemplate(layout, 0, "t12", mergeExpand: false);

        Assert.Equal(PanelTemplateEndpoint.ChangeOutcome.Ok, result.Outcome);
        Assert.Single(result.Layout!.Pages); // no page created - surplus parks in place
        Assert.Equal(12, result.Layout.Pages[0].Slots.Count);
        Assert.Equal(18, result.Layout.Pages[0].Parked.Count);
    }

    [Fact]
    public void ChangeTemplate_UnknownTemplateId_ReturnsUnknownTemplateOutcome_AndNoLayout()
    {
        var layout = DenseLayout("t30", 30);

        var result = PanelTemplateEndpoint.ChangeTemplate(layout, 0, "not-a-real-template", mergeExpand: true);

        Assert.Equal(PanelTemplateEndpoint.ChangeOutcome.UnknownTemplate, result.Outcome);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void ChangeTemplate_PageIndexOutOfRange_ReturnsNoSuchPageOutcome_AndNoLayout()
    {
        var layout = DenseLayout("t30", 30);

        var result = PanelTemplateEndpoint.ChangeTemplate(layout, 1, "t12", mergeExpand: true);

        Assert.Equal(PanelTemplateEndpoint.ChangeOutcome.NoSuchPage, result.Outcome);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void ChangeTemplate_MergeExpandOff_DoesNotTouchOtherPages()
    {
        var pages = new List<LayoutPage>
        {
            new("SHIP", "t30", Enumerable.Range(0, 30).Select(i => Slot(i, $"Action{i}")).ToArray(), Array.Empty<LayoutSlot>()),
            new("SRV", "t9", new[] { Slot(0, "SrvAction") }, Array.Empty<LayoutSlot>()),
        };
        var layout = new Layout(1, pages);

        var result = PanelTemplateEndpoint.ChangeTemplate(layout, 0, "t12", mergeExpand: false);

        Assert.Equal(2, result.Layout!.Pages.Count);
        Assert.Equal("SRV", result.Layout.Pages[1].Name);
        Assert.Equal("t9", result.Layout.Pages[1].TemplateId);
    }
}
