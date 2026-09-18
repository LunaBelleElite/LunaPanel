namespace LunaPanel.Core.GameState;

/// <summary>
/// Which kind of vessel the commander is currently in, per
/// <c>ref/docs/vessel-context.md</c>. Every value except
/// <see cref="Unknown"/> comes free from a single <c>Status.json</c> flag
/// already in <see cref="StatusVocabulary"/>.
/// </summary>
public enum VesselContextKind
{
    /// <summary>No snapshot yet, or every context flag is clear (a closed
    /// game, or sat at the main menu).</summary>
    Unknown = 0,

    /// <summary><c>InMainShip</c> - <c>Flags</c> bit 24.</summary>
    MainShip = 1,

    /// <summary><c>InFighter</c> - <c>Flags</c> bit 25.</summary>
    Fighter = 2,

    /// <summary><c>InSrv</c> - <c>Flags</c> bit 26. Measured 2026-09-07: the
    /// Nomad reports here, NOT as a fighter (LC17).</summary>
    Srv = 3,

    /// <summary><c>OnFoot</c> - <c>Flags2</c> bit 0.</summary>
    OnFoot = 4,
}

/// <summary>
/// Where the commander is, as automatic page switching understands it: the
/// kind of vessel from <c>Status.json</c>, plus - and only inside
/// <see cref="VesselContextKind.Srv"/> - which SRV, from the journal.
///
/// <para><b><see cref="VesselType"/> is non-null only for
/// <see cref="VesselContextKind.Srv"/>.</b> This is
/// <c>ref/docs/gamestate.md</c>'s "the flags say <em>whether</em>, the
/// journal says <em>which</em>" composition made concrete:
/// <see cref="JournalStateStore.CurrentVesselType"/> is "the vessel the
/// journal last saw them get into" and legitimately names a <em>ship</em>
/// (<c>krait_mkii</c>) when the commander logged in inside one, so reading
/// it outside an <c>InSrv</c> snapshot would report a ship as the vessel
/// they are in. The consequence, stated plainly because it is a real limit
/// rather than an accident: a <c>Vessel:</c> term in a page's
/// <c>showWhen</c> can only ever match inside the SRV context. See
/// <c>ref/docs/vessel-context.md</c>'s "Not decided".</para>
///
/// <para><b><see cref="VesselType"/> being <see langword="null"/> inside
/// <see cref="VesselContextKind.Srv"/> is a real, reachable state, and it
/// means "in an SRV, and the journal has not said which".</b> It matches no
/// <c>Vessel:</c> page, so <see cref="Layouts.ContextPageSelector"/> takes
/// the "no page matches" path and the panel stays exactly where it is until
/// the launch line arrives. That is deliberately the honest answer rather
/// than the likeliest one: guessing produces a switch to the wrong page
/// followed by a correction, and a page that changes twice under a
/// commander's thumb is worse than one that changes late
/// (<c>ref/docs/vessel-context.md</c>).</para>
///
/// <para>A record, so equality compares both members as a unit - which is
/// the whole change-detection mechanism <see cref="Layouts.AutoPageSwitcher"/>
/// depends on, the same way <see cref="StatusDestination"/> serves
/// <see cref="PanelTabTracker.RecordDestinationChange"/>.</para>
/// </summary>
/// <param name="Kind">Which kind of vessel, from <c>Status.json</c>'s flags.</param>
/// <param name="VesselType">The lower-cased journal vessel type (<c>lander01</c>, <c>testbuggy</c>), or <see langword="null"/> outside <see cref="VesselContextKind.Srv"/> and when the journal has never named one.</param>
public sealed record VesselContext(VesselContextKind Kind, string? VesselType)
{
    /// <summary>No snapshot yet, or no context flag set at all.</summary>
    public static readonly VesselContext Unknown = new(VesselContextKind.Unknown, null);
}

/// <summary>
/// Derives a <see cref="VesselContext"/> from a <see cref="StatusSnapshot"/>
/// and the journal's last-known vessel type. Pure; reads no files and holds
/// no state.
/// </summary>
public static class VesselContextResolver
{
    /// <summary>
    /// Parsed once from <see cref="StatusVocabulary"/> rather than restating
    /// the bit numbers here - a Frontier redefinition then touches one data
    /// row, exactly as <c>ref/docs/gamestate.md</c> intends.
    /// </summary>
    private static readonly Condition InSrv = Condition.Parse("InSrv");
    private static readonly Condition InFighter = Condition.Parse("InFighter");
    private static readonly Condition OnFoot = Condition.Parse("OnFoot");
    private static readonly Condition InMainShip = Condition.Parse("InMainShip");

    /// <summary>
    /// <see cref="VesselContext.Unknown"/> for a <see langword="null"/>
    /// snapshot (no <c>Status.json</c> read yet - not an error) and for a
    /// snapshot with none of the four flags set.
    ///
    /// <para>The four flags are believed mutually exclusive, so the order
    /// below is a <em>deterministic tiebreak</em> for a combination nothing
    /// has ever measured, not a claim that the combination occurs:
    /// SRV, then fighter, then on-foot, then main ship - the broadest last.
    /// It is pinned by a test so it stays a decision rather than an
    /// accident.</para>
    /// </summary>
    public static VesselContext Resolve(StatusSnapshot? snapshot, string? lastKnownVesselType)
    {
        if (snapshot is null)
        {
            return VesselContext.Unknown;
        }

        if (InSrv.Evaluate(snapshot))
        {
            // The one place the journal is read, and only here: the flags say
            // whether, the journal says which.
            return new VesselContext(VesselContextKind.Srv, lastKnownVesselType?.ToLowerInvariant());
        }

        if (InFighter.Evaluate(snapshot))
        {
            return new VesselContext(VesselContextKind.Fighter, null);
        }

        if (OnFoot.Evaluate(snapshot))
        {
            return new VesselContext(VesselContextKind.OnFoot, null);
        }

        if (InMainShip.Evaluate(snapshot))
        {
            return new VesselContext(VesselContextKind.MainShip, null);
        }

        return VesselContext.Unknown;
    }
}
