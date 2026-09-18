using LunaPanel.Core.GameState;
using LunaPanel.Core.Macros;

namespace LunaPanel.Tests.Macros;

/// <summary>
/// Sweep: every shipped macro definition under
/// <c>src/LunaPanel.Server/definitions/macros/</c> is parsed, and every
/// action name referenced by a <c>press</c> or <c>pressUntil</c> step must
/// exist verbatim in <c>Fixtures/bindings/action-names.txt</c> - the same
/// real-Frontier-vocabulary check <c>CatalogueTests</c> runs over the
/// curated catalogue.
///
/// <see cref="MacroDefinition.Parse"/> only validates an action name is
/// non-empty at load time (Core has no vocabulary of real Frontier action
/// names to check against there - see the comment on
/// <c>MacroDefinition.RequireNonEmptyString</c>), so a typo'd-but-non-blank
/// action name in a shipped macro loads happily and only fails at press
/// time, in front of a user. This test closes that gap for every macro
/// LunaPanel actually ships.
///
/// The macro directory is enumerated dynamically (every <c>*.json</c> file
/// found there), not against a hardcoded list of filenames, so a macro
/// added later is covered automatically without anyone remembering to
/// update this test. The repo root is located relative to this test
/// assembly (by walking up to find <c>LunaPanel.sln</c>), the same pattern
/// <c>CoreNeedleGuardTests</c> uses - not hardcoded, so this still works
/// from a clone.
///
/// The macro's own step *sequence* (what presses happen in what order) is a
/// design choice this test does not and cannot validate - only the
/// Frontier action names the sequence references.
/// </summary>
public class MacroActionNameSweepTests
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

    private static IReadOnlySet<string> ReadActionNamesVocabulary()
    {
        var fixturesRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var lines = File.ReadAllLines(Path.Combine(fixturesRoot, "bindings", "action-names.txt"));
        return lines
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            .Select(line => line.Trim())
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void ShippedMacros_EveryPressActionName_ExistsInActionNamesTxt()
    {
        var macrosDir = Path.Combine(FindRepoRoot(), "src", "LunaPanel.Server", "definitions", "macros");
        Assert.True(Directory.Exists(macrosDir), $"Expected {macrosDir} to exist.");

        var macroFiles = Directory.EnumerateFiles(macrosDir, "*.json", SearchOption.TopDirectoryOnly).ToList();
        Assert.True(macroFiles.Count > 0, $"Expected at least one shipped macro definition under {macrosDir}.");

        var vocabulary = ReadActionNamesVocabulary();
        var violations = new List<string>();

        foreach (var file in macroFiles)
        {
            var macro = MacroDefinition.Parse(File.ReadAllText(file));

            for (var i = 0; i < macro.Steps.Count; i++)
            {
                var action = macro.Steps[i] switch
                {
                    PressStep press => press.Action,
                    PressUntilStep pressUntil => pressUntil.Action,
                    // gotoLeftPanelTab names no action of its own in JSON,
                    // but it always presses PanelTabTracker.AdvanceTabAction
                    // at run time when a press is actually needed - swept
                    // here for the same reason MacroKnowledgeBuilder checks
                    // it (see that class's own remarks): an implicit action
                    // is still an action a macro can fail to fire.
                    GotoLeftPanelTabStep => PanelTabTracker.AdvanceTabAction,
                    _ => null
                };

                if (action is not null && !vocabulary.Contains(action))
                {
                    violations.Add(
                        $"macro '{macro.Id}' (file '{Path.GetFileName(file)}') step {i}: action name '{action}' not found in action-names.txt");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Typo'd Frontier action name(s) in shipped macro definition(s):\n" + string.Join("\n", violations));
    }
}
