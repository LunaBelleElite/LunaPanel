namespace LunaPanel.Core.Layouts;

/// <summary>
/// The fixed template ladder, decided by slot count. These ids and slot
/// counts are permanent - a saved user layout references a template by id
/// forever, so renaming or removing a rung here orphans every layout in the
/// wild that used it. Add new rungs only by appending; never rename or
/// remove an existing one.
///
/// <c>t36</c>/<c>t48</c>/<c>t64</c> were appended 2026-09-12, extending the
/// ladder from its original six-rung top of <c>t30</c> to a new maximum of
/// 64 slots, with two intermediate steps in between (the user's own framing:
/// "double the offered buttons section to 64 and step it up twice before
/// that"). <c>t36</c> continues the existing "wide, increasing rows" shape
/// (t18=3x6, t24=4x6, t30=5x6, all with a fixed 6-axis) one more step to a
/// square 6x6. <c>t48</c> then grows the fixed axis itself to 8 (6x8),
/// and <c>t64</c> squares that off at 8x8 - three geometric steps from a
/// square 6x6 to a square 8x8, doubling the original t9-style square shape's
/// linear dimension along the way rather than jumping straight there.
///
/// BandAfter for these three follows a rule the existing four nonzero-band
/// rungs all satisfy exactly - band equals half of the axis that grows with
/// orientation (rows in portrait, cols in landscape): t12's 4-row/4-col axis
/// bands at 2, and t18/t24/t30's shared 6-row/6-col axis all band at 3.
/// t36 and t64 are square, so their growing axis is 6 and 8 respectively in
/// both orientations, banding at 3 and 4. t48's growing axis is 8 in both
/// orientations (rows in portrait, cols in landscape), so it also bands at 4.
/// </summary>
public static class Templates
{
    public static readonly IReadOnlyList<PanelTemplate> All = new[]
    {
        new PanelTemplate("t6", 6, new TemplateGeometry(2, 3, 0), new TemplateGeometry(3, 2, 0)),
        new PanelTemplate("t9", 9, new TemplateGeometry(3, 3, 0), new TemplateGeometry(3, 3, 0)),
        new PanelTemplate("t12", 12, new TemplateGeometry(3, 4, 2), new TemplateGeometry(4, 3, 2)),
        new PanelTemplate("t18", 18, new TemplateGeometry(3, 6, 3), new TemplateGeometry(6, 3, 3)),
        new PanelTemplate("t24", 24, new TemplateGeometry(4, 6, 3), new TemplateGeometry(6, 4, 3)),
        new PanelTemplate("t30", 30, new TemplateGeometry(5, 6, 3), new TemplateGeometry(6, 5, 3)),
        new PanelTemplate("t36", 36, new TemplateGeometry(6, 6, 3), new TemplateGeometry(6, 6, 3)),
        new PanelTemplate("t48", 48, new TemplateGeometry(6, 8, 4), new TemplateGeometry(8, 6, 4)),
        new PanelTemplate("t64", 64, new TemplateGeometry(8, 8, 4), new TemplateGeometry(8, 8, 4)),
    };

    private static readonly IReadOnlyDictionary<string, PanelTemplate> ById =
        All.ToDictionary(t => t.Id, t => t, StringComparer.Ordinal);

    /// <summary>
    /// Resolves a template by id. Returns false for an id this build does
    /// not know - e.g. a layout saved by a newer build that added a rung -
    /// rather than throwing; the caller decides how to handle an unknown
    /// template, it is not this method's job to fail the process.
    /// </summary>
    public static bool TryGet(string id, out PanelTemplate? template) =>
        ById.TryGetValue(id, out template);
}
