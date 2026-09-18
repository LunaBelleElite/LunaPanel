using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Which macro routes are authoring and which are reading
/// (<see cref="HostOnlyRoutes"/>). The whole value of this class is the
/// pairs it keeps apart: <c>GET</c> and <c>POST</c> on the same literal
/// path, and a near-miss path that must not be swept up by a prefix check.
/// </summary>
public class HostOnlyRoutesTests
{
    /// <summary>
    /// The three that write. Anything on this list changes what every
    /// device's buttons do, which is why it is the line that gets enforced.
    /// </summary>
    [Theory]
    [InlineData("/api/macros")]
    [InlineData("/api/macros/copy")]
    [InlineData("/api/macros/delete")]
    public void PostingToAnAuthoringRoute_RequiresTheHost(string path)
    {
        Assert.True(HostOnlyRoutes.RequiresHost(path, "POST"));
    }

    /// <summary>
    /// Driven off <see cref="ApiPaths"/> itself rather than the literals
    /// above, so renaming a route in the one place route literals are
    /// spelled cannot leave this gate pointing at a path that no longer
    /// exists while the theory above goes on passing against a stale string.
    /// </summary>
    [Fact]
    public void TheAuthoringRoutes_AreTheOnesApiPathsSpells()
    {
        Assert.True(HostOnlyRoutes.RequiresHost(ApiPaths.Macros, "POST"));
        Assert.True(HostOnlyRoutes.RequiresHost(ApiPaths.MacrosCopy, "POST"));
        Assert.True(HostOnlyRoutes.RequiresHost(ApiPaths.MacrosDelete, "POST"));
    }

    /// <summary>
    /// Reading is not authoring. <c>GET</c> on the very same literal the
    /// save posts to is the pair that matters: a path-only gate would refuse
    /// a tablet the macro list, which is how a macro gets onto a button at
    /// all.
    /// </summary>
    [Fact]
    public void ReadingTheMacroList_DoesNotRequireTheHost()
    {
        Assert.False(HostOnlyRoutes.RequiresHost(ApiPaths.Macros, "GET"));
    }

    /// <summary>
    /// The vocabulary is a constant table of flag and event names read off
    /// <c>StatusVocabulary</c>/<c>JournalVocabulary</c>. Refusing it would
    /// hide the pickers from a device that already cannot save anything -
    /// UI-hiding dressed as a security rule.
    /// </summary>
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public void TheVocabulary_IsNeverHostOnly(string method)
    {
        Assert.False(HostOnlyRoutes.RequiresHost(ApiPaths.MacrosVocabulary, method));
    }

    /// <summary>
    /// Exact match, not a prefix - the same discipline
    /// <c>DeviceAuthMiddlewareExtensions.IsExemptPath</c> follows, and for
    /// the mirror-image reason: a prefix check here would make
    /// <c>/api/macros/vocabulary</c> host-only for free, and a trailing
    /// slash would slip past it.
    /// </summary>
    [Theory]
    [InlineData("/api/macros/")]
    [InlineData("/api/macros/copy/")]
    [InlineData("/api/macroses")]
    [InlineData("/api/macros/vocabulary")]
    public void NearMissPaths_AreNotHostOnly(string path)
    {
        Assert.False(HostOnlyRoutes.RequiresHost(path, "POST"));
    }

    [Fact]
    public void NoPathAndNoMethod_IsNotHostOnly()
    {
        Assert.False(HostOnlyRoutes.RequiresHost(null, "POST"));
        Assert.False(HostOnlyRoutes.RequiresHost(ApiPaths.Macros, null));
    }

    /// <summary>
    /// The refusal has to name where the builder actually is. A device told
    /// only "no" has nowhere to go.
    ///
    /// The menu label is asserted <b>in its quoted form</b>, not as a bare
    /// substring. Measured, not stylistic: renaming the tray item to
    /// "Macros" left a bare <c>Contains</c> passing, because the sentence
    /// happens to begin with the word "Macros" - the needle was satisfied by
    /// an occurrence that has nothing to do with the item it is meant to
    /// name.
    /// </summary>
    [Fact]
    public void TheRefusal_NamesTheTrayItemThatOpensTheBuilder()
    {
        Assert.Contains("PC running LunaPanel", HostOnlyRoutes.NotTheHostAdvice, StringComparison.Ordinal);
        Assert.Contains($"\"{LunaPanel.Server.Tray.TrayMacroBuilder.MenuLabel}\"", HostOnlyRoutes.NotTheHostAdvice, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------
    // Transfer (ref/docs/transfer.md) - the exception to "the line is
    // authoring", and the pair of claims that keeps it honest
    // -----------------------------------------------------------------

    /// <summary>
    /// Host-only under <b>every</b> method, reading included - which is
    /// exactly what the authoring three are not. A theory over both methods
    /// and all four routes, because "host-only on POST" is the answer that
    /// would be produced by simply adding these to the existing list, and it
    /// would leave <c>GET</c> handing a commander's whole arrangement, and
    /// every device name in the household, to any paired tablet.
    /// </summary>
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public void EveryTransferRoute_RequiresTheHost_UnderEveryMethod(string method)
    {
        Assert.True(HostOnlyRoutes.RequiresHost(ApiPaths.TransferTargets, method));
        Assert.True(HostOnlyRoutes.RequiresHost(ApiPaths.TransferProfile, method));
        Assert.True(HostOnlyRoutes.RequiresHost(ApiPaths.TransferMacro, method));
        Assert.True(HostOnlyRoutes.RequiresHost(ApiPaths.TransferUndo, method));
    }

    /// <summary>
    /// The other half: the macro-reading route a tablet needs did not become
    /// host-only when the transfer routes did. Both directions in one test -
    /// the claim is that the two families are treated differently, and a
    /// single-direction assertion passes against a gate that had started
    /// refusing everything.
    /// </summary>
    [Fact]
    public void AddingTheTransferRoutes_DidNotMakeReadingMacrosHostOnly()
    {
        Assert.True(HostOnlyRoutes.RequiresHost(ApiPaths.TransferProfile, "GET"));
        Assert.False(HostOnlyRoutes.RequiresHost(ApiPaths.Macros, "GET"));
        Assert.False(HostOnlyRoutes.RequiresHost(ApiPaths.MacrosVocabulary, "GET"));
    }

    /// <summary>
    /// <b>Macro timing is deliberately not host-only, under either method</b>
    /// (2026-09-10). It gained a tray item on the PC the same day the
    /// transfer routes did, and the reasoning that made those host-only does
    /// not carry: the commander asked for a server-side adjustment area, not
    /// for the tablet's own Timing pane to be taken away, and the value is
    /// one machine-wide number either way rather than a file or a list of
    /// every device in the household.
    ///
    /// Pinned here, at the decision itself, because "the timing pane is on
    /// the PC now, tidy it up like the others" is a plausible later edit that
    /// would silently remove a pane the commander uses.
    /// </summary>
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public void MacroTiming_IsNeverHostOnly(string method)
    {
        Assert.False(HostOnlyRoutes.RequiresHost(ApiPaths.MacroTiming, method));
    }

    /// <summary>
    /// A commander who wanted to export a profile and was sent to "Build a
    /// macro" would be worse off than one told nothing. Each family gets its
    /// own sentence, and each names the tray item that actually exists -
    /// asserted against the seams' own labels rather than repeated literals,
    /// so renaming a menu item cannot leave a refusal pointing at nothing.
    /// </summary>
    [Fact]
    public void TheRefusal_ForATransferRoute_NamesTheImportExportTrayItem_NotTheBuilder()
    {
        var advice = HostOnlyRoutes.AdviceFor(ApiPaths.TransferProfile);

        Assert.Equal(HostOnlyRoutes.NotTheHostTransferAdvice, advice);
        Assert.Contains($"\"{LunaPanel.Server.Tray.TrayTransfer.MenuLabel}\"", advice, StringComparison.Ordinal);
        Assert.DoesNotContain(LunaPanel.Server.Tray.TrayMacroBuilder.MenuLabel, advice, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the authoring routes kept theirs. Asked after
    /// <c>RequiresHost</c> has said yes, so the authoring wording is the
    /// fallback rather than a guess.
    /// </summary>
    [Theory]
    [InlineData("/api/macros")]
    [InlineData("/api/macros/copy")]
    [InlineData("/api/macros/delete")]
    public void TheRefusal_ForAnAuthoringRoute_IsStillTheBuilderSentence(string path)
    {
        Assert.Equal(HostOnlyRoutes.NotTheHostAdvice, HostOnlyRoutes.AdviceFor(path));
    }

    /// <summary>
    /// Exact match here too: a near-miss transfer path is not swept up, and
    /// - more importantly - the transfer check does not accidentally widen
    /// the authoring one by matching on a prefix.
    /// </summary>
    [Theory]
    [InlineData("/api/transfer")]
    [InlineData("/api/transfer/")]
    [InlineData("/api/transfer/profiles")]
    [InlineData("/api/transfer/profile/undo")]
    public void NearMissTransferPaths_AreNotHostOnly(string path)
    {
        Assert.False(HostOnlyRoutes.RequiresHost(path, "GET"));
    }
}
