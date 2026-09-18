using LunaPanel.Server.Catalogue;

namespace LunaPanel.Tests.Catalogue;

/// <summary>
/// Pins <see cref="CatalogueLoader.LoadShipped"/> against the real embedded
/// resource - the same shipped <c>catalogue.json</c>
/// <see cref="CatalogueTests"/> reads directly via <c>Assembly.Load</c>/
/// <c>GetManifestResourceStream</c>, exercised here through the actual
/// production loader instead.
/// </summary>
public class CatalogueLoaderTests
{
    [Fact]
    public void LoadShipped_ParsesTheRealEmbeddedCatalogue_WithoutThrowing()
    {
        var catalogue = CatalogueLoader.LoadShipped();

        Assert.True(catalogue.Actions.Count > 100);
        Assert.True(catalogue.Categories.Count > 0);
    }

    [Fact]
    public void LoadShipped_ContainsLandingGearToggle_AsACuratedAction()
    {
        var catalogue = CatalogueLoader.LoadShipped();

        Assert.True(catalogue.Actions.ContainsKey("LandingGearToggle"));
    }
}
