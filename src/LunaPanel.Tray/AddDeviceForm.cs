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

    public AddDeviceForm(string groupedCode, string url)
    {
        Text = "Add a device";
        Width = 400;
        Height = 316;
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

        Controls.Add(codeBox);
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
}
