using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LunaPanel.Server.Input;

/// <summary>
/// Builds a <see cref="ForegroundContext"/> from the real Windows
/// environment: which window is foreground, what process owns it, and both
/// that process's and our own process's Mandatory Integrity Control level.
/// Every failure degrades to a <c>null</c> field rather than throwing -
/// <see cref="InjectionGuard"/> treats a <c>null</c> as "could not be
/// determined", never as a mismatch. Not directly unit-tested (it is a thin
/// wrapper over live OS state); <see cref="InjectionGuard"/> is what carries
/// the actual test coverage, driven by hand-built <see cref="ForegroundContext"/>
/// values.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Win32ForegroundInspector
{
    public static ForegroundContext Capture()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return new ForegroundContext(null, null, null);
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var foregroundPid);
        if (foregroundPid == 0)
        {
            return new ForegroundContext(null, null, null);
        }

        string? processName = null;
        try
        {
            using var process = Process.GetProcessById((int)foregroundPid);
            processName = process.ProcessName;
        }
        catch (ArgumentException)
        {
            // No process with that id - it exited between GetForegroundWindow
            // and here. Report as undetermined, not as a crash.
        }

        var foregroundIntegrity = TryGetIntegrityLevel(foregroundPid);
        var ourIntegrity = TryGetIntegrityLevel((uint)Environment.ProcessId);

        return new ForegroundContext(processName, foregroundIntegrity, ourIntegrity);
    }

    private static IntegrityLevel? TryGetIntegrityLevel(uint processId)
    {
        var processHandle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (processHandle == IntPtr.Zero)
        {
            return null;
        }

        var tokenHandle = IntPtr.Zero;
        var tokenInfo = IntPtr.Zero;
        try
        {
            if (!NativeMethods.OpenProcessToken(processHandle, NativeMethods.TokenQuery, out tokenHandle))
            {
                return null;
            }

            // First call with no buffer to learn the required size.
            NativeMethods.GetTokenInformation(tokenHandle, NativeMethods.TokenIntegrityLevel, IntPtr.Zero, 0, out var length);
            if (length == 0)
            {
                return null;
            }

            tokenInfo = Marshal.AllocHGlobal((int)length);
            if (!NativeMethods.GetTokenInformation(tokenHandle, NativeMethods.TokenIntegrityLevel, tokenInfo, length, out _))
            {
                return null;
            }

            // TOKEN_MANDATORY_LABEL is { SID_AND_ATTRIBUTES Label }, and
            // SID_AND_ATTRIBUTES starts with the SID pointer at offset 0.
            var sid = Marshal.ReadIntPtr(tokenInfo, 0);
            var subAuthorityCountPtr = NativeMethods.GetSidSubAuthorityCount(sid);
            var subAuthorityCount = Marshal.ReadByte(subAuthorityCountPtr);
            if (subAuthorityCount == 0)
            {
                return null;
            }

            var ridPtr = NativeMethods.GetSidSubAuthority(sid, (uint)(subAuthorityCount - 1));
            var rid = unchecked((uint)Marshal.ReadInt32(ridPtr));

            return RidToIntegrityLevel(rid);
        }
        finally
        {
            if (tokenInfo != IntPtr.Zero) Marshal.FreeHGlobal(tokenInfo);
            if (tokenHandle != IntPtr.Zero) NativeMethods.CloseHandle(tokenHandle);
            NativeMethods.CloseHandle(processHandle);
        }
    }

    // SECURITY_MANDATORY_*_RID constants (winnt.h), compared as ranges since
    // real-world SIDs can carry values between the named boundaries.
    private static IntegrityLevel RidToIntegrityLevel(uint rid) => rid switch
    {
        < 0x1000 => IntegrityLevel.Untrusted,
        < 0x2000 => IntegrityLevel.Low,
        < 0x2100 => IntegrityLevel.Medium,
        < 0x3000 => IntegrityLevel.MediumPlus,
        < 0x4000 => IntegrityLevel.High,
        < 0x5000 => IntegrityLevel.System,
        _ => IntegrityLevel.Protected,
    };
}
