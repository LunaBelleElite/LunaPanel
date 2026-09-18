namespace LunaPanel.Core.GameState;

/// <summary>
/// A single parsed <em>edge</em> condition: <c>Journal:EventName</c>, meaning
/// "this journal event has been seen since the caller's watermark".
///
/// This is a different kind of thing from <see cref="Condition"/>, and it is
/// deliberately a separate type rather than a fourth shape inside it.
/// <see cref="Condition.Evaluate"/> takes a <see cref="StatusSnapshot"/> and
/// nothing else, because a level condition needs nothing else: "is this bit
/// set right now" is answerable from the snapshot alone. An edge condition
/// cannot be answered from any snapshot, because there is no field to read -
/// <c>FSDJump</c> is a thing that happened, not a state that holds. It needs
/// two things a level condition never has: a record of what has happened,
/// and a <see cref="JournalWatermark"/> saying since when.
///
/// Folding this into <see cref="Condition"/> would have meant giving
/// <see cref="Condition.Evaluate"/> nowhere to get either, so an edge token
/// reaching any existing call site would have evaluated <see langword="false"/>
/// - silently, forever, which is precisely the failure mode this subsystem's
/// spelling trap already threatens. Keeping the types apart makes that
/// impossible instead: <see cref="Condition.Parse"/> rejects a
/// <c>Journal:</c> token outright, because <c>Journal:FSDJump</c> is not a
/// name in <see cref="StatusVocabulary"/>.
/// </summary>
/// <remarks>
/// There is no negated form. <c>!Journal:X</c> would mean "X has not been
/// seen since T", which is true the instant a mark is taken and stays true
/// until the event arrives - a condition that is trivially satisfiable is not
/// a useful gate, and reading one as "X will not happen" is the mistake it
/// would invite. Left out on purpose; add it only with a consumer that
/// genuinely wants the always-true-at-first reading.
/// </remarks>
public sealed class EdgeCondition
{
    /// <summary>The prefix that distinguishes an edge token from a level one.</summary>
    public const string Prefix = "Journal:";

    private EdgeCondition(string eventName)
    {
        EventName = eventName;
    }

    /// <summary>The event name, spelled as <see cref="JournalVocabulary"/> holds it.</summary>
    public string EventName { get; }

    /// <summary>
    /// Parses a single edge token of the form <c>Journal:EventName</c>.
    /// </summary>
    /// <exception cref="FormatException">
    /// The token is missing the <c>Journal:</c> prefix, or names an event
    /// that is not in <see cref="JournalVocabulary"/>. The message names the
    /// offending token - an unrecognized event is never allowed through to
    /// silently never fire.
    /// </exception>
    public static EdgeCondition Parse(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new FormatException("Edge condition token must not be empty.");
        }

        if (!token.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new FormatException(
                $"Edge condition token '{token}' must start with '{Prefix}'.");
        }

        var name = token[Prefix.Length..];
        if (!JournalVocabulary.TryGet(name, out var entry))
        {
            throw new FormatException(
                $"Unknown journal event name '{name}' in condition token '{token}'. " +
                "Check the spelling against JournalVocabulary - a misspelled event name never matches anything and fails silently.");
        }

        return new EdgeCondition(entry.Name);
    }

    /// <summary>
    /// Whether this event has been recorded by <paramref name="journal"/>
    /// since <paramref name="since"/> was taken.
    /// </summary>
    public bool Evaluate(JournalStateStore journal, JournalWatermark since)
    {
        ArgumentNullException.ThrowIfNull(journal);
        return journal.HasSeenSince(EventName, since);
    }
}
