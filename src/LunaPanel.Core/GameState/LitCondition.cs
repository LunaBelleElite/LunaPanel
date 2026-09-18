namespace LunaPanel.Core.GameState;

/// <summary>
/// How engaged a lit-capable control currently is. Ordered:
/// <see cref="Off"/> &lt; <see cref="Partial"/> &lt; <see cref="Full"/>.
///
/// This answers "how engaged is this control", never "which of two modes am
/// I in" and never inverted to put a disabled feature at <see cref="Full"/>
/// brightness - <see cref="Partial"/> is what a caution looks like. See
/// <c>ref/docs/lit-state.md</c> for the full rule and the three curated
/// actions that used to get this wrong.
/// </summary>
public enum SlotLitLevel
{
    Off = 0,
    Partial = 1,
    Full = 2,
}

/// <summary>
/// One entry in a catalogue action's ordered <c>lit</c> list: a condition
/// list and the level to report when it is the first entry, in declaration
/// order, whose <see cref="When"/> evaluates true against a
/// <see cref="StatusSnapshot"/>. See <see cref="Catalogue.CatalogueAction.Lit"/>'s
/// own remarks and <c>ref/docs/lit-state.md</c> for the JSON shape this
/// mirrors (<c>{ "when": [...], "level": "..." }</c>) and the bare
/// <c>"lit": ["Name"]</c> shape that normalizes to a single
/// <see cref="SlotLitLevel.Full"/> entry for backward compatibility.
/// </summary>
public sealed record LitCondition(ConditionList When, SlotLitLevel Level)
{
    /// <summary>
    /// Evaluates an ordered list of <see cref="LitCondition"/>s against a
    /// snapshot - first match wins. <see cref="SlotLitLevel.Off"/> when none
    /// match, including an empty list.
    /// </summary>
    public static SlotLitLevel Evaluate(IReadOnlyList<LitCondition> conditions, StatusSnapshot snapshot)
    {
        foreach (var condition in conditions)
        {
            if (condition.When.Evaluate(snapshot))
            {
                return condition.Level;
            }
        }

        return SlotLitLevel.Off;
    }
}
