namespace LunaPanel.Core.GameState;

/// <summary>
/// A single parsed condition: one of <c>Name</c>, <c>!Name</c>, or
/// <c>GuiFocus:Name</c>, evaluated against a <see cref="StatusSnapshot"/>.
/// Deliberately flat - no OR, no nesting. A list of conditions (see
/// <see cref="ConditionList"/>) is how AND is expressed; there is no way to
/// express anything richer than that here, on purpose.
///
/// An unknown name is rejected at <see cref="Parse"/> time with a message
/// naming the offending token, never allowed through to silently evaluate
/// as false later.
/// </summary>
public sealed class Condition
{
    private readonly bool _isGuiFocus;
    private readonly bool _negated;
    private readonly FlagCondition _flagCondition;
    private readonly int _guiFocusValue;

    private Condition(bool isGuiFocus, bool negated, FlagCondition flagCondition, int guiFocusValue)
    {
        _isGuiFocus = isGuiFocus;
        _negated = negated;
        _flagCondition = flagCondition;
        _guiFocusValue = guiFocusValue;
    }

    /// <summary>
    /// Parses a single condition token.
    /// </summary>
    /// <exception cref="FormatException">
    /// The token names a condition or <c>GuiFocus</c> value that does not
    /// exist in <see cref="StatusVocabulary"/>. The message names the
    /// offending token.
    /// </exception>
    public static Condition Parse(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new FormatException("Condition token must not be empty.");
        }

        const string guiFocusPrefix = "GuiFocus:";
        if (token.StartsWith(guiFocusPrefix, StringComparison.Ordinal))
        {
            var name = token[guiFocusPrefix.Length..];
            if (!StatusVocabulary.TryGetGuiFocusValue(name, out var value))
            {
                throw new FormatException($"Unknown GuiFocus name '{name}' in condition token '{token}'.");
            }

            return new Condition(isGuiFocus: true, negated: false, default, value);
        }

        var negated = token.StartsWith('!');
        var flagName = negated ? token[1..] : token;
        if (!StatusVocabulary.TryGetFlagCondition(flagName, out var flagCondition))
        {
            throw new FormatException($"Unknown condition name '{flagName}' in condition token '{token}'.");
        }

        return new Condition(isGuiFocus: false, negated, flagCondition, guiFocusValue: 0);
    }

    public bool Evaluate(StatusSnapshot snapshot)
    {
        if (_isGuiFocus)
        {
            return snapshot.GuiFocus == _guiFocusValue;
        }

        var raw = _flagCondition.Field == FlagsField.Flags ? snapshot.Flags : snapshot.Flags2 ?? 0;
        var isSet = (raw & (1u << _flagCondition.Bit)) != 0;
        return _negated ? !isSet : isSet;
    }
}
