using System.Linq;
using System.Text.Json;
using LunaPanel.Core.GameState;
using LunaPanel.Core.Input;

namespace LunaPanel.Core.Macros;

/// <summary>
/// One macro: an id (what a layout slot names - see "a slot names an
/// action... or a macro by id" in <c>ref/docs/design-decisions.md</c>), an
/// optional display name, and its ordered steps.
///
/// <see cref="Parse"/> takes already-read JSON text - same discipline as
/// every other Core parser (<c>StatusJsonParser</c>, <c>BindingsFile.Parse</c>,
/// <c>Catalogue.Parse</c>): Core never discovers a file itself.
/// <c>LunaPanel.Server</c> hands over embedded-resource content.
///
/// Unlike <c>StatusJsonParser</c>/<c>BindingsFile.Parse</c> (runtime data
/// that can be caught mid-write and must never throw), <see cref="Parse"/>
/// follows <c>Catalogue.Parse</c>'s discipline instead and <b>throws
/// <see cref="FormatException"/> on anything malformed</b>: a macro
/// definition is content LunaPanel ships, not something read off the
/// player's machine, so a malformed one is a build-time defect that should
/// fail loudly rather than silently shipping a macro that can't run.
/// </summary>
/// <param name="Id">The macro's stable identifier, referenced by layout slots.</param>
/// <param name="Name">An optional human-readable display name.</param>
/// <param name="Steps">The macro's steps, in execution order. Never empty.</param>
public sealed record MacroDefinition(string Id, string? Name, IReadOnlyList<MacroStep> Steps)
{
    private const string PressKey = "press";
    private const string PressKeyKey = "pressKey";
    private const string WaitKey = "wait";
    private const string RequireKey = "require";
    private const string WaitForKey = "waitFor";
    private const string PressUntilKey = "pressUntil";
    private const string WaitForEdgeKey = "waitForEdge";
    private const string GotoLeftPanelTabKey = "gotoLeftPanelTab";
    private const string BranchKey = "branch";

    // Branch ARM field names. Deliberately NOT in StepKindKeys: they are
    // fields of a branch step, not step kinds, so a step object carrying
    // `branch` + `then` + `else` is one kind, not three (which the
    // exactly-one-kind check above would otherwise reject).
    private const string ThenKey = "then";
    private const string ElseKey = "else";

    // How deep `branch` may appear. 0 = the macro's own top-level `steps`
    // array; 1 = inside a `then`/`else` arm, where a further branch is
    // refused. See BranchStep's remarks for why the cap exists at all.
    private const int MaxBranchDepth = 1;

    /// <summary>
    /// Every discriminator key the grammar recognizes - the authority, and
    /// the thing a step object must carry exactly one of.
    ///
    /// [2026-09-09] Made <c>public</c> (was <c>private</c>) for the macro
    /// builder. The commander is offered <b>every</b> step kind, with none
    /// hidden behind an "advanced" tier
    /// (<c>ref/docs/macro-builder.md</c>, question 1: hiding
    /// <c>gotoLeftPanelTab</c> produces worse macros, since the alternative
    /// a commander reaches for instead is raw <c>CycleNextPanel</c>
    /// presses). Exposing this list is what lets a test sweep it against
    /// what the builder's own step-kind list offers, so a grammar addition
    /// that the builder forgets to offer fails a test rather than quietly
    /// being unauthorable.
    /// </summary>
    public static readonly IReadOnlyList<string> StepKindKeys =
        new[] { PressKey, PressKeyKey, WaitKey, RequireKey, WaitForKey, PressUntilKey, WaitForEdgeKey, GotoLeftPanelTabKey, BranchKey };

    /// <summary>
    /// The non-throwing entry <b>user-authored</b> macro content goes
    /// through - <see cref="Parse"/>'s exact grammar and exact messages,
    /// reported as a <see cref="MacroParseResult"/> instead of thrown.
    ///
    /// There is deliberately no second grammar and no second error shape
    /// here: this wraps <see cref="Parse"/> rather than reimplementing it,
    /// so a grammar addition can never be understood by one entry point and
    /// not the other, and the message a commander sees is the same message
    /// a malformed shipped macro would have failed the build with (every one
    /// of them already names the macro id and the offending step index).
    /// See <see cref="MacroParseResult"/> for why the two categories differ
    /// at all.
    /// </summary>
    public static MacroParseResult TryParse(string json)
    {
        try
        {
            return MacroParseResult.Ok(Parse(json));
        }
        catch (FormatException ex)
        {
            return MacroParseResult.Fail(ex.Message);
        }
    }

    /// <exception cref="FormatException">
    /// The JSON is malformed, or any step fails validation - missing/empty
    /// id, no steps, a step naming zero or more than one kind, an unknown
    /// (blank) action name, an unparseable condition token, or a
    /// non-positive <c>repeat</c>/<c>maxAttempts</c>. The message names the
    /// macro id (when known) and the offending step index.
    ///
    /// <b>User-authored content must not come through here</b> - see
    /// <see cref="TryParse"/>, which reports the same failures without
    /// throwing.
    /// </exception>
    public static MacroDefinition Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Macro definition is not valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Macro definition must be a JSON object.");
            }

            if (!root.TryGetProperty("id", out var idElement) ||
                idElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(idElement.GetString()))
            {
                throw new FormatException("Macro definition is missing a non-empty 'id'.");
            }

            var id = idElement.GetString()!;

            string? name = root.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()
                : null;

            if (!root.TryGetProperty("steps", out var stepsElement) ||
                stepsElement.ValueKind != JsonValueKind.Array ||
                stepsElement.GetArrayLength() == 0)
            {
                throw new FormatException($"Macro '{id}' has no 'steps' array, or it is empty.");
            }

            var steps = ParseStepArray(id, stepsElement, labelPrefix: null, depth: 0);

            return new MacroDefinition(id, name, steps);
        }
    }

    /// <summary>
    /// Parses one array of steps - the macro's own <c>steps</c>, or one arm
    /// of a <c>branch</c>. <b>One dispatch method, two call sites</b>, not a
    /// nested copy of the grammar: an arm's steps are parsed by exactly the
    /// code a top-level step is, so a grammar rule can never hold at one
    /// depth and not the other.
    ///
    /// <paramref name="labelPrefix"/> is <see langword="null"/> for the root
    /// array, where a step's label is the bare index every existing error
    /// message already names (<c>step 2</c>). Inside an arm it is the
    /// branch's own label plus the arm's field name, so nested errors read
    /// <c>step 2.then[1]</c> rather than colliding with top-level
    /// <c>step 1</c> - the commander otherwise goes looking at a step that
    /// parsed perfectly.
    /// </summary>
    private static List<MacroStep> ParseStepArray(string macroId, JsonElement arrayElement, string? labelPrefix, int depth)
    {
        var steps = new List<MacroStep>();
        var index = 0;
        foreach (var stepElement in arrayElement.EnumerateArray())
        {
            var label = labelPrefix is null
                ? index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : $"{labelPrefix}[{index}]";
            steps.Add(ParseStep(macroId, label, stepElement, depth));
            index++;
        }

        return steps;
    }

    private static MacroStep ParseStep(string macroId, string stepLabel, JsonElement stepElement, int depth)
    {
        if (stepElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"Macro '{macroId}' step {stepLabel}: expected a JSON object.");
        }

        var kindsPresent = StepKindKeys.Where(key => stepElement.TryGetProperty(key, out _)).ToArray();

        if (kindsPresent.Length == 0)
        {
            throw new FormatException(
                $"Macro '{macroId}' step {stepLabel}: has no recognized step kind (expected exactly one of {string.Join("/", StepKindKeys)}).");
        }

        if (kindsPresent.Length > 1)
        {
            throw new FormatException(
                $"Macro '{macroId}' step {stepLabel}: has more than one step kind ({string.Join(", ", kindsPresent)}) - exactly one is required.");
        }

        return kindsPresent[0] switch
        {
            PressKey => ParsePress(macroId, stepLabel, stepElement),
            PressKeyKey => ParsePressKey(macroId, stepLabel, stepElement),
            WaitKey => ParseWait(macroId, stepLabel, stepElement),
            RequireKey => ParseRequire(macroId, stepLabel, stepElement),
            WaitForKey => ParseWaitFor(macroId, stepLabel, stepElement),
            PressUntilKey => ParsePressUntil(macroId, stepLabel, stepElement),
            WaitForEdgeKey => ParseWaitForEdge(macroId, stepLabel, stepElement),
            GotoLeftPanelTabKey => ParseGotoLeftPanelTab(macroId, stepLabel, stepElement),
            BranchKey => ParseBranch(macroId, stepLabel, stepElement, depth),
            var other => throw new InvalidOperationException($"Unreachable - unhandled step kind '{other}'.")
        };
    }

    /// <summary>
    /// <c>{ "branch": [conditions], "then": [steps], "else": [steps] }</c> -
    /// see <see cref="BranchStep"/> for what it means at run time. Three
    /// load-time refusals, all of them rejecting a macro that would be
    /// meaningless rather than merely unusual:
    ///
    /// <list type="bullet">
    /// <item>No condition tokens - an always-true test is not a branch.</item>
    /// <item>Nothing in either arm - a branch that does nothing whichever way
    /// it goes.</item>
    /// <item>A <c>branch</c> nested inside an arm - the one-level cap.</item>
    /// </list>
    /// </summary>
    private static BranchStep ParseBranch(string macroId, string stepLabel, JsonElement stepElement, int depth)
    {
        if (depth >= MaxBranchDepth)
        {
            throw new FormatException(
                $"Macro '{macroId}' step {stepLabel}: '{BranchKey}' cannot be nested inside another branch's " +
                $"'{ThenKey}'/'{ElseKey}' - one level of branching only.");
        }

        var tokens = RequireStringArray(stepElement, BranchKey, macroId, stepLabel);
        if (tokens.Count == 0)
        {
            throw new FormatException($"Macro '{macroId}' step {stepLabel}: '{BranchKey}' must name at least one condition.");
        }

        var conditions = ParseConditions(tokens, macroId, stepLabel);

        var then = ParseBranchArm(macroId, stepLabel, stepElement, ThenKey, depth);
        var otherwise = ParseBranchArm(macroId, stepLabel, stepElement, ElseKey, depth);

        if (then.Count == 0 && otherwise.Count == 0)
        {
            throw new FormatException(
                $"Macro '{macroId}' step {stepLabel}: '{BranchKey}' must have at least one step in '{ThenKey}' or '{ElseKey}'.");
        }

        return new BranchStep(conditions, tokens, then, otherwise);
    }

    /// <summary>
    /// One arm. Absent means empty - the same omit-when-absent convention
    /// every other optional field in this grammar follows - but a present
    /// arm that is not an array is a shape error reported as one, never
    /// silently treated as absent.
    /// </summary>
    private static IReadOnlyList<MacroStep> ParseBranchArm(string macroId, string stepLabel, JsonElement stepElement, string field, int depth)
    {
        if (!stepElement.TryGetProperty(field, out var armElement))
        {
            return Array.Empty<MacroStep>();
        }

        if (armElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"Macro '{macroId}' step {stepLabel}: '{field}' must be an array of steps when present.");
        }

        return ParseStepArray(macroId, armElement, $"{stepLabel}.{field}", depth + 1);
    }

    private static PressStep ParsePress(string macroId, string stepLabel, JsonElement stepElement)
    {
        var action = RequireNonEmptyString(stepElement, PressKey, macroId, stepLabel);
        var repeat = RequirePositiveInt(stepElement, "repeat", macroId, stepLabel);
        var holdMs = OptionalPositiveInt(stepElement, "holdMs", macroId, stepLabel);
        return new PressStep(action, repeat, holdMs is int h ? TimeSpan.FromMilliseconds(h) : null);
    }

    /// <summary>
    /// Unlike <see cref="ParsePress"/>'s action name, <c>pressKey</c>'s value
    /// IS checked against a real vocabulary at load time -
    /// <see cref="Scancodes.TryGet"/> - because that vocabulary
    /// (<see cref="Scancodes.All"/>) lives in this same assembly and is fixed
    /// at build time, unlike Frontier's action names, which only exist in a
    /// player's own <c>.binds</c> file read at run time (see
    /// <see cref="RequireNonEmptyString"/>'s own remarks on why an action
    /// name gets no such check). A macro shipped with a key name Core has
    /// never heard of is a build-time defect, not a "not bound yet" state,
    /// so it fails loudly here rather than only at press time.
    /// </summary>
    private static PressKeyStep ParsePressKey(string macroId, string stepLabel, JsonElement stepElement)
    {
        if (!stepElement.TryGetProperty(PressKeyKey, out var keyElement) ||
            keyElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(keyElement.GetString()))
        {
            throw new FormatException($"Macro '{macroId}' step {stepLabel}: '{PressKeyKey}' must be a non-empty key name.");
        }

        var key = keyElement.GetString()!;
        if (!Scancodes.TryGet(key, out _))
        {
            throw new FormatException(
                $"Macro '{macroId}' step {stepLabel}: '{PressKeyKey}' names an unrecognized key '{key}'.");
        }

        var repeat = RequirePositiveInt(stepElement, "repeat", macroId, stepLabel);
        var holdMs = OptionalPositiveInt(stepElement, "holdMs", macroId, stepLabel);
        return new PressKeyStep(key, repeat, holdMs is int h ? TimeSpan.FromMilliseconds(h) : null);
    }

    private static WaitStep ParseWait(string macroId, string stepLabel, JsonElement stepElement)
    {
        var ms = RequireNonNegativeInt(stepElement, WaitKey, macroId, stepLabel);
        return new WaitStep(TimeSpan.FromMilliseconds(ms));
    }

    private static RequireStep ParseRequire(string macroId, string stepLabel, JsonElement stepElement)
    {
        // No meta-tokens here anymore: `LeftPanelTabKnown` was removed
        // 2026-09-08 (RequireStep's own remarks, "Reversed 2026-09-08"), so
        // every token in the array is an ordinary status condition. A macro
        // that still names the retired token is not silently ignored - it
        // falls straight through to ParseConditions/ConditionList.Parse,
        // which rejects it as an unknown condition name, naming it in the
        // FormatException.
        var tokens = RequireStringArray(stepElement, RequireKey, macroId, stepLabel);
        var conditions = ParseConditions(tokens, macroId, stepLabel);
        return new RequireStep(conditions, tokens);
    }

    private static WaitForStep ParseWaitFor(string macroId, string stepLabel, JsonElement stepElement)
    {
        var tokens = RequireStringArray(stepElement, WaitForKey, macroId, stepLabel);
        var conditions = ParseConditions(tokens, macroId, stepLabel);
        var timeoutMs = RequirePositiveInt(stepElement, "timeoutMs", macroId, stepLabel);
        return new WaitForStep(conditions, tokens, TimeSpan.FromMilliseconds(timeoutMs));
    }

    private static PressUntilStep ParsePressUntil(string macroId, string stepLabel, JsonElement stepElement)
    {
        var action = RequireNonEmptyString(stepElement, PressUntilKey, macroId, stepLabel);

        // Exactly one of 'cond' (a STATUS condition array) or 'condJournal'
        // (a single JOURNAL event name) is required - both given, or
        // neither, is a load-time error. Presence alone decides which
        // branch parses below; a malformed value for whichever key IS
        // present still fails with its own specific message via
        // RequireStringArray/RequireNonEmptyString/ParseJournalCondition.
        var hasCond = stepElement.TryGetProperty("cond", out _);
        var hasCondJournal = stepElement.TryGetProperty("condJournal", out _);

        if (hasCond == hasCondJournal)
        {
            throw new FormatException(
                $"Macro '{macroId}' step {stepLabel}: 'pressUntil' must specify exactly one of 'cond' or 'condJournal', " +
                $"not {(hasCond ? "both" : "neither")}.");
        }

        ConditionList? conditions = null;
        IReadOnlyList<string>? tokens = null;
        EdgeCondition? journalCondition = null;

        if (hasCond)
        {
            tokens = RequireStringArray(stepElement, "cond", macroId, stepLabel);
            conditions = ParseConditions(tokens, macroId, stepLabel);
        }
        else
        {
            var eventName = RequireNonEmptyString(stepElement, "condJournal", macroId, stepLabel);
            journalCondition = ParseJournalCondition(eventName, macroId, stepLabel);
        }

        var timeoutMs = RequirePositiveInt(stepElement, "timeoutMs", macroId, stepLabel);
        var maxAttempts = RequirePositiveInt(stepElement, "maxAttempts", macroId, stepLabel);
        return new PressUntilStep(action, conditions, tokens, TimeSpan.FromMilliseconds(timeoutMs), maxAttempts, journalCondition);
    }

    /// <summary>
    /// Parses <c>condJournal</c>'s bare journal event name (e.g.
    /// <c>"RefuelAll"</c>, no <c>Journal:</c> prefix - the prefix is this
    /// grammar's own internal spelling for <see cref="EdgeCondition.Parse"/>,
    /// not something a macro author writes) into an <see cref="EdgeCondition"/>,
    /// via the exact same parser <see cref="ParseEdgeConditions"/> uses for
    /// <c>waitForEdge</c>/<c>failOn</c> tokens.
    /// </summary>
    private static EdgeCondition ParseJournalCondition(string eventName, string macroId, string stepLabel)
    {
        try
        {
            return EdgeCondition.Parse(EdgeCondition.Prefix + eventName);
        }
        catch (FormatException ex)
        {
            throw new FormatException($"Macro '{macroId}' step {stepLabel}: 'condJournal': {ex.Message}", ex);
        }
    }

    private static WaitForEdgeStep ParseWaitForEdge(string macroId, string stepLabel, JsonElement stepElement)
    {
        var succeedTokens = RequireStringArray(stepElement, WaitForEdgeKey, macroId, stepLabel);
        if (succeedTokens.Count == 0)
        {
            throw new FormatException($"Macro '{macroId}' step {stepLabel}: '{WaitForEdgeKey}' must name at least one condition.");
        }

        var succeedOn = ParseEdgeConditions(succeedTokens, macroId, stepLabel, WaitForEdgeKey);
        var failTokens = OptionalStringArray(stepElement, "failOn", macroId, stepLabel);
        var failOn = ParseEdgeConditions(failTokens, macroId, stepLabel, "failOn");

        string? failureDetailField = null;
        if (stepElement.TryGetProperty("failureDetailField", out var detailElement))
        {
            if (detailElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(detailElement.GetString()))
            {
                throw new FormatException($"Macro '{macroId}' step {stepLabel}: 'failureDetailField' must be a non-empty string when present.");
            }

            failureDetailField = detailElement.GetString();
        }

        var timeoutMs = OptionalPositiveInt(stepElement, "timeoutMs", macroId, stepLabel);
        var timeout = timeoutMs is int t ? TimeSpan.FromMilliseconds(t) : MacroTimingDefaults.DefaultWaitForEdgeTimeout;

        return new WaitForEdgeStep(succeedOn, succeedTokens, failOn, failTokens, failureDetailField, timeout);
    }

    private static GotoLeftPanelTabStep ParseGotoLeftPanelTab(string macroId, string stepLabel, JsonElement stepElement)
    {
        if (!stepElement.TryGetProperty(GotoLeftPanelTabKey, out var element) ||
            element.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new FormatException($"Macro '{macroId}' step {stepLabel}: '{GotoLeftPanelTabKey}' must be a non-empty tab name.");
        }

        var name = element.GetString()!;
        if (!Enum.TryParse<PanelTab>(name, ignoreCase: false, out var target))
        {
            throw new FormatException(
                $"Macro '{macroId}' step {stepLabel}: unknown left-panel tab '{name}' in '{GotoLeftPanelTabKey}'. " +
                $"Expected one of: {string.Join(", ", Enum.GetNames<PanelTab>())}.");
        }

        return new GotoLeftPanelTabStep(target);
    }

    private static IReadOnlyList<EdgeCondition> ParseEdgeConditions(IReadOnlyList<string> tokens, string macroId, string stepLabel, string field)
    {
        var list = new List<EdgeCondition>(tokens.Count);
        foreach (var token in tokens)
        {
            try
            {
                list.Add(EdgeCondition.Parse(token));
            }
            catch (FormatException ex)
            {
                throw new FormatException($"Macro '{macroId}' step {stepLabel}: '{field}': {ex.Message}", ex);
            }
        }

        return list;
    }

    private static IReadOnlyList<string> OptionalStringArray(JsonElement obj, string field, string macroId, string stepLabel)
    {
        if (!obj.TryGetProperty(field, out var element))
        {
            return Array.Empty<string>();
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"Macro '{macroId}' step {stepLabel}: '{field}' must be an array of condition strings when present.");
        }

        var list = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new FormatException($"Macro '{macroId}' step {stepLabel}: '{field}' must contain only strings.");
            }

            list.Add(item.GetString()!);
        }

        return list;
    }

    // Action names are validated for shape only (non-empty/non-blank) - a
    // step naming a real-but-nonexistent Frontier action is not a load-time
    // defect the way a blank one is, because Core has no vocabulary of real
    // action names to check against at definition-load time (that's the
    // player's own .binds file, resolved later, at run time, via the same
    // lookup slot status uses - see MacroRunner). A blank/missing name can
    // never resolve to anything, so it is rejected here instead of surfacing
    // as a confusing "unbound" outcome the first time the macro runs.
    private static string RequireNonEmptyString(JsonElement obj, string field, string macroId, string stepLabel)
    {
        if (!obj.TryGetProperty(field, out var element) ||
            element.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new FormatException($"Macro '{macroId}' step {stepLabel}: '{field}' must be a non-empty action name.");
        }

        return element.GetString()!;
    }

    private static int RequirePositiveInt(JsonElement obj, string field, string macroId, string stepLabel)
    {
        if (!obj.TryGetProperty(field, out var element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out var value) ||
            value <= 0)
        {
            throw new FormatException($"Macro '{macroId}' step {stepLabel}: '{field}' must be a positive integer.");
        }

        return value;
    }

    private static int? OptionalPositiveInt(JsonElement obj, string field, string macroId, string stepLabel)
    {
        if (!obj.TryGetProperty(field, out var element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value) || value <= 0)
        {
            throw new FormatException($"Macro '{macroId}' step {stepLabel}: '{field}' must be a positive integer when present.");
        }

        return value;
    }

    private static int RequireNonNegativeInt(JsonElement obj, string field, string macroId, string stepLabel)
    {
        if (!obj.TryGetProperty(field, out var element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out var value) ||
            value < 0)
        {
            throw new FormatException($"Macro '{macroId}' step {stepLabel}: '{field}' must be a non-negative integer.");
        }

        return value;
    }

    private static IReadOnlyList<string> RequireStringArray(JsonElement obj, string field, string macroId, string stepLabel)
    {
        if (!obj.TryGetProperty(field, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"Macro '{macroId}' step {stepLabel}: '{field}' must be an array of condition strings.");
        }

        var list = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new FormatException($"Macro '{macroId}' step {stepLabel}: '{field}' must contain only strings.");
            }

            list.Add(item.GetString()!);
        }

        return list;
    }

    private static ConditionList ParseConditions(IReadOnlyList<string> tokens, string macroId, string stepLabel)
    {
        try
        {
            return ConditionList.Parse(tokens);
        }
        catch (FormatException ex)
        {
            throw new FormatException($"Macro '{macroId}' step {stepLabel}: {ex.Message}", ex);
        }
    }
}
