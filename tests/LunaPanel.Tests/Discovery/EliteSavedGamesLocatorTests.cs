using LunaPanel.Server.Discovery;

namespace LunaPanel.Tests.Discovery;

/// <summary>
/// Pure-function pins for <see cref="EliteSavedGamesLocator.ResolveStatusDirectory"/>.
/// No file system is touched; every path below is a made-up string.
/// </summary>
public class EliteSavedGamesLocatorTests
{
    private const string Profile = @"C:\Users\Someone";
    private const string Fallback = @"C:\Users\Someone\Saved Games\Frontier Developments\Elite Dangerous";

    [Fact]
    public void RelocatedToAnotherDrive_UsesTheRelocatedRoot()
    {
        var result = EliteSavedGamesLocator.ResolveStatusDirectory(Profile, () => @"D:\Games\Saved Games");

        Assert.Equal(@"D:\Games\Saved Games\Frontier Developments\Elite Dangerous", result);
    }

    [Theory]
    [InlineData(@"D:\My Saved Games", @"D:\My Saved Games\Frontier Developments\Elite Dangerous")]
    [InlineData(@"D:\O'Brien & Co (2)\Saved Games", @"D:\O'Brien & Co (2)\Saved Games\Frontier Developments\Elite Dangerous")]
    [InlineData(@"D:\100% Games\Saved", @"D:\100% Games\Saved\Frontier Developments\Elite Dangerous")]
    [InlineData(@"D:\#1 Folder\Saved", @"D:\#1 Folder\Saved\Frontier Developments\Elite Dangerous")]
    [InlineData(@"D:\me@home\Saved", @"D:\me@home\Saved\Frontier Developments\Elite Dangerous")]
    [InlineData(@"D:\A+B=C;D\Saved", @"D:\A+B=C;D\Saved\Frontier Developments\Elite Dangerous")]
    [InlineData(@"D:\[x]{y}~z!\Saved", @"D:\[x]{y}~z!\Saved\Frontier Developments\Elite Dangerous")]
    [InlineData("D:\\$cash^up`tick\\Saved", "D:\\$cash^up`tick\\Saved\\Frontier Developments\\Elite Dangerous")]
    [InlineData(@"D:\v1.2.3\my.saved.games", @"D:\v1.2.3\my.saved.games\Frontier Developments\Elite Dangerous")]
    [InlineData("D:\\Caf\u00e9 \u00c9l\u00e8ve\\Saved", "D:\\Caf\u00e9 \u00c9l\u00e8ve\\Saved\\Frontier Developments\\Elite Dangerous")]
    [InlineData("D:\\\u0418\u0433\u0440\u044b\\Saved", "D:\\\u0418\u0433\u0440\u044b\\Saved\\Frontier Developments\\Elite Dangerous")]
    [InlineData("D:\\\u30b2\u30fc\u30e0\u4fdd\u5b58\\Saved", "D:\\\u30b2\u30fc\u30e0\u4fdd\u5b58\\Saved\\Frontier Developments\\Elite Dangerous")]
    public void RelocatedRootWithLegalOddCharacters_IsUsedVerbatim(string root, string expected)
    {
        Assert.Equal(expected, EliteSavedGamesLocator.ResolveStatusDirectory(Profile, () => root));
    }

    [Fact]
    public void RelocatedRootWithTrailingBackslash_DoesNotDoubleTheSeparator()
    {
        var result = EliteSavedGamesLocator.ResolveStatusDirectory(Profile, () => @"D:\Saved\");

        Assert.Equal(@"D:\Saved\Frontier Developments\Elite Dangerous", result);
    }

    [Fact]
    public void RelocatedToAUncShare_IsUsed()
    {
        var result = EliteSavedGamesLocator.ResolveStatusDirectory(Profile, () => @"\\server\share\Saved Games");

        Assert.Equal(@"\\server\share\Saved Games\Frontier Developments\Elite Dangerous", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"Saved Games")]
    [InlineData(@"..\Saved Games")]
    [InlineData(@"\Saved Games")]
    [InlineData("C:foo")]
    [InlineData("D:\\Sav\0ed")]
    public void UnusableLookupValue_FallsBackToTheProfilePath(string? lookupValue)
    {
        var result = EliteSavedGamesLocator.ResolveStatusDirectory(Profile, () => lookupValue);

        Assert.Equal(Fallback, result);
    }

    [Fact]
    public void LookupThatThrows_FallsBackToTheProfilePath()
    {
        var result = EliteSavedGamesLocator.ResolveStatusDirectory(
            Profile, () => throw new InvalidOperationException("boom"));

        Assert.Equal(Fallback, result);
    }

    [Fact]
    public void NullLookup_FallsBackToTheProfilePath()
    {
        Assert.Equal(Fallback, EliteSavedGamesLocator.ResolveStatusDirectory(Profile, null));
    }

    [Fact]
    public void Fallback_WithAnUnusualProfilePath_StillJoinsCorrectly()
    {
        var result = EliteSavedGamesLocator.ResolveStatusDirectory(@"C:\Users\O'Brien & Co (2)", () => null);

        Assert.Equal(
            @"C:\Users\O'Brien & Co (2)\Saved Games\Frontier Developments\Elite Dangerous", result);
    }
}
