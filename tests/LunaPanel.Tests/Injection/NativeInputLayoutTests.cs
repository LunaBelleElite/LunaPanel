using System.Runtime.InteropServices;
using LunaPanel.Server.Input;

namespace LunaPanel.Tests.Injection;

/// <summary>
/// Pins the binary layout of <see cref="NativeMethods.INPUT"/> against the
/// Win32 ABI, because <c>Win32NativeInputSender</c> passes
/// <c>Marshal.SizeOf&lt;INPUT&gt;()</c> as <c>SendInput</c>'s <c>cbSize</c> and
/// Windows rejects the whole call with <c>ERROR_INVALID_PARAMETER</c> (87) if
/// that number is not exactly right.
///
/// This is a regression pin for a real, live-verified defect: the union
/// originally declared only <c>KEYBDINPUT</c>, so it inherited that struct's
/// 24-byte size and <c>INPUT</c> measured 32 rather than 40. Every unit test
/// passed - the struct builder's field values were all correct - and the
/// first real <c>SendInput</c> call against Elite Dangerous failed with
/// error 87 and injected nothing. In C the union is sized by its *largest*
/// member, <c>MOUSEINPUT</c>, whether or not this project ever sends a mouse
/// event; declaring only the member we use silently shrinks it.
///
/// See <c>tests/notes/live-checks.md</c> (LC2) for the run that caught it.
/// These are pure size/offset assertions, so they run on any OS - no
/// P/Invoke, no injection.
/// </summary>
public class NativeInputLayoutTests
{
    /// <summary>
    /// 40 bytes on 64-bit, 28 on 32-bit. Both are the documented, widely
    /// pinned values for <c>sizeof(INPUT)</c>; the difference is entirely
    /// <c>ULONG_PTR dwExtraInfo</c> plus the alignment padding it forces.
    /// Written as a branch on pointer size rather than a single constant so
    /// the test states the ABI rather than this machine's architecture.
    /// </summary>
    [Fact]
    public void Input_MarshalSize_MatchesWin32Abi()
    {
        var expected = IntPtr.Size == 8 ? 40 : 28;

        Assert.Equal(expected, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    /// <summary>
    /// The union must be sized by <c>MOUSEINPUT</c>, the largest of the three
    /// members, not by whichever member this project happens to use. This is
    /// the specific mistake that produced the wrong <c>cbSize</c>, so it is
    /// pinned directly rather than only implied by the total above.
    /// </summary>
    [Fact]
    public void InputUnion_IsSizedByItsLargestMember()
    {
        var union = Marshal.SizeOf<NativeMethods.InputUnion>();

        Assert.Equal(Marshal.SizeOf<NativeMethods.MOUSEINPUT>(), union);
        Assert.True(
            union > Marshal.SizeOf<NativeMethods.KEYBDINPUT>(),
            $"The union ({union}) must be larger than KEYBDINPUT alone " +
            $"({Marshal.SizeOf<NativeMethods.KEYBDINPUT>()}) - if it is not, the union " +
            "is missing its larger members and cbSize will be too small.");
    }

    /// <summary>
    /// Field offsets within <c>KEYBDINPUT</c>, so a future edit cannot
    /// reorder or resize a field and still satisfy the total-size pin above
    /// by coincidence.
    /// </summary>
    [Fact]
    public void KeybdInput_FieldOffsets_MatchWin32Abi()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<NativeMethods.KEYBDINPUT>(nameof(NativeMethods.KEYBDINPUT.wVk)));
        Assert.Equal(2, (int)Marshal.OffsetOf<NativeMethods.KEYBDINPUT>(nameof(NativeMethods.KEYBDINPUT.wScan)));
        Assert.Equal(4, (int)Marshal.OffsetOf<NativeMethods.KEYBDINPUT>(nameof(NativeMethods.KEYBDINPUT.dwFlags)));
        Assert.Equal(8, (int)Marshal.OffsetOf<NativeMethods.KEYBDINPUT>(nameof(NativeMethods.KEYBDINPUT.time)));
        Assert.Equal(
            IntPtr.Size == 8 ? 16 : 12,
            (int)Marshal.OffsetOf<NativeMethods.KEYBDINPUT>(nameof(NativeMethods.KEYBDINPUT.dwExtraInfo)));
    }

    /// <summary>
    /// The union starts after <c>type</c> plus its alignment padding, which
    /// is 8 on 64-bit (not 4) because the union contains a pointer-sized
    /// field. Getting this wrong is the other way to arrive at a
    /// wrong-but-plausible total.
    /// </summary>
    [Fact]
    public void Input_UnionOffset_AccountsForAlignmentPadding()
    {
        Assert.Equal(
            IntPtr.Size == 8 ? 8 : 4,
            (int)Marshal.OffsetOf<NativeMethods.INPUT>(nameof(NativeMethods.INPUT.U)));
    }
}
