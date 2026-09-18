namespace LunaPanel.Core.GameState;

/// <summary>
/// One token of a page's <c>showWhen</c>: either an ordinary
/// <see cref="Condition"/> (<c>Name</c> / <c>!Name</c> /
/// <c>GuiFocus:Name</c>, evaluated against a <see cref="StatusSnapshot"/> -
/// the grammar <c>lit</c> already uses, no new primitive) or the one term
/// the flags cannot supply, <c>Vessel:&lt;type&gt;</c>.
///
/// <para><b>Why this is a separate type from <see cref="Condition"/>, not a
/// row added to <see cref="StatusVocabulary"/>.</b> Exactly the reason
/// <see cref="EdgeCondition"/> is separate (see
/// <c>ref/docs/gamestate.md</c>): <see cref="Condition.Evaluate"/> takes a
/// <see cref="StatusSnapshot"/> and nothing else, and a vessel type is not a
/// field on any snapshot - it comes from the journal. Folding it in would
/// leave <c>Evaluate</c> nowhere to get the value, so a <c>Vessel:</c> token
/// reaching an existing call site would evaluate <see langword="false"/>,
/// silently, forever. Keeping them apart makes that impossible instead:
/// <c>Condition.Parse("Vessel:lander01")</c> <b>throws</b>, because
/// <c>Vessel:lander01</c> is not a name in <see cref="StatusVocabulary"/>.
/// That is pinned by a test.</para>
///
/// <para><b>The casing trap.</b> <c>LoadGame</c> writes <c>"Lander01"</c>
/// while <c>LaunchVessel</c>/<c>DockSRV</c> write <c>"lander01"</c>
/// (<c>ref/docs/vessel-context.md</c>). An ordinal comparison works
/// perfectly against the launch path - the path anyone would naturally write
/// a test for - and fails only for a commander who logged in already inside
/// their SRV. <b>The single comparison in <see cref="Evaluate"/> is where
/// that is handled</b>, case-insensitively, because a <c>showWhen</c> token
/// is written by hand in a layout file and nothing normalises it on the way
/// in. <see cref="VesselContextResolver"/> separately lower-cases the
/// journal's side, but for a different reason - so two readings of one
/// vessel cannot compare as a context CHANGE - not to make this comparison
/// work.</para>
/// </summary>
public sealed class ShowWhenCondition
{
    private const string VesselPrefix = "Vessel:";

    private readonly string? _vesselType;
    private readonly Condition? _condition;

    private ShowWhenCondition(string? vesselType, Condition? condition)
    {
        _vesselType = vesselType;
        _condition = condition;
    }

    /// <exception cref="FormatException">
    /// The token is empty, names no vessel type after <c>Vessel:</c>, or is
    /// an ordinary condition token naming something absent from
    /// <see cref="StatusVocabulary"/> (see <see cref="Condition.Parse"/>).
    /// </exception>
    public static ShowWhenCondition Parse(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new FormatException("A showWhen token must not be empty.");
        }

        if (token.StartsWith(VesselPrefix, StringComparison.Ordinal))
        {
            var vesselType = token[VesselPrefix.Length..];
            if (vesselType.Length == 0)
            {
                throw new FormatException($"showWhen token '{token}' names no vessel type after '{VesselPrefix}'.");
            }

            // Deliberately NOT lower-cased here. The comparison below is
            // case-insensitive, and normalising in both places would leave
            // neither mechanism load-bearing: removing either one alone
            // would still pass every casing test, which is how a guard
            // quietly stops guarding anything.
            return new ShowWhenCondition(vesselType, null);
        }

        return new ShowWhenCondition(null, Condition.Parse(token));
    }

    /// <summary>
    /// A <c>Vessel:</c> term reads the <em>context's</em> vessel type, never
    /// <see cref="JournalStateStore.CurrentVesselType"/> directly - see
    /// <see cref="VesselContext.VesselType"/> for why those are different
    /// questions. A context whose vessel type is <see langword="null"/> (the
    /// journal has never named one, or this is not the SRV context) matches
    /// no <c>Vessel:</c> term at all, which is
    /// <c>ref/docs/vessel-context.md</c>'s "no page matches" path.
    /// </summary>
    public bool Evaluate(StatusSnapshot snapshot, VesselContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);

        if (_vesselType is not null)
        {
            // A null context vessel type simply does not equal the named one
            // - no separate guard, which would be dead code rather than
            // defence.
            return string.Equals(context.VesselType, _vesselType, StringComparison.OrdinalIgnoreCase);
        }

        return _condition!.Evaluate(snapshot);
    }
}

/// <summary>
/// A list of <see cref="ShowWhenCondition"/>s, ANDed together - the same and
/// only composition <see cref="ConditionList"/> offers.
/// </summary>
public sealed class ShowWhenConditionList
{
    public IReadOnlyList<ShowWhenCondition> Conditions { get; }

    private ShowWhenConditionList(IReadOnlyList<ShowWhenCondition> conditions)
    {
        Conditions = conditions;
    }

    /// <exception cref="FormatException">Any token fails to parse - see <see cref="ShowWhenCondition.Parse"/>.</exception>
    public static ShowWhenConditionList Parse(IEnumerable<string> tokens) =>
        new(tokens.Select(ShowWhenCondition.Parse).ToArray());

    public bool Evaluate(StatusSnapshot snapshot, VesselContext context) =>
        Conditions.All(c => c.Evaluate(snapshot, context));
}
