namespace LunaPanel.Core.Layouts;

/// <summary>
/// Honest verdict on whether a template's resulting cell size is usable on
/// the device in the user's hand, from <see cref="CellSizeEstimator"/>.
/// <see cref="TooSmall"/>'s 44px floor is a hard accessibility floor and
/// must never be softened.
/// </summary>
public enum ComfortVerdict
{
    TooSmall,
    // [2026-09-17] Renamed from Tight (O13): the one measured Tight rung on a
    // real device (30 slots, phone, landscape) was judged perfectly usable,
    // so the word was reading as a warning where the honest meaning is
    // "smaller, still usable". The 56px floor this rung sits above is
    // unchanged - only the label moved.
    Compact,
    Comfortable
}
