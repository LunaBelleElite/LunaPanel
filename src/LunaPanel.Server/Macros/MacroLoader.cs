using LunaPanel.Core.Macros;

namespace LunaPanel.Server.Macros;

/// <summary>
/// Reads every embedded macro definition under <c>definitions/macros/</c>
/// and parses each with <see cref="MacroDefinition.Parse"/> - the same
/// hand-off <c>LunaPanel.Server.Catalogue.CatalogueLoader</c> makes for
/// <c>catalogue.json</c>, generalized to more than one file.
///
/// Resources are discovered by manifest name prefix rather than a
/// hand-maintained list of file names, so a macro's own
/// <c>&lt;EmbeddedResource&gt;</c> entry in the csproj is the only place a
/// shipped macro ever needs registering - a second, forgettable list here
/// can't silently drop a real macro from <see cref="Layouts.MacroKnowledgeBuilder"/>'s
/// input.
/// </summary>
public static class MacroLoader
{
    private const string ResourcePrefix = "LunaPanel.Server.definitions.macros.";

    /// <exception cref="InvalidOperationException">A resource matched by name could not be opened - a packaging defect, not a runtime condition.</exception>
    /// <exception cref="FormatException">A shipped macro definition is malformed - see <see cref="MacroDefinition.Parse"/>.</exception>
    public static IReadOnlyList<MacroDefinition> LoadShipped()
    {
        var assembly = typeof(MacroLoader).Assembly;
        var names = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal);

        var macros = new List<MacroDefinition>();
        foreach (var name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded resource '{name}' was not found.");
            using var reader = new StreamReader(stream);
            macros.Add(MacroDefinition.Parse(reader.ReadToEnd()));
        }

        return macros;
    }
}
