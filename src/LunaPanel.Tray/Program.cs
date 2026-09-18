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
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var options = RealServerEnvironment.Build();
        var app = ServerHostBuilder.Build(Array.Empty<string>(), options);

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

        using var context = new TrayApplicationContext(app, options);
        Application.Run(context);
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
