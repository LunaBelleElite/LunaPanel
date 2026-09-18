using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LunaPanel.Core.Discovery;
using LunaPanel.Server.Discovery;
using LunaPanel.Server.Tray;

namespace LunaPanel.Tray;

/// <summary>
/// The tray's "About" window - shows the running version, the logs folder
/// (with a button to open it), and lets a commander manually point LunaPanel
/// at the Elite Dangerous install and/or the EDHM-UI settings file when
/// auto-detection can't find one (or found the wrong one on a multi-install
/// machine). Copies <see cref="PortSettingsForm"/>'s conventions throughout:
/// dark theme via <see cref="TrayTheme"/>, the <c>DwmSetWindowAttribute</c>
/// dark title bar pattern, <see cref="RoundedButton"/>, and measured (never
/// hardcoded) sizing.
///
/// <b>No live re-discovery</b> - matches <see cref="PortSettingsForm.OnOk"/>'s
/// own precedent exactly: picking a path saves it via
/// <see cref="PathOverrideStore"/>, then offers to restart now (a new process
/// via <see cref="Application.ExecutablePath"/>, then this process's own
/// normal Quit shutdown via <paramref name="quit"/> passed to the
/// constructor) so the next launch's discovery pass actually reads it.
/// Declining the restart leaves the setting saved for next launch.
///
/// <b>The EDHM row shows a recomputed path, not one read off
/// <paramref name="discovery"/>.</b> <see cref="EdhmDiscoveryResult"/> only
/// ever exposes <c>SettingsFound</c>/<c>UserDataFolder</c>/<c>ActiveInstance</c>
/// - never the settings-file path itself - and this task's own scope
/// deliberately does not touch <c>EdhmDiscovery</c> to add one. The path
/// shown here is therefore recomputed the same way
/// <c>RealServerEnvironment.Build()</c> computes it: the saved override if
/// one exists, otherwise the same fixed conventional location. Whether it
/// was actually found is still <see cref="EdhmDiscoveryResult.SettingsFound"/>
/// from <paramref name="discovery"/>, not re-derived here.
/// </summary>
internal sealed class AboutForm : Form
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    private readonly PathDiscoveryResult _discovery;
    private readonly LunaPanelDirectoryLayout _layout;
    private readonly PathOverrideStore _overrides;
    private readonly Action _quit;

    public AboutForm(
        PathDiscoveryResult discovery,
        LunaPanelDirectoryLayout layout,
        PathOverrideStore overrides,
        Action quit)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _overrides = overrides ?? throw new ArgumentNullException(nameof(overrides));
        _quit = quit ?? throw new ArgumentNullException(nameof(quit));

        var background = ColorTranslator.FromHtml(TrayTheme.Background);
        var primaryText = ColorTranslator.FromHtml(TrayTheme.PrimaryText);
        var labelText = ColorTranslator.FromHtml(TrayTheme.LabelText);

        Text = "About LunaPanel";
        Width = 520;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        Font = new Font("Segoe UI", 9F);
        BackColor = background;
        ForeColor = primaryText;

        var content = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(16, 16, 16, 0),
        };

        // All three action buttons share one size (the widest of their texts,
        // "Select…") so "Open" doesn't end up narrower just because its own
        // text is shorter.
        var actionButtonSize = MeasureButtonSize(Font, "Open", "Select…");

        content.Controls.Add(BuildVersionRow(primaryText, labelText));
        content.Controls.Add(BuildRow(
            "Logs folder",
            _layout.LogsDirectory,
            primaryText,
            labelText,
            "Open",
            actionButtonSize,
            (_, _) => OpenLogsFolder()));
        content.Controls.Add(BuildRow(
            "Elite Dangerous",
            CurrentEliteLabel(),
            primaryText,
            labelText,
            "Select…",
            actionButtonSize,
            (_, _) => OnSelectEliteInstall()));
        content.Controls.Add(BuildRow(
            "EDHM",
            CurrentEdhmLabel(),
            primaryText,
            labelText,
            "Select…",
            actionButtonSize,
            (_, _) => OnSelectEdhmSettingsFile()));

        var closeButton = new RoundedButton
        {
            Text = "Close",
            Font = Font,
            DialogResult = DialogResult.Cancel,
            FillColor = ColorTranslator.FromHtml(TrayTheme.SecondaryButtonFill),
            ForeColor = primaryText,
            ContainerBackColor = background,
            BorderColor = ColorTranslator.FromHtml(TrayTheme.SecondaryButtonBorder),
            BorderThickness = 1,
        };

        var closeTextSize = TextRenderer.MeasureText(closeButton.Text, closeButton.Font);
        var closeButtonWidth = closeTextSize.Width + 32;
        var closeButtonHeight = closeTextSize.Height + 16;

        var buttonPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(16, 12, 16, 16),
            BackColor = background,
            Height = closeButtonHeight + 24,
        };
        buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, closeButtonWidth));
        buttonPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, closeButtonHeight));

        closeButton.Dock = DockStyle.None;
        closeButton.Anchor = AnchorStyles.None;
        closeButton.Size = new Size(closeButtonWidth, closeButtonHeight);
        buttonPanel.Controls.Add(closeButton, column: 1, row: 0);

        Controls.Add(content);
        Controls.Add(buttonPanel);

        CancelButton = closeButton;

        var neededHeight = content.GetPreferredSize(Size.Empty).Height
            + buttonPanel.GetPreferredSize(Size.Empty).Height;
        ClientSize = new Size(ClientSize.Width, neededHeight);
    }

    private Control BuildVersionRow(Color primaryText, Color labelText)
    {
        var label = new Label
        {
            AutoSize = true,
            ForeColor = labelText,
            Text = "Version",
            Margin = new Padding(0, 0, 0, 0),
        };
        var value = new Label
        {
            AutoSize = true,
            ForeColor = primaryText,
            Text = CurrentVersion(),
            Margin = new Padding(0, 0, 0, 12),
        };

        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        row.Controls.Add(label);
        row.Controls.Add(value);
        return row;
    }

    /// <summary>
    /// The size every action button in this form shares, so a shorter label
    /// like "Open" doesn't end up narrower than "Select…" beside it -
    /// measures every candidate text at the given font and sizes to the
    /// widest, exactly like every individual button already sized itself
    /// before this shared measurement existed.
    /// </summary>
    private static Size MeasureButtonSize(Font font, params string[] texts)
    {
        var width = 0;
        var height = 0;
        foreach (var text in texts)
        {
            var size = TextRenderer.MeasureText(text, font);
            width = Math.Max(width, size.Width);
            height = Math.Max(height, size.Height);
        }

        return new Size(width + 24, height + 12);
    }

    /// <summary>
    /// One label+value+button row, matching every other row's shape - a
    /// small label caption, the current value beneath it, and an action
    /// button beside the value.
    /// </summary>
    private static Control BuildRow(
        string caption,
        string valueText,
        Color primaryText,
        Color labelText,
        string buttonText,
        Size buttonSize,
        EventHandler onClick)
    {
        var captionLabel = new Label
        {
            AutoSize = true,
            ForeColor = labelText,
            Text = caption,
            Margin = new Padding(0, 0, 0, 0),
        };

        var valueLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(360, 0),
            ForeColor = primaryText,
            Text = valueText,
            Margin = new Padding(0, 0, 12, 0),
        };

        var button = new RoundedButton
        {
            Text = buttonText,
            Font = valueLabel.Font,
            FillColor = ColorTranslator.FromHtml(TrayTheme.SecondaryButtonFill),
            ForeColor = primaryText,
            ContainerBackColor = ColorTranslator.FromHtml(TrayTheme.Background),
            BorderColor = ColorTranslator.FromHtml(TrayTheme.SecondaryButtonBorder),
            BorderThickness = 1,
        };
        button.Size = buttonSize;
        button.Click += onClick;

        var valueRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        valueRow.Controls.Add(valueLabel);
        valueRow.Controls.Add(button);

        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 12),
        };
        row.Controls.Add(captionLabel);
        row.Controls.Add(valueRow);
        return row;
    }

    /// <summary>
    /// Read from the assembly's own <c>InformationalVersion</c>, stamped at
    /// build time from <c>CHANGELOG.md</c>'s topmost <c>## ver-...</c> header
    /// (<c>LunaPanel.Tray.csproj</c>) - never a second, hand-typed copy that
    /// could drift from the file the commander already keeps current.
    /// </summary>
    /// <remarks>
    /// [2026-09-17] Moved into <see cref="RunningVersion"/> rather than
    /// duplicated: the update check needs the same string to decide whether
    /// a published release is newer, and two readers of it could disagree.
    /// </remarks>
    private static string CurrentVersion() => RunningVersion.Current();

    private string CurrentEliteLabel()
    {
        var firstInstall = _discovery.EliteInstallations.FirstOrDefault();
        return firstInstall?.ProductPath ?? "Not found";
    }

    /// <summary>
    /// The EDHM settings path shown to the commander - see this class's own
    /// remarks on why it is recomputed rather than read off
    /// <see cref="PathDiscoveryResult.Edhm"/>.
    /// </summary>
    private string CurrentEdhmSettingsPath()
    {
        var overridePath = _overrides.Load().EdhmSettingsJsonPath;
        if (overridePath is not null)
        {
            return overridePath;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "EDHM-UI-V3", "resources", "data", "Settings.json");
    }

    private string CurrentEdhmLabel() => _discovery.Edhm.SettingsFound ? CurrentEdhmSettingsPath() : "Not found";

    private void OpenLogsFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_layout.LogsDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"LunaPanel could not open the logs folder:{Environment.NewLine}{Environment.NewLine}{_layout.LogsDirectory}{Environment.NewLine}{Environment.NewLine}({ex.Message})",
                "LunaPanel",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private void OnSelectEliteInstall()
    {
        var current = _overrides.Load();

        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the folder containing Elite Dangerous' \"Products\" folder.",
        };
        if (current.EliteInstallPath is not null && Directory.Exists(current.EliteInstallPath))
        {
            dialog.SelectedPath = current.EliteInstallPath;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _overrides.Save(current with { EliteInstallPath = dialog.SelectedPath });
        OfferRestart();
    }

    private void OnSelectEdhmSettingsFile()
    {
        var current = _overrides.Load();

        using var dialog = new OpenFileDialog
        {
            Title = "Select EDHM-UI's Settings.json",
            Filter = "Settings.json|Settings.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
        };

        var existingPath = CurrentEdhmSettingsPath();
        var existingDirectory = Path.GetDirectoryName(existingPath);
        if (!string.IsNullOrEmpty(existingDirectory) && Directory.Exists(existingDirectory))
        {
            dialog.InitialDirectory = existingDirectory;
        }
        if (File.Exists(existingPath))
        {
            dialog.FileName = existingPath;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _overrides.Save(current with { EdhmSettingsJsonPath = dialog.FileName });
        OfferRestart();
    }

    /// <summary>
    /// Same save-then-offer-restart flow as <see cref="PortSettingsForm.OnOk"/> -
    /// no live re-discovery exists for Elite/EDHM, so the new path only takes
    /// effect on the next launch.
    /// </summary>
    private void OfferRestart()
    {
        var restart = MessageBox.Show(
            "LunaPanel needs to restart to use the new path.",
            "LunaPanel",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);

        if (restart == DialogResult.OK)
        {
            Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true });
            _quit();
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TryEnableDarkTitleBar();
    }

    /// <summary>
    /// Same calls as <see cref="PortSettingsForm.TryEnableDarkTitleBar"/> -
    /// see that method's own remarks. Must never throw.
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
