namespace LunaPanel.Server.Tray;

/// <summary>
/// Which tray action a button/menu item represents. Shared between the
/// tray's right-click menu and the status window's own action buttons
/// (<c>LunaPanel.Tray.TrayApplicationContext</c>/<c>StatusForm</c>) so both
/// surfaces dispatch to the exact same code for the same action - the task
/// this exists for ("put the tray's actions on the status window") is
/// explicit that both must call the same code, not two copies of it.
/// </summary>
public enum TrayAction
{
    AddDevice,
    Devices,
    MacroBuilder,
    Transfer,
    MacroTiming,
    EditLivePanels,
    Ports,
    About,
    CheckForUpdates,
    Quit,
}

/// <summary>
/// One action's button label, paired with the <see cref="TrayAction"/> it
/// represents. <see cref="IsPrimary"/> decides which of the two "Option A:
/// Windows 11 dark" button styles (accent-filled primary vs outlined
/// secondary, ref/docs/hosting.md) the tray's Status window renders it
/// with - decided here, alongside the label and order, rather than by
/// <c>StatusForm</c> inferring it from which <see cref="TrayAction"/> it is.
///
/// <b>There is no group-break flag, deliberately (2026-09-10).</b> Quit
/// briefly carried one, rendered as a wider left margin so it echoed the
/// <c>ToolStripSeparator</c> the right-click menu puts before it. That made
/// Quit a different width from every other button, which the commander read
/// as an inconsistency rather than as a grouping: <i>"quit on the status
/// panel doesn't match the other buttons. Please make them all the same size
/// and shape as 'Add a device'."</i> Uniform won. Quit is last and sits in a
/// corner on its own row, which is separation enough on a strip this small.
/// </summary>
public sealed record TrayActionButton(
    TrayAction Action,
    string Label,
    bool IsPrimary = false);

/// <summary>
/// The ordered set of action buttons the tray's Status window shows,
/// alongside its existing status text.
///
/// <b>Every tray menu item, in the menu's own order</b> - the commander's
/// ask, 2026-09-10: <i>"I need buttons on the status window that match the
/// items put on the tray. Right clicking on the tray isn't intuitive to
/// everyone."</i> That includes Quit: if right-clicking is the part a
/// commander does not find, then without a Quit button there is no way out
/// of the program at all, because closing this window only minimises it
/// (<see cref="TrayCloseDecision"/>).
///
/// Two menu items are deliberately absent. <b>Status</b>, because this
/// window already is the Status view and a button that opens the window you
/// are looking at is a no-op. And the browser items are <b>gated on
/// the same condition the menu gates them on</b> - a host listener actually
/// being bound - which is why this takes the port rather than a flag: it
/// asks <see cref="TrayMacroBuilder.Url"/>,
/// <see cref="TrayTransfer.Url"/>, <see cref="TrayMacroTiming.Url"/> and
/// <see cref="TrayEditLivePanels.Url"/> the same question
/// <c>TrayApplicationContext</c> asks when building the menu, so the two
/// surfaces cannot drift into disagreeing about what exists.
///
/// This is the seam: what buttons exist, their labels, and their order are
/// decided here and pinned by <c>TrayStatusWindowActionsTests</c>, so
/// <c>StatusForm</c> only has to lay out whatever this returns and forward
/// each click - see <c>ref/docs/hosting.md</c>'s tray section for the
/// testable/untestable split this keeps.
/// </summary>
public static class TrayStatusWindowActions
{
    /// <param name="hostAccessPort">
    /// The loopback host listener's port, exactly as
    /// <c>ServerHostOptions.HostAccessPort</c> carries it. Zero or less means
    /// no host listener was bound, and the three PC-only buttons are left off
    /// - the same call the right-click menu makes for the same items.
    /// </param>
    public static IReadOnlyList<TrayActionButton> ButtonsFor(int hostAccessPort)
    {
        var buttons = new List<TrayActionButton>
        {
            new(TrayAction.AddDevice, "Add a device", IsPrimary: true),
            new(TrayAction.Devices, "Devices"),
        };

        if (TrayMacroBuilder.Url(hostAccessPort) is not null)
        {
            buttons.Add(new TrayActionButton(TrayAction.MacroBuilder, TrayMacroBuilder.MenuLabel));
        }

        if (TrayTransfer.Url(hostAccessPort) is not null)
        {
            buttons.Add(new TrayActionButton(TrayAction.Transfer, TrayTransfer.MenuLabel));
        }

        if (TrayMacroTiming.Url(hostAccessPort) is not null)
        {
            buttons.Add(new TrayActionButton(TrayAction.MacroTiming, TrayMacroTiming.MenuLabel));
        }

        if (TrayEditLivePanels.Url(hostAccessPort) is not null)
        {
            buttons.Add(new TrayActionButton(TrayAction.EditLivePanels, TrayEditLivePanels.MenuLabel));
        }

        // Unlike the four buttons above, this needs no hostAccessPort/
        // browser-URL gate: it opens a native WinForms dialog
        // (PortSettingsForm, LunaPanel.Tray) rather than a loopback page, so
        // it is always offered.
        buttons.Add(new TrayActionButton(TrayAction.Ports, "Ports"));

        // Same reasoning as Ports just above - a native WinForms dialog
        // (AboutForm, LunaPanel.Tray), not a loopback page, so no
        // hostAccessPort gate.
        buttons.Add(new TrayActionButton(TrayAction.About, "About"));

        // Same reasoning again, for a third reason on top of the other two:
        // this one reaches the internet rather than a loopback page, so the
        // host listener being bound tells it nothing either way. A commander
        // with no host listener can still be out of date.
        buttons.Add(new TrayActionButton(TrayAction.CheckForUpdates, "Check for updates"));

        buttons.Add(new TrayActionButton(TrayAction.Quit, "Quit"));

        return buttons;
    }
}
