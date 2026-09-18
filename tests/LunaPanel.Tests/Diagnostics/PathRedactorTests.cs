using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Tests.Diagnostics;

/// <summary>
/// Proves <see cref="PathRedactor"/> rewrites configured roots without ever
/// discovering them itself - the roots always arrive via the constructor.
/// </summary>
public class PathRedactorTests
{
    [Fact]
    public void Redact_RootAtStartOfPath_IsReplaced()
    {
        var redactor = new PathRedactor(new[] { (@"C:\Users\Owner\AppData\Local", "%LOCALAPPDATA%") });

        var result = redactor.Redact(@"C:\Users\Owner\AppData\Local\LunaPanel\log.txt");

        Assert.Equal(@"%LOCALAPPDATA%\LunaPanel\log.txt", result);
    }

    [Fact]
    public void Redact_IsCaseInsensitive()
    {
        var redactor = new PathRedactor(new[] { (@"C:\Users\Owner\AppData\Local", "%LOCALAPPDATA%") });

        var result = redactor.Redact(@"c:\users\owner\appdata\local\LunaPanel\log.txt");

        Assert.Equal(@"%LOCALAPPDATA%\LunaPanel\log.txt", result);
    }

    [Fact]
    public void Redact_RootEmbeddedMidString_IsReplaced()
    {
        var redactor = new PathRedactor(new[] { (@"C:\Users\Owner\AppData\Local", "%LOCALAPPDATA%") });

        var result = redactor.Redact(@"Loaded config from C:\Users\Owner\AppData\Local\LunaPanel\config.json successfully");

        Assert.Equal(@"Loaded config from %LOCALAPPDATA%\LunaPanel\config.json successfully", result);
    }

    [Fact]
    public void Redact_NestedRoots_LongestWins()
    {
        var redactor = new PathRedactor(new[]
        {
            (@"C:\Users\Owner", "%USERPROFILE%"),
            (@"C:\Users\Owner\AppData\Local", "%LOCALAPPDATA%")
        });

        var result = redactor.Redact(@"C:\Users\Owner\AppData\Local\LunaPanel\log.txt");

        // The longer, nested LocalAppData root must win here, not the
        // shorter profile root it sits inside.
        Assert.Equal(@"%LOCALAPPDATA%\LunaPanel\log.txt", result);
    }

    [Fact]
    public void Redact_NestedRoots_ShorterRootStillMatchesOutsideTheNestedPortion()
    {
        var redactor = new PathRedactor(new[]
        {
            (@"C:\Users\Owner", "%USERPROFILE%"),
            (@"C:\Users\Owner\AppData\Local", "%LOCALAPPDATA%")
        });

        var result = redactor.Redact(@"C:\Users\Owner\Documents\notes.txt");

        Assert.Equal(@"%USERPROFILE%\Documents\notes.txt", result);
    }

    [Fact]
    public void Redact_NoMatch_ReturnsInputUnchanged()
    {
        var redactor = new PathRedactor(new[] { (@"C:\Users\Owner\AppData\Local", "%LOCALAPPDATA%") });

        var result = redactor.Redact(@"D:\Games\EliteDangerous\bindings.binds");

        Assert.Equal(@"D:\Games\EliteDangerous\bindings.binds", result);
    }

    [Fact]
    public void Redact_EmptyString_ReturnsEmptyString()
    {
        var redactor = new PathRedactor(new[] { (@"C:\Users\Owner\AppData\Local", "%LOCALAPPDATA%") });

        Assert.Equal(string.Empty, redactor.Redact(string.Empty));
    }
}
