using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Tray;
using LunaPanel.Server.Tray;

namespace LunaPanel.Tray;

/// <summary>
/// The tray's "Status" window (menu item 3), also what double-clicking the
/// tray icon opens. Closing it minimises back to the tray rather than
/// quitting (<c>ref/docs/pairing-and-devices.md</c>; the user's own framing:
/// "the ability to put the program in the background and not take up a
/// taskbar slot") - only the Quit menu action, or Windows itself shutting
/// down, closes it for real. The actual decision is
/// <see cref="TrayCloseDecision.ShouldMinimizeInsteadOfClosing"/>, kept free
/// of any WinForms type so it can be pinned by a test; this form only maps
/// its own <see cref="FormClosingEventArgs.CloseReason"/> onto that decision.
///
/// Also carries the tray's own actions as buttons ("put the tray's actions
/// on the status window, where people actually look for them" - the user
/// opened this window first and found nothing to act on), and since
/// 2026-09-10 that means <i>every</i> menu item rather than the first two:
/// <i>"Right clicking on the tray isn't intuitive to everyone."</i> What
/// buttons exist, their labels and their order all come from
/// <see cref="TrayStatusWindowActions.ButtonsFor"/>;
/// this form only lays them out and forwards each click to
/// <c>onActionClicked</c> - <c>TrayApplicationContext</c> owns what each
/// action actually does, the same code the right-click menu already calls.
///
/// <b>"Option A: Windows 11 dark" restyle (2026-09-07).</b> Every colour and
/// the found/not-found row model come from <see cref="TrayTheme"/> and
/// <see cref="TrayStatusModel"/> in <c>LunaPanel.Server.Tray</c> - both
/// tested (<c>ref/docs/hosting.md</c>'s tray section). This form only lays
/// out whatever those produce; it makes no colour or found/not-found
/// decision of its own. The one thing that has to live here, unavoidably,
/// is the dark title bar - a single <c>dwmapi.dll</c> P/Invoke that needs a
/// real window handle, so it cannot sit behind the seam. It is wrapped to
/// never throw: an older Windows build that doesn't support the attribute
/// simply keeps a light title bar (see <see cref="TryEnableDarkTitleBar"/>).
///
/// <b>Not covered by any automated test</b> - there is no headless WinForms
/// harness in this suite (<c>ref/docs/hosting.md</c>'s "What is NOT
/// tested"). Everything below is reasoned about, not driven.
/// </summary>
internal sealed class StatusForm : Form
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    private readonly Action<TrayAction> _onActionClicked;
    private readonly int _hostAccessPort;
    private readonly StatusWindowPositionStore _positionStore;
    private readonly TrayBehaviorStore _trayBehaviorStore;
    private readonly Panel _contentPanel;
    private readonly Color _background;
    private readonly Color _primaryText;

    /// <summary>
    /// One shared ToolTip for the whole window, same pattern as
    /// <see cref="PortSettingsForm"/>/<see cref="AboutForm"/> - a status row
    /// like "Bindings found: No" tells a commander THAT something is
    /// missing but not WHAT it means or where to fix it, and the button
    /// strip's own labels ("Ports", "Macro timing") don't say what they'll
    /// open either.
    /// </summary>
    private readonly ToolTip _tooltips = new();

    /// <summary>
    /// Explains what a status row actually means and, where there's an
    /// obvious next step, points at it - keyed on
    /// <see cref="TrayStatusRow.Label"/> exactly as
    /// <c>TrayStatusModelBuilder.Build</c> writes it. A label with no entry
    /// here (a future row this dictionary hasn't caught up with) simply gets
    /// no tooltip rather than a wrong or stale one.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RowTooltips = new Dictionary<string, string>
    {
        ["Elite install found"] = "Whether LunaPanel found your Elite Dangerous install automatically. If not, use the tray's About window to point it at the right folder.",
        ["Bindings found"] = "Whether LunaPanel found your Elite Dangerous key bindings file - this is what lets buttons follow your own keybindings instead of a fixed default.",
        ["EDHM theme found"] = "Whether LunaPanel found an actual usable HUD colour theme from EDHM-UI, so the panel can match it. If you don't use EDHM-UI, this stays \"Not found\" and that's fine - the panel falls back to Elite's own orange, or whatever colour you pick yourself.",
        ["Paired devices"] = "How many phones or tablets are currently paired to this PC.",
    };

    public StatusForm(TrayStatusModel model, int hostAccessPort, StatusWindowPositionStore positionStore, TrayBehaviorStore trayBehaviorStore, Action<TrayAction> onActionClicked)
    {
        _onActionClicked = onActionClicked;
        _hostAccessPort = hostAccessPort;
        _positionStore = positionStore;
        _trayBehaviorStore = trayBehaviorStore;
        _background = ColorTranslator.FromHtml(TrayTheme.Background);
        _primaryText = ColorTranslator.FromHtml(TrayTheme.PrimaryText);

        Text = "LunaPanel status";
        Width = 520;
        Height = 400;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = true;
        MaximizeBox = false;
        Font = new Font("Segoe UI", 9F);
        BackColor = _background;
        ForeColor = _primaryText;

        // Remembers where the commander last put this window
        // (ref/docs' tray/hosting notes) - restores that position only if a
        // live monitor still covers it, since a laptop-plus-dock setup can
        // lose the monitor this window was on without the app restarting.
        // No saved position at all (first-ever run, or nothing ever moved
        // it) keeps today's CenterScreen behavior untouched.
        var saved = _positionStore.Load();
        if (saved is not null && MonitorPositionValidator.IsOnLiveScreen(saved.X, saved.Y, CurrentScreenBounds()))
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(saved.X, saved.Y);
        }

        _contentPanel = new Panel { Dock = DockStyle.Fill, BackColor = _background, Padding = new Padding(16, 12, 16, 0) };
        var buttonPanel = BuildButtonPanel();

        // Fill added first (back of z-order, docked last - takes whatever
        // space the bottom button strip leaves), buttons added second (front,
        // claims the bottom strip first) - same order the pre-restyle version
        // of this form already used successfully for textbox+buttons.
        Controls.Add(_contentPanel);
        Controls.Add(buttonPanel);

        SetStatus(model);
    }

    /// <summary>
    /// Rebuilds the URL line and every label/value row from
    /// <paramref name="model"/>. Called from the constructor, and again by
    /// <c>TrayApplicationContext</c> whenever the underlying state (e.g. the
    /// paired-device count) could have changed while this window stayed
    /// open.
    /// </summary>
    public void SetStatus(TrayStatusModel model)
    {
        _contentPanel.Controls.Clear();

        var rowsPanel = BuildRowsPanel(model);
        var urlBox = BuildUrlBox(model.Url);

        // Same ordering rule as above: Fill first, Top second.
        _contentPanel.Controls.Add(rowsPanel);
        _contentPanel.Controls.Add(urlBox);
    }

    /// <summary>
    /// A read-only box rather than a label so the address can be selected and
    /// copied - but <see cref="Control.TabStop"/> is off deliberately. WinForms
    /// focuses the first tab-stop control when a form opens, and a focused
    /// TextBox selects its whole contents, so leaving it on meant the window
    /// opened with the address highlighted blue. That is the same auto-selection
    /// the commander rejected twice on the pairing window (2026-09-09), and the
    /// fix is the same one: take it out of the tab order rather than trying to
    /// clear the selection after the fact. Focus lands on "Add a device"
    /// instead, which is the thing to press.
    /// </summary>
    private TextBox BuildUrlBox(string url)
    {
        var box = new TextBox
        {
            Text = url,
            ReadOnly = true,
            TabStop = false,
            Dock = DockStyle.Top,
            Height = 26,
            BorderStyle = BorderStyle.None,
            BackColor = _background,
            ForeColor = ColorTranslator.FromHtml(TrayTheme.LinkText),
            Font = new Font("Segoe UI", 9F),
        };
        _tooltips.SetToolTip(box, "Type this address into your phone or tablet's browser to open the panel.");
        return box;
    }

    /// <summary>
    /// <b>DPI/font-dependent row squish, fixed here (2026-09-15).</b> Each
    /// row's two <see cref="Label"/>s used to be <c>AutoSize = false, Height
    /// = 24</c> - a hardcoded pixel height inside a <see cref="RowStyle"/>
    /// that was already <see cref="SizeType.AutoSize"/>. At a different
    /// font/DPI the real text metrics exceeded 24px and rows crowded each
    /// other, the same class of defect this file's button strip (see
    /// <see cref="BuildButtonPanel"/>'s own remarks) and
    /// <see cref="AddDeviceForm"/>'s instruction label were already fixed
    /// for. Both labels are <c>AutoSize = true</c> now, so each row reports
    /// its own real preferred size and the already-<c>AutoSize</c> row style
    /// sizes correctly at any DPI/font.
    /// </summary>
    private TableLayoutPanel BuildRowsPanel(TrayStatusModel model)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = model.Rows.Count,
            BackColor = _background,
            Padding = new Padding(0, 8, 0, 8),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));

        for (var i = 0; i < model.Rows.Count; i++)
        {
            var row = model.Rows[i];
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var labelControl = new Label
            {
                Text = row.Label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = ColorTranslator.FromHtml(TrayTheme.LabelText),
                AutoSize = true,
            };
            var valueControl = new Label
            {
                Text = row.Value,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = ColorTranslator.FromHtml(TrayTheme.ColorFor(row.State)),
                AutoSize = true,
            };

            if (RowTooltips.TryGetValue(row.Label, out var tooltip))
            {
                _tooltips.SetToolTip(labelControl, tooltip);
                _tooltips.SetToolTip(valueControl, tooltip);
            }

            panel.Controls.Add(labelControl, column: 0, row: i);
            panel.Controls.Add(valueControl, column: 1, row: i);
        }

        return panel;
    }

    private const int ButtonColumns = 2;

    /// <summary>
    /// The action strip along the bottom.
    ///
    /// <b>Every size here is measured, never chosen.</b> The first version of
    /// this strip picked a button height by eye and let the columns divide
    /// the window evenly, and it shipped broken: "Build a macro", "Import or
    /// export" and "Macro timing" word-wrapped onto a second line that the
    /// fixed height then cut off, so the commander saw "Build a", "Import
    /// or", "Macro". What made that survive review is worth recording -
    /// a probe measured the buttons and reported them fine, because the probe
    /// had not called <see cref="Application.SetCompatibleTextRenderingDefault"/>
    /// the way <c>Program</c> does, so it measured GDI+ text while the real
    /// window drew GDI text at a different width.
    ///
    /// So the cell size comes from asking the buttons themselves, with the
    /// font and renderer they will actually be drawn with
    /// (<see cref="Control.GetPreferredSize"/>), and both the column widths
    /// and the row heights are <see cref="SizeType.Absolute"/> from that
    /// measurement. Nothing here can wrap or clip at any DPI or text-rendering
    /// setting, because nothing here assumes a size.
    ///
    /// <b>Two columns, not three.</b> Three fits the window but leaves each
    /// label about sixty pixels of room, which is where the wrapping came
    /// from. Two gives the longest label ("Import or export") its full width
    /// with margin to spare.
    ///
    /// A grid rather than a wrapping <c>FlowLayoutPanel</c>, for a separate
    /// reason that still holds: a docked auto-sizing flow panel measures its
    /// preferred size at unconstrained width, concludes everything fits on
    /// one row, and clips whatever actually wrapped - which is the last
    /// button, Quit, the one button on this window a commander who never
    /// found the right-click menu cannot reach any other way.
    /// </summary>
    private TableLayoutPanel BuildButtonPanel()
    {
        var controls = TrayStatusWindowActions
            .ButtonsFor(_hostAccessPort)
            .Select(BuildActionButton)
            .ToList();

        var cellWidth = controls.Max(c => c.GetPreferredSize(Size.Empty).Width + c.Margin.Horizontal);
        var cellHeight = controls.Max(c => c.GetPreferredSize(Size.Empty).Height + c.Margin.Vertical);
        var rows = (controls.Count + ButtonColumns - 1) / ButtonColumns;

        var buttonPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            ColumnCount = ButtonColumns,
            RowCount = rows,
            Padding = new Padding(16, 4, 16, 12),
            BackColor = _background,
        };

        // Percent rather than Absolute: an absolute column leaves the window's
        // leftover width on the last column and the grid comes out lopsided.
        // The measured cellWidth is not the column width - it is the floor the
        // window is not allowed to shrink below, enforced by MinimumSize.
        for (var c = 0; c < ButtonColumns; c++)
        {
            buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / ButtonColumns));
        }

        for (var r = 0; r < rows; r++)
        {
            buttonPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, cellHeight));
        }

        for (var i = 0; i < controls.Count; i++)
        {
            buttonPanel.Controls.Add(controls[i], column: i % ButtonColumns, row: i / ButtonColumns);
        }

        buttonPanel.Height = (rows * cellHeight) + buttonPanel.Padding.Vertical;

        // The window has to be at least wide and tall enough for what was
        // just measured, or the grid is clipped by the form instead of by a
        // cell - the same failure one level out.
        var neededWidth = (ButtonColumns * cellWidth) + buttonPanel.Padding.Horizontal;
        if (ClientSize.Width < neededWidth)
        {
            ClientSize = new Size(neededWidth, ClientSize.Height);
        }

        MinimumSize = new Size(
            neededWidth + (Width - ClientSize.Width),
            buttonPanel.Height + MinimumContentHeight + (Height - ClientSize.Height));

        return buttonPanel;
    }

    /// <summary>
    /// How much room the status rows above the strip are never squeezed below.
    /// </summary>
    private const int MinimumContentHeight = 180;

    private Button BuildActionButton(TrayActionButton button)
    {
        // RoundedButton, not a plain Button - same pill-style corners as the
        // web-served panels, applied across every native window in this app
        // (Ports, Paired devices, here) rather than leaving this one strip
        // square while the rest round.
        var control = new RoundedButton
        {
            Text = button.Label,
            Dock = DockStyle.Fill,
            // One margin for every button, so every button is the same size.
            // Quit used to carry a wider left gap as the strip's answer to the
            // menu's ToolStripSeparator, and the only thing that achieved was
            // making Quit narrower than the rest.
            Margin = new Padding(0, 0, 8, 6),
            Padding = new Padding(8, 4, 8, 4),
            ContainerBackColor = _background,
        };

        if (button.IsPrimary)
        {
            control.FillColor = ColorTranslator.FromHtml(TrayTheme.PrimaryButtonFill);
            control.ForeColor = ColorTranslator.FromHtml(TrayTheme.PrimaryButtonText);
            control.BorderColor = control.FillColor;
        }
        else
        {
            control.FillColor = ColorTranslator.FromHtml(TrayTheme.SecondaryButtonFill);
            control.ForeColor = _primaryText;
            control.BorderColor = ColorTranslator.FromHtml(TrayTheme.SecondaryButtonBorder);
            // See PortSettingsForm's own remarks on this: a secondary
            // button's near-background fill reads as visually smaller than
            // a same-size solid primary button unless its outline is
            // thickened to compensate.
            control.BorderThickness = 2;
        }

        control.Click += (_, _) => _onActionClicked(button.Action);
        if (ButtonTooltips.TryGetValue(button.Action, out var tooltip))
        {
            _tooltips.SetToolTip(control, tooltip);
        }
        return control;
    }

    /// <summary>
    /// Keyed on <see cref="TrayAction"/> rather than the button's own label
    /// text, so a future label wording change can't silently disconnect a
    /// tooltip from the button it describes.
    /// </summary>
    private static readonly IReadOnlyDictionary<TrayAction, string> ButtonTooltips = new Dictionary<TrayAction, string>
    {
        [TrayAction.AddDevice] = "Pair a new phone or tablet.",
        [TrayAction.Devices] = "See every paired device, rename or forget one, or edit one's layout live from here.",
        [TrayAction.MacroBuilder] = "Build or edit a macro - a sequence of steps one button can run.",
        [TrayAction.Transfer] = "Save a panel (or a single macro) to a file to share it, or load one back in.",
        [TrayAction.MacroTiming] = "Tune how long a key is held and the gap between presses, if a button isn't registering reliably.",
        [TrayAction.EditLivePanels] = "Edit a paired device's layout live from this PC's browser.",
        [TrayAction.Ports] = "Change which network port LunaPanel uses.",
        [TrayAction.About] = "See the running version, open the logs folder, or point LunaPanel at your Elite Dangerous install/EDHM-UI settings by hand.",
        [TrayAction.CheckForUpdates] = "Check whether a newer version of LunaPanel is available.",
        [TrayAction.Quit] = "Close LunaPanel completely.",
    };

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TryEnableDarkTitleBar();
    }

    /// <summary>
    /// <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c> (attribute 20) and
    /// <c>DWMWA_WINDOW_CORNER_PREFERENCE</c> (attribute 33, requesting
    /// <c>DWMWCP_ROUND</c>) - both must never throw: on a Windows build that
    /// doesn't support one or both attributes the call simply returns
    /// non-zero and the window keeps its default title bar/corners - a
    /// cosmetic failure this method is not allowed to turn into a window
    /// that fails to open at all. Rounded corners are what Windows 11 gives
    /// every top-level window by default already; this app's windows were
    /// square only because nothing here opted in.
    /// </summary>
    private void TryEnableDarkTitleBar()
    {
        try
        {
            var enabled = 1;
            DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));

            var cornerPreference = DwmwcpRound;
            DwmSetWindowAttribute(Handle, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));
        }
        catch
        {
            // Best-effort only - see the doc comment above.
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Only Normal is meaningful - Minimized/Maximized's Location isn't
        // where the commander actually put the window (MaximizeBox is
        // already false, so Normal/Minimized are the only reachable states).
        if (WindowState == FormWindowState.Normal)
        {
            _positionStore.Save(new StatusWindowPosition(Location.X, Location.Y));
        }

        var trigger = e.CloseReason == CloseReason.UserClosing
            ? WindowCloseTrigger.UserClickedClose
            : WindowCloseTrigger.ApplicationExiting;

        // Read fresh, not captured at construction - the commander can flip
        // the About window's checkbox while this window is open, and this
        // decision has to see that change the very next time the X is
        // clicked, not on the next restart.
        var minimizeToTrayEnabled = _trayBehaviorStore.Load().MinimizeToTrayEnabled;

        if (TrayCloseDecision.ShouldMinimizeInsteadOfClosing(trigger, minimizeToTrayEnabled))
        {
            e.Cancel = true;
            Hide();
            return;
        }

        if (trigger == WindowCloseTrigger.UserClickedClose)
        {
            // Minimize-to-tray is off: closing this window's X quits the
            // whole app for real, the same effect as the "Quit" tray-menu
            // action - not just closing this one form while Kestrel and the
            // tray icon silently keep running. Routed through the same
            // dispatch every other tray action already goes through
            // (TrayApplicationContext.Dispatch), rather than duplicating
            // BeginQuit's own shutdown logic here.
            _onActionClicked(TrayAction.Quit);
        }

        base.OnFormClosing(e);
    }

    /// <summary>
    /// Re-validates this form's <b>current</b> <see cref="Control.Location"/>
    /// against whatever monitors exist right now, called by
    /// <c>TrayApplicationContext.ShowStatusWindow</c> right before every
    /// <c>Show()</c>. This is the part plain <c>CenterScreen</c> never
    /// covered: <c>TrayApplicationContext</c> keeps reusing the same hidden
    /// <see cref="StatusForm"/> instance across show/hide cycles, so a
    /// monitor can disappear while this window is hidden, and nothing else
    /// re-checks <see cref="Control.Location"/> before showing it again.
    /// Repositions to the primary screen's working-area centre (the same
    /// math <see cref="FormStartPosition.CenterScreen"/> would have used) -
    /// computed by hand because by the time this runs the form's handle
    /// already exists, so <c>StartPosition</c> no longer has any effect.
    /// </summary>
    public void EnsureOnLiveScreen()
    {
        if (MonitorPositionValidator.IsOnLiveScreen(Location.X, Location.Y, CurrentScreenBounds()))
        {
            return;
        }

        var workingArea = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(
            workingArea.Left + ((workingArea.Width - Width) / 2),
            workingArea.Top + ((workingArea.Height - Height) / 2));
    }

    private static IReadOnlyList<(int Left, int Top, int Width, int Height)> CurrentScreenBounds() =>
        Screen.AllScreens.Select(s => (s.Bounds.Left, s.Bounds.Top, s.Bounds.Width, s.Bounds.Height)).ToList();
}
