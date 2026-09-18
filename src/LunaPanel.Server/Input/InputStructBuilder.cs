using LunaPanel.Core.Input;

namespace LunaPanel.Server.Input;

/// <summary>
/// Shapes one <see cref="NativeMethods.INPUT"/> for a keyboard event. Pure
/// data transformation, no P/Invoke, no OS dependency - safe to call and
/// assert against on any platform, which is exactly what lets a test pin
/// the exact structure a real <c>SendInput</c> call would receive without
/// ever making that call. See <c>ref/docs/input.md</c> for why <see cref="ScancodeInfo"/>
/// already carries the low-byte scan code and the extended flag split
/// apart, rather than a raw DirectInput value.
/// </summary>
public static class InputStructBuilder
{
    /// <summary>
    /// Builds the <see cref="NativeMethods.INPUT"/> for one key transition:
    /// <c>wVk = 0</c>, <c>wScan</c> = the scan code's low byte,
    /// <c>KEYEVENTF_SCANCODE</c> always set, <c>KEYEVENTF_EXTENDEDKEY</c>
    /// added only when <paramref name="key"/> says so, and
    /// <c>KEYEVENTF_KEYUP</c> added only when <paramref name="keyUp"/> is
    /// true.
    /// </summary>
    public static NativeMethods.INPUT BuildKeyInput(ScancodeInfo key, bool keyUp)
    {
        var flags = NativeMethods.KeyEventFScanCode;

        if (key.IsExtended)
        {
            flags |= NativeMethods.KeyEventFExtendedKey;
        }

        if (keyUp)
        {
            flags |= NativeMethods.KeyEventFKeyUp;
        }

        return new NativeMethods.INPUT
        {
            type = NativeMethods.InputKeyboard,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = key.ScanCode,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };
    }
}
