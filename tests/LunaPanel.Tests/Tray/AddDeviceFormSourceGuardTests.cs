namespace LunaPanel.Tests.Tray;

/// <summary>
/// Source-scan pins for the tray's "Add a device" window
/// (<c>src/LunaPanel.Tray/AddDeviceForm.cs</c>) - see
/// <c>TrayFormsPaletteSourceGuardTests</c>' own remarks for why a source
/// scan rather than a behavioural test is what this suite can do here at
/// all (<c>LunaPanel.Tray</c> is <c>net10.0-windows</c>; this test assembly
/// does not reference it).
/// </summary>
public class AddDeviceFormSourceGuardTests
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
        var path = Path.Combine(FindRepoRoot(), "src", "LunaPanel.Tray", "AddDeviceForm.cs");
        Assert.True(File.Exists(path), $"Expected {path} to exist.");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// The full instruction, not the width-truncated prefix a commander
    /// actually saw on screen ("On the new device, open LunaPanel and").
    /// The literal string was always complete in source - the defect was in
    /// the label's allocated height, not the text - so this alone would not
    /// have caught the original bug; it guards the sentence itself against a
    /// future edit that shortens or rewords it without noticing.
    /// </summary>
    [Fact]
    public void Instruction_FullSentenceIsPresent()
    {
        var content = ReadSource();

        Assert.Contains("On the new device, open LunaPanel and enter this code:", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The actual defect: a hardcoded pixel <c>Height</c> fit the label's
    /// old text at the old width and silently stopped fitting once either
    /// changed. The fix measures the real wrapped height for the label's
    /// current text/font/width instead of guessing a constant - this pins
    /// that the measurement is still there and that the old style of magic
    /// number has not come back.
    /// </summary>
    [Fact]
    public void Label_MeasuresItsRealHeight_DoesNotHardcodeTheOldFixedPixelValue()
    {
        var content = ReadSource();

        Assert.Contains("TextRenderer.MeasureText(", content, StringComparison.Ordinal);
        Assert.Contains("TextFormatFlags.WordBreak", content, StringComparison.Ordinal);
        Assert.Contains("label.Height = measured.Height", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Height = 48", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pairing code box no longer selects itself on <c>Shown</c>
    /// (reported three times as looking accidental), but stays a read-only
    /// <see cref="System.Windows.Forms.TextBox"/> - not a
    /// <see cref="System.Windows.Forms.Label"/> - so it remains selectable
    /// and copyable by hand. Pasting the code with its grouping space is a
    /// real, supported path (<c>PairEndpoint.NormalizeCode</c>,
    /// <c>ref/docs/hosting.md</c>), so selectability itself must survive
    /// even though the auto-selection does not.
    /// </summary>
    [Fact]
    public void CodeBox_NoLongerAutoSelectsOnShow_ButStaysAReadOnlySelectableTextBox()
    {
        var content = ReadSource();

        Assert.DoesNotContain("SelectAll()", content, StringComparison.Ordinal);
        Assert.Contains("new TextBox", content, StringComparison.Ordinal);
        Assert.Contains("ReadOnly = true", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The first fix (2026-09-08) removed <c>SelectAll()</c> but left a
    /// <c>Shown += (_, _) =&gt; codeBox.Focus();</c> line, which reproduces
    /// the exact same full-text selection - a plain
    /// <see cref="System.Windows.Forms.TextBox"/> selects all of its text
    /// whenever it receives keyboard focus programmatically, whether that
    /// focus comes from an explicit <c>Focus()</c> call or not. The prior
    /// pin (<see cref="CodeBox_NoLongerAutoSelectsOnShow_ButStaysAReadOnlySelectableTextBox"/>)
    /// only forbade the literal string <c>"SelectAll()"</c> and could not
    /// see this - the commander reported the same selection a third time
    /// with that pin still green. This one forbids <c>.Focus()</c>
    /// anywhere in this file at all, which the earlier pin's needle could
    /// never catch since the regressing line never contained it.
    /// </summary>
    [Fact]
    public void CodeBox_IsNeverProgrammaticallyFocused()
    {
        var content = ReadSource();

        Assert.DoesNotContain(".Focus()", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// Forbidding <c>.Focus()</c> alone is not sufficient: measured
    /// directly (2026-09-09), WinForms auto-focuses the first tab-stop
    /// control when a form is shown even with no <c>Focus()</c> call
    /// anywhere in the file, and that default auto-focus triggers the
    /// identical full-text selection. <c>TabStop = false</c> is what
    /// actually removes the code box from that path; this pin guards that
    /// the real fix, not just the absence of the old symptom, stays in
    /// place.
    /// </summary>
    [Fact]
    public void CodeBox_IsRemovedFromDefaultInitialFocus()
    {
        var content = ReadSource();

        Assert.Contains("TabStop = false", content, StringComparison.Ordinal);
    }
}
