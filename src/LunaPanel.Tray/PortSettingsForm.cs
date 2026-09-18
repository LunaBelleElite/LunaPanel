using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LunaPanel.Core.Network;
using LunaPanel.Server.Tray;

namespace LunaPanel.Tray;

/// <summary>
/// The tray's "Ports" window - lets a commander pick a common preset port or
/// type a custom one, persisted via <see cref="PortSettingsStore"/>
/// (<c>ref/docs</c>'s hosting notes) rather than requiring an environment
/// variable. Added after a work Wi-Fi network was found blocking LunaPanel's
/// old fixed port outright.
///
/// <b>No live rebind.</b> There is no existing path in this codebase to tear
/// down and reconstruct the Kestrel <c>WebApplication</c> in place, and
/// building one is out of scope for this change - too much new risk. OK
/// takes the simple, safe route instead: save the setting, then offer to
/// restart LunaPanel now (a new process launched via
/// <see cref="Application.ExecutablePath"/>, then this process's own normal
/// Quit shutdown - <paramref name="quit"/> - so Kestrel releases the port it
/// is holding before the new instance tries to bind it). Declining the
/// restart leaves the setting saved for next launch without disturbing the
/// process that is currently running.
///
/// <b>The first WinForms dialog in this project to submit a typed/chosen
/// value</b> - <see cref="AddDeviceForm"/>/<see cref="DevicesForm"/> are
/// display/act-only, so there was no existing input pattern to match here.
/// Only the shared styling is borrowed: dark theme via <see cref="TrayTheme"/>,
/// the <c>DwmSetWindowAttribute</c> dark title bar pattern, and
/// measured/auto-sized labels rather than a hardcoded height (see
/// <see cref="StatusForm.BuildRowsPanel"/>'s own remarks on that class of
/// defect).
/// </summary>
internal sealed class PortSettingsForm : Form
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    private readonly PortSettingsStore _store;
    private readonly Action _quit;
    private readonly int _currentPort;

    private readonly RadioButton _commonPortRadio;
    private readonly RadioButton _customPortRadio;
    private readonly ComboBox _presetCombo;
    private readonly TextBox _customPortTextBox;
    private readonly Label _validationLabel;
    private readonly RoundedButton _okButton;

    public PortSettingsForm(PortSettingsStore store, Action quit)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _quit = quit ?? throw new ArgumentNullException(nameof(quit));
        _currentPort = _store.Load().Port;

        var background = ColorTranslator.FromHtml(TrayTheme.Background);
        var primaryText = ColorTranslator.FromHtml(TrayTheme.PrimaryText);

        Text = "Ports";
        Width = 420;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        Font = new Font("Segoe UI", 9F);
        BackColor = background;
        ForeColor = primaryText;

        var isFriendlyPort = PortSettings.FriendlyPorts.Contains(_currentPort);

        _commonPortRadio = new RadioButton
        {
            Text = "Use a common port",
            AutoSize = true,
            ForeColor = primaryText,
            Margin = new Padding(0, 0, 0, 4),
        };

        _presetCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Width = 160,
            Margin = new Padding(20, 0, 0, 12),
            BackColor = ColorTranslator.FromHtml(TrayTheme.SecondaryButtonFill),
            ForeColor = primaryText,
        };
        foreach (var preset in PortSettings.FriendlyPorts)
        {
            _presetCombo.Items.Add(preset);
        }
        _presetCombo.SelectedItem = isFriendlyPort ? _currentPort : PortSettings.FriendlyPorts[0];

        _customPortRadio = new RadioButton
        {
            Text = "Use a custom port",
            AutoSize = true,
            ForeColor = primaryText,
            Margin = new Padding(0, 0, 0, 4),
        };

        _customPortTextBox = new TextBox
        {
            Width = 160,
            Text = isFriendlyPort ? string.Empty : _currentPort.ToString(),
            Margin = new Padding(20, 0, 0, 8),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ColorTranslator.FromHtml(TrayTheme.SecondaryButtonFill),
            ForeColor = primaryText,
        };

        // "Use a common port" starts checked regardless of the persisted
        // port, per this dialog's spec - set after both controls exist so
        // the CheckedChanged handlers (added below) see a consistent pair.
        _commonPortRadio.Checked = true;
        _customPortRadio.Checked = false;

        _validationLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(360, 0),
            ForeColor = ColorTranslator.FromHtml(TrayTheme.NotFoundColor),
            Text = string.Empty,
            Margin = new Padding(0, 0, 0, 8),
        };

        // Every control above reports its own real preferred size
        // (AutoSize/measured, never a hardcoded height) inside a
        // top-down FlowLayoutPanel that is itself AutoSize - the same
        // "measure, don't guess" discipline StatusForm's button strip and
        // AddDeviceForm's instruction label already established, applied
        // here so this dialog isn't the one place in the project still
        // guessing pixel coordinates and clipping content at a different
        // DPI/scaling setting.
        var content = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(16, 16, 16, 0),
        };
        content.Controls.Add(_commonPortRadio);
        content.Controls.Add(_presetCombo);
        content.Controls.Add(_customPortRadio);
        content.Controls.Add(_customPortTextBox);
        content.Controls.Add(_validationLabel);

        // Primary/secondary button styling - same TrayTheme tokens and
        // pattern StatusForm.BuildActionButton and DevicesForm's forget
        // button already use elsewhere in this app. OK is the filled accent
        // button (the default/likely action); Cancel is the outlined
        // secondary, not two identical unstyled squares.
        _okButton = new RoundedButton
        {
            Text = "OK",
            Font = Font,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 12, 0),
            FillColor = ColorTranslator.FromHtml(TrayTheme.PrimaryButtonFill),
            ForeColor = ColorTranslator.FromHtml(TrayTheme.PrimaryButtonText),
            ContainerBackColor = background,
        };
        _okButton.BorderColor = _okButton.FillColor;
        _okButton.Click += (_, _) => OnOk();

        var cancelButton = new RoundedButton
        {
            Text = "Cancel",
            Font = Font,
            Dock = DockStyle.Fill,
            DialogResult = DialogResult.Cancel,
            FillColor = ColorTranslator.FromHtml(TrayTheme.SecondaryButtonFill),
            ForeColor = primaryText,
            ContainerBackColor = background,
            BorderColor = ColorTranslator.FromHtml(TrayTheme.SecondaryButtonBorder),
            // A secondary button's fill is deliberately close to the dialog's
            // own background, which means a 1px outline reads as visually
            // smaller than a same-size solid-filled primary button next to
            // it - confirmed by direct measurement that both buttons are the
            // same actual pixel width. A thicker outline gives it comparable
            // visual weight without changing the colour scheme.
            BorderThickness = 2,
        };

        // Measured directly with TextRenderer against each button's own
        // (explicitly assigned) Font, not Button.GetPreferredSize - neither
        // button has a parent yet at this point in the constructor, so the
        // ambient Font property hasn't resolved from the form, and combined
        // with RoundedButton's owner-draw (UserPaint) style,
        // GetPreferredSize measured "OK" and "Cancel" as visibly different
        // widths instead of the shared one both buttons were supposed to
        // get. TextRenderer.MeasureText against an explicit font sidesteps
        // both problems and is the same "measure, don't guess" approach
        // already used for AddDeviceForm's label and DevicesForm's columns
        // - it also scales correctly with DPI, unlike a hardcoded constant.
        var okTextSize = TextRenderer.MeasureText(_okButton.Text, _okButton.Font);
        var cancelTextSize = TextRenderer.MeasureText(cancelButton.Text, cancelButton.Font);
        var buttonContentWidth = Math.Max(okTextSize.Width, cancelTextSize.Width) + 32;
        var buttonRowHeight = Math.Max(okTextSize.Height, cancelTextSize.Height) + 16;
        var okWidth = buttonContentWidth + _okButton.Margin.Horizontal;
        var cancelWidth = buttonContentWidth + cancelButton.Margin.Horizontal;

        // [2026-09-17] Still read as slightly smaller than OK even with the
        // thicker outline above - live-confirmed twice now. Rather than
        // guess at another visual-weight trick, the commander asked
        // directly for 2px more on every edge, so Cancel's own cell is
        // grown by 4px in each dimension (2px/side) and the button is
        // explicitly sized and centred in it instead of docked to fill -
        // Dock=Fill would have stretched it to match OK's cell again and
        // erased the difference this exists to create.
        const int cancelGrowPerSide = 2;
        var cancelCellWidth = cancelWidth + (cancelGrowPerSide * 2);
        var buttonRowCellHeight = buttonRowHeight + (cancelGrowPerSide * 2);

        var buttonPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(16, 12, 16, 16),
            BackColor = background,
            Height = buttonRowCellHeight + 24,
        };
        buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, okWidth));
        buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, cancelCellWidth));
        buttonPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, buttonRowCellHeight));

        _okButton.Dock = DockStyle.None;
        _okButton.Anchor = AnchorStyles.None;
        _okButton.Size = new Size(okWidth, buttonRowHeight);

        cancelButton.Dock = DockStyle.None;
        cancelButton.Anchor = AnchorStyles.None;
        cancelButton.Size = new Size(cancelWidth + (cancelGrowPerSide * 2), buttonRowHeight + (cancelGrowPerSide * 2));

        buttonPanel.Controls.Add(_okButton, column: 1, row: 0);
        buttonPanel.Controls.Add(cancelButton, column: 2, row: 0);

        Controls.Add(content);
        Controls.Add(buttonPanel);

        CancelButton = cancelButton;

        _commonPortRadio.CheckedChanged += (_, _) => OnRadioChanged();
        _customPortRadio.CheckedChanged += (_, _) => OnRadioChanged();
        _customPortTextBox.TextChanged += (_, _) => UpdateValidation();

        var tooltips = new ToolTip();
        tooltips.SetToolTip(_commonPortRadio, "Pick from a short list of ports most home and work networks already allow through.");
        tooltips.SetToolTip(_customPortRadio, "Type any port number of your own choosing.");
        tooltips.SetToolTip(_presetCombo, "Common ports that most networks/firewalls already allow through.");
        tooltips.SetToolTip(_customPortTextBox, "Port number, 1024-65535; changing this requires LunaPanel to restart.");

        OnRadioChanged();

        // The window's height comes from what got built, not a guessed
        // constant - same reasoning as StatusForm.BuildButtonPanel's own
        // remarks on this. GetPreferredSize measures against the real
        // font/DPI the controls will actually render with, so nothing here
        // can clip at a scaling setting different from whatever this was
        // last eyeballed at.
        var neededHeight = content.GetPreferredSize(Size.Empty).Height
            + buttonPanel.GetPreferredSize(Size.Empty).Height;
        ClientSize = new Size(ClientSize.Width, neededHeight);
    }

    private void OnRadioChanged()
    {
        _presetCombo.Enabled = _commonPortRadio.Checked;
        _customPortTextBox.Enabled = _customPortRadio.Checked;
        UpdateValidation();
    }

    /// <summary>
    /// Enables/disables OK and shows a short reason - only the custom port
    /// path can be invalid at all, since the preset combo only ever offers
    /// values from <see cref="PortSettings.FriendlyPorts"/>.
    /// </summary>
    private void UpdateValidation()
    {
        if (_commonPortRadio.Checked)
        {
            _validationLabel.Text = string.Empty;
            _okButton.Enabled = true;
            return;
        }

        if (!int.TryParse(_customPortTextBox.Text, out var typed))
        {
            _validationLabel.Text = "Enter a whole number.";
            _okButton.Enabled = false;
            return;
        }

        if (!PortSettings.IsValid(typed))
        {
            _validationLabel.Text = $"Port must be between {PortSettings.MinPort} and {PortSettings.MaxPort}.";
            _okButton.Enabled = false;
            return;
        }

        _validationLabel.Text = string.Empty;
        _okButton.Enabled = true;
    }

    private int SelectedPort() => _commonPortRadio.Checked
        ? (int)_presetCombo.SelectedItem!
        : int.Parse(_customPortTextBox.Text);

    private void OnOk()
    {
        var chosenPort = SelectedPort();
        _store.Save(new PortSettings(chosenPort));

        if (chosenPort == _currentPort)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        var restart = MessageBox.Show(
            "LunaPanel needs to restart to use the new port.",
            "LunaPanel",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);

        if (restart == DialogResult.OK)
        {
            Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true });
            _quit();
        }

        DialogResult = DialogResult.OK;
        Close();
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
