using LunaPanel.Core.Bindings;
using LunaPanel.Core.Catalogue;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Drives <see cref="ActionsEndpoint.BuildResponse"/> directly - the mapping
/// <c>GET /api/actions</c> serializes, kept free of any ASP.NET type, same
/// discipline as every other pure endpoint type in this namespace. Which of
/// <see cref="CatalogueMerger.Merge"/>/<see cref="CatalogueMerger.MergeAll"/>
/// is handed in is <c>ServerHostBuilder</c>'s own choice (the <c>all</c>
/// query flag) - this type only proves the DTO mapping is faithful to
/// whatever list it's given.
/// </summary>
public class ActionsEndpointTests
{
    private const string CatalogueJson = """
        {
          "catalogueVersion": 1,
          "categories": [ { "id": "ship", "label": "SHIP" } ],
          "actions": {
            "LandingGearToggle": { "label": "GEAR", "category": "ship" }
          }
        }
        """;

    private const string BindingsXml = """
        <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
            <LandingGearToggle>
                <Primary Device="Keyboard" Key="Key_G" />
                <Secondary Device="{NoDevice}" Key="" />
            </LandingGearToggle>
            <UncuratedUnboundAction>
                <Primary Device="{NoDevice}" Key="" />
                <Secondary Device="{NoDevice}" Key="" />
            </UncuratedUnboundAction>
        </Root>
        """;

    private static BindingsFile Bindings()
    {
        var result = BindingsFile.Parse(BindingsXml);
        Assert.True(result.Success, result.Error);
        return result.File!;
    }

    [Fact]
    public void BuildResponse_MapsEveryFieldOfACuratedBoundEntry()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(CatalogueJson);
        var merge = CatalogueMerger.Merge(catalogue, Bindings());

        var dtos = ActionsEndpoint.BuildResponse(merge);

        var dto = Assert.Single(dtos, d => d.ActionName == "LandingGearToggle");
        Assert.Equal("GEAR", dto.DisplayLabel);
        Assert.Equal("ship", dto.Category);
        Assert.True(dto.IsCurated);
        Assert.True(dto.IsBound);
        Assert.Equal("G", dto.DisplayChord);
        Assert.Null(dto.UnboundReason);
    }

    [Fact]
    public void BuildResponse_UnboundReason_IsSerializedAsItsEnumName_NotANumber()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(CatalogueJson);
        var merge = CatalogueMerger.MergeAll(catalogue, Bindings());

        var dtos = ActionsEndpoint.BuildResponse(merge);

        var dto = Assert.Single(dtos, d => d.ActionName == "UncuratedUnboundAction");
        Assert.False(dto.IsBound);
        Assert.Equal("NoDevice", dto.UnboundReason);
    }

    [Fact]
    public void BuildResponse_PreservesTheInputListsCount_OneDtoPerEntry()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(CatalogueJson);
        var merge = CatalogueMerger.MergeAll(catalogue, Bindings());

        var dtos = ActionsEndpoint.BuildResponse(merge);

        Assert.Equal(merge.Count, dtos.Count);
    }

    [Fact]
    public void BuildResponse_EmptyMerge_ReturnsEmptyList_NotNull()
    {
        var dtos = ActionsEndpoint.BuildResponse(Array.Empty<CataloguePickerEntry>());

        Assert.NotNull(dtos);
        Assert.Empty(dtos);
    }
}
