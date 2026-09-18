using LunaPanel.Core.Macros;
using LunaPanel.Server.Macros;

namespace LunaPanel.Tests.Macros;

/// <summary>
/// Pins <see cref="MacroLoader.LoadShipped"/> against the real embedded
/// macro resources - same discipline as
/// <see cref="LunaPanel.Tests.Catalogue.CatalogueLoaderTests"/>, generalized
/// to more than one shipped file. All shipped macros are also read
/// directly via <c>Assembly.GetManifestResourceStream</c> elsewhere
/// (<c>MacroRunnerTests</c>'s own <c>Shipped*Macro_...</c> tests) - this file
/// is what actually exercises the production loader's own discovery logic
/// instead, which nothing else in this suite reaches.
/// </summary>
public class MacroLoaderTests
{
    [Fact]
    public void LoadShipped_ReturnsAllFifteenShippedMacros_ById()
    {
        // [2026-09-07, superseded: was "both shipped macros" - request-docking
        // and nomad-dock-launch (ref/docs/panel-tab-tracking.md) are new.]
        // [2026-09-09, superseded: was "all four" - nomad-disembark and
        // enter-cockpit (ref/docs/vessel-context.md's Nomad page) are new,
        // replacing the ship's own "disembark" macro on the NOMAD page's
        // wrong slot (ref/docs/macros.md's "The Nomad's docking button was
        // wrong" pattern, applied to the sibling slot it left behind).]
        // [2026-09-12, superseded: was "all six" - srv-board-ship and
        // srv-launch (TASK 4b, expressive-kindling-starfish.md) are new,
        // deliberately separate files rather than a reuse of
        // nomad-dock-launch.]
        // [2026-09-16, superseded: was "all eight" - five new pip macro
        // FILES (pip-preset-engines, pip-preset-shields, and three
        // experimental two-category combos) join the existing
        // pip-preset-weapons, which was only renamed, not added. 8 + 5 = 13.]
        // [2026-09-16, superseded again, same day: the three two-category
        // combo macros were renamed and their press pattern corrected
        // (pip-preset-weapons-shields/-weapons-engines/-engines-shields ->
        // pip-preset-shields-weapons/-engines-weapons/-shields-engines) -
        // still three files, still 13 total, ids changed only.]
        // [2026-09-16, superseded again, same day: was "all thirteen" - the
        // SRV and RHINO pages had no way to exit onto foot at all.
        // srv-disembark and rhino-disembark are new, reusing nomad-disembark's
        // proven FocusRadarPanel_Buggy/UI_Right/UI_Select sequence. 13 + 2 =
        // 15. FIGHTER was explicitly excluded from this change.]
        // [2026-09-17, superseded: the three single-system "Power to X"
        // preset macros (pip-preset-engines/-shields/-weapons) are retired -
        // the commander now wants every pairwise ordering of the three
        // systems as a combo instead. Three new combo macros
        // (pip-preset-weapons-engines/-engines-shields/-weapons-shields)
        // join the three that already existed, so the three singles leaving
        // and three combos arriving cancel out: still 15 total. See
        // LoadShipped_TheSixPipComboMacros_CoverEveryPrimarySecondaryOrdering
        // below for the full set and LoadShipped_RevivedComboIds_... for why
        // three of the six ids look like a previously-retired id.]
        // [2026-09-17, superseded again, same day: was 15 - prepare-to-dock
        // is new, promoted from the commander's live personal user macro to
        // a shipped default (tablet SHIP slot 41). 15 + 1 = 16.]
        var macros = MacroLoader.LoadShipped();

        var ids = macros.Select(m => m.Id).ToList();
        Assert.Contains("pip-preset-engines-weapons", ids);
        Assert.Contains("pip-preset-shields-weapons", ids);
        Assert.Contains("pip-preset-shields-engines", ids);
        Assert.Contains("pip-preset-weapons-engines", ids);
        Assert.Contains("pip-preset-engines-shields", ids);
        Assert.Contains("pip-preset-weapons-shields", ids);
        Assert.Contains("disembark", ids);
        Assert.Contains("request-docking", ids);
        Assert.Contains("nomad-dock-launch", ids);
        Assert.Contains("nomad-disembark", ids);
        Assert.Contains("enter-cockpit", ids);
        Assert.Contains("srv-board-ship", ids);
        Assert.Contains("srv-launch", ids);
        Assert.Contains("srv-disembark", ids);
        Assert.Contains("rhino-disembark", ids);
        Assert.Contains("prepare-to-dock", ids);
    }

    [Fact]
    public void LoadShipped_ReturnsExactlySixteenMacros_NoMore_NoFewer()
    {
        // A sweep, not just a "contains" check - if a future macro is added
        // and this count is not updated deliberately, that is exactly the
        // signal a sweep test exists to catch (as opposed to a silently
        // growing or shrinking set nothing ever notices).
        // [2026-09-07, superseded: was 2.] [2026-09-09, superseded: was 4.]
        // [2026-09-12, superseded: was 6.] [2026-09-16, superseded: was 8.]
        // [2026-09-16, superseded again, same day: was 13 - srv-disembark and
        // rhino-disembark are new (SRV/RHINO exit-to-foot parity).]
        // [2026-09-17, superseded: still 15 - the three single-system
        // "Power to X" preset macros were deleted and three new pip-combo
        // macros were added in their place (see the id list above), a wash.]
        // [2026-09-17, superseded again, same day: was 15 - prepare-to-dock
        // is new (see the id list above).]
        var macros = MacroLoader.LoadShipped();

        Assert.Equal(16, macros.Count);
    }

    [Fact]
    public void LoadShipped_EveryMacroHasAtLeastOneStep()
    {
        var macros = MacroLoader.LoadShipped();

        Assert.All(macros, m => Assert.NotEmpty(m.Steps));
    }

    // -------------------------------------------------------------------
    // The six pip-combo macros (2026-09-16 renamed three, 2026-09-17 added
    // three more): reset, wait 150ms, then the SECONDARY system's increase
    // action x4 followed by the PRIMARY system's increase action x4 -
    // primary ends at 4 pips, secondary at 2. All six pairwise
    // primary/secondary orderings of {Engines, Shields, Weapons} now exist
    // side by side.
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("pip-preset-engines-weapons", "Engines +\nWeapons", "IncreaseWeaponsPower", "IncreaseEnginesPower")]
    [InlineData("pip-preset-shields-weapons", "Shields +\nWeapons", "IncreaseWeaponsPower", "IncreaseSystemsPower")]
    [InlineData("pip-preset-shields-engines", "Shields +\nEngines", "IncreaseEnginesPower", "IncreaseSystemsPower")]
    [InlineData("pip-preset-weapons-engines", "Weapons +\nEngines", "IncreaseEnginesPower", "IncreaseWeaponsPower")]
    [InlineData("pip-preset-engines-shields", "Engines +\nShields", "IncreaseSystemsPower", "IncreaseEnginesPower")]
    [InlineData("pip-preset-weapons-shields", "Weapons +\nShields", "IncreaseSystemsPower", "IncreaseWeaponsPower")]
    public void LoadShipped_ComboMacro_HasTheSecondaryThenPrimaryFourFourShape(
        string id, string expectedName, string expectedSecondaryAction, string expectedPrimaryAction)
    {
        var macro = MacroLoader.LoadShipped().Single(m => m.Id == id);

        Assert.Equal(expectedName, macro.Name);
        Assert.Equal(4, macro.Steps.Count);

        var reset = Assert.IsType<PressStep>(macro.Steps[0]);
        Assert.Equal("ResetPowerDistribution", reset.Action);
        Assert.Equal(1, reset.Repeat);

        var wait = Assert.IsType<WaitStep>(macro.Steps[1]);
        Assert.Equal(TimeSpan.FromMilliseconds(150), wait.Duration);

        var secondary = Assert.IsType<PressStep>(macro.Steps[2]);
        Assert.Equal(expectedSecondaryAction, secondary.Action);
        Assert.Equal(4, secondary.Repeat);

        var primary = Assert.IsType<PressStep>(macro.Steps[3]);
        Assert.Equal(expectedPrimaryAction, primary.Action);
        Assert.Equal(4, primary.Repeat);
    }

    // [2026-09-17, supersedes LoadShipped_TheThreeOldComboIds_AreGone
    // (2026-09-16): that test asserted "pip-preset-weapons-shields",
    // "pip-preset-weapons-engines" and "pip-preset-engines-shields" must
    // never reappear, because on 2026-09-16 those exact ids briefly shipped
    // a broken press pattern under an experimental naming scheme, then were
    // renamed away to their current, differently-named counterparts
    // (pip-preset-shields-weapons/-engines-weapons/-shields-engines, which
    // flip which system is named first and therefore which is primary) with
    // a corrected 4/4 pattern - and that rename was pinned as permanent.
    //
    // The coordinator's own reasoning for reviving the three retired ids,
    // quoted verbatim: "the 'three old ids must never reappear' test was
    // written when this project only ever intended ONE primary choice per
    // pair (3 total combos) - it was correctly guarding against a
    // broken-math macro resurfacing under a name that looks legitimate. It
    // was never written with the possibility in mind that the commander
    // would later ask for BOTH primary orderings per pair (6 total, which
    // is what's happening now). The old ruling was correct for its own
    // scope; the scope has since expanded."
    //
    // Note this is NOT a case of the revived id flipping its own primary -
    // pip-preset-weapons-shields still names Weapons first and Weapons is
    // still its primary, same as before. What changed is the MATH: this
    // pin exists to hold that these three specific, previously-poisoned ids
    // now carry exactly the same correct 4/4 secondary-then-primary shape
    // as every other combo (proven generically by
    // LoadShipped_ComboMacro_HasTheSecondaryThenPrimaryFourFourShape above,
    // which these three ids are also rows of) rather than whatever broken
    // shape they briefly carried under the old, retired version of
    // themselves. Restated here explicitly, rather than left to be inferred
    // from the shared theory test, so the supersession itself has its own
    // dedicated, readable record.]
    [Theory]
    [InlineData("pip-preset-weapons-shields")]
    [InlineData("pip-preset-weapons-engines")]
    [InlineData("pip-preset-engines-shields")]
    public void LoadShipped_RevivedComboId_NowCarriesTheCorrectFourFourShape(string id)
    {
        var macro = MacroLoader.LoadShipped().Single(m => m.Id == id);

        Assert.Equal(4, macro.Steps.Count);

        var reset = Assert.IsType<PressStep>(macro.Steps[0]);
        Assert.Equal("ResetPowerDistribution", reset.Action);
        Assert.Equal(1, reset.Repeat);

        var wait = Assert.IsType<WaitStep>(macro.Steps[1]);
        Assert.Equal(TimeSpan.FromMilliseconds(150), wait.Duration);

        var secondary = Assert.IsType<PressStep>(macro.Steps[2]);
        Assert.Equal(4, secondary.Repeat);

        var primary = Assert.IsType<PressStep>(macro.Steps[3]);
        Assert.Equal(4, primary.Repeat);
    }
}
