using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Discovery;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Network;
using LunaPanel.Core.Pairing;
using LunaPanel.Core.Updates;
using LunaPanel.Server.Discovery;
using LunaPanel.Server.Updates;
using LunaPanel.Server.Hosting;
using LunaPanel.Server.Tray;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace LunaPanel.Tray;

/// <summary>
/// Owns the tray icon, its menu, and the running host's lifetime.
/// <see cref="DeviceRegistry"/> and <see cref="PathDiscoveryResult"/> are
/// resolved straight out of <paramref name="app"/>'s own service provider -
/// the same singletons <see cref="ServerHostBuilder.Build"/> already wired
/// up for the HTTP endpoints - so every menu action calls them <b>in
/// process</b>, with no HTTP round trip to the tray's own host
/// (<c>ref/docs/pairing-and-devices.md</c>'s "What this means for the
/// tray").
///
/// Shutdown ordering (the "Quit" menu action, and the hard constraint the
/// brief calls out): hide the tray icon first (a lingering
/// <see cref="NotifyIcon"/> is a known WinForms quirk otherwise), THEN stop
/// Kestrel with <c>app.StopAsync()</c>/<c>DisposeAsync()</c> so the bound
/// port is actually released, THEN end the WinForms message loop via
/// <see cref="ApplicationContext.ExitThread"/>. Reversing the last two would
/// let the process exit (or <see cref="Application.Run(ApplicationContext)"/>
/// return) while Kestrel is still mid-shutdown, which is exactly the
/// "leaves a port bound" failure the brief warns about.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly WebApplication _app;
    private readonly DeviceRegistry _deviceRegistry;
    private readonly IDiagnosticLog _log;
    private readonly OrphanMarkerStore _orphanMarkers;
    private readonly PortSettingsStore _portSettings;
    private readonly PathOverrideStore _pathOverrides;
    private readonly StatusWindowPositionStore _statusWindowPosition;
    private readonly PathDiscoveryResult _discovery;
    private readonly IReleaseChecker _releaseChecker;
    private readonly LastLaunchedVersionStore _lastLaunchedVersion;
    private readonly string _url;
    private readonly int _hostAccessPort;
    private readonly string? _macroBuilderUrl;
    private readonly string? _transferUrl;
    private readonly string? _macroTimingUrl;
    private readonly string? _editLivePanelsUrl;
    private readonly NotifyIcon _notifyIcon;
    private StatusForm? _statusForm;
    private bool _shuttingDown;

    public TrayApplicationContext(WebApplication app, ServerHostOptions options)
    {
        _app = app;
        _deviceRegistry = app.Services.GetRequiredService<DeviceRegistry>();
        _log = app.Services.GetRequiredService<IDiagnosticLog>();
        _orphanMarkers = app.Services.GetRequiredService<OrphanMarkerStore>();
        _portSettings = app.Services.GetRequiredService<PortSettingsStore>();
        _pathOverrides = app.Services.GetRequiredService<PathOverrideStore>();
        _statusWindowPosition = app.Services.GetRequiredService<StatusWindowPositionStore>();
        _discovery = app.Services.GetRequiredService<PathDiscoveryResult>();
        // Built here rather than resolved out of the host's container like
        // everything above, deliberately: nothing the HTTP surface serves
        // knows or cares about updates, and registering them as host
        // singletons would hand an HttpClient to every test host this
        // project builds for a facility only the tray ever uses. They take
        // the same layouts directory and the same diagnostics log those
        // singletons were given, so nothing about where state lives changes.
        _releaseChecker = GitHubReleaseChecker.Create(_log);
        _lastLaunchedVersion = new LastLaunchedVersionStore(_discovery.LunaPanelDirectories.LayoutsDirectory, _log);
        _url = $"http://{options.BindAddress}:{options.Port}/";
        _hostAccessPort = options.HostAccessPort;

        _macroBuilderUrl = TrayMacroBuilder.Url(options.HostAccessPort);
        _transferUrl = TrayTransfer.Url(options.HostAccessPort);
        _macroTimingUrl = TrayMacroTiming.Url(options.HostAccessPort);
        _editLivePanelsUrl = TrayEditLivePanels.Url(options.HostAccessPort);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Add a device", null, (_, _) => Dispatch(TrayAction.AddDevice));
        menu.Items.Add("Devices", null, (_, _) => Dispatch(TrayAction.Devices));
        // Authoring a macro is a PC job now (ref/docs/macro-builder.md), and
        // the address that allows it is a loopback one nobody would guess -
        // so the program that owns that address is the thing that has to
        // offer it. Left off entirely when no host listener was bound, which
        // production never does.
        if (_macroBuilderUrl is not null)
        {
            menu.Items.Add(TrayMacroBuilder.MenuLabel, null, (_, _) => Dispatch(TrayAction.MacroBuilder));
        }
        // Import and export live on the same loopback-only surface, for the
        // same reason (ref/docs/transfer.md), and are left off the menu on
        // the same condition.
        if (_transferUrl is not null)
        {
            menu.Items.Add(TrayTransfer.MenuLabel, null, (_, _) => Dispatch(TrayAction.Transfer));
        }
        // How fast LunaPanel drives the keyboard (ref/docs/macro-timing.md).
        // Machine-wide already, so the PC is where it belongs - and until
        // this item existed the only way to the pane was to already know the
        // loopback address. Gated and omitted on the same condition as the
        // two above.
        if (_macroTimingUrl is not null)
        {
            menu.Items.Add(TrayMacroTiming.MenuLabel, null, (_, _) => Dispatch(TrayAction.MacroTiming));
        }
        // The per-device "edit a device live from the PC" buttons already
        // live on the Devices settings pane
        // (ref/docs/pairing-and-devices.md); this is only a way in, gated on
        // the same condition as the three items above.
        if (_editLivePanelsUrl is not null)
        {
            menu.Items.Add(TrayEditLivePanels.MenuLabel, null, (_, _) => Dispatch(TrayAction.EditLivePanels));
        }
        menu.Items.Add("Status", null, (_, _) => ShowStatusWindow());
        menu.Items.Add("Ports", null, (_, _) => Dispatch(TrayAction.Ports));
        menu.Items.Add("About", null, (_, _) => Dispatch(TrayAction.About));
        menu.Items.Add("Check for updates", null, (_, _) => Dispatch(TrayAction.CheckForUpdates));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Dispatch(TrayAction.Quit));

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "LunaPanel",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowStatusWindow();

        // Before the pairing-code window below, so that on the one launch
        // where both could fire, the commander is told what happened to
        // their install before being handed a code to type.
        ShowUpdatedMessageIfThisLaunchFollowedAnUpdate();

        // First run is the one case (ref/docs/pairing-and-devices.md) where
        // nothing has shown this code anywhere yet. LunaPanel.Tray is a
        // WinExe app with no attached console, so ServerHostBuilder's own
        // startup banner (Console.WriteLine) has nowhere to appear when
        // launched this way - see TrayStartupBehavior's own remarks for why
        // the tray shows it itself instead.
        if (TrayStartupBehavior.ShouldShowPairingCodeOnStartup(_deviceRegistry.IsPairingWindowOpen))
        {
            ShowAddDeviceWindow();
        }
    }

    private void ShowStatusWindow()
    {
        var model = BuildStatusModel();

        if (_statusForm is { IsDisposed: false })
        {
            _statusForm.SetStatus(model);
            _statusForm.EnsureOnLiveScreen();
            _statusForm.Show();
            _statusForm.Activate();
            return;
        }

        _statusForm = new StatusForm(model, _hostAccessPort, _statusWindowPosition, Dispatch);
        _statusForm.EnsureOnLiveScreen();
        _statusForm.Show();
    }

    /// <summary>
    /// The one place a tray action turns into behaviour. Both surfaces go
    /// through here - the right-click menu items above, and every button on
    /// the Status window (<see cref="TrayStatusWindowActions"/>) - so the two
    /// cannot drift into doing subtly different things for the same named
    /// action. Adding an action means adding it here once, not twice.
    ///
    /// <see cref="TrayAction.Quit"/> cannot be awaited from a click handler,
    /// so it goes through <see cref="BeginQuit"/> rather than being dropped
    /// on the floor - see that method for what a bare <c>_ =</c> cost.
    /// </summary>
    private void Dispatch(TrayAction action)
    {
        switch (action)
        {
            case TrayAction.AddDevice:
                ShowAddDeviceWindow();
                break;
            case TrayAction.Devices:
                ShowDevicesWindow();
                break;
            case TrayAction.MacroBuilder:
                OpenInBrowser(_macroBuilderUrl!);
                break;
            case TrayAction.Transfer:
                OpenInBrowser(_transferUrl!);
                break;
            case TrayAction.MacroTiming:
                OpenInBrowser(_macroTimingUrl!);
                break;
            case TrayAction.EditLivePanels:
                OpenInBrowser(_editLivePanelsUrl!);
                break;
            case TrayAction.Ports:
                ShowPortsWindow();
                break;
            case TrayAction.About:
                ShowAboutWindow();
                break;
            case TrayAction.CheckForUpdates:
                BeginCheckForUpdates();
                break;
            case TrayAction.Quit:
                BeginQuit();
                break;
        }
    }

    private void ShowAddDeviceWindow()
    {
        _deviceRegistry.OpenPairingWindow();
        using var form = new AddDeviceForm(PairingCodeFormatter.GroupForDisplay(_deviceRegistry.CurrentCode), _url);
        form.ShowDialog();
        RefreshStatusFormIfOpen();
    }

    /// <summary>
    /// Hands the loopback URL to the default browser.
    /// <c>UseShellExecute = true</c> is what makes a URL open a browser at
    /// all rather than being treated as an executable to run. Wrapped
    /// because a machine with no registered browser throws, and a tray icon
    /// that dies on a menu click is worse than one that says nothing.
    /// </summary>
    private void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"LunaPanel could not open your browser. Open this address on this PC instead:{Environment.NewLine}{Environment.NewLine}{url}{Environment.NewLine}{Environment.NewLine}({ex.Message})",
                "LunaPanel",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private void ShowDevicesWindow()
    {
        using var form = new DevicesForm(_deviceRegistry, _orphanMarkers);
        form.ShowDialog();
        RefreshStatusFormIfOpen();
    }

    /// <summary>
    /// Opens the "Ports" dialog. <see cref="BeginQuit"/>, not
    /// <see cref="QuitAsync"/> directly, is handed through as the restart
    /// dialog's shutdown action - the same reason the "Quit" menu item and
    /// button use it rather than calling <c>QuitAsync</c> themselves (see
    /// that method's own remarks): a click handler cannot await, and the
    /// dialog's OK handler is exactly that.
    /// </summary>
    private void ShowPortsWindow()
    {
        using var form = new PortSettingsForm(_portSettings, BeginQuit);
        form.ShowDialog();
    }

    /// <summary>
    /// Opens the "About" window - version, logs folder, and the manual
    /// Elite/EDHM path fallback (<see cref="AboutForm"/>). Same
    /// <see cref="BeginQuit"/>-not-<see cref="QuitAsync"/> reasoning as
    /// <see cref="ShowPortsWindow"/> above: a click handler cannot await.
    /// </summary>
    private void ShowAboutWindow()
    {
        using var form = new AboutForm(_discovery, _discovery.LunaPanelDirectories, _pathOverrides, BeginQuit);
        form.ShowDialog();
    }

    /// <summary>
    /// Starts the "Check for updates" conversation
    /// (<see cref="UpdateCheckFlow"/>) from a click or menu handler, neither
    /// of which can await - same shape and same reason as
    /// <see cref="BeginQuit"/>, including the fault continuation: a
    /// discarded task that faults does so in complete silence, which is a
    /// bug this file has already paid for once.
    /// </summary>
    private void BeginCheckForUpdates()
    {
        var flow = new UpdateCheckFlow(_releaseChecker, _log, RunningVersion.Current());

        _ = flow.RunAsync().ContinueWith(
            t => _log.Error("Updates", "The update check faulted outside its own handler", t.Exception?.GetBaseException().ToString() ?? "unknown"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    /// <summary>
    /// The other half of the silent self-update: the flow that installs an
    /// update deliberately says nothing and shows no installer UI, so this
    /// launch is where a commander finds out it happened.
    ///
    /// Detected from LunaPanel's own side rather than from a flag the
    /// installer could pass, because that also covers the other way the
    /// version changes - somebody double-clicking a newer MSI over an older
    /// install by hand. See <see cref="LastLaunchedVersionStore"/> for the
    /// three-way decision; a first install is deliberately silent.
    /// </summary>
    private void ShowUpdatedMessageIfThisLaunchFollowedAnUpdate()
    {
        var version = RunningVersion.Current();

        if (!_lastLaunchedVersion.RecordLaunch(version))
        {
            return;
        }

        _log.Info("Updates", "This launch followed an update", version);
        MessageBox.Show(
            $"You're all set — LunaPanel just updated itself to {version} and is back up and running.",
            "LunaPanel",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private TrayStatusModel BuildStatusModel()
    {
        var pairedDeviceCount = _deviceRegistry.ListDevices().Count;
        return TrayStatusModelBuilder.Build(_url, _discovery, pairedDeviceCount);
    }

    private void RefreshStatusFormIfOpen()
    {
        if (_statusForm is { IsDisposed: false })
        {
            _statusForm.SetStatus(BuildStatusModel());
        }
    }

    /// <summary>
    /// How long shutting Kestrel down is allowed to take before LunaPanel
    /// stops waiting for it and goes anyway. The process is ending either
    /// way, so the only thing a longer wait can buy is a tidier stop - and
    /// the only thing it can cost is a window that will not go away.
    /// </summary>
    private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Starts <see cref="QuitAsync"/> from a click or menu handler, neither
    /// of which can await.
    ///
    /// <b>Not a bare <c>_ = QuitAsync()</c>, and that distinction cost a
    /// bug (2026-09-10).</b> Quit used to be an <c>async</c> menu handler,
    /// where a thrown exception surfaces loudly through WinForms. Folding
    /// the menu and the Status window's buttons onto one dispatch turned it
    /// into a discarded task, so a shutdown that failed part way did so in
    /// complete silence - the commander saw exactly that: <i>"when I hit
    /// quit on the status panel, the project seems to stop, but the status
    /// window doesn't close."</i> The tray icon had gone and Kestrel had
    /// stopped, because those happen first; <see cref="ExitThread"/> never
    /// ran, so <c>Application.Run</c> never returned, so
    /// <c>Program.Main</c>'s <c>using</c> never disposed this context and
    /// never disposed the window.
    /// </summary>
    private void BeginQuit()
    {
        _ = QuitAsync().ContinueWith(
            t => _log.Error("Tray", "Shutdown faulted outside its own handler", t.Exception?.GetBaseException().ToString() ?? "unknown"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Shutdown order is a hard constraint (see this class's own remarks):
    /// hide the tray icon, THEN stop Kestrel so the port is actually
    /// released, THEN end the message loop.
    ///
    /// <b><see cref="ExitThread"/> is in a <c>finally</c>, deliberately.</b>
    /// It is the single call that ends <c>Application.Run</c>, and every
    /// other step here can fail: a host that throws on stop, a dispose that
    /// hangs, a shutdown that outruns <see cref="ShutdownBudget"/>. None of
    /// those are reasons to leave a commander with a dead window they cannot
    /// close and no way to quit the program.
    /// </summary>
    private async Task QuitAsync()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        _log.Info("Tray", "Quit requested", "shutting down");

        _notifyIcon.Visible = false;

        // The window goes now rather than when shutdown finishes. Stopping
        // Kestrel can take seconds with a live channel still open, and a
        // window sitting there through it says "nothing happened" - which is
        // the report this whole method is a fix for.
        if (_statusForm is { IsDisposed: false })
        {
            _statusForm.Hide();
        }

        // Any other open native window (Add a device / Paired devices /
        // Ports) is blocked inside its own modal ShowDialog loop and has no
        // reference kept here - ExitThread's own "closes all windows"
        // documented behavior isn't reliable for those (reported
        // 2026-09-15: quitting while the Ports dialog was open left it on
        // screen). Application.OpenForms tracks every open Form regardless
        // of how it was shown, so closing everything on it except the
        // already-handled status form covers all three without needing to
        // thread a reference back from each Show*Window method.
        // ToList() first: Close() removes the form from OpenForms, and
        // mutating a collection while enumerating it throws.
        foreach (var openForm in Application.OpenForms.Cast<Form>().ToList())
        {
            if (!ReferenceEquals(openForm, _statusForm) && !openForm.IsDisposed)
            {
                openForm.Close();
            }
        }

        try
        {
            using var budget = new CancellationTokenSource(ShutdownBudget);
            await _app.StopAsync(budget.Token);
            await _app.DisposeAsync();
            _log.Info("Tray", "Shutdown complete", "host stopped and disposed");
        }
        catch (Exception ex)
        {
            _log.Warn("Tray", "Shutdown did not finish cleanly; exiting anyway", $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            ExitThread();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Dispose();
            _statusForm?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// The tray icon, read from the .ico embedded in this assembly at the
    /// size Windows actually wants for the notification area. That file holds
    /// six real sizes (16 through 64) rather than one image scaled, so this
    /// gets a purpose-drawn 16px crescent rather than a squashed 32px one.
    ///
    /// Falls back to the stock application icon if the resource is missing: a
    /// tray app with an ugly icon is usable, one that throws on startup is not.
    /// </summary>
    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var assembly = typeof(TrayApplicationContext).Assembly;
            using var stream = assembly.GetManifestResourceStream("LunaPanel.Tray.lunapanel.ico");
            return stream is null
                ? System.Drawing.SystemIcons.Application
                : new System.Drawing.Icon(stream, SystemInformation.SmallIconSize);
        }
        catch (Exception)
        {
            return System.Drawing.SystemIcons.Application;
        }
    }
}
