namespace LunaPanel.Core.GameState;

/// <summary>
/// How well attested a journal event name is <em>in this repository</em>.
/// The same idea as <see cref="FlagConfidence"/> on the flag table, and for
/// the same reason: a name we have watched the game write is a different
/// kind of fact from a name we merely believe exists, and collapsing the two
/// hides exactly the risk that matters here.
/// </summary>
public enum JournalEventProvenance
{
    /// <summary>
    /// The exact string has been seen in a real, Elite-written file by this
    /// project - a probe capture, a live check, or a count taken over the
    /// commander's own journal history.
    /// </summary>
    Observed,

    /// <summary>
    /// Named in a repo document but never captured or counted here. A
    /// misspelling in one of these would not have been caught by anything.
    /// </summary>
    Cited,
}

/// <summary>One row of <see cref="JournalVocabulary"/>.</summary>
/// <param name="Name">The event name exactly as Elite writes it.</param>
/// <param name="Provenance">How well attested that spelling is - see <see cref="JournalEventProvenance"/>.</param>
/// <param name="Source">Where in this repo the attestation lives, so a reader can check it.</param>
public sealed record JournalEventEntry(string Name, JournalEventProvenance Provenance, string Source);

/// <summary>
/// The journal event names LunaPanel knows about - the single data table the
/// edge grammar validates against, exactly as <see cref="StatusVocabulary"/>
/// is for the level grammar.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This table is the defence against the spelling trap.</strong>
/// <c>DockingCancelled</c> has a double L, and a misspelled event name never
/// matches anything: it fails silently, forever, with nothing to debug. So
/// an event name that is not in this table is rejected by
/// <see cref="EdgeCondition.Parse"/> at parse time, naming the offending
/// token - the same discipline <see cref="Condition.Parse"/> applies to an
/// unknown flag name, and for the same reason.
/// </para>
/// <para>
/// <strong>[2026-09-12, superseded]</strong> This table used to be kept
/// deliberately short - only events LunaPanel's own design documents named a
/// consumer for, on the reasoning that an event nobody reads is a row nobody
/// has checked the spelling of. The commander asked for the opposite: a
/// broad, curated vocabulary so the macro builder can offer real wait
/// conditions for macros that do not exist yet, not just the ones already
/// wired up. The spelling-trap discipline in the paragraph above is
/// unaffected - every row still needs a real name and an honest
/// <see cref="JournalEventProvenance"/> - but "no named consumer yet" is no
/// longer a reason to leave a real, documented event out. Add a row for any
/// event genuinely useful as a macro wait-condition, sourced honestly.
/// </para>
/// </remarks>
public static class JournalVocabulary
{
    private static readonly JournalEventEntry[] Table =
    {
        // Panel-tab tracking: the re-sync points and the acknowledgement.
        new("FSDJump", JournalEventProvenance.Observed, "live-checks LC4 capture - resets the left panel to NAVIGATION"),
        new("FSDTarget", JournalEventProvenance.Observed, "live-checks LC5 capture - proves the NAVIGATION tab was open"),
        new("DockingRequested", JournalEventProvenance.Observed, "panel-tab-tracking.md - 761 occurrences counted across the commander's own journals"),
        new("DockingGranted", JournalEventProvenance.Observed, "live-checks LC18/LC19 captures - arrives in well under a second"),
        new("DockingDenied", JournalEventProvenance.Observed, "live-checks LC19 capture - fires ALONE, with no preceding DockingRequested, when the request is refused (e.g. Reason \"Distance\")"),
        new("DockingCancelled", JournalEventProvenance.Cited, "panel-tab-tracking.md and in-game-test-plan.md - DOUBLE L; never captured here"),
        new("Docked", JournalEventProvenance.Observed, "live-checks LC16 - the bare-callsign naming shape"),
        new("Embark", JournalEventProvenance.Observed, "probe capture 2026-09-07-A - the on-foot round trip that resets the tab (LC15)"),
        new("Disembark", JournalEventProvenance.Observed, "probe capture 2026-09-07-A - the on-foot round trip that resets the tab (LC15)"),

        // Vessel context: the only four events that name which SRV. The two
        // launch spellings are NOT interchangeable - each vessel writes one
        // of them and never the other, counted 2026-09-10 over the
        // commander's own journals. Knowing only LaunchVessel is what sent
        // the first Scarab boarding to the Nomad's page.
        new("LoadGame", JournalEventProvenance.Observed, "live-checks LC17 - the session-start anchor, and the one that writes a capital L"),
        new("LaunchVessel", JournalEventProvenance.Observed, "probe capture 2026-09-07-C - written for lander01 (the Nomad) and NOTHING else, 33 times across the commander's journals"),
        new("LaunchSRV", JournalEventProvenance.Observed, "counted 2026-09-10 over the commander's journals - 15 testbuggy, 8 combat_multicrew_srv_01, never lander01; carries SRVType, like DockSRV"),
        new("DockSRV", JournalEventProvenance.Observed, "probe capture 2026-09-07-C - a completion, not an acknowledgement (LC17); the one launch-and-dock event written for all three SRVs"),

        // Movement, useful to a consumer deciding whether tracked state is
        // still trustworthy. Supercruise deliberately does NOT reset the tab.
        new("SupercruiseEntry", JournalEventProvenance.Observed, "probe capture 2026-09-07-C"),
        new("SupercruiseExit", JournalEventProvenance.Observed, "probe capture 2026-09-07-C"),

        // The multi-part journal seam (O24). Frontier writes this at the end
        // of a journal part that continues into a new file, rather than
        // leaving that seam silent. Never captured here - no multi-part
        // journal has been examined - so it is Cited, not Observed.
        new("Continued", JournalEventProvenance.Cited, "tests/notes/open-items.md O24 - Frontier's documented multi-part journal seam; never captured here"),

        // 2026-09-12: vocabulary-expansion pass. All Cited - documented in
        // Frontier's journal manual, not yet captured live in this repo -
        // except RefuelAll, which this project's own probe already caught.

        // Docking/departure.
        new("Undocked", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("DockingTimeout", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("Touchdown", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("Liftoff", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),

        // Station services.
        new("RefuelAll", JournalEventProvenance.Observed, "tests/notes/probe-2026-09-07-E.txt:122 - fires right after Docked once the refuel-all service tops off the tank"),
        new("RepairAll", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("BuyAmmo", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),

        // Travel.
        new("StartJump", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("SupercruiseDestinationDrop", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("NavRouteClear", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),

        // Exploration.
        new("FSSDiscoveryScan", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("FSSAllBodiesFound", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("SAAScanComplete", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("MultiSellExplorationData", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("SellExplorationData", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),

        // Combat.
        new("Interdicted", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("EscapeInterdiction", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("UnderAttack", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("HullDamage", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("FighterDestroyed", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("SRVDestroyed", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),

        // Mining/trade.
        new("AsteroidCracked", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("MarketBuy", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("MarketSell", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("MissionCompleted", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("RedeemVoucher", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),

        // Crew/multicrew.
        new("CrewMemberJoins", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("CrewMemberQuits", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("LaunchFighter", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("DockFighter", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("JoinACrew", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("QuitACrew", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),

        // On-foot.
        new("BookDropship", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("DropShipDeploy", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("BookTaxi", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("CancelTaxi", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("SwitchSuitLoadout", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("CollectItems", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
        new("DropItems", JournalEventProvenance.Cited, "Frontier's Journal Manual - documented in Frontier's journal manual, not yet captured live in this repo"),
    };

    private static readonly Dictionary<string, JournalEventEntry> ByName =
        Table.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every known event, keyed case-insensitively by name.</summary>
    public static IReadOnlyDictionary<string, JournalEventEntry> Events => ByName;

    /// <summary>
    /// Looks a name up case-insensitively. Case-forgiveness cannot create a
    /// wrong match, and it costs nothing; it is emphatically <em>not</em> a
    /// defence against a misspelling, which is what the table itself is for.
    /// </summary>
    public static bool TryGet(string name, out JournalEventEntry entry) =>
        ByName.TryGetValue(name, out entry!);

    /// <summary>Whether this event name is one LunaPanel tracks.</summary>
    public static bool IsKnown(string name) => ByName.ContainsKey(name);
}
