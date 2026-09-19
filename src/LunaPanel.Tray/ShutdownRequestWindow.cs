using System.Windows.Forms;

namespace LunaPanel.Tray;

/// <summary>
/// A hidden top-level window whose only job is to hear "please exit" from
/// outside the process and act on it.
///
/// <b>Why (2026-09-18).</b> Windows asks a GUI application to close by
/// sending <c>WM_QUERYENDSESSION</c> and then <c>WM_ENDSESSION</c> to its
/// top-level windows. Two things send exactly that pair to LunaPanel:
/// Windows Installer's Restart Manager, at <c>InstallValidate</c> of every
/// self-update (<c>/passive</c> installs shut applications down
/// automatically), and the installer's own close action
/// (<c>installer/Package.wxs</c>, <c>util:CloseApplication</c> with
/// <c>EndSessionMessage="yes"</c>). Measured against the running tray with
/// <c>RmShutdown</c> directly: LunaPanel ignored the request, Restart
/// Manager waited its full 30 s for an exit that never came and gave up
/// (<c>ERROR_FAIL_SHUTDOWN</c>), and every one of 18 instrumented upgrades
/// then fell through to a hard <c>TerminateProcess</c> - which is where
/// the 30 s of "dead time" in a self-update came from, and where the
/// "file is being held in use" stall after the kill came from too. See
/// <c>ref/docs/updates.md</c>, "Closing the running copy: the app now
/// answers".
///
/// Neither the tray icon's own window nor the (possibly hidden) status
/// window turns those messages into an exit: WinForms closes a
/// <c>Form</c> on <c>WM_ENDSESSION</c>, but closing a window is not
/// quitting the app when the message loop belongs to an
/// <see cref="ApplicationContext"/>. This window is the one place that
/// does. It is a real top-level window, not a message-only one, because
/// message-only windows are not enumerated and never receive either
/// message.
///
/// <b>What it does on the request.</b> Answers <c>WM_QUERYENDSESSION</c>
/// with "yes" and, on <c>WM_ENDSESSION</c> with <c>wParam</c> true, invokes
/// the callback - which <c>Program.Run</c> wires to end the message loop
/// and then stop the host, in the same order the Quit menu action uses.
/// A <c>WM_ENDSESSION</c> with <c>wParam</c> false means the shutdown was
/// cancelled and is ignored. The messages are the same ones a real logoff
/// or shutdown sends, so LunaPanel now stops cleanly there as well.
/// </summary>
internal sealed class ShutdownRequestWindow : NativeWindow, IDisposable
{
    private const int WmQueryEndSession = 0x0011;
    private const int WmEndSession = 0x0016;
    private const int WsExToolWindow = 0x00000080;

    private readonly Action _onShutdownRequested;

    public ShutdownRequestWindow(Action onShutdownRequested)
    {
        _onShutdownRequested = onShutdownRequested;

        // Parent left at zero makes this a top-level window; no WS_VISIBLE
        // means it never shows; WS_EX_TOOLWINDOW keeps it out of any window
        // list even so.
        CreateHandle(new CreateParams
        {
            Caption = "LunaPanel",
            ExStyle = WsExToolWindow,
        });
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WmQueryEndSession:
                m.Result = 1;
                return;

            case WmEndSession:
                if (m.WParam != 0)
                {
                    _onShutdownRequested();
                }

                m.Result = 0;
                return;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        DestroyHandle();
    }
}
