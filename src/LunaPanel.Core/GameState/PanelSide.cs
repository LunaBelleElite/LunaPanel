namespace LunaPanel.Core.GameState;

/// <summary>
/// Which of Elite's two side panels is meant - <see cref="Left"/> is the
/// <c>GuiFocus:ExternalPanel</c> (value 2) panel <see cref="PanelTabTracker"/>
/// already tracked before the right panel existed in this model;
/// <see cref="Right"/> is <c>GuiFocus:InternalPanel</c> (value 1), per
/// <see cref="StatusVocabulary.GuiFocusValues"/>. Counter-intuitive on
/// purpose - that numbering is the game's own, not a choice made here (see
/// <c>StatusVocabulary</c>'s own remarks).
///
/// Used two ways by <see cref="PanelTabTracker"/>: as the caller-supplied
/// ground truth in <see cref="PanelTabTracker.RecordObservedFocus"/>, and as
/// the tracker's own belief of which panel a focus-ambiguous action
/// (<see cref="PanelTabTracker.AdvanceTabAction"/>/
/// <see cref="PanelTabTracker.RetreatTabAction"/>) should apply to - see
/// that type's remarks, "Which panel is focused, and why that can't just be
/// read live".
/// </summary>
public enum PanelSide
{
    Left,
    Right,
}
