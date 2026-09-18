using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LunaPanel.Server.Input;

/// <summary>
/// Raw Win32 P/Invoke declarations for keyboard injection and process
/// integrity inspection. Nothing here decides anything - see
/// <see cref="InputStructBuilder"/> for the (pure, portable) logic that
/// shapes an <see cref="INPUT"/>, and <see cref="InjectionGuard"/> for the
/// (pure, portable) logic that decides whether to send one at all.
///
/// Struct/constant definitions here carry no platform attribute - they are
/// plain data shapes with no OS dependency of their own, so code that only
/// builds them (<see cref="InputStructBuilder"/>) stays fully testable on
/// any OS. Only the actual <c>DllImport</c> methods are marked
/// <see cref="SupportedOSPlatformAttribute"/> - those are the only members
/// that touch a real Windows API.
/// </summary>
public static class NativeMethods
{
    public const uint InputKeyboard = 1;

    public const uint KeyEventFExtendedKey = 0x0001;
    public const uint KeyEventFKeyUp = 0x0002;
    public const uint KeyEventFScanCode = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    /// <summary>
    /// Mouse and hardware events, which this project never sends. They are
    /// declared anyway because <c>INPUT</c>'s union is sized by its largest
    /// member: omitting <c>MOUSEINPUT</c> shrinks the union from its correct 32
    /// bytes to <c>KEYBDINPUT</c>'s 24, <c>Marshal.SizeOf&lt;INPUT&gt;()</c> then
    /// reports 32 instead of 40, and <c>SendInput</c> rejects every call with
    /// <c>ERROR_INVALID_PARAMETER</c> (87) while injecting nothing. Deleting
    /// these as "unused" reintroduces that bug - see
    /// <c>NativeInputLayoutTests</c>, which pins the size against exactly this.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    /// <inheritdoc cref="MOUSEINPUT"/>
    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    // --- Keyboard injection ---

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // --- Foreground window / owning process ---

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // --- Process integrity level (Mandatory Integrity Control) ---

    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const uint TokenQuery = 0x0008;
    public const uint TokenIntegrityLevel = 25;
    public const int SidRevision = 1;

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [SupportedOSPlatform("windows")]
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [SupportedOSPlatform("windows")]
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetTokenInformation(
        IntPtr TokenHandle,
        uint TokenInformationClass,
        IntPtr TokenInformation,
        uint TokenInformationLength,
        out uint ReturnLength);

    [SupportedOSPlatform("windows")]
    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern IntPtr GetSidSubAuthorityCount(IntPtr pSid);

    [SupportedOSPlatform("windows")]
    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern IntPtr GetSidSubAuthority(IntPtr pSid, uint nSubAuthority);
}
