using LunaPanel.Server.Input;

namespace LunaPanel.Tests.Injection;

/// <summary>
/// Drives <see cref="InjectionGuard.Evaluate"/> directly against hand-built
/// <see cref="ForegroundContext"/> values - no P/Invoke, no OS access, and
/// therefore no risk of ever sending a real keystroke. This is the "decide"
/// half of the Win32 key injector; see <c>InputStructBuilderTests</c> for
/// the "what to send" half and <c>Win32KeyInjectorTests</c> for both wired
/// together (with the actual send faked).
/// </summary>
public class InjectionGuardTests
{
    private static ForegroundContext EliteForeground(IntegrityLevel? ours = IntegrityLevel.Medium, IntegrityLevel? theirs = IntegrityLevel.Medium) =>
        new("EliteDangerous64", theirs, ours);

    [Fact]
    public void Evaluate_EliteForeground_EqualIntegrity_ReturnsSent()
    {
        var result = InjectionGuard.Evaluate(EliteForeground());

        Assert.Equal(InjectionOutcome.Sent, result.Outcome);
    }

    [Fact]
    public void Evaluate_EliteForeground_32BitExe_ReturnsSent()
    {
        var context = new ForegroundContext("EliteDangerous32", IntegrityLevel.Medium, IntegrityLevel.Medium);

        var result = InjectionGuard.Evaluate(context);

        Assert.Equal(InjectionOutcome.Sent, result.Outcome);
    }

    [Fact]
    public void Evaluate_ProcessNameMatch_IsCaseInsensitive()
    {
        var context = new ForegroundContext("elitedangerous64", IntegrityLevel.Medium, IntegrityLevel.Medium);

        var result = InjectionGuard.Evaluate(context);

        Assert.Equal(InjectionOutcome.Sent, result.Outcome);
    }

    [Fact]
    public void Evaluate_ForegroundIsSomeOtherProcess_ReturnsGameNotForeground()
    {
        var context = new ForegroundContext("Discord", IntegrityLevel.Medium, IntegrityLevel.Medium);

        var result = InjectionGuard.Evaluate(context);

        Assert.Equal(InjectionOutcome.GameNotForeground, result.Outcome);
        Assert.Contains("not the active window", result.Reason);
    }

    [Fact]
    public void Evaluate_NoForegroundWindowAtAll_ReturnsGameNotForeground()
    {
        var context = new ForegroundContext(null, null, IntegrityLevel.Medium);

        var result = InjectionGuard.Evaluate(context);

        Assert.Equal(InjectionOutcome.GameNotForeground, result.Outcome);
    }

    [Fact]
    public void Evaluate_EliteForeground_OurIntegrityLower_ReturnsUipiSuspected_NotGameNotForeground()
    {
        // Contrast pin: the foreground process name DOES match Elite here -
        // this must be distinguished from GameNotForeground, since the
        // advice a player needs ("run both at the same privilege level") is
        // completely different from "click on the game".
        var context = new ForegroundContext("EliteDangerous64", IntegrityLevel.High, IntegrityLevel.Medium);

        var result = InjectionGuard.Evaluate(context);

        Assert.Equal(InjectionOutcome.UipiSuspected, result.Outcome);
        Assert.Contains("privilege level", result.Reason);
    }

    [Fact]
    public void Evaluate_EliteForeground_OurIntegrityHigher_ReturnsSent()
    {
        var context = new ForegroundContext("EliteDangerous64", IntegrityLevel.Medium, IntegrityLevel.High);

        var result = InjectionGuard.Evaluate(context);

        Assert.Equal(InjectionOutcome.Sent, result.Outcome);
    }

    [Theory]
    [InlineData(null, IntegrityLevel.High)]
    [InlineData(IntegrityLevel.Medium, null)]
    [InlineData(null, null)]
    public void Evaluate_EliteForeground_EitherIntegrityUnknown_SkipsComparison_ReturnsSent(
        IntegrityLevel? ours, IntegrityLevel? theirs)
    {
        // An undetermined integrity level (OpenProcessToken denied, etc.)
        // means the check itself failed - it must not be treated as
        // evidence of a mismatch, or a harmless permissions hiccup would
        // block every injection.
        var context = new ForegroundContext("EliteDangerous64", theirs, ours);

        var result = InjectionGuard.Evaluate(context);

        Assert.Equal(InjectionOutcome.Sent, result.Outcome);
    }
}
