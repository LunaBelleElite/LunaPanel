using LunaPanel.Server.Discovery;

namespace LunaPanel.Tests.Discovery;

public class PlaceholderExpanderTests
{
    [Fact]
    public void Expand_KnownPlaceholder_IsReplaced()
    {
        var result = PlaceholderExpander.Expand(
            @"%USERPROFILE%\EDHM_UI",
            new Dictionary<string, string> { ["USERPROFILE"] = @"C:\Users\Test" });

        Assert.Equal(@"C:\Users\Test\EDHM_UI", result);
    }

    [Fact]
    public void Expand_UnknownPlaceholder_IsLeftVerbatim()
    {
        var result = PlaceholderExpander.Expand(@"%SOMETHING_UNKNOWN%\EDHM_UI", new Dictionary<string, string>());
        Assert.Equal(@"%SOMETHING_UNKNOWN%\EDHM_UI", result);
    }

    [Fact]
    public void Expand_NoPlaceholders_ReturnsInputUnchanged()
    {
        var result = PlaceholderExpander.Expand(
            @"C:\Users\Test\EDHM_UI",
            new Dictionary<string, string> { ["USERPROFILE"] = @"C:\Users\Test" });

        Assert.Equal(@"C:\Users\Test\EDHM_UI", result);
    }

    [Fact]
    public void Expand_PlaceholderNameIsCaseInsensitive()
    {
        var result = PlaceholderExpander.Expand(
            @"%userprofile%\EDHM_UI",
            new Dictionary<string, string> { ["USERPROFILE"] = @"C:\Users\Test" });

        Assert.Equal(@"C:\Users\Test\EDHM_UI", result);
    }
}
