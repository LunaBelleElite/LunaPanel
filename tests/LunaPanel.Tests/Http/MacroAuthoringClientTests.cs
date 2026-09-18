using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// What the page does about authoring, on the PC and on a device
/// (<c>ref/docs/macro-builder.md</c>'s "Authoring moved to the PC").
///
/// <b>Every assertion in this class is a PIN over the built page's text,
/// not a driven behaviour.</b> There is no browser in this suite: nothing
/// here proves a button is invisible on a real screen, only that the line
/// that hides it is in the page the server serves and says what it is
/// supposed to say. The enforcement these sit beside - a device's authoring
/// call being refused - IS driven, end to end, in
/// <c>ServerHostBuilderTests.MacroAuthoring_FromAPairedDevice_Returns403_NamingWhereTheBuilderIs</c>,
/// and that is the one that matters: this file guards the courtesy.
/// </summary>
public class MacroAuthoringClientTests
{
    private static string DevicePage() => PanelClientEndpoint.BuildPage(canAuthorMacros: false);

    private static string HostPage() => PanelClientEndpoint.BuildPage(canAuthorMacros: true);

    /// <summary>
    /// The constant the whole client half turns on, in both directions, in
    /// one test - the claim is that the two differ, and two tests each
    /// pinning one value would both pass against a page that had stopped
    /// varying it.
    /// </summary>
    [Fact]
    public void ThePage_CarriesTheAuthoringConstant_BothWays()
    {
        Assert.Contains("const CAN_AUTHOR_MACROS = true;", HostPage(), StringComparison.Ordinal);
        Assert.Contains("const CAN_AUTHOR_MACROS = false;", DevicePage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The default is the device's. A call site that forgets the argument
    /// serves the page with authoring off, never on - the deny direction,
    /// which is the whole reason the parameter has a default at all rather
    /// than being required at 100-odd call sites.
    /// </summary>
    [Fact]
    public void TheDefault_IsNoAuthoring()
    {
        Assert.Contains("const CAN_AUTHOR_MACROS = false;", PanelClientEndpoint.BuildPage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The builder is gated, never deleted - the same UI, the same page.
    /// A device that opens a macro still reads its steps back; it just
    /// cannot change them.
    /// </summary>
    [Fact]
    public void TheBuilderScreen_IsStillServedToADevice()
    {
        var page = DevicePage();

        Assert.Contains("id=\"macroBuilder\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"macroStepList\"", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The three authoring affordances, each keyed to the constant. Pinned
    /// as the exact expressions, because "hidden when not the host" and
    /// "hidden when the host" are one character apart and both compile.
    /// </summary>
    [Fact]
    public void TheAuthoringAffordances_AreKeyedToTheConstant()
    {
        var page = DevicePage();

        Assert.Contains("el('macroNew').classList.toggle('hidden', !CAN_AUTHOR_MACROS);", page, StringComparison.Ordinal);
        Assert.Contains("el('macroCopy').classList.toggle('hidden', !macroDraftReadOnly || !CAN_AUTHOR_MACROS);", page, StringComparison.Ordinal);
        Assert.Contains("if (!CAN_AUTHOR_MACROS) return 'device';", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// A macro opened on a device is read-only whoever wrote it, and the
    /// reason shown is the true one. Telling a commander their own macro
    /// "came with LunaPanel" would be a lie, and the shipped-macro sentence
    /// is the one that was already there to be reused by accident.
    /// </summary>
    [Fact]
    public void TheReadOnlyReason_IsTheTrueOne_NotTheShippedMacroSentence()
    {
        var page = DevicePage();

        Assert.Contains("readOnlyReason === 'device'", page, StringComparison.Ordinal);
        Assert.Contains("Changing it happens on the PC running LunaPanel", page, StringComparison.Ordinal);
        // The shipped-macro wording is still there for its own case.
        Assert.Contains("This one came with LunaPanel, so it stays as it is", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// A commander who taps Macros on a tablet is told where the builder
    /// went, in the pane they went looking for it in - and told they can
    /// still put one on a button, which is what the pane is still for.
    /// </summary>
    [Fact]
    public void TheMacrosPane_SaysWhereTheBuilderIs_WhenItIsNotHere()
    {
        var page = DevicePage();

        Assert.Contains("id=\"macroAuthorOnPc\"", page, StringComparison.Ordinal);
        Assert.Contains("Macros are built on the PC running LunaPanel", page, StringComparison.Ordinal);
        Assert.Contains("el('macroAuthorOnPc').classList.toggle('hidden', CAN_AUTHOR_MACROS);", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The tray opens this page at <c>#macros</c>; without this the item
    /// would land the commander on the panel with the builder three taps
    /// away, which is the opposite of "so that it can be found easily".
    /// Pinned against the tray's own URL rather than a repeated literal, so
    /// the two cannot drift into disagreeing about the fragment.
    /// </summary>
    [Fact]
    public void ThePage_OpensTheMacrosPane_ForTheFragmentTheTraySends()
    {
        var page = HostPage();

        Assert.Contains("location.hash === '#macros'", page, StringComparison.Ordinal);
        Assert.Contains("showSettingsPane('paneMacros')", page, StringComparison.Ordinal);
        Assert.EndsWith("#macros", LunaPanel.Server.Tray.TrayMacroBuilder.Url(51824), StringComparison.Ordinal);
    }
}
