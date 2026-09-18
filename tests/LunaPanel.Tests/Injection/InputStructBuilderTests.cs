using LunaPanel.Core.Input;
using LunaPanel.Server.Input;

namespace LunaPanel.Tests.Injection;

/// <summary>
/// Pins the exact <see cref="NativeMethods.INPUT"/> <see cref="InputStructBuilder.BuildKeyInput"/>
/// would hand to <c>SendInput</c> - never the send itself, which this pure
/// function never performs. Covers a plain key, an extended key, and both
/// halves (down/up) of each, per <c>ref/docs/input.md</c>'s DIK-vs-wScan
/// trap: <c>wVk</c> must always be 0, <c>wScan</c> must always be the
/// low-byte scan code (never a raw DirectInput value), and
/// <c>KEYEVENTF_EXTENDEDKEY</c> must be present if and only if the key
/// itself says it is extended.
/// </summary>
public class InputStructBuilderTests
{
    private const uint ScanCodeFlag = 0x0008; // KEYEVENTF_SCANCODE
    private const uint ExtendedFlag = 0x0001; // KEYEVENTF_EXTENDEDKEY
    private const uint KeyUpFlag = 0x0002;    // KEYEVENTF_KEYUP

    [Fact]
    public void BuildKeyInput_PlainKeyDown_WVkIsZero_ScanCodeIsLowByte_NoExtendedFlag_NoKeyUpFlag()
    {
        var key = new ScancodeInfo(0x11, false); // Key_W

        var input = InputStructBuilder.BuildKeyInput(key, keyUp: false);

        Assert.Equal(NativeMethods.InputKeyboard, input.type);
        Assert.Equal((ushort)0, input.U.ki.wVk);
        Assert.Equal((ushort)0x11, input.U.ki.wScan);
        Assert.Equal(ScanCodeFlag, input.U.ki.dwFlags);
    }

    [Fact]
    public void BuildKeyInput_PlainKeyUp_AddsKeyUpFlag_Only()
    {
        var key = new ScancodeInfo(0x11, false); // Key_W

        var input = InputStructBuilder.BuildKeyInput(key, keyUp: true);

        Assert.Equal((ushort)0x11, input.U.ki.wScan);
        Assert.Equal(ScanCodeFlag | KeyUpFlag, input.U.ki.dwFlags);
    }

    [Fact]
    public void BuildKeyInput_ExtendedKeyDown_AddsExtendedFlag()
    {
        var key = new ScancodeInfo(0x48, true); // Key_UpArrow

        var input = InputStructBuilder.BuildKeyInput(key, keyUp: false);

        Assert.Equal((ushort)0x48, input.U.ki.wScan);
        Assert.Equal(ScanCodeFlag | ExtendedFlag, input.U.ki.dwFlags);
    }

    [Fact]
    public void BuildKeyInput_ExtendedKeyUp_AddsBothExtendedAndKeyUpFlags()
    {
        var key = new ScancodeInfo(0x48, true); // Key_UpArrow

        var input = InputStructBuilder.BuildKeyInput(key, keyUp: true);

        Assert.Equal((ushort)0x48, input.U.ki.wScan);
        Assert.Equal(ScanCodeFlag | ExtendedFlag | KeyUpFlag, input.U.ki.dwFlags);
    }

    [Fact]
    public void BuildKeyInput_NeverSetsWVk_RegardlessOfScanCode()
    {
        // wVk must stay 0 for every scan-code-based injection - a non-zero
        // wVk would tell SendInput to use virtual-key mode instead, which
        // is not the shape ref/docs/input.md calls for.
        var key = new ScancodeInfo(0x7F, true);

        var input = InputStructBuilder.BuildKeyInput(key, keyUp: false);

        Assert.Equal((ushort)0, input.U.ki.wVk);
    }
}
