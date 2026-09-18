namespace LunaPanel.Server.Catalogue;

/// <summary>
/// Reads the embedded <c>catalogue.json</c> resource and hands its text to
/// <see cref="LunaPanel.Core.Catalogue.Catalogue.Parse"/> - the one place in
/// <c>LunaPanel.Server</c> that does so for the shipping catalogue, so every
/// caller (the panel endpoint today) shares one parsed instance rather than
/// re-parsing the same shipped JSON on every request. See
/// <c>ref/docs/catalogue.md</c>'s "Where the shipped data lives".
/// </summary>
public static class CatalogueLoader
{
    private const string ResourceName = "LunaPanel.Server.definitions.catalogue.json";

    /// <exception cref="InvalidOperationException">The embedded resource is missing - a packaging defect, not a runtime condition.</exception>
    /// <exception cref="FormatException">The embedded catalogue JSON is malformed - see <see cref="LunaPanel.Core.Catalogue.Catalogue.Parse"/>.</exception>
    public static LunaPanel.Core.Catalogue.Catalogue LoadShipped()
    {
        using var stream = typeof(CatalogueLoader).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return LunaPanel.Core.Catalogue.Catalogue.Parse(reader.ReadToEnd());
    }
}
