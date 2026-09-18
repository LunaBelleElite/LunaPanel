using LunaPanel.Core.Bindings;
using LunaPanel.Server.Discovery;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Pins <see cref="BindingsStatusEndpoint"/>'s pure logic - the settings
/// gear's Bindings pane data (<c>ref/docs/bindings-source.md</c>'s "It has
/// to be visible") - independent of any real file or ASP.NET type.
/// </summary>
public class BindingsStatusEndpointTests
{
    private static readonly BindingElement BoundElement = new(
        "LandingGearToggle",
        new BindingSlot("Keyboard", "Key_G", Array.Empty<BoundKey>()),
        null);

    private static readonly BindingElement UnboundElement = new(
        "ToggleCargoScoop",
        new BindingSlot("{NoDevice}", "", Array.Empty<BoundKey>()),
        null);

    private static BindingsFile FileWith(string? presetName, params BindingElement[] elements) =>
        new(presetName, 4, 2, elements);

    [Fact]
    public void SourceKind_NothingFound_ReportsNotFound()
    {
        var selection = new PresetSelectionResult(null, null, PresetSelectionMethod.Fallback, null, null, false, null);

        Assert.Equal("NotFound", BindingsStatusEndpoint.SourceKind(selection));
    }

    [Fact]
    public void SourceKind_Fallback_ReportsFallback_EvenThoughOriginIsCommanderAuthored()
    {
        var selection = new PresetSelectionResult(
            @"C:\fake\Custom.4.2.binds", null, PresetSelectionMethod.Fallback, PresetOrigin.CommanderAuthored, new BindsVersion(4, 2), false, null);

        Assert.Equal("Fallback", BindingsStatusEndpoint.SourceKind(selection));
    }

    [Fact]
    public void SourceKind_CommanderAuthored_ReportsCommanderAuthored()
    {
        var selection = new PresetSelectionResult(
            @"C:\fake\Custom.4.2.binds", "Custom", PresetSelectionMethod.CommanderAuthored, PresetOrigin.CommanderAuthored, new BindsVersion(4, 2), false, "Custom");

        Assert.Equal("CommanderAuthored", BindingsStatusEndpoint.SourceKind(selection));
    }

    [Fact]
    public void SourceKind_Stock_ReportsStock()
    {
        var selection = new PresetSelectionResult(
            @"C:\fake\ControlSchemes\KeyboardMouseOnly.binds", "KeyboardMouseOnly", PresetSelectionMethod.Stock, PresetOrigin.Stock, null, false, "KeyboardMouseOnly");

        Assert.Equal("Stock", BindingsStatusEndpoint.SourceKind(selection));
    }

    [Fact]
    public void BuildResponse_CountsOnlyResolvedActionsAsBound()
    {
        var selection = new PresetSelectionResult(
            @"C:\fake\Custom.4.2.binds", "Custom", PresetSelectionMethod.CommanderAuthored, PresetOrigin.CommanderAuthored, new BindsVersion(4, 2), false, "Custom");
        var file = FileWith("Custom", BoundElement, UnboundElement);

        var response = BindingsStatusEndpoint.BuildResponse(selection, file);

        Assert.Equal(1, response.BoundActionCount);
        Assert.Equal(2, response.TotalActionCount);
        Assert.Equal("4.2", response.Version);
        Assert.Equal("Custom", response.PresetName);
        Assert.False(response.PresetNameMismatch);
    }

    [Fact]
    public void BuildResponse_StockFile_VersionIsNull()
    {
        var selection = new PresetSelectionResult(
            @"C:\fake\ControlSchemes\KeyboardMouseOnly.binds", "KeyboardMouseOnly", PresetSelectionMethod.Stock, PresetOrigin.Stock, null, false, "KeyboardMouseOnly");
        var file = FileWith("KeyboardMouseOnly", BoundElement);

        var response = BindingsStatusEndpoint.BuildResponse(selection, file);

        Assert.Null(response.Version);
    }

    [Fact]
    public void BuildResponse_PresetNameMismatch_IsCarriedThrough()
    {
        var selection = new PresetSelectionResult(
            @"C:\fake\Custom.4.2.binds", "Custom", PresetSelectionMethod.CommanderAuthored, PresetOrigin.CommanderAuthored, new BindsVersion(4, 2), true, "SomethingElse");
        var file = FileWith("SomethingElse", BoundElement);

        var response = BindingsStatusEndpoint.BuildResponse(selection, file);

        Assert.True(response.PresetNameMismatch);
        Assert.Equal("SomethingElse", response.PresetName);
    }

    [Fact]
    public void BuildResponse_NothingFound_PresetNameIsNull_ZeroCounts()
    {
        var selection = new PresetSelectionResult(null, null, PresetSelectionMethod.Fallback, null, null, false, null);
        var file = new BindingsFile(null, null, null, Array.Empty<BindingElement>());

        var response = BindingsStatusEndpoint.BuildResponse(selection, file);

        Assert.Null(response.PresetName);
        Assert.Equal(0, response.BoundActionCount);
        Assert.Equal(0, response.TotalActionCount);
        Assert.Equal("NotFound", response.SourceKind);
    }
}
