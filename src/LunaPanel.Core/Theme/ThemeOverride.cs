namespace LunaPanel.Core.Theme;

/// <summary>
/// A commander's manual colour choice (colour chain step 3,
/// <c>ref/docs/theme.md</c>) - the roles they assign directly: Border,
/// Text, Lit, and (since 2026-09-10) Background. Accent and Dim are never
/// part of this record; they are always derived from <see cref="Text"/> via
/// <see cref="HudColorRamp"/>, the same as automatic resolution - see
/// <see cref="HudThemeResolver.FromOverride"/>. Four choices is still a
/// feature, six is still a paint program.
///
/// A layout stores intent, never resolution (<c>ref/docs/layouts.md</c>);
/// the same rule applies here. This record's mere presence for a device
/// (see <see cref="ThemeOverrideStore.Load"/>) IS the override - its
/// absence means "resolve automatically", never "whatever was last
/// resolved".
/// </summary>
/// <param name="Background">
/// The commander's own choice of panel background (<c>--lp-ground</c>), or
/// <see langword="null"/> for "derive it from <see cref="Text"/>", which is
/// what every override written before 2026-09-10 means and what an override
/// whose Background was never picked still means. The same intent-not-
/// resolution rule as the record itself, applied one level down: this is
/// deliberately nullable rather than a required fourth colour, because a
/// required one would force the settings pane to post the currently-derived
/// ground back as though the commander had chosen it - exactly what
/// <c>ref/docs/button-naming.md</c>'s override principle forbids - and would
/// additionally make every already-stored three-colour override file
/// unreadable.
/// </param>
public sealed record ThemeOverride(HudColor Border, HudColor Text, HudColor Lit, HudColor? Background = null);
