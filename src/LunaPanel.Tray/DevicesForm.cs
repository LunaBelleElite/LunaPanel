using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Pairing;
using LunaPanel.Server.Pairing;
using LunaPanel.Server.Tray;

namespace LunaPanel.Tray;

/// <summary>
/// The tray's "Devices" window (menu item 2) - lists every paired device
/// (id, name, class, paired-at, last-seen) with a forget action, and never
/// the token: <see cref="DeviceListFormatter.BuildRows"/>'s rows have no
/// field for it at all (<c>ref/docs/pairing-and-devices.md</c>). Every row
/// here can be forgotten - unlike a paired device's own Settings -> Devices
/// pane, the tray itself is not a device, so "a device never forgets
/// itself" has no self-row to exempt here.
///
/// <b>"Option A: Windows 11 dark" restyle (2026-09-08).</b> Every colour
/// comes from <see cref="TrayTheme"/> in <c>LunaPanel.Server.Tray</c> - same
/// seam <see cref="StatusForm"/> already uses, see
/// <c>ref/docs/hosting.md</c>'s tray section. The forget button is styled
/// as the same "secondary" (outlined, not accent-filled) button
/// <see cref="StatusForm.BuildActionButton"/> already uses for its own
/// non-primary action - this is a plain action, not an error, and the
/// palette's own rule is no red anywhere regardless. The forget action
/// itself is untouched: still <see cref="LunaPanel.Server.Pairing.DeviceForget.Forget"/>,
/// never <c>_registry.Forget</c> directly - see <c>ForgetPathSourceGuardTests</c>.
/// </summary>
internal sealed class DevicesForm : Form
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    private readonly DeviceRegistry _registry;
    private readonly OrphanMarkerStore _orphanMarkers;
    private readonly ListView _listView;

    public DevicesForm(DeviceRegistry registry, OrphanMarkerStore orphanMarkers)
    {
        _registry = registry;
        _orphanMarkers = orphanMarkers;

        var background = ColorTranslator.FromHtml(TrayTheme.Background);
        var primaryText = ColorTranslator.FromHtml(TrayTheme.PrimaryText);

        Text = "Paired devices";
        Height = 360;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        BackColor = background;
        ForeColor = primaryText;

        _listView = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            Dock = DockStyle.Fill,
            BackColor = background,
            ForeColor = primaryText,
            BorderStyle = BorderStyle.FixedSingle,
        };

        // Column widths used to be hardcoded pixels sized for this header
        // text at 100% scaling - at a different DPI/font the same pixel
        // count no longer fits the header's actual rendered width, and
        // "Device ID" silently became "Devic...". Each column is now at
        // least as wide as its own header measures at this form's real
        // font, the same "measure, don't guess" fix already applied to
        // StatusForm's button strip and AddDeviceForm's instruction label;
        // the second number is a floor for the data column itself (dates
        // and names don't shrink just because the header is short).
        var columns = new (string Header, int MinDataWidth)[]
        {
            ("Device ID", 90),
            ("Name", 160),
            ("Class", 80),
            ("Paired at", 140),
            ("Last seen", 140),
        };
        foreach (var (header, minDataWidth) in columns)
        {
            var headerWidth = TextRenderer.MeasureText(header, Font).Width + 28;
            _listView.Columns.Add(header, Math.Max(headerWidth, minDataWidth));
        }

        // RoundedButton, not a plain Button - matches the pill-style corners
        // now used across every native window in this app (Status, Ports).
        var forgetButton = new RoundedButton
        {
            Text = "Forget selected device",
            Dock = DockStyle.Bottom,
            FillColor = ColorTranslator.FromHtml(TrayTheme.SecondaryButtonFill),
            ForeColor = primaryText,
            BorderColor = ColorTranslator.FromHtml(TrayTheme.SecondaryButtonBorder),
            ContainerBackColor = background,
            // Same secondary-button convention as StatusForm/PortSettingsForm
            // - a thicker outline for visual consistency across the app.
            BorderThickness = 2,
        };
        forgetButton.Click += (_, _) => ForgetSelected();

        // Measured, not a hardcoded 32 - a text-height-sensitive button
        // squished the same way the rest of this sweep's fixes were about.
        forgetButton.Height = forgetButton.GetPreferredSize(Size.Empty).Height + 12;

        Controls.Add(_listView);
        Controls.Add(forgetButton);

        // The window has to be at least as wide as what the columns
        // actually measured out to, or the rightmost one clips against the
        // form edge instead of against a cell - same failure one level out,
        // same fix StatusForm.BuildButtonPanel already uses for its strip.
        var neededWidth = columns.Length == 0
            ? 640
            : _listView.Columns.Cast<ColumnHeader>().Sum(c => c.Width) + 40;
        Width = Math.Max(640, neededWidth);

        Reload();
    }

    private void Reload()
    {
        _listView.Items.Clear();
        foreach (var row in DeviceListFormatter.BuildRows(_registry.ListDevices()))
        {
            _listView.Items.Add(new ListViewItem(new[] { row.DeviceId, row.Name, row.DeviceClass, row.PairedAtDisplay, row.LastSeenDisplay }));
        }
    }

    private void ForgetSelected()
    {
        if (_listView.SelectedItems.Count == 0)
        {
            return;
        }

        var deviceId = _listView.SelectedItems[0].SubItems[0].Text;

        // DeviceForget, not _registry.Forget: this window is the second of
        // the two forget paths, and the orphan marker that keeps the layout
        // left behind describable has to be written from both. See
        // DeviceForget's own remarks.
        DeviceForget.Forget(_registry, _orphanMarkers, deviceId);
        Reload();
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
