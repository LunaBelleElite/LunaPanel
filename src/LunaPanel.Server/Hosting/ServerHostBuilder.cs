using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using LunaPanel.Core.Catalogue;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Discovery;
using LunaPanel.Core.GameState;
using LunaPanel.Core.Input;
using LunaPanel.Core.Latching;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Macros;
using LunaPanel.Core.Network;
using LunaPanel.Core.Pairing;
using LunaPanel.Core.Theme;
using LunaPanel.Core.Tray;
using LunaPanel.Server.Bindings;
using LunaPanel.Server.Discovery;
using LunaPanel.Server.Http;
using LunaPanel.Server.Input;
using LunaPanel.Server.Layouts;
using LunaPanel.Server.Macros;
using LunaPanel.Server.Theme;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using CoreCatalogue = LunaPanel.Core.Catalogue.Catalogue;
using ServerCatalogueLoader = LunaPanel.Server.Catalogue.CatalogueLoader;

namespace LunaPanel.Server.Hosting;

/// <summary>
/// Builds LunaPanel's whole ASP.NET Core host from a <see cref="ServerHostOptions"/>:
/// wires the diagnostics pipeline, runs path discovery, constructs the
/// device registry, binds Kestrel to exactly one address, and maps this
/// task's three endpoints (<c>/api/health</c>, <c>/api/pair</c>,
/// <c>/api/diagnostics</c>) behind device authentication. Never reads the
/// real environment itself - see <see cref="RealServerEnvironment"/> for the
/// one call site that does, and <c>ref/docs/discovery.md</c> for the same
/// split applied to path discovery.
/// </summary>
public static class ServerHostBuilder
{
    public static WebApplication Build(string[] args, ServerHostOptions options)
    {
        // This project is Windows-only (the injector is Win32 SendInput -
        // see ref/docs/injection.md) but deliberately targets net10.0, not
        // net10.0-windows (same reasoning as Win32KeyInjector's own
        // [SupportedOSPlatform] split). This early-exit guard is what lets
        // this method construct the production Win32KeyInjector below
        // without a CA1416 warning - not a portability shim (nothing here
        // behaves differently on another OS; it just states the hard
        // constraint loudly instead of failing later with a confusing
        // PlatformNotSupportedException from deep inside a P/Invoke call).
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("LunaPanel.Server requires Windows (Win32 SendInput).");
        }

        var layout = LunaPanelDirectories.Resolve(options.DiscoveryEnvironment.LocalAppData, options.DiscoveryEnvironment.IsDevBuild);

        var writer = new DiagnosticLogWriter(layout.LogsDirectory, options.Clock, options.LogRetentionDays, options.Redactor);
        var ringBuffer = new DiagnosticRingBuffer(options.DiagnosticsRingBufferCapacity);
        var log = new CompositeDiagnosticLog(writer, ringBuffer);

        // Re-resolves and re-ensures the same LunaPanel directories
        // LunaPanelDirectories.Resolve already computed above -
        // EnsureCreated is idempotent (LunaPanelDirectoriesTests pins that
        // calling it twice does not throw), and this is what actually runs
        // the Elite/bindings/EDHM discovery sweep and logs every probe.
        var discovery = PathDiscoveryService.Discover(options.DiscoveryEnvironment, log);
        var deviceRegistry = new DeviceRegistry(discovery.LunaPanelDirectories.DeviceRegistryFilePath, log, options.Clock);

        // Make the bind-address choice legible (O12): when more than one
        // candidate existed, log all of them - address, interface name,
        // type, gateway presence - and name the one chosen, so
        // LUNAPANEL_BIND_ADDRESS becomes an informed override rather than a
        // guess. A single-candidate or override-driven resolution has
        // nothing ambiguous to explain.
        if (options.BindCandidates is { Count: > 1 } bindCandidates)
        {
            foreach (var candidate in bindCandidates)
            {
                var chosen = candidate.Address.Equals(options.BindAddress) ? " <- chosen" : string.Empty;
                log.Info(
                    "Server",
                    $"Bind candidate: {candidate.Address} on '{candidate.InterfaceName}' "
                    + $"(type={candidate.InterfaceType}, gateway={candidate.HasGateway}){chosen}");
            }
        }

        log.Info(
            "Server",
            $"LunaPanel ready at http://{options.BindAddress}:{options.Port}/ - "
            + $"Elite install found: {discovery.EliteInstallations.Count > 0}, "
            + $"binds found: {discovery.Bindings.LatestBindsFilePath is not null} (latest {discovery.Bindings.LatestVersion?.ToString() ?? "none"}), "
            + $"EDHM present: {discovery.Edhm.SettingsFound}");

        // The pairing code goes to the console and NOWHERE else. It is
        // deliberately not written through IDiagnosticLog: the diagnostics log
        // is expected to be pasted into public bug reports, and
        // ServerHostBuilderTests pins that neither the code nor a raw token
        // ever reaches a log line. The console is the operator's own screen,
        // which is the one place the code has to appear for anyone to be able
        // to pair at all - it was previously shown nowhere, so the client was
        // unusable without reading the value out of a debugger.
        // Plain ASCII on purpose: box-drawing characters render as mojibake
        // in the default Windows console codepage, and a first-run banner that
        // looks corrupted is worse than a plain one.
        var url = $"http://{options.BindAddress}:{options.Port}/";
        Console.WriteLine();
        Console.WriteLine("  +------------------------------------------------+");
        Console.WriteLine("  |  LunaPanel is running.                         |");
        Console.WriteLine("  |                                                |");
        Console.WriteLine(("  |  On your phone or tablet, open:                ").PadRight(51) + "|");
        Console.WriteLine(("  |    " + url).PadRight(51) + "|");
        Console.WriteLine("  |                                                |");
        // Pairing is closed by default (ref/docs/pairing-and-devices.md) - a
        // code is only ever shown here while the window is actually open, so
        // this banner never prints a code that would just be refused. The
        // window opens on its own only when no device is registered yet
        // (first run); once one exists, it stays closed until an
        // already-paired device's Settings > Devices > "Add another device"
        // opens it again - there is no console-triggered way to reopen it,
        // by design (that route is the tray's "Add a device", a separate
        // dispatch - see the class remarks above).
        if (deviceRegistry.IsPairingWindowOpen)
        {
            Console.WriteLine(("  |  Then enter this pairing code:                 ").PadRight(51) + "|");
            Console.WriteLine(("  |    " + deviceRegistry.CurrentCode).PadRight(51) + "|");
        }
        else
        {
            Console.WriteLine(("  |  Pairing is closed. To add a device, open      ").PadRight(51) + "|");
            Console.WriteLine(("  |  Settings > Devices on an already-paired one.  ").PadRight(51) + "|");
        }

        // The PC's own address, which needs no pairing code at all
        // (ref/docs/hosting.md). A headless run has no tray icon, so this
        // banner is the only place the loopback URL is ever named - the tray
        // is the answer for everybody else.
        if (options.HostAccessPort > 0)
        {
            Console.WriteLine("  |                                                |");
            Console.WriteLine(("  |  On this PC (no pairing code needed):          ").PadRight(51) + "|");
            Console.WriteLine(("  |    " + HostRequest.LoopbackUrl(options.HostAccessPort)).PadRight(51) + "|");
        }

        Console.WriteLine("  +------------------------------------------------+");
        Console.WriteLine();

        var catalogue = ServerCatalogueLoader.LoadShipped();
        var layoutStore = new LayoutStore(discovery.LunaPanelDirectories.LayoutsDirectory, log);
        // Alongside the layout store, same directory and per-device-file
        // convention (ref/docs/theme.md's "Storage") - a device's manual
        // colour override is closely related, per-device state, not a
        // separate subsystem that needs its own directory.
        var themeOverrideStore = new ThemeOverrideStore(discovery.LunaPanelDirectories.LayoutsDirectory, log);
        // Same directory again, and the reason it is the same one: an orphan
        // marker describes a layout file, and lives beside it (ref/docs/
        // layout-import.md). It is what keeps a forgotten device's layout
        // describable as "Phone - last used yesterday, 20:14" instead of a
        // line of hex, and it is the only record that survives the forget.
        var orphanMarkerStore = new OrphanMarkerStore(discovery.LunaPanelDirectories.LayoutsDirectory, log);
        // Same directory/per-device-file convention again (ref/docs/panels-
        // and-pages.md's "Setting: 'Merging and expanding panels'") - closely
        // related per-device state, not a separate subsystem.
        var panelSettingsStore = new PanelSettingsStore(discovery.LunaPanelDirectories.LayoutsDirectory, log);
        // Same directory again, but NOT per-device unlike every store above -
        // input pacing is a property of this machine and this copy of Elite,
        // identical whichever phone is driving it (ref/docs/macro-timing.md's
        // "Scope: the PC, not the device"). One fixed file for the whole
        // install; MacroTimingSettingsStore takes no device id at all.
        var macroTimingSettingsStore = new MacroTimingSettingsStore(discovery.LunaPanelDirectories.LayoutsDirectory, log);
        // Same directory and shape again, for the tray's "Ports" dialog
        // (LunaPanel.Tray.PortSettingsForm) - server-wide, no device id,
        // same as macroTimingSettingsStore above.
        // RealServerEnvironment.Build() already reads this file once, with
        // its own throwaway no-op log, before this options record even
        // exists (ref/docs' hosting notes) - this instance is the real one,
        // wired into the real diagnostics pipeline, that the tray's Ports
        // dialog saves through.
        var portSettingsStore = new PortSettingsStore(discovery.LunaPanelDirectories.LayoutsDirectory, log);
        // Same directory and shape again, for the tray's About window
        // (LunaPanel.Tray.AboutForm) - the manual Elite/EDHM path fallback.
        // RealServerEnvironment.Build() already reads this file once, with
        // its own throwaway no-op log, before this options record even
        // exists - this instance is the real one, wired into the real
        // diagnostics pipeline, that the About window saves through.
        var pathOverrideStore = new PathOverrideStore(discovery.LunaPanelDirectories.LayoutsDirectory, log);
        // Same directory and shape again, for the tray's Status window
        // remembering which monitor it was last shown on
        // (LunaPanel.Tray.StatusForm) - server-wide, no device id, same as
        // portSettingsStore above.
        var statusWindowPositionStore = new StatusWindowPositionStore(discovery.LunaPanelDirectories.LayoutsDirectory, log);
        // Same directory and shape again, for the tray's "Minimize to system
        // tray" checkbox (LunaPanel.Tray.AboutForm) - server-wide, no device
        // id, same as portSettingsStore above.
        var trayBehaviorStore = new TrayBehaviorStore(discovery.LunaPanelDirectories.LayoutsDirectory, log);
        // Wraps the just-computed discovery result in a holder so "Refresh
        // bindings" (ref/docs/bindings-source.md) can swap in a freshly
        // re-run PathDiscoveryService.Discover pass without restarting the
        // host - see DiscoveryResultHolder's own remarks for why this shape.
        // Only LiveBindingsReader reads through the holder; every other
        // consumer of `discovery` below still sees the frozen startup
        // result, which is an existing, documented limitation this task does
        // not extend to (ref/docs/discovery.md's "Wired at startup").
        var discoveryHolder = new DiscoveryResultHolder(discovery);
        var bindingsReader = new LiveBindingsReader(discoveryHolder, log);
        // Watches the ONE file bindingsReader currently reads through, so a
        // rebind made in Elite while a device's live channel is open reaches
        // it without a restart or a manual "Refresh controls" tap - see
        // BindsFileWatcher's own remarks for exactly what this does and does
        // not cover (switching PRESET is unaffected, same limit
        // LiveBindingsReader already documents). Never throws even when no
        // bindings file was discovered at all; it degrades to raising
        // nothing, same "a layout can't break, only degrade" contract as the
        // reader beside it.
        var bindsWatcher = new BindsFileWatcher(discoveryHolder, options.Clock, log);
        var themeResolver = new LiveThemeResolver(options.DiscoveryEnvironment, discovery, log);
        // Watches the whole ini directory themeResolver reads through for the
        // active EDHM edition, so a colour edit made while a device's live
        // channel is open reaches it without a restart or some unrelated
        // event (switching pages, editing the layout) happening to trigger a
        // re-fetch - see ThemeFileWatcher's own remarks for exactly what this
        // does and does not cover (switching EDITION is unaffected, same
        // limit LiveThemeResolver already documents for a startup-frozen
        // discovery result). Never throws even when no EDHM edition was
        // discovered at all; it degrades to raising nothing, same "a layout
        // can't break, only degrade" contract as themeResolver beside it.
        var themeWatcher = new ThemeFileWatcher(discovery, options.Clock, log);
        // KeyInjectorOverride is null for every production caller - see its
        // own remarks on ServerHostOptions for why the seam exists at all.
        var keyInjector = options.KeyInjectorOverride ?? new Win32KeyInjector(log, () => options.DiagnosticModeEnabled);

        // Latching keys (ref/docs/latching-keys.md) - the one place a key
        // LunaPanel is deliberately holding down is recorded, and the only
        // thing that ever releases one. Constructed here rather than in DI
        // alone because the shutdown hook and the sweeper loop below both
        // need this exact instance.
        var latches = new LatchRegistry(keyInjector, options.Clock, log);

        // Live game state. StatusFileWatcher's constructor requires the
        // directory it watches to already exist (a real FileSystemWatcher
        // cannot be pointed at a missing directory), so this only starts
        // watching when discovery actually found Elite's live-state
        // directory - if it hasn't been created yet (the game has simply
        // never been launched), the store just stays at Current == null,
        // which every consumer already treats as "no live state yet," not
        // an error. See ref/docs/gamestate.md and panel-api.md.
        var gameStateStore = new GameStateStore(options.Clock);
        StatusFileWatcher? statusWatcher = discovery.StatusJson.Directory is not null
            ? new StatusFileWatcher(discovery.StatusJson.Directory, "Status.json", gameStateStore, options.Clock, log)
            : null;

        // The journal lives in the same directory Status.json does, so it is
        // gated on the same discovery result and for the same reason - a real
        // FileSystemWatcher cannot be pointed at a directory that does not
        // exist yet. JournalTailer takes only the directory, never a
        // filename: the journal's name changes every time the game restarts.
        // See ref/docs/gamestate.md's journal section.
        var journalStateStore = new JournalStateStore();
        JournalTailer? journalTailer = discovery.StatusJson.Directory is not null
            ? new JournalTailer(discovery.StatusJson.Directory, journalStateStore, options.Clock, log)
            : null;

        // Tracks which tab the left panel is on (ref/docs/panel-tab-tracking.md)
        // - built entirely from events LunaPanel already watches, never a new
        // discovery of its own. Wired continuously, not just while a macro
        // runs: the commander can open the panel by hand at any time, and a
        // GuiFocus edge this project did not cause is exactly the "we've lost
        // track" signal the tracker exists to catch (LC3).
        // Fix 7, 2026-09-07: the right panel gained the same edge-staleness
        // watcher as the left, plus a shared focus-belief update
        // (PanelTabTracker.RecordObservedFocus) so PanelTabPressEffect knows
        // which panel a shared CycleNextPanel/CyclePreviousPanel press should
        // apply to - see that type's remarks, "Which panel is focused".
        // GuiFocus:InternalPanel (1) is the right panel, GuiFocus:ExternalPanel
        // (2) the left, per StatusVocabulary - both mutually exclusive with
        // each other, so at most one of isLeftOpen/isRightOpen is ever true.
        var panelTabTracker = new PanelTabTracker(options.Clock);
        var lastLeftPanelOpen = gameStateStore.Current?.GuiFocus == StatusVocabulary.GuiFocusValues["ExternalPanel"];
        var lastRightPanelOpen = gameStateStore.Current?.GuiFocus == StatusVocabulary.GuiFocusValues["InternalPanel"];
        var lastDestination = gameStateStore.Current?.Destination;
        panelTabTracker.RecordObservedFocus(
            lastLeftPanelOpen ? PanelSide.Left : lastRightPanelOpen ? PanelSide.Right : null);
        gameStateStore.Changed += snapshot =>
        {
            var isLeftOpen = snapshot?.GuiFocus == StatusVocabulary.GuiFocusValues["ExternalPanel"];
            if (isLeftOpen != lastLeftPanelOpen)
            {
                panelTabTracker.RecordLeftPanelEdge();
            }

            lastLeftPanelOpen = isLeftOpen;

            var isRightOpen = snapshot?.GuiFocus == StatusVocabulary.GuiFocusValues["InternalPanel"];
            if (isRightOpen != lastRightPanelOpen)
            {
                panelTabTracker.RecordRightPanelEdge();
            }

            lastRightPanelOpen = isRightOpen;

            PanelSide? focus = isLeftOpen ? PanelSide.Left : isRightOpen ? PanelSide.Right : null;
            panelTabTracker.RecordObservedFocus(focus);

            // 2026-09-08 (LC20): a Destination change seen while the LEFT
            // panel is open is evidence the commander is on NAVIGATION -
            // the only re-sync available to a commander sitting docked, and
            // the one that would have saved the request-docking run that
            // pressed into the wrong tab on 2026-09-09. Status.json is
            // rewritten on an idle heartbeat as well as on change (LC6), so
            // the comparison against the last observed value is what makes
            // this a CHANGE rather than "a snapshot arrived".
            //
            // Absent-to-present counts as a change; present-to-absent does
            // not. LC20's own first attributed transition is exactly
            // "(absent) -> a value" produced by a real Navigation selection,
            // whereas nothing has ever measured a selection CLEARING the
            // destination - and the panel-open gate already excludes the
            // one absent/present flip that was measured (LC17's boarding).
            var destination = snapshot?.Destination;
            if (destination is not null && destination != lastDestination)
            {
                panelTabTracker.RecordDestinationChange(focus);
            }

            lastDestination = destination;
        };
        journalStateStore.Changed += journalEvent =>
        {
            if (string.Equals(journalEvent.EventName, "FSDJump", StringComparison.OrdinalIgnoreCase))
            {
                panelTabTracker.RecordFsdJump();
            }
            else if (string.Equals(journalEvent.EventName, "Embark", StringComparison.OrdinalIgnoreCase))
            {
                panelTabTracker.RecordOnFootRoundTrip();
            }
            else if (string.Equals(journalEvent.EventName, "LoadGame", StringComparison.OrdinalIgnoreCase))
            {
                // Fix 4, 2026-09-07: seeds the tab tracker at session start,
                // per the commander's confirmation that the left panel is
                // reliably on NAVIGATION when a session begins. Safe from the
                // startup back-scan's own LoadGame for two independent
                // reasons: JournalStateStore.Changed never fires for a
                // back-scanned event at all (isBackScan gating), and this
                // handler is only wired up after JournalTailer's constructor
                // - which runs the back-scan synchronously - has already
                // returned. See PanelTabTracker.RecordLoadGame's remarks.
                panelTabTracker.RecordLoadGame();
            }
        };

        // Every macro this build ships (ref/docs/macros.md), and the one
        // runner that fires any of them - one macro at a time, across every
        // caller, same as keyInjector above being the one choke point every
        // caller's keystrokes pass through.
        // Same directory again, and NOT per-device - for a stronger reason
        // than macro timing's (ref/docs/macro-builder.md, question 2): a
        // re-pair mints a new device id, and layout recovery carries the
        // layout file alone, so per-device macros would leave a recovered
        // layout naming ids whose definitions were left behind - every macro
        // slot rendering UnknownMacro while the recovery looked like it
        // worked.
        var userMacroStore = new UserMacroStore(discovery.LunaPanelDirectories.LayoutsDirectory, log);
        // Every macro this build ships (ref/docs/macros.md) UNION every one
        // the commander authored, and the one runner that fires any of them.
        // LoadShipped is called once because an embedded resource cannot
        // change while the host runs; the user half is re-read per request
        // by MacroCatalogue.All(), because it changes precisely when a
        // commander saves one - closing over a startup list here is what
        // would have meant "your new macro works after you restart
        // LunaPanel".
        var macroCatalogue = new MacroCatalogue(MacroLoader.LoadShipped(), userMacroStore);
        var macroRunner = new MacroRunner(keyInjector, gameStateStore, journalStateStore, panelTabTracker, options.Clock, log, macroTimingSettingsStore);
        // How a macro's outcome gets from the press that started it to the
        // device's live channel, now that the press no longer waits for the
        // run (O28) - see the type's own remarks for why this is the server's
        // and not MacroRunner's.
        var macroPressOutcomes = new MacroPressOutcomeBroadcaster();

        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Listen(options.BindAddress, options.Port);

            // The second listener is the whole of what "this request came
            // from the host machine" means (HostRequest, ref/docs/hosting.md):
            // bound to loopback only, so a packet from anywhere else cannot
            // reach it at all, rather than reaching it and being judged.
            // Never bound when HostAccessPort is 0 - no listener, no host
            // access, which is the deny direction an unconfigured
            // ServerHostOptions gets.
            if (options.HostAccessPort > 0)
            {
                kestrel.Listen(IPAddress.Loopback, options.HostAccessPort);
            }
        });

        // Bridges ASP.NET Core's own internal ILogger output (Kestrel,
        // routing, etc.) into the same diagnostics log this project's own
        // code writes through - see DiagnosticLoggerProvider's own remarks
        // and ref/docs/diagnostics.md's "Why Core has no ILogger".
        builder.Logging.AddProvider(new LunaPanel.Server.Diagnostics.DiagnosticLoggerProvider(log, options.Clock));

        builder.Services.AddSingleton<IDiagnosticLog>(log);
        builder.Services.AddSingleton(ringBuffer);
        builder.Services.AddSingleton(deviceRegistry);
        builder.Services.AddSingleton(discovery);
        builder.Services.AddSingleton(discoveryHolder);
        builder.Services.AddSingleton(options.Redactor);
        builder.Services.AddSingleton(options.Clock);
        builder.Services.AddSingleton(catalogue);
        builder.Services.AddSingleton(layoutStore);
        builder.Services.AddSingleton(themeOverrideStore);
        builder.Services.AddSingleton(panelSettingsStore);
        builder.Services.AddSingleton(macroTimingSettingsStore);
        builder.Services.AddSingleton(portSettingsStore);
        builder.Services.AddSingleton(pathOverrideStore);
        builder.Services.AddSingleton(statusWindowPositionStore);
        builder.Services.AddSingleton(trayBehaviorStore);
        builder.Services.AddSingleton(orphanMarkerStore);
        builder.Services.AddSingleton(bindingsReader);
        builder.Services.AddSingleton(bindsWatcher);
        builder.Services.AddSingleton(themeResolver);
        builder.Services.AddSingleton(themeWatcher);
        builder.Services.AddSingleton(keyInjector);
        builder.Services.AddSingleton(latches);
        builder.Services.AddSingleton(gameStateStore);
        builder.Services.AddSingleton(journalStateStore);
        builder.Services.AddSingleton(panelTabTracker);
        builder.Services.AddSingleton(userMacroStore);
        builder.Services.AddSingleton(macroCatalogue);
        builder.Services.AddSingleton(macroRunner);
        builder.Services.AddSingleton(macroPressOutcomes);

        var app = builder.Build();

        // Tied to the host's own lifetime, not garbage collection: the
        // container never disposes an instance registered via
        // AddSingleton(instance), since it assumes the caller retains
        // ownership - so the watcher (and its real FileSystemWatcher/timers)
        // must be disposed explicitly when the host stops, exactly like the
        // "stopped" log line StatusFileWatcher.Dispose already emits.
        if (statusWatcher is not null)
        {
            app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(statusWatcher.Dispose);
        }

        if (journalTailer is not null)
        {
            app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(journalTailer.Dispose);
        }

        // Unlike the two above, bindsWatcher is always constructed (never
        // conditionally, since its own constructor already degrades cleanly
        // when no bindings file was discovered) - so its disposal is
        // unconditional too.
        app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(bindsWatcher.Dispose);

        // Same reasoning as bindsWatcher immediately above: themeWatcher is
        // always constructed (its own constructor already degrades cleanly
        // when no EDHM edition was discovered), so its disposal is
        // unconditional too.
        app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(themeWatcher.Dispose);

        // Release rule 4 (ref/docs/latching-keys.md): the server shutting
        // down must not leave a key held in the game with nothing left
        // running to release it. Registered on BOTH the clean path
        // (ApplicationStopping, same hook the status watcher already uses)
        // and ProcessExit, which still runs for an Environment.Exit or an
        // unhandled exception tearing the process down - "including on an
        // unclean exit if that can be arranged". LatchRegistry.ReleaseAll is
        // idempotent precisely so both firing is harmless.
        //
        // What this CANNOT cover, stated rather than implied: a hard kill
        // (Task Manager's End Process, TerminateProcess, a power cut) runs
        // no user code at all, so nothing releases the key and Windows is
        // left holding it. That is the residual hole rule 5's backstop does
        // not close either, because the backstop needs this process alive to
        // fire. It is the one case where the commander has to press the key
        // themselves.
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Register(() => latches.ReleaseAll(LatchRelease.ServerShutdown));
        AppDomain.CurrentDomain.ProcessExit += (_, _) => latches.ReleaseAll(LatchRelease.ServerShutdown);

        // Release rules 3 and 5, the two nothing pushes at us. CheckGuard is
        // exactly the verdict a KeyDown would get, without sending one - the
        // same member MacroPresser already uses to refuse a whole macro run
        // up front (ref/docs/injection.md). Only ever called while something
        // is actually latched; see LatchSweeper.
        _ = LatchSweeper.RunAsync(
            latches,
            eliteIsForeground: () => keyInjector.CheckGuard().Outcome == InjectionOutcome.Sent,
            options.Clock,
            LatchDefaults.SweepInterval,
            lifetime.ApplicationStopping);

        app.UseDeviceAuthentication(deviceRegistry, log, options.HostAccessPort);

        // The panel client itself - the page a commander actually taps.
        // Auth-exempt like /api/pair: an unpaired device has no cookie yet,
        // and the pairing screen this page draws for that case has to be
        // reachable before one exists. See PanelClientEndpoint's own remarks.
        app.MapGet(ApiPaths.App, (HttpContext http) =>
        {
            // No-store, deliberately. The page carries the entire client - CSS,
            // markup and script - and it changes on every build. Served with no
            // cache directives at all (as it was until 2026-09-07), a browser
            // applies heuristic caching and may keep rendering an older copy
            // indefinitely: the commander reported a fixed rendering bug as
            // still broken three times while a fetch from the server showed the
            // fix present in the served HTML each time. Nothing was wrong with
            // the fix; their device never received it.
            //
            // There is nothing to gain here - one device, one LAN, a page that
            // is regenerated per request anyway - and the failure mode is
            // invisible from the server side, which is what makes it worth an
            // explicit header rather than a hope.
            http.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            http.Response.Headers.Pragma = "no-cache";

            // The same page either way - the builder is not a second client
            // (ref/docs/macro-builder.md). What changes is whether its
            // authoring affordances are offered at all, decided by where the
            // request came from and never by anything the page asks for
            // later. The server refuses the authoring routes regardless
            // (HostOnlyRoutes); this is the courtesy half.
            var canAuthorMacros = DeviceAuthMiddlewareExtensions.IsHostRequest(http);
            return Results.Content(PanelClientEndpoint.BuildPage(canAuthorMacros), "text/html; charset=utf-8");
        });

        app.MapGet(ApiPaths.Health, (PathDiscoveryResult discoveryResult) =>
            Results.Ok(HealthEndpoint.BuildResponse(discoveryResult)));

        // Calibration surface - see TemplateProbeEndpoint's remarks. Delete
        // this and its three routes once the ladder's comfort thresholds have
        // been confirmed against real devices.
        app.MapGet(ApiPaths.Probe, () =>
            Results.Content(TemplateProbeEndpoint.BuildPage(), "text/html; charset=utf-8"));

        app.MapGet(ApiPaths.ProbeEstimate, (double w, double h) =>
            w <= 0 || h <= 0
                ? Results.BadRequest(new { error = "w and h must be positive" })
                : Results.Ok(TemplateProbeEndpoint.Estimate(w, h)));

        app.MapGet(ApiPaths.ProbeLabels, () =>
            Results.Ok(TemplateProbeEndpoint.CuratedLabels()));

        app.MapGet(ApiPaths.ProbeServiceWorker, () =>
            Results.Content("self.addEventListener('fetch', () => {});", "text/javascript"));

        app.MapPost(ApiPaths.Pair, async (
            HttpContext context,
            DeviceRegistry registry,
            LayoutStore store,
            OrphanMarkerStore orphanMarkers,
            LiveBindingsReader liveBindings,
            IDiagnosticLog diagLog) =>
        {
            PairEndpoint.Request? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<PairEndpoint.Request>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (request is null || string.IsNullOrWhiteSpace(request.Code))
            {
                return Results.Json(new PairEndpoint.ErrorResponse("Invalid request."), statusCode: StatusCodes.Status400BadRequest);
            }

            if (PairEndpoint.TryPair(registry, request, out var token, out var deviceId, out var deviceName))
            {
                context.Response.Cookies.Append(DeviceCookieAuth.CookieName, token!, new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Secure = false,
                    Path = "/",
                    Expires = options.Clock.GetUtcNow().AddDays(365),
                });

                // Layout recovery, at the one moment it is asked for
                // (ref/docs/layout-import.md). Null when nothing on disk is
                // unowned, which is the whole of "a first-ever pairing must
                // not be interrupted by an empty chooser" - there is no
                // empty object for the client to have to recognise.
                var knownActionNames = liveBindings.Read().Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
                var recovery = LayoutImportEndpoint.Recover(
                    store, orphanMarkers, registry.ListDevices(), deviceId!, deviceName, knownActionNames, diagLog);

                // A recovered/imported layout must never be silently
                // overwritten by a fresh seed - only proactively seed when
                // nothing was auto-adopted from an orphan.
                if (recovery is null || recovery.Adopted is null)
                {
                    var deviceClass = DeviceNaming.NormalizeClass(request.DeviceClass);
                    LayoutAccess.SeedForNewDevice(store, deviceId!, deviceClass, knownActionNames, diagLog);
                }

                return Results.Ok(new PairEndpoint.SuccessResponse(true, deviceId!, deviceName, recovery));
            }

            return Results.Json(new PairEndpoint.ErrorResponse("Invalid or expired pairing code."), statusCode: StatusCodes.Status401Unauthorized);
        });

        app.MapGet(ApiPaths.Diagnostics, (DiagnosticRingBuffer ring, PathRedactor redactor) =>
            Results.Ok(DiagnosticsEndpoint.BuildResponse(ring.Snapshot(), redactor)));

        app.MapGet(ApiPaths.Panel, (
            HttpContext context,
            double w,
            double h,
            int? page,
            LayoutStore store,
            CoreCatalogue cat,
            LiveBindingsReader liveBindings,
            LiveThemeResolver liveTheme,
            ThemeOverrideStore themeOverrideStore,
            GameStateStore gameState,
            MacroCatalogue macroCatalogue,
            LatchRegistry latchRegistry,
            MacroRunner macroRunner,
            IDiagnosticLog diagLog) =>
        {
            if (w <= 0 || h <= 0)
            {
                return Results.BadRequest(new { error = "w and h must be positive" });
            }

            var deviceId = (string)context.Items["DeviceId"]!;
            var bindings = liveBindings.Read();
            var knownActionNames = bindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
            var catalogueMerge = CatalogueMerger.Merge(cat, bindings);
            // Shipped plus user macros, re-read now rather than captured at
            // startup - a macro the commander saved a moment ago labels its
            // slot on this very request.
            var macroKnowledge = MacroKnowledgeBuilder.Build(macroCatalogue.All(), bindings);

            var loadResult = LayoutAccess.LoadOrSeed(store, deviceId, knownActionNames, diagLog);
            if (loadResult.Outcome != LayoutLoadOutcome.Loaded)
            {
                diagLog.Warn("Layout", $"GET /api/panel: layout could not be loaded", $"deviceId={deviceId}, outcome={loadResult.Outcome}");
                return Results.Json(new { error = "Your layout could not be loaded." }, statusCode: StatusCodes.Status500InternalServerError);
            }

            var theme = ResolveEffectiveTheme(themeOverrideStore, liveTheme, deviceId, out _);
            var built = PanelEndpoint.BuildResponse(
                loadResult.Layout!, pageIndex: page ?? 0, w, h, cat, catalogueMerge, macroKnowledge, knownActionNames, theme, gameState.Current, diagLog,
                latchRegistry.LatchedActions(deviceId),
                macroRunner.RunningMacroIds);

            if (built.Outcome != PanelEndpoint.BuildOutcome.Ok)
            {
                diagLog.Warn("Layout", $"GET /api/panel: could not build a response", $"deviceId={deviceId}, outcome={built.Outcome}");
                return Results.Json(new { error = "Panel could not be built." }, statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.Ok(built.Response);
        });

        // The template picker, moved into settings (ref/docs/panels-and-pages.md).
        // Authenticated (unlike /probe/estimate) since this is product, not
        // the calibration surface - the same real CellSizeEstimator, just
        // reached from a route a paired device can call.
        app.MapGet(ApiPaths.Templates, (double w, double h) =>
            w <= 0 || h <= 0
                ? Results.BadRequest(new { error = "w and h must be positive" })
                : Results.Ok(TemplateProbeEndpoint.Estimate(w, h)));

        app.MapGet(ApiPaths.PanelSettings, (HttpContext context, PanelSettingsStore settingsStore) =>
        {
            var deviceId = (string)context.Items["DeviceId"]!;
            return Results.Ok(PanelSettingsEndpoint.BuildResponse(settingsStore.Load(deviceId)));
        });

        app.MapPost(ApiPaths.PanelSettings, async (HttpContext context, PanelSettingsStore settingsStore) =>
        {
            PanelSettingsEndpoint.Request? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<PanelSettingsEndpoint.Request>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (request is null)
            {
                return Results.Json(new { error = "Invalid request." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var deviceId = (string)context.Items["DeviceId"]!;
            var settings = new PanelSettings(request.MergeExpand, request.ShowMacroStepResults, request.AutoSwitchEnabled);
            settingsStore.Save(deviceId, settings);
            return Results.Ok(PanelSettingsEndpoint.BuildResponse(settings));
        });

        // The settings gear's Timing pane (ref/docs/macro-timing.md). Unlike
        // every route above, no device id is read here at all -
        // MacroTimingSettingsStore is server-wide, shared by every paired
        // device - though the route itself is still device-authenticated
        // like every other settings route (not in the auth middleware's
        // exempt list).
        app.MapGet(ApiPaths.MacroTiming, (MacroTimingSettingsStore timingStore) =>
            Results.Ok(MacroTimingEndpoint.BuildResponse(timingStore.Load())));

        app.MapPost(ApiPaths.MacroTiming, async (HttpContext context, MacroTimingSettingsStore timingStore) =>
        {
            MacroTimingEndpoint.Request? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<MacroTimingEndpoint.Request>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (!MacroTimingEndpoint.TryParse(request, out var timingSettings, out var error))
            {
                return Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);
            }

            timingStore.Save(timingSettings);
            return Results.Ok(MacroTimingEndpoint.BuildResponse(timingSettings));
        });

        // The macro builder (ref/docs/macro-builder.md), and the macro-list
        // endpoint the action picker has wanted since the editor shipped.
        // Like the timing routes above, no device id is read anywhere here -
        // user macros are machine-wide - though every route is still
        // device-authenticated.
        app.MapGet(ApiPaths.Macros, (MacroCatalogue macroCatalogue, LiveBindingsReader liveBindings) =>
            Results.Ok(MacrosEndpoint.BuildResponse(
                macroCatalogue.All(),
                macroCatalogue.ShippedIds,
                liveBindings.Read(),
                macroCatalogue.UserMacros.LoadAllRecords())));

        app.MapGet(ApiPaths.MacrosVocabulary, () => Results.Ok(MacrosEndpoint.BuildVocabulary()));

        app.MapPost(ApiPaths.Macros, async (HttpContext context, MacroCatalogue macroCatalogue, IDiagnosticLog diagLog) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var rawBody = await reader.ReadToEndAsync();

            // Existing ids are read fresh here rather than trusted from the
            // client, so "update this one" cannot become "create a file
            // named whatever was asked for".
            var existingUserMacroIds = macroCatalogue.UserMacros.LoadAll().Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
            var result = MacrosEndpoint.BuildForSave(rawBody, UserMacroIds.Mint(), existingUserMacroIds);
            return SaveMacro(macroCatalogue, result, diagLog);
        });

        app.MapPost(ApiPaths.MacrosCopy, async (HttpContext context, MacroCatalogue macroCatalogue, IDiagnosticLog diagLog) =>
        {
            MacrosCopyRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<MacrosCopyRequest>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (request is null || string.IsNullOrWhiteSpace(request.Id))
            {
                return Results.Json(new { error = "Invalid request." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var source = macroCatalogue.Find(request.Id);
            if (source is null)
            {
                return Results.Json(new { error = "That macro no longer exists." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var result = MacrosEndpoint.BuildForCopy(source, UserMacroIds.Mint(), request.Name);
            return SaveMacro(macroCatalogue, result, diagLog);
        });

        app.MapPost(ApiPaths.MacrosDelete, async (HttpContext context, MacroCatalogue macroCatalogue, IDiagnosticLog diagLog) =>
        {
            MacrosDeleteRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<MacrosDeleteRequest>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (request is null || !UserMacroIds.IsUserMacroId(request.Id))
            {
                // A shipped id lands here too, and is refused for the same
                // reason an edit of one is: shipped macros are read-only.
                return Results.Json(
                    new { error = "That macro came with LunaPanel and cannot be deleted." },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!macroCatalogue.UserMacros.Delete(request.Id))
            {
                return Results.Json(new { error = "That macro no longer exists." }, statusCode: StatusCodes.Status400BadRequest);
            }

            // Deliberately no reference counting and no cascade: a slot that
            // still names this id renders UnknownMacro and refuses at press
            // time, exactly as the starter layout's slot 0 did for months
            // before request-docking existed (ref/docs/macro-builder.md).
            diagLog.Info("Macro", "Deleted a user macro", $"macroId={request.Id}");
            return Results.Ok(new { deleted = request.Id });
        });

        app.MapPost(ApiPaths.PanelTemplate, async (
            HttpContext context,
            LayoutStore store,
            PanelSettingsStore settingsStore,
            LiveBindingsReader liveBindings,
            IDiagnosticLog diagLog) =>
        {
            PanelTemplateEndpoint.Request? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<PanelTemplateEndpoint.Request>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (request is null || string.IsNullOrWhiteSpace(request.TemplateId))
            {
                return Results.Json(new { error = "Invalid request." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var deviceId = (string)context.Items["DeviceId"]!;
            var bindings = liveBindings.Read();
            var knownActionNames = bindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

            var loadResult = LayoutAccess.LoadOrSeed(store, deviceId, knownActionNames, diagLog);
            if (loadResult.Outcome != LayoutLoadOutcome.Loaded)
            {
                diagLog.Warn("Layout", "POST /api/panel/template: layout could not be loaded", $"deviceId={deviceId}, outcome={loadResult.Outcome}");
                return Results.Json(new { error = "Your layout could not be loaded." }, statusCode: StatusCodes.Status500InternalServerError);
            }

            var mergeExpand = settingsStore.Load(deviceId).MergeExpand;
            var changed = PanelTemplateEndpoint.ChangeTemplate(loadResult.Layout!, request.Page, request.TemplateId, mergeExpand);
            if (changed.Outcome != PanelTemplateEndpoint.ChangeOutcome.Ok)
            {
                return Results.Json(new { error = $"Could not change template: {changed.Outcome}." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var saveResult = store.Save(deviceId, changed.Layout!, knownActionNames);
            if (saveResult.Outcome != LayoutSaveOutcome.Saved)
            {
                // Same tolerant treatment LayoutAccess.LoadOrSeed already
                // gives a seed save that fails validation: ChangeTemplate
                // never introduces a new action name, only reshapes which
                // page an already-existing slot sits on, so a validation
                // failure here reflects the current bindings file not
                // recognizing an action the layout already carried BEFORE
                // this request - an environment condition, not something
                // this request caused. The change is still returned for
                // this response; only the write to disk was skipped, and
                // the next save attempt (from any route) tries again.
                diagLog.Warn(
                    "Layout",
                    "POST /api/panel/template: layout changed but could not be persisted; using it in memory only for this response",
                    $"deviceId={deviceId}, errors={string.Join("; ", saveResult.ValidationErrors)}");
            }

            return Results.Ok(new { ok = true, pageCount = changed.Layout!.Pages.Count });
        });

        // The page bar's own verbs (ref/docs/panels-and-pages.md): add,
        // rename, and set-visibility for a device's pages. Same
        // load-edit-save shape every other layout mutation route already
        // follows.
        app.MapPost(ApiPaths.PageAdd, async (
            HttpContext context,
            LayoutStore store,
            LiveBindingsReader liveBindings,
            IDiagnosticLog diagLog) =>
        {
            PageEditEndpoint.AddPageRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<PageEditEndpoint.AddPageRequest>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (request is null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.TemplateId))
            {
                return Results.Json(new { error = "Invalid request." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var deviceId = (string)context.Items["DeviceId"]!;
            var bindings = liveBindings.Read();
            var knownActionNames = bindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

            var loadResult = LayoutAccess.LoadOrSeed(store, deviceId, knownActionNames, diagLog);
            if (loadResult.Outcome != LayoutLoadOutcome.Loaded)
            {
                diagLog.Warn("Layout", "POST /api/panel/page/add: layout could not be loaded", $"deviceId={deviceId}, outcome={loadResult.Outcome}");
                return Results.Json(new { error = "Your layout could not be loaded." }, statusCode: StatusCodes.Status500InternalServerError);
            }

            var edited = PageEditEndpoint.AddPage(loadResult.Layout!, request.Name, request.TemplateId);
            if (edited.Outcome != PageEditEndpoint.EditOutcome.Ok)
            {
                return Results.Json(new { error = $"Could not add page: {edited.Outcome}." }, statusCode: StatusCodes.Status400BadRequest);
            }

            // A brand new page carries no slots at all, so it can never
            // itself introduce an action name the current bindings file
            // fails to recognize - same tolerant in-memory-only treatment
            // PanelTemplateEndpoint's own reshape gets above.
            var saveResult = store.Save(deviceId, edited.Layout!, knownActionNames);
            if (saveResult.Outcome != LayoutSaveOutcome.Saved)
            {
                diagLog.Warn(
                    "Layout",
                    "POST /api/panel/page/add: layout changed but could not be persisted; using it in memory only for this response",
                    $"deviceId={deviceId}, errors={string.Join("; ", saveResult.ValidationErrors)}");
            }

            return Results.Ok(new { ok = true, pageIndex = edited.Layout!.Pages.Count - 1, pageCount = edited.Layout.Pages.Count });
        });

        app.MapPost(ApiPaths.PageRename, async (
            HttpContext context,
            LayoutStore store,
            LiveBindingsReader liveBindings,
            IDiagnosticLog diagLog) =>
        {
            PageEditEndpoint.RenamePageRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<PageEditEndpoint.RenamePageRequest>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (request is null || string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.Json(new { error = "Invalid request." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var deviceId = (string)context.Items["DeviceId"]!;
            var bindings = liveBindings.Read();
            var knownActionNames = bindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

            var loadResult = LayoutAccess.LoadOrSeed(store, deviceId, knownActionNames, diagLog);
            if (loadResult.Outcome != LayoutLoadOutcome.Loaded)
            {
                diagLog.Warn("Layout", "POST /api/panel/page/rename: layout could not be loaded", $"deviceId={deviceId}, outcome={loadResult.Outcome}");
                return Results.Json(new { error = "Your layout could not be loaded." }, statusCode: StatusCodes.Status500InternalServerError);
            }

            var edited = PageEditEndpoint.RenamePage(loadResult.Layout!, request.Page, request.Name);
            if (edited.Outcome != PageEditEndpoint.EditOutcome.Ok)
            {
                return Results.Json(new { error = $"Could not rename page: {edited.Outcome}." }, statusCode: StatusCodes.Status400BadRequest);
            }

            // A rename never introduces a new action name - same tolerant
            // treatment SlotLabel's own rename gets above.
            var saveResult = store.Save(deviceId, edited.Layout!, knownActionNames);
            if (saveResult.Outcome != LayoutSaveOutcome.Saved)
            {
                diagLog.Warn(
                    "Layout",
                    "POST /api/panel/page/rename: layout changed but could not be persisted; using it in memory only for this response",
                    $"deviceId={deviceId}, errors={string.Join("; ", saveResult.ValidationErrors)}");
            }

            return Results.Ok(new { ok = true });
        });

        app.MapPost(ApiPaths.PageShowWhen, async (
            HttpContext context,
            LayoutStore store,
            LiveBindingsReader liveBindings,
            IDiagnosticLog diagLog) =>
        {
            PageEditEndpoint.SetShowWhenRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<PageEditEndpoint.SetShowWhenRequest>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (request is null)
            {
                return Results.Json(new { error = "Invalid request." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var deviceId = (string)context.Items["DeviceId"]!;
            var bindings = liveBindings.Read();
            var knownActionNames = bindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

            var loadResult = LayoutAccess.LoadOrSeed(store, deviceId, knownActionNames, diagLog);
            if (loadResult.Outcome != LayoutLoadOutcome.Loaded)
            {
                diagLog.Warn("Layout", "POST /api/panel/page/showwhen: layout could not be loaded", $"deviceId={deviceId}, outcome={loadResult.Outcome}");
                return Results.Json(new { error = "Your layout could not be loaded." }, statusCode: StatusCodes.Status500InternalServerError);
            }

            var edited = PageEditEndpoint.SetShowWhen(loadResult.Layout!, request.Page, request.ShowWhen, request.Force);
            if (edited.Outcome == PageEditEndpoint.EditOutcome.ShowWhenConflict)
            {
                return Results.Ok(new { conflict = new { pageIndex = edited.ConflictPageIndex, pageName = edited.ConflictPageName } });
            }

            if (edited.Outcome != PageEditEndpoint.EditOutcome.Ok)
            {
                var message = edited.Outcome == PageEditEndpoint.EditOutcome.InvalidShowWhen
                    ? $"Could not understand that visibility condition: {edited.Error}"
                    : $"Could not set visibility: {edited.Outcome}.";
                return Results.Json(new { error = message }, statusCode: StatusCodes.Status400BadRequest);
            }

            // Never introduces a new action name - same tolerant treatment
            // SlotLabel/PageRename get above.
            var saveResult = store.Save(deviceId, edited.Layout!, knownActionNames);
            if (saveResult.Outcome != LayoutSaveOutcome.Saved)
            {
                diagLog.Warn(
                    "Layout",
                    "POST /api/panel/page/showwhen: layout changed but could not be persisted; using it in memory only for this response",
                    $"deviceId={deviceId}, errors={string.Join("; ", saveResult.ValidationErrors)}");
            }

            return Results.Ok(new { ok = true });
        });

        app.MapPost(ApiPaths.PageDelete, async (
            HttpContext context,
            LayoutStore store,
            LiveBindingsReader liveBindings,
            IDiagnosticLog diagLog) =>
        {
            PageEditEndpoint.DeletePageRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<PageEditEndpoint.DeletePageRequest>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (request is null)
            {
                return Results.Json(new { error = "Invalid request." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var deviceId = (string)context.Items["DeviceId"]!;
            var bindings = liveBindings.Read();
            var knownActionNames = bindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

            var loadResult = LayoutAccess.LoadOrSeed(store, deviceId, knownActionNames, diagLog);
            if (loadResult.Outcome != LayoutLoadOutcome.Loaded)
            {
                diagLog.Warn("Layout", "POST /api/panel/page/delete: layout could not be loaded", $"deviceId={deviceId}, outcome={loadResult.Outcome}");
                return Results.Json(new { error = "Your layout could not be loaded." }, statusCode: StatusCodes.Status500InternalServerError);
            }

            var edited = PageEditEndpoint.DeletePage(loadResult.Layout!, request.Page);
            if (edited.Outcome != PageEditEndpoint.EditOutcome.Ok)
            {
                return Results.Json(new { error = $"Could not delete page: {edited.Outcome}." }, statusCode: StatusCodes.Status400BadRequest);
            }

            // Removing a page never introduces a new action name - same
            // tolerant treatment PageRename/PageAdd get above.
            var saveResult = store.Save(deviceId, edited.Layout!, knownActionNames);
            if (saveResult.Outcome != LayoutSaveOutcome.Saved)
            {
                diagLog.Warn(
                    "Layout",
                    "POST /api/panel/page/delete: layout changed but could not be persisted; using it in memory only for this response",
                    $"deviceId={deviceId}, errors={string.Join("; ", saveResult.ValidationErrors)}");
            }

            // The client already knows which index it asked to delete and
            // its own currentPage, so it does its own index-shift arithmetic
            // rather than the server guessing what the client was showing -
            // pageCount is the one thing it does not already have.
            return Results.Ok(new { ok = true, pageCount = edited.Layout!.Pages.Count });
        });

        app.MapPost(ApiPaths.PageMove, async (
            HttpContext context,
            LayoutStore store,
            LiveBindingsReader liveBindings,
            IDiagnosticLog diagLog) =>
        {
            PageEditEndpoint.MovePageRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<PageEditEndpoint.MovePageRequest>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (request is null)
            {
                return Results.Json(new { error = "Invalid request." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var deviceId = (string)context.Items["DeviceId"]!;
            var bindings = liveBindings.Read();
            var knownActionNames = bindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

            var loadResult = LayoutAccess.LoadOrSeed(store, deviceId, knownActionNames, diagLog);
            if (loadResult.Outcome != LayoutLoadOutcome.Loaded)
            {
                diagLog.Warn("Layout", "POST /api/panel/page/move: layout could not be loaded", $"deviceId={deviceId}, outcome={loadResult.Outcome}");
                return Results.Json(new { error = "Your layout could not be loaded." }, statusCode: StatusCodes.Status500InternalServerError);
            }

            var edited = PageEditEndpoint.MovePage(loadResult.Layout!, request.From, request.To);
            if (edited.Outcome != PageEditEndpoint.EditOutcome.Ok)
            {
                return Results.Json(new { error = $"Could not move page: {edited.Outcome}." }, statusCode: StatusCodes.Status400BadRequest);
            }

            // A reorder never introduces a new action name - same tolerant
            // treatment every other page verb above gets.
            var saveResult = store.Save(deviceId, edited.Layout!, knownActionNames);
            if (saveResult.Outcome != LayoutSaveOutcome.Saved)
            {
                diagLog.Warn(
                    "Layout",
                    "POST /api/panel/page/move: layout changed but could not be persisted; using it in memory only for this response",
                    $"deviceId={deviceId}, errors={string.Join("; ", saveResult.ValidationErrors)}");
            }

            // Nothing the client doesn't already have - same reasoning as
            // PageDelete's own response, minus pageCount, which a move never
            // changes.
            return Results.Ok(new { ok = true });
        });

        // The on-device editor (ref/docs/editor.md): the action picker's own
        // list, and the assign/rename/long-press/clear verbs its slot sheet
        // commits through. Authenticated like every other panel route - none
        // of these five are in the auth middleware's exempt list.
        app.MapGet(ApiPaths.Actions, (bool? all, CoreCatalogue cat, LiveBindingsReader liveBindings) =>
        {
            var bindings = liveBindings.Read();
            var merge = (all ?? false) ? CatalogueMerger.MergeAll(cat, bindings) : CatalogueMerger.Merge(cat, bindings);
            return Results.Ok(ActionsEndpoint.BuildResponse(merge));
        });

        // "Press a key" (ref/docs/macros.md): the key picker's own list, and
        // the reverse lookup it calls the moment a key is chosen. Both
        // ordinary device routes - reading either changes nothing.
        app.MapGet(ApiPaths.Keys, () => Results.Ok(KeysEndpoint.BuildResponse()));

        app.MapGet(ApiPaths.ActionForKey, (string? domCode, CoreCatalogue cat, LiveBindingsReader liveBindings) =>
        {
            if (string.IsNullOrWhiteSpace(domCode))
            {
                return Results.Json(new { error = "Unrecognized key." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var bindings = liveBindings.Read();
            // MergeAll, not Merge: the matched action may be one the curated
            // catalogue never mentions, and a key genuinely bound to it in
            // Elite must still get a readable label rather than silently
            // falling through - see ActionForKeyEndpoint's own remarks.
            var merge = CatalogueMerger.MergeAll(cat, bindings);
            var response = ActionForKeyEndpoint.BuildResponse(domCode, bindings, merge);
            return response is null
                ? Results.Json(new { error = "Unrecognized key." }, statusCode: StatusCodes.Status400BadRequest)
                : Results.Ok(response);
        });

        app.MapPost(ApiPaths.SlotAssign, async (
            HttpContext context,
            LayoutStore store,
            LiveBindingsReader liveBindings,
            IDiagnosticLog diagLog) =>
        {
            SlotEditEndpoint.AssignRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<SlotEditEndpoint.AssignRequest>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (request is null)
            {
                return Results.Json(new { error = "Invalid request." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var deviceId = (string)context.Items["DeviceId"]!;
            var bindings = liveBindings.Read();
            var knownActionNames = bindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

            var loadResult = LayoutAccess.LoadOrSeed(store, deviceId, knownActionNames, diagLog);
            if (loadResult.Outcome != LayoutLoadOutcome.Loaded)
            {
                diagLog.Warn("Layout", "POST /api/panel/slot/assign: layout could not be loaded", $"deviceId={deviceId}, outcome={loadResult.Outcome}");
                return Results.Json(new { error = "Your layout could not be loaded." }, statusCode: StatusCodes.Status500InternalServerError);
            }

            var edited = SlotEditEndpoint.Assign(loadResult.Layout!, request.Page, request.Slot, request.Action, request.Macro, request.Label);
            if (edited.Outcome != SlotEditEndpoint.EditOutcome.Ok)
            {
                return Results.Json(new { error = $"Could not assign: {edited.Outcome}." }, statusCode: StatusCodes.Status400BadRequest);
            }

            // Unlike PanelTemplateEndpoint's reshaping, an assign can
            // genuinely introduce an action name the current bindings file
            // doesn't recognize - the picker's "show everything" mode
            // deliberately allows assigning an unbound-but-real action, and
            // a stale/absent bindings file could still reject it. That is a
            // real refusal to report here, not something to tolerate
            // in-memory-only the way a template reshape or a seed is.
            var saveResult = store.Save(deviceId, edited.Layout!, knownActionNames);
            if (saveResult.Outcome != LayoutSaveOutcome.Saved)
            {
                return Results.Json(new { error = string.Join("; ", saveResult.ValidationErrors) }, statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Ok(new { ok = true });
        });

        // [2026-09-16] "Make this button a folder" (ref/docs/editor.md).
        // Shaped exactly like SlotAssign above - parse, load, edit, save -
        // and, like it, reports a save-time validation refusal rather than
        // tolerating it in memory: MakeFolder is the ONLY thing that mints a
        // page id, so a layout it produced failing validation means the
        // folder rules were violated, not that the bindings file is stale.
        app.MapPost(ApiPaths.SlotMakeFolder, async (
            HttpContext context,
            LayoutStore store,
            LiveBindingsReader liveBindings,
            IDiagnosticLog diagLog) =>
        {
            SlotEditEndpoint.MakeFolderRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<SlotEditEndpoint.MakeFolderRequest>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            // The empty-name refusal lives here rather than in MakeFolder,
            // the same division PageEditEndpoint.AddPage already documents -
            // a folder's name IS its page's name.
            if (request is null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.TemplateId))
            {
                return Results.Json(new { error = "Invalid request." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var deviceId = (string)context.Items["DeviceId"]!;
            var bindings = liveBindings.Read();
            var knownActionNames = bindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

            var loadResult = LayoutAccess.LoadOrSeed(store, deviceId, knownActionNames, diagLog);
            if (loadResult.Outcome != LayoutLoadOutcome.Loaded)
            {
                diagLog.Warn("Layout", "POST /api/panel/slot/makefolder: layout could not be loaded", $"deviceId={deviceId}, outcome={loadResult.Outcome}");
                return Results.Json(new { error = "Your layout could not be loaded." }, statusCode: StatusCodes.Status500InternalServerError);
            }

            var edited = SlotEditEndpoint.MakeFolder(loadResult.Layout!, request.Page, request.Slot, request.Name, request.TemplateId);
            if (edited.Outcome != SlotEditEndpoint.EditOutcome.Ok)
            {
                return Results.Json(new { error = $"Could not make that a folder: {edited.Outcome}." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var saveResult = store.Save(deviceId, edited.Layout!, knownActionNames);
            if (saveResult.Outcome != LayoutSaveOutcome.Saved)
            {
                return Results.Json(new { error = string.Join("; ", saveResult.ValidationErrors) }, statusCode: StatusCodes.Status400BadRequest);
            }

            // The new interior page's index, so the client can navigate
            // straight into the folder it just made - same courtesy
            // PageAdd's own response gives the "+" flow.
            return Results.Ok(new { ok = true, pageIndex = edited.PageIndex });
        });

        app.MapPost(ApiPaths.SlotLabel, async (
            HttpContext context,
            LayoutStore store,
            LiveBindingsReader liveBindings,
            IDiagnosticLog diagLog) =>
        {
            SlotEditEndpoint.LabelRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<SlotEditEndpoint.LabelRequest>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (request is null)
            {
                return Results.Json(new { error = "Invalid request." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var deviceId = (string)context.Items["DeviceId"]!;
            var bindings = liveBindings.Read();
            var knownActionNames = bindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

            var loadResult = LayoutAccess.LoadOrSeed(store, deviceId, knownActionNames, diagLog);
            if (loadResult.Outcome != LayoutLoadOutcome.Loaded)
            {
                diagLog.Warn("Layout", "POST /api/panel/slot/label: layout could not be loaded", $"deviceId={deviceId}, outcome={loadResult.Outcome}");
                return Results.Json(new { error = "Your layout could not be loaded." }, statusCode: StatusCodes.Status500InternalServerError);
            }

            var edited = SlotEditEndpoint.SetLabel(loadResult.Layout!, request.Page, request.Slot, request.Label);
            if (edited.Outcome != SlotEditEndpoint.EditOutcome.Ok)
            {
                return Results.Json(new { error = $"Could not set label: {edited.Outcome}." }, statusCode: StatusCodes.Status400BadRequest);
            }

            // A rename never introduces a new action name - same tolerant
            // treatment PanelTemplateEndpoint's own wiring gives a reshape
            // that can't itself have caused a validation failure
            // (LayoutAccess.LoadOrSeed's documented behaviour, extended here).
            var saveResult = store.Save(deviceId, edited.Layout!, knownActionNames);
            if (saveResult.Outcome != LayoutSaveOutcome.Saved)
            {
                diagLog.Warn(
                    "Layout",
                    "POST /api/panel/slot/label: layout changed but could not be persisted; using it in memory only for this response",
                    $"deviceId={deviceId}, errors={string.Join("; ", saveResult.ValidationErrors)}");
            }

            return Results.Ok(new { ok = true });
        });

        app.MapPost(ApiPaths.SlotLongPress, async (
            HttpContext context,
            LayoutStore store,
            LiveBindingsReader liveBindings,
            IDiagnosticLog diagLog) =>
        {
            SlotEditEndpoint.LongPressRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<SlotEditEndpoint.LongPressRequest>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (request is null)
            {
                return Results.Json(new { error = "Invalid request." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var deviceId = (string)context.Items["DeviceId"]!;
            var bindings = liveBindings.Read();
            var knownActionNames = bindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

            var loadResult = LayoutAccess.LoadOrSeed(store, deviceId, knownActionNames, diagLog);
            if (loadResult.Outcome != LayoutLoadOutcome.Loaded)
            {
                diagLog.Warn("Layout", "POST /api/panel/slot/longpress: layout could not be loaded", $"deviceId={deviceId}, outcome={loadResult.Outcome}");
                return Results.Json(new { error = "Your layout could not be loaded." }, statusCode: StatusCodes.Status500InternalServerError);
            }

            var edited = SlotEditEndpoint.SetLongPress(loadResult.Layout!, request.Page, request.Slot, request.Action, request.Macro);
            if (edited.Outcome != SlotEditEndpoint.EditOutcome.Ok)
            {
                return Results.Json(new { error = $"Could not set long-press action: {edited.Outcome}." }, statusCode: StatusCodes.Status400BadRequest);
            }

            // Like assign, a long-press CAN genuinely introduce an action
            // name the current bindings file doesn't recognize (the
            // picker's "show everything" mode) - a real refusal to report,
            // not tolerated in-memory-only the way SlotLabel/SlotClear's
            // reshapes are.
            var saveResult = store.Save(deviceId, edited.Layout!, knownActionNames);
            if (saveResult.Outcome != LayoutSaveOutcome.Saved)
            {
                return Results.Json(new { error = string.Join("; ", saveResult.ValidationErrors) }, statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Ok(new { ok = true });
        });

        app.MapPost(ApiPaths.SlotLatch, async (
            HttpContext context,
            LayoutStore store,
            LiveBindingsReader liveBindings,
            IDiagnosticLog diagLog) =>
        {
            SlotEditEndpoint.LatchRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<SlotEditEndpoint.LatchRequest>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (request is null)
            {
                return Results.Json(new { error = "Invalid request." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var deviceId = (string)context.Items["DeviceId"]!;
            var bindings = liveBindings.Read();
            var knownActionNames = bindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

            var loadResult = LayoutAccess.LoadOrSeed(store, deviceId, knownActionNames, diagLog);
            if (loadResult.Outcome != LayoutLoadOutcome.Loaded)
            {
                diagLog.Warn("Layout", "POST /api/panel/slot/latch: layout could not be loaded", $"deviceId={deviceId}, outcome={loadResult.Outcome}");
                return Results.Json(new { error = "Your layout could not be loaded." }, statusCode: StatusCodes.Status500InternalServerError);
            }

            var edited = SlotEditEndpoint.SetLatch(loadResult.Layout!, request.Page, request.Slot, request.Latch);
            if (edited.Outcome != SlotEditEndpoint.EditOutcome.Ok)
            {
                var message = edited.Outcome == SlotEditEndpoint.EditOutcome.InvalidRequest
                    ? "Only a single control can be held down - a macro is a sequence of keystrokes, so there is no one key to hold."
                    : $"Could not change latching: {edited.Outcome}.";
                return Results.Json(new { error = message }, statusCode: StatusCodes.Status400BadRequest);
            }

            // Like a rename, this never introduces a new action name - the
            // slot already named whatever it names. Tolerated in memory for
            // this response if the save fails, same as SlotLabel.
            var saveResult = store.Save(deviceId, edited.Layout!, knownActionNames);
            if (saveResult.Outcome != LayoutSaveOutcome.Saved)
            {
                diagLog.Warn(
                    "Layout",
                    "POST /api/panel/slot/latch: layout changed but could not be persisted; using it in memory only for this response",
                    $"deviceId={deviceId}, errors={string.Join("; ", saveResult.ValidationErrors)}");
            }

            return Results.Ok(new { ok = true });
        });

        // [2026-09-12] The hold gesture (ref/docs/latching-keys.md's
        // hold-to-thrust extension) - exact mirror of SlotLatch above,
        // targeting SlotEditEndpoint.SetHold instead of SetLatch.
        app.MapPost(ApiPaths.SlotHold, async (
            HttpContext context,
            LayoutStore store,
            LiveBindingsReader liveBindings,
            IDiagnosticLog diagLog) =>
        {
            SlotEditEndpoint.HoldRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<SlotEditEndpoint.HoldRequest>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (request is null)
            {
                return Results.Json(new { error = "Invalid request." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var deviceId = (string)context.Items["DeviceId"]!;
            var bindings = liveBindings.Read();
            var knownActionNames = bindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

            var loadResult = LayoutAccess.LoadOrSeed(store, deviceId, knownActionNames, diagLog);
            if (loadResult.Outcome != LayoutLoadOutcome.Loaded)
            {
                diagLog.Warn("Layout", "POST /api/panel/slot/hold: layout could not be loaded", $"deviceId={deviceId}, outcome={loadResult.Outcome}");
                return Results.Json(new { error = "Your layout could not be loaded." }, statusCode: StatusCodes.Status500InternalServerError);
            }

            var edited = SlotEditEndpoint.SetHold(loadResult.Layout!, request.Page, request.Slot, request.Hold);
            if (edited.Outcome != SlotEditEndpoint.EditOutcome.Ok)
            {
                var message = edited.Outcome == SlotEditEndpoint.EditOutcome.InvalidRequest
                    ? "Only a single control can be held down - a macro is a sequence of keystrokes, so there is no one key to hold."
                    : $"Could not change holding: {edited.Outcome}.";
                return Results.Json(new { error = message }, statusCode: StatusCodes.Status400BadRequest);
            }

            var saveResult = store.Save(deviceId, edited.Layout!, knownActionNames);
            if (saveResult.Outcome != LayoutSaveOutcome.Saved)
            {
                diagLog.Warn(
                    "Layout",
                    "POST /api/panel/slot/hold: layout changed but could not be persisted; using it in memory only for this response",
                    $"deviceId={deviceId}, errors={string.Join("; ", saveResult.ValidationErrors)}");
            }

            return Results.Ok(new { ok = true });
        });

        // Drag a button to a different slot, in edit mode, on the tablet
        // (ref/docs/editor.md). A swap between two indices on ONE page -
        // see SlotEditEndpoint.Move for why swap rather than push-along,
        // and why there is no cross-page form.
        //
        // There is no edit-mode check here, deliberately: edit mode is
        // client state and the server has never known about it for any of
        // the assign/rename/clear/latch verbs either. Inventing a flag for
        // this one route would be a second convention that proves nothing -
        // a caller that wanted to move a slot without the mode would simply
        // send the flag. What actually keeps a drag from moving a button
        // outside edit mode is that the gesture is only wired to a button
        // when the mode is on (PanelClientEndpoint's layoutGrid).
        app.MapPost(ApiPaths.SlotMove, async (
            HttpContext context,
            LayoutStore store,
            LiveBindingsReader liveBindings,
            IDiagnosticLog diagLog) =>
        {
            SlotEditEndpoint.MoveRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<SlotEditEndpoint.MoveRequest>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (request is null)
            {
                return Results.Json(new { error = "Invalid request." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var deviceId = (string)context.Items["DeviceId"]!;
            var bindings = liveBindings.Read();
            var knownActionNames = bindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

            var loadResult = LayoutAccess.LoadOrSeed(store, deviceId, knownActionNames, diagLog);
            if (loadResult.Outcome != LayoutLoadOutcome.Loaded)
            {
                diagLog.Warn("Layout", "POST /api/panel/slot/move: layout could not be loaded", $"deviceId={deviceId}, outcome={loadResult.Outcome}");
                return Results.Json(new { error = "Your layout could not be loaded." }, statusCode: StatusCodes.Status500InternalServerError);
            }

            var edited = SlotEditEndpoint.Move(loadResult.Layout!, request.Page, request.From, request.To);
            if (edited.Outcome != SlotEditEndpoint.EditOutcome.Ok)
            {
                return Results.Json(new { error = $"Could not move that button: {edited.Outcome}." }, statusCode: StatusCodes.Status400BadRequest);
            }

            // Like a rename or a latch, and unlike an assign, a move can
            // never introduce an action name the bindings file doesn't
            // recognize - both slots already named whatever they name. A
            // save refusal here is therefore something this edit cannot
            // have caused, so it is tolerated in memory for this response
            // and warned about, exactly as SlotLabel/SlotLatch do.
            var saveResult = store.Save(deviceId, edited.Layout!, knownActionNames);
            if (saveResult.Outcome != LayoutSaveOutcome.Saved)
            {
                diagLog.Warn(
                    "Layout",
                    "POST /api/panel/slot/move: layout changed but could not be persisted; using it in memory only for this response",
                    $"deviceId={deviceId}, errors={string.Join("; ", saveResult.ValidationErrors)}");
            }

            return Results.Ok(new { ok = true });
        });

        app.MapPost(ApiPaths.SlotClear, async (
            HttpContext context,
            LayoutStore store,
            LiveBindingsReader liveBindings,
            IDiagnosticLog diagLog) =>
        {
            SlotEditEndpoint.ClearRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<SlotEditEndpoint.ClearRequest>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (request is null)
            {
                return Results.Json(new { error = "Invalid request." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var deviceId = (string)context.Items["DeviceId"]!;
            var bindings = liveBindings.Read();
            var knownActionNames = bindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

            var loadResult = LayoutAccess.LoadOrSeed(store, deviceId, knownActionNames, diagLog);
            if (loadResult.Outcome != LayoutLoadOutcome.Loaded)
            {
                diagLog.Warn("Layout", "POST /api/panel/slot/clear: layout could not be loaded", $"deviceId={deviceId}, outcome={loadResult.Outcome}");
                return Results.Json(new { error = "Your layout could not be loaded." }, statusCode: StatusCodes.Status500InternalServerError);
            }

            var edited = SlotEditEndpoint.Clear(loadResult.Layout!, request.Page, request.Slot);
            if (edited.Outcome != SlotEditEndpoint.EditOutcome.Ok)
            {
                return Results.Json(new { error = $"Could not clear: {edited.Outcome}." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var saveResult = store.Save(deviceId, edited.Layout!, knownActionNames);
            if (saveResult.Outcome != LayoutSaveOutcome.Saved)
            {
                diagLog.Warn(
                    "Layout",
                    "POST /api/panel/slot/clear: layout changed but could not be persisted; using it in memory only for this response",
                    $"deviceId={deviceId}, errors={string.Join("; ", saveResult.ValidationErrors)}");
            }

            return Results.Ok(new { ok = true });
        });

        // The settings gear's colour pane (ref/docs/theme.md's colour
        // chain). All three routes require device auth like every other
        // per-device route - only the page shell itself is exempt.
        app.MapGet(ApiPaths.Theme, (HttpContext context, ThemeOverrideStore themeOverrideStore, LiveThemeResolver liveTheme) =>
        {
            var deviceId = (string)context.Items["DeviceId"]!;
            var theme = ResolveEffectiveTheme(themeOverrideStore, liveTheme, deviceId, out var activeOverride);
            return Results.Ok(ThemeEndpoint.BuildStatusResponse(
                theme,
                overrideActive: activeOverride is not null,
                backgroundChosen: activeOverride?.Background is not null,
                liveTheme.GetDiscoveredColours()));
        });

        app.MapPost(ApiPaths.Theme, async (HttpContext context, ThemeOverrideStore themeOverrideStore, LiveThemeResolver liveTheme, IDiagnosticLog diagLog) =>
        {
            ThemeEndpoint.OverrideRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<ThemeEndpoint.OverrideRequest>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (!ThemeEndpoint.TryParseOverride(request, out var overrideValue, out var error))
            {
                return Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);
            }

            var deviceId = (string)context.Items["DeviceId"]!;
            themeOverrideStore.Save(deviceId, overrideValue);
            var theme = HudThemeResolver.FromOverride(overrideValue);
            return Results.Ok(ThemeEndpoint.BuildStatusResponse(
                theme,
                overrideActive: true,
                backgroundChosen: overrideValue.Background is not null,
                liveTheme.GetDiscoveredColours()));
        });

        app.MapPost(ApiPaths.ThemeReset, (HttpContext context, ThemeOverrideStore themeOverrideStore, LiveThemeResolver liveTheme) =>
        {
            var deviceId = (string)context.Items["DeviceId"]!;
            themeOverrideStore.Clear(deviceId);
            var theme = liveTheme.Resolve();
            return Results.Ok(ThemeEndpoint.BuildStatusResponse(
                theme,
                overrideActive: false,
                backgroundChosen: false,
                liveTheme.GetDiscoveredColours()));
        });

        // The settings gear's Devices pane (ref/docs/pairing-and-devices.md).
        // All three device-authenticated like every other settings route -
        // never exempt, unlike the page shell itself.
        app.MapGet(ApiPaths.Devices, (HttpContext context, DeviceRegistry registry) =>
        {
            var deviceId = (string)context.Items["DeviceId"]!;
            return Results.Ok(DevicesEndpoint.BuildListResponse(registry.ListDevices(), deviceId));
        });

        app.MapPost(ApiPaths.DevicesForget, async (HttpContext context, DeviceRegistry registry, OrphanMarkerStore orphanMarkers) =>
        {
            DevicesEndpoint.ForgetRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<DevicesEndpoint.ForgetRequest>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            var callerDeviceId = (string)context.Items["DeviceId"]!;
            if (!DevicesEndpoint.TryForget(registry, orphanMarkers, callerDeviceId, request, out var error))
            {
                return Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Ok(new { ok = true });
        });

        // "Add another device" - the authenticated route
        // ref/docs/pairing-and-devices.md names as the one that gets used:
        // only a paired device may open pairing for another. No request body
        // is needed; the caller's own device auth IS the authorization.
        app.MapPost(ApiPaths.DevicesOpenPairing, (DeviceRegistry registry) =>
            Results.Ok(DevicesEndpoint.OpenPairing(registry)));

        // Layout import from settings (ref/docs/layout-import.md) - the same
        // list and the same copy the end of a successful pair already
        // offers, reachable at any time. Authenticated like every other
        // settings route.
        app.MapGet(ApiPaths.LayoutImport, (LayoutStore store, OrphanMarkerStore orphanMarkers, DeviceRegistry registry) =>
            Results.Ok(LayoutImportEndpoint.BuildListResponse(
                LayoutImportEndpoint.Candidates(store, orphanMarkers, registry.ListDevices()))));

        app.MapPost(ApiPaths.LayoutImport, async (
            HttpContext context,
            LayoutStore store,
            OrphanMarkerStore orphanMarkers,
            DeviceRegistry registry,
            LiveBindingsReader liveBindings,
            IDiagnosticLog diagLog) =>
        {
            LayoutImportEndpoint.ImportRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<LayoutImportEndpoint.ImportRequest>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            var deviceId = (string)context.Items["DeviceId"]!;
            var knownActionNames = liveBindings.Read().Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
            var candidates = LayoutImportEndpoint.Candidates(store, orphanMarkers, registry.ListDevices());

            var outcome = LayoutImportEndpoint.Import(
                store, deviceId, request?.DeviceId, candidates, knownActionNames, diagLog);

            return outcome == LayoutImportEndpoint.ImportOutcome.Imported
                ? Results.Ok(new { ok = true })
                : Results.Json(new { error = $"Could not import that layout: {outcome}." }, statusCode: StatusCodes.Status400BadRequest);
        });

        // Permanently deletes one orphan's layout file and marker - the
        // delete half of the chooser above. Same validation as the import
        // POST (recompute Candidates, refuse anything not on it), so a
        // live-paired device or the host's own layout can never be reached
        // through this route.
        app.MapPost(ApiPaths.LayoutImportDiscard, async (
            HttpContext context,
            LayoutStore store,
            OrphanMarkerStore orphanMarkers,
            DeviceRegistry registry) =>
        {
            LayoutImportEndpoint.ImportRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<LayoutImportEndpoint.ImportRequest>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            var outcome = LayoutImportEndpoint.Discard(store, orphanMarkers, registry.ListDevices(), request?.DeviceId);

            return outcome == LayoutImportEndpoint.DiscardOutcome.Discarded
                ? Results.Ok(new { ok = true })
                : Results.Json(new { error = $"Could not delete that layout: {outcome}." }, statusCode: StatusCodes.Status400BadRequest);
        });

        // Puts back whatever the last import displaced - LayoutStore's own
        // single kept .bak generation, not a second history mechanism.
        app.MapPost(ApiPaths.LayoutImportUndo, (HttpContext context, LayoutStore store) =>
        {
            var deviceId = (string)context.Items["DeviceId"]!;
            return LayoutImportEndpoint.Undo(store, deviceId)
                ? Results.Ok(new { ok = true })
                : Results.Json(new { error = "There is nothing to undo." }, statusCode: StatusCodes.Status400BadRequest);
        });

        // Reset this device's buttons to the shipped starter
        // (ref/docs/reset-to-default.md) - the same LayoutStore.Save path
        // every other layout write uses, so LayoutImportUndo above is also
        // this route's undo; there is no second undo route for it.
        app.MapPost(ApiPaths.LayoutReset, (HttpContext context, LayoutStore store, DeviceRegistry registry, LiveBindingsReader liveBindings, IDiagnosticLog diagLog) =>
        {
            var deviceId = (string)context.Items["DeviceId"]!;
            var knownActionNames = liveBindings.Read().Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
            var deviceClass = registry.ListDevices().FirstOrDefault(d => d.DeviceId == deviceId)?.DeviceClass ?? "unknown";

            return LayoutResetEndpoint.Reset(store, deviceId, deviceClass, knownActionNames, diagLog) == LayoutResetEndpoint.ResetOutcome.Reset
                ? Results.Ok(new { ok = true })
                : Results.Json(new { error = "Could not reset this device's buttons." }, statusCode: StatusCodes.Status400BadRequest);
        });

        // Import and export as files (ref/docs/transfer.md). Host-only in
        // BOTH directions - the middleware refuses every one of these to a
        // paired device, GET included, which is wider than the macro
        // authoring line and is the point.
        app.MapGet(ApiPaths.TransferTargets, (LayoutStore store, OrphanMarkerStore orphanMarkers, DeviceRegistry registry) =>
            Results.Ok(new TransferEndpoint.TargetsResponse(
                TransferEndpoint.Targets(store, orphanMarkers, registry.ListDevices()))));

        app.MapGet(ApiPaths.TransferProfile, (
            string? deviceId,
            LayoutStore store,
            OrphanMarkerStore orphanMarkers,
            DeviceRegistry registry,
            MacroCatalogue macroCatalogue) =>
        {
            var targets = TransferEndpoint.Targets(store, orphanMarkers, registry.ListDevices());
            var result = TransferEndpoint.ExportProfile(store, macroCatalogue, targets, deviceId, DateTimeOffset.UtcNow);
            return DownloadOrError(result);
        });

        app.MapGet(ApiPaths.TransferMacro, (string? id, MacroCatalogue macroCatalogue) =>
            DownloadOrError(TransferEndpoint.ExportMacros(macroCatalogue, id, DateTimeOffset.UtcNow)));

        app.MapPost(ApiPaths.TransferProfile, async (
            HttpContext context,
            string? deviceId,
            LayoutStore store,
            OrphanMarkerStore orphanMarkers,
            DeviceRegistry registry,
            MacroCatalogue macroCatalogue,
            LiveBindingsReader liveBindings,
            IDiagnosticLog diagLog) =>
        {
            // The body is the file, byte for byte, rather than a JSON
            // envelope carrying it as a string - the same "read the raw
            // body" shape POST /api/macros already uses, and the reason a
            // file never picks up a second layer of escaping on its way in.
            using var reader = new StreamReader(context.Request.Body);
            var fileText = await reader.ReadToEndAsync();

            var targets = TransferEndpoint.Targets(store, orphanMarkers, registry.ListDevices());
            var knownActionNames = liveBindings.Read().Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

            var result = TransferEndpoint.ImportProfile(
                store, macroCatalogue, targets, deviceId, fileText, knownActionNames, diagLog);

            return ImportedOrError(result);
        });

        app.MapPost(ApiPaths.TransferMacro, async (HttpContext context, MacroCatalogue macroCatalogue, IDiagnosticLog diagLog) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var fileText = await reader.ReadToEndAsync();

            return ImportedOrError(TransferEndpoint.ImportMacros(macroCatalogue, fileText, diagLog));
        });

        app.MapPost(ApiPaths.TransferUndo, (
            string? deviceId,
            LayoutStore store,
            OrphanMarkerStore orphanMarkers,
            DeviceRegistry registry) =>
        {
            var targets = TransferEndpoint.Targets(store, orphanMarkers, registry.ListDevices());
            return TransferEndpoint.Undo(store, targets, deviceId)
                ? Results.Ok(new { ok = true })
                : Results.Json(new { error = "There is nothing to undo." }, statusCode: StatusCodes.Status400BadRequest);
        });

        // The settings gear's Bindings pane (ref/docs/bindings-source.md):
        // reports which preset is actually in effect and re-runs discovery
        // on demand. Device-authenticated like every other settings route -
        // never exempt, unlike the page shell itself.
        app.MapGet(ApiPaths.BindingsStatus, (DiscoveryResultHolder discoveryHolder, LiveBindingsReader liveBindings) =>
        {
            var bindings = liveBindings.Read();
            var selection = discoveryHolder.Current.BindingsSelection;
            return Results.Ok(BindingsStatusEndpoint.BuildResponse(selection, bindings));
        });

        app.MapPost(ApiPaths.BindingsRefresh, (DiscoveryResultHolder discoveryHolder, LiveBindingsReader liveBindings) =>
        {
            // Re-runs the WHOLE discovery sweep against the same real
            // environment the host started with, not just a re-scan of the
            // bindings folder alone - a newly-created preset could equally
            // depend on a newly-appeared Elite install (for ControlSchemes)
            // as on a new file in Options\Bindings itself. See
            // DiscoveryResultHolder's own remarks for why swapping the
            // reference here is safe against a panel request arriving
            // concurrently.
            var fresh = PathDiscoveryService.Discover(options.DiscoveryEnvironment, log);
            discoveryHolder.Replace(fresh);

            var bindings = liveBindings.Read();
            var selection = discoveryHolder.Current.BindingsSelection;
            return Results.Ok(BindingsStatusEndpoint.BuildResponse(selection, bindings));
        });

        // Live update channel: server-sent events pushing lit-state and
        // coarse connection-state ("is Elite running at all") changes to a
        // paired device. See ref/docs/panel-api.md.
        app.MapGet(ApiPaths.PanelLive, async (
            HttpContext context,
            int? page,
            LayoutStore store,
            CoreCatalogue cat,
            LiveBindingsReader liveBindings,
            GameStateStore gameState,
            JournalStateStore journalState,
            LatchRegistry latchRegistry,
            MacroRunner macroRunner,
            MacroPressOutcomeBroadcaster macroOutcomes,
            MacroTimingSettingsStore timingStore,
            BindsFileWatcher bindsWatcher,
            ThemeFileWatcher themeWatcher,
            PanelSettingsStore panelSettingsStore,
            IDiagnosticLog diagLog,
            CancellationToken ct) =>
        {
            var deviceId = (string)context.Items["DeviceId"]!;
            var bindings = liveBindings.Read();
            var knownActionNames = bindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

            var loadResult = LayoutAccess.LoadOrSeed(store, deviceId, knownActionNames, diagLog);
            if (loadResult.Outcome != LayoutLoadOutcome.Loaded)
            {
                diagLog.Warn("Layout", "GET /api/panel/live: layout could not be loaded", $"deviceId={deviceId}, outcome={loadResult.Outcome}");
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(new { error = "Your layout could not be loaded." }, ct);
                return;
            }

            // [2026-09-10 - SUPERSEDED. This used to read: "The layout
            // loaded for this connection is used for its whole lifetime,
            // never re-read per push - no editor exists yet that could
            // change it mid-connection... If an editor is added later, an
            // already-open live connection would show stale slot->action
            // mappings until the client reconnects - an acceptable,
            // documented limit for this dispatch." The editor was added, and
            // that limit stopped being acceptable the moment a slot's own
            // LIT state started depending on the layout rather than only on
            // the game: a slot latched (or reassigned) mid-connection went on
            // being drawn from the arrangement the stream opened with, so the
            // commander's latched button never lit until they reloaded the
            // whole page. See ref/docs/lit-state.md's "The glow that never
            // arrived".]
            //
            // Still not re-read per push - that really would be pure waste at
            // several file reads a second. It is re-read on exactly the event
            // that can invalidate it: this device's own layout being written,
            // which LayoutStore.Saved raises from the one place any route can
            // write one.
            var liveLayout = loadResult.Layout!;
            var livePageIndex = page ?? 0;

            // Per-device auto-switch toggle (ref/docs/vessel-context.md). Read
            // once at connection-open time, same as liveLayout/livePageIndex -
            // a toggle flipped mid-connection takes effect on the next
            // reconnect, consistent with AutoPageSwitcher already being "one
            // instance per connection."
            var autoSwitchEnabled = panelSettingsStore.Load(deviceId).AutoSwitchEnabled;
            if (!autoSwitchEnabled)
            {
                // Once per connection, not per push - the gate itself is only
                // read once here (see the comment above), so this can never
                // spam. Added 2026-09-19: without this line, a device with
                // the toggle off produces IDENTICAL log silence to a device
                // where nothing happened to switch to, which is exactly the
                // ambiguity that made a real "why didn't it switch" question
                // undiagnosable from the log alone.
                diagLog.Info("Layout", "Auto-switch is off for this device - vessel-context changes will not move its page", $"deviceId={deviceId}");
            }

            PanelLiveEndpoint.BuildResult Build(StatusSnapshot? snapshot) =>
                PanelLiveEndpoint.BuildState(liveLayout, livePageIndex, cat, snapshot, latchRegistry.LatchedActions(deviceId), macroRunner.RunningMacroIds);

            var initialBuild = Build(gameState.Current);
            if (initialBuild.Outcome != PanelLiveEndpoint.BuildOutcome.Ok)
            {
                diagLog.Warn("Layout", "GET /api/panel/live: could not build live state", $"deviceId={deviceId}, outcome={initialBuild.Outcome}");
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(new { error = "Panel could not be built." }, ct);
                return;
            }

            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";

            // Every write below goes through this single channel-driven loop
            // - never directly from OnChanged, which can run on whatever
            // background thread StatusFileWatcher's read cycle happens to be
            // on. HttpResponse.Body is not safe for concurrent writers, so
            // the channel is what serializes every push onto the one async
            // flow already doing the initial write.
            var updates = Channel.CreateUnbounded<PanelLiveEndpoint.LiveState>();

            // Automatic vessel-context page switching (ref/docs/vessel-context.md).
            // ONE SWITCHER PER CONNECTION, seeded with the context this device
            // opened in - which is what makes "fires on a change, then gets
            // out of the way" true per device rather than globally: a device
            // connecting mid-session is not dragged anywhere, and a device
            // that switched (automatically or by hand) reconnects for its new
            // page and starts again from where it actually is.
            var switcher = new AutoPageSwitcher(
                VesselContextResolver.Resolve(gameState.Current, journalState.CurrentVesselType));

            void Push(StatusSnapshot? snapshot, bool layoutChanged = false)
            {
                var built = Build(snapshot);
                if (built.Outcome != PanelLiveEndpoint.BuildOutcome.Ok)
                {
                    return;
                }

                var switchTo = autoSwitchEnabled
                    ? switcher.Decide(liveLayout, livePageIndex, snapshot, journalState.CurrentVesselType, diagLog)
                    : null;
                var state = built.State!;
                if (switchTo is not null)
                {
                    state = state with { SwitchToPage = switchTo };
                }

                if (layoutChanged)
                {
                    state = state with { LayoutChanged = true };
                }

                updates.Writer.TryWrite(state);
            }

            void OnChanged(StatusSnapshot? snapshot) => Push(snapshot);

            // The journal is the other half of the context (the flags say
            // whether, the journal says which), so a DockSRV naming a
            // different SRV has to reach the switcher too - Status.json says
            // InSrv either way and would never fire on its own.
            void OnJournalChanged(JournalEvent _) => Push(gameState.Current);

            // A latch changing is not a game-state change, so nothing else
            // here would ever push it - a latched button would light up only
            // on the next unrelated Status.json tick, which on a quiet ship
            // is up to ~11.8 seconds of a held key looking un-held
            // (ref/docs/latching-keys.md: "a held key that is invisible on
            // the panel is the same failure as one that cannot be released").
            void OnLatchChanged() => Push(gameState.Current);

            // An edit is not a game-state change either, and it changes
            // what this connection's OWN copy of the layout says - so the
            // reload has to happen before the push, and the push has to
            // happen at all. Another device's save is ignored: a layout file
            // belongs to one device.
            //
            // A reload that fails (the file was just written, so this is
            // very nearly unreachable) leaves the previous layout in place
            // and pushes nothing, rather than tearing down a working stream
            // over a transient read.
            // A macro starting or finishing is not a game-state change either,
            // and a run can be over in well under a second - far inside
            // Status.json's own tick, let alone its ~11.8s idle heartbeat
            // (LC6). Without this the glow would arrive after the macro that
            // caused it had already finished, which is worse than no glow.
            void OnMacroRunChanged() => Push(gameState.Current);

            // A macro-timing change is not a game-state change either, and it
            // is the one push here whose payload is NOT derived from anything
            // this connection is holding: the saved settings arrive as the
            // event's own argument and go straight out. Reading them back
            // from a copy loaded at connect is precisely the staleness that
            // stopped a latched button ever lighting (ref/docs/lit-state.md's
            // "The glow that never arrived"), and it would be invisible here
            // - the value would simply always be the one the device connected
            // with. Deliberately NOT run through the switcher: a timing save
            // says nothing about vessel context, and asking would invite a
            // page switch that no context change caused.
            void OnTimingSaved(MacroTimingSettings saved)
            {
                var built = Build(gameState.Current);
                if (built.Outcome != PanelLiveEndpoint.BuildOutcome.Ok)
                {
                    return;
                }

                updates.Writer.TryWrite(built.State! with { Timing = PanelLiveEndpoint.TimingOf(saved) });
            }

            // A rebind is not a game-state change either, and - like timing -
            // it carries no payload of its own here: BindsFileWatcher.Changed
            // fires with no argument, and this pushes a bare "re-fetch"
            // signal rather than threading bindings content through the live
            // channel (see PanelLiveEndpoint.LiveState.BindingsChanged and
            // BindsFileWatcher's own remarks for why). Deliberately NOT run
            // through the switcher, same as OnTimingSaved: a rebind says
            // nothing about vessel context.
            void OnBindingsChanged()
            {
                var built = Build(gameState.Current);
                if (built.Outcome != PanelLiveEndpoint.BuildOutcome.Ok)
                {
                    return;
                }

                updates.Writer.TryWrite(built.State! with { BindingsChanged = true });
            }

            // An EDHM colour edit is not a game-state change either, and -
            // like a rebind - it carries no payload of its own here:
            // ThemeFileWatcher.Changed fires with no argument, and this
            // pushes a bare "re-fetch" signal rather than threading theme
            // content through the live channel (see
            // PanelLiveEndpoint.LiveState.ThemeChanged and
            // ThemeFileWatcher's own remarks for why). Deliberately NOT run
            // through the switcher, same as OnBindingsChanged: a theme edit
            // says nothing about vessel context.
            void OnThemeChanged()
            {
                var built = Build(gameState.Current);
                if (built.Outcome != PanelLiveEndpoint.BuildOutcome.Ok)
                {
                    return;
                }

                updates.Writer.TryWrite(built.State! with { ThemeChanged = true });
            }

            void OnLayoutSaved(string savedDeviceId)
            {
                if (!string.Equals(savedDeviceId, deviceId, StringComparison.Ordinal))
                {
                    return;
                }

                var reloaded = store.Load(deviceId);
                if (reloaded.Outcome != LayoutLoadOutcome.Loaded)
                {
                    diagLog.Warn(
                        "Layout",
                        "Live channel could not re-read the layout it was just told changed; keeping the previous one",
                        $"deviceId={deviceId}, outcome={reloaded.Outcome}");
                    return;
                }

                liveLayout = reloaded.Layout!;
                Push(gameState.Current, layoutChanged: true);
            }

            // A macro this device started has finished (O28). Addressed, and
            // filtered the same way OnLayoutSaved is: a run another device
            // pressed for is not this device's outcome to show. Like timing,
            // the payload is the event's own argument, not anything this
            // connection cached - and like every other non-game-state push,
            // deliberately not run through the switcher.
            void OnMacroFinished(string finishedDeviceId, string macroId, MacroPresser.MacroPressResult result)
            {
                if (!string.Equals(finishedDeviceId, deviceId, StringComparison.Ordinal))
                {
                    return;
                }

                var built = Build(gameState.Current);
                if (built.Outcome != PanelLiveEndpoint.BuildOutcome.Ok)
                {
                    return;
                }

                updates.Writer.TryWrite(built.State! with { MacroFinished = PanelLiveEndpoint.MacroFinishedOf(macroId, result) });
            }

            // Subscribe before reading Current for the initial payload, so a
            // change landing in between is a harmless duplicate push (deduped
            // below) rather than a silently missed one.
            gameState.Changed += OnChanged;
            journalState.Changed += OnJournalChanged;
            latchRegistry.Changed += OnLatchChanged;
            macroRunner.Changed += OnMacroRunChanged;
            macroOutcomes.Finished += OnMacroFinished;
            store.Saved += OnLayoutSaved;
            timingStore.Saved += OnTimingSaved;
            bindsWatcher.Changed += OnBindingsChanged;
            themeWatcher.Changed += OnThemeChanged;

            // Release rule 2 (ref/docs/latching-keys.md): this connection IS
            // the device's live channel, so a latch made from now on belongs
            // to it, and its ending - screen sleep, wifi loss, the tab
            // closing, or the client reopening the stream on another page -
            // releases exactly what was latched under it.
            var latchChannel = latchRegistry.OpenChannel(deviceId);
            diagLog.Info("Status", "Live channel opened", $"deviceId={deviceId}");
            try
            {
                var lastSent = initialBuild.State!;
                await WriteSseEventAsync(context.Response, lastSent, ct);

                await foreach (var state in updates.Reader.ReadAllAsync(ct))
                {
                    if (!PanelLiveEndpoint.ShouldPush(state, lastSent))
                    {
                        continue;
                    }

                    lastSent = state;
                    await WriteSseEventAsync(context.Response, state, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // The client disconnected (or the host is shutting down) -
                // normal, not an error.
            }
            catch (IOException)
            {
                // The connection dropped mid-write - same treatment.
            }
            finally
            {
                gameState.Changed -= OnChanged;
                journalState.Changed -= OnJournalChanged;
                latchRegistry.Changed -= OnLatchChanged;
                macroRunner.Changed -= OnMacroRunChanged;
                macroOutcomes.Finished -= OnMacroFinished;
                store.Saved -= OnLayoutSaved;
                timingStore.Saved -= OnTimingSaved;
                bindsWatcher.Changed -= OnBindingsChanged;
                themeWatcher.Changed -= OnThemeChanged;
                var releasedByClose = latchRegistry.CloseChannel(latchChannel);
                diagLog.Info(
                    "Status",
                    "Live channel closed",
                    $"deviceId={deviceId}, latchesReleased={releasedByClose}");
            }
        });

        app.MapPost(ApiPaths.Press, async (
            HttpContext context,
            LayoutStore store,
            CoreCatalogue cat,
            LiveBindingsReader liveBindings,
            Win32KeyInjector injector,
            MacroRunner macroRunner,
            MacroPressOutcomeBroadcaster macroOutcomes,
            MacroCatalogue macroCatalogue,
            LatchRegistry latchRegistry,
            PanelTabTracker tabTracker,
            TimeProvider clock,
            IDiagnosticLog diagLog) =>
        {
            PressEndpoint.Request? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<PressEndpoint.Request>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (request is null)
            {
                return Results.Json(new { fired = false, outcome = "Refused", reason = "Invalid request." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var deviceId = (string)context.Items["DeviceId"]!;
            var bindings = liveBindings.Read();
            var knownActionNames = bindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
            var catalogueMerge = CatalogueMerger.Merge(cat, bindings);
            // Read ONCE for this request, and used for both the knowledge
            // build and the definition lookup below - the defensive branch
            // there relies on the two deriving from the same list, which two
            // separate All() calls (each re-reading the directory) would
            // quietly stop guaranteeing.
            var macroDefinitions = macroCatalogue.All();
            var macroKnowledge = MacroKnowledgeBuilder.Build(macroDefinitions, bindings);

            var loadResult = LayoutAccess.LoadOrSeed(store, deviceId, knownActionNames, diagLog);
            if (loadResult.Outcome != LayoutLoadOutcome.Loaded)
            {
                diagLog.Warn("Press", "Refused: layout could not be loaded", $"deviceId={deviceId}, outcome={loadResult.Outcome}");
                return Results.Json(
                    new { fired = false, outcome = "Refused", reason = "Your layout could not be loaded." },
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var evaluation = PressEndpoint.Evaluate(
                loadResult.Layout!, request.Page, request.Slot, bindings, catalogueMerge, macroKnowledge, knownActionNames, request.LongPress);

            if (evaluation.Outcome != PressEndpoint.EvaluationOutcome.CanFire)
            {
                diagLog.Warn(
                    "Press",
                    $"Refused press: page={request.Page} slot={request.Slot} ({evaluation.Outcome})",
                    $"deviceId={deviceId}, reason={evaluation.Reason}");
                return Results.Ok(new { fired = false, outcome = evaluation.Outcome.ToString(), reason = evaluation.Reason });
            }

            if (evaluation.MacroId is not null)
            {
                var macroDefinition = macroDefinitions.FirstOrDefault(m => m.Id == evaluation.MacroId);
                if (macroDefinition is null)
                {
                    // Defensive: the annotation that produced CanFire and
                    // this lookup both derive from the same macroDefinitions
                    // read earlier in this same request, so this should be
                    // unreachable - but stays defensive rather than assuming
                    // that invariant holds forever (same discipline as the
                    // "annotation says Ok but live bindings disagree" branch
                    // ChordPresser's own caller above already guards).
                    diagLog.Warn(
                        "Press",
                        $"Refused: macro '{evaluation.MacroId}' had no definition despite an Ok annotation",
                        $"deviceId={deviceId}, page={request.Page}, slot={request.Slot}");
                    return Results.Json(
                        new { fired = false, outcome = "Refused", reason = "This macro could not be found." },
                        statusCode: StatusCodes.Status500InternalServerError);
                }

                // [2026-09-17, O28] This used to be a single
                // `await MacroPresser.RunAsync(...)` holding the request open
                // for the run's whole duration, wait steps included. It now
                // answers the moment the run has genuinely started; the
                // outcome the response used to carry goes to this device's
                // live channel instead, once the run ends (OnMacroFinished
                // above, via MacroPressOutcomeBroadcaster). The three
                // synchronous outcomes - guard refused, Busy, Cancelled -
                // were never waited for and still come back right here, in
                // exactly the shape they always had.
                var start = MacroPresser.Start(injector, macroRunner, macroDefinition, bindings);
                if (start.Started)
                {
                    diagLog.Info(
                        "Press",
                        $"Macro started: page={request.Page} slot={request.Slot} macro={evaluation.MacroId}",
                        $"deviceId={deviceId}");

                    // Fire-and-observe, not fire-and-forget: the continuation
                    // logs the outcome and publishes it. Never awaited here -
                    // that would be the very block this change removes.
                    _ = ObserveMacroCompletionAsync(
                        start.Completion, macroOutcomes, deviceId, evaluation.MacroId, request.Page, request.Slot, diagLog);

                    // No steps, no failedStepIndex - absent, not null, so a
                    // client cannot mistake this for a finished run.
                    return Results.Ok(new { fired = true, outcome = "Started" });
                }

                var macroResult = start.Immediate!;
                LogMacroOutcome(diagLog, deviceId, evaluation.MacroId, request.Page, request.Slot, macroResult);

                return Results.Ok(new
                {
                    fired = macroResult.Fired,
                    outcome = macroResult.Outcome,
                    reason = macroResult.Reason,
                    failedStepIndex = macroResult.FailedStepIndex,
                    // Kept for the synchronous outcomes so their shape is
                    // unchanged: always empty here (nothing ran), present
                    // rather than absent exactly as before O28. The populated
                    // read-back now travels as the live channel's
                    // macroFinished.steps.
                    steps = macroResult.Steps.Select(s => new
                    {
                        stepIndex = s.StepIndex,
                        stepKind = s.StepKind,
                        outcome = s.Outcome.ToString(),
                        elapsedMs = s.Elapsed.TotalMilliseconds,
                    }),
                });
            }

            // [2026-09-12] The hold gesture (ref/docs/latching-keys.md's
            // hold-to-thrust extension), checked BEFORE the latch branch:
            // completely separate from a tap or a latch toggle, so a
            // mismatched verb (a hold phase against a non-hold slot, or the
            // reverse - which should never happen from the real client, but
            // this handler does not trust it) is refused cleanly rather than
            // falling through to a plain tap or a latch toggle.
            if (request.Hold is not null)
            {
                if (!evaluation.Hold)
                {
                    diagLog.Warn(
                        "Press",
                        $"Refused: hold phase sent for a non-hold slot: page={request.Page} slot={request.Slot}",
                        $"deviceId={deviceId}, hold={request.Hold}");
                    return Results.Ok(new { fired = false, outcome = "Refused", reason = "This button does not use a hold gesture." });
                }

                if (request.Hold == PressEndpoint.HoldPhase.Up)
                {
                    // A release is never guarded - refusing one is exactly
                    // the stuck-modifier defect O8 closed, matching every
                    // other release LatchRegistry sends.
                    latchRegistry.Release(deviceId, evaluation.Action!);
                    diagLog.Info(
                        "Press",
                        $"Hold released: page={request.Page} slot={request.Slot} action={evaluation.Action}",
                        $"deviceId={deviceId}");
                    return Results.Ok(new { fired = true, outcome = "Released", reason = "Released." });
                }

                // Down: checked against the injection guard first, the same
                // CheckGuard call the latch branch below already uses -
                // a refused KeyDown must never leave a button lit over a key
                // that never went down.
                var holdGuard = injector.CheckGuard();
                if (holdGuard.Outcome != InjectionOutcome.Sent)
                {
                    diagLog.Warn(
                        "Press",
                        $"Hold refused: page={request.Page} slot={request.Slot} ({holdGuard.Outcome})",
                        $"deviceId={deviceId}, reason={holdGuard.Reason}");
                    return Results.Ok(new { fired = false, outcome = holdGuard.Outcome.ToString(), reason = holdGuard.Reason });
                }

                latchRegistry.Hold(deviceId, evaluation.Action!, evaluation.Chord!);
                diagLog.Info(
                    "Press",
                    $"Held: page={request.Page} slot={request.Slot} action={evaluation.Action}",
                    $"deviceId={deviceId}");
                return Results.Ok(new { fired = true, outcome = "Held", reason = "Held down while pressed." });
            }

            if (evaluation.Latch)
            {
                // Release rule 1, and the press itself
                // (ref/docs/latching-keys.md). A RELEASE is never guarded -
                // refusing one is exactly the stuck-modifier defect O8
                // closed, and Win32KeyInjector.KeyUp bypasses the foreground
                // guard for the same reason. A LATCH is checked first, with
                // the same CheckGuard call MacroPresser already uses to
                // refuse a whole macro run up front: a refused KeyDown sends
                // nothing, so recording a latch anyway would leave the button
                // lit over a key that never went down, which is the one thing
                // worse than a button that did nothing.
                if (latchRegistry.IsLatched(deviceId, evaluation.Action!))
                {
                    latchRegistry.Toggle(deviceId, evaluation.Action!, evaluation.Chord!);
                    diagLog.Info(
                        "Press",
                        $"Latch released: page={request.Page} slot={request.Slot} action={evaluation.Action}",
                        $"deviceId={deviceId}");
                    return Results.Ok(new { fired = true, outcome = "Released", reason = "Released." });
                }

                var latchGuard = injector.CheckGuard();
                if (latchGuard.Outcome != InjectionOutcome.Sent)
                {
                    diagLog.Warn(
                        "Press",
                        $"Latch refused: page={request.Page} slot={request.Slot} ({latchGuard.Outcome})",
                        $"deviceId={deviceId}, reason={latchGuard.Reason}");
                    return Results.Ok(new { fired = false, outcome = latchGuard.Outcome.ToString(), reason = latchGuard.Reason });
                }

                latchRegistry.Toggle(deviceId, evaluation.Action!, evaluation.Chord!);
                diagLog.Info(
                    "Press",
                    $"Latched: page={request.Page} slot={request.Slot} action={evaluation.Action}",
                    $"deviceId={deviceId}");
                return Results.Ok(new { fired = true, outcome = "Latched", reason = "Held down until you tap it again." });
            }

            var result = await PlainPresser.PressAsync(injector, evaluation.Chord!, evaluation.Action!, tabTracker, clock);
            diagLog.Info(
                "Press",
                $"Pressed: page={request.Page} slot={request.Slot} -> {result.Outcome}",
                $"deviceId={deviceId}, reason={result.Reason}");

            return Results.Ok(new
            {
                fired = result.Outcome == InjectionOutcome.Sent,
                outcome = result.Outcome.ToString(),
                reason = result.Reason,
            });
        });

        return app;
    }

    /// <summary>
    /// The one place both <c>GET /api/panel</c> and <c>GET /api/theme</c>
    /// decide which theme is actually in effect for a device, so the two
    /// endpoints can never disagree about it: a stored manual override
    /// (colour chain step 3) always wins when present; otherwise the
    /// automatic resolution chain (<see cref="LiveThemeResolver"/>) decides,
    /// same as before this feature existed.
    /// </summary>
    /// <param name="activeOverride">
    /// The stored override that won, or <see langword="null"/> when
    /// resolution was automatic. Handed back whole rather than as a bare
    /// "an override is active" flag so the theme route can also report
    /// which roles inside it the commander actually chose - specifically
    /// whether Background was picked or is still derived from Text
    /// (<c>ref/docs/theme.md</c>).
    /// </param>
    private static HudTheme ResolveEffectiveTheme(ThemeOverrideStore themeOverrideStore, LiveThemeResolver liveTheme, string deviceId, out ThemeOverride? activeOverride)
    {
        var overrideResult = themeOverrideStore.Load(deviceId);
        activeOverride = overrideResult.Outcome == ThemeOverrideLoadOutcome.Loaded ? overrideResult.Override : null;
        return activeOverride is not null ? HudThemeResolver.FromOverride(activeOverride) : liveTheme.Resolve();
    }

    /// <param name="Id">The macro to copy - shipped or user; copying is how a shipped one is edited at all.</param>
    /// <param name="Name">What to call the copy, or <see langword="null"/> to keep the source's own name.</param>
    private sealed record MacrosCopyRequest(string? Id, string? Name);

    private sealed record MacrosDeleteRequest(string? Id);

    /// <summary>
    /// The one place a macro save reaches disk, shared by the save and copy
    /// routes so the two can never persist by different rules. A refusal is
    /// mapped to a status code by <em>why</em> it was refused, never
    /// flattened into one: content the grammar cannot read is the client's
    /// fault (400), and so is naming a macro that does not exist.
    ///
    /// [2026-09-12] <paramref name="result"/> carries the copy-to-edit
    /// staleness provenance only when it came from <c>BuildForCopy</c>. An
    /// ordinary save (a fresh macro, or an edit of an existing one) has none
    /// of its own to give - but an edit of an already-copied macro must not
    /// erase provenance it already had on disk, or "copied from X" would
    /// vanish the first time the copy's own steps changed. So a save with no
    /// source of its own carries forward whatever the macro already had.
    /// </summary>
    private static IResult SaveMacro(MacroCatalogue macroCatalogue, MacrosEndpoint.SaveResult result, IDiagnosticLog diagLog)
    {
        if (result.Outcome != MacrosEndpoint.SaveOutcome.Ok)
        {
            // [2026-09-17] Name the offending value when there is one to name
            // (O34) - a refusal with nothing but the reason cannot be
            // diagnosed from the log alone once the commander has moved on.
            var detail = result.AttemptedName is null
                ? result.Error
                : $"{result.Error} (attempted name: \"{result.AttemptedName}\")";
            diagLog.Warn("Macro", $"Refused to save a macro ({result.Outcome})", detail);
            return Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status400BadRequest);
        }

        var sourceMacroId = result.SourceMacroId;
        var sourceStepsHash = result.SourceStepsHash;
        if (sourceMacroId is null)
        {
            var existing = macroCatalogue.UserMacros.LoadRecord(result.Macro!.Id);
            sourceMacroId = existing?.SourceMacroId;
            sourceStepsHash = existing?.SourceStepsHash;
        }

        macroCatalogue.UserMacros.Save(result.Macro!, sourceMacroId, sourceStepsHash);
        return Results.Ok(new { id = result.Macro!.Id, name = result.Macro.Name });
    }

    /// <summary>
    /// An export, as a download rather than as a JSON response body
    /// (<c>ref/docs/transfer.md</c>). <c>Content-Disposition: attachment</c>
    /// with a file name is what makes a browser save the thing instead of
    /// rendering it - which matters because the page triggers this by
    /// navigating, and a navigation that rendered JSON would leave the
    /// commander looking at their layout in a browser tab with the panel
    /// gone.
    ///
    /// The file name is quoted and built by
    /// <c>TransferFile.FileNameFor</c>, which reduces a device name to
    /// characters a header and a filesystem both accept - never the raw
    /// name, which a commander is free to put a quote mark in.
    /// </summary>
    private static IResult DownloadOrError(TransferEndpoint.ExportResult result)
    {
        if (result.Outcome != TransferEndpoint.ExportOutcome.Exported)
        {
            return Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.File(
            System.Text.Encoding.UTF8.GetBytes(result.Json!),
            "application/json",
            result.FileName!);
    }

    private static IResult ImportedOrError(TransferEndpoint.ImportResult result) =>
        result.Outcome == TransferEndpoint.ImportOutcome.Imported
            ? Results.Ok(new
            {
                ok = true,
                macrosAdded = result.MacrosAdded,
                macrosAlreadyPresent = result.MacrosAlreadyPresent,
                referencesToMissingMacros = result.ReferencesToMissingMacros,
            })
            : Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// Matches ASP.NET Core's own default web JSON casing (camelCase) - the
    /// same shape <c>GET /api/panel</c>'s <c>Results.Ok(...)</c> already
    /// produces via the framework's built-in formatter. This endpoint writes
    /// directly to the response body instead of going through that
    /// formatter, so the casing has to be requested explicitly here rather
    /// than inherited for free.
    /// </summary>
    /// <summary>
    /// The log line a macro's final outcome gets, whichever side of the
    /// start boundary it landed on - one function so the synchronous
    /// refusals in the press handler and the asynchronous outcomes in
    /// <see cref="ObserveMacroCompletionAsync"/> read identically in the
    /// diagnostics log.
    /// </summary>
    private static void LogMacroOutcome(IDiagnosticLog diagLog, string deviceId, string macroId, int page, int slot, MacroPresser.MacroPressResult result)
    {
        if (result.Fired)
        {
            diagLog.Info(
                "Press",
                $"Macro fired: page={page} slot={slot} macro={macroId} -> {result.Outcome}",
                $"deviceId={deviceId}");
        }
        else
        {
            diagLog.Warn(
                "Press",
                $"Macro refused: page={page} slot={slot} macro={macroId} ({result.Outcome})",
                $"deviceId={deviceId}, reason={result.Reason}, failedStepIndex={result.FailedStepIndex}");
        }
    }

    /// <summary>
    /// Waits for a started macro run to end, logs its outcome and publishes
    /// it to the device's live channel (O28). Runs detached from the request
    /// that started it - that request has long since answered "Started".
    ///
    /// A run that THROWS (not aborts - an exception escaping the runner,
    /// which its own try/finally already guards against leaving anything
    /// held) used to surface as a bare HTTP 500 with no body, which the
    /// client read as "The button did not fire." Detached, an exception has
    /// nowhere to go but an unobserved-task fault, so it is caught here,
    /// logged in full, and published as a not-fired outcome so the commander
    /// still sees that something went wrong rather than a button that
    /// silently went dark.
    /// </summary>
    private static async Task ObserveMacroCompletionAsync(
        Task<MacroPresser.MacroPressResult> completion,
        MacroPressOutcomeBroadcaster macroOutcomes,
        string deviceId,
        string macroId,
        int page,
        int slot,
        IDiagnosticLog diagLog)
    {
        MacroPresser.MacroPressResult result;
        try
        {
            result = await completion.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            diagLog.Error(
                "Press",
                $"Macro run threw: page={page} slot={slot} macro={macroId}",
                $"deviceId={deviceId}, exception={ex}");
            result = new MacroPresser.MacroPressResult(false, "Faulted", "The macro stopped unexpectedly. Check the diagnostics log.", null, Array.Empty<MacroStepProgress>());
        }

        LogMacroOutcome(diagLog, deviceId, macroId, page, slot, result);
        macroOutcomes.Publish(deviceId, macroId, result);
    }

    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Writes one server-sent-events "data:" line and flushes immediately -
    /// Kestrel does not buffer responses on its own, but an explicit flush is
    /// what actually gets a push to the client promptly rather than waiting
    /// for a later, larger write.
    /// </summary>
    private static async Task WriteSseEventAsync<T>(HttpResponse response, T payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, SseJsonOptions);
        await response.WriteAsync($"data: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
