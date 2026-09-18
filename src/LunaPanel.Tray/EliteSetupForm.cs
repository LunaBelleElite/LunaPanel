using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LunaPanel.Core.Discovery;
using LunaPanel.Server.Discovery;
using LunaPanel.Server.Tray;

namespace LunaPanel.Tray;

/// <summary>
/// The one-time first-launch screen that checks whether Elite Dangerous was
/// actually found, shown from <c>Program.Main</c> before the host is ever
/// started listening - <see cref="AboutForm"/> already carries this exact
/// same check, but it is opt-in/easy to miss (a commander has to know to
/// open About), so this form surfaces it up front instead, the one time it
/// matters most.
///
/// Gated by <see cref="PathOverrideSettings.EliteSetupAcknowledged"/>: once a
/// commander has been shown this screen and either fixed the path or
/// explicitly deferred it, it never shows again on any later launch,
/// regardless of whether Elite is found by then - <see cref="AboutForm"/>'s
/// own Elite row stays available afterward for anyone who wants to revisit
/// it.
///
/// Reuses <see cref="EliteInstallPathValidator"/> for the exact same
/// "does this folder contain a Products subfolder" check and wording
/// <see cref="AboutForm.OnSelectEliteInstall"/> uses, so the two validation
/// messages cannot drift apart - only the <see cref="FolderBrowserDialog"/>
/// wiring itself is duplicated per-form, matching this codebase's own
/// stated preference for small per-form duplication over a shared type for
/// two call sites.
///
/// <b>No restart flow</b>, unlike <see cref="AboutForm.OfferRestart"/> -
/// discovery has not run yet when this form is shown (it is shown from
/// <c>Program.Main</c> before the host starts), so <c>Program.Main</c>
/// simply disposes and rebuilds the host after this dialog closes, the same
/// dispose-and-rebuild shape <c>Program.RestartAgainstDefaultPort</c> already
/// uses for a port bind failure. This form itself never restarts anything.
/// </summary>
internal sealed class EliteSetupForm : Form
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    private readonly PathOverrideStore _overrides;
    private readonly Label _explanationLabel;
    private readonly Label _statusLabel;
    private readonly RoundedButton _continueButton;
    private readonly FlowLayoutPanel _content;
    private readonly TableLayoutPanel _buttonPanel;

    private string? _verifiedPath;

    /// <summary>
    /// Guards <see cref="ReflowContent"/> against re-entering itself - it
    /// sets <see cref="Form.ClientSize"/>, which raises this form's own
    /// <see cref="Control.Resize"/> event again. Harmless either way (the
    /// second pass recomputes the same, already-correct height and sets
    /// nothing further), but the guard keeps this to one extra pass instead
    /// of relying on that convergence.
    /// </summary>
    private bool _reflowing;

    /// <summary>
    /// Set by <see cref="OnContinue"/>/<see cref="OnSkip"/> so
    /// <see cref="OnFormClosing"/> doesn't save
    /// <see cref="PathOverrideSettings.EliteSetupAcknowledged"/> a second
    /// time when the form closes via one of those buttons rather than the
    /// window's own close button/Alt+F4 - harmless either way (saving the
    /// same value twice is a no-op), kept only to avoid a redundant disk
    /// write and log line on the common path.
    /// </summary>
    private bool _acknowledgedSaved;

    public EliteSetupForm(PathDiscoveryResult discovery, PathOverrideStore overrides)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        _overrides = overrides ?? throw new ArgumentNullException(nameof(overrides));

        _verifiedPath = discovery.EliteInstallations.FirstOrDefault()?.ProductPath;

        var background = ColorTranslator.FromHtml(TrayTheme.Background);
        var primaryText = ColorTranslator.FromHtml(TrayTheme.PrimaryText);

        Text = "Welcome to LunaPanel";
        Width = 520;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;

        // Resizable, unlike AboutForm (left as FixedDialog - out of this
        // task's scope): the explanation below is long enough that it needs
        // to genuinely reflow with the window's width rather than wrap
        // awkwardly at one fixed width forever. MinimumSize is a floor only
        // - it stops a commander from crushing the text into an unreadably
        // narrow column, not a target height (ReflowContent below always
        // resizes the client area to whatever height the current width's
        // wrapping actually needs).
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(360, 280);
        Font = new Font("Segoe UI", 9F);
        BackColor = background;
        ForeColor = primaryText;

        // AutoSize is deliberately off for these two labels - see
        // ReflowContent/ReflowLabel below, which sizes each one explicitly
        // against the form's actual current width on every resize, rather
        // than the fixed MaximumSize this form used before it was made
        // resizable.
        _explanationLabel = new Label
        {
            AutoSize = false,
            ForeColor = primaryText,
            Text = "LunaPanel reads your keybindings from Elite Dangerous - let's make sure it can find your install. "
                + "It should be the folder that directly contains a \"Products\" folder inside it - usually just named "
                + "\"Elite Dangerous\" inside your Steam or Epic library.",
            Margin = new Padding(0, 0, 0, 12),
        };

        _statusLabel = new Label
        {
            AutoSize = false,
            Margin = new Padding(0, 0, 0, 12),
        };

        var selectButton = new RoundedButton
        {
            Text = "Select…",
            Font = Font,
            FillColor = ColorTranslator.FromHtml(TrayTheme.SecondaryButtonFill),
            ForeColor = primaryText,
            ContainerBackColor = background,
            BorderColor = ColorTranslator.FromHtml(TrayTheme.SecondaryButtonBorder),
            BorderThickness = 1,
        };
        var selectTextSize = TextRenderer.MeasureText(selectButton.Text, selectButton.Font);
        selectButton.Size = new Size(selectTextSize.Width + 24, selectTextSize.Height + 12);
        selectButton.Margin = new Padding(0, 0, 0, 16);
        selectButton.Click += (_, _) => OnSelectEliteInstall();

        var tooltips = new ToolTip();
        tooltips.SetToolTip(selectButton, "Point LunaPanel at your Elite Dangerous install by hand.");

        _content = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(16, 16, 16, 0),
        };
        _content.Controls.Add(_explanationLabel);
        _content.Controls.Add(_statusLabel);
        _content.Controls.Add(selectButton);

        _continueButton = new RoundedButton
        {
            Text = "Continue",
            Font = Font,
            FillColor = ColorTranslator.FromHtml(TrayTheme.PrimaryButtonFill),
            ForeColor = ColorTranslator.FromHtml(TrayTheme.PrimaryButtonText),
            ContainerBackColor = background,
        };
        _continueButton.BorderColor = _continueButton.FillColor;
        _continueButton.Click += (_, _) => OnContinue();

        var skipButton = new RoundedButton
        {
            Text = "I'll do this later",
            Font = Font,
            FillColor = ColorTranslator.FromHtml(TrayTheme.SecondaryButtonFill),
            ForeColor = primaryText,
            ContainerBackColor = background,
            BorderColor = ColorTranslator.FromHtml(TrayTheme.SecondaryButtonBorder),
            BorderThickness = 1,
        };
        skipButton.Click += (_, _) => OnSkip();

        var continueTextSize = TextRenderer.MeasureText(_continueButton.Text, _continueButton.Font);
        var skipTextSize = TextRenderer.MeasureText(skipButton.Text, skipButton.Font);
        var buttonRowHeight = Math.Max(continueTextSize.Height, skipTextSize.Height) + 16;
        var continueWidth = continueTextSize.Width + 32;
        var skipWidth = skipTextSize.Width + 32;

        _buttonPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(16, 12, 16, 16),
            BackColor = background,
            Height = buttonRowHeight + 24,
        };
        _buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, skipWidth));
        _buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, continueWidth));
        _buttonPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, buttonRowHeight));

        skipButton.Dock = DockStyle.None;
        skipButton.Anchor = AnchorStyles.None;
        skipButton.Size = new Size(skipWidth, buttonRowHeight);
        skipButton.Margin = new Padding(0, 0, 12, 0);

        _continueButton.Dock = DockStyle.None;
        _continueButton.Anchor = AnchorStyles.None;
        _continueButton.Size = new Size(continueWidth, buttonRowHeight);

        _buttonPanel.Controls.Add(skipButton, column: 1, row: 0);
        _buttonPanel.Controls.Add(_continueButton, column: 2, row: 0);

        Controls.Add(_content);
        Controls.Add(_buttonPanel);

        UpdateStatusDisplay();

        // Initial sizing uses the same reflow logic every later resize does
        // - see ReflowContent's own remarks - rather than a one-off
        // GetPreferredSize measurement the way this form (and AboutForm,
        // which stays fixed-size and keeps that older approach) used to.
        ReflowContent();

        Resize += OnResize;
        FormClosing += OnFormClosing;
    }

    /// <summary>
    /// Re-measures <see cref="_explanationLabel"/> and <see cref="_statusLabel"/>
    /// against the form's actual current width and grows/shrinks the client
    /// area to match, every time the user resizes the window - unlike a
    /// native <see cref="FolderBrowserDialog"/>'s own description column
    /// (see <see cref="EliteInstallPathValidator.FolderPickerDescription"/>'s
    /// doc comment), a WinForms label genuinely can reflow like this.
    /// Dragging the window wider lets the explanation collapse toward one
    /// line; dragging it narrower stacks it back up - and either way the
    /// window's own height is kept exactly matched to what that wrapping
    /// needs, so there is never dead space below the buttons nor text
    /// clipped behind them.
    /// </summary>
    private void OnResize(object? sender, EventArgs e) => ReflowContent();

    private void ReflowContent()
    {
        if (_reflowing)
        {
            return;
        }

        _reflowing = true;
        try
        {
            var availableWidth = Math.Max(100, ClientSize.Width - _content.Padding.Left - _content.Padding.Right);

            ReflowLabel(_explanationLabel, availableWidth);
            ReflowLabel(_statusLabel, availableWidth);

            var neededHeight = _content.GetPreferredSize(Size.Empty).Height
                + _buttonPanel.GetPreferredSize(Size.Empty).Height;
            if (ClientSize.Height != neededHeight)
            {
                ClientSize = new Size(ClientSize.Width, neededHeight);
            }
        }
        finally
        {
            _reflowing = false;
        }
    }

    /// <summary>
    /// Same measurement convention <see cref="AddDeviceForm"/> established
    /// for this exact problem (see its own remarks on the 2026-09-08
    /// truncation this pattern fixed) - measure the real wrapped height for
    /// the label's actual current width rather than trusting a fixed
    /// <c>MaximumSize</c>, which is what let this form's text stack one or
    /// two words per line regardless of how wide the window actually was.
    /// </summary>
    private static void ReflowLabel(Label label, int availableWidth)
    {
        label.Width = availableWidth;
        var measured = TextRenderer.MeasureText(
            label.Text,
            label.Font,
            new Size(availableWidth, int.MaxValue),
            TextFormatFlags.WordBreak);
        label.Height = measured.Height;
    }

    private void UpdateStatusDisplay()
    {
        if (_verifiedPath is not null)
        {
            _statusLabel.ForeColor = ColorTranslator.FromHtml(TrayTheme.FoundColor);
            _statusLabel.Text = $"Found: {_verifiedPath}";
        }
        else
        {
            _statusLabel.ForeColor = ColorTranslator.FromHtml(TrayTheme.NotFoundColor);
            _statusLabel.Text = "Elite Dangerous wasn't found automatically.";
        }

        _continueButton.Enabled = _verifiedPath is not null;
    }

    private void OnSelectEliteInstall()
    {
        var current = _overrides.Load();

        using var dialog = new FolderBrowserDialog
        {
            Description = EliteInstallPathValidator.FolderPickerDescription,
        };
        // _verifiedPath is either a raw discovered EliteInstallation.ProductPath
        // (two levels too deep - see InstallRootFromProductPath's own remarks)
        // or an already-validated root from a prior successful pick in this
        // same dialog session (already contains Products). Open the browser
        // at the actual "Elite Dangerous" folder either way, not three
        // folders too deep - the exact mistake reported live tonight.
        if (_verifiedPath is not null)
        {
            var defaultPath = EliteInstallPathValidator.IsValidEliteInstallRoot(_verifiedPath)
                ? _verifiedPath
                : EliteInstallPathValidator.InstallRootFromProductPath(_verifiedPath);
            if (defaultPath is not null && Directory.Exists(defaultPath))
            {
                dialog.SelectedPath = defaultPath;
            }
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (!EliteInstallPathValidator.IsValidEliteInstallRoot(dialog.SelectedPath))
        {
            MessageBox.Show(
                EliteInstallPathValidator.InvalidSelectionMessage,
                "LunaPanel",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _verifiedPath = dialog.SelectedPath;
        _overrides.Save(current with { EliteInstallPath = _verifiedPath });
        UpdateStatusDisplay();
    }

    private void OnContinue()
    {
        if (_verifiedPath is null)
        {
            return;
        }

        _overrides.Save(_overrides.Load() with { EliteSetupAcknowledged = true });
        _acknowledgedSaved = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnSkip()
    {
        _overrides.Save(_overrides.Load() with { EliteSetupAcknowledged = true });
        _acknowledgedSaved = true;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    /// <summary>
    /// Catches the window's own close button/Alt+F4 - anything that isn't
    /// <see cref="OnContinue"/> or <see cref="OnSkip"/> - and treats it
    /// exactly like "I'll do this later", so this screen can never loop
    /// forever on every future launch with no way out. Saving
    /// <see cref="PathOverrideSettings.EliteSetupAcknowledged"/> a second
    /// time on the common path (where a button handler already saved it) is
    /// harmless - same value, same file - so no extra guard is needed beyond
    /// <see cref="_acknowledgedSaved"/> avoiding the redundant write.
    /// </summary>
    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_acknowledgedSaved)
        {
            return;
        }

        _overrides.Save(_overrides.Load() with { EliteSetupAcknowledged = true });
        _acknowledgedSaved = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TryEnableDarkTitleBar();
    }

    /// <summary>
    /// Same calls as <see cref="AboutForm.TryEnableDarkTitleBar"/>. Must
    /// never throw.
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
