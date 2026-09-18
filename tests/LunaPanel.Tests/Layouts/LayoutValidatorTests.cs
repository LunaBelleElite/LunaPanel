using LunaPanel.Core.Layouts;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// Pins every <see cref="LayoutValidator"/> rule, each failing with a
/// message naming the offending page and slot, plus the one asymmetry that
/// matters: an unknown action name is rejected on save but tolerated on
/// load (see <see cref="ValidateForSave_UnknownActionName_Fails_ButValidateForLoad_TheSameLayout_Tolerates"/>).
/// </summary>
public class LayoutValidatorTests
{
    private static readonly HashSet<string> KnownActions = new(StringComparer.Ordinal)
    {
        "LandingGearToggle", "ToggleCargoScoop", "ShipSpotLightToggle",
    };

    private static LayoutSlot Slot(int index, string? action = null, string? macro = null, string? label = null, LongPressAction? longPress = null) =>
        new(index, action, macro, label, longPress);

    private static LayoutPage ValidPage(string name = "SHIP", string templateId = "t6", IReadOnlyList<LayoutSlot>? slots = null, IReadOnlyList<LayoutSlot>? parked = null) =>
        new(name, templateId, slots ?? new[] { Slot(0, action: "LandingGearToggle") }, parked ?? Array.Empty<LayoutSlot>());

    [Fact]
    public void ValidateForLoad_WellFormedLayout_IsValid()
    {
        var layout = new Layout(1, new[] { ValidPage() });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateForLoad_UnknownTemplateId_Fails_NamingPageAndTemplate()
    {
        var layout = new Layout(1, new[] { ValidPage(templateId: "t99") });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SHIP") && e.Contains("t99"));
    }

    [Fact]
    public void ValidateForLoad_DuplicateIndexWithinSlots_Fails_NamingPageAndSlot()
    {
        var page = ValidPage(slots: new[]
        {
            Slot(0, action: "LandingGearToggle"),
            Slot(0, action: "ToggleCargoScoop"),
        });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SHIP") && e.Contains("slot 0") && e.Contains("duplicate"));
    }

    [Fact]
    public void ValidateForLoad_DuplicateIndexAcrossSlotsAndParked_Fails()
    {
        var page = ValidPage(
            slots: new[] { Slot(0, action: "LandingGearToggle") },
            parked: new[] { Slot(0, action: "ToggleCargoScoop") });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("duplicate"));
    }

    [Fact]
    public void ValidateForLoad_ActiveSlotIndexOutsideTemplateRange_Fails()
    {
        // t6 has slots [0, 6) - index 6 is out of range.
        var page = ValidPage(slots: new[] { Slot(6, action: "LandingGearToggle") });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SHIP") && e.Contains("slot 6") && e.Contains("range"));
    }

    [Fact]
    public void ValidateForLoad_ParkedSlotIndexOutsideTemplateRange_DoesNotFail()
    {
        // Parked slots are exempt from range checking by design - that's
        // the whole point of parking (see ref/docs/design-decisions.md).
        var page = ValidPage(parked: new[] { Slot(14, action: "ToggleCargoScoop") });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void ValidateForLoad_SlotWithBothActionAndMacro_Fails()
    {
        var page = ValidPage(slots: new[] { Slot(0, action: "LandingGearToggle", macro: "some-macro") });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SHIP") && e.Contains("slot 0") && e.Contains("exactly one") && e.Contains("both"));
    }

    [Fact]
    public void ValidateForLoad_SlotWithNeitherActionNorMacro_Fails()
    {
        var page = ValidPage(slots: new[] { Slot(0) });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SHIP") && e.Contains("slot 0") && e.Contains("exactly one") && e.Contains("neither"));
    }

    [Fact]
    public void ValidateForLoad_LongPressWithBothActionAndMacro_Fails_NamingLongPress()
    {
        var page = ValidPage(slots: new[]
        {
            Slot(0, action: "LandingGearToggle", longPress: new LongPressAction("ToggleCargoScoop", "some-macro")),
        });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SHIP") && e.Contains("slot 0 long-press") && e.Contains("both"));
    }

    /// <summary>
    /// [2026-09-09] SUPERSEDED by the coordinator's ruling on wrapping, and
    /// inverted rather than deleted so the reversal stays visible.
    /// Before-value: this same fixture - one word of
    /// <c>UserLabelLineCap + 1</c> identical characters - was asserted to
    /// FAIL, and the test was named <c>ValidateForLoad_LabelExceedingCap_Fails</c>.
    /// A single word has nowhere to wrap, and refusing a name the commander
    /// deliberately typed is worse than letting it look cramped, so it is
    /// now accepted. The cap still bites on anything that CAN wrap - see
    /// <see cref="ValidateForLoad_ANameThatWrapsToThreeLines_Fails"/> and
    /// <see cref="WrapLabel_OneCharacterOverTheLineCap_WrapsAtTheSpace_WithNoTypedBreak"/>.
    /// </summary>
    [Fact]
    public void ValidateForLoad_OneWordExceedingTheLineCap_IsValid_SupersedesLabelExceedingCapFails()
    {
        var label = new string('X', LayoutValidator.UserLabelLineCap + 1);
        var page = ValidPage(slots: new[] { Slot(0, action: "LandingGearToggle", label: label) });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void ValidateForLoad_LabelExactlyAtCap_IsValid()
    {
        var label = new string('X', LayoutValidator.UserLabelLineCap);
        var page = ValidPage(slots: new[] { Slot(0, action: "LandingGearToggle", label: label) });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    // -----------------------------------------------------------------
    // The two-line, per-line-budget label rule (ref/docs/button-naming.md's
    // "The flat cap has to go"). SUPERSEDES the flat whole-string
    // `UserLabelCap = 12` cap the two tests above used to guard (before-value:
    // `slot.Label.Length > 12`, counted across any embedded newline) - it
    // failed in both directions, which is exactly what the two pins directly
    // below hold in place, taken verbatim from that doc's own table.
    // -----------------------------------------------------------------

    /// <summary>
    /// Doc table row 1 (`button-naming.md`): a real button the commander
    /// uses, confirmed on hardware. 15 characters total, across two real
    /// lines of 7 and 7 - the flat cap rejected it outright; the new rule
    /// must accept it.
    /// </summary>
    [Fact]
    public void ValidateForLoad_RequestDocking_TwoLinesWithinBudget_IsValid_TheFlatCapUsedToRejectThis()
    {
        var page = ValidPage(slots: new[] { Slot(0, action: "LandingGearToggle", label: "Request\nDocking") });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    /// <summary>
    /// Doc table row 2 (`button-naming.md`): a single squeezed 12-character
    /// line - "fits, and is useless." Both the old flat cap and the new
    /// per-line rule accept it (this rule judges whether a label fits the
    /// button, not whether it reads as real words) - pinned as the control
    /// showing the new rule does not become stricter for the case that was
    /// already correct.
    /// </summary>
    [Fact]
    public void ValidateForLoad_ReqestdockngSqueezedSingleLine_IsStillValid_TheNewRuleDoesNotRegressThisCase()
    {
        var page = ValidPage(slots: new[] { Slot(0, action: "LandingGearToggle", label: "Reqestdockng") });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void ValidateForLoad_LabelWithThreeLines_Fails_ExceedsUserLabelMaxLines()
    {
        var page = ValidPage(slots: new[] { Slot(0, action: "LandingGearToggle", label: "One\nTwo\nThree") });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SHIP") && e.Contains("slot 0") && e.Contains("does not fit"));
    }

    [Fact]
    public void ValidateForLoad_LabelWithAnEmptyLine_Fails()
    {
        var page = ValidPage(slots: new[] { Slot(0, action: "LandingGearToggle", label: "Request\n") });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SHIP") && e.Contains("slot 0") && e.Contains("does not fit"));
    }

    /// <summary>
    /// [2026-09-09] SUPERSEDED and inverted, and this one is the defect
    /// itself rather than a consequence of it. Before-value: this exact
    /// fixture, <c>"Request\nDocking "</c>, was asserted to FAIL, under a
    /// rule that refused any line differing from its own <c>Trim()</c>.
    /// That clause is what refused the commander a ten-character name in
    /// live testing on 2026-09-09: an on-screen keyboard appends a space
    /// when a word suggestion is tapped, and the character it refused over
    /// is invisible. Surrounding whitespace is now absorbed by
    /// <c>LayoutValidator.WrapLabel</c> instead.
    /// </summary>
    [Fact]
    public void ValidateForLoad_ATrailingSpaceOnALine_IsValid_SupersedesLabelWithLeadingOrTrailingWhitespaceFails()
    {
        var page = ValidPage(slots: new[] { Slot(0, action: "LandingGearToggle", label: "Request\nDocking ") });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal(new[] { "Request", "Docking" }, LayoutValidator.WrapLabel("Request\nDocking "));
    }

    /// <summary>
    /// [2026-09-09] SUPERSEDED fixture (before-value: line 2 was
    /// <c>"DockingBayXXX"</c>, one unbroken word of 13 characters, then one
    /// over the cap of 12). A single word has nowhere to wrap and is now
    /// deliberately let through, so that fixture proves nothing; the second
    /// line is now one that CAN wrap, and wrapping it is what pushes the
    /// label past two lines. The claim is unchanged: the per-line budget is
    /// applied to each hard-broken line on its own, not to the label's
    /// total length.
    /// </summary>
    [Fact]
    public void ValidateForLoad_SecondLineExceedsThePerLineBudget_Fails()
    {
        var word = new string('B', LayoutValidator.UserLabelLineCap - 2);
        var page = ValidPage(slots: new[] { Slot(0, action: "LandingGearToggle", label: $"Request\n{word} {word}") });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SHIP") && e.Contains("slot 0") && e.Contains("does not fit"));
    }

    [Fact]
    public void ValidateForSave_UnknownActionName_Fails_ButValidateForLoad_TheSameLayout_Tolerates()
    {
        var page = ValidPage(slots: new[] { Slot(0, action: "SomeRenamedOrRemovedAction") });
        var layout = new Layout(1, new[] { page });

        var saveResult = LayoutValidator.ValidateForSave(layout, KnownActions);
        var loadResult = LayoutValidator.ValidateForLoad(layout);

        Assert.False(saveResult.IsValid);
        Assert.Contains(saveResult.Errors, e => e.Contains("unknown action") && e.Contains("SomeRenamedOrRemovedAction"));

        Assert.True(loadResult.IsValid, string.Join("; ", loadResult.Errors));
    }

    [Fact]
    public void ValidateForSave_KnownActionName_IsValid()
    {
        var page = ValidPage(slots: new[] { Slot(0, action: "LandingGearToggle") });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForSave(layout, KnownActions);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void ValidateForSave_UnknownLongPressActionName_Fails()
    {
        var page = ValidPage(slots: new[]
        {
            Slot(0, action: "LandingGearToggle", longPress: new LongPressAction("MadeUpAction", null)),
        });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForSave(layout, KnownActions);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("long-press") && e.Contains("MadeUpAction"));
    }

    // -----------------------------------------------------------------
    // Wrapping, and the fifteen-character line (2026-09-09,
    // ref/docs/button-naming.md's "Fifteen characters, and the break the
    // commander cannot type"). Three defects found in live testing, all
    // reached through this one method:
    //   1. the per-line cap was 12 and the commander asked for 15;
    //   2. a tablet's on-screen keyboard offers no Enter, so the second
    //      line was unreachable on the device this product is for - the
    //      rule now wraps at spaces instead of demanding a typed break;
    //   3. a name of about ten characters was refused, which was the
    //      leading/trailing-whitespace clause firing on the space an
    //      on-screen keyboard appends when a suggestion is tapped.
    // Every fixture below is built from the constants, never from a
    // re-typed 15 or 2 - the one exception is TheLineCapIsFifteen, whose
    // whole job is to hold the commander's own number in place.
    // -----------------------------------------------------------------

    private static int Cap => LayoutValidator.UserLabelLineCap;

    /// <summary>
    /// The commander's own ruling, 2026-09-09: "I am good with 15
    /// characters" (before-value: 12). Asserted as a literal on purpose -
    /// comparing the constant to itself would pin nothing. Two lines
    /// maximum is unchanged.
    /// </summary>
    [Fact]
    public void TheLineCapIsFifteenCharacters_AcrossAtMostTwoLines()
    {
        Assert.Equal(15, LayoutValidator.UserLabelLineCap);
        Assert.Equal(2, LayoutValidator.UserLabelMaxLines);
    }

    [Fact]
    public void WrapLabel_ALineOfExactlyTheLineCap_StaysOnOneLine()
    {
        var label = new string('A', Cap - 4) + " BBB";
        Assert.Equal(Cap, label.Length);

        var lines = LayoutValidator.WrapLabel(label);

        Assert.Equal(new[] { label }, lines);
    }

    /// <summary>
    /// One character over the cap wraps at the space rather than being
    /// refused - the commander types one line and gets two, because there
    /// is no Enter key on a tablet's on-screen keyboard to type the break
    /// with.
    /// </summary>
    [Fact]
    public void WrapLabel_OneCharacterOverTheLineCap_WrapsAtTheSpace_WithNoTypedBreak()
    {
        var first = new string('A', Cap - 4);
        var label = first + " BBBB";
        Assert.Equal(Cap + 1, label.Length);

        var lines = LayoutValidator.WrapLabel(label);

        Assert.Equal(new[] { first, "BBBB" }, lines);
    }

    [Fact]
    public void WrapLabel_ATypedLineBreak_IsStillAHardBreak_NeverRejoined()
    {
        var lines = LayoutValidator.WrapLabel("Request\nDocking");

        Assert.Equal(new[] { "Request", "Docking" }, lines);
    }

    [Fact]
    public void ValidateForLoad_ANameLongerThanOneLine_IsValid_ItWrapsAtTheSpaceInstead()
    {
        var label = new string('A', Cap - 4) + " BBBB";
        var page = ValidPage(slots: new[] { Slot(0, action: "LandingGearToggle", label: label) });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void ValidateForLoad_ANameThatWrapsToThreeLines_Fails()
    {
        // Three words, each two characters short of the cap, so no two of
        // them fit on one line together.
        var word = new string('A', Cap - 2);
        var label = $"{word} {word} {word}";
        var page = ValidPage(slots: new[] { Slot(0, action: "LandingGearToggle", label: label) });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.Equal(3, LayoutValidator.WrapLabel(label).Count);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SHIP") && e.Contains("slot 0") && e.Contains("does not fit"));
    }

    /// <summary>
    /// Coordinator's ruling, 2026-09-09: a single word longer than the cap
    /// cannot wrap, so it is let through and allowed to look cramped rather
    /// than refused - a name deliberately typed is never rejected, and never
    /// silently truncated either.
    /// </summary>
    [Fact]
    public void ValidateForLoad_ASingleWordLongerThanTheLineCap_IsValid_ThereIsNowhereToWrapIt()
    {
        var label = new string('A', Cap + 1);
        var page = ValidPage(slots: new[] { Slot(0, action: "LandingGearToggle", label: label) });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    /// <summary>
    /// THE TEN-CHARACTER REFUSAL, 2026-09-09. The commander was refused a
    /// name "even at 10" characters. This is it: an on-screen keyboard
    /// appends a space when a word suggestion is tapped, and the rule's
    /// no-leading-or-trailing-whitespace clause refused the result -
    /// invisibly, since the offending character is a space. Ten characters,
    /// nine of them visible.
    /// </summary>
    [Fact]
    public void ValidateForLoad_ATrailingSpaceFromAnOnScreenKeyboard_IsValid_TheTenCharacterRefusal()
    {
        const string label = "Set Speed ";
        Assert.Equal(10, label.Length);

        var page = ValidPage(slots: new[] { Slot(0, action: "LandingGearToggle", label: label) });
        var layout = new Layout(1, new[] { page });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal(new[] { "Set Speed" }, LayoutValidator.WrapLabel(label));
    }
}
