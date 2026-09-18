using System.Text.RegularExpressions;

namespace LunaPanel.Tests.Tray;

/// <summary>
/// Source-scan pin (repo root located by walking up to <c>LunaPanel.sln</c>,
/// same idiom as <c>ForgetPathSourceGuardTests</c>/<c>PanelClientSourceGuardTests</c>)
/// closing the tray's "Windows 11 dark" restyle reaching <c>AddDeviceForm</c>
/// and <c>DevicesForm</c> - see <c>ref/docs/hosting.md</c>'s tray section.
///
/// <b>Why a source scan and not a behavioural test.</b> <c>LunaPanel.Tray</c>
/// is <c>net10.0-windows</c> and <c>tests/LunaPanel.Tests</c> is plain
/// <c>net10.0</c> and does not reference it, so these three
/// <see cref="System.Windows.Forms.Form"/> classes cannot be constructed or
/// inspected from this suite at all. What can be pinned is the one thing a
/// hex literal or a stray <c>Color.</c> constant would prove regardless -
/// that a form reached for a colour without going through
/// <c>LunaPanel.Server.Tray.TrayTheme</c>, the testable seam.
///
/// <b>Sweeps all three WinForms files, not only the two this dispatch
/// touched</b> - <c>StatusForm</c> is the reference implementation this
/// restyle is supposed to match, and a sweep that only checked the files it
/// changed would say nothing about whether the reference itself later
/// regresses.
/// </summary>
public class TrayFormsPaletteSourceGuardTests
{
    private static readonly Regex HexColorLiteral = new(@"#[0-9A-Fa-f]{6}", RegexOptions.Compiled);
    private static readonly Regex NamedColorConstant = new(@"(?<!Translator)(?<!System\.Drawing\.)\bColor\.[A-Z]", RegexOptions.Compiled);

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

    private static string ReadTraySource(string fileName)
    {
        var path = Path.Combine(FindRepoRoot(), "src", "LunaPanel.Tray", fileName);
        Assert.True(File.Exists(path), $"Expected {path} to exist.");
        return File.ReadAllText(path);
    }

    public static IEnumerable<object[]> TrayFormFiles() =>
        new[] { "StatusForm.cs", "AddDeviceForm.cs", "DevicesForm.cs" }.Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(TrayFormFiles))]
    public void TrayForm_NeverHardcodesAHexColorLiteral(string fileName)
    {
        var content = ReadTraySource(fileName);

        var matches = HexColorLiteral.Matches(content).Select(m => m.Value).ToArray();
        Assert.True(matches.Length == 0, $"{fileName} hardcodes hex colour literal(s) instead of going through TrayTheme: {string.Join(", ", matches)}");
    }

    [Theory]
    [MemberData(nameof(TrayFormFiles))]
    public void TrayForm_NeverUsesANamedColorConstantInsteadOfTrayTheme(string fileName)
    {
        var content = ReadTraySource(fileName);

        var matches = NamedColorConstant.Matches(content).Select(m => m.Value).ToArray();
        Assert.True(matches.Length == 0, $"{fileName} uses a named System.Drawing.Color constant instead of TrayTheme: {string.Join(", ", matches)}");
    }

    /// <summary>
    /// The positive half: each form actually consumes <c>TrayTheme</c>
    /// rather than merely happening to avoid the two patterns above (e.g. by
    /// building colours some third way). Every one of the three files sets
    /// at least a background and a foreground from it.
    /// </summary>
    [Theory]
    [MemberData(nameof(TrayFormFiles))]
    public void TrayForm_ActuallyConsumesTrayTheme(string fileName)
    {
        var content = ReadTraySource(fileName);

        Assert.Contains("TrayTheme.Background", content, StringComparison.Ordinal);
        Assert.Contains("TrayTheme.PrimaryText", content, StringComparison.Ordinal);
    }
}
