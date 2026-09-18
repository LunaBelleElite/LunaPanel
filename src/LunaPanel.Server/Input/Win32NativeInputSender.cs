using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LunaPanel.Server.Input;

/// <summary>
/// The literal <c>SendInput</c> call - and nothing else. This is the
/// smallest possible seam between "decided to inject" and "asked Windows to
/// inject": <see cref="Win32KeyInjector"/> takes this as an injected
/// delegate (<see cref="Send"/>'s method group), so a test can supply a
/// recording delegate instead and exercise every other line of
/// <see cref="Win32KeyInjector"/> - the guard, the logging, the struct
/// building - without this method ever running. Nothing here is testable on
/// its own; it is a one-line wrapper by design.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Win32NativeInputSender
{
    /// <returns>
    /// The number of events <c>SendInput</c> reports as successfully
    /// inserted, and the Win32 error code from <c>GetLastError</c> when that
    /// count is zero (0 otherwise). A zero count with a real error code is a
    /// genuine, API-reported failure - distinct from UIPI, which reports
    /// success and sets no error at all (see <c>ref/docs/injection.md</c>).
    /// </returns>
    public static (uint Inserted, int Win32Error) Send(NativeMethods.INPUT[] inputs)
    {
        var inserted = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        var error = inserted == 0 ? Marshal.GetLastWin32Error() : 0;
        return (inserted, error);
    }
}
