using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Discovery;
using LunaPanel.Core.Network;
using LunaPanel.Server.Discovery;
using LunaPanel.Server.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace LunaPanel.Tray;

/// <summary>
/// LunaPanel's normal way to run now (see <c>ref/docs/hosting.md</c>): hosts
/// the same <see cref="ServerHostBuilder"/>-built Kestrel app the headless
/// console entry point (<c>LunaPanel.Server</c>'s own <c>Program.cs</c>)
/// runs, but in the system tray instead of a console window - see
/// <c>ref/docs/pairing-and-devices.md</c>'s "What this means for the tray".
/// <c>LunaPanel.Server</c>'s console entry point is untouched and still
/// works for headless runs and diagnostics; this is a second, independent
/// entry point over the same shared <see cref="ServerHostBuilder"/>.
/// </summary>
internal static class Program
{
    /// <summary>
    /// How many times startup is attempted before an assembly-load failure
    /// is allowed to surface as the crash it would otherwise have been.
    /// Bounded on purpose: a genuinely missing or broken file must still
    /// crash loudly (event log, WER), just a few seconds later - never hang
    /// or loop in silence.
    /// </summary>
    private const int StartupLoadAttempts = 5;

    /// <summary>
    /// The most one retry waits for LunaPanel's own binaries to become
    /// openable again before trying anyway. Five attempts at two seconds
    /// bounds the whole thing at roughly ten seconds.
    /// </summary>
    private static readonly TimeSpan StartupLoadRetryWait = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How a relaunched copy is told which attempt it is, so the retry stays
    /// bounded across processes. Nothing else reads the command line -
    /// <see cref="ServerHostBuilder.Build"/> is handed an empty array.
    /// </summary>
    private const string StartupAttemptSwitch = "--startup-attempt=";

    /// <summary>
    /// Deliberately references nothing but the runtime's core library.
    ///
    /// <b>Why this shape (2026-09-18).</b> After the installer replaces
    /// LunaPanel's files it relaunches <c>LunaPanel.Tray.exe</c> about a
    /// second later (<c>installer/Package.wxs</c>, <c>Wix4ShellExec</c> after
    /// <c>InstallFinalize</c>). Twice on a real machine that relaunch died
    /// within 130 ms of starting, once with
    /// <c>FileLoadException: ... LunaPanel.Tray.dll ... being used by
    /// another process</c> and once with <c>FileNotFoundException: Could not
    /// load file or assembly 'LunaPanel.Server, Version=...'</c> - and a
    /// manual relaunch minutes later was always fine. Reproduced under
    /// controlled conditions, the second message is exactly what the runtime
    /// reports when <c>LunaPanel.Server.dll</c> is present, correct, and
    /// merely held open exclusively by another process at the instant the
    /// loader wants it (the entry assembly reports the raw sharing violation
    /// instead; a dependency resolved through the trusted-platform list
    /// reports it as "not found"). Both are the same transient condition.
    ///
    /// The runtime resolves an assembly when it JIT-compiles the first method
    /// that mentions one of its types, so a <c>try</c> inside a method that
    /// itself mentions <c>ServerHostBuilder</c> can never catch that
    /// failure - it happens before the method's first line runs. Hence
    /// <see cref="Run"/>: everything that touches LunaPanel's own assemblies
    /// (or WinForms) lives there, marked <c>NoInlining</c> so the JIT of
    /// <c>Main</c> stays free of those references, and the load attempt
    /// happens at the <c>Run()</c> call where it can be caught.
    ///
    /// The retry is a <b>fresh process</b>, not a loop. Measured 2026-09-18:
    /// the runtime remembers a failed bind for the life of the process, so
    /// calling <see cref="Run"/> again after the hold is gone fails with the
    /// very same exception without touching the disk. A relaunched copy
    /// binds from scratch and comes up. The attempt number rides along on
    /// the command line so the whole thing stays bounded; this process
    /// exits quietly once the next one has been started, and the last
    /// attempt is left to crash loudly, as before.
    ///
    /// What this cannot cover: <c>LunaPanel.Tray.dll</c> itself being held
    /// at the moment the host loads it, because no managed code of ours has
    /// run yet. That case is the installer's to avoid, not this file's.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        var attempt = StartupAttemptFromCommandLine();

        try
        {
            Run(attempt);
        }
        catch (Exception ex) when (attempt < StartupLoadAttempts && IsTransientAssemblyLoadFailure(ex))
        {
            WaitForOwnBinariesToBeOpenable(StartupLoadRetryWait);

            if (!TryRelaunchSelf(attempt + 1))
            {
                throw;
            }
        }
    }

    private static int StartupAttemptFromCommandLine()
    {
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (arg.StartsWith(StartupAttemptSwitch, StringComparison.Ordinal)
                && int.TryParse(arg.AsSpan(StartupAttemptSwitch.Length), out var parsed)
                && parsed > 1)
            {
                return parsed;
            }
        }

        return 1;
    }

    /// <summary>
    /// Starts a fresh copy of this executable carrying the next attempt
    /// number. Out of line for the same reason as <see cref="Run"/>:
    /// <c>System.Diagnostics.Process</c> is its own assembly, and mentioning
    /// it in <c>Main</c> would load it while <c>Main</c> compiles. If even
    /// that fails, the caller rethrows the original failure.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryRelaunchSelf(int nextAttempt)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null)
            {
                return false;
            }

            Process.Start(new ProcessStartInfo(exe, StartupAttemptSwitch + nextAttempt)
            {
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory,
            });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// A file that is missing, cannot be opened, or is only partly written
    /// yet - at the very start of the process, each of these reads as "the
    /// installer's files are not settled", which is worth a bounded retry.
    /// Checked down the inner-exception chain because a static initializer
    /// that trips on one wraps it in <see cref="TypeInitializationException"/>.
    /// </summary>
    private static bool IsTransientAssemblyLoadFailure(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Polls until every <c>LunaPanel.*.dll</c>/<c>.exe</c> beside this
    /// executable can be opened the way the loader opens them (read, sharing
    /// read and delete but not write), or the wait runs out - whichever is
    /// first. Retrying the instant the failure happened would just hit the
    /// same hold again; waiting a fixed time would guess at how long it
    /// lasts. This waits for the actual condition, bounded.
    /// </summary>
    private static void WaitForOwnBinariesToBeOpenable(TimeSpan wait)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < wait)
        {
            if (OwnBinariesAreOpenable())
            {
                return;
            }

            Thread.Sleep(100);
        }
    }

    private static bool OwnBinariesAreOpenable()
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "LunaPanel.*"))
            {
                if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The whole of what used to be <c>Main</c>. Kept out of line so that
    /// resolving <c>LunaPanel.Server</c>, <c>LunaPanel.Core</c> and WinForms
    /// happens here, at the call inside <see cref="Main"/>'s <c>try</c>,
    /// rather than while <c>Main</c> itself is being compiled - see that
    /// method's remarks.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(int attempt)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var options = RealServerEnvironment.Build();
        var app = ServerHostBuilder.Build(Array.Empty<string>(), options);

        if (attempt > 1)
        {
            // Evidence for the next investigation: a launch that needed this
            // is exactly the post-install race described on Main, and the
            // count says roughly how long the hold lasted.
            app.Services.GetRequiredService<IDiagnosticLog>()
                .Warn("Startup", "Startup needed a relaunch to load LunaPanel's own assemblies", $"attempt={attempt}");
        }

        // One-time first-launch check: before the host ever starts
        // listening, confirm Elite Dangerous was actually found and let the
        // commander fix it right then if not - see EliteSetupForm's own
        // remarks on why this exists alongside AboutForm's identical, but
        // easy-to-miss, opt-in check. Gated on
        // PathOverrideSettings.EliteSetupAcknowledged so this never shows
        // again once a commander has been through it once, either way.
        var pathOverrides = app.Services.GetRequiredService<PathOverrideStore>();
        if (!pathOverrides.Load().EliteSetupAcknowledged)
        {
            var discovery = app.Services.GetRequiredService<PathDiscoveryResult>();
            using var setupForm = new EliteSetupForm(discovery, pathOverrides);
            setupForm.ShowDialog();

            // The override may have changed (a manual pick, or just the
            // acknowledged flag) - dispose and rebuild fresh so THIS SAME
            // launch honors it, no restart needed. Same dispose-and-rebuild
            // shape as RestartAgainstDefaultPort below, just run
            // unconditionally here rather than only on a failed start.
            //
            // Re-running RealServerEnvironment.Build() (not just reusing
            // `options`) matters: it is what actually reads
            // PathOverrideStore.Load().EliteInstallPath back into
            // PathDiscoveryEnvironment.EliteInstallPathOverride - the stale
            // `options` from before this dialog ran would still carry
            // whatever override (or lack of one) existed before a manual
            // pick was saved just now.
            app.DisposeAsync().GetAwaiter().GetResult();
            options = RealServerEnvironment.Build();
            app = ServerHostBuilder.Build(Array.Empty<string>(), options);
        }

        // Started before Application.Run begins the WinForms message loop -
        // there is no UI SynchronizationContext yet for this blocking wait
        // to fight over, and every tray menu action (Add a device, Devices)
        // needs the host already running before it can touch the
        // DeviceRegistry singleton the host wired up.
        //
        // A bind failure here (port already in use, or a privileged port
        // picked accidentally) used to throw unhandled before any UI
        // existed. Letting a commander type an arbitrary custom port
        // (PortSettingsForm) makes that meaningfully more likely, so a
        // failed start now retries once against the shipped default port
        // rather than crashing outright.
        if (!TryStart(app))
        {
            app = RestartAgainstDefaultPort(app, ref options);
            if (app is null || !TryStart(app))
            {
                MessageBox.Show(
                    "LunaPanel could not start. Check that the port it needs isn't already in use by another program.",
                    "LunaPanel",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
        }

        // The listener is the app's answer to "please exit" from outside -
        // Windows Installer's Restart Manager and the installer's own close
        // action, or a real logoff (see ShutdownRequestWindow). It ends the
        // message loop; the host is stopped below, once Application.Run has
        // returned and the context (tray icon included) is disposed - the
        // same icon-then-host-then-loop order TrayApplicationContext's Quit
        // documents, just with the loop's end moved first because the
        // request arrives from inside a window message. The context's own
        // quit path is private to it, so this is wired here rather than
        // through it.
        var shutdownRequested = false;
        var log = app.Services.GetRequiredService<IDiagnosticLog>();
        using (var context = new TrayApplicationContext(app, options))
        using (new ShutdownRequestWindow(() =>
        {
            shutdownRequested = true;
            log.Info("Tray", "Shutdown requested by the system", "Restart Manager, the installer's close action, or the session ending");
            Application.Exit();
        }))
        {
            Application.Run(context);
        }

        if (shutdownRequested)
        {
            StopHostAfterShutdownRequest(app, log);
        }
    }

    /// <summary>
    /// The host stop that <c>TrayApplicationContext</c>'s own Quit does
    /// before ending the loop, done after it here. Run on the thread pool:
    /// the WinForms synchronization context may still be installed on this
    /// thread, and a continuation posted to a loop that has already ended
    /// would never run. Bounded twice over - the stop's own budget and the
    /// outer wait - because a shutdown request is exactly the moment an
    /// installer is waiting on this process to be gone.
    /// </summary>
    private static void StopHostAfterShutdownRequest(WebApplication app, IDiagnosticLog log)
    {
        try
        {
            var stop = Task.Run(async () =>
            {
                using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await app.StopAsync(budget.Token);
                await app.DisposeAsync();
            });

            if (stop.Wait(TimeSpan.FromSeconds(8)))
            {
                log.Info("Tray", "Shutdown complete", "host stopped and disposed after a system shutdown request");
            }
            else
            {
                log.Warn("Tray", "Shutdown did not finish in time; exiting anyway", "system shutdown request");
            }
        }
        catch (Exception ex)
        {
            log.Warn("Tray", "Shutdown did not finish cleanly; exiting anyway", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts <see cref="WebApplication.StartAsync"/>, logging and
    /// swallowing a bind failure rather than letting it surface unhandled -
    /// there is no UI yet for an unhandled exception at this point in
    /// startup to be shown against.
    /// </summary>
    private static bool TryStart(WebApplication app)
    {
        try
        {
            app.StartAsync().GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            try
            {
                app.Services.GetRequiredService<IDiagnosticLog>()
                    .Error("Startup", "LunaPanel failed to start", $"{ex.GetType().Name}: {ex.Message}");
            }
            catch
            {
                // Best-effort only - the DI container itself may be the
                // thing that failed to come up.
            }

            return false;
        }
    }

    /// <summary>
    /// Disposes the app that just failed to start and rebuilds one bound to
    /// <see cref="PortSettings.DefaultPort"/> instead, unless that was
    /// already the port that just failed - retrying the exact same port a
    /// second time cannot succeed where the first attempt did not.
    /// </summary>
    private static WebApplication? RestartAgainstDefaultPort(WebApplication failedApp, ref ServerHostOptions options)
    {
        failedApp.DisposeAsync().GetAwaiter().GetResult();

        if (options.Port == PortSettings.DefaultPort)
        {
            return null;
        }

        options = options with { Port = PortSettings.DefaultPort };
        return ServerHostBuilder.Build(Array.Empty<string>(), options);
    }
}
