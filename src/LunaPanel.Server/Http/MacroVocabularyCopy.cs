using LunaPanel.Core.GameState;

namespace LunaPanel.Server.Http;

/// <summary>
/// The plain-English half of the macro builder's token pickers - one short
/// name and one sentence of explanation for every <c>Status.json</c> flag,
/// every <c>GuiFocus</c> value, every journal event and every left-panel tab
/// the grammar accepts.
///
/// <b>Why this exists at all.</b> <c>ref/docs/macro-builder.md</c>'s ruling
/// was "one list, everything offered, nothing behind an advanced tier", on
/// the argument that hiding the powerful steps produces worse macros. That
/// ruling only works if the powerful steps are <em>explained</em> instead:
/// a commander has to be able to use "wait for the game" without knowing
/// what <c>GuiFocus:NoFocus</c> means. The commander asked for exactly this
/// ("a friendly, easily usable UI with tooltips and basic instructions",
/// 2026-09-09), and it closes that page's own "How the vocabulary picker
/// glosses a flag" open question.
///
/// <b>Three rules the copy here follows, each earned elsewhere.</b>
///
/// <list type="bullet">
/// <item><b>Say when it is true, not what it is called.</b> A gloss that
/// restates the token ("Docked: you are docked") is filler - the same
/// judgement <c>ref/docs/macro-timing.md</c> made about a tooltip that
/// merely defines the term. Every <see cref="Meaning"/> below names the
/// situation a commander would recognise from the cockpit.</item>
/// <item><b>Never correct Frontier's naming silently.</b>
/// <c>InternalPanel</c> is the RIGHT panel and <c>ExternalPanel</c> is the
/// LEFT one, which <see cref="StatusVocabulary"/>'s own remarks call
/// counter-intuitive and deliberately keep traceable to the Journal Manual.
/// The glosses lead with the side a commander can see and then say what
/// Elite calls it, so somebody reading the raw token underneath is not left
/// thinking the panel got it backwards.</item>
/// <item><b>The raw token stays reachable.</b> These are a layer over the
/// real tables, never a replacement: the builder shows the label first, the
/// meaning under it, and the token itself in small type, so a commander who
/// already knows the vocabulary is not made to guess which pretty name maps
/// to the flag they wanted.</item>
/// </list>
///
/// A missing entry degrades to the raw name and no sentence rather than
/// throwing - a panel request must never fail because somebody added a flag
/// and not its copy. What catches that omission is
/// <c>MacrosEndpointTests</c>'s sweep over the real tables, which fails the
/// build instead.
/// </summary>
public static class MacroVocabularyCopy
{
    /// <param name="Label">The short plain name shown first, in place of the raw token.</param>
    /// <param name="Meaning">One sentence saying when this is true, in terms a commander would recognise.</param>
    public sealed record Gloss(string Label, string Meaning);

    /// <summary>
    /// Every <c>Flags</c> and <c>Flags2</c> condition name in
    /// <see cref="StatusVocabulary"/>. Confidence is carried separately by
    /// the endpoint and deliberately not baked into the sentence: a
    /// community-sourced flag means the same thing as an official one if it
    /// is right, and the picker marks the doubt in its own row rather than
    /// hedging every sentence.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Gloss> Flags = new Dictionary<string, Gloss>(StringComparer.Ordinal)
    {
        // Flags - all 32, Journal Manual v38.
        ["Docked"] = new("Docked", "Your ship is sitting on a landing pad at a station, outpost or fleet carrier."),
        ["Landed"] = new("Landed on a surface", "Your ship is down on a planet's surface rather than on a station pad."),
        ["LandingGearDown"] = new("Landing gear down", "The landing gear is deployed."),
        ["ShieldsUp"] = new("Shields up", "The shields are online - not down and not still coming back."),
        ["Supercruise"] = new("In supercruise", "You are travelling in supercruise rather than in normal space."),
        ["FlightAssistOff"] = new("Flight assist off", "Flight assist is switched off."),
        ["HardpointsDeployed"] = new("Hardpoints out", "Weapons are deployed."),
        ["InWing"] = new("In a wing", "You are winged up with at least one other commander."),
        ["LightsOn"] = new("Ship lights on", "The ship's external lights are on."),
        ["CargoScoopDeployed"] = new("Cargo scoop out", "The cargo scoop is deployed."),
        ["SilentRunning"] = new("Silent running", "Silent running is engaged, so heat is building with nowhere to go."),
        ["ScoopingFuel"] = new("Scooping fuel", "The fuel scoop is drawing fuel from a star right now."),
        ["SrvHandbrake"] = new("SRV handbrake on", "The SRV's handbrake is applied."),
        ["SrvTurretView"] = new("SRV turret view", "You are looking through the SRV's turret rather than driving."),
        ["SrvTurretRetracted"] = new("SRV turret stowed", "The SRV's turret is retracted."),
        ["SrvDriveAssist"] = new("SRV drive assist on", "The SRV's drive assist is switched on."),
        ["FsdMassLocked"] = new("Mass locked", "Something large nearby is holding the frame shift drive down, so you cannot jump or enter supercruise yet."),
        ["FsdCharging"] = new("FSD charging", "The frame shift drive is spinning up, for a jump or for supercruise."),
        ["FsdCooldown"] = new("FSD cooling down", "The frame shift drive is cooling after a jump and will refuse to fire again until it finishes."),
        ["LowFuel"] = new("Low fuel", "Fuel is below a quarter of the tank."),
        ["OverHeating"] = new("Overheating", "The ship is past its heat limit and taking damage."),
        ["HasLatLong"] = new("Close to a surface", "The game is reporting a latitude and longitude, which it only does near a body's surface."),
        ["IsInDanger"] = new("In danger", "The game reckons something is threatening you."),
        ["BeingInterdicted"] = new("Being interdicted", "Someone is pulling you out of supercruise at this moment."),
        ["InMainShip"] = new("In your ship", "You are in your main ship - not a fighter, not an SRV, not on foot."),
        ["InFighter"] = new("In a fighter", "You are flying a ship-launched fighter."),
        ["InSrv"] = new("In an SRV", "You are driving an SRV."),
        ["HudInAnalysisMode"] = new("Analysis mode", "The HUD is in analysis mode rather than combat mode."),
        ["NightVision"] = new("Night vision on", "Night vision is switched on."),
        ["AltitudeFromAverageRadius"] = new("High above a planet", "Altitude is being measured from the body's average radius instead of the ground below you, which is what the game does when you are high up."),
        ["FsdJump"] = new("Jumping", "A hyperspace jump is under way - the tunnel."),
        ["SrvHighBeam"] = new("SRV high beam on", "The SRV's high beam is on."),

        // Flags2 - the Odyssey/on-foot half. Bits 20-22 are the ones
        // StatusVocabulary marks below Official; see this type's remarks for
        // why the doubt is shown in the row rather than hedged into the
        // sentence.
        ["OnFoot"] = new("On foot", "You are out of the ship on foot."),
        ["InTaxi"] = new("In an Apex taxi", "You are riding in a transport ship someone else is flying."),
        ["InMulticrew"] = new("In multicrew", "You are crewing another commander's ship, or one of them is crewing yours."),
        ["OnFootInStation"] = new("On foot in a station", "On foot inside a station."),
        ["OnFootOnPlanet"] = new("On foot on a planet", "On foot on a planet's surface."),
        ["AimDownSight"] = new("Aiming down sights", "On foot, aiming a weapon down its sights."),
        ["LowOxygen"] = new("Low oxygen", "The suit is running short of oxygen."),
        ["LowHealth"] = new("Low health", "Your commander is badly hurt."),
        ["Cold"] = new("Cold", "Cold enough outside for the suit to be working at it."),
        ["Hot"] = new("Hot", "Hot enough outside for the suit to be working at it."),
        ["VeryCold"] = new("Very cold", "Cold enough that it is doing damage."),
        ["VeryHot"] = new("Very hot", "Hot enough that it is doing damage."),
        ["GlideMode"] = new("Gliding", "The ship is in the glide down towards a planet's surface."),
        ["OnFootInHangar"] = new("On foot in a hangar", "On foot in a station or settlement hangar."),
        ["OnFootSocialSpace"] = new("On foot in a concourse", "On foot in the social area of a station or settlement."),
        ["OnFootExterior"] = new("On foot outdoors", "On foot outside, rather than inside a building or station."),
        ["BreathableAtmosphere"] = new("Breathable air", "The air where you are standing can be breathed without drawing on the suit."),
        ["TelepresenceMulticrew"] = new("Crewing by telepresence", "You joined the crew remotely rather than being physically aboard."),
        ["PhysicalMulticrew"] = new("Crewing in person", "You are physically aboard the ship you are crewing."),
        ["FsdHyperdriveCharging"] = new("Charging for a jump", "The frame shift drive is charging specifically for a hyperspace jump, not for supercruise."),
        ["SupercruiseOverdrive"] = new("Supercruise overdrive", "Supercruise overdrive - the speed boost - is engaged."),
        ["SupercruiseAssist"] = new("Supercruise assist", "Supercruise assist is flying the approach for you."),
        ["NpcCrewActive"] = new("Hired crew aboard", "A hired NPC crew member is aboard and active."),
    };

    /// <summary>
    /// The twelve documented <c>GuiFocus</c> values. <c>NoFocus</c> gets the
    /// longest sentence in this file on purpose: it is the one a commander
    /// most needs and least understands, and it is the condition three of
    /// the four shipped macros open with.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Gloss> GuiFocus = new Dictionary<string, Gloss>(StringComparer.Ordinal)
    {
        ["NoFocus"] = new(
            "No panel open",
            "You are looking at the cockpit with none of the ship's panels open. Start here if the macro is going to open a panel itself - otherwise its first press lands in whatever is already on screen."),
        ["InternalPanel"] = new(
            "Right panel open",
            "The right-hand panel - ship systems, modules, cargo. Elite's own name for it is InternalPanel, meaning the ship's insides rather than the left side."),
        ["ExternalPanel"] = new(
            "Left panel open",
            "The left-hand panel - navigation, transactions, contacts. Elite's own name for it is ExternalPanel, meaning the world outside the ship rather than the right side."),
        ["CommsPanel"] = new("Comms panel open", "The comms panel, top left, is open."),
        ["RolePanel"] = new("Role panel open", "The role panel, bottom - fighters, SRV and crew."),
        ["StationServices"] = new("Station services open", "The docked services menu is open."),
        ["GalaxyMap"] = new("Galaxy map open", "The galaxy map is open."),
        ["SystemMap"] = new("System map open", "The system map is open."),
        ["Orrery"] = new("Orrery view open", "The system map's orrery view is open."),
        ["FSS"] = new("Scanner open", "The full-spectrum scanner - the honk-and-tune view - is open."),
        ["SAA"] = new("Surface scanner open", "The detailed surface scanner's mapping view is open, after probes have been fired."),
        ["Codex"] = new("Codex open", "The codex is open."),
    };

    /// <summary>
    /// The journal events the edge grammar accepts. Each sentence
    /// says <em>when Elite writes the line</em>, because that is the thing
    /// that decides whether waiting for it will work: <c>DockingRequested</c>
    /// means the request went out, not that it was granted, and a macro that
    /// waits for the wrong one of those two sits there until it times out.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Gloss> JournalEvents = new Dictionary<string, Gloss>(StringComparer.OrdinalIgnoreCase)
    {
        ["FSDJump"] = new("Arrived in a new system", "Written when a hyperspace jump finishes and you drop into the new system."),
        ["FSDTarget"] = new("Picked the next system", "Written when a star system is selected as the next jump target."),
        ["DockingRequested"] = new("Docking asked for", "Written the moment the request goes out. It says you asked - not that anyone said yes."),
        ["DockingGranted"] = new("Docking granted", "Written when the station gives you a pad number. Usually inside a second."),
        ["DockingDenied"] = new("Docking refused", "Written when the station turns you down - too far out, pads full, or you are wanted here. It arrives on its own, with no grant before it."),
        ["DockingCancelled"] = new("Docking cancelled", "Written when a granted docking is given up or runs out of time."),
        ["Docked"] = new("Docked", "Written once the ship has actually settled on the pad."),
        ["Embark"] = new("Boarded", "Written when your commander climbs into a ship, SRV or taxi."),
        ["Disembark"] = new("Stepped out", "Written when your commander gets out on foot."),
        ["LoadGame"] = new("Session started", "Written once, when the game finishes loading you into the galaxy."),
        ["LaunchVessel"] = new("Launched the Nomad or a fighter", "Written when a Nomad or a fighter leaves the ship's hangar. The Scarab and the Scorpion write 'Launched an SRV' instead."),
        ["LaunchSRV"] = new("Launched an SRV", "Written when a Scarab or a Scorpion leaves the ship's hangar. The Nomad writes 'Launched the Nomad or a fighter' instead."),
        ["DockSRV"] = new("SRV back aboard", "Written when the SRV has finished docking with the ship - a completion, not an acknowledgement. Every SRV writes this one."),
        ["SupercruiseEntry"] = new("Entered supercruise", "Written when the ship reaches supercruise."),
        ["SupercruiseExit"] = new("Dropped to normal space", "Written when the ship leaves supercruise."),
        ["Continued"] = new("Journal rolled to a new file", "Written at the end of a long session's journal, right before the next part starts. The new part has no session-start line, so vessel type is unknown again until the next launch."),

        // 2026-09-12: vocabulary-expansion pass, matching JournalVocabulary's own additions.
        ["Undocked"] = new("Undocked", "Written the moment the ship lifts off the pad and leaves the station or outpost."),
        ["DockingTimeout"] = new("Docking timed out", "Written when a docking request is never answered and the request itself expires."),
        ["Touchdown"] = new("Touched down", "Written when the ship lands on a planet's surface, on its own landing gear rather than a station pad."),
        ["Liftoff"] = new("Lifted off", "Written when the ship leaves a planet's surface under its own power."),
        ["RefuelAll"] = new("Refuelled", "Written when the station's refuel-all service tops off the tank."),
        ["RepairAll"] = new("Repaired", "Written when the station's repair-all service finishes fixing the hull and modules."),
        ["BuyAmmo"] = new("Bought ammo", "Written when the station's restock-ammo service refills the ship's weapons."),
        ["StartJump"] = new("Jump starting", "Written the moment the frame shift drive begins charging, before Elite reveals whether it is a hyperspace jump or a drop to supercruise."),
        ["SupercruiseDestinationDrop"] = new("Dropped at a destination", "Written when the ship drops out of supercruise at a station, planet or other selected destination."),
        ["NavRouteClear"] = new("Route cleared", "Written when a plotted galaxy map route is cancelled or completed and removed."),
        ["FSSDiscoveryScan"] = new("Honked the system", "Written when the full-spectrum scanner's initial discovery scan of the system completes."),
        ["FSSAllBodiesFound"] = new("All bodies found", "Written when the full-spectrum scanner has located every body in the system."),
        ["SAAScanComplete"] = new("Surface mapped", "Written when the detailed surface scanner finishes mapping a body after probes are fired."),
        ["MultiSellExplorationData"] = new("Sold exploration data (batch)", "Written when scan data for several systems is sold to a universal cartographics kiosk in one go."),
        ["SellExplorationData"] = new("Sold exploration data", "Written when scan data for a single system is sold to a universal cartographics kiosk."),
        ["Interdicted"] = new("Pulled from supercruise", "Written when an interdiction succeeds and drags the ship out of supercruise."),
        ["EscapeInterdiction"] = new("Escaped an interdiction", "Written when the ship manages to break free of an interdiction attempt."),
        ["UnderAttack"] = new("Under attack", "Written while something is actively firing on the ship."),
        ["HullDamage"] = new("Hull damaged", "Written when the ship's hull takes damage."),
        ["FighterDestroyed"] = new("Fighter destroyed", "Written when a ship-launched fighter is destroyed."),
        ["SRVDestroyed"] = new("SRV destroyed", "Written when an SRV is destroyed."),
        ["AsteroidCracked"] = new("Asteroid cracked", "Written when a mining charge splits an asteroid open for prospecting."),
        ["MarketBuy"] = new("Bought cargo", "Written when cargo is bought from a station's commodities market."),
        ["MarketSell"] = new("Sold cargo", "Written when cargo is sold to a station's commodities market."),
        ["MissionCompleted"] = new("Mission completed", "Written when a mission is turned in and its rewards are paid out."),
        ["RedeemVoucher"] = new("Voucher redeemed", "Written when a combat, trade or other voucher is cashed in at a station."),
        ["CrewMemberJoins"] = new("Crew member joined", "Written when another commander joins your ship as multicrew."),
        ["CrewMemberQuits"] = new("Crew member left", "Written when a multicrew commander leaves your ship."),
        ["LaunchFighter"] = new("Fighter launched", "Written when a ship-launched fighter leaves the hangar, whether flown by you or an NPC."),
        ["DockFighter"] = new("Fighter docked", "Written when a ship-launched fighter finishes docking back with the ship."),
        ["JoinACrew"] = new("Joined a crew", "Written when you join another commander's ship as multicrew."),
        ["QuitACrew"] = new("Left a crew", "Written when you leave a ship you were crewing as multicrew."),
        ["BookDropship"] = new("Booked a dropship", "Written when an Apex dropship is booked for on-foot transport."),
        ["DropShipDeploy"] = new("Dropship arrived", "Written when a booked dropship deploys you at its destination."),
        ["BookTaxi"] = new("Booked a taxi", "Written when an Apex taxi is booked for on-foot transport."),
        ["CancelTaxi"] = new("Taxi cancelled", "Written when a booked Apex taxi or dropship is cancelled before it arrives."),
        ["SwitchSuitLoadout"] = new("Switched suit loadout", "Written when your on-foot commander switches to a different suit loadout."),
        ["CollectItems"] = new("Collected an item", "Written when your on-foot commander picks up an item or piece of data."),
        ["DropItems"] = new("Dropped an item", "Written when your on-foot commander drops an item or piece of data."),
    };

    /// <summary>
    /// The left panel's four tabs, in the cycle order
    /// <see cref="PanelTab"/> records. The sentences name what is on each
    /// tab rather than restating the tab's name, since the whole point of
    /// this step is that a commander picks the tab they want something from.
    /// </summary>
    public static readonly IReadOnlyDictionary<PanelTab, Gloss> LeftPanelTabs = new Dictionary<PanelTab, Gloss>
    {
        [PanelTab.Galaxy] = new("GALAXY", "The galaxy tab - route and bookmarks. First in the panel's own cycle order."),
        [PanelTab.Navigation] = new("NAVIGATION", "Places you can head for in this system. The panel comes back here by itself after a jump, or after a trip on foot."),
        [PanelTab.Transactions] = new("TRANSACTIONS", "Missions you have taken on, and what you are carrying for them."),
        [PanelTab.Contacts] = new("CONTACTS", "Stations, ships and beacons you can hail - this is the tab a docking request is made from."),
    };

    /// <summary>
    /// The gloss for a name, or a degraded one carrying the raw name and no
    /// sentence. Deliberately non-throwing - see this type's own remarks.
    /// </summary>
    public static Gloss FlagOrFallback(string name) =>
        Flags.TryGetValue(name, out var gloss) ? gloss : new Gloss(name, string.Empty);

    /// <inheritdoc cref="FlagOrFallback"/>
    public static Gloss GuiFocusOrFallback(string name) =>
        GuiFocus.TryGetValue(name, out var gloss) ? gloss : new Gloss(name, string.Empty);

    /// <inheritdoc cref="FlagOrFallback"/>
    public static Gloss JournalEventOrFallback(string name) =>
        JournalEvents.TryGetValue(name, out var gloss) ? gloss : new Gloss(name, string.Empty);

    /// <inheritdoc cref="FlagOrFallback"/>
    public static Gloss LeftPanelTabOrFallback(PanelTab tab) =>
        LeftPanelTabs.TryGetValue(tab, out var gloss) ? gloss : new Gloss(tab.ToString(), string.Empty);
}
