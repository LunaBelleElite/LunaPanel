using LunaPanel.Core.Bindings;
using LunaPanel.Core.Input;

namespace LunaPanel.Tests.Bindings;

/// <summary>
/// Drives <see cref="KeyBindingLookup.FindActionBoundTo"/> - the "press a
/// key" builder step's reverse lookup (<c>ref/docs/macros.md</c>). Same
/// inline-XML convention as <see cref="BindResolverTests"/>.
/// </summary>
public class KeyBindingLookupTests
{
    private static BindingsFile ParseXml(string xml)
    {
        var result = BindingsFile.Parse(xml);
        Assert.True(result.Success, result.Error);
        return result.File!;
    }

    [Fact]
    public void FindActionBoundTo_KeyMatchesABareBoundElement_ReturnsItsName()
    {
        var file = ParseXml("""
            <Root MajorVersion="4" MinorVersion="2">
                <LandingGearToggle><Primary Device="Keyboard" Key="Key_F5" /></LandingGearToggle>
            </Root>
            """);

        var matched = KeyBindingLookup.FindActionBoundTo(new ScancodeInfo(0x3F, false), file);

        Assert.Equal("LandingGearToggle", matched);
    }

    /// <summary>
    /// Scope decision (this class's own remarks): a control bound to
    /// <c>Ctrl+F5</c> is not "the same key" as a bare <c>F5</c> press -
    /// substituting that action name into a real <c>press</c> step would
    /// send a completely different chord than the raw key the commander
    /// actually picked, so a modifier on the bound side disqualifies the
    /// match even though the main key is identical.
    /// </summary>
    [Fact]
    public void FindActionBoundTo_KeyMatchesButTheBoundElementAlsoNeedsAModifier_DoesNotMatch()
    {
        var file = ParseXml("""
            <Root MajorVersion="4" MinorVersion="2">
                <ChordedAction><Primary Device="Keyboard" Key="Key_F5">
                    <Modifier Device="Keyboard" Key="Key_LeftControl" />
                </Primary></ChordedAction>
            </Root>
            """);

        var matched = KeyBindingLookup.FindActionBoundTo(new ScancodeInfo(0x3F, false), file);

        Assert.Null(matched);
    }

    [Fact]
    public void FindActionBoundTo_NothingBoundToThatKey_ReturnsNull()
    {
        var file = ParseXml("""
            <Root MajorVersion="4" MinorVersion="2">
                <SomeOtherAction><Primary Device="Keyboard" Key="Key_W" /></SomeOtherAction>
            </Root>
            """);

        var matched = KeyBindingLookup.FindActionBoundTo(new ScancodeInfo(0x3F, false), file);

        Assert.Null(matched);
    }

    [Fact]
    public void FindActionBoundTo_ElementUnbound_NoDevice_ReturnsNull()
    {
        var file = ParseXml("""
            <Root MajorVersion="4" MinorVersion="2">
                <Unbound><Primary Device="{NoDevice}" Key="" /></Unbound>
            </Root>
            """);

        var matched = KeyBindingLookup.FindActionBoundTo(new ScancodeInfo(0x3F, false), file);

        Assert.Null(matched);
    }
}
