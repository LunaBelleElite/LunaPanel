namespace LunaPanel.Tests.Tray;

/// <summary>
/// Source-scan pins for the tray's "Devices" window
/// (<c>src/LunaPanel.Tray/DevicesForm.cs</c>) - see
/// <c>TrayFormsPaletteSourceGuardTests</c>' own remarks for why a source
/// scan rather than a behavioural test is what this suite can do here at
/// all (<c>LunaPanel.Tray</c> is <c>net10.0-windows</c>; this test assembly
/// does not reference it).
///
/// The forget-path pin itself (<c>DeviceForget.Forget</c>, not
/// <c>_registry.Forget</c> directly) already belongs to
/// <c>ForgetPathSourceGuardTests</c> and is not repeated here - this file is
/// only the restyle's own claims.
/// </summary>
public class DevicesFormSourceGuardTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LunaPanel.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate repo root (a directory containing LunaPanel.sln) above {AppContext.BaseDirectory}.");
    }

    private static string ReadSource()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "LunaPanel.Tray", "DevicesForm.cs");
        Assert.True(File.Exists(path), $"Expected {path} to exist.");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// The forget button is styled as the same "secondary" (outlined) role
    /// <c>StatusForm</c>'s own non-primary action buttons already use - not
    /// a bespoke danger/red style. The palette's own rule is no red anywhere
    /// (a missing EDHM install is not an error; nothing here is either).
    /// </summary>
    [Fact]
    public void ForgetButton_UsesTheSecondaryButtonPaletteRoles()
    {
        var content = ReadSource();

        Assert.Contains("TrayTheme.SecondaryButtonFill", content, StringComparison.Ordinal);
        Assert.Contains("TrayTheme.SecondaryButtonBorder", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The list view itself - the window's actual content - is themed too,
    /// not only the button below it.
    /// </summary>
    [Fact]
    public void ListView_IsThemedWithBackgroundAndForeground()
    {
        var content = ReadSource();

        Assert.Contains("_listView = new ListView", content, StringComparison.Ordinal);
        // TrayFormsPaletteSourceGuardTests already pins that this file
        // consumes TrayTheme.Background/PrimaryText at all; this pin adds
        // that the ListView control itself - not just the Form - is the one
        // reading them, so restyling the form's own BackColor without ever
        // touching the ListView (which would still look stock white) cannot
        // pass silently.
        var listViewBlockStart = content.IndexOf("_listView = new ListView", StringComparison.Ordinal);
        var listViewBlockEnd = content.IndexOf("};", listViewBlockStart, StringComparison.Ordinal);
        var listViewBlock = content[listViewBlockStart..listViewBlockEnd];

        Assert.Contains("BackColor = background", listViewBlock, StringComparison.Ordinal);
        Assert.Contains("ForeColor = primaryText", listViewBlock, StringComparison.Ordinal);
    }
}
