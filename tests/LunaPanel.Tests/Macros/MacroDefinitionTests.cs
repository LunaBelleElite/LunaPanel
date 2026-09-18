using LunaPanel.Core.GameState;
using LunaPanel.Core.Macros;

namespace LunaPanel.Tests.Macros;

/// <summary>
/// Pins <see cref="MacroDefinition.Parse"/> against every step kind in the
/// grammar and every load-time validation failure the task brief calls for:
/// two kinds on one step, no kind, a blank/missing action name, an
/// unparseable condition token, and a non-positive <c>repeat</c>/<c>maxAttempts</c>/
/// <c>timeoutMs</c>. Like <c>LunaPanel.Core.Catalogue.Catalogue.Parse</c>,
/// this loader throws on anything malformed rather than returning a result -
/// a macro definition is shipped content, not runtime data from the
/// player's machine.
/// </summary>
public class MacroDefinitionTests
{
    private const string ValidId = "test-macro";

    [Fact]
    public void Parse_AllFiveStepKinds_ProducesCorrectlyTypedSteps_InOrder()
    {
        var json = """
        {
          "id": "all-kinds",
          "name": "All Kinds",
          "steps": [
            { "press": "SomeAction", "repeat": 3, "holdMs": 200 },
            { "wait": 500 },
            { "require": ["Docked"] },
            { "waitFor": ["!Docked"], "timeoutMs": 4000 },
            { "pressUntil": "ToggleThing", "cond": ["LandingGearDown"], "timeoutMs": 2000, "maxAttempts": 3 }
          ]
        }
        """;

        var macro = MacroDefinition.Parse(json);

        Assert.Equal("all-kinds", macro.Id);
        Assert.Equal("All Kinds", macro.Name);
        Assert.Equal(5, macro.Steps.Count);

        var press = Assert.IsType<PressStep>(macro.Steps[0]);
        Assert.Equal("SomeAction", press.Action);
        Assert.Equal(3, press.Repeat);
        Assert.Equal(TimeSpan.FromMilliseconds(200), press.Hold);

        var wait = Assert.IsType<WaitStep>(macro.Steps[1]);
        Assert.Equal(TimeSpan.FromMilliseconds(500), wait.Duration);

        var require = Assert.IsType<RequireStep>(macro.Steps[2]);
        Assert.Equal(new[] { "Docked" }, require.ConditionTokens);

        var waitFor = Assert.IsType<WaitForStep>(macro.Steps[3]);
        Assert.Equal(new[] { "!Docked" }, waitFor.ConditionTokens);
        Assert.Equal(TimeSpan.FromMilliseconds(4000), waitFor.Timeout);

        var pressUntil = Assert.IsType<PressUntilStep>(macro.Steps[4]);
        Assert.Equal("ToggleThing", pressUntil.Action);
        Assert.Equal(new[] { "LandingGearDown" }, pressUntil.ConditionTokens);
        Assert.Equal(TimeSpan.FromMilliseconds(2000), pressUntil.Timeout);
        Assert.Equal(3, pressUntil.MaxAttempts);
    }

    [Fact]
    public void Parse_PressStep_NoHoldMs_HoldIsNull()
    {
        var macro = MacroDefinition.Parse(SingleStepJson("""{ "press": "A", "repeat": 1 }"""));

        var press = Assert.IsType<PressStep>(macro.Steps[0]);
        Assert.Null(press.Hold);
    }

    [Fact]
    public void Parse_NotValidJson_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => MacroDefinition.Parse("not json at all"));
    }

    [Fact]
    public void Parse_RootIsNotAnObject_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => MacroDefinition.Parse("[1,2,3]"));
    }

    [Fact]
    public void Parse_MissingId_ThrowsFormatException()
    {
        var json = """{ "steps": [ { "wait": 10 } ] }""";

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_BlankId_ThrowsFormatException()
    {
        var json = """{ "id": "   ", "steps": [ { "wait": 10 } ] }""";

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_MissingSteps_ThrowsFormatException_NamingTheMacroId()
    {
        var json = """{ "id": "no-steps" }""";

        var ex = Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
        Assert.Contains("no-steps", ex.Message);
    }

    [Fact]
    public void Parse_EmptyStepsArray_ThrowsFormatException()
    {
        var json = """{ "id": "empty-steps", "steps": [] }""";

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_StepWithTwoKinds_ThrowsFormatException_NamingMacroAndStepIndex()
    {
        var json = SingleStepJson("""{ "wait": 10, "require": ["Docked"] }""");

        var ex = Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
        Assert.Contains(ValidId, ex.Message);
        Assert.Contains("step 0", ex.Message);
    }

    [Fact]
    public void Parse_StepWithNoKind_ThrowsFormatException_NamingMacroAndStepIndex()
    {
        var json = SingleStepJson("""{ "foo": "bar" }""");

        var ex = Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
        Assert.Contains(ValidId, ex.Message);
        Assert.Contains("step 0", ex.Message);
    }

    [Fact]
    public void Parse_SecondStep_InvalidKind_NamesStepIndexOne_NotZero()
    {
        var json = $$"""
        {
          "id": "{{ValidId}}",
          "steps": [
            { "wait": 10 },
            { "foo": "bar" }
          ]
        }
        """;

        var ex = Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
        Assert.Contains("step 1", ex.Message);
    }

    [Fact]
    public void Parse_PressStep_MissingAction_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "press": "", "repeat": 1 }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_PressStep_NonPositiveRepeat_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "press": "A", "repeat": 0 }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_PressStep_NegativeRepeat_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "press": "A", "repeat": -1 }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_PressStep_NonPositiveHoldMs_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "press": "A", "repeat": 1, "holdMs": 0 }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_PressStep_MissingRepeat_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "press": "A" }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    // ---------------------------------------------------------------
    // pressKey - "press a key" (ref/docs/macros.md): a raw physical key,
    // distinct from press's Frontier action name.
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_PressKeyStep_ProducesCorrectlyTypedStep()
    {
        var macro = MacroDefinition.Parse(SingleStepJson("""{ "pressKey": "Key_F5", "repeat": 2, "holdMs": 300 }"""));

        var step = Assert.IsType<PressKeyStep>(macro.Steps[0]);
        Assert.Equal("Key_F5", step.Key);
        Assert.Equal(2, step.Repeat);
        Assert.Equal(TimeSpan.FromMilliseconds(300), step.Hold);
    }

    [Fact]
    public void Parse_PressKeyStep_NoHoldMs_HoldIsNull()
    {
        var macro = MacroDefinition.Parse(SingleStepJson("""{ "pressKey": "Key_F5", "repeat": 1 }"""));

        var step = Assert.IsType<PressKeyStep>(macro.Steps[0]);
        Assert.Null(step.Hold);
    }

    [Fact]
    public void Parse_PressKeyStep_MissingKey_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "pressKey": "", "repeat": 1 }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_PressKeyStep_UnrecognizedKey_ThrowsFormatException_NamingTheKey()
    {
        var json = SingleStepJson("""{ "pressKey": "Key_NotARealKey", "repeat": 1 }""");

        var ex = Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
        Assert.Contains("Key_NotARealKey", ex.Message);
    }

    [Fact]
    public void Parse_PressKeyStep_MissingRepeat_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "pressKey": "Key_F5" }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_PressKeyStep_NonPositiveRepeat_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "pressKey": "Key_F5", "repeat": 0 }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_WaitStep_NegativeMs_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "wait": -1 }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_WaitStep_ZeroMs_IsValid()
    {
        var macro = MacroDefinition.Parse(SingleStepJson("""{ "wait": 0 }"""));

        Assert.Equal(TimeSpan.Zero, Assert.IsType<WaitStep>(macro.Steps[0]).Duration);
    }

    [Fact]
    public void Parse_RequireStep_UnparseableCondition_ThrowsFormatException_NamingMacroAndStep()
    {
        var json = SingleStepJson("""{ "require": ["NotARealCondition"] }""");

        var ex = Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
        Assert.Contains(ValidId, ex.Message);
        Assert.Contains("step 0", ex.Message);
        Assert.Contains("NotARealCondition", ex.Message);
    }

    [Fact]
    public void Parse_RequireStep_MissingArray_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "require": "Docked" }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    // ---------------------------------------------------------------
    // require: LeftPanelTabKnown - REMOVED 2026-09-08 at the user's explicit
    // governing decision reversing refuse-when-unknown ("We should never NOT
    // be allowed to use a macro." - ref/docs/panel-tab-tracking.md, "Reversed
    // 2026-09-08"). The token is no longer meta-syntax RequireStep
    // understands - it now falls straight through to ConditionList.Parse
    // like any other require token and is rejected there as an unknown
    // status condition name. A silently-ignored token would read as working
    // when it no longer does anything, which is exactly what these tests
    // guard against.
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_RequireStep_LeftPanelTabKnownToken_IsRejectedAsAnUnknownCondition_NotSilentlyIgnored()
    {
        var json = SingleStepJson("""{ "require": ["LeftPanelTabKnown"] }""");

        var ex = Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
        Assert.Contains(ValidId, ex.Message);
        Assert.Contains("step 0", ex.Message);
        Assert.Contains("LeftPanelTabKnown", ex.Message);
    }

    [Fact]
    public void Parse_RequireStep_LeftPanelTabKnownAlongsideAStatusCondition_StillRejected()
    {
        var json = SingleStepJson("""{ "require": ["GuiFocus:NoFocus", "LeftPanelTabKnown"] }""");

        var ex = Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
        Assert.Contains("LeftPanelTabKnown", ex.Message);
    }

    [Fact]
    public void Parse_WaitForStep_MissingTimeoutMs_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "waitFor": ["Docked"] }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_WaitForStep_NonPositiveTimeoutMs_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "waitFor": ["Docked"], "timeoutMs": 0 }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_WaitForStep_UnparseableCondition_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "waitFor": ["GuiFocus:NotAPanel"], "timeoutMs": 100 }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_PressUntilStep_MissingAction_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "pressUntil": "", "cond": ["Docked"], "timeoutMs": 100, "maxAttempts": 2 }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_PressUntilStep_NonPositiveMaxAttempts_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "pressUntil": "A", "cond": ["Docked"], "timeoutMs": 100, "maxAttempts": 0 }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_PressUntilStep_NonPositiveTimeoutMs_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "pressUntil": "A", "cond": ["Docked"], "timeoutMs": -5, "maxAttempts": 2 }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_PressUntilStep_MissingCond_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "pressUntil": "A", "timeoutMs": 100, "maxAttempts": 2 }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_PressUntilStep_UnparseableCondition_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "pressUntil": "A", "cond": ["!!Docked"], "timeoutMs": 100, "maxAttempts": 2 }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    // ---------------------------------------------------------------
    // pressUntil - condJournal (journal-event gate, added 2026-09-13,
    // mutually exclusive with `cond`)
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_PressUntilStep_CondJournalAlone_ParsesToTheMatchingEdgeCondition()
    {
        var json = SingleStepJson("""{ "pressUntil": "A", "condJournal": "RefuelAll", "timeoutMs": 100, "maxAttempts": 2 }""");

        var macro = MacroDefinition.Parse(json);

        var pressUntil = Assert.IsType<PressUntilStep>(macro.Steps[0]);
        Assert.Equal("A", pressUntil.Action);
        Assert.Null(pressUntil.Conditions);
        Assert.Null(pressUntil.ConditionTokens);
        Assert.NotNull(pressUntil.JournalCondition);
        Assert.Equal("RefuelAll", pressUntil.JournalCondition!.EventName);
        Assert.Equal(TimeSpan.FromMilliseconds(100), pressUntil.Timeout);
        Assert.Equal(2, pressUntil.MaxAttempts);
    }

    [Fact]
    public void Parse_PressUntilStep_CondAlone_StillWorksExactlyAsBefore()
    {
        var json = SingleStepJson("""{ "pressUntil": "A", "cond": ["Docked"], "timeoutMs": 100, "maxAttempts": 2 }""");

        var macro = MacroDefinition.Parse(json);

        var pressUntil = Assert.IsType<PressUntilStep>(macro.Steps[0]);
        Assert.Equal(new[] { "Docked" }, pressUntil.ConditionTokens);
        Assert.NotNull(pressUntil.Conditions);
        Assert.Null(pressUntil.JournalCondition);
    }

    [Fact]
    public void Parse_PressUntilStep_BothCondAndCondJournal_ThrowsFormatException()
    {
        var json = SingleStepJson(
            """{ "pressUntil": "A", "cond": ["Docked"], "condJournal": "RefuelAll", "timeoutMs": 100, "maxAttempts": 2 }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_PressUntilStep_NeitherCondNorCondJournal_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "pressUntil": "A", "timeoutMs": 100, "maxAttempts": 2 }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_PressUntilStep_CondJournal_UnknownEventName_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "pressUntil": "A", "condJournal": "NotARealEvent", "timeoutMs": 100, "maxAttempts": 2 }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    // ---------------------------------------------------------------
    // waitForEdge (built for request-docking's Grant/Deny acknowledgement,
    // and nomad-dock-launch's LaunchVessel-or-DockSRV completion -
    // ref/docs/panel-tab-tracking.md)
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_WaitForEdgeStep_SucceedOnOnly_ProducesCorrectlyTypedStep()
    {
        var json = SingleStepJson("""{ "waitForEdge": ["Journal:LaunchVessel", "Journal:DockSRV"] }""");

        var macro = MacroDefinition.Parse(json);

        var step = Assert.IsType<WaitForEdgeStep>(macro.Steps[0]);
        Assert.Equal(new[] { "Journal:LaunchVessel", "Journal:DockSRV" }, step.SucceedOnTokens);
        Assert.Equal(2, step.SucceedOn.Count);
        Assert.Equal("LaunchVessel", step.SucceedOn[0].EventName);
        Assert.Equal("DockSRV", step.SucceedOn[1].EventName);
        Assert.Empty(step.FailOn);
        Assert.Empty(step.FailOnTokens);
        Assert.Null(step.FailureDetailField);
    }

    [Fact]
    public void Parse_WaitForEdgeStep_WithFailOnAndFailureDetailField_ProducesCorrectlyTypedStep()
    {
        var json = SingleStepJson(
            """{ "waitForEdge": ["Journal:DockingGranted"], "failOn": ["Journal:DockingDenied"], "failureDetailField": "Reason" }""");

        var macro = MacroDefinition.Parse(json);

        var step = Assert.IsType<WaitForEdgeStep>(macro.Steps[0]);
        Assert.Equal(new[] { "Journal:DockingGranted" }, step.SucceedOnTokens);
        Assert.Equal(new[] { "Journal:DockingDenied" }, step.FailOnTokens);
        Assert.Equal("DockingDenied", Assert.Single(step.FailOn).EventName);
        Assert.Equal("Reason", step.FailureDetailField);
    }

    [Fact]
    public void Parse_WaitForEdgeStep_EmptySucceedOn_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "waitForEdge": [] }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_WaitForEdgeStep_SucceedOnNotAnArray_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "waitForEdge": "Journal:FSDJump" }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_WaitForEdgeStep_UnknownEventName_ThrowsFormatException_NamingTheToken()
    {
        var json = SingleStepJson("""{ "waitForEdge": ["Journal:NotARealEvent"] }""");

        var ex = Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
        Assert.Contains("NotARealEvent", ex.Message);
    }

    [Fact]
    public void Parse_WaitForEdgeStep_LevelConditionInsteadOfEdge_ThrowsFormatException()
    {
        // "Docked" (no "Journal:" prefix) is a level condition, not an edge -
        // Condition and EdgeCondition are deliberately separate grammars
        // (gamestate.md's "Why EdgeCondition is a separate type").
        var json = SingleStepJson("""{ "waitForEdge": ["Docked"] }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_WaitForEdgeStep_FailOnUnknownEventName_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "waitForEdge": ["Journal:FSDJump"], "failOn": ["Journal:NotARealEvent"] }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_WaitForEdgeStep_FailOnNotAnArray_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "waitForEdge": ["Journal:FSDJump"], "failOn": "Journal:FSDJump" }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_WaitForEdgeStep_BlankFailureDetailField_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "waitForEdge": ["Journal:FSDJump"], "failureDetailField": "   " }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    // ---------------------------------------------------------------
    // waitForEdge: timeoutMs (O28, 2026-09-08 - see WaitForEdgeStep's and
    // MacroTimingDefaults.DefaultWaitForEdgeTimeout's own remarks)
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_WaitForEdgeStep_NoTimeoutMs_DefaultsToDefaultWaitForEdgeTimeout()
    {
        var json = SingleStepJson("""{ "waitForEdge": ["Journal:FSDJump"] }""");

        var macro = MacroDefinition.Parse(json);

        var step = Assert.IsType<WaitForEdgeStep>(macro.Steps[0]);
        Assert.Equal(MacroTimingDefaults.DefaultWaitForEdgeTimeout, step.Timeout);
    }

    [Fact]
    public void Parse_WaitForEdgeStep_ExplicitTimeoutMs_OverridesTheDefault()
    {
        var json = SingleStepJson("""{ "waitForEdge": ["Journal:FSDJump"], "timeoutMs": 5000 }""");

        var macro = MacroDefinition.Parse(json);

        var step = Assert.IsType<WaitForEdgeStep>(macro.Steps[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(5000), step.Timeout);
        Assert.NotEqual(MacroTimingDefaults.DefaultWaitForEdgeTimeout, step.Timeout);
    }

    [Fact]
    public void Parse_WaitForEdgeStep_ZeroTimeoutMs_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "waitForEdge": ["Journal:FSDJump"], "timeoutMs": 0 }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_WaitForEdgeStep_NegativeTimeoutMs_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "waitForEdge": ["Journal:FSDJump"], "timeoutMs": -1 }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    // ---------------------------------------------------------------
    // gotoLeftPanelTab (built for request-docking - ref/docs/panel-tab-tracking.md)
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_GotoLeftPanelTabStep_ProducesCorrectlyTypedStep()
    {
        var json = SingleStepJson("""{ "gotoLeftPanelTab": "Contacts" }""");

        var macro = MacroDefinition.Parse(json);

        var step = Assert.IsType<GotoLeftPanelTabStep>(macro.Steps[0]);
        Assert.Equal(PanelTab.Contacts, step.Target);
    }

    [Theory]
    [InlineData("Galaxy", PanelTab.Galaxy)]
    [InlineData("Navigation", PanelTab.Navigation)]
    [InlineData("Transactions", PanelTab.Transactions)]
    [InlineData("Contacts", PanelTab.Contacts)]
    public void Parse_GotoLeftPanelTabStep_EveryTabName_Parses(string name, PanelTab expected)
    {
        var macro = MacroDefinition.Parse(SingleStepJson($$"""{ "gotoLeftPanelTab": "{{name}}" }"""));

        Assert.Equal(expected, Assert.IsType<GotoLeftPanelTabStep>(macro.Steps[0]).Target);
    }

    [Fact]
    public void Parse_GotoLeftPanelTabStep_UnknownTabName_ThrowsFormatException_NamingTheName()
    {
        var json = SingleStepJson("""{ "gotoLeftPanelTab": "Target" }""");

        var ex = Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
        Assert.Contains("Target", ex.Message);
    }

    [Fact]
    public void Parse_GotoLeftPanelTabStep_WrongCase_ThrowsFormatException()
    {
        // Case-sensitive by convention, matching Condition's GuiFocus:Name
        // form (StatusVocabulary.GuiFocusValues is an ordinal dictionary).
        var json = SingleStepJson("""{ "gotoLeftPanelTab": "contacts" }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_GotoLeftPanelTabStep_BlankValue_ThrowsFormatException()
    {
        var json = SingleStepJson("""{ "gotoLeftPanelTab": "" }""");

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    [Fact]
    public void Parse_StepIsNotAnObject_ThrowsFormatException()
    {
        var json = $$"""{ "id": "{{ValidId}}", "steps": [ "not-an-object" ] }""";

        Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));
    }

    // -------------------------------------------------------------------
    // TryParse - the non-throwing entry user-authored macro content goes
    // through (ref/docs/macro-builder.md). Same grammar, same messages,
    // reported instead of thrown; Parse's own throw is unchanged above and
    // stays correct for the shipped content it was written for.
    // -------------------------------------------------------------------

    [Fact]
    public void TryParse_ValidMacro_Succeeds_WithTheSameDefinitionParseWouldHaveReturned()
    {
        var json = SingleStepJson("""{ "press": "UI_Down", "repeat": 2 }""");

        var result = MacroDefinition.TryParse(json);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Equal(ValidId, result.Macro!.Id);
        Assert.Equal("UI_Down", Assert.IsType<PressStep>(Assert.Single(result.Macro.Steps)).Action);
    }

    [Fact]
    public void TryParse_NotValidJson_ReportsFailure_WithoutThrowing()
    {
        var result = MacroDefinition.TryParse("{ this is not json");

        Assert.False(result.Success);
        Assert.Null(result.Macro);
        Assert.NotNull(result.Error);
    }

    /// <summary>
    /// The message a commander is shown has to say what is wrong and
    /// <b>where</b> - the reason this wraps <c>Parse</c> rather than
    /// reimplementing it is that every thrown message already names the
    /// macro id and the offending step index, and a second error shape would
    /// have had to earn that again. Pinned by content, not by being
    /// non-empty: a result carrying "invalid macro" would pass a
    /// non-empty check and help nobody.
    /// </summary>
    [Fact]
    public void TryParse_BadStep_ReportsTheMacroIdAndTheOffendingStepIndex_NotJustThatSomethingFailed()
    {
        var json = $$"""
        {
          "id": "{{ValidId}}",
          "steps": [ { "wait": 10 }, { "press": "UI_Down" } ]
        }
        """;

        var result = MacroDefinition.TryParse(json);

        Assert.False(result.Success);
        Assert.Contains(ValidId, result.Error!);
        Assert.Contains("step 1", result.Error);
        Assert.Contains("repeat", result.Error);
    }

    [Fact]
    public void TryParse_UnknownConditionToken_ReportsTheOffendingToken()
    {
        var json = SingleStepJson("""{ "require": ["NotARealFlag"] }""");

        var result = MacroDefinition.TryParse(json);

        Assert.False(result.Success);
        Assert.Contains("NotARealFlag", result.Error!);
    }

    /// <summary>
    /// TryParse must agree with Parse on <b>every</b> failure the grammar
    /// has, not only the ones a hand-written list happened to name. Sweeps
    /// each malformed shape past both entry points: Parse throws, TryParse
    /// reports the identical message. A future grammar rule reachable only
    /// through some path TryParse did not cover would show up here as a
    /// difference.
    /// </summary>
    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("[ ]")]
    [InlineData("""{ "steps": [ { "wait": 1 } ] }""")]
    [InlineData("""{ "id": "  ", "steps": [ { "wait": 1 } ] }""")]
    [InlineData("""{ "id": "m" }""")]
    [InlineData("""{ "id": "m", "steps": [] }""")]
    [InlineData("""{ "id": "m", "steps": [ { } ] }""")]
    [InlineData("""{ "id": "m", "steps": [ { "wait": 1, "require": ["Docked"] } ] }""")]
    [InlineData("""{ "id": "m", "steps": [ { "press": "", "repeat": 1 } ] }""")]
    [InlineData("""{ "id": "m", "steps": [ { "press": "UI_Down", "repeat": 0 } ] }""")]
    [InlineData("""{ "id": "m", "steps": [ { "press": "UI_Down", "repeat": 1, "holdMs": 0 } ] }""")]
    [InlineData("""{ "id": "m", "steps": [ { "wait": -1 } ] }""")]
    [InlineData("""{ "id": "m", "steps": [ { "require": ["Nope"] } ] }""")]
    [InlineData("""{ "id": "m", "steps": [ { "waitFor": ["Docked"] } ] }""")]
    [InlineData("""{ "id": "m", "steps": [ { "pressUntil": "UI_Select", "cond": ["Docked"], "timeoutMs": 1 } ] }""")]
    [InlineData("""{ "id": "m", "steps": [ { "waitForEdge": [] } ] }""")]
    [InlineData("""{ "id": "m", "steps": [ { "waitForEdge": ["Journal:NotAnEvent"] } ] }""")]
    [InlineData("""{ "id": "m", "steps": [ { "gotoLeftPanelTab": "Nowhere" } ] }""")]
    [InlineData("""{ "id": "m", "steps": [ { "branch": [], "then": [ { "wait": 1 } ] } ] }""")]
    [InlineData("""{ "id": "m", "steps": [ { "branch": ["Docked"] } ] }""")]
    [InlineData("""{ "id": "m", "steps": [ { "branch": ["Nope"], "then": [ { "wait": 1 } ] } ] }""")]
    [InlineData("""{ "id": "m", "steps": [ { "branch": ["Docked"], "then": [ { "press": "UI_Down", "repeat": 0 } ] } ] }""")]
    [InlineData("""{ "id": "m", "steps": [ { "branch": ["Docked"], "then": [ { "branch": ["Landed"], "then": [ { "wait": 1 } ] } ] } ] }""")]
    public void TryParse_ReportsExactlyWhatParseThrows_ForEveryMalformedShape(string json)
    {
        var thrown = Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));

        var result = MacroDefinition.TryParse(json);

        Assert.False(result.Success);
        Assert.Null(result.Macro);
        Assert.Equal(thrown.Message, result.Error);
    }

    // ---------------------------------------------------------------
    // branch - the grammar's one conditional step (2026-09-17).
    // ---------------------------------------------------------------

    /// <summary>
    /// Both arms present, each parsing through the <b>same</b> step-parsing
    /// path a top-level step does - which is the whole point of the shared
    /// <c>ParseStepArray</c> helper rather than a second, nested grammar.
    /// Asserted by checking that a nested step's own validation still
    /// applies (a <c>press</c> inside an arm still carries its <c>repeat</c>)
    /// rather than merely that the arm has the right number of entries.
    /// </summary>
    [Fact]
    public void Parse_Branch_ParsesBothArms_ThroughTheSameStepGrammar()
    {
        var macro = MacroDefinition.Parse("""
        {
          "id": "branchy",
          "steps": [
            {
              "branch": ["Docked"],
              "then": [ { "press": "UI_Down", "repeat": 3 }, { "wait": 150 } ],
              "else": [ { "press": "FocusRadarPanel", "repeat": 1 } ]
            }
          ]
        }
        """);

        var branch = Assert.IsType<BranchStep>(Assert.Single(macro.Steps));
        Assert.Equal(new[] { "Docked" }, branch.ConditionTokens);
        Assert.True(branch.Conditions.Evaluate(new StatusSnapshot(1u, 0u, null, GameRunning: true, SignedIn: true)));

        Assert.Equal(2, branch.Then.Count);
        var thenPress = Assert.IsType<PressStep>(branch.Then[0]);
        Assert.Equal("UI_Down", thenPress.Action);
        Assert.Equal(3, thenPress.Repeat);
        Assert.Equal(TimeSpan.FromMilliseconds(150), Assert.IsType<WaitStep>(branch.Then[1]).Duration);

        var elsePress = Assert.IsType<PressStep>(Assert.Single(branch.Else));
        Assert.Equal("FocusRadarPanel", elsePress.Action);
    }

    /// <summary>
    /// An omitted arm is empty, never null - the same "collections are never
    /// null" rule every other step's lists follow, so nothing downstream has
    /// to null-check an arm before iterating it.
    /// </summary>
    [Fact]
    public void Parse_Branch_WithOnlyOneArm_LeavesTheOtherEmpty_NotNull()
    {
        var thenOnly = Assert.IsType<BranchStep>(Assert.Single(MacroDefinition.Parse(
            SingleStepJson("""{ "branch": ["Docked"], "then": [ { "wait": 1 } ] }""")).Steps));
        Assert.Single(thenOnly.Then);
        Assert.NotNull(thenOnly.Else);
        Assert.Empty(thenOnly.Else);

        var elseOnly = Assert.IsType<BranchStep>(Assert.Single(MacroDefinition.Parse(
            SingleStepJson("""{ "branch": ["Docked"], "else": [ { "wait": 1 } ] }""")).Steps));
        Assert.Single(elseOnly.Else);
        Assert.NotNull(elseOnly.Then);
        Assert.Empty(elseOnly.Then);
    }

    /// <summary>
    /// An empty <c>branch</c> array is a vacuous always-true test, and a
    /// branch with nothing in either arm does nothing whichever way it goes.
    /// Both are mistakes, not legitimate no-ops, so both fail at load rather
    /// than shipping a macro that silently means nothing.
    /// </summary>
    [Theory]
    [InlineData("""{ "branch": [], "then": [ { "wait": 1 } ] }""")]
    [InlineData("""{ "branch": ["Docked"] }""")]
    [InlineData("""{ "branch": ["Docked"], "then": [], "else": [] }""")]
    public void Parse_Branch_VacuousShapes_AreRejectedAtLoad(string stepJson)
    {
        var thrown = Assert.Throws<FormatException>(() => MacroDefinition.Parse(SingleStepJson(stepJson)));
        Assert.Contains(ValidId, thrown.Message);
        Assert.Contains("step 0", thrown.Message);
        Assert.Contains("branch", thrown.Message);
    }

    /// <summary>
    /// The one-level nesting cap (<see cref="BranchStep"/>'s own remarks).
    /// Rejected at load, naming the nested step's own label - not silently
    /// accepted and then mis-executed.
    /// </summary>
    [Fact]
    public void Parse_BranchInsideAnArm_IsRejected_NamingTheNestedStep()
    {
        var thrown = Assert.Throws<FormatException>(() => MacroDefinition.Parse(SingleStepJson("""
            {
              "branch": ["Docked"],
              "then": [
                { "wait": 1 },
                { "branch": ["Landed"], "then": [ { "wait": 2 } ] }
              ]
            }
            """)));

        Assert.Contains("step 0.then[1]", thrown.Message);
    }

    /// <summary>
    /// Error labels inside an arm read <c>step 0.then[1]</c>, not a bare
    /// integer that collides with a top-level step's index. Without this a
    /// commander reading "step 1" would go looking at the wrong step
    /// entirely - and with one branch per macro, "step 1" would often be a
    /// step that parsed perfectly.
    /// </summary>
    [Theory]
    [InlineData("then")]
    [InlineData("else")]
    public void Parse_AMalformedStepInsideAnArm_IsLabelledByItsPathNotABareIndex(string arm)
    {
        var json = SingleStepJson($$"""
            {
              "branch": ["Docked"],
              "{{arm}}": [ { "wait": 1 }, { "press": "UI_Down", "repeat": 0 } ]
            }
            """);

        var thrown = Assert.Throws<FormatException>(() => MacroDefinition.Parse(json));

        Assert.Contains($"step 0.{arm}[1]", thrown.Message);
        Assert.Contains("repeat", thrown.Message);
    }

    /// <summary>
    /// A top-level step's label is still the bare index it always was - the
    /// widening from <c>int</c> to a path label must not change a single
    /// message a commander already reads for an ordinary macro.
    /// </summary>
    [Fact]
    public void Parse_ATopLevelStepsErrorLabel_IsStillABareIndex_Unchanged()
    {
        var thrown = Assert.Throws<FormatException>(() => MacroDefinition.Parse("""
            { "id": "test-macro", "steps": [ { "wait": 1 }, { "press": "UI_Down", "repeat": 0 } ] }
            """));

        Assert.Contains("step 1:", thrown.Message);
        Assert.DoesNotContain("step 1.", thrown.Message);
    }

    /// <summary>
    /// <c>then</c>/<c>else</c> are branch fields, not step-kind
    /// discriminators - a branch step carrying both is one kind, not three.
    /// </summary>
    [Fact]
    public void Parse_Branch_IsOneStepKind_NotThree()
    {
        Assert.Contains("branch", MacroDefinition.StepKindKeys);
        Assert.DoesNotContain("then", MacroDefinition.StepKindKeys);
        Assert.DoesNotContain("else", MacroDefinition.StepKindKeys);
    }

    /// <summary>
    /// A non-array arm is a shape error, reported as one rather than
    /// silently treated as absent.
    /// </summary>
    [Fact]
    public void Parse_Branch_ArmThatIsNotAnArray_IsRejected()
    {
        var thrown = Assert.Throws<FormatException>(() => MacroDefinition.Parse(
            SingleStepJson("""{ "branch": ["Docked"], "then": 3 }""")));

        Assert.Contains("'then'", thrown.Message);
    }

    private static string SingleStepJson(string stepJson) => $$"""
    {
      "id": "{{ValidId}}",
      "steps": [ {{stepJson}} ]
    }
    """;
}
