using System.Text.Json;
using LunaPanel.Core.GameState;
using LunaPanel.Core.Macros;
using LunaPanel.Server.Macros;

namespace LunaPanel.Tests.Macros;

/// <summary>
/// Drives <see cref="MacroJson.Serialize"/> - the writer a user-authored
/// macro is persisted through (<c>ref/docs/macro-builder.md</c>).
///
/// The pin that matters most here is the round trip against <b>every shipped
/// macro</b>: a grammar addition that forgets to write one of its own fields
/// is invisible to a hand-written example and caught immediately by the
/// shipped macro that actually uses that field. It is the same reason
/// <c>MacroLoaderTests</c> sweeps the shipped set rather than naming one.
/// </summary>
public class MacroJsonTests
{
    /// <summary>
    /// Round trip, macro by macro, over every shipped macro: parse the
    /// shipped text, serialize it, parse THAT, and compare - byte-identical
    /// serializations, and every step equal <b>property by property</b>.
    ///
    /// The byte-identity half alone would not be enough and it is worth
    /// saying why: a writer that dropped one field would drop it on both
    /// passes and stay byte-identical. The property comparison is derived by
    /// reflection over each step record's own properties rather than from a
    /// hand-written list, so a field added to the grammar later and
    /// forgotten by the writer fails here without anybody remembering to
    /// extend this test - which is the failure a hand-written list would
    /// have.
    /// </summary>
    [Fact]
    public void Serialize_EveryShippedMacro_RoundTripsThroughParse_LosingNoField()
    {
        var shipped = MacroLoader.LoadShipped();
        Assert.NotEmpty(shipped);

        foreach (var macro in shipped)
        {
            var once = MacroJson.Serialize(macro);
            var reparsed = MacroDefinition.Parse(once);
            var twice = MacroJson.Serialize(reparsed);

            Assert.Equal(once, twice);
            Assert.Equal(macro.Id, reparsed.Id);
            Assert.Equal(macro.Name, reparsed.Name);
            Assert.Equal(macro.Steps.Count, reparsed.Steps.Count);

            for (var i = 0; i < macro.Steps.Count; i++)
            {
                AssertStepsEquivalent(macro.Id, i, macro.Steps[i], reparsed.Steps[i]);
            }
        }
    }

    /// <summary>
    /// Compares two steps by type and by every public property, elementwise
    /// for the list-valued ones. Records with <c>IReadOnlyList</c> members
    /// compare those by reference under the compiler-generated
    /// <c>Equals</c>, so <c>Assert.Equal(step, step)</c> across two separate
    /// parses would fail for every condition-bearing step and prove nothing
    /// about the ones it passed for.
    /// </summary>
    private static void AssertStepsEquivalent(string macroId, int index, MacroStep expected, MacroStep actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());

        foreach (var property in expected.GetType().GetProperties())
        {
            // The PARSED projections - ConditionList, and the EdgeCondition
            // list - are not part of the round trip: each is built from the
            // step's own token list, which this loop does compare, and both
            // are plain classes with reference equality, so comparing them
            // would fail for two objects carrying identical conditions.
            // Skipping them leaves nothing unguarded: a dropped or altered
            // token shows up in the tokens themselves, one property along.
            // Selected by TYPE rather than by property name, so a third step
            // kind carrying a parsed projection of the same kind is covered
            // without this list being revisited.
            if (property.PropertyType == typeof(ConditionList) ||
                property.PropertyType == typeof(IReadOnlyList<EdgeCondition>))
            {
                continue;
            }

            var expectedValue = property.GetValue(expected);
            var actualValue = property.GetValue(actual);

            // A single EdgeCondition (PressUntilStep.JournalCondition, added
            // 2026-09-13) is the same kind of parsed-projection class as the
            // two skipped above, but unlike ConditionTokens/cond it has no
            // sibling raw-token property elsewhere in the record for this
            // loop to fall through to - "the tokens themselves catch a drop"
            // does not hold for it. A blanket skip here would make the
            // byte-identity check above the ONLY thing standing between a
            // dropped/altered condJournal event name and a silent pass -
            // exactly the vacuous shape this test's own docstring warns
            // against. Compare EventName by value instead of skipping.
            // [Part E, expressive-kindling-starfish.md, 2026-09-16:
            // srv-launch is the first SHIPPED macro to actually carry a
            // JournalCondition, which is what surfaced this gap - nothing
            // shipped exercised this property through this test before.]
            if (property.PropertyType == typeof(EdgeCondition))
            {
                var expectedEdge = (EdgeCondition?)expectedValue;
                var actualEdge = (EdgeCondition?)actualValue;
                Assert.True(
                    expectedEdge?.EventName == actualEdge?.EventName,
                    $"Macro '{macroId}' step {index}: 'JournalCondition' did not survive the round trip " +
                    $"(expected EventName '{expectedEdge?.EventName ?? "null"}', got '{actualEdge?.EventName ?? "null"}').");
                continue;
            }

            // A branch's arms are lists of STEPS, and a MacroStep record with
            // list members (RequireStep, and every condition-bearing kind)
            // compares by reference under the compiler-generated Equals -
            // exactly the trap this method's own docstring describes, one
            // level down. The elementwise Assert.Equal below would therefore
            // fail for two identical arms, and "fix" it by loosening would
            // have silently stopped checking arm contents at all. Recurse
            // instead, so an arm's steps get the same property-by-property
            // treatment a top-level step gets.
            // [2026-09-17: branch is the grammar's first step kind that
            // contains other steps.]
            if (expectedValue is IReadOnlyList<MacroStep> expectedSteps)
            {
                var actualSteps = Assert.IsAssignableFrom<IReadOnlyList<MacroStep>>(actualValue);
                Assert.Equal(expectedSteps.Count, actualSteps.Count);
                for (var i = 0; i < expectedSteps.Count; i++)
                {
                    AssertStepsEquivalent(macroId, index, expectedSteps[i], actualSteps[i]);
                }

                continue;
            }

            if (expectedValue is System.Collections.IEnumerable expectedList and not string)
            {
                Assert.Equal(
                    expectedList.Cast<object>().ToList(),
                    ((System.Collections.IEnumerable)actualValue!).Cast<object>().ToList());
                continue;
            }

            Assert.True(
                Equals(expectedValue, actualValue),
                $"Macro '{macroId}' step {index}: '{property.Name}' did not survive the round trip (expected {expectedValue ?? "null"}, got {actualValue ?? "null"}).");
        }
    }

    /// <summary>
    /// The round-trip test above proves stability, not correctness - a
    /// writer that dropped every step's detail equally on both passes would
    /// still be byte-identical. This pins the actual step content survives,
    /// for one macro of each shipped shape.
    /// </summary>
    [Fact]
    public void Serialize_ThenParse_PreservesEveryStepsFields()
    {
        var source = MacroDefinition.Parse("""
            {
              "id": "round-trip",
              "name": "Round\nTrip",
              "steps": [
                { "require": ["Docked", "!Landed", "GuiFocus:NoFocus"] },
                { "press": "UI_Down", "repeat": 3, "holdMs": 220 },
                { "press": "UI_Select", "repeat": 1 },
                { "pressKey": "Key_F5", "repeat": 2, "holdMs": 300 },
                { "pressKey": "Key_F6", "repeat": 1 },
                { "wait": 150 },
                { "gotoLeftPanelTab": "Contacts" },
                { "waitFor": ["Supercruise"], "timeoutMs": 2000 },
                { "pressUntil": "LandingGearToggle", "cond": ["LandingGearDown"], "timeoutMs": 800, "maxAttempts": 3 },
                { "waitForEdge": ["Journal:DockingGranted"], "failOn": ["Journal:DockingDenied"], "failureDetailField": "Reason", "timeoutMs": 45000 }
              ]
            }
            """);

        var result = MacroDefinition.Parse(MacroJson.Serialize(source));

        Assert.Equal("round-trip", result.Id);
        Assert.Equal("Round\nTrip", result.Name);
        Assert.Equal(10, result.Steps.Count);

        var require = Assert.IsType<RequireStep>(result.Steps[0]);
        Assert.Equal(new[] { "Docked", "!Landed", "GuiFocus:NoFocus" }, require.ConditionTokens);

        var press = Assert.IsType<PressStep>(result.Steps[1]);
        Assert.Equal("UI_Down", press.Action);
        Assert.Equal(3, press.Repeat);
        Assert.Equal(TimeSpan.FromMilliseconds(220), press.Hold);

        var pressNoHold = Assert.IsType<PressStep>(result.Steps[2]);
        Assert.Equal("UI_Select", pressNoHold.Action);
        Assert.Equal(1, pressNoHold.Repeat);
        Assert.Null(pressNoHold.Hold);

        var pressKey = Assert.IsType<PressKeyStep>(result.Steps[3]);
        Assert.Equal("Key_F5", pressKey.Key);
        Assert.Equal(2, pressKey.Repeat);
        Assert.Equal(TimeSpan.FromMilliseconds(300), pressKey.Hold);

        var pressKeyNoHold = Assert.IsType<PressKeyStep>(result.Steps[4]);
        Assert.Equal("Key_F6", pressKeyNoHold.Key);
        Assert.Equal(1, pressKeyNoHold.Repeat);
        Assert.Null(pressKeyNoHold.Hold);

        var wait = Assert.IsType<WaitStep>(result.Steps[5]);
        Assert.Equal(TimeSpan.FromMilliseconds(150), wait.Duration);

        var goTo = Assert.IsType<GotoLeftPanelTabStep>(result.Steps[6]);
        Assert.Equal(PanelTab.Contacts, goTo.Target);

        var waitFor = Assert.IsType<WaitForStep>(result.Steps[7]);
        Assert.Equal(new[] { "Supercruise" }, waitFor.ConditionTokens);
        Assert.Equal(TimeSpan.FromMilliseconds(2000), waitFor.Timeout);

        var pressUntil = Assert.IsType<PressUntilStep>(result.Steps[8]);
        Assert.Equal("LandingGearToggle", pressUntil.Action);
        Assert.Equal(new[] { "LandingGearDown" }, pressUntil.ConditionTokens);
        Assert.Equal(TimeSpan.FromMilliseconds(800), pressUntil.Timeout);
        Assert.Equal(3, pressUntil.MaxAttempts);

        var edge = Assert.IsType<WaitForEdgeStep>(result.Steps[9]);
        Assert.Equal(new[] { "Journal:DockingGranted" }, edge.SucceedOnTokens);
        Assert.Equal(new[] { "Journal:DockingDenied" }, edge.FailOnTokens);
        Assert.Equal("Reason", edge.FailureDetailField);
        Assert.Equal(TimeSpan.FromMilliseconds(45000), edge.Timeout);
    }

    /// <summary>
    /// A macro with no <c>name</c> writes no <c>name</c> property at all -
    /// absent, not an explicit <c>null</c> or an empty string, because
    /// <c>MacroKnowledge.NameFor</c>'s whole "absent means fall back to
    /// <c>Prettify(id)</c>" convention reads a null <c>Name</c>, and an
    /// empty string would label a button with nothing at all.
    /// </summary>
    [Fact]
    public void Serialize_MacroWithNoName_OmitsTheNameProperty_RatherThanWritingNull()
    {
        var source = MacroDefinition.Parse("""
            { "id": "nameless", "steps": [ { "wait": 10 } ] }
            """);

        var json = MacroJson.Serialize(source);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("name", out _));
        Assert.Null(MacroDefinition.Parse(json).Name);
    }

    /// <summary>
    /// A <c>branch</c> survives the round trip with both arms intact, step
    /// by step - the writer has to recurse, and a writer that emitted the
    /// condition tokens but dropped the arms would produce a file
    /// <c>MacroDefinition.Parse</c> rejects outright ("must have at least one
    /// step in 'then' or 'else'"), so this asserts the arms' CONTENT rather
    /// than only that a parse succeeded.
    /// </summary>
    [Fact]
    public void Serialize_ThenParse_PreservesABranchsConditionAndBothArms()
    {
        var source = MacroDefinition.Parse("""
            {
              "id": "branchy",
              "steps": [
                {
                  "branch": ["Docked"],
                  "then": [ { "require": ["GuiFocus:NoFocus"] }, { "press": "UI_Down", "repeat": 3 } ],
                  "else": [ { "press": "FocusRadarPanel", "repeat": 1 }, { "wait": 150 } ]
                }
              ]
            }
            """);

        var result = MacroDefinition.Parse(MacroJson.Serialize(source));

        var branch = Assert.IsType<BranchStep>(Assert.Single(result.Steps));
        Assert.Equal(new[] { "Docked" }, branch.ConditionTokens);

        Assert.Equal(2, branch.Then.Count);
        Assert.Equal(new[] { "GuiFocus:NoFocus" }, Assert.IsType<RequireStep>(branch.Then[0]).ConditionTokens);
        var thenPress = Assert.IsType<PressStep>(branch.Then[1]);
        Assert.Equal("UI_Down", thenPress.Action);
        Assert.Equal(3, thenPress.Repeat);

        Assert.Equal(2, branch.Else.Count);
        Assert.Equal("FocusRadarPanel", Assert.IsType<PressStep>(branch.Else[0]).Action);
        Assert.Equal(TimeSpan.FromMilliseconds(150), Assert.IsType<WaitStep>(branch.Else[1]).Duration);
    }

    /// <summary>
    /// An empty arm is <b>omitted</b>, not written as <c>[]</c> - the same
    /// omit-when-absent convention this writer already follows for every
    /// other optional field, and the reason the parser reads absence as an
    /// empty arm rather than requiring both.
    /// </summary>
    [Fact]
    public void Serialize_BranchWithOneEmptyArm_OmitsThatArm_RatherThanWritingAnEmptyArray()
    {
        var source = MacroDefinition.Parse("""
            { "id": "one-armed", "steps": [ { "branch": ["Docked"], "then": [ { "wait": 10 } ] } ] }
            """);

        var json = MacroJson.Serialize(source);

        using var document = JsonDocument.Parse(json);
        var step = document.RootElement.GetProperty("steps")[0];
        Assert.True(step.TryGetProperty("then", out _));
        Assert.False(step.TryGetProperty("else", out _));
    }

    /// <summary>
    /// The intent-never-resolution rule (<c>ref/docs/layouts.md</c>) applied
    /// to a stored macro: an action name is written through <b>verbatim</b>,
    /// never normalized, prettified, or dropped - including one that names
    /// nothing real, which is the case that can actually distinguish a
    /// pass-through writer from one that tried to be clever.
    ///
    /// A "the file contains no resolved chord" assertion is deliberately NOT
    /// made here: <see cref="MacroJson.Serialize"/> takes no
    /// <c>BindingsFile</c> at all, so it has nothing to resolve <em>with</em>
    /// and such an assertion could never fail. The structural fact is the
    /// guarantee; a string search for it would only look like one.
    /// </summary>
    [Fact]
    public void Serialize_WritesAnActionNameVerbatim_EvenOneThatResolvesToNothing()
    {
        var source = MacroDefinition.Parse("""
            { "id": "intent", "steps": [ { "press": "NotARealFrontierAction", "repeat": 1 } ] }
            """);

        var json = MacroJson.Serialize(source);

        using var document = JsonDocument.Parse(json);
        var step = document.RootElement.GetProperty("steps")[0];
        Assert.Equal("NotARealFrontierAction", step.GetProperty("press").GetString());
    }
}
