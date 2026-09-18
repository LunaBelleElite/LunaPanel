using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.GameState;
using LunaPanel.Core.Layouts;
using LunaPanel.Server.Layouts;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// Pins the shape of the embedded starter layout (see
/// <c>ref/docs/layouts.md</c>'s "The starter layout" and the task brief that
/// specified it).
///
/// [2026-09-07, superseded: slot 0 ("request-docking") used to deliberately
/// name a macro id that did not exist anywhere, as the first end-to-end
/// proof that a degraded slot renders as visibly degraded and refuses to
/// fire rather than crashing. Now that the real <c>request-docking</c> macro
/// ships (<c>ref/docs/panel-tab-tracking.md</c>), slot 0 genuinely resolves,
/// exactly like slot 17's "disembark" always has - see
/// <see cref="Load_ShipPageSlot0_NamesTheRequestDockingMacro_NotAnAction"/>
/// below.]
///
/// [2026-09-09, superseded: the starter used to be ONE context-free page
/// ("SHIP", t30). It now ships five vessel-context pages beside it - SRV,
/// NOMAD, RHINO, FIGHTER, ON FOOT - so that automatic page switching does
/// something on a fresh install instead of being inert (O33,
/// <c>ref/docs/vessel-context.md</c>'s "The default context pages"). Every
/// test below that used to say "the page" now says "the ship page" and
/// reads <c>Pages[0]</c> deliberately rather than incidentally.]
///
/// [2026-09-09, superseded again, same day: the ship page shipped
/// context-free above, making switching one-way (docking an SRV/fighter/
/// on-foot never returned the commander to it automatically). The
/// commander ruled both directions are needed, so it now declares
/// <c>InMainShip</c> - see
/// <see cref="Load_TheShipPage_DeclaresInMainShip"/> below, which replaces
/// what was <c>Load_TheShipPage_StaysContextFree</c>.]
/// </summary>
public class StarterLayoutTests
{
    private const int InMainShipBit = 24;
    private const int InFighterBit = 25;
    private const int InSrvBit = 26;
    private const int OnFootBit2 = 0;

    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static StatusSnapshot Snapshot(uint flags, uint flags2 = 0u) =>
        new(flags, flags2, null, GameRunning: true, SignedIn: true);

    private static LayoutPage PageNamed(Layout layout, string name) =>
        layout.Pages.Single(p => p.Name == name);

    // -------------------------------------------------------------------
    // The ship page (index 0). [2026-09-09, superseded: this header used to
    // say "context-free, unchanged since 2026-09-07" - it now declares
    // InMainShip, so switching back to it is automatic like every other
    // context page.]
    // -------------------------------------------------------------------

    [Fact]
    public void Load_ShipPage_IsFirst_AndUsesTemplateT30()
    {
        // [2026-09-09, superseded: was
        // Load_ReturnsExactlyOnePage_UsingTemplateT30, an Assert.Single over
        // layout.Pages. There are six pages now; that the FIRST one is the
        // ship page on t30 is the part of the old claim that still holds,
        // and the page count has its own test below.]
        var layout = StarterLayout.Load();

        Assert.Equal("SHIP", layout.Pages[0].Name);
        Assert.Equal("t30", layout.Pages[0].TemplateId);
    }

    [Fact]
    public void Load_ShipPage_HasTwentyNineActiveSlots_NoneParked()
    {
        // [2026-09-16, superseded: was 26. The pip-management task added
        // three new slots (25: srv-launch, 26: DeployHardpointToggle, 27:
        // ToggleFlightAssist) and replaced the three individual pip-adjust
        // actions at 7/8/9 with their one-tap preset macro equivalents
        // (same count, no net change there). Slot 28 stays genuinely absent -
        // the three experimental pip-combo macros are catalogue-only, not
        // placed on any default page yet. Net: 26 + 3 = 29.]
        var layout = StarterLayout.Load();
        var page = layout.Pages[0];

        Assert.Equal(29, page.Slots.Count);
        Assert.Empty(page.Parked);
    }

    [Fact]
    public void Load_ShipPageIndex28_IsGenuinelyAbsent_NotJustUnassigned()
    {
        // [2026-09-16, superseded: this used to check indices 25-28 were all
        // absent. 25-27 are now filled (srv-launch, DeployHardpointToggle,
        // ToggleFlightAssist); only 28 is left as a deliberate spare -
        // LayoutSlot has no "empty" shape of its own (exactly one of
        // Action/Macro is always required by LayoutValidator), so "empty"
        // here means the index simply has no entry in either Slots or
        // Parked at all.]
        var layout = StarterLayout.Load();
        var page = layout.Pages[0];

        Assert.DoesNotContain(page.Slots, s => s.Index == 28);
        Assert.DoesNotContain(page.Parked, s => s.Index == 28);
    }

    [Fact]
    public void Load_ShipPageSlot0_NamesTheRequestDockingMacro_NotAnAction()
    {
        var layout = StarterLayout.Load();
        var slot0 = layout.Pages[0].Slots.Single(s => s.Index == 0);

        Assert.Null(slot0.Action);
        Assert.Equal("request-docking", slot0.Macro);
    }

    [Fact]
    public void Load_ShipPageSlot17_NamesTheDisembarkMacro_NotAnAction()
    {
        // The second shipped macro slot (ref/docs/macros.md's "The shipped
        // macros").
        var layout = StarterLayout.Load();
        var slot17 = layout.Pages[0].Slots.Single(s => s.Index == 17);

        Assert.Null(slot17.Action);
        Assert.Equal("disembark", slot17.Macro);
    }

    [Fact]
    public void Load_ShipPageSlot24_NamesTheNomadDockLaunchMacro_NotAnAction()
    {
        var layout = StarterLayout.Load();
        var slot24 = layout.Pages[0].Slots.Single(s => s.Index == 24);

        Assert.Null(slot24.Action);
        Assert.Equal("nomad-dock-launch", slot24.Macro);
    }

    [Fact]
    public void Load_ShipPage_EveryOtherSlot_NamesAnAction_NeverAMacro()
    {
        // [2026-09-16, superseded: "other" used to exclude just 0, 17, 24 -
        // the pip-preset task turned 7/8/9 into one-tap presets and added
        // srv-launch at 25, all four of which now also name a macro.]
        var macroIndices = new[] { 0, 7, 8, 9, 17, 24, 25 };
        var layout = StarterLayout.Load();
        var others = layout.Pages[0].Slots.Where(s => !macroIndices.Contains(s.Index));

        Assert.All(others, s =>
        {
            Assert.NotNull(s.Action);
            Assert.Null(s.Macro);
        });
    }

    // [2026-09-17, superseded: slots 7/8/9 used to hold the three
    // single-system "Power to X" preset macros (pip-preset-shields/-engines/
    // -weapons), now retired. The commander's direct instruction puts three
    // combo macros there instead - Shields+Engines, Engines+Shields,
    // Weapons+Engines.]
    [Theory]
    [InlineData(7, "pip-preset-shields-engines")]
    [InlineData(8, "pip-preset-engines-shields")]
    [InlineData(9, "pip-preset-weapons-engines")]
    [InlineData(25, "srv-launch")]
    public void Load_ShipPage_PipAndSrvLaunchSlots_NameTheirMacros_NotActions(int index, string expectedMacro)
    {
        var layout = StarterLayout.Load();
        var slot = layout.Pages[0].Slots.Single(s => s.Index == index);

        Assert.Null(slot.Action);
        Assert.Equal(expectedMacro, slot.Macro);
    }

    [Theory]
    [InlineData(26, "DeployHardpointToggle")]
    [InlineData(27, "ToggleFlightAssist")]
    public void Load_ShipPage_NewActionSlots_NameTheirActions_NotMacros(int index, string expectedAction)
    {
        var layout = StarterLayout.Load();
        var slot = layout.Pages[0].Slots.Single(s => s.Index == index);

        Assert.Null(slot.Macro);
        Assert.Equal(expectedAction, slot.Action);
    }

    [Fact]
    public void Load_TheShipPage_DeclaresInMainShip()
    {
        // [2026-09-09, superseded: this used to be
        // Load_TheShipPage_StaysContextFree, asserting Assert.Null(ShowWhen)
        // - "deliberate, and the reason automatic switching is one-way
        // today". The commander ruled switching must go both ways ("yes we
        // definitely need to go two ways and have it auto switch back to the
        // ship"), which is exactly the case that old comment named as
        // undecided. The ship page is no longer context-free.]
        var layout = StarterLayout.Load();

        Assert.Equal(new[] { "InMainShip" }, layout.Pages[0].ShowWhen);
    }

    // -------------------------------------------------------------------
    // Whole-layout sweeps
    // -------------------------------------------------------------------

    [Fact]
    public void Load_ReturnsSixPages_InTheDeclaredOrder()
    {
        var layout = StarterLayout.Load();

        Assert.Equal(
            new[] { "SHIP", "SRV", "NOMAD", "RHINO", "FIGHTER", "ON FOOT" },
            layout.Pages.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Load_NoPageParksAnything()
    {
        var layout = StarterLayout.Load();

        Assert.All(layout.Pages, p => Assert.Empty(p.Parked));
    }

    [Fact]
    public void Load_EveryActionSlot_OnEveryPage_NamesARealCuratedAction()
    {
        // [2026-09-09: widened from the ship page to every page. An
        // on-foot page is the reason this matters more than it used to -
        // Humanoid* elements were entirely absent from the curated
        // catalogue before this task, and an uncurated action renders with
        // Prettifier's fallback label ("Humanoid Toggle Flashlight
        // Button"), which no button on this panel can show.]
        var layout = StarterLayout.Load();
        var catalogue = LunaPanel.Server.Catalogue.CatalogueLoader.LoadShipped();

        var actionNames = layout.Pages
            .SelectMany(p => p.Slots.Concat(p.Parked))
            .Where(s => s.Action is not null)
            .Select(s => s.Action!);

        Assert.All(actionNames, name => Assert.True(catalogue.Actions.ContainsKey(name), $"'{name}' is not a curated action."));
    }

    [Fact]
    public void Load_EveryMacroSlot_OnEveryPage_NamesAShippedMacro()
    {
        var layout = StarterLayout.Load();
        var shippedIds = LunaPanel.Server.Macros.MacroLoader.LoadShipped().Select(m => m.Id).ToHashSet(StringComparer.Ordinal);

        var macroIds = layout.Pages
            .SelectMany(p => p.Slots.Concat(p.Parked))
            .Where(s => s.Macro is not null)
            .Select(s => s.Macro!);

        Assert.All(macroIds, id => Assert.True(shippedIds.Contains(id), $"'{id}' is not a shipped macro id."));
    }

    [Fact]
    public void Load_NoSlotOnAnyPage_CarriesAUserLabelOverride()
    {
        var layout = StarterLayout.Load();

        Assert.All(layout.Pages.SelectMany(p => p.Slots.Concat(p.Parked)), s => Assert.Null(s.Label));
    }

    [Fact]
    public void Load_PassesLayoutValidatorForLoad()
    {
        var layout = StarterLayout.Load();
        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Load_EveryDeclaredShowWhen_ParsesThroughTheRealGrammar()
    {
        // A showWhen token nothing can parse is skipped with a warning by
        // ContextPageSelector, which would make a shipped page silently
        // never appear. Shipped content gets to fail here instead.
        var layout = StarterLayout.Load();

        foreach (var page in layout.Pages.Where(p => p.ShowWhen is { Count: > 0 }))
        {
            var exception = Record.Exception(() => ShowWhenConditionList.Parse(page.ShowWhen!));
            Assert.Null(exception);
        }
    }

    // -------------------------------------------------------------------
    // The context pages (ref/docs/vessel-context.md, O33)
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("SRV", "InSrv", "Vessel:testbuggy")]
    [InlineData("NOMAD", "InSrv", "Vessel:lander01")]
    [InlineData("FIGHTER", "InFighter")]
    [InlineData("ON FOOT", "OnFoot")]
    public void Load_EachContextPage_DeclaresItsOwnShowWhen(string pageName, params string[] expected)
    {
        var layout = StarterLayout.Load();
        var page = PageNamed(layout, pageName);

        Assert.Equal(expected, page.ShowWhen);
    }

    [Fact]
    public void Load_TheSrvAndNomadPages_AreSeparateContexts_NotOneSharedSrvPage()
    {
        // The commander was explicit that the Scarab and the Nomad are
        // different vehicles that play differently and must not be
        // collapsed (ref/docs/vessel-context.md's "Which SRV needs the
        // journal"). Both name InSrv; the Vessel: term is the whole
        // difference, so a page that dropped it would swallow the other.
        var layout = StarterLayout.Load();

        Assert.Contains("Vessel:testbuggy", PageNamed(layout, "SRV").ShowWhen!);
        Assert.Contains("Vessel:lander01", PageNamed(layout, "NOMAD").ShowWhen!);
        Assert.NotEqual(PageNamed(layout, "SRV").ShowWhen, PageNamed(layout, "NOMAD").ShowWhen);
    }

    [Fact]
    public void Load_TheRhinoPage_CarriesOnlyRhinoDisembark()
    {
        // [2026-09-16, superseded: this was
        // Load_TheRhinoPage_ExistsAndIsCompletelyEmpty, asserting
        // Assert.Empty(rhino.Slots) - true at the time ("Rhino = blank page"
        // was the commander's original instruction, because the page had no
        // way to disembark at all). The commander then asked for exit-to-foot
        // parity with SRV/NOMAD despite RHINO's context-free status (see
        // Load_TheRhinoPage_IsContextFree_... below, unaffected - only the
        // slot list changed, not showWhen). rhino-disembark uses the same
        // proven FocusRadarPanel_Buggy/UI_Right/UI_Select sequence as
        // nomad-disembark and srv-disembark.]
        var layout = StarterLayout.Load();
        var rhino = PageNamed(layout, "RHINO");

        var slot = Assert.Single(rhino.Slots);
        Assert.Equal(0, slot.Index);
        Assert.Equal("rhino-disembark", slot.Macro);
        Assert.Null(slot.Action);
        Assert.Empty(rhino.Parked);
    }

    [Fact]
    public void Load_TheRhinoPage_IsContextFree_BecauseNoJournalHasEverNamedARhino()
    {
        // NOT an oversight, and not the same statement as the other four
        // pages. Every SRV type this project has ever seen is one of
        // testbuggy (Scarab), lander01 (Nomad) or combat_multicrew_srv_01
        // (Scorpion) - measured over 226 of the commander's journals on
        // 2026-09-09, in which the string "rhino" appears nowhere. There is
        // therefore no Vessel: token that can be written here without
        // inventing one, and an invented token produces a page that silently
        // never appears. Context-free is the honest state of the knowledge:
        // the page exists, it is reachable by hand from the tab row, and it
        // is never switched TO. See ref/docs/vessel-context.md's R5 and
        // tests/notes/open-items.md's O33.
        var layout = StarterLayout.Load();
        var rhino = PageNamed(layout, "RHINO");

        Assert.True(rhino.ShowWhen is null || rhino.ShowWhen.Count == 0);
    }

    [Fact]
    public void Load_TheNomadPage_CarriesNomadDisembarkAndDockLaunch_AndNothingElse()
    {
        // [2026-09-09, superseded: this used to be
        // Load_TheNomadPage_CarriesDisembarkAndRequestDocking_AndNothingElse,
        // pinning slot 1 as "request-docking". That macro presses
        // FocusLeftPanel - the ship's left-panel binding, which does nothing
        // from an SRV driver's seat. nomad-dock-launch is the contextual
        // toggle built for exactly this (ref/docs/vessel-context.md).]
        //
        // [2026-09-09, superseded again, same day: this was
        // Load_TheNomadPage_CarriesDisembarkAndNomadDockLaunch_AndNothingElse,
        // asserting slot 0 named "disembark" - the SHIP's own disembark
        // macro, which requires Docked and refuses from inside an SRV
        // ("require [Docked,GuiFocus:NoFocus] was not satisfied", reported
        // live). Slot 0 now names "nomad-disembark", built specifically for
        // the SRV's own role panel (FocusRadarPanel_Buggy/UI_Right/
        // UI_Select). A new "enter-cockpit" macro is added at slot 2.]
        //
        // [2026-09-16, superseded: "enter-cockpit" moved off this page. The
        // commander clarified its real purpose directly - it's an on-foot
        // action used to enter any craft, ship included, and should only
        // ever show up on ON FOOT. See Load_TheOnFootPage_... below.]
        var layout = StarterLayout.Load();
        var nomad = PageNamed(layout, "NOMAD");

        Assert.Equal(
            new[] { "nomad-disembark", "nomad-dock-launch" },
            nomad.Slots.OrderBy(s => s.Index).Select(s => s.Macro).ToArray());
        Assert.All(nomad.Slots, s => Assert.Null(s.Action));
    }

    [Fact]
    public void Load_TheSrvPage_CarriesTheMainSrvControls_EveryOneOfThemKeyboardBindableAndCurated()
    {
        // The commander's brief was "the main srv controls by default, we'll
        // alter from there" - so this list is a starting point they are
        // expected to tune, and a change to it should land here as a
        // deliberate edit rather than pass unnoticed. Chosen 2026-09-09 from
        // the curated srv category plus two shared ship actions that work
        // from the driver's seat, restricted to actions that resolve to a
        // KEYBOARD chord: BuggyPrimaryFireButton, BuggySecondaryFireButton,
        // IncreaseSpeedButtonMax and DecreaseSpeedButtonMax are all bound to
        // mouse buttons or the wheel, which LunaPanel cannot press
        // (UnboundReason.NonKeyboardOnly).
        //
        // [2026-09-12, superseded: the page grew from 8 slots to 16 and its
        // template from t9 to t18. Slots 8-15 are the eight SRV panel-access
        // buggy variants curated in the same task
        // (ref/docs/vessel-context.md's "The SRV page, and why these eight")
        // - the SRV's OWN FocusLeftPanel_Buggy etc, not the ship's, because
        // pressing the ship's own panel keys from the driver's seat does
        // nothing (the same finding that already justified curating
        // FocusRadarPanel_Buggy for the Nomad's disembark macro). The
        // original eight (indices 0-7) are unchanged.]
        var layout = StarterLayout.Load();
        var srv = PageNamed(layout, "SRV");

        Assert.Equal("t18", srv.TemplateId);
        Assert.Equal(
            new[]
            {
                "AutoBreakBuggyButton",
                "ToggleDriveAssist",
                "ToggleBuggyTurretButton",
                "HeadlightsBuggyButton",
                "NightVisionToggle",
                "BuggyCycleFireGroupNext",
                "ToggleCargoScoop_Buggy",
                "RecallDismissShip",
                "FocusLeftPanel_Buggy",
                "FocusRightPanel_Buggy",
                "GalaxyMapOpen_Buggy",
                "SystemMapOpen_Buggy",
                "UIFocus_Buggy",
                "PlayerHUDModeToggle_Buggy",
                "OpenCodexGoToDiscovery_Buggy",
                "PhotoCameraToggle_Buggy",
            },
            srv.Slots.Where(s => s.Action is not null).OrderBy(s => s.Index).Select(s => s.Action).ToArray());

        // [2026-09-16: slot 16 was previously-orphaned macro srv-board-ship,
        // shipped but never placed on any default page - see ref/docs/macros.md.]
        var slot16 = srv.Slots.Single(s => s.Index == 16);
        Assert.Null(slot16.Action);
        Assert.Equal("srv-board-ship", slot16.Macro);

        // [2026-09-16: slot 17 added - the SRV page had no way to disembark
        // at all. srv-disembark uses the same proven
        // FocusRadarPanel_Buggy/UI_Right/UI_Select sequence as
        // nomad-disembark, applied to the standard SRV/Scarab.]
        var slot17 = srv.Slots.Single(s => s.Index == 17);
        Assert.Null(slot17.Action);
        Assert.Equal("srv-disembark", slot17.Macro);
    }

    [Fact]
    public void Load_TheOnFootPage_IsPopulated_AndEveryActionIsCuratedUnderTheOnFootCategory()
    {
        // On foot is a separate binding world: none of the ship's actions
        // reach it, so every slot here has to be a Humanoid* element. The
        // category check is what stops a ship action being dropped onto this
        // page later, where it would look plausible and do nothing.
        //
        // [2026-09-16, superseded: "enter-cockpit" moved here from NOMAD (the
        // commander's own words: "used to enter into all craft, including
        // the ship when needed... it should only show up in On Foot"). The
        // page grew from t12 (all 12 slots full) to t18 to make room for it
        // at slot 12 - a macro, so the blanket "every slot is a curated
        // onfoot action" sweep now excludes it explicitly.]
        var layout = StarterLayout.Load();
        var onFoot = PageNamed(layout, "ON FOOT");
        var catalogue = LunaPanel.Server.Catalogue.CatalogueLoader.LoadShipped();

        Assert.Equal("t18", onFoot.TemplateId);
        Assert.Equal(13, onFoot.Slots.Count);

        var actionSlots = onFoot.Slots.Where(s => s.Index != 12);
        Assert.All(actionSlots, s =>
        {
            Assert.NotNull(s.Action);
            Assert.Null(s.Macro);
            Assert.Equal("onfoot", catalogue.Actions[s.Action!].Category);
        });

        var slot12 = onFoot.Slots.Single(s => s.Index == 12);
        Assert.Null(slot12.Action);
        Assert.Equal("enter-cockpit", slot12.Macro);
    }

    [Fact]
    public void Load_TheFighterPage_CarriesRequestDockingAndTheLeftPanel_AndNothingElse()
    {
        var layout = StarterLayout.Load();
        var fighter = PageNamed(layout, "FIGHTER");

        Assert.Equal(2, fighter.Slots.Count);
        Assert.Equal("request-docking", fighter.Slots.Single(s => s.Index == 0).Macro);
        Assert.Equal("FocusLeftPanel", fighter.Slots.Single(s => s.Index == 1).Action);
    }

    // -------------------------------------------------------------------
    // Boarding each vessel, driven through the real resolver and the real
    // selector against the real shipped layout - no hand-built pages.
    // -------------------------------------------------------------------

    public static TheoryData<string, uint, uint, string?, int?> BoardingCases() => new()
    {
        // name                       flags               flags2  journal vessel type         expected page index
        { "the Scarab",               1u << InSrvBit,     0u,     "testbuggy",                1 },
        { "the Scarab, LoadGame's spelling", 1u << InSrvBit, 0u,  "TestBuggy",                1 },
        { "the Nomad",                1u << InSrvBit,     0u,     "lander01",                 2 },
        { "the Nomad, LoadGame's spelling",  1u << InSrvBit, 0u,  "Lander01",                 2 },
        { "a fighter",                1u << InFighterBit, 0u,     null,                       4 },
        { "on foot",                  0u,                 1u << OnFootBit2, null,             5 },
        // [2026-09-09, superseded: this row used to expect null - "the main
        // ship's page is context-free, so coming home matches nothing and
        // the panel stays where it is - the one-way switch this task
        // deliberately did not change." The ship page now declares
        // InMainShip, so docking back from an SRV/fighter/on-foot returns
        // the commander to page 0, same as any other context.]
        { "the main ship",            1u << InMainShipBit, 0u,    null,                       0 },
        // The Scorpion is a real SRV the commander owns and no shipped page
        // names it. This is the pin that stops the RHINO page from ever
        // being turned into an "any other SRV" catch-all: if it were, this
        // case would land on a blank page instead of staying put.
        { "the Scorpion",             1u << InSrvBit,     0u,     "combat_multicrew_srv_01",  null },
        // The multi-part-journal gap (O24): InSrv with no vessel type known.
        { "an SRV the journal has not named", 1u << InSrvBit, 0u, null,                       null },
    };

    [Theory]
    [MemberData(nameof(BoardingCases))]
    public void Boarding_TheRealSelectorOverTheRealStarterLayout_ChoosesTheExpectedPage(
        string what, uint flags, uint flags2, string? lastKnownVesselType, int? expectedPageIndex)
    {
        var layout = StarterLayout.Load();
        var snapshot = Snapshot(flags, flags2);
        var context = VesselContextResolver.Resolve(snapshot, lastKnownVesselType);
        var log = new CapturingDiagnosticLog();

        var index = ContextPageSelector.Select(layout, snapshot, context, log);

        Assert.Equal(expectedPageIndex, index);
        // A skipped page would mean a shipped showWhen nothing can parse.
        Assert.DoesNotContain(log.Events, e => e.Level == DiagnosticLevel.Warn);
        _ = what; // the case's own label, carried so a failure names the vessel
    }

    /// <summary>
    /// 2026-09-09: the commander's second ruling - "yes we definitely need
    /// to go two ways and have it auto switch back to the ship." Driven
    /// through the real <see cref="AutoPageSwitcher"/> over the real shipped
    /// layout, not just the selector: boarding the Scarab switches away from
    /// the ship page, and docking it again switches back, exactly like any
    /// other context change round-trip.
    /// </summary>
    [Fact]
    public void Boarding_TheRealSwitcherOverTheRealStarterLayout_DockingReturnsToTheShipPage()
    {
        var layout = StarterLayout.Load();
        var log = new CapturingDiagnosticLog();
        var inShip = Snapshot(1u << InMainShipBit);
        var inSrv = Snapshot(1u << InSrvBit);
        var switcher = new AutoPageSwitcher(VesselContextResolver.Resolve(inShip, null));

        var boarded = switcher.Decide(layout, currentPageIndex: 0, inSrv, "testbuggy", log);
        Assert.Equal(1, boarded);

        var docked = switcher.Decide(layout, currentPageIndex: boarded!.Value, inShip, "testbuggy", log);
        Assert.Equal(0, docked);

        Assert.DoesNotContain(log.Events, e => e.Level == DiagnosticLevel.Warn);
    }

    // -------------------------------------------------------------------
    // The commander's own boarding sequence, 2026-09-10.
    // -------------------------------------------------------------------

    private static JournalEvent JournalLine(string name, params (string Key, string Value)[] strings) =>
        new(name, null, strings.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));

    /// <summary>
    /// <b>The commander's own sequence, replayed through the real store, the
    /// real resolver, the real switcher and the real shipped layout.</b>
    /// Their words: <em>"SRV switched to the Nomad page on first try, but on
    /// second try it switched to the SRV page properly."</em>
    ///
    /// <para>The five lines below are transcribed from their journal,
    /// <c>Journal.2026-09-09T194232.01.log</c>, with the timestamps the game
    /// wrote:
    /// <c>01:36:45 LaunchVessel lander01</c>,
    /// <c>01:58:34 DockSRV lander01</c>,
    /// <c>02:00:15 LaunchSRV testbuggy</c>,
    /// <c>02:00:51 DockSRV testbuggy</c>,
    /// <c>02:01:21 LaunchSRV testbuggy</c>.</para>
    ///
    /// <para>The third line is the one that was invisible. Counted over the
    /// commander's whole journal history: <c>LaunchVessel</c> is written for
    /// <c>lander01</c> and for nothing else (33 times), while
    /// <c>LaunchSRV</c> is written only for <c>testbuggy</c> (15) and
    /// <c>combat_multicrew_srv_01</c> (8). Only the Nomad launches with the
    /// spelling this code knew, so boarding the Scarab named no vessel at
    /// all and the last one the journal had named - the Nomad, from line two
    /// - still won. The fifth line worked only because the fourth
    /// (<c>DockSRV testbuggy</c>) had named the Scarab in between, which is
    /// exactly why the second try succeeded and the first did not.</para>
    ///
    /// <para>The final assertion is the load-bearing one: <b>five switches,
    /// one per boarding, and no correction.</b> A fix that switched to NOMAD
    /// and then to SRV would satisfy every page assertion above it and still
    /// be the failure <c>ref/docs/vessel-context.md</c>'s never-switch-under
    /// -a-moving-thumb rule exists to prevent.</para>
    /// </summary>
    [Fact]
    public void Boarding_TheCommandersOwnJournalSequence_TheScarabAfterTheNomad_LandsOnTheSrvPageFirstTry()
    {
        var layout = StarterLayout.Load();
        var log = new CapturingDiagnosticLog();
        var journal = new JournalStateStore();
        var inShip = Snapshot(1u << InMainShipBit);
        var inSrv = Snapshot(1u << InSrvBit);

        var status = inShip;
        var page = 0;
        var switches = new List<int>();
        var switcher = new AutoPageSwitcher(VesselContextResolver.Resolve(status, journal.CurrentVesselType));

        void Push()
        {
            var decided = switcher.Decide(layout, page, status, journal.CurrentVesselType, log);
            if (decided is null)
            {
                return;
            }

            switches.Add(decided.Value);
            page = decided.Value;
        }

        // One journal line and one Status.json rewrite, pushed the way
        // ServerHostBuilder pushes them: OnJournalChanged fires against the
        // snapshot already in hand, then OnChanged fires with the new one.
        void Fly(JournalEvent line, StatusSnapshot next)
        {
            journal.Record(line);
            Push();
            status = next;
            Push();
        }

        Fly(JournalLine("LaunchVessel", ("VesselType", "lander01"), ("VesselType_Localised", "Nomad")), inSrv);
        Assert.Equal(2, page); // NOMAD

        Fly(JournalLine("DockSRV", ("SRVType", "lander01"), ("SRVType_Localised", "Nomad")), inShip);
        Assert.Equal(0, page); // SHIP

        Fly(JournalLine("LaunchSRV", ("SRVType", "testbuggy"), ("SRVType_Localised", "SRV Scarab")), inSrv);
        Assert.Equal(1, page); // SRV, first try - this is the reported defect

        Fly(JournalLine("DockSRV", ("SRVType", "testbuggy"), ("SRVType_Localised", "SRV Scarab")), inShip);
        Assert.Equal(0, page); // SHIP

        Fly(JournalLine("LaunchSRV", ("SRVType", "testbuggy"), ("SRVType_Localised", "SRV Scarab")), inSrv);
        Assert.Equal(1, page); // SRV, second try - the one that did work

        Assert.Equal(new[] { 2, 0, 1, 0, 1 }, switches);
        Assert.DoesNotContain(log.Events, e => e.Level == DiagnosticLevel.Warn);
    }

    /// <summary>
    /// <b>The same boarding with the two files observed in the other order:
    /// <c>Status.json</c> flips <c>InSrv</c> first and the launch line is
    /// read a moment later.</b> Both are file watchers with their own
    /// debounce, and nothing here decides which of the game's two writes
    /// lands first - so this ordering has to be survivable rather than
    /// merely unlikely.
    ///
    /// <para>The commander drove the Nomad before this, so "whatever the
    /// journal last named" is <c>lander01</c>. If that survived being
    /// docked, this ordering would put them on the NOMAD page and then
    /// correct itself to the SRV page a moment later - a page that changes
    /// twice under a thumb already moving, which is the one failure
    /// <c>ref/docs/vessel-context.md</c> says decides whether this feature is
    /// liked or hated. <b>The assertion that matters is the switch list: one
    /// entry, and it is the right page.</b></para>
    ///
    /// <para>The intermediate state is not "nothing happened" - it is
    /// <c>Srv</c> with no vessel type, which matches no page, so the
    /// commander stays on the ship's page until the journal says which SRV.
    /// If it never says (a journal part with no <c>LoadGame</c> at its top,
    /// O24), they simply stay there; that is the "no page matches" rule
    /// doing its job, not a hang.</para>
    /// </summary>
    [Fact]
    public void Boarding_TheScarabWithTheFlagSeenBeforeTheJournalLine_SwitchesOnce_AndOnlyToTheSrvPage()
    {
        var layout = StarterLayout.Load();
        var log = new CapturingDiagnosticLog();
        var journal = new JournalStateStore();
        var inShip = Snapshot(1u << InMainShipBit);
        var inSrv = Snapshot(1u << InSrvBit);

        // The commander's last outing: the Nomad, launched and docked again.
        journal.Record(JournalLine("LaunchVessel", ("VesselType", "lander01"), ("VesselType_Localised", "Nomad")));
        journal.Record(JournalLine("DockSRV", ("SRVType", "lander01"), ("SRVType_Localised", "Nomad")));

        var page = 0;
        var switches = new List<int>();
        var switcher = new AutoPageSwitcher(VesselContextResolver.Resolve(inShip, journal.CurrentVesselType));

        void Push(StatusSnapshot status)
        {
            var decided = switcher.Decide(layout, page, status, journal.CurrentVesselType, log);
            if (decided is null)
            {
                return;
            }

            switches.Add(decided.Value);
            page = decided.Value;
        }

        // The flag arrives first: in an SRV, and the journal has not said
        // which one.
        Push(inSrv);
        Assert.Equal(0, page);
        Assert.Empty(switches);

        // ... and then the launch line.
        journal.Record(JournalLine("LaunchSRV", ("SRVType", "testbuggy"), ("SRVType_Localised", "SRV Scarab")));
        Push(inSrv);

        Assert.Equal(new[] { 1 }, switches);
        Assert.DoesNotContain(log.Events, e => e.Level == DiagnosticLevel.Warn);
    }

    // -------------------------------------------------------------------
    // LoadForDeviceClass - device-size-aware seeding (2026-09-16).
    // -------------------------------------------------------------------

    [Fact]
    public void LoadForDeviceClass_Phone_ReturnsTheSameLayoutAsLoad()
    {
        var byClass = StarterLayout.LoadForDeviceClass("phone");
        var byLoad = StarterLayout.Load();

        Assert.Equal(byLoad.Pages.Select(p => p.Name), byClass.Pages.Select(p => p.Name));
        Assert.Equal("t30", byClass.Pages[0].TemplateId);
    }

    [Fact]
    public void LoadForDeviceClass_Unknown_FallsBackToThePhoneVariant()
    {
        // The safe default: a too-small grid on an actual tablet is a minor
        // inconvenience, while a 64-slot grid on an actually-small unknown
        // screen could be unusable.
        var byClass = StarterLayout.LoadForDeviceClass("unknown");

        Assert.Equal("t30", byClass.Pages[0].TemplateId);
    }

    [Fact]
    public void LoadForDeviceClass_Tablet_ReturnsTheT64ShipPage()
    {
        var layout = StarterLayout.LoadForDeviceClass("tablet");

        Assert.Equal("SHIP", layout.Pages[0].Name);
        Assert.Equal("t64", layout.Pages[0].TemplateId);
    }

    [Fact]
    public void LoadForDeviceClass_Tablet_HasFiftyOccupiedSlots_GroupedByFunction_NoneStraddlingARowBoundary()
    {
        // [2026-09-16, superseded: this used to be
        // LoadForDeviceClass_Tablet_KeepsTheOriginalTwentySixShipSlots_PlusNineNewOnes,
        // pinning 35 slots scattered by the order features were added rather
        // than by function. The commander asked for a full reorg (49
        // existing buttons plus the new Confirm button = 50) into
        // functional groups, each packed into contiguous grid cells that
        // never straddle a row boundary (t64 = 8 cols x 8 rows; row =
        // index/8, col = index%8) - see
        // expressive-kindling-starfish.md's Part D. This test replaces the
        // old scattered-index pins with the new grouped ones.
        // [2026-09-17, superseded: slots 20-22 used to hold the three
        // single-system "Power to X" preset macros (pip-preset-shields/
        // -engines/-weapons), now retired. They're replaced with the three
        // new combo macros (Weapons+Engines, Engines+Shields,
        // Weapons+Shields), giving a contiguous block of all six combos at
        // slots 20-25 alongside the three combos already at 23-25.]
        // [2026-09-17, superseded again, same day: the six combos were
        // reordered to group each primary's two variants together
        // (Engines-primary pair, then Shields-primary pair, then
        // Weapons-primary pair, alphabetical within each pair), rather than
        // sitting in whatever order they happened to be built.]
        // [2026-09-17, superseded again, same day: was 50 - slot 41 (Landing
        // Gear) is now the commander's own "Prepare to Dock" macro (promoted
        // from a live personal user macro to a shipped default), and plain
        // Landing Gear moved to the new slot 52 to make room. 50 + 1 = 51.]
        var layout = StarterLayout.LoadForDeviceClass("tablet");
        var ship = layout.Pages[0];

        Assert.Equal(51, ship.Slots.Count);

        var expected = new Dictionary<int, (string? Action, string? Macro)>
        {
            // Panels / pages / tabs - one full row.
            [0] = ("FocusLeftPanel", null),
            [1] = ("FocusRightPanel", null),
            [2] = ("FocusCommsPanel", null),
            [3] = ("FocusRadarPanel", null),
            [4] = ("CycleNextPanel", null),
            [5] = ("CyclePreviousPanel", null),
            [6] = ("CycleNextPage", null),
            [7] = ("CyclePreviousPage", null),

            // Navigation / travel - one full row.
            [8] = ("Supercruise", null),
            [9] = ("Hyperspace", null),
            [10] = ("GalaxyMapOpen", null),
            [11] = ("SystemMapOpen", null),
            [12] = ("TargetNextRouteSystem", null),
            [13] = ("SetSpeed75", null),
            [14] = ("ExplorationFSSEnter", null),
            [15] = ("ExplorationFSSQuit", null),

            // Power / pips - one full row plus two into the next.
            [16] = ("ResetPowerDistribution", null),
            [17] = ("IncreaseSystemsPower", null),
            [18] = ("IncreaseEnginesPower", null),
            [19] = ("IncreaseWeaponsPower", null),
            [20] = (null, "pip-preset-engines-shields"),
            [21] = (null, "pip-preset-engines-weapons"),
            [22] = (null, "pip-preset-shields-engines"),
            [23] = (null, "pip-preset-shields-weapons"),
            [24] = (null, "pip-preset-weapons-engines"),
            [25] = (null, "pip-preset-weapons-shields"),

            // Thrust (hold) - one adjacent block within a single row.
            [26] = ("UpThrustButton", null),
            [27] = ("DownThrustButton", null),
            [28] = ("LeftThrustButton", null),
            [29] = ("RightThrustButton", null),

            // Combat / targeting - one full row.
            [32] = ("PrimaryFire", null),
            [33] = ("SecondaryFire", null),
            [34] = ("CycleNextHostileTarget", null),
            [35] = ("SelectHighestThreat", null),
            [36] = ("DeployHardpointToggle", null),
            [37] = ("ToggleFlightAssist", null),
            [38] = ("OrbitLinesToggle", null),
            [39] = ("WingNavLock", null),

            // Docking / ship utility - one full row plus one into the next.
            [40] = (null, "request-docking"),
            [41] = (null, "prepare-to-dock"),
            [42] = ("ToggleCargoScoop", null),
            [43] = ("NightVisionToggle", null),
            [44] = ("ShipSpotLightToggle", null),
            [45] = ("DeployHeatSink", null),
            [46] = (null, "nomad-dock-launch"),
            [47] = (null, "srv-launch"),
            [48] = (null, "disembark"),

            // Misc / HUD.
            [49] = ("PlayerHUDModeToggle", null),
            [50] = ("TriggerColonisationModule", null),
            [51] = ("UI_Select", null),

            // Landing Gear moved here from slot 41 to make room for
            // prepare-to-dock, which took over slot 41's position.
            [52] = ("LandingGearToggle", null),
        };

        Assert.Equal(expected.Count, ship.Slots.Count);

        foreach (var (index, (expectedAction, expectedMacro)) in expected)
        {
            var slot = ship.Slots.Single(s => s.Index == index);
            Assert.Equal(expectedAction, slot.Action);
            Assert.Equal(expectedMacro, slot.Macro);
        }

        // The thrust block is the only group not confined to a single "own"
        // row by the plan's diagram (it's placed mid-row, cols 2-5 of row 3)
        // - pin that every one of its occupied indices genuinely shares
        // that one row (row = index/8), so the block never straddles a row
        // boundary even though it doesn't start at column 0.
        var thrustIndices = new[] { 26, 27, 28, 29 };
        Assert.All(thrustIndices, index => Assert.Equal(thrustIndices[0] / 8, index / 8));

        // Every OTHER group's occupied indices likewise stay within a
        // single row, or - where a group's size exceeds 8 - its remainder
        // begins cleanly at column 0 of the next row rather than mid-row.
        int[][] singleRowGroups =
        {
            new[] { 0, 1, 2, 3, 4, 5, 6, 7 },       // panels
            new[] { 8, 9, 10, 11, 12, 13, 14, 15 }, // navigation
            new[] { 16, 17, 18, 19, 20, 21, 22, 23 }, // power, first 8
            new[] { 24, 25 },                       // power, remainder
            new[] { 32, 33, 34, 35, 36, 37, 38, 39 }, // combat
            new[] { 40, 41, 42, 43, 44, 45, 46, 47 }, // docking, first 8
            new[] { 48 },                           // docking, remainder
            new[] { 49, 50, 51 },                   // misc
            new[] { 52 },                           // Landing Gear, moved from slot 41
        };
        foreach (var group in singleRowGroups)
        {
            Assert.All(group, index => Assert.Equal(group[0] / 8, index / 8));
        }
    }

    [Fact]
    public void LoadForDeviceClass_BothVariants_ShareTheFiveNonShipPagesByteForByte()
    {
        var phone = StarterLayout.LoadForDeviceClass("phone");
        var tablet = StarterLayout.LoadForDeviceClass("tablet");

        foreach (var name in new[] { "SRV", "NOMAD", "RHINO", "FIGHTER", "ON FOOT" })
        {
            var phonePage = PageNamed(phone, name);
            var tabletPage = PageNamed(tablet, name);

            Assert.Equal(phonePage.TemplateId, tabletPage.TemplateId);
            Assert.Equal(phonePage.ShowWhen, tabletPage.ShowWhen);
            Assert.Equal(
                phonePage.Slots.OrderBy(s => s.Index).Select(s => (s.Index, s.Action, s.Macro)),
                tabletPage.Slots.OrderBy(s => s.Index).Select(s => (s.Index, s.Action, s.Macro)));
        }
    }

    [Fact]
    public void LoadForDeviceClass_Tablet_PassesLayoutValidatorForLoad()
    {
        var layout = StarterLayout.LoadForDeviceClass("tablet");
        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }
}
