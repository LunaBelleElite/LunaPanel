using LunaPanel.Core.Bindings;
using LunaPanel.Core.Catalogue;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Drives <see cref="ActionForKeyEndpoint.BuildResponse"/> directly - the
/// mapping <c>GET /api/actions/for-key</c> serializes, kept free of any
/// ASP.NET type like every other endpoint type in this namespace.
/// </summary>
public class ActionForKeyEndpointTests
{
    private static BindingsFile Bindings(string xml)
    {
        var result = BindingsFile.Parse(xml);
        Assert.True(result.Success, result.Error);
        return result.File!;
    }

    private static CataloguePickerEntry Entry(string actionName, string displayLabel) =>
        new(actionName, displayLabel, Category: null, IsCurated: true, IsBound: true, DisplayChord: "F5", UnboundReason: null);

    [Fact]
    public void BuildResponse_KeyMatchesABoundControl_ReportsItsNameAndLabel()
    {
        var bindings = Bindings("""
            <Root MajorVersion="4" MinorVersion="2">
                <LandingGearToggle><Primary Device="Keyboard" Key="Key_F5" /></LandingGearToggle>
            </Root>
            """);
        var merge = new[] { Entry("LandingGearToggle", "Landing Gear") };

        var response = ActionForKeyEndpoint.BuildResponse("F5", bindings, merge);

        Assert.NotNull(response);
        Assert.Equal("LandingGearToggle", response!.Matched);
        Assert.Equal("Landing Gear", response.MatchedLabel);
        Assert.Equal("Key_F5", response.ResolvedKey);
        Assert.Equal("F5", response.ResolvedKeyLabel);
    }

    /// <summary>
    /// A matched action absent even from the FULL merge (a name real enough
    /// to be bound in Elite but not present in this build's catalogue at
    /// all) still gets a readable label - <see cref="Prettifier.Prettify"/>
    /// applied to the bare name - rather than a null the client would have
    /// to guess how to display.
    /// </summary>
    [Fact]
    public void BuildResponse_MatchedActionMissingFromMerge_FallsBackToPrettifiedName()
    {
        var bindings = Bindings("""
            <Root MajorVersion="4" MinorVersion="2">
                <SomeObscureAction><Primary Device="Keyboard" Key="Key_F5" /></SomeObscureAction>
            </Root>
            """);

        var response = ActionForKeyEndpoint.BuildResponse("F5", bindings, Array.Empty<CataloguePickerEntry>());

        Assert.NotNull(response);
        Assert.Equal("SomeObscureAction", response!.Matched);
        Assert.Equal(Prettifier.Prettify("SomeObscureAction"), response.MatchedLabel);
    }

    [Fact]
    public void BuildResponse_NothingBoundToThatKey_ReportsNullMatchAndNullLabel()
    {
        var bindings = Bindings("""
            <Root MajorVersion="4" MinorVersion="2">
                <SomeOtherAction><Primary Device="Keyboard" Key="Key_W" /></SomeOtherAction>
            </Root>
            """);

        var response = ActionForKeyEndpoint.BuildResponse("F5", bindings, Array.Empty<CataloguePickerEntry>());

        Assert.NotNull(response);
        Assert.Null(response!.Matched);
        Assert.Null(response.MatchedLabel);
        Assert.Equal("Key_F5", response.ResolvedKey);
        Assert.Equal("F5", response.ResolvedKeyLabel);
    }

    [Fact]
    public void BuildResponse_ResolvedKeyAndLabel_ReflectTheDomCodeArgument_NotTheMatchedAction()
    {
        var bindings = Bindings("""
            <Root MajorVersion="4" MinorVersion="2">
                <SomeOtherAction><Primary Device="Keyboard" Key="Key_W" /></SomeOtherAction>
            </Root>
            """);

        var response = ActionForKeyEndpoint.BuildResponse("KeyW", bindings, Array.Empty<CataloguePickerEntry>());

        Assert.NotNull(response);
        Assert.Equal("SomeOtherAction", response!.Matched);
        Assert.Equal("Key_W", response.ResolvedKey);
        Assert.Equal("W", response.ResolvedKeyLabel);
    }

    [Fact]
    public void BuildResponse_UnrecognizedDomCode_ReturnsNull()
    {
        var bindings = Bindings("""
            <Root MajorVersion="4" MinorVersion="2">
                <SomeOtherAction><Primary Device="Keyboard" Key="Key_W" /></SomeOtherAction>
            </Root>
            """);

        var response = ActionForKeyEndpoint.BuildResponse("ThisIsNotARealDomCode", bindings, Array.Empty<CataloguePickerEntry>());

        Assert.Null(response);
    }
}
