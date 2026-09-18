using LunaPanel.Core.Bindings;
using LunaPanel.Core.Input;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Drives <see cref="KeysEndpoint.BuildResponse"/> - the key picker's own
/// list (<c>ref/docs/macros.md</c>'s "press a key").
/// </summary>
public class KeysEndpointTests
{
    [Fact]
    public void BuildResponse_EveryScancodesEntry_IsPresentExactlyOnce()
    {
        var rows = KeysEndpoint.BuildResponse();

        Assert.Equal(Scancodes.All.Count, rows.Count);
        Assert.Equal(
            Scancodes.All.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase),
            rows.Select(r => r.Key));
    }

    [Fact]
    public void BuildResponse_LabelsMatchBindResolversOwnPrettifier_NotASecondHandTypedCopy()
    {
        var rows = KeysEndpoint.BuildResponse();

        var f5 = Assert.Single(rows, r => r.Key == "Key_F5");
        Assert.Equal(BindResolver.PrettifyKeyName("Key_F5"), f5.Label);
        Assert.Equal("F5", f5.Label);
    }
}
