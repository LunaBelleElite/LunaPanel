using System.Text.Json;

namespace LunaPanel.Core.Macros;

/// <summary>
/// Writes a <see cref="MacroDefinition"/> back out as the same JSON grammar
/// <see cref="MacroDefinition.Parse"/> reads (<c>ref/docs/macros.md</c>) -
/// the counterpart <c>LunaPanel.Core.Layouts.LayoutJson.Serialize</c> is to
/// <c>LayoutJson.Parse</c>, and needed for the same reason: a store cannot
/// persist what a commander authored without one.
///
/// <b>There is exactly one grammar, not two.</b> Everything this writes,
/// <see cref="MacroDefinition.Parse"/> reads, and a user macro on disk is
/// the same kind of file a shipped macro is - which is what keeps
/// <c>MacroRunner</c> unable to tell the two apart
/// (<c>ref/docs/macro-builder.md</c>, "What a user macro is").
///
/// <b>Intent, never resolution.</b> A step is written with its action
/// <em>name</em> and its condition tokens verbatim - never a resolved chord,
/// scancode or label. That is the same rule a layout obeys
/// (<c>ref/docs/layouts.md</c>), and it is what makes rebinding an action in
/// Elite heal a user macro instead of breaking it. This type takes no
/// <c>BindingsFile</c> at all, so it could not write a resolved key even if
/// somebody wanted it to.
/// </summary>
public static class MacroJson
{
    /// <summary>
    /// Round-trips: <c>Parse(Serialize(m))</c> is equivalent to <c>m</c> for
    /// every macro this codebase can produce, and
    /// <c>Serialize(Parse(Serialize(m)))</c> is byte-identical to
    /// <c>Serialize(m)</c> - pinned by <c>MacroJsonTests</c> over every
    /// shipped macro, so a grammar addition that forgets to write one of its
    /// fields is caught by the shipped macro that uses it.
    ///
    /// Optional fields are <b>omitted when absent</b> rather than written as
    /// <c>null</c>, because <see cref="MacroDefinition.Parse"/> reads
    /// "absent" and "present but wrong type" differently and an explicit
    /// <c>null</c> is the second of those. The one non-obvious case is
    /// <c>waitForEdge</c>'s <c>timeoutMs</c>: the parser fills an absent one
    /// in from <see cref="MacroTimingDefaults.DefaultWaitForEdgeTimeout"/>,
    /// so writing every timeout back explicitly would freeze today's default
    /// into every stored macro and quietly opt them out of a later, better
    /// measured one. A timeout equal to the default is therefore written as
    /// absence - the value is identical either way, and the macro keeps
    /// following the default.
    /// </summary>
    public static string Serialize(MacroDefinition macro)
    {
        ArgumentNullException.ThrowIfNull(macro);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("id", macro.Id);

            if (macro.Name is not null)
            {
                writer.WriteString("name", macro.Name);
            }

            writer.WriteStartArray("steps");
            foreach (var step in macro.Steps)
            {
                WriteStep(writer, step);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteStep(Utf8JsonWriter writer, MacroStep step)
    {
        writer.WriteStartObject();

        switch (step)
        {
            case PressStep press:
                writer.WriteString("press", press.Action);
                writer.WriteNumber("repeat", press.Repeat);
                if (press.Hold is TimeSpan hold)
                {
                    writer.WriteNumber("holdMs", (int)hold.TotalMilliseconds);
                }

                break;

            case PressKeyStep pressKey:
                writer.WriteString("pressKey", pressKey.Key);
                writer.WriteNumber("repeat", pressKey.Repeat);
                if (pressKey.Hold is TimeSpan pressKeyHold)
                {
                    writer.WriteNumber("holdMs", (int)pressKeyHold.TotalMilliseconds);
                }

                break;

            case WaitStep wait:
                writer.WriteNumber("wait", (int)wait.Duration.TotalMilliseconds);
                break;

            case RequireStep require:
                WriteTokens(writer, "require", require.ConditionTokens);
                break;

            case WaitForStep waitFor:
                WriteTokens(writer, "waitFor", waitFor.ConditionTokens);
                writer.WriteNumber("timeoutMs", (int)waitFor.Timeout.TotalMilliseconds);
                break;

            case PressUntilStep pressUntil:
                writer.WriteString("pressUntil", pressUntil.Action);
                if (pressUntil.JournalCondition is not null)
                {
                    writer.WriteString("condJournal", pressUntil.JournalCondition.EventName);
                }
                else
                {
                    WriteTokens(writer, "cond", pressUntil.ConditionTokens!);
                }

                writer.WriteNumber("timeoutMs", (int)pressUntil.Timeout.TotalMilliseconds);
                writer.WriteNumber("maxAttempts", pressUntil.MaxAttempts);
                break;

            case WaitForEdgeStep edge:
                WriteTokens(writer, "waitForEdge", edge.SucceedOnTokens);
                if (edge.FailOnTokens.Count > 0)
                {
                    WriteTokens(writer, "failOn", edge.FailOnTokens);
                }

                if (edge.FailureDetailField is not null)
                {
                    writer.WriteString("failureDetailField", edge.FailureDetailField);
                }

                if (edge.Timeout != MacroTimingDefaults.DefaultWaitForEdgeTimeout)
                {
                    writer.WriteNumber("timeoutMs", (int)edge.Timeout.TotalMilliseconds);
                }

                break;

            case GotoLeftPanelTabStep goTo:
                writer.WriteString("gotoLeftPanelTab", goTo.Target.ToString());
                break;

            // The only recursive case: a branch's arms are steps, written by
            // this same method, so an arm can never hold a shape the top
            // level could not. An EMPTY arm is omitted rather than written as
            // `[]`, matching the omit-when-absent rule this method already
            // follows everywhere else - MacroDefinition.ParseBranchArm reads
            // absence as an empty arm, so nothing is lost.
            case BranchStep branch:
                WriteTokens(writer, "branch", branch.ConditionTokens);
                if (branch.Then.Count > 0)
                {
                    WriteStepArray(writer, "then", branch.Then);
                }

                if (branch.Else.Count > 0)
                {
                    WriteStepArray(writer, "else", branch.Else);
                }

                break;

            default:
                // Every sealed MacroStep subtype in the grammar is handled
                // above. A new one reaching here has no way to be written,
                // and writing a step-shaped object with no discriminator key
                // would produce a file MacroDefinition.Parse rejects as
                // "no recognized step kind" - a corrupt-file report about
                // something this writer did, which is far harder to trace
                // back than failing at the moment of the omission.
                throw new InvalidOperationException($"Unhandled macro step kind '{step.GetType().Name}' - MacroJson.Serialize needs a case for it.");
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// One <c>branch</c> arm - the step-array counterpart of
    /// <see cref="WriteTokens"/>, recursing into <see cref="WriteStep"/> so
    /// there is still exactly one writer for a step at any depth.
    /// </summary>
    private static void WriteStepArray(Utf8JsonWriter writer, string field, IReadOnlyList<MacroStep> steps)
    {
        writer.WriteStartArray(field);
        foreach (var step in steps)
        {
            WriteStep(writer, step);
        }

        writer.WriteEndArray();
    }

    private static void WriteTokens(Utf8JsonWriter writer, string field, IReadOnlyList<string> tokens)
    {
        writer.WriteStartArray(field);
        foreach (var token in tokens)
        {
            writer.WriteStringValue(token);
        }

        writer.WriteEndArray();
    }
}
