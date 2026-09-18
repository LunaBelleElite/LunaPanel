namespace LunaPanel.Core.Layouts;

/// <summary>One slot's freshly-computed annotation, tagged with which page it belongs to. Produced by <see cref="LayoutAnnotator"/>.</summary>
public sealed record LayoutAnnotation(string PageName, SlotAnnotation Slot);
