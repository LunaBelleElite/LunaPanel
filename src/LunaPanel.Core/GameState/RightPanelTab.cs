namespace LunaPanel.Core.GameState;

/// <summary>
/// The right panel's eight tabs, in cycle order - given by the commander
/// 2026-09-07, left to right exactly as they appear in-game:
/// <c>HOME, MODULES, FIRE GROUPS, SHIP, INVENTORY, STORAGE, STATUS, PLAYLIST</c>.
///
/// Unlike <see cref="PanelTab"/> (the left panel's four tabs, measured live
/// against a running session - see <c>ref/docs/panel-tab-tracking.md</c>'s
/// LC19), this order is recorded but not independently measured: nobody has
/// confirmed live that <c>CycleNextPanel</c>/<c>CyclePreviousPanel</c> visit
/// these eight in exactly this sequence, that the strip wraps the same way
/// the left panel's does, or that <see cref="Home"/> specifically is where a
/// fresh session leaves it (see <see cref="PanelTabTracker"/>'s remarks,
/// "Fix 7", for what is assumed here and why). Treat this enum as the
/// commander's stated order, not a live measurement, until an LC entry says
/// otherwise.
/// </summary>
public enum RightPanelTab
{
    Home,
    Modules,
    FireGroups,
    Ship,
    Inventory,
    Storage,
    Status,
    Playlist,
}
