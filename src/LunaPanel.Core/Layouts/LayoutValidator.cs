namespace LunaPanel.Core.Layouts;

/// <summary>
/// Pure semantic validation over an already-parsed <see cref="Layout"/> - no
/// filesystem, no catalogue, no bindings. Checks, for every page: the
/// template id resolves via <see cref="Templates.TryGet"/>; slot indices are
/// unique within the page (across <see cref="LayoutPage.Slots"/> and
/// <see cref="LayoutPage.Parked"/> together - a slot can't be both active
/// and parked at the same index) and, for active slots only, within the
/// resolved template's slot count; exactly one of action/macro is set per
/// slot and per long-press; a user-overridden label fits
/// <see cref="UserLabelMaxLines"/> lines of at most
/// <see cref="UserLabelLineCap"/> characters each once wrapped at the spaces
/// (<see cref="WrapLabel"/>), with no empty line (<c>ref/docs/button-naming.md</c>'s
/// "The flat cap has to go" - this replaced a flat whole-string character cap,
/// which rejected a label like <c>"Request\nDocking"</c> that fits the button
/// fine while accepting a single squeezed line that doesn't read as words at
/// all).
///
/// <see cref="ValidateForSave"/> additionally rejects an action name that
/// isn't present in the caller-supplied <c>knownActionNames</c> - the
/// picker can't produce one, so this arriving at save time is a bug or a
/// hand edit. <see cref="ValidateForLoad"/> deliberately skips that one
/// check: a file on disk may legitimately predate a game update that
/// renamed an action, and refusing to load it would destroy an otherwise-
/// working panel over one degraded slot. Whether a *currently* recognized
/// action name is actually bound is a separate, read-time concern handled
/// by <see cref="LayoutAnnotator"/>, not by this type.
/// </summary>
public static class LayoutValidator
{
    /// <summary>
    /// A user's own label override is at most this many lines
    /// (<c>ref/docs/button-naming.md</c>: "up to two lines the user
    /// controls").
    ///
    /// [2026-09-09] Unchanged in value, changed in meaning: the line break
    /// no longer has to be one the commander typed with Enter. A tablet's
    /// on-screen keyboard has no Enter key, so a second line was
    /// unreachable on the device this product is built for -
    /// <see cref="WrapLabel"/> now finds the break at a space, and a typed
    /// break is still honoured where there is one.
    /// </summary>
    public const int UserLabelMaxLines = 2;

    /// <summary>
    /// Each line of a user's own label override is at most this many
    /// characters. Curated labels
    /// (<c>LunaPanel.Core.Catalogue.CatalogueAction.Label</c>) are held to
    /// their own, tighter ceiling, so a curated line is never wider than an
    /// override is allowed to be.
    /// This, together with <see cref="UserLabelMaxLines"/>, REPLACES a flat
    /// 12-character cap on the whole string (before-value: <c>UserLabelCap
    /// = 12</c>, one line) that judged whether a label fits the button by
    /// counting characters across an embedded newline - it rejected
    /// <c>"Request\nDocking"</c> (15 characters, two real lines, confirmed on
    /// hardware) while accepting <c>"Reqestdockng"</c> (12 characters, one
    /// line, squeezed and unreadable) - see
    /// <c>ref/docs/button-naming.md</c>'s "The flat cap has to go".
    ///
    /// [2026-09-09] Raised from 12 to 15 (before-value: <c>12</c>) on the
    /// commander's own ruling after live testing - "I am good with 15
    /// characters" - which is why 15 is asserted as a literal in
    /// <c>LayoutValidatorTests.TheLineCapIsFifteenCharacters_AcrossAtMostTwoLines</c>
    /// rather than only being read back out of this field.
    /// </summary>
    public const int UserLabelLineCap = 15;

    public static LayoutValidationResult ValidateForSave(Layout layout, IReadOnlySet<string> knownActionNames)
    {
        ArgumentNullException.ThrowIfNull(knownActionNames);
        return Validate(layout, knownActionNames);
    }

    public static LayoutValidationResult ValidateForLoad(Layout layout) => Validate(layout, knownActionNames: null);

    private static LayoutValidationResult Validate(Layout layout, IReadOnlySet<string>? knownActionNames)
    {
        var errors = new List<string>();

        foreach (var page in layout.Pages)
        {
            PanelTemplate? template = null;
            if (!Templates.TryGet(page.TemplateId, out template) || template is null)
            {
                errors.Add($"Page '{page.Name}': unknown template id '{page.TemplateId}'.");
            }

            var seenIndices = new HashSet<int>();

            ValidateSlotGroup(page, page.Slots, template, enforceRange: true, knownActionNames, seenIndices, errors);
            ValidateSlotGroup(page, page.Parked, template, enforceRange: false, knownActionNames, seenIndices, errors);
        }

        ValidateFolders(layout, errors);

        return errors.Count == 0 ? LayoutValidationResult.Ok() : LayoutValidationResult.Fail(errors);
    }

    /// <summary>
    /// [2026-09-16] The three folder rules that CANNOT be decided from one
    /// page (<c>ref/docs/layouts.md</c>) - which is exactly why they live
    /// here and not in <see cref="ValidateSlotGroup"/>, where every other
    /// slot rule sits:
    /// <list type="bullet">
    /// <item><b>One level, exactly.</b> A slot on a page that is itself a
    /// folder's interior may never open a further folder. Refused outright
    /// rather than flattened, per the commander's ruling - a second level
    /// would need a navigation stack, a breadcrumb, and an answer to "what
    /// does Back mean three deep" that nobody asked for.</item>
    /// <item><b>No dangling reference.</b> Every
    /// <see cref="LayoutSlot.FolderPage"/> must resolve to a page that
    /// actually exists AND is marked <see cref="LayoutPage.IsFolderOwned"/>.
    /// Pointing at an ordinary page is its own bug, not a near miss: that
    /// page would appear in the tab bar and behind a button at once, with an
    /// "up one level" control competing against its own tab.</item>
    /// <item><b>No shared target.</b> Two slots may never open the same
    /// interior. The reverse lookup that recovers "which button owns this
    /// folder" (<c>PanelEndpoint</c>'s ParentPageIndex) would have two
    /// answers, and deleting the folder would have to clear two buttons the
    /// commander never connected to each other.</item>
    /// </list>
    /// An interior page that nothing points at is deliberately NOT an error:
    /// clearing or reassigning a folder button leaves an intact, unreferenced
    /// orphan rather than destroying a page full of buttons (the same
    /// philosophy as <see cref="LayoutPage.Parked"/>), so that shape has to
    /// stay savable.
    /// </summary>
    private static void ValidateFolders(Layout layout, List<string> errors)
    {
        var pagesById = new Dictionary<string, LayoutPage>(StringComparer.Ordinal);
        foreach (var page in layout.Pages)
        {
            if (page.Id is not null)
            {
                pagesById[page.Id] = page;
            }
        }

        var claimedBy = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var page in layout.Pages)
        {
            foreach (var slot in page.Slots.Concat(page.Parked))
            {
                if (slot.FolderPage is null)
                {
                    continue;
                }

                if (page.IsFolderOwned)
                {
                    errors.Add($"Page '{page.Name}', slot {slot.Index}: a folder cannot contain another folder - folders nest one level only.");
                }

                if (!pagesById.TryGetValue(slot.FolderPage, out var target))
                {
                    errors.Add($"Page '{page.Name}', slot {slot.Index}: opens a folder page '{slot.FolderPage}' that is missing from this layout.");
                }
                else if (!target.IsFolderOwned)
                {
                    errors.Add($"Page '{page.Name}', slot {slot.Index}: opens page '{target.Name}', which is an ordinary page rather than a folder's own page.");
                }

                if (claimedBy.TryGetValue(slot.FolderPage, out var firstClaim))
                {
                    errors.Add($"Page '{page.Name}', slot {slot.Index}: opens the same folder page as {firstClaim} - a folder page is opened by more than one button.");
                }
                else
                {
                    claimedBy[slot.FolderPage] = $"page '{page.Name}', slot {slot.Index}";
                }
            }
        }
    }

    private static void ValidateSlotGroup(
        LayoutPage page,
        IReadOnlyList<LayoutSlot> slots,
        PanelTemplate? template,
        bool enforceRange,
        IReadOnlySet<string>? knownActionNames,
        HashSet<int> seenIndices,
        List<string> errors)
    {
        foreach (var slot in slots)
        {
            if (!seenIndices.Add(slot.Index))
            {
                errors.Add($"Page '{page.Name}', slot {slot.Index}: duplicate index within the page.");
            }

            if (enforceRange && template is not null && (slot.Index < 0 || slot.Index >= template.Slots))
            {
                errors.Add($"Page '{page.Name}', slot {slot.Index}: index is outside template '{template.Id}''s range of [0, {template.Slots}).");
            }

            ValidateExactlyOne(page, $"slot {slot.Index}", slot.Action, slot.Macro, slot.FolderPage, errors);
            ValidateActionKnown(page, $"slot {slot.Index}", slot.Action, knownActionNames, errors);

            if (slot.Label is not null && !LabelFitsBudget(slot.Label))
            {
                errors.Add($"Page '{page.Name}', slot {slot.Index}: label '{slot.Label}' does not fit within {UserLabelMaxLines} line(s) of {UserLabelLineCap} character(s) each, even wrapped at the spaces (no empty lines).");
            }

            // A latch holds ONE key down until tapped again
            // (ref/docs/latching-keys.md). A macro is a sequence of
            // keystrokes with its own choreography, so there is no single
            // key for a latch to hold - the combination is meaningless
            // rather than merely unsupported, which is why it is rejected
            // here rather than silently ignored at press time.
            // [2026-09-16] A folder reaches this same guard through the same
            // null Action, and is named separately in the message: telling a
            // commander that their FOLDER cannot be latched "because it is a
            // macro" is a wrong explanation of a right refusal, and a wrong
            // explanation is what sends someone looking for a bug that is not
            // there.
            if (slot.Latch && slot.Action is null)
            {
                errors.Add($"Page '{page.Name}', slot {slot.Index}: only an action can be latched, not {(slot.FolderPage is not null ? "a folder" : "a macro")}.");
            }

            // [2026-09-12] The hold gesture (ref/docs/latching-keys.md's
            // hold-to-thrust extension) holds ONE key down for as long as a
            // finger stays on the button - same "no single key to hold"
            // reasoning as the latch check immediately above, for the same
            // macro shape.
            if (slot.Hold && slot.Action is null)
            {
                errors.Add($"Page '{page.Name}', slot {slot.Index}: only an action can be held, not {(slot.FolderPage is not null ? "a folder" : "a macro")}.");
            }

            // A slot fires exactly one of a tap-to-toggle latch or a
            // press-and-hold - both are gestures over the SAME one key, and
            // a button cannot mean both at once.
            if (slot.Latch && slot.Hold)
            {
                errors.Add($"Page '{page.Name}', slot {slot.Index}: cannot be both latched and held - choose one.");
            }

            if (slot.LongPress is not null)
            {
                ValidateExactlyOne(page, $"slot {slot.Index} long-press", slot.LongPress.Action, slot.LongPress.Macro, errors);
                ValidateActionKnown(page, $"slot {slot.Index} long-press", slot.LongPress.Action, knownActionNames, errors);
            }
        }
    }

    /// <summary>
    /// The long-press form: a long-press can only ever be an action or a
    /// macro, never a folder (<see cref="LongPressAction"/> carries no
    /// folder field at all - a second gesture that navigates instead of
    /// firing is a feature nobody asked for, and its absence is enforced by
    /// the type, not here).
    /// </summary>
    private static void ValidateExactlyOne(LayoutPage page, string descriptor, string? action, string? macro, List<string> errors) =>
        ValidateExactlyOne(page, descriptor, action, macro, folderPage: null, errors);

    /// <summary>
    /// [2026-09-16] Widened from two-way to three-way for folders. Counting
    /// rather than comparing two booleans, deliberately: the obvious
    /// three-way rewrite (<c>if (a &amp;&amp; b) ...</c>) keeps the
    /// both-set arm and silently loses the NEITHER arm, which is the one
    /// that catches a slot naming nothing at all.
    /// </summary>
    private static void ValidateExactlyOne(LayoutPage page, string descriptor, string? action, string? macro, string? folderPage, List<string> errors)
    {
        var named = new List<string>(3);
        if (action is not null)
        {
            named.Add("an action");
        }

        if (macro is not null)
        {
            named.Add("a macro");
        }

        if (folderPage is not null)
        {
            named.Add("a folder");
        }

        if (named.Count == 1)
        {
            return;
        }

        var detail = named.Count switch
        {
            0 => "neither an action, a macro nor a folder",
            2 => $"both {named[0]} and {named[1]}",
            _ => $"all of {string.Join(", ", named)}",
        };
        errors.Add($"Page '{page.Name}', {descriptor}: must name exactly one of action/macro/folder (has {detail}).");
    }

    private static void ValidateActionKnown(LayoutPage page, string descriptor, string? action, IReadOnlySet<string>? knownActionNames, List<string> errors)
    {
        if (action is not null && knownActionNames is not null && !knownActionNames.Contains(action))
        {
            errors.Add($"Page '{page.Name}', {descriptor}: unknown action '{action}'.");
        }
    }

    /// <summary>
    /// The shared rule <c>ref/docs/button-naming.md</c> calls for: at most
    /// <see cref="UserLabelMaxLines"/> lines ONCE WRAPPED
    /// (<see cref="WrapLabel"/>), each within
    /// <see cref="UserLabelLineCap"/> characters unless it is a single word
    /// with nowhere to break, and no empty line. This is the ONE place this
    /// decision is made server-side - the client's rename dialog mirrors
    /// this exact logic in JavaScript against the same two numbers
    /// (<c>LunaPanel.Server.Http.PanelClientEndpoint</c> substitutes
    /// <see cref="UserLabelMaxLines"/>/<see cref="UserLabelLineCap"/> in
    /// directly rather than restating them as separate literals), so a name
    /// the dialog accepts is a name this validator accepts too - never two
    /// independently-drifting implementations of one rule.
    ///
    /// [2026-09-09] Made <c>public</c> (was <c>private</c>) for the macro
    /// builder: a user macro's <em>name</em> is what a slot naming it shows
    /// on the button when no override is set (<c>MacroKnowledge.NameFor</c>,
    /// <c>ref/docs/button-naming.md</c>), so it is bounded by this identical
    /// budget and must be checked by this identical method. A second
    /// implementation for macro names - even one copied from here on the day
    /// - is the drift this summary already says it exists to prevent. It
    /// remains the ONE place the rule lives.
    /// </summary>
    public static bool LabelFitsBudget(string label)
    {
        var lines = WrapLabel(label);
        if (lines.Count > UserLabelMaxLines)
        {
            return false;
        }

        foreach (var line in lines)
        {
            // A blank line is still a blank line: it is one of the two the
            // button has, spent on nothing, and the renderer draws it
            // (white-space: pre-line) exactly as it was typed.
            if (line.Length == 0)
            {
                return false;
            }

            // Over the cap survives ONLY when there is nowhere to wrap -
            // one word longer than a line. Coordinator's ruling,
            // 2026-09-09: let it through and let it look cramped rather
            // than refuse a name deliberately typed, and never truncate it
            // silently.
            if (line.Length > UserLabelLineCap && line.Contains(' '))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The lines a label actually occupies on a button: a typed <c>\n</c> is
    /// a hard break, and everything between two hard breaks is word-wrapped
    /// at whitespace to <see cref="UserLabelLineCap"/>, greedily, the same
    /// way the client's own <c>white-space: pre-line</c> rendering breaks it.
    ///
    /// [2026-09-09] New, and the reason two of the three defects found in
    /// live testing existed at all
    /// (<c>ref/docs/button-naming.md</c>'s "Fifteen characters, and the
    /// break the commander cannot type"):
    /// <list type="bullet">
    /// <item>A tablet's on-screen keyboard has no Enter key, so before this
    /// there was no way to reach a second line at all on the device this
    /// product is for. The commander types <c>"Request Docking Now"</c> as
    /// one line and it wraps itself.</item>
    /// <item>Surrounding whitespace is absorbed rather than refused. The
    /// old rule rejected any line that differed from its own
    /// <c>Trim()</c>, which is what refused a ten-character name after an
    /// on-screen keyboard appended a space to a tapped word suggestion -
    /// invisibly, since the offending character is a space.</item>
    /// </list>
    /// A word longer than a whole line has nowhere to break and comes back
    /// as its own over-long line; <see cref="LabelFitsBudget"/> lets that
    /// one case through deliberately. This wraps for MEASUREMENT only -
    /// nothing here rewrites what is stored, because a layout stores the
    /// commander's intent and never a resolution of it
    /// (<c>ref/docs/layouts.md</c>).
    /// </summary>
    public static IReadOnlyList<string> WrapLabel(string label)
    {
        var wrapped = new List<string>();

        foreach (var segment in label.Split('\n'))
        {
            var words = segment.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                wrapped.Add(string.Empty);
                continue;
            }

            var line = words[0];
            for (var i = 1; i < words.Length; i++)
            {
                if (line.Length + 1 + words[i].Length <= UserLabelLineCap)
                {
                    line = $"{line} {words[i]}";
                }
                else
                {
                    wrapped.Add(line);
                    line = words[i];
                }
            }

            wrapped.Add(line);
        }

        return wrapped;
    }
}
