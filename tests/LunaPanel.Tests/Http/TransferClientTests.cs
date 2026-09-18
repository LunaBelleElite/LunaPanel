using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// What the page does about importing and exporting
/// (<c>ref/docs/transfer.md</c>).
///
/// <b>Every assertion in this class is a PIN over the built page's text, not
/// a driven behaviour.</b> There is no browser in this suite: nothing here
/// proves a tab is invisible on a real screen, that a file input opens, or
/// that a download saves. It proves the lines that do those things are in
/// the page the server serves and say what they are supposed to say. The
/// enforcement these sit beside - a device's transfer call being refused -
/// IS driven end to end in
/// <c>ServerHostBuilderTests.Transfer_FromAPairedDevice_Returns403_NamingWhereItLives</c>,
/// and that is the one that matters.
/// </summary>
public class TransferClientTests
{
    private static string DevicePage() => PanelClientEndpoint.BuildPage(canAuthorMacros: false);

    private static string HostPage() => PanelClientEndpoint.BuildPage(canAuthorMacros: true);

    /// <summary>
    /// The constant the pane turns on, in both directions, in one test - the
    /// claim is that the two differ, and two tests each pinning one value
    /// would both pass against a page that had stopped varying it.
    /// </summary>
    [Fact]
    public void ThePage_CarriesTheHostConstant_BothWays()
    {
        Assert.Contains("const IS_HOST = true;", HostPage(), StringComparison.Ordinal);
        Assert.Contains("const IS_HOST = false;", DevicePage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A call site that forgets the argument serves the page a tablet gets,
    /// never the PC's - the deny direction, same as
    /// <c>CAN_AUTHOR_MACROS</c>'s.
    /// </summary>
    [Fact]
    public void TheDefault_IsNotTheHost()
    {
        Assert.Contains("const IS_HOST = false;", PanelClientEndpoint.BuildPage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Two constants, not one aliased to the other. Pinned because the
    /// tempting simplification - <c>const IS_HOST = CAN_AUTHOR_MACROS;</c> -
    /// makes a pane hide itself because macro authoring was off, which is
    /// answering the wrong question.
    /// </summary>
    [Fact]
    public void TheTwoCapabilities_AreSpelledSeparately_NeverDerivedFromEachOther()
    {
        var page = HostPage();

        Assert.Contains("const CAN_AUTHOR_MACROS = true;", page, StringComparison.Ordinal);
        Assert.Contains("const IS_HOST = true;", page, StringComparison.Ordinal);
        Assert.DoesNotContain("IS_HOST = CAN_AUTHOR_MACROS", page, StringComparison.Ordinal);
        Assert.DoesNotContain("CAN_AUTHOR_MACROS = IS_HOST", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The tab is hidden outright on a device, unlike the Macros tab beside
    /// it. Pinned as the exact expression, because "hidden when not the
    /// host" and "hidden when the host" are one character apart and both
    /// run.
    /// </summary>
    [Fact]
    public void TheTab_IsHiddenOnAnythingThatIsNotThePc()
    {
        Assert.Contains(
            "el('tabTransfer').classList.toggle('hidden', !IS_HOST);",
            DevicePage(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Both halves of the pane exist, and the two selects that pick what is
    /// exported and where an import lands are separate elements. One select
    /// used for both would silently make "export from the tablet" mean
    /// "import onto the tablet".
    /// </summary>
    [Fact]
    public void ThePane_HasBothHalves_WithSeparatePickersForSourceAndTarget()
    {
        var page = HostPage();

        Assert.Contains("id=\"paneTransfer\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"transferExportDevice\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"transferImportDevice\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"transferExportMacro\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"transferImportMacros\"", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Only devices that HAVE an arrangement can be exported from; any of
    /// them can be imported onto. Pinned as the exact filter, because the
    /// two calls are otherwise identical and swapping them produces a pane
    /// that offers to export from a device that has nothing.
    /// </summary>
    [Fact]
    public void TheExportPicker_OffersOnlyDevicesThatHaveALayout_AndTheImportPickerOffersThemAll()
    {
        var page = HostPage();

        Assert.Contains("fillTransferDevices(el('transferExportDevice'), targets.filter(t => t.hasLayout));", page, StringComparison.Ordinal);
        Assert.Contains("fillTransferDevices(el('transferImportDevice'), targets);", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Importing replaces what is on that device, so it warns first - the
    /// rule <c>ref/docs/layout-import.md</c> already set for the in-place
    /// copy, and the one thing here that a commander cannot take back by
    /// closing a dialog.
    /// </summary>
    [Fact]
    public void ImportingAProfile_WarnsBeforeItOverwrites()
    {
        var page = HostPage();

        Assert.Contains("if (!confirm('Replace the buttons on '", page, StringComparison.Ordinal);
        Assert.Contains("You can undo it straight afterwards.", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The undo is offered only after a profile import actually happened,
    /// and goes away once used - one kept generation, so a second undo would
    /// put the import back.
    /// </summary>
    [Fact]
    public void TheUndo_IsHiddenUntilThereIsSomethingToUndo_AndAgainAfterwards()
    {
        var page = HostPage();

        Assert.Contains("id=\"transferUndo\" type=\"button\" class=\"hidden\"", page, StringComparison.Ordinal);
        Assert.Contains("el('transferUndo').classList.toggle('hidden', !undoDeviceId);", page, StringComparison.Ordinal);
        Assert.Contains("transferUndoDeviceId = null;", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// A shipped macro is left out of the export picker, because the
    /// receiving copy already has it under the same id. The server refuses
    /// one anyway (<c>TransferEndpointTests.ExportMacros_AShippedMacro_IsRefused</c>);
    /// this is the courtesy that stops a commander picking a row that cannot
    /// work.
    /// </summary>
    [Fact]
    public void TheMacroPicker_LeavesOutShippedMacros()
    {
        Assert.Contains("if (m.isShipped) continue;", HostPage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Fetched and saved as a blob rather than navigated to, so a refusal is
    /// read out of the response and shown in the pane. A navigation would
    /// put the commander on a page of JSON with the panel gone - and the
    /// naive spelling (<c>location.href = url</c>) is exactly what somebody
    /// would reach for.
    /// </summary>
    [Fact]
    public void AnExport_IsFetched_NeverNavigatedTo()
    {
        var page = HostPage();

        Assert.Contains("const blob = await res.blob();", page, StringComparison.Ordinal);
        Assert.Contains("link.download = transferFileNameOf(res.headers.get('Content-Disposition'));", page, StringComparison.Ordinal);
        Assert.DoesNotContain("location.href = '__API_TRANSFER", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The tray opens this page at <c>#transfer</c>. Pinned against the
    /// tray's own URL rather than a repeated literal, so the two cannot
    /// drift into disagreeing about the fragment.
    /// </summary>
    [Fact]
    public void ThePage_OpensTheTransferPane_ForTheFragmentTheTraySends()
    {
        var page = HostPage();

        Assert.Contains("location.hash === '#transfer'", page, StringComparison.Ordinal);
        Assert.Contains("showSettingsPane('paneTransfer')", page, StringComparison.Ordinal);
        Assert.EndsWith("#transfer", LunaPanel.Server.Tray.TrayTransfer.Url(51824), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every route the pane calls is substituted from <see cref="ApiPaths"/>
    /// rather than typed into the page - so a route rename cannot leave the
    /// pane calling an address that no longer exists. Asserted by checking
    /// no placeholder survived AND that each real literal is present, since
    /// a missing <c>.Replace</c> leaves the token in the page and a typo'd
    /// one leaves a literal nothing serves.
    /// </summary>
    [Fact]
    public void EveryTransferRoute_IsSubstitutedFromApiPaths()
    {
        var page = HostPage();

        Assert.DoesNotContain("__API_TRANSFER", page, StringComparison.Ordinal);
        Assert.DoesNotContain("__IS_HOST__", page, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.TransferTargets}'", page, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.TransferProfile}?deviceId='", page, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.TransferMacro}?id='", page, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.TransferUndo}?deviceId='", page, StringComparison.Ordinal);
    }
}
