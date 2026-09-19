using LunaPanel.Server.Tray;

namespace LunaPanel.Tests.Tray;

/// <summary>
/// Pins the tray Status window's action buttons - what exists, their
/// labels, their order, and that none of them carries spacing of its own -
/// independent of any WinForms type, per
/// <see cref="TrayStatusWindowActions"/>'s own remarks.
/// </summary>
public class TrayStatusWindowActionsTests
{
    private const int BoundHostPort = 51824;

    [Fact]
    public void ButtonsFor_ListsEveryTrayMenuItem_InTheMenusOwnOrder()
    {
        var actions = TrayStatusWindowActions.ButtonsFor(BoundHostPort).Select(b => b.Action);

        // [2026-09-17] Superseded to add TrayAction.About, grouped with
        // Status/Ports before the separator/Quit pair - a genuine new tray
        // menu item (the About window's manual Elite/EDHM path fallback),
        // not a weakening of this pin. Previous expected list ended
        // "..., TrayAction.Ports, TrayAction.Quit," with no About entry.
        //
        // [2026-09-17] Superseded again to add TrayAction.CheckForUpdates
        // after About - a genuine new tray menu item (the self-update
        // check), same kind of addition as About was. Previous expected list
        // ended "..., TrayAction.Ports, TrayAction.About, TrayAction.Quit,"
        // with no CheckForUpdates entry.
        Assert.Equal(
            new[]
            {
                TrayAction.AddDevice,
                TrayAction.Devices,
                TrayAction.MacroBuilder,
                TrayAction.Transfer,
                TrayAction.MacroTiming,
                TrayAction.EditLivePanels,
                TrayAction.Ports,
                TrayAction.About,
                TrayAction.CheckForUpdates,
                TrayAction.Quit,
            },
            actions);
    }

    /// <summary>
    /// The labels are not re-typed here: a button reading anything other
    /// than what the same commander saw on the right-click menu is the
    /// failure this whole seam exists to prevent, so the three PC-only
    /// buttons must read their wording from the very constants the menu
    /// items use.
    /// </summary>
    [Fact]
    public void ButtonsFor_LabelsMatchTheTrayMenusOwnWording()
    {
        var buttons = TrayStatusWindowActions.ButtonsFor(BoundHostPort);

        // [2026-09-17] Superseded: About inserted at index 7, Quit moved
        // from index 7 to 8 - see ButtonsFor_ListsEveryTrayMenuItem_InTheMenusOwnOrder's
        // own superseded note for why this is a genuine addition.
        //
        // [2026-09-17] Superseded again: "Check for updates" inserted at
        // index 8, Quit moved from index 8 to 9. Previously this method
        // asserted `Assert.Equal("Quit", buttons[8].Label);` as its last
        // line, with no index 8 "Check for updates" assertion at all.
        Assert.Equal("Add a device", buttons[0].Label);
        Assert.Equal("Devices", buttons[1].Label);
        Assert.Equal(TrayMacroBuilder.MenuLabel, buttons[2].Label);
        Assert.Equal(TrayTransfer.MenuLabel, buttons[3].Label);
        Assert.Equal(TrayMacroTiming.MenuLabel, buttons[4].Label);
        Assert.Equal(TrayEditLivePanels.MenuLabel, buttons[5].Label);
        Assert.Equal("Ports", buttons[6].Label);
        Assert.Equal("General", buttons[7].Label);
        Assert.Equal("Check for updates", buttons[8].Label);
        Assert.Equal("Quit", buttons[9].Label);
    }

    /// <summary>
    /// "Option A: Windows 11 dark" (ref/docs/hosting.md, 2026-09-07): "Add a
    /// device" is the primary (accent-filled) button and everything else is
    /// secondary (outlined) - decided here so <c>StatusForm</c> only has to
    /// read <see cref="TrayActionButton.IsPrimary"/>, not infer a style from
    /// which action it is. Quit in particular must never come out primary:
    /// the accent fill is what a commander's eye lands on first.
    /// </summary>
    [Fact]
    public void ButtonsFor_OnlyAddDeviceIsPrimary()
    {
        var buttons = TrayStatusWindowActions.ButtonsFor(BoundHostPort);

        Assert.True(buttons[0].IsPrimary);
        Assert.All(buttons.Skip(1), b => Assert.False(b.IsPrimary));
    }

    /// <summary>
    /// Every button is the same size, which means no button carries an
    /// extra margin of its own. Quit briefly did - a group break echoing
    /// the menu's separator - and it only made Quit narrower than the rest.
    /// Pinned as the absence of the property, so re-adding one has to be a
    /// deliberate change to this test rather than a quiet regression.
    /// </summary>
    [Fact]
    public void TrayActionButton_CarriesNoPerButtonSpacingFlag()
    {
        var spacingFlags = typeof(TrayActionButton)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(bool) && p.Name != nameof(TrayActionButton.IsPrimary));

        Assert.Empty(spacingFlags);
    }

    /// <summary>
    /// The same gate the menu applies: with no host listener bound there is
    /// no address those four pages would answer on
    /// (<see cref="TrayMacroBuilder.Url"/>), so they are left off rather
    /// than opening something that answers nothing.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ButtonsFor_WithNoHostListener_DropsTheFourPcOnlyButtons(int hostAccessPort)
    {
        var actions = TrayStatusWindowActions.ButtonsFor(hostAccessPort).Select(b => b.Action);

        // [2026-09-17] Superseded to add TrayAction.About - it opens a
        // native dialog rather than a loopback page, so it is unaffected by
        // this gate and stays present alongside Ports/Quit. Previous
        // expected list had no About entry.
        //
        // [2026-09-17] Superseded again to add TrayAction.CheckForUpdates -
        // it reaches the internet rather than a loopback page, so the host
        // listener says nothing about it either. Previous expected list was
        // "{ AddDevice, Devices, Ports, About, Quit }".
        Assert.Equal(new[] { TrayAction.AddDevice, TrayAction.Devices, TrayAction.Ports, TrayAction.About, TrayAction.CheckForUpdates, TrayAction.Quit }, actions);
    }

    /// <summary>
    /// Unlike the four browser-opening buttons, Ports opens a native dialog
    /// rather than a loopback page, so it needs no host-listener gate and
    /// survives even when nothing was bound.
    /// </summary>
    [Fact]
    public void ButtonsFor_AlwaysOffersPorts_EvenWithNoHostListener()
    {
        Assert.Contains(TrayStatusWindowActions.ButtonsFor(0), b => b.Action == TrayAction.Ports);
        Assert.Contains(TrayStatusWindowActions.ButtonsFor(-1), b => b.Action == TrayAction.Ports);
        Assert.Contains(TrayStatusWindowActions.ButtonsFor(BoundHostPort), b => b.Action == TrayAction.Ports);
    }

    /// <summary>
    /// Same reasoning as Ports above - About opens a native dialog
    /// (AboutForm), not a loopback page, so it needs no host-listener gate.
    /// </summary>
    [Fact]
    public void ButtonsFor_AlwaysOffersAbout_EvenWithNoHostListener()
    {
        Assert.Contains(TrayStatusWindowActions.ButtonsFor(0), b => b.Action == TrayAction.About);
        Assert.Contains(TrayStatusWindowActions.ButtonsFor(-1), b => b.Action == TrayAction.About);
        Assert.Contains(TrayStatusWindowActions.ButtonsFor(BoundHostPort), b => b.Action == TrayAction.About);
    }

    /// <summary>
    /// Same reasoning as Ports and About above, with one more on top: this
    /// one reaches the public repository's releases over the internet rather
    /// than a loopback page, so whether a host listener was bound tells it
    /// nothing at all.
    /// </summary>
    [Fact]
    public void ButtonsFor_AlwaysOffersCheckForUpdates_EvenWithNoHostListener()
    {
        Assert.Contains(TrayStatusWindowActions.ButtonsFor(0), b => b.Action == TrayAction.CheckForUpdates);
        Assert.Contains(TrayStatusWindowActions.ButtonsFor(-1), b => b.Action == TrayAction.CheckForUpdates);
        Assert.Contains(TrayStatusWindowActions.ButtonsFor(BoundHostPort), b => b.Action == TrayAction.CheckForUpdates);
    }

    /// <summary>
    /// Quit survives that gate. It is the only way out of the program for a
    /// commander who never found the right-click menu - which is the entire
    /// reason it is on this window - so it can never be conditional.
    /// </summary>
    [Fact]
    public void ButtonsFor_AlwaysOffersQuit()
    {
        Assert.Contains(TrayStatusWindowActions.ButtonsFor(0), b => b.Action == TrayAction.Quit);
        Assert.Contains(TrayStatusWindowActions.ButtonsFor(BoundHostPort), b => b.Action == TrayAction.Quit);
    }

    /// <summary>
    /// This window already is the Status view; a button that opens the
    /// window you are looking at is a no-op, and is the one menu item
    /// deliberately absent here.
    /// </summary>
    [Fact]
    public void ButtonsFor_HasNoStatusAction()
    {
        Assert.DoesNotContain(
            TrayStatusWindowActions.ButtonsFor(BoundHostPort),
            b => b.Label.Contains("Status", StringComparison.OrdinalIgnoreCase));
    }
}
