using LunaPanel.Core.Layouts;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Drives <see cref="PageEditEndpoint"/>'s three pure verbs directly - the
/// add/rename/set-visibility logic the page bar needs
/// (<c>ref/docs/panels-and-pages.md</c>) - kept free of any ASP.NET type,
/// same discipline as <see cref="SlotEditEndpointTests"/>/<see cref="PanelTemplateEndpointTests"/>.
/// Persisting the result (<c>LayoutStore.Save</c>) is the caller's job, not
/// this type's.
/// </summary>
public class PageEditEndpointTests
{
    private static LayoutSlot Slot(int index, string action) => new(index, action, null, null, null);

    private static Layout OnePageLayout(string name = "SHIP", string templateId = "t6", IReadOnlyList<string>? showWhen = null) =>
        new(1, new[] { new LayoutPage(name, templateId, new[] { Slot(0, "LandingGearToggle") }, Array.Empty<LayoutSlot>(), showWhen) });

    // -------------------------------------------------------------------
    // AddPage
    // -------------------------------------------------------------------

    [Fact]
    public void AddPage_AppendsANewEmptyContextFreePage_AtTheEnd()
    {
        var layout = OnePageLayout();

        var result = PageEditEndpoint.AddPage(layout, "SRV", "t18");

        Assert.Equal(PageEditEndpoint.EditOutcome.Ok, result.Outcome);
        Assert.Equal(2, result.Layout!.Pages.Count);
        var added = result.Layout.Pages[1];
        Assert.Equal("SRV", added.Name);
        Assert.Equal("t18", added.TemplateId);
        Assert.Empty(added.Slots);
        Assert.Empty(added.Parked);
        Assert.Null(added.ShowWhen);
    }

    [Fact]
    public void AddPage_DoesNotTouchAnyExistingPage()
    {
        var layout = OnePageLayout();

        var result = PageEditEndpoint.AddPage(layout, "SRV", "t18");

        Assert.Equal("SHIP", result.Layout!.Pages[0].Name);
        Assert.Same(layout.Pages[0].Slots, result.Layout.Pages[0].Slots);
    }

    [Fact]
    public void AddPage_UnknownTemplateId_ReturnsUnknownTemplate_AndNoLayout()
    {
        var layout = OnePageLayout();

        var result = PageEditEndpoint.AddPage(layout, "SRV", "not-a-real-template");

        Assert.Equal(PageEditEndpoint.EditOutcome.UnknownTemplate, result.Outcome);
        Assert.Null(result.Layout);
    }

    // -------------------------------------------------------------------
    // RenamePage
    // -------------------------------------------------------------------

    [Fact]
    public void RenamePage_ChangesOnlyTheName()
    {
        var layout = OnePageLayout(showWhen: new[] { "InMainShip" });

        var result = PageEditEndpoint.RenamePage(layout, 0, "SHIP2");

        Assert.Equal(PageEditEndpoint.EditOutcome.Ok, result.Outcome);
        var page = result.Layout!.Pages[0];
        Assert.Equal("SHIP2", page.Name);
        Assert.Equal("t6", page.TemplateId);
        Assert.Same(layout.Pages[0].Slots, page.Slots);
        Assert.Equal(new[] { "InMainShip" }, page.ShowWhen);
    }

    [Fact]
    public void RenamePage_PageIndexOutOfRange_ReturnsNoSuchPage()
    {
        var layout = OnePageLayout();

        var result = PageEditEndpoint.RenamePage(layout, 1, "SHIP2");

        Assert.Equal(PageEditEndpoint.EditOutcome.NoSuchPage, result.Outcome);
        Assert.Null(result.Layout);
    }

    // -------------------------------------------------------------------
    // SetShowWhen
    // -------------------------------------------------------------------

    [Fact]
    public void SetShowWhen_ValidTokens_StoresThemVerbatim()
    {
        var layout = OnePageLayout();

        var result = PageEditEndpoint.SetShowWhen(layout, 0, new[] { "InSrv", "Vessel:testbuggy" });

        Assert.Equal(PageEditEndpoint.EditOutcome.Ok, result.Outcome);
        Assert.Equal(new[] { "InSrv", "Vessel:testbuggy" }, result.Layout!.Pages[0].ShowWhen);
    }

    [Fact]
    public void SetShowWhen_EmptyList_ClearsToNull()
    {
        var layout = OnePageLayout(showWhen: new[] { "InMainShip" });

        var result = PageEditEndpoint.SetShowWhen(layout, 0, Array.Empty<string>());

        Assert.Equal(PageEditEndpoint.EditOutcome.Ok, result.Outcome);
        Assert.Null(result.Layout!.Pages[0].ShowWhen);
    }

    [Fact]
    public void SetShowWhen_NullList_ClearsToNull()
    {
        var layout = OnePageLayout(showWhen: new[] { "InMainShip" });

        var result = PageEditEndpoint.SetShowWhen(layout, 0, null);

        Assert.Equal(PageEditEndpoint.EditOutcome.Ok, result.Outcome);
        Assert.Null(result.Layout!.Pages[0].ShowWhen);
    }

    [Fact]
    public void SetShowWhen_UnparsableToken_ReturnsInvalidShowWhen_AndChangesNothing()
    {
        var layout = OnePageLayout();

        var result = PageEditEndpoint.SetShowWhen(layout, 0, new[] { "NotARealConditionName" });

        Assert.Equal(PageEditEndpoint.EditOutcome.InvalidShowWhen, result.Outcome);
        Assert.Null(result.Layout);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void SetShowWhen_VesselTokenWithNoTypeNamed_ReturnsInvalidShowWhen()
    {
        var layout = OnePageLayout();

        var result = PageEditEndpoint.SetShowWhen(layout, 0, new[] { "Vessel:" });

        Assert.Equal(PageEditEndpoint.EditOutcome.InvalidShowWhen, result.Outcome);
    }

    [Fact]
    public void SetShowWhen_PageIndexOutOfRange_ReturnsNoSuchPage()
    {
        var layout = OnePageLayout();

        var result = PageEditEndpoint.SetShowWhen(layout, 1, new[] { "InMainShip" });

        Assert.Equal(PageEditEndpoint.EditOutcome.NoSuchPage, result.Outcome);
    }

    // -------------------------------------------------------------------
    // SetShowWhen - exact-set duplicate guard
    // -------------------------------------------------------------------

    private static Layout TwoPageLayout(IReadOnlyList<string>? page0ShowWhen, IReadOnlyList<string>? page1ShowWhen) =>
        new(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { Slot(0, "LandingGearToggle") }, Array.Empty<LayoutSlot>(), page0ShowWhen),
            new LayoutPage("SRV", "t6", new[] { Slot(0, "LandingGearToggle") }, Array.Empty<LayoutSlot>(), page1ShowWhen),
        });

    [Fact]
    public void SetShowWhen_ExactSetMatchOnAnotherPage_SameOrderDifferent_ReturnsConflict_AndChangesNothing()
    {
        var layout = TwoPageLayout(new[] { "InSrv", "Vessel:testbuggy" }, null);

        var result = PageEditEndpoint.SetShowWhen(layout, 1, new[] { "Vessel:testbuggy", "InSrv" });

        Assert.Equal(PageEditEndpoint.EditOutcome.ShowWhenConflict, result.Outcome);
        Assert.Equal(0, result.ConflictPageIndex);
        Assert.Equal("SHIP", result.ConflictPageName);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void SetShowWhen_SpecificGeneralOverlap_IsNotAConflict()
    {
        var layout = TwoPageLayout(new[] { "InSrv" }, null);

        var result = PageEditEndpoint.SetShowWhen(layout, 1, new[] { "InSrv", "Vessel:testbuggy" });

        Assert.Equal(PageEditEndpoint.EditOutcome.Ok, result.Outcome);
        Assert.Equal(new[] { "InSrv", "Vessel:testbuggy" }, result.Layout!.Pages[1].ShowWhen);
        Assert.Equal(new[] { "InSrv" }, result.Layout.Pages[0].ShowWhen);
    }

    [Fact]
    public void SetShowWhen_EmptyCandidate_NeverConflicts_EvenAgainstAnotherContextFreePage()
    {
        var layout = TwoPageLayout(null, null);

        var result = PageEditEndpoint.SetShowWhen(layout, 1, Array.Empty<string>());

        Assert.Equal(PageEditEndpoint.EditOutcome.Ok, result.Outcome);
        Assert.Null(result.Layout!.Pages[1].ShowWhen);
    }

    [Fact]
    public void SetShowWhen_ForceTrue_ClearsTheConflictingPage_AndSetsThisPage_InOneEdit()
    {
        var layout = TwoPageLayout(new[] { "InSrv", "Vessel:testbuggy" }, null);

        var result = PageEditEndpoint.SetShowWhen(layout, 1, new[] { "InSrv", "Vessel:testbuggy" }, force: true);

        Assert.Equal(PageEditEndpoint.EditOutcome.Ok, result.Outcome);
        Assert.Null(result.Layout!.Pages[0].ShowWhen);
        Assert.Equal(new[] { "InSrv", "Vessel:testbuggy" }, result.Layout.Pages[1].ShowWhen);
    }

    [Fact]
    public void SetShowWhen_ForceFalse_LeavesBothPagesShowWhenCompletelyUntouched()
    {
        var original = new[] { "InSrv", "Vessel:testbuggy" };
        var layout = TwoPageLayout(original, null);

        var result = PageEditEndpoint.SetShowWhen(layout, 1, new[] { "InSrv", "Vessel:testbuggy" }, force: false);

        Assert.Equal(PageEditEndpoint.EditOutcome.ShowWhenConflict, result.Outcome);
        // The layout passed in must be untouched - SetShowWhen never mutates
        // its input, and a conflict returns no Layout at all to save.
        Assert.Equal(original, layout.Pages[0].ShowWhen);
        Assert.Null(layout.Pages[1].ShowWhen);
    }

    // -------------------------------------------------------------------
    // DeletePage
    // -------------------------------------------------------------------

    [Fact]
    public void DeletePage_RemovesThatPage_AndShiftsTheRest()
    {
        var layout = TwoPageLayout(new[] { "InSrv" }, null);

        var result = PageEditEndpoint.DeletePage(layout, 0);

        Assert.Equal(PageEditEndpoint.EditOutcome.Ok, result.Outcome);
        Assert.Single(result.Layout!.Pages);
        Assert.Equal("SRV", result.Layout.Pages[0].Name);
    }

    [Fact]
    public void DeletePage_PageIndexOutOfRange_ReturnsNoSuchPage()
    {
        var layout = TwoPageLayout(null, null);

        var result = PageEditEndpoint.DeletePage(layout, 2);

        Assert.Equal(PageEditEndpoint.EditOutcome.NoSuchPage, result.Outcome);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void DeletePage_NegativeIndex_ReturnsNoSuchPage()
    {
        var layout = TwoPageLayout(null, null);

        var result = PageEditEndpoint.DeletePage(layout, -1);

        Assert.Equal(PageEditEndpoint.EditOutcome.NoSuchPage, result.Outcome);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void DeletePage_OnlyOnePageInLayout_ReturnsCannotDeleteLastPage_AndChangesNothing()
    {
        var layout = OnePageLayout();

        var result = PageEditEndpoint.DeletePage(layout, 0);

        Assert.Equal(PageEditEndpoint.EditOutcome.CannotDeleteLastPage, result.Outcome);
        Assert.Null(result.Layout);
        Assert.Single(layout.Pages);
    }

    // -------------------------------------------------------------------
    // MovePage
    // -------------------------------------------------------------------

    [Fact]
    public void MovePage_MovesTheFirstPageToTheEnd()
    {
        var layout = TwoPageLayout(new[] { "InSrv" }, null);

        var result = PageEditEndpoint.MovePage(layout, 0, 1);

        Assert.Equal(PageEditEndpoint.EditOutcome.Ok, result.Outcome);
        Assert.Equal("SRV", result.Layout!.Pages[0].Name);
        Assert.Equal("SHIP", result.Layout.Pages[1].Name);
        Assert.Equal(new[] { "InSrv" }, result.Layout.Pages[1].ShowWhen);
    }

    [Fact]
    public void MovePage_MovesTheLastPageToTheFront()
    {
        var layout = TwoPageLayout(new[] { "InSrv" }, null);

        var result = PageEditEndpoint.MovePage(layout, 1, 0);

        Assert.Equal(PageEditEndpoint.EditOutcome.Ok, result.Outcome);
        Assert.Equal("SRV", result.Layout!.Pages[0].Name);
        Assert.Equal("SHIP", result.Layout.Pages[1].Name);
    }

    [Fact]
    public void MovePage_FromIndexOutOfRange_ReturnsNoSuchPage()
    {
        var layout = TwoPageLayout(null, null);

        var result = PageEditEndpoint.MovePage(layout, 2, 0);

        Assert.Equal(PageEditEndpoint.EditOutcome.NoSuchPage, result.Outcome);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void MovePage_ToIndexOutOfRange_ReturnsNoSuchPage()
    {
        var layout = TwoPageLayout(null, null);

        var result = PageEditEndpoint.MovePage(layout, 0, 2);

        Assert.Equal(PageEditEndpoint.EditOutcome.NoSuchPage, result.Outcome);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void MovePage_SameFromAndTo_IsANoOp_ButStillOk()
    {
        var layout = TwoPageLayout(new[] { "InSrv" }, null);

        var result = PageEditEndpoint.MovePage(layout, 0, 0);

        Assert.Equal(PageEditEndpoint.EditOutcome.Ok, result.Outcome);
        Assert.Equal("SHIP", result.Layout!.Pages[0].Name);
        Assert.Equal("SRV", result.Layout.Pages[1].Name);
    }
}
