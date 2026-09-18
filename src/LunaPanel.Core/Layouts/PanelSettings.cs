namespace LunaPanel.Core.Layouts;

/// <summary>
/// A device's own panel-wide settings - today just one: "merging and
/// expanding panels" (<c>ref/docs/panels-and-pages.md</c>'s "Setting:
/// 'Merging and expanding panels'", the commander's own name for it, used
/// verbatim). Kept as its own small per-device record rather than a field on
/// <see cref="Layout"/> itself - closely related, per-device state, not a
/// schema change to the layout format (same reasoning
/// <c>LunaPanel.Core.Theme.ThemeOverride</c> is kept separate from
/// <see cref="Layout"/> rather than folded in).
/// </summary>
/// <param name="MergeExpand">
/// <see langword="true"/> (the default): buttons flow between panels
/// automatically - shrinking spills the surplus forward
/// (<see cref="LayoutSpiller"/>), growing pulls it back, and a panel left
/// empty by that is removed. <see langword="false"/>: buttons never move
/// between panels - shrinking parks the surplus where it is
/// (<see cref="LayoutParker"/>), growing leaves new slots empty, and panels
/// are removed only when deleted deliberately. Both behaviours are already
/// implemented; this is a switch between them, not a third code path.
/// </param>
/// <param name="ShowMacroStepResults">
/// <see langword="true"/>: pressing a macro-bound button opens the per-step
/// result sheet (<c>#macroRunResult</c>) afterwards. <see langword="false"/>
/// (the default, since 2026-09-17 - previously defaulted on): the sheet
/// never opens, purely client-side - the press itself and the server's
/// response are unaffected either way. A commander who wants the per-step
/// detail back can still turn it on from Settings.
/// </param>
/// <param name="AutoSwitchEnabled">
/// <see langword="true"/> (the default): this device follows automatic
/// vessel-context page switching as today (<c>ref/docs/vessel-context.md</c>).
/// <see langword="false"/>: the gate at the <c>GET /api/panel/live</c> call
/// site skips <c>AutoPageSwitcher.Decide</c> entirely for this device, so no
/// automatic push ever arrives - manual page reachability via the tab row is
/// completely unaffected either way. A commander running two devices at once
/// can turn this off on one while the other keeps following context changes.
/// </param>
public sealed record PanelSettings(bool MergeExpand, bool ShowMacroStepResults, bool AutoSwitchEnabled = true)
{
    /// <summary>The default for a device with no stored settings file yet - merge/expand ON, step results OFF, auto-switch ON.</summary>
    public static readonly PanelSettings Default = new(MergeExpand: true, ShowMacroStepResults: false, AutoSwitchEnabled: true);
}
