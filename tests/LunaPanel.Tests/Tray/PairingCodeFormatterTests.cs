using LunaPanel.Server.Tray;

namespace LunaPanel.Tests.Tray;

/// <summary>Pins <see cref="PairingCodeFormatter"/>'s "123 456" grouping for the tray's "Add a device" window.</summary>
public class PairingCodeFormatterTests
{
    [Fact]
    public void GroupForDisplay_SixDigitCode_InsertsSpaceAfterThirdDigit()
    {
        Assert.Equal("123 456", PairingCodeFormatter.GroupForDisplay("123456"));
    }

    [Fact]
    public void GroupForDisplay_LeadingZeros_ArePreserved()
    {
        Assert.Equal("012 034", PairingCodeFormatter.GroupForDisplay("012034"));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("")]
    [InlineData("12a456")]
    public void GroupForDisplay_NotSixAsciiDigits_ReturnsUnchanged(string input)
    {
        Assert.Equal(input, PairingCodeFormatter.GroupForDisplay(input));
    }
}
