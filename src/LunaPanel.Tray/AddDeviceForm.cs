using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LunaPanel.Server.Tray;

namespace LunaPanel.Tray;

/// <summary>
/// The tray's "Add a device" window (menu item 1) -
/// <c>ref/docs/pairing-and-devices.md</c>'s whole reason the tray exists:
/// the pairing code is a one-shot, invisible the moment console scrollback
/// moves past it, and single-use (a fresh code is needed per device). Shows
/// the freshly-opened window's code large and selectable - a read-only
/// <see cref="TextBox"/>, not a <see cref="Label"/>, specifically so it can
/// be selected and copied. <paramref name="groupedCode"/> is already
/// formatted by <see cref="LunaPanel.Server.Tray.PairingCodeFormatter"/>;
/// this form only displays it.
///
/// <b>"Option A: Windows 11 dark" restyle (2026-09-08).</b> Every colour
/// comes from <see cref="TrayTheme"/> in <c>LunaPanel.Server.Tray</c> -
/// same seam <see cref="StatusForm"/> already uses, see
/// <c>ref/docs/hosting.md</c>'s tray section. The dark title bar P/Invoke is
/// duplicated from <see cref="StatusForm"/> rather than shared, the same way
/// this suite already tolerates a duplicated <c>FindRepoRoot</c> helper
/// across several source-scan tests - one extra small P/Invoke per form
/// reads more locally than a third shared type for two call sites.
///
/// <b>Two defects fixed here, not just the restyle.</b> The instruction
/// label used to be truncated by the window's width - it read only "On the
/// new device, open LunaPanel and" before stopping, because a hardcoded
/// pixel <c>Height</c> fit the label's old, shorter text but not this one.
/// The label's height is now measured from the real text/font/width instead
/// of guessed, so it cannot silently start truncating again the same way.
///
/// <b>Selection on show, fixed twice (2026-09-08, then 2026-09-09).</b> The
/// code box no longer pre-selects itself when the window opens - reported
/// three times as looking accidental. The first fix removed the explicit
/// select-everything call this box used to make on itself, but left an
/// explicit <c>Focus()</c> in a
/// <see cref="Form.Shown"/> handler, which reproduces the exact same
/// full-text selection - a plain <see cref="TextBox"/> selects all of its
/// text whenever it is given keyboard focus programmatically. The real fix
/// (see the box's own <c>TabStop</c> comment below) also had to stop the
/// box from receiving WinForms' own default initial focus, which happens
/// even with no <c>Focus()</c> call anywhere in this file and triggers the
/// identical selection. It stays a selectable, copyable
/// <see cref="TextBox"/> - only programmatic focus is affected, not a
/// commander's own mouse click.
/// </summary>
internal sealed class AddDeviceForm : Form
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    private const string Instruction = "On the new device, open LunaPanel and enter this code:";

    /// <summary>
    /// Shown once <see cref="_countdownTimer"/> observes <paramref name="expiresAt"/>
    /// has passed. Purely visual - see this class's own remarks on the
    /// countdown bar; nothing here re-checks or changes server-side pairing
    /// state.
    /// </summary>
    private const string ExpiredCaption = "This code has expired — close and try again";

    private readonly System.Windows.Forms.Timer _countdownTimer;
    private readonly Panel _countdownBarBackground;
    private readonly Panel _countdownBarFill;
    private readonly Label _countdownLabel;
    private readonly DateTimeOffset _expiresAt;
    private readonly TimeSpan _totalSpan;

    /// <summary>
    /// <paramref name="expiresAt"/> is the instant
    /// <see cref="LunaPanel.Core.Pairing.DeviceRegistry"/>'s own pairing
    /// window closes - read once here, at construction, and the span
    /// between it and "now" at that moment is captured as the countdown
    /// bar's 100% (see <see cref="_totalSpan"/>). This form is always
    /// constructed immediately after
    /// <see cref="LunaPanel.Core.Pairing.DeviceRegistry.OpenPairingWindow"/>
    /// returns, so that captured span is, for all practical purposes, the
    /// full two-minute window, without this form needing to know
    /// <c>DeviceRegistry</c>'s own duration constant. Purely a client-side
    /// visual: the server-side <c>IsPairingWindowOpen</c> check already
    /// refuses an expired code on its own, and this form never auto-closes
    /// or auto-regenerates a code once the bar empties.
    /// </summary>
    public AddDeviceForm(string groupedCode, string url, DateTimeOffset expiresAt)
    {
        Text = "Add a device";
        Width = 400;
        Height = 356;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;

        var background = ColorTranslator.FromHtml(TrayTheme.Background);
        var primaryText = ColorTranslator.FromHtml(TrayTheme.PrimaryText);
        Font = new Font("Segoe UI", 9F);
        BackColor = background;
        ForeColor = primaryText;

        var label = new Label
        {
            Text = Instruction,
            Dock = DockStyle.Top,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = primaryText,
        };

        // Measure the real wrapped height for this label's actual
        // width/font rather than hardcoding one - a hardcoded constant is
        // exactly how this text got silently truncated in the first place
        // (2026-09-08), and it would just as quietly go stale again under a
        // font or DPI change. Measuring against ClientSize.Width minus a
        // margin (rather than the label's own eventual width) biases toward
        // *more* height than strictly needed, never less.
        var measured = TextRenderer.MeasureText(
            Instruction,
            label.Font,
            new Size(ClientSize.Width - 24, int.MaxValue),
            TextFormatFlags.WordBreak);
        label.Height = measured.Height + 16;

        var codeBox = new TextBox
        {
            Text = groupedCode,
            ReadOnly = true,
            TabStop = false,
            Dock = DockStyle.Fill,
            TextAlign = HorizontalAlignment.Center,
            Font = new Font("Consolas", 28F, FontStyle.Bold),
            BorderStyle = BorderStyle.None,
            BackColor = background,
            ForeColor = primaryText,
        };

        // The LAN address a first-time commander has nothing else to type
        // into their tablet's browser alongside the code above - same
        // read-only/non-focusable pattern as StatusForm.BuildUrlBox, so it
        // is copyable but never the thing that steals the window's initial
        // focus/selection (see this class's own remarks on that fix).
        var urlBox = new TextBox
        {
            Text = url,
            ReadOnly = true,
            TabStop = false,
            Dock = DockStyle.Fill,
            TextAlign = HorizontalAlignment.Center,
            Font = new Font("Segoe UI", 9F),
            BorderStyle = BorderStyle.None,
            BackColor = background,
            ForeColor = ColorTranslator.FromHtml(TrayTheme.LinkText),
        };

        // A bottom-padded panel rather than docking urlBox straight to the
        // window edge - without it the address text sits flush against the
        // bottom border with no breathing room.
        var urlPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            BackColor = background,
            Padding = new Padding(0, 0, 0, 16),
        };
        urlPanel.Controls.Add(urlBox);

        // The countdown bar - positioned below the code box, above the URL.
        // A plain custom-drawn Panel-on-Panel rather than a native
        // ProgressBar, matching this form's existing "no native control that
        // can't be recoloured to match TrayTheme" preference (see this
        // class's own remarks on why the dark-title-bar P/Invoke is
        // duplicated locally rather than shared, for the same "small enough
        // to just do it here" reasoning). _countdownBarFill's Width is the
        // only thing UpdateCountdown mutates on every tick.
        _countdownBarBackground = new Panel
        {
            Dock = DockStyle.Top,
            Height = 6,
            BackColor = ColorTranslator.FromHtml(TrayTheme.Border),
        };
        _countdownBarFill = new Panel
        {
            Location = new Point(0, 0),
            Height = 6,
            Width = 0,
            BackColor = ColorTranslator.FromHtml(TrayTheme.PrimaryButtonFill),
        };
        _countdownBarBackground.Controls.Add(_countdownBarFill);

        _countdownLabel = new Label
        {
            Dock = DockStyle.Bottom,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = ColorTranslator.FromHtml(TrayTheme.LabelText),
            Font = new Font("Segoe UI", 9F),
        };

        // Measured against ExpiredCaption specifically, not the shorter
        // "Expires in m:ss" text this label starts with - ExpiredCaption is
        // the longer of the two and can wrap to two lines at this width, so
        // sizing for the short text (as a prior version of this label did,
        // with a hardcoded Height=24) clips the second line the instant the
        // countdown reaches zero. Same "measure the real text, don't guess
        // a constant" fix already applied to the Instruction label above -
        // see this class's own remarks on why that one was fixed first.
        const int countdownPanelHorizontalPadding = 24;
        var countdownLabelMeasured = TextRenderer.MeasureText(
            ExpiredCaption,
            _countdownLabel.Font,
            new Size(Width - countdownPanelHorizontalPadding * 2, int.MaxValue),
            TextFormatFlags.WordBreak);
        _countdownLabel.Height = countdownLabelMeasured.Height + 8;

        var countdownPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = _countdownBarBackground.Height + 6 + _countdownLabel.Height,
            BackColor = background,
            Padding = new Padding(countdownPanelHorizontalPadding, 6, countdownPanelHorizontalPadding, 0),
        };
        // Added in this order (label, then bar) deliberately - within this
        // sub-panel the same "last added docks first" rule this form's own
        // outer Controls.Add already relies on applies again: the label
        // (Dock=Bottom) is added last so it claims the panel's bottom edge,
        // leaving the bar (Dock=Top) the remaining space above it.
        countdownPanel.Controls.Add(_countdownBarBackground);
        countdownPanel.Controls.Add(_countdownLabel);

        _expiresAt = expiresAt;
        var initialSpan = expiresAt - DateTimeOffset.UtcNow;
        _totalSpan = initialSpan > TimeSpan.Zero ? initialSpan : TimeSpan.FromSeconds(1);

        _countdownTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _countdownTimer.Tick += (_, _) => UpdateCountdown();
        _countdownTimer.Start();
        Load += (_, _) => UpdateCountdown();

        Controls.Add(codeBox);
        Controls.Add(countdownPanel);
        Controls.Add(urlPanel);
        Controls.Add(label);

        // Never focused, and the box's own TabStop is off (see its
        // initializer above) - not just "no explicit Focus() call".
        // Measured directly (2026-09-09): WinForms auto-focuses the first
        // tab-stop control when a form is shown even with no Focus() call
        // anywhere in this file, and *any* programmatic focus of a
        // single-line TextBox - whether from that auto-focus or from an
        // explicit Focus() - reproduces the exact same full-text selection
        // this box used to produce by selecting everything directly.
        // Removing only the Shown handler (the first fix attempted here)
        // does not stop the auto-focus path, which is why the code kept
        // coming up selected after that fix shipped. Turning the box's
        // TabStop off removes it from that auto-focus path entirely, while ReadOnly
        // keeps it a TextBox a commander can still click into and select
        // by hand - ordinary mouse-driven selection is a different code
        // path and is unaffected by TabStop.
        //
        // There is nothing a keyboard focus buys this box: it is ReadOnly,
        // so nothing can be typed into it, and the commander reads the code
        // on this screen and types it on a phone rather than pasting it -
        // so the one thing focus would enable (Ctrl+A/Ctrl+C without a
        // click first) has no real user behind it either.
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TryEnableDarkTitleBar();
    }

    /// <summary>
    /// Recomputes the countdown bar's fill width and caption text against
    /// <see cref="_expiresAt"/> and "now". Stops <see cref="_countdownTimer"/>
    /// once the window has expired - the caption then reads
    /// <see cref="ExpiredCaption"/> and the bar sits empty, but the form
    /// itself is left open; it never auto-closes or auto-regenerates a code
    /// (see this class's own remarks on why).
    /// </summary>
    private void UpdateCountdown()
    {
        var remaining = _expiresAt - DateTimeOffset.UtcNow;

        if (remaining <= TimeSpan.Zero)
        {
            _countdownTimer.Stop();
            _countdownBarFill.Width = 0;
            _countdownLabel.Text = ExpiredCaption;
            return;
        }

        var fraction = Math.Clamp(remaining / _totalSpan, 0.0, 1.0);
        _countdownBarFill.Width = (int)(_countdownBarBackground.ClientSize.Width * fraction);
        _countdownLabel.Text = $"Expires in {(int)remaining.TotalMinutes}:{remaining.Seconds:D2}";
    }

    /// <summary>
    /// <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c> (attribute 20) and
    /// <c>DWMWA_WINDOW_CORNER_PREFERENCE</c> (attribute 33, requesting
    /// <c>DWMWCP_ROUND</c>) - same calls as
    /// <see cref="StatusForm.TryEnableDarkTitleBar"/>. Must never throw: on
    /// a Windows build that doesn't support one or both attributes the call
    /// simply returns non-zero and the window keeps its default title
    /// bar/corners.
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

    /// <summary>
    /// Stops and disposes <see cref="_countdownTimer"/> alongside the rest
    /// of the form - a running <see cref="System.Windows.Forms.Timer"/> left
    /// ticking against a disposed form is the standard WinForms trap this
    /// avoids.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _countdownTimer.Stop();
            _countdownTimer.Dispose();
        }

        base.Dispose(disposing);
    }
}
