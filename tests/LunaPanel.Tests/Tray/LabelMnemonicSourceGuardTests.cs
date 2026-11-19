namespace LunaPanel.Tests.Tray;

/// <summary>
/// Source-scan pins (<c>LunaPanel.Tray</c> is <c>net10.0-windows</c> and this
/// assembly does not reference it - see <c>TrayFormsPaletteSourceGuardTests</c>)
/// that every Tray <c>Label</c> showing a path or other disk-supplied text
/// sets <c>UseMnemonic = false</c>. A WinForms Label defaults to
/// <c>UseMnemonic = true</c>, so <c>E:\Games\Elite &amp; Co\...</c> renders as
/// <c>Elite  Co</c> with an underlined letter. Each needle is scoped to the
/// one initialiser it guards (from <c>new Label</c> to its closing
/// <c>};</c>), so a <c>UseMnemonic = false</c> on some other control in the
/// same file cannot satisfy it.
/// </summary>
public class LabelMnemonicSourceGuardTests
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

    private static string InitialiserOf(string fileName, string assignmentPrefix)
    {
        var path = Path.Combine(FindRepoRoot(), "src", "LunaPanel.Tray", fileName);
        Assert.True(File.Exists(path), $"Expected {path} to exist.");
        var source = File.ReadAllText(path);

        var start = source.IndexOf(assignmentPrefix + " = new Label", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected '{assignmentPrefix} = new Label' in {fileName}.");
        var end = source.IndexOf("};", start, StringComparison.Ordinal);
        Assert.True(end > start, $"Expected the initialiser in {fileName} to close with '}};'.");
        return source.Substring(start, end - start);
    }

    [Fact]
    public void AboutForm_PathValueLabel_DoesNotEatAmpersands()
    {
        Assert.Contains("UseMnemonic = false", InitialiserOf("AboutForm.cs", "var valueLabel"), StringComparison.Ordinal);
    }

    [Fact]
    public void EliteSetupForm_FoundPathStatusLabel_DoesNotEatAmpersands()
    {
        Assert.Contains("UseMnemonic = false", InitialiserOf("EliteSetupForm.cs", "_statusLabel"), StringComparison.Ordinal);
    }
}
