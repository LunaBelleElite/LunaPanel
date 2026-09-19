// LunaPanel.Launcher - the installer's own tiny helper (2026-09-18).
//
// WHAT IT IS. A .NET Framework 4 WinExe of a few kilobytes, compiled by
// scripts/build-installer.sh with the csc.exe that ships inside Windows
// itself (C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe - C# 5,
// so nothing newer than C# 5 syntax may appear in this file). It needs no
// SDK, no NuGet package and no C++ toolchain on the build machine, and no
// runtime on the commander's machine beyond the .NET Framework 4.x that is
// an inbox component of every Windows 10/11 install. It is NOT part of the
// LunaPanel.sln build; it is installer tooling, which is why it lives under
// installer/ rather than src/.
//
// WHY IT EXISTS. Windows Installer's own launch-after-install mechanism
// (the WiX Util extension's Wix4ShellExec) is a bare ShellExecute with no
// wait and no retry. Twice on a real machine that relaunch died within
// 130 ms because a just-written binary was still held open exclusively by
// something else (a scanner, most likely) at the instant the .NET host
// loaded it - see ref/docs/updates.md, "The post-install relaunch race".
// LunaPanel.Tray's own Program.Main recovers from that for every
// DEPENDENCY it loads, but structurally cannot cover its own entry
// assembly (LunaPanel.Tray.dll) or its own apphost (LunaPanel.Tray.exe):
// those loads happen before any of LunaPanel's code runs. Only whatever
// launches the process can cover them, and that is this file.
//
// It is also the installer's answer to a second race: the close action
// terminates the old process, and process teardown outlives
// TerminateProcess by a few milliseconds, so the very next file copy can
// hit "file is being held in use" and stall Windows Installer for ~50 s.
//
// THREE MODES, each handed the installed LunaPanel.Tray.exe path:
//
//   close  <tray.exe>  - run immediately, BEFORE InstallValidate. Asks every
//                        running LunaPanel.Tray.exe to exit the way Windows
//                        does (WM_QUERYENDSESSION then WM_ENDSESSION, with
//                        ENDSESSION_CLOSEAPP, to each of its top-level
//                        windows), waits up to 10 s for it to be gone, and
//                        only then terminates it as a last resort; finally
//                        waits, bounded, for the folder's binaries to be
//                        openable. This is what removes the dead time:
//                        measured 2026-09-18, Windows Installer's own
//                        files-in-use handling at InstallValidate sits ~30 s
//                        whenever the app still holds its files there -
//                        with Restart Manager enabled or disabled, with
//                        MSIRMSHUTDOWN=1 or not, and however promptly the
//                        app answers Restart Manager when asked directly
//                        (99 ms). The one thing that avoids it is the app
//                        not running when validation looks.
//   wait   <tray.exe>  - run deferred, after the close action and before
//                        InstallFiles. Polls, bounded, until every .dll/.exe
//                        in that folder can be opened the way a loader opens
//                        them. On a fresh install the folder does not exist
//                        yet and this returns at once.
//   launch <tray.exe>  - run immediately after InstallFinalize, replacing
//                        Wix4ShellExec. Same wait first, then starts the
//                        tray and WATCHES it for a few seconds: a process
//                        that exits non-zero within that window is a
//                        load-time crash, so it waits again and starts a
//                        fresh one, a bounded number of times. A process
//                        that creates a top-level window, is simply still
//                        running at the end of the window, or exits 0 (the
//                        tray hands over to a fresh copy of itself that way)
//                        counts as launched.
//
// WHY ITS VERSION IS FIXED, AND WHY THAT MUST STAY SO. The MSI installs this
// file next to LunaPanel.Tray.exe with the fixed FileVersion below. Windows
// Installer only overwrites a versioned file when the incoming version is
// higher, so after the first install this file is never written again by an
// upgrade - which is exactly what makes it immune to the "just-written file
// held by a scanner" hold it exists to work around. Bump the version by hand
// if this file's behaviour changes; never wire it to CHANGELOG.md's version.
// scripts/verify-msi-file-versions.ps1 knows this file is the one exception
// to "every LunaPanel.*.exe carries the product version".
//
// Every exit code here is ignored by the installer (Return="ignore" on both
// custom actions in installer/Package.wxs): nothing this file does may fail
// an install, and nothing it does may hang one - every loop is bounded.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

[assembly: AssemblyTitle("LunaPanel installer helper")]
[assembly: AssemblyProduct("LunaPanel")]
[assembly: AssemblyCompany("LunaBelleElite")]
// Version history (bump BOTH by hand whenever this file's behaviour changes):
//   1.0.0.0  first build, CreateProcess launch
//   1.0.0.1  launch through ShellExecuteEx like Wix4ShellExec did (see Launch)
//            - also the version that proved the replace-on-bump mechanism
//            works, by being installed over 1.0.0.0 on 2026-09-18
//   1.0.0.2  CameUp waits for exit when the process is not input-idle; the
//            1.0.0.1 draft mistook a crashed-but-not-yet-reaped process for a
//            slow start and never retried (forced and measured 2026-09-18)
//   1.0.0.3  close mode, run before InstallValidate (see above)
//   1.0.0.4  CameUp decides by exit code or a top-level window, never by
//            WaitForInputIdle, which also called a WER-frozen crash "idle"
[assembly: AssemblyVersion("1.0.0.4")]
[assembly: AssemblyFileVersion("1.0.0.4")]

namespace LunaPanel.Launcher
{
    internal static class Program
    {
        /// <summary>The most a wait for the folder's binaries to be openable lasts.</summary>
        private const int WaitForFilesSeconds = 10;

        /// <summary>How many times launch mode starts the tray before giving up.</summary>
        private const int LaunchAttempts = 3;

        /// <summary>
        /// How long a freshly started tray is watched for a load-time crash.
        /// Both real crashes FAULTED within 130 ms of process start, but the
        /// process does not EXIT until Windows Error Reporting has finished
        /// with it: measured 2026-09-18, a tray whose entry assembly is held
        /// exits 0xE0434352 about 1.3-1.9 s after it started. The window has
        /// to outlast that, or the crash is mistaken for a slow start. A
        /// healthy tray ends the watch early by creating its first window.
        /// </summary>
        private const int StartupWatchMilliseconds = 5000;

        private const int PollMilliseconds = 100;

        /// <summary>How long close mode gives the tray to exit on its own before terminating it.</summary>
        private const int GracefulExitMilliseconds = 10000;

        private const uint WmQueryEndSession = 0x0011;
        private const uint WmEndSession = 0x0016;
        private const int EndSessionCloseApp = 0x00000001;
        private const uint SmtoAbortIfHung = 0x0002;
        private const uint MessageTimeoutMilliseconds = 5000;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

        private static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                return 2;
            }

            string mode = args[0];
            string trayExe = args[1];
            string folder = Path.GetDirectoryName(trayExe);

            if (mode == "close")
            {
                return Close(trayExe, folder) ? 0 : 1;
            }

            if (mode == "wait")
            {
                return WaitForFilesToBeFree(folder, TimeSpan.FromSeconds(WaitForFilesSeconds)) ? 0 : 1;
            }

            if (mode == "launch")
            {
                return Launch(trayExe, folder) ? 0 : 1;
            }

            return 2;
        }

        /// <summary>
        /// The same request Restart Manager and the installer's own close
        /// action send, sent to every top-level window of every running
        /// copy (the tray answers it by quitting - ShutdownRequestWindow),
        /// then a bounded wait for the exit, then termination only of what
        /// is still there. Every copy is targeted by process name, wherever
        /// it was started from, exactly like util:CloseApplication.
        /// </summary>
        private static bool Close(string trayExe, string folder)
        {
            string processName = Path.GetFileNameWithoutExtension(trayExe);
            Process[] running = Process.GetProcessesByName(processName);
            if (running.Length == 0)
            {
                return true;
            }

            HashSet<uint> targets = new HashSet<uint>();
            foreach (Process p in running)
            {
                targets.Add((uint)p.Id);
            }

            List<IntPtr> windows = new List<IntPtr>();
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (targets.Contains(pid))
                {
                    windows.Add(hWnd);
                }

                return true;
            }, IntPtr.Zero);

            foreach (IntPtr hWnd in windows)
            {
                IntPtr answer;
                SendMessageTimeout(hWnd, WmQueryEndSession, IntPtr.Zero, (IntPtr)EndSessionCloseApp, SmtoAbortIfHung, MessageTimeoutMilliseconds, out answer);
                if (answer != IntPtr.Zero)
                {
                    SendMessageTimeout(hWnd, WmEndSession, (IntPtr)1, (IntPtr)EndSessionCloseApp, SmtoAbortIfHung, MessageTimeoutMilliseconds, out answer);
                }
            }

            bool allGone = true;
            foreach (Process p in running)
            {
                using (p)
                {
                    if (!p.WaitForExit(GracefulExitMilliseconds))
                    {
                        try
                        {
                            p.Kill();
                            p.WaitForExit(GracefulExitMilliseconds);
                        }
                        catch (Exception)
                        {
                            allGone = false;
                        }
                    }
                }
            }

            return WaitForFilesToBeFree(folder, TimeSpan.FromSeconds(WaitForFilesSeconds)) && allGone;
        }

        private static bool Launch(string trayExe, string folder)
        {
            for (int attempt = 1; attempt <= LaunchAttempts; attempt++)
            {
                WaitForFilesToBeFree(folder, TimeSpan.FromSeconds(WaitForFilesSeconds));

                Process process;
                try
                {
                    // ShellExecuteEx, exactly as Wix4ShellExec did, so the
                    // tray is started the same way it always was (shell
                    // semantics, working directory, no inherited console
                    // handles) and the only thing this file changes is the
                    // waiting and the watching. A plain CreateProcess was
                    // measured to work too (2026-09-18, three upgrade cycles,
                    // every relaunch healthy and logging to the right
                    // place); parity with the old action is simply the
                    // safer default. .NET keeps a real process handle either
                    // way (SEE_MASK_NOCLOSEPROCESS), so the watch below still
                    // sees the exit code.
                    ProcessStartInfo start = new ProcessStartInfo(trayExe);
                    start.UseShellExecute = true;
                    start.WorkingDirectory = folder;
                    process = Process.Start(start);
                }
                catch (Exception)
                {
                    // The apphost itself could not be opened (the sharing
                    // violation case for LunaPanel.Tray.exe) - wait and try
                    // again rather than give up on the first refusal.
                    Thread.Sleep(PollMilliseconds);
                    continue;
                }

                if (process == null)
                {
                    return false;
                }

                using (process)
                {
                    if (CameUp(process))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// True unless the process exited with a non-zero code inside the
        /// watch window - the one signature of a load-time crash.
        ///
        /// The verdict is structural, not a heuristic: a healthy tray creates
        /// top-level windows (its tray icon's, its shutdown listener) within
        /// about a second of starting; a process that dies loading its entry
        /// assembly never creates one. So: exited -> the exit code decides;
        /// has a top-level window -> it is up; neither by the end of the
        /// window -> a slow start, not a crash.
        ///
        /// Two earlier drafts trusted WaitForInputIdle and both failed the
        /// forced test on 2026-09-18 (the entry assembly held exclusively
        /// the instant the new process appeared): the crashed process sits
        /// frozen under Windows Error Reporting for about a second, and in
        /// that state WaitForInputIdle reports it idle - so the launcher
        /// declared success, retried nothing, and left no tray running.
        /// </summary>
        private static bool CameUp(Process process)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            uint pid = (uint)process.Id;

            while (true)
            {
                if (process.WaitForExit(PollMilliseconds))
                {
                    return process.ExitCode == 0;
                }

                if (HasTopLevelWindow(pid))
                {
                    return true;
                }

                if (stopwatch.ElapsedMilliseconds >= StartupWatchMilliseconds)
                {
                    return true;
                }
            }
        }

        private static bool HasTopLevelWindow(uint pid)
        {
            bool found = false;
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                uint owner;
                GetWindowThreadProcessId(hWnd, out owner);
                if (owner == pid)
                {
                    found = true;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return found;
        }

        private static bool WaitForFilesToBeFree(string folder, TimeSpan budget)
        {
            if (folder == null || !Directory.Exists(folder))
            {
                return true;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (true)
            {
                if (AllBinariesAreOpenable(folder))
                {
                    return true;
                }

                if (stopwatch.Elapsed >= budget)
                {
                    return false;
                }

                Thread.Sleep(PollMilliseconds);
            }
        }

        /// <summary>
        /// Every .dll and .exe under the folder, opened the way the loader
        /// opens an image: read access, sharing read and delete but not
        /// write. A running LunaPanel passes this (mapped images share
        /// read); a file mid-write, or held exclusively, does not.
        /// </summary>
        private static bool AllBinariesAreOpenable(string folder)
        {
            try
            {
                foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        && !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                    {
                    }
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
