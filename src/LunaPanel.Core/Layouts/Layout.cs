namespace LunaPanel.Core.Layouts;

/// <summary>
/// A device's full layout: every page it holds. See
/// <c>ref/docs/design-decisions.md</c>'s "Data model: a layout stores
/// intent, never resolution" - nothing in this type or anything it
/// contains stores a key, a resolved display label, or a lit/unlit state;
/// all of that is recomputed on every read (see <see cref="LayoutAnnotator"/>),
/// which is what lets a rebind in Elite heal a slot with no edit to this
/// data at all.
/// </summary>
/// <param name="SchemaVersion">The schema version this layout is currently expressed at. Always <see cref="LayoutMigrator.CurrentSchemaVersion"/> once produced by <see cref="LayoutStore.Load"/> or <see cref="LayoutJson.Parse"/> - <see cref="LayoutMigrator"/> brings anything older up before it's parsed into this type.</param>
/// <param name="Pages">The device's pages.</param>
public sealed record Layout(int SchemaVersion, IReadOnlyList<LayoutPage> Pages);
