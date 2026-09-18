namespace LunaPanel.Core.Theme;

/// <summary>
/// One <c>ui_groups[].Elements[]</c> entry from <c>ThemeSettings.json</c>
/// whose <c>ValueType</c> is <c>"Color"</c> - the schema mapping a human
/// title to the INI file and key tuple that actually holds the live value.
/// Non-colour elements (<c>Preset</c>, <c>ONOFF</c>, <c>Brightness</c>, ...)
/// never become one of these; see <see cref="EdhmThemeSettingsParser"/>.
/// </summary>
/// <param name="Title">The human title, e.g. <c>"Main Text Color"</c>.</param>
/// <param name="File">Which INI file holds the live value (e.g. <c>"Advanced"</c>, <c>"Startup-Profile"</c>) - a name, never a path.</param>
/// <param name="Keys">The <c>Key</c> field split on <c>|</c>, e.g. <c>["x77","y77","z77","w77"]</c>. At least 3 entries (R,G,B); a 4th, when present, is alpha and is not used to build a <see cref="HudColor"/>.</param>
public sealed record EdhmColorElement(string Title, string File, IReadOnlyList<string> Keys);
