using LunaPanel.Core.Bindings;
using LunaPanel.Core.GameState;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Macros;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// Drives <see cref="MacroKnowledgeBuilder.Build"/> directly - the
/// production counterpart to the hand-built <see cref="MacroKnowledge"/>
/// every <see cref="LayoutAnnotatorTests"/> test uses, now that a macro
/// loader exists (<c>LunaPanel.Server.Macros.MacroLoader</c>).
/// </summary>
public class MacroKnowledgeBuilderTests
{
    private static BindingsFile BuildBindings(params (string Action, string Key)[] bindings)
    {
        var elements = string.Concat(bindings.Select(b =>
            $"<{b.Action}><Primary Device=\"Keyboard\" Key=\"{b.Key}\" /></{b.Action}>"));
        var xml = $"<Root MajorVersion=\"4\" MinorVersion=\"2\">{elements}</Root>";
        var result = BindingsFile.Parse(xml);
        Assert.True(result.Success, result.Error);
        return result.File!;
    }

    private static MacroDefinition Macro(string id, params MacroStep[] steps) => new(id, null, steps);

    [Fact]
    public void Build_EveryMacro_IsInKnownIds()
    {
        var bindings = BuildBindings(("Action1", "Key_1"), ("Action2", "Key_2"));
        var macros = new[]
        {
            Macro("macro-a", new PressStep("Action1", 1, null)),
            Macro("macro-b", new PressStep("Action2", 1, null)),
        };

        var knowledge = MacroKnowledgeBuilder.Build(macros, bindings);

        Assert.Contains("macro-a", knowledge.KnownIds);
        Assert.Contains("macro-b", knowledge.KnownIds);
    }

    [Fact]
    public void Build_EveryPressActionBound_NotDegraded()
    {
        var bindings = BuildBindings(("Action1", "Key_1"));
        var macros = new[] { Macro("macro-a", new PressStep("Action1", 1, null)) };

        var knowledge = MacroKnowledgeBuilder.Build(macros, bindings);

        Assert.DoesNotContain("macro-a", knowledge.DegradedIds);
    }

    [Fact]
    public void Build_APressActionUnbound_IsDegraded()
    {
        var bindings = BuildBindings(); // nothing bound
        var macros = new[] { Macro("macro-a", new PressStep("NotBound", 1, null)) };

        var knowledge = MacroKnowledgeBuilder.Build(macros, bindings);

        Assert.Contains("macro-a", knowledge.KnownIds);
        Assert.Contains("macro-a", knowledge.DegradedIds);
    }

    [Fact]
    public void Build_APressUntilActionUnbound_IsDegraded()
    {
        var bindings = BuildBindings();
        var macros = new[]
        {
            Macro("macro-a", new PressUntilStep(
                "NotBound", ConditionList.Parse(new[] { "Docked" }), new[] { "Docked" }, TimeSpan.FromSeconds(1), MaxAttempts: 3)),
        };

        var knowledge = MacroKnowledgeBuilder.Build(macros, bindings);

        Assert.Contains("macro-a", knowledge.DegradedIds);
    }

    [Fact]
    public void Build_OnlyWaitAndRequireSteps_NeverDegrades_TheyNameNoAction()
    {
        var bindings = BuildBindings(); // nothing bound at all
        var macros = new[]
        {
            Macro(
                "macro-a",
                new RequireStep(ConditionList.Parse(new[] { "Docked" }), new[] { "Docked" }),
                new WaitStep(TimeSpan.FromMilliseconds(100)),
                new WaitForStep(ConditionList.Parse(new[] { "Docked" }), new[] { "Docked" }, TimeSpan.FromSeconds(1))),
        };

        var knowledge = MacroKnowledgeBuilder.Build(macros, bindings);

        Assert.Contains("macro-a", knowledge.KnownIds);
        Assert.DoesNotContain("macro-a", knowledge.DegradedIds);
    }

    [Fact]
    public void Build_OneOfSeveralStepsUnbound_WholeMacroIsDegraded()
    {
        var bindings = BuildBindings(("Bound", "Key_1"));
        var macros = new[]
        {
            Macro("macro-a", new PressStep("Bound", 1, null), new PressStep("NotBound", 1, null)),
        };

        var knowledge = MacroKnowledgeBuilder.Build(macros, bindings);

        Assert.Contains("macro-a", knowledge.DegradedIds);
    }

    [Fact]
    public void Build_GotoLeftPanelTabStep_AdvanceActionBound_NotDegraded()
    {
        var bindings = BuildBindings((PanelTabTracker.AdvanceTabAction, "Key_E"));
        var macros = new[] { Macro("macro-a", new GotoLeftPanelTabStep(PanelTab.Contacts)) };

        var knowledge = MacroKnowledgeBuilder.Build(macros, bindings);

        Assert.DoesNotContain("macro-a", knowledge.DegradedIds);
    }

    [Fact]
    public void Build_GotoLeftPanelTabStep_AdvanceActionUnbound_IsDegraded()
    {
        // Regression: MacroKnowledgeBuilder originally only inspected
        // PressStep/PressUntilStep, so a macro whose only action-bearing
        // step was gotoLeftPanelTab would report Ok while actually aborting
        // at run time the moment a press was needed - the exact invariant
        // this class exists to hold ("a degraded macro and a macro that
        // would actually fail to run always agree").
        var bindings = BuildBindings(); // CycleNextPanel not bound
        var macros = new[] { Macro("macro-a", new GotoLeftPanelTabStep(PanelTab.Contacts)) };

        var knowledge = MacroKnowledgeBuilder.Build(macros, bindings);

        Assert.Contains("macro-a", knowledge.KnownIds);
        Assert.Contains("macro-a", knowledge.DegradedIds);
    }

    /// <summary>
    /// A raw key press names no Frontier action at all - there is nothing
    /// for <c>BindResolver</c> to fail to resolve, so this step kind can
    /// never contribute to a macro being degraded, regardless of whether the
    /// key it names happens to match anything in <paramref name="bindings"/>
    /// (irrelevant here anyway - <c>PressKeyStep</c> is resolved through
    /// <c>Scancodes.TryGet</c>, never <c>BindingsFile</c>).
    /// </summary>
    [Fact]
    public void Build_OnlyPressKeyStep_NeverDegrades_ItNamesNoAction()
    {
        var bindings = BuildBindings(); // nothing bound at all
        var macros = new[] { Macro("macro-a", new PressKeyStep("Key_F5", 1, null)) };

        var knowledge = MacroKnowledgeBuilder.Build(macros, bindings);

        Assert.Contains("macro-a", knowledge.KnownIds);
        Assert.DoesNotContain("macro-a", knowledge.DegradedIds);
    }

    [Fact]
    public void Build_OnlyWaitForEdgeStep_NeverDegrades_ItNamesNoAction()
    {
        var bindings = BuildBindings(); // nothing bound
        var macros = new[]
        {
            Macro(
                "macro-a",
                new WaitForEdgeStep(
                    new[] { EdgeCondition.Parse("Journal:FSDJump") }, new[] { "Journal:FSDJump" },
                    Array.Empty<EdgeCondition>(), Array.Empty<string>(), null, TimeSpan.FromSeconds(30))),
        };

        var knowledge = MacroKnowledgeBuilder.Build(macros, bindings);

        Assert.Contains("macro-a", knowledge.KnownIds);
        Assert.DoesNotContain("macro-a", knowledge.DegradedIds);
    }

    [Fact]
    public void Build_NoMacros_ReturnsEmptyKnowledge()
    {
        var bindings = BuildBindings();

        var knowledge = MacroKnowledgeBuilder.Build(Array.Empty<MacroDefinition>(), bindings);

        Assert.Empty(knowledge.KnownIds);
        Assert.Empty(knowledge.DegradedIds);
    }

    // ---------------------------------------------------------------
    // Names - MacroKnowledge.NameFor (2026-09-07: a macro-referencing
    // slot's label used to skip the macro's own `name` entirely - see
    // ref/docs/button-naming.md's "How a macro supplies a default label").
    // ---------------------------------------------------------------

    [Fact]
    public void Build_MacroWithName_NameForReturnsIt()
    {
        var bindings = BuildBindings(("Action1", "Key_1"));
        var macros = new[] { new MacroDefinition("macro-a", "Macro A", new MacroStep[] { new PressStep("Action1", 1, null) }) };

        var knowledge = MacroKnowledgeBuilder.Build(macros, bindings);

        Assert.Equal("Macro A", knowledge.NameFor("macro-a"));
    }

    [Fact]
    public void Build_MacroWithoutName_NameForReturnsNull()
    {
        var bindings = BuildBindings(("Action1", "Key_1"));
        var macros = new[] { Macro("macro-a", new PressStep("Action1", 1, null)) }; // Macro() helper passes name: null

        var knowledge = MacroKnowledgeBuilder.Build(macros, bindings);

        Assert.Null(knowledge.NameFor("macro-a"));
    }

    [Fact]
    public void Build_UnknownMacroId_NameForReturnsNull()
    {
        var knowledge = MacroKnowledgeBuilder.Build(Array.Empty<MacroDefinition>(), BuildBindings());

        Assert.Null(knowledge.NameFor("does-not-exist"));
    }
}
