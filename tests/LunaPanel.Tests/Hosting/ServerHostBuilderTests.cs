using System.Net;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.Json;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.GameState;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Macros;
using LunaPanel.Core.Pairing;
using LunaPanel.Server.Bindings;
using LunaPanel.Server.Discovery;
using LunaPanel.Server.Hosting;
using LunaPanel.Server.Http;
using LunaPanel.Server.Input;
using LunaPanel.Server.Layouts;
using LunaPanel.Tests.Discovery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace LunaPanel.Tests.Hosting;

/// <summary>
/// Drives a real, self-hosted Kestrel instance built by
/// <see cref="ServerHostBuilder.Build"/> over loopback with a dynamically
/// assigned port (never fixed, so parallel test classes cannot collide) -
/// there is no <c>Microsoft.AspNetCore.Mvc.Testing</c>/<c>TestHost</c>
/// package available offline (see this task's brief - no new NuGet
/// packages, restore is offline), so a genuine Kestrel round trip via
/// <see cref="HttpClient"/> is the only way to prove the actual wiring
/// (routing, the auth middleware, cookie attributes) rather than only the
/// handler logic each endpoint delegates to, which the other classes in
/// this namespace/`Http/` already cover without needing a real host.
/// </summary>
public class ServerHostBuilderTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>A 6-digit code guaranteed to differ from <paramref name="code"/>: increments its first digit, wrapping 9 to 0.</summary>
    private static string WrongCodeFor(string code) => ((code[0] - '0' + 1) % 10) + code[1..];

    /// <summary>
    /// A port the OS just told us was free, for the loopback-only host
    /// listener (<see cref="HostRequest"/>). Deliberately an ephemeral pick
    /// rather than a fixed or counted port: the OS never hands out a port
    /// sitting in <c>TIME_WAIT</c>, so repeated suite runs inside the
    /// four-minute window cannot collide with their own previous sockets -
    /// which a deterministic counter would do reliably rather than rarely.
    ///
    /// The host listener cannot simply take port 0 the way the device
    /// listener does: the rule under test is "did this arrive on the host
    /// port", so the test has to know the number before the host starts.
    /// </summary>
    private static int FreeLoopbackPort()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <summary>
    /// The default <see cref="Win32KeyInjector"/> every test in this file
    /// gets unless it explicitly asks for a different one via
    /// <see cref="RunningHost.StartAsync"/>'s own <c>keyInjector</c>
    /// parameter. Built entirely through <see cref="Win32KeyInjector"/>'s
    /// test-seam constructor (same pattern <c>Win32KeyInjectorTests</c> uses
    /// for its own <c>NotElite</c> fixture) - its foreground fake reports a
    /// process that never matches <see cref="InjectionGuard.EliteProcessNames"/>,
    /// so every real-host test in this file takes the guard-refused branch
    /// deterministically for a <c>KeyDown</c>, regardless of whatever window
    /// is actually focused on the machine running the suite.
    ///
    /// Its <c>sendInput</c> fake MUST be inert (returns a harmless "1
    /// inserted, no error") rather than throwing - measured, not assumed:
    /// mutating it to throw reddened <c>SlotMove_ThePressRouteResolvesTheButtonAtItsNewIndex</c>
    /// (predicted 0, actual 1), because <see cref="ChordPresser.PressAsync"/>
    /// sends the main key's <c>KeyUp</c> (and every modifier's) <b>unconditionally</b>,
    /// even when the preceding <c>KeyDown</c> was refused - <c>KeyUp</c>
    /// always bypasses the guard entirely (O8, <c>Win32KeyInjector</c>'s own
    /// remarks). So this fake's <c>sendInput</c> IS reached by ordinary
    /// single-tap press tests, routinely and by design - what is guaranteed
    /// is not "this fake is never called", but that the REAL
    /// <see cref="Win32ForegroundInspector.Capture"/> and
    /// <see cref="Win32NativeInputSender.Send"/> are never reachable from any
    /// test in this file, because both are permanently substituted here.
    /// </summary>
    private static Win32KeyInjector SafeGuardRefusingKeyInjector() =>
        new(
            new DiagnosticRingBuffer(64),
            () => false,
            () => new ForegroundContext("NotElite", null, null),
            _ => (1, 0));

    private static ServerHostOptions BuildOptions(
        TempDirectory temp,
        string localAppData,
        IReadOnlyList<LanCandidate>? bindCandidates = null,
        string? statusJsonDirectory = null,
        int hostAccessPort = 0,
        Win32KeyInjector? keyInjector = null) => new(
        BindAddress: IPAddress.Loopback,
        Port: 0,
        DiscoveryEnvironment: new PathDiscoveryEnvironment(
            // Always points at a real, fixed location under temp - unless a
            // test's own beforeStart callback (see StartAsync) populates a
            // real libraryfolders.vdf there, SteamInstallDiscovery finds
            // nothing at this path and reports a clean not-found, same as
            // the empty list every test here relied on before this field
            // needed to be real for the ControlSchemes-wiring tests below.
            SteamRoots: new[] { temp.Combine("Steam") },
            EpicManifestsDirectory: temp.Combine("NoEpic"),
            FrontierRegistryInstallPath: null,
            FrontierDefaultInstallRoot: temp.Combine("NoFrontier"),
            BindingsDirectory: temp.Combine("NoBindings"),
            EdhmSettingsJsonPath: temp.Combine("NoEdhm", "Settings.json"),
            EnvironmentVariables: new Dictionary<string, string>(),
            LocalAppData: localAppData,
            StatusJsonDirectory: statusJsonDirectory),
        Redactor: new PathRedactor(new[] { (temp.Path, "%TEMP_ROOT%") }),
        Clock: TimeProvider.System,
        BindCandidates: bindCandidates,
        HostAccessPort: hostAccessPort,
        KeyInjectorOverride: keyInjector ?? SafeGuardRefusingKeyInjector());

    private sealed class RunningHost : IAsyncDisposable
    {
        public WebApplication App { get; }
        public HttpClient Client { get; }
        public string LocalAppData { get; }

        /// <summary>
        /// The real <c>Options\Bindings</c> path this host's environment was
        /// built with - exposed so a test can write a real bindings/
        /// StartPreset file into it AFTER the host has already started, and
        /// prove <c>POST /api/bindings/refresh</c> picks it up without a
        /// restart (<c>ref/docs/bindings-source.md</c>'s whole point).
        /// </summary>
        public string BindingsDirectory { get; }

        /// <summary>
        /// A client pointed at the loopback-only host listener - the PC's own
        /// browser, in other words. Only present when the host was started
        /// with <c>withHostAccess: true</c>; every other test gets a host
        /// with no host listener at all, which is what
        /// <c>ServerHostOptions.HostAccessPort</c>'s default of zero means
        /// and is the same "nothing is the host" state a misconfigured
        /// production run would be in.
        ///
        /// <b>Requests through this client are genuinely non-equivalent to
        /// requests through <see cref="Client"/></b>, even though both are
        /// loopback: they arrive on different ports, and the port they
        /// arrived on is what <see cref="HostRequest.IsFromHost"/> reads.
        /// </summary>
        public HttpClient HostClient =>
            _hostClient ?? throw new InvalidOperationException(
                "This host was started without host access - pass withHostAccess: true to RunningHost.StartAsync.");

        private readonly HttpClient? _hostClient;
        private readonly TempDirectory _temp;

        private RunningHost(WebApplication app, HttpClient client, HttpClient? hostClient, TempDirectory temp, string localAppData, string bindingsDirectory)
        {
            App = app;
            Client = client;
            _hostClient = hostClient;
            _temp = temp;
            LocalAppData = localAppData;
            BindingsDirectory = bindingsDirectory;
        }

        /// <param name="statusJsonContent">
        /// When supplied, a status directory is created and a
        /// <c>Status.json</c> file is written into it with this exact
        /// content <em>before</em> the host is built - StatusFileWatcher's
        /// constructor reads it synchronously, so GameStateStore.Current is
        /// already populated by the time this method returns, with no
        /// polling needed.
        /// </param>
        /// <param name="beforeStart">
        /// Runs after <paramref name="temp"/> exists but before the host is
        /// built - lets a test lay down a real Steam install (at
        /// <c>temp.Combine("Steam")</c>/<c>temp.Combine("SteamLibrary")</c>,
        /// the fixed convention <see cref="BuildOptions"/> always points
        /// <c>SteamRoots</c> at) so <c>ref/docs/bindings-source.md</c>'s
        /// stock-scheme (<c>ControlSchemes</c>) resolution can be driven
        /// through a real, running host rather than only unit-tested in
        /// isolation.
        /// </param>
        /// <param name="withHostAccess">
        /// Binds the second, loopback-only listener and exposes
        /// <see cref="HostClient"/> for it. Off by default so the several
        /// hundred tests that have nothing to do with host access keep
        /// binding exactly one socket, and so the "nothing is the host"
        /// state stays the one most of this file runs in.
        /// </param>
        /// <param name="keyInjector">
        /// Overrides the default <see cref="SafeGuardRefusingKeyInjector"/>
        /// every other call gets - for the rare test that wants a DIFFERENT
        /// fake (e.g. one whose foreground fake reports Elite, for a test
        /// that wants a "Sent" outcome). <c>null</c> (the default) uses the
        /// safe, deterministically-refusing fake, same as every test that
        /// does not pass this at all.
        /// </param>
        public static async Task<RunningHost> StartAsync(
            IReadOnlyList<LanCandidate>? bindCandidates = null,
            string? statusJsonContent = null,
            Action<TempDirectory>? beforeStart = null,
            bool withHostAccess = false,
            Win32KeyInjector? keyInjector = null)
        {
            var temp = TempDirectory.Create();
            var localAppData = temp.CreateSubdirectory("LocalAppData");
            beforeStart?.Invoke(temp);

            string? statusJsonDirectory = null;
            if (statusJsonContent is not null)
            {
                statusJsonDirectory = temp.CreateSubdirectory("StatusDir");
                File.WriteAllText(Path.Combine(statusJsonDirectory, "Status.json"), statusJsonContent);
            }

            var hostAccessPort = withHostAccess ? FreeLoopbackPort() : 0;
            var options = BuildOptions(temp, localAppData, bindCandidates, statusJsonDirectory, hostAccessPort, keyInjector);
            var app = ServerHostBuilder.Build(Array.Empty<string>(), options);
            await app.StartAsync();

            var addressesFeature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();

            // Picked by exclusion rather than by position: which order
            // Kestrel reports two listeners in is its business, and a test
            // that took the first would silently swap the host and device
            // clients if that ever changed - which is the one mix-up that
            // would make every refusal test pass for the wrong reason.
            var baseUrl = hostAccessPort == 0
                ? addressesFeature!.Addresses.Single()
                : addressesFeature!.Addresses.Single(a => !a.EndsWith($":{hostAccessPort}", StringComparison.Ordinal));

            var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var hostClient = hostAccessPort == 0
                ? null
                : new HttpClient { BaseAddress = new Uri(HostRequest.LoopbackUrl(hostAccessPort)!) };

            return new RunningHost(app, client, hostClient, temp, localAppData, options.DiscoveryEnvironment.BindingsDirectory);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            _hostClient?.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
            _temp.Dispose();
        }
    }

    [Fact]
    public async Task Health_Unauthenticated_Returns200_LiveTrue()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.GetAsync(ApiPaths.Health);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthEndpoint.Response>(JsonOptions);
        Assert.True(body!.Live);
        Assert.False(body.BindsFound);
        Assert.False(body.EliteInstallFound);
    }

    [Fact]
    public async Task Root_Unauthenticated_Returns200_HtmlPage_WithNoDeviceCookieSet()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.GetAsync(ApiPaths.App);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("<!doctype html>", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"pairCode\"", body, StringComparison.Ordinal);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
    }

    [Fact]
    public async Task Diagnostics_NoCookie_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.GetAsync(ApiPaths.Diagnostics);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Pair_WrongCode_Returns401_AndSetsNoCookie()
    {
        await using var host = await RunningHost.StartAsync();
        var wrongCode = WrongCodeFor(host.App.Services.GetRequiredService<DeviceRegistry>().CurrentCode);

        var response = await host.Client.PostAsJsonAsync(
            ApiPaths.Pair, new { code = wrongCode, deviceName = "x", deviceClass = "tablet" }, JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Pair_MalformedJsonBody_Returns400_NotAnUnhandledException()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PostAsync(
            ApiPaths.Pair, new StringContent("{not valid json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Pair_CorrectCode_Returns200_AndSetsHttpOnlySameSiteCookie_NeverSecure()
    {
        await using var host = await RunningHost.StartAsync();
        var currentCode = host.App.Services.GetRequiredService<DeviceRegistry>().CurrentCode;

        var response = await host.Client.PostAsJsonAsync(
            ApiPaths.Pair, new { code = currentCode, deviceName = "Kitchen tablet", deviceClass = "tablet" }, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PairEndpoint.SuccessResponse>(JsonOptions);
        Assert.True(body!.Paired);
        Assert.False(string.IsNullOrEmpty(body.DeviceId));

        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var cookieHeader = Assert.Single(cookies!);
        Assert.Contains(DeviceCookieAuth.CookieName, cookieHeader, StringComparison.Ordinal);
        Assert.Contains("httponly", cookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookieHeader, StringComparison.OrdinalIgnoreCase);
        // Plain HTTP, not HTTPS - a Secure cookie would never be sent back
        // by a browser at all, silently breaking every subsequent request.
        Assert.DoesNotContain("; secure", cookieHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diagnostics_WithCookieFromSuccessfulPair_Returns200_WithEvents()
    {
        await using var host = await RunningHost.StartAsync();
        var currentCode = host.App.Services.GetRequiredService<DeviceRegistry>().CurrentCode;
        var pairResponse = await host.Client.PostAsJsonAsync(
            ApiPaths.Pair, new { code = currentCode, deviceName = "Kitchen tablet", deviceClass = "tablet" }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, pairResponse.StatusCode);

        // HttpClient's default handler has UseCookies=true, so the cookie
        // just set by /api/pair is replayed automatically on this request -
        // exactly what a real browser does, and what proves the middleware
        // reads the same cookie the pair endpoint wrote.
        var diagnosticsResponse = await host.Client.GetAsync(ApiPaths.Diagnostics);

        Assert.Equal(HttpStatusCode.OK, diagnosticsResponse.StatusCode);
        var events = await diagnosticsResponse.Content.ReadFromJsonAsync<List<DiagnosticsEndpoint.EventDto>>(JsonOptions);
        Assert.NotNull(events);
        Assert.NotEmpty(events!);
    }

    [Fact]
    public async Task BindsToLoopback_NeverAllInterfaces()
    {
        await using var host = await RunningHost.StartAsync();

        var addressesFeature = host.App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var boundUrl = new Uri(addressesFeature!.Addresses.Single());

        Assert.Equal("127.0.0.1", boundUrl.Host);
        Assert.NotEqual(0, boundUrl.Port);
    }

    [Fact]
    public async Task Logs_NeverContainPairingCodeOrRawToken_AcrossFailedPair_RejectedAuth_AndSuccessfulPair()
    {
        await using var host = await RunningHost.StartAsync();
        var registry = host.App.Services.GetRequiredService<DeviceRegistry>();
        var correctCode = registry.CurrentCode;
        var wrongCode = WrongCodeFor(correctCode);

        await host.Client.PostAsJsonAsync(ApiPaths.Pair, new { code = wrongCode, deviceName = "x", deviceClass = "tablet" }, JsonOptions);
        await host.Client.GetAsync(ApiPaths.Diagnostics);
        var pairResponse = await host.Client.PostAsJsonAsync(
            ApiPaths.Pair, new { code = correctCode, deviceName = "Kitchen tablet", deviceClass = "tablet" }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, pairResponse.StatusCode);
        var rawToken = ExtractTokenFromSetCookie(pairResponse);

        // A rejected request presenting an actual, non-null (but wrong)
        // token - distinct from the "no cookie at all" case above - is the
        // one path most likely to leak the presented value through the auth
        // middleware's own rejection log line, since that's new code this
        // task adds (DeviceRegistry's own logging is already covered by
        // DeviceRegistryTests). A separate UseCookies=false client is used
        // so this forged cookie can't be mixed up with the real one
        // host.Client already holds from the successful pair above.
        var bogusToken = "0000000000000000000000000000000000000000000000000000000000000000BOGUS";
        using var bogusHandler = new HttpClientHandler { UseCookies = false };
        using var bogusClient = new HttpClient(bogusHandler) { BaseAddress = host.Client.BaseAddress };
        var bogusRequest = new HttpRequestMessage(HttpMethod.Get, ApiPaths.Diagnostics);
        bogusRequest.Headers.Add("Cookie", $"{DeviceCookieAuth.CookieName}={bogusToken}");
        var bogusResponse = await bogusClient.SendAsync(bogusRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, bogusResponse.StatusCode);

        var ringBuffer = host.App.Services.GetRequiredService<DiagnosticRingBuffer>();
        var ringText = string.Join(
            "\n",
            ringBuffer.Snapshot().SelectMany(e => new[] { e.Message, e.Detail }).Where(s => s is not null));

        var layout = LunaPanelDirectories.Resolve(host.LocalAppData);
        var logFileText = string.Concat(Directory.EnumerateFiles(layout.LogsDirectory, "*.log").Select(File.ReadAllText));

        foreach (var secret in new[] { correctCode, wrongCode, rawToken, bogusToken })
        {
            Assert.DoesNotContain(secret, ringText, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, logFileText, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// O12's "make the choice legible" requirement: when more than one bind
    /// candidate exists, every one of them is logged - address, interface
    /// name, type, gateway presence - and the chosen one is named. Drives
    /// this through a real <see cref="ServerHostBuilder.Build"/> call with a
    /// synthetic two-candidate list (a tunnel candidate and the loopback
    /// address actually bound), rather than asserting on
    /// <c>LanAddressResolver</c> in isolation, since the logging lives in
    /// the wiring layer, not the pure resolver.
    /// </summary>
    [Fact]
    public async Task StartupLog_MultipleBindCandidates_LogsAllAndNamesChosen()
    {
        var tunnelCandidate = new LanCandidate(IPAddress.Parse("10.5.0.2"), NetworkInterfaceType.Tunnel, HasGateway: true, InterfaceName: "vpn0");
        var chosenCandidate = new LanCandidate(IPAddress.Loopback, NetworkInterfaceType.Ethernet, HasGateway: true, InterfaceName: "Ethernet");

        await using var host = await RunningHost.StartAsync(new[] { tunnelCandidate, chosenCandidate });

        var ringBuffer = host.App.Services.GetRequiredService<DiagnosticRingBuffer>();
        var messages = ringBuffer.Snapshot().Select(e => e.Message).ToList();

        var tunnelLine = Assert.Single(messages, m => m.Contains("10.5.0.2", StringComparison.Ordinal));
        Assert.Contains("vpn0", tunnelLine, StringComparison.Ordinal);
        Assert.Contains("Tunnel", tunnelLine, StringComparison.Ordinal);
        Assert.DoesNotContain("chosen", tunnelLine, StringComparison.Ordinal);

        var chosenLine = Assert.Single(messages, m => m.Contains("127.0.0.1", StringComparison.Ordinal) && m.Contains("Ethernet", StringComparison.Ordinal));
        Assert.Contains("chosen", chosenLine, StringComparison.Ordinal);
    }

    /// <summary>
    /// Pairs a fresh device against a running host and returns an
    /// <see cref="HttpClient"/> already carrying its auth cookie - shared
    /// setup for every <c>/api/panel</c>/<c>/api/press</c> test below, none
    /// of which care about pairing itself.
    /// </summary>
    private static async Task<HttpClient> PairedClientAsync(RunningHost host)
    {
        // [2026-09-16: deviceClass was "tablet" here, but at the time this
        // had no effect on the seeded layout at all - StarterLayout.Load()
        // was unconditional. Device-size-aware seeding now makes deviceClass
        // load-bearing, and every test below this helper feeds pins the
        // PHONE variant's content (t30, 26/29-slot ship page, 6x5 landscape
        // grid) - "phone" is the honest label for what these tests actually
        // exercise, not a behavior change to any of them.]
        var code = host.App.Services.GetRequiredService<DeviceRegistry>().CurrentCode;
        var response = await host.Client.PostAsJsonAsync(
            ApiPaths.Pair, new { code, deviceName = "Test device", deviceClass = "phone" }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return host.Client;
    }

    // -------------------------------------------------------------------
    // GET /api/panel and POST /api/press. Two independent things keep every
    // test in this file from ever sending a real keystroke, not just one:
    // the synthetic environment (BuildOptions above) has no real bindings
    // directory at all, so a test that never calls
    // UseFullyBoundStarterBindingsAsync sees every action in the seeded
    // starter layout resolve Unbound (a real, curated action - just not
    // bound to anything here), never Ok, so PressEndpoint.Evaluate can never
    // reach EvaluationOutcome.CanFire and ChordPresser.PressAsync is never
    // invoked at all. But the more fundamental guarantee, covering the
    // tests that DO bind real actions (e.g. the Macros_* tests below), is
    // SafeGuardRefusingKeyInjector: every RunningHost in this file wires a
    // Win32KeyInjector whose foreground fake can never match
    // InjectionGuard.EliteProcessNames, so even a fully-bound, genuinely
    // fireable press is refused by the guard before ChordPresser reaches a
    // real Win32 API - the real Win32ForegroundInspector.Capture and
    // Win32NativeInputSender.Send are structurally unreachable from any test
    // in this file, not merely unexercised by coincidence of an unbound
    // action or an unfocused window.
    // -------------------------------------------------------------------

    [Fact]
    public async Task Panel_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Panel_FirstRequestForANewDevice_SeedsStarterLayout_ReturnsT30WithTwentyNineSlots()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal("t30", body!.TemplateId);
        // [2026-09-07, superseded: was 30. Five starter-layout actions were
        // freed (StarterLayoutTests has the full accounting); net was 26.]
        // [2026-09-16, superseded: was 26. The pip-management task filled
        // three more of the freed slots; net is 29 - see StarterLayoutTests.]
        Assert.Equal(29, body.Slots.Count);

        // Slot 0 names the request-docking macro, which now genuinely
        // exists (ref/docs/panel-tab-tracking.md) - it used to be the
        // starter layout's deliberately-missing macro, the first
        // end-to-end proof that a degraded slot renders as visibly
        // degraded rather than crashing. [2026-09-07, superseded: was
        // "UnknownMacro" with reason containing "request-docking" - the
        // macro id is recognized now, so this reads MacroDegraded instead
        // (its own actions - FocusLeftPanel/UI_Right/UI_Select - aren't
        // bound in this synthetic, bindings-file-less environment), the
        // same shape slot 17's disembark already had.]
        var slot0 = body.Slots.Single(s => s.Index == 0);
        Assert.Equal("MacroDegraded", slot0.Status);
        Assert.Contains("request-docking", slot0.Reason);

        // No real bindings file exists in this synthetic environment, so
        // every real action is Unbound (not UnknownAction) - it's a
        // genuine, curated action, just not currently bound to anything.
        var slot1 = body.Slots.Single(s => s.Index == 1);
        Assert.Equal("Unbound", slot1.Status);
        Assert.Null(slot1.Chord);

        // Slot 17 is the starter layout's OTHER macro - "disembark" - which,
        // unlike slot 0's request-docking, genuinely exists (the macro
        // loader really is wired: MacroKnowledge is no longer always empty,
        // or this would read UnknownMacro, not MacroDegraded). It still
        // can't be Ok here, since UI_Down/UI_Select aren't bound in this
        // synthetic, bindings-file-less environment.
        var slot17 = body.Slots.Single(s => s.Index == 17);
        Assert.Equal("MacroDegraded", slot17.Status);
        Assert.Contains("disembark", slot17.Reason);
    }

    [Fact]
    public async Task Panel_LandscapeViewport_ReportsLandscapeGeometry()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.GetAsync($"{ApiPaths.Panel}?w=800&h=400");
        var body = await response.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);

        // t30 landscape is 6 columns x 5 rows (see Templates); portrait
        // would be 5x6 - proves the endpoint reads orientation from the
        // viewport it was actually given, not a fixed default.
        Assert.Equal(6, body!.Cols);
        Assert.Equal(5, body.Rows);
    }

    [Fact]
    public async Task Panel_InvalidViewport_Returns400()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.GetAsync($"{ApiPaths.Panel}?w=0&h=640");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Panel_ResponseBody_NeverContainsTheLocalAppDataTempRootPath()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(host.LocalAppData, raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AppData", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", raw, StringComparison.OrdinalIgnoreCase);

        // Confirms ASP.NET's own default web JSON casing (camelCase) is what
        // actually reaches the wire here - panel-api.md documents the SSE
        // endpoint as matching this casing deliberately, which is only true
        // if this claim about /api/panel's own casing holds.
        Assert.Contains("\"templateId\"", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\"TemplateId\"", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Panel_NoStatusJsonDirectory_LitIsAlwaysFalse_ServerStaysHealthy()
    {
        // The default RunningHost.StartAsync() below supplies no status
        // directory at all - the exact "Elite has never been launched"
        // degrade case. The server must stay healthy and simply report
        // every lit-capable slot as unlit, never fail the request.
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);

        // Slot 1 is the starter layout's LandingGearToggle, which carries a
        // real "lit" condition in the shipped catalogue.
        var slot1 = body!.Slots.Single(s => s.Index == 1);
        Assert.Equal("Off", slot1.Lit);
    }

    [Fact]
    public async Task Panel_StatusJsonPresentAtStartup_LitReflectsGearDown()
    {
        // Flags bit 2 is LandingGearDown (see StatusVocabularyTests). Written
        // before the host starts, so StatusFileWatcher's constructor picks
        // it up synchronously - no polling needed to observe it.
        const string statusJson = """{ "timestamp": "2026-09-06T00:00:00Z", "event": "Status", "Flags": 4, "Pips": [4, 4, 4] }""";
        await using var host = await RunningHost.StartAsync(statusJsonContent: statusJson);
        var client = await PairedClientAsync(host);

        var response = await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);
        var slot1 = body!.Slots.Single(s => s.Index == 1);
        Assert.Equal("Full", slot1.Lit);
    }

    // -------------------------------------------------------------------
    // GET /api/templates, GET/POST /api/panel/settings, POST /api/panel/template
    // - the template picker moved into settings, and the "merging and
    // expanding panels" toggle (ref/docs/panels-and-pages.md).
    // -------------------------------------------------------------------

    [Fact]
    public async Task Templates_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.GetAsync($"{ApiPaths.Templates}?w=360&h=640");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Templates_ReturnsAllNineRungs_WithAComfortVerdictEach()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.GetAsync($"{ApiPaths.Templates}?w=1280&h=800");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<TemplateProbeEndpoint.RungEstimate>>(JsonOptions);
        Assert.Equal(9, body!.Count);
        Assert.All(body, r => Assert.Contains(r.Verdict, new[] { "Comfortable", "Compact", "TooSmall" }));
    }

    // -------------------------------------------------------------------
    // GET /probe, /probe/estimate, /probe/labels, /probe/sw.js - the
    // calibration surface, no longer auth-exempt (O14, closed 2026-09-17).
    // Real Kestrel round trips, mirroring the Templates pair above, since
    // an unauthenticated-surface regression is exactly the kind of thing a
    // pure-function pin (DeviceAuthMiddlewareExtensionsTests.IsExemptPath)
    // cannot catch on its own - it would still pass if some other route
    // mapping bypassed the middleware entirely.
    // -------------------------------------------------------------------

    [Theory]
    [InlineData(ApiPaths.Probe)]
    [InlineData(ApiPaths.ProbeEstimate)]
    [InlineData(ApiPaths.ProbeLabels)]
    [InlineData(ApiPaths.ProbeServiceWorker)]
    public async Task ProbeRoutes_Unauthenticated_Return401(string path)
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(ApiPaths.Probe)]
    [InlineData(ApiPaths.ProbeLabels)]
    [InlineData(ApiPaths.ProbeServiceWorker)]
    public async Task ProbeRoutes_APairedDevice_CanStillReachThem(string path)
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProbeEstimate_APairedDevice_CanStillReachIt()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.GetAsync($"{ApiPaths.ProbeEstimate}?w=360&h=640");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PanelSettings_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.GetAsync(ApiPaths.PanelSettings);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PanelSettings_NoneStoredYet_DefaultsToMergeExpandOn()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.GetAsync(ApiPaths.PanelSettings);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PanelSettingsEndpoint.Response>(JsonOptions);
        Assert.True(body!.MergeExpand);
    }

    [Fact]
    public async Task PanelSettings_Post_ThenGet_RoundTrips()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var postResponse = await client.PostAsJsonAsync(ApiPaths.PanelSettings, new PanelSettingsEndpoint.Request(false, true));
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

        var getResponse = await client.GetAsync(ApiPaths.PanelSettings);
        var body = await getResponse.Content.ReadFromJsonAsync<PanelSettingsEndpoint.Response>(JsonOptions);
        Assert.False(body!.MergeExpand);
    }

    [Fact]
    public async Task PanelSettings_NoneStoredYet_DefaultsToShowMacroStepResultsOff()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.GetAsync(ApiPaths.PanelSettings);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PanelSettingsEndpoint.Response>(JsonOptions);
        Assert.False(body!.ShowMacroStepResults);
    }

    [Fact]
    public async Task PanelSettings_Post_ShowMacroStepResultsFalse_ThenGet_RoundTrips()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var postResponse = await client.PostAsJsonAsync(ApiPaths.PanelSettings, new PanelSettingsEndpoint.Request(true, false));
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

        var getResponse = await client.GetAsync(ApiPaths.PanelSettings);
        var body = await getResponse.Content.ReadFromJsonAsync<PanelSettingsEndpoint.Response>(JsonOptions);
        Assert.False(body!.ShowMacroStepResults);
    }

    // -------------------------------------------------------------------
    // GET/POST /api/macro-timing (ref/docs/macro-timing.md). Unlike
    // PanelSettings above, this endpoint takes no device id at all - the
    // "shared across devices" test below pairs two DIFFERENT devices and
    // proves the second one sees what the first one stored, which
    // PanelSettings would fail (it is deliberately per-device).
    // -------------------------------------------------------------------

    [Fact]
    public async Task MacroTiming_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var getResponse = await host.Client.GetAsync(ApiPaths.MacroTiming);
        var postResponse = await host.Client.PostAsJsonAsync(ApiPaths.MacroTiming, new MacroTimingEndpoint.Request(200, 120), JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, postResponse.StatusCode);
    }

    [Fact]
    public async Task MacroTiming_NoneStoredYet_DefaultsToShippedValues()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.GetAsync(ApiPaths.MacroTiming);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MacroTimingEndpoint.Response>(JsonOptions);
        Assert.Equal((int)MacroTimingDefaults.DefaultHoldDuration.TotalMilliseconds, body!.HoldMs);
        Assert.Equal((int)MacroTimingDefaults.InterPressGap.TotalMilliseconds, body.InterPressGapMs);
    }

    [Fact]
    public async Task MacroTiming_Post_ThenGet_RoundTrips()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var postResponse = await client.PostAsJsonAsync(ApiPaths.MacroTiming, new MacroTimingEndpoint.Request(275, 180), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

        var getResponse = await client.GetAsync(ApiPaths.MacroTiming);
        var body = await getResponse.Content.ReadFromJsonAsync<MacroTimingEndpoint.Response>(JsonOptions);
        Assert.Equal(275, body!.HoldMs);
        Assert.Equal(180, body.InterPressGapMs);
    }

    [Fact]
    public async Task MacroTiming_OutOfRangeValue_Returns400_AndDoesNotPersist()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var postResponse = await client.PostAsJsonAsync(
            ApiPaths.MacroTiming, new MacroTimingEndpoint.Request(MacroTimingSettings.MaxMs + 1, 100), JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, postResponse.StatusCode);

        var getResponse = await client.GetAsync(ApiPaths.MacroTiming);
        var body = await getResponse.Content.ReadFromJsonAsync<MacroTimingEndpoint.Response>(JsonOptions);
        Assert.Equal((int)MacroTimingDefaults.DefaultHoldDuration.TotalMilliseconds, body!.HoldMs);
    }

    /// <summary>
    /// The scope test <see cref="MacroTiming_Post_ThenGet_RoundTrips"/> alone
    /// cannot make: two DIFFERENT devices (proven by re-pairing, which
    /// replaces host.Client's cookie with the new device's - same technique
    /// <see cref="Pair_APhoneRePairingUnderItsOldName_AdoptsThatLayout_AndSaysSo"/>
    /// already uses) must see the SAME stored value, unlike every per-device
    /// setting (PanelSettings, ThemeOverride) which would fail this exact
    /// test.
    /// </summary>
    [Fact]
    public async Task MacroTiming_IsSharedAcrossDevices_NotKeyedPerDevice()
    {
        await using var host = await RunningHost.StartAsync();

        var first = await PairAsync(host, deviceName: "Luna phone", deviceClass: "phone");
        var firstPostResponse = await host.Client.PostAsJsonAsync(ApiPaths.MacroTiming, new MacroTimingEndpoint.Request(275, 180), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, firstPostResponse.StatusCode);

        // Re-pairing a second device replaces host.Client's cookie with the
        // SECOND device's - PairAsync's own remarks.
        var second = await PairAsync(host, deviceName: "Bridge slate", deviceClass: "tablet");
        Assert.NotEqual(first.DeviceId, second.DeviceId);

        var secondGetResponse = await host.Client.GetAsync(ApiPaths.MacroTiming);
        var body = await secondGetResponse.Content.ReadFromJsonAsync<MacroTimingEndpoint.Response>(JsonOptions);
        Assert.Equal(275, body!.HoldMs);
        Assert.Equal(180, body.InterPressGapMs);
    }

    [Fact]
    public async Task PanelTemplate_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(ApiPaths.PanelTemplate, new PanelTemplateEndpoint.Request(0, "t12"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PanelTemplate_MergeExpandOn_ShrinkingTheStarterT30ToT12_SpillsAcrossThreePages()
    {
        // This synthetic environment discovers no real bindings file at all
        // (BindingsDirectory points at a directory that doesn't exist), so
        // knownActionNames is always empty and LayoutStore.Save can never
        // actually persist a layout here - the exact same "validation fails,
        // fall back to in-memory only" case LayoutAccess.LoadOrSeed's own
        // remarks describe for seeding. A follow-up GET would therefore
        // re-seed the untouched starter rather than observe the change - so
        // this test (and the OFF-setting one below) only checks the POST
        // response itself, which is what actually exercises this route's
        // wiring to PanelTemplateEndpoint.ChangeTemplate; the redistribution
        // logic itself is already pinned directly by LayoutSpillerTests/
        // PanelTemplateEndpointTests without this environment limitation.
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        // Seed the starter layout (t30) first, same as any other panel route.
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");

        var changeResponse = await client.PostAsJsonAsync(ApiPaths.PanelTemplate, new PanelTemplateEndpoint.Request(0, "t12"));

        Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);
        var changeBody = await changeResponse.Content.ReadFromJsonAsync<JsonElement>();
        // [2026-09-09, superseded: was 3. pageCount is the WHOLE layout's
        // page count, not the spill's - the ship page still spills into
        // three, but the starter now carries five context pages beside it
        // (ref/docs/vessel-context.md's "The default context pages"), so
        // 3 + 5 = 8. The spill arithmetic itself is unchanged and is pinned
        // directly by LayoutSpillerTests.]
        Assert.Equal(8, changeBody.GetProperty("pageCount").GetInt32());
    }

    [Fact]
    public async Task PanelTemplate_MergeExpandOff_ShrinkingTheStarterT30ToT12_ParksInPlace_StaysOnePage()
    {
        // See the merge-expand-ON test above for why this checks only the
        // POST response, not a follow-up GET.
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        await client.PostAsJsonAsync(ApiPaths.PanelSettings, new PanelSettingsEndpoint.Request(false, true));

        var changeResponse = await client.PostAsJsonAsync(ApiPaths.PanelTemplate, new PanelTemplateEndpoint.Request(0, "t12"));

        Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);
        var changeBody = await changeResponse.Content.ReadFromJsonAsync<JsonElement>();
        // [2026-09-09, superseded: was 1, when the starter was a single page.
        // Parking in place still adds NO page - the starter's own five
        // context pages account for the whole difference (6 vs the ON case's
        // 8, which is this same 6 plus the ship page's two spill pages).]
        Assert.Equal(6, changeBody.GetProperty("pageCount").GetInt32());
    }

    [Fact]
    public async Task PanelTemplate_UnknownTemplateId_Returns400()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");

        var response = await client.PostAsJsonAsync(ApiPaths.PanelTemplate, new PanelTemplateEndpoint.Request(0, "not-a-real-template"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -------------------------------------------------------------------
    // GET /api/actions and POST /api/panel/slot/assign|label|clear - the
    // on-device editor (ref/docs/editor.md). The pure transformations
    // themselves are already pinned directly by SlotEditEndpointTests/
    // ActionsEndpointTests without this environment's limitations; these
    // prove the real Kestrel wiring (auth, JSON body binding,
    // LayoutStore.Save actually being called).
    // -------------------------------------------------------------------

    /// <summary>
    /// A real bindings file binding every action the starter layout
    /// (<c>starter-layout.json</c>) actually names, each to a distinct real
    /// key - so a slot edit against a freshly-seeded starter never fails
    /// <c>LayoutValidator.ValidateForSave</c> over some OTHER, untouched
    /// slot's action name being unrecognized in this synthetic environment.
    /// </summary>
    private const string StarterActionsFullyBoundBindingsXml = """
        <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
            <LandingGearToggle><Primary Device="Keyboard" Key="Key_A" /><Secondary Device="{NoDevice}" Key="" /></LandingGearToggle>
            <ToggleCargoScoop><Primary Device="Keyboard" Key="Key_B" /><Secondary Device="{NoDevice}" Key="" /></ToggleCargoScoop>
            <NightVisionToggle><Primary Device="Keyboard" Key="Key_C" /><Secondary Device="{NoDevice}" Key="" /></NightVisionToggle>
            <ShipSpotLightToggle><Primary Device="Keyboard" Key="Key_D" /><Secondary Device="{NoDevice}" Key="" /></ShipSpotLightToggle>
            <DeployHeatSink><Primary Device="Keyboard" Key="Key_E" /><Secondary Device="{NoDevice}" Key="" /></DeployHeatSink>
            <ResetPowerDistribution><Primary Device="Keyboard" Key="Key_F" /><Secondary Device="{NoDevice}" Key="" /></ResetPowerDistribution>
            <IncreaseSystemsPower><Primary Device="Keyboard" Key="Key_G" /><Secondary Device="{NoDevice}" Key="" /></IncreaseSystemsPower>
            <IncreaseEnginesPower><Primary Device="Keyboard" Key="Key_H" /><Secondary Device="{NoDevice}" Key="" /></IncreaseEnginesPower>
            <IncreaseWeaponsPower><Primary Device="Keyboard" Key="Key_I" /><Secondary Device="{NoDevice}" Key="" /></IncreaseWeaponsPower>
            <Supercruise><Primary Device="Keyboard" Key="Key_J" /><Secondary Device="{NoDevice}" Key="" /></Supercruise>
            <Hyperspace><Primary Device="Keyboard" Key="Key_K" /><Secondary Device="{NoDevice}" Key="" /></Hyperspace>
            <FocusLeftPanel><Primary Device="Keyboard" Key="Key_L" /><Secondary Device="{NoDevice}" Key="" /></FocusLeftPanel>
            <FocusRightPanel><Primary Device="Keyboard" Key="Key_M" /><Secondary Device="{NoDevice}" Key="" /></FocusRightPanel>
            <FocusCommsPanel><Primary Device="Keyboard" Key="Key_N" /><Secondary Device="{NoDevice}" Key="" /></FocusCommsPanel>
            <FocusRadarPanel><Primary Device="Keyboard" Key="Key_O" /><Secondary Device="{NoDevice}" Key="" /></FocusRadarPanel>
            <CycleNextPanel><Primary Device="Keyboard" Key="Key_P" /><Secondary Device="{NoDevice}" Key="" /></CycleNextPanel>
            <CyclePreviousPanel><Primary Device="Keyboard" Key="Key_Q" /><Secondary Device="{NoDevice}" Key="" /></CyclePreviousPanel>
            <ExplorationFSSEnter><Primary Device="Keyboard" Key="Key_R" /><Secondary Device="{NoDevice}" Key="" /></ExplorationFSSEnter>
            <ExplorationFSSQuit><Primary Device="Keyboard" Key="Key_S" /><Secondary Device="{NoDevice}" Key="" /></ExplorationFSSQuit>
            <GalaxyMapOpen><Primary Device="Keyboard" Key="Key_T" /><Secondary Device="{NoDevice}" Key="" /></GalaxyMapOpen>
            <SystemMapOpen><Primary Device="Keyboard" Key="Key_U" /><Secondary Device="{NoDevice}" Key="" /></SystemMapOpen>
            <TargetNextRouteSystem><Primary Device="Keyboard" Key="Key_V" /><Secondary Device="{NoDevice}" Key="" /></TargetNextRouteSystem>
            <SetSpeed75><Primary Device="Keyboard" Key="Key_W" /><Secondary Device="{NoDevice}" Key="" /></SetSpeed75>
            <SelectTarget><Primary Device="Keyboard" Key="Key_X" /><Secondary Device="{NoDevice}" Key="" /></SelectTarget>
            <CycleNextTarget><Primary Device="Keyboard" Key="Key_Y" /><Secondary Device="{NoDevice}" Key="" /></CycleNextTarget>
            <RadarIncreaseRange><Primary Device="Keyboard" Key="Key_Z" /><Secondary Device="{NoDevice}" Key="" /></RadarIncreaseRange>
            <RadarDecreaseRange><Primary Device="Keyboard" Key="Key_0" /><Secondary Device="{NoDevice}" Key="" /></RadarDecreaseRange>
            <OrbitLinesToggle><Primary Device="Keyboard" Key="Key_1" /><Secondary Device="{NoDevice}" Key="" /></OrbitLinesToggle>
            <PlayerHUDModeToggle><Primary Device="Keyboard" Key="Key_2" /><Secondary Device="{NoDevice}" Key="" /></PlayerHUDModeToggle>
            <!-- [2026-09-16] The pip-management task's two new SHIP action
                 slots (DeployHardpointToggle, ToggleFlightAssist) - same
                 reasoning as every other block here. -->
            <DeployHardpointToggle><Primary Device="Keyboard" Key="Key_Comma" /><Secondary Device="{NoDevice}" Key="" /></DeployHardpointToggle>
            <ToggleFlightAssist><Primary Device="Keyboard" Key="Key_Period" /><Secondary Device="{NoDevice}" Key="" /></ToggleFlightAssist>
            <!-- [2026-09-16] The tablet SHIP page's grouped reorg
                 (expressive-kindling-starfish.md's Part D) added several
                 actions no phone-layout slot had ever named: page-cycling,
                 the four thrust hold-buttons, weapon fire/targeting,
                 WingNavLock, TriggerColonisationModule and the new Confirm
                 (UI_Select) button - same reasoning as every other block
                 here, this fixture must bind every action ANY shipped
                 starter layout (phone or tablet) names. -->
            <CycleNextPage><Primary Device="Keyboard" Key="Key_Semicolon" /><Secondary Device="{NoDevice}" Key="" /></CycleNextPage>
            <CyclePreviousPage><Primary Device="Keyboard" Key="Key_Slash" /><Secondary Device="{NoDevice}" Key="" /></CyclePreviousPage>
            <UpThrustButton><Primary Device="Keyboard" Key="Key_Equals" /><Secondary Device="{NoDevice}" Key="" /></UpThrustButton>
            <DownThrustButton><Primary Device="Keyboard" Key="Key_Minus" /><Secondary Device="{NoDevice}" Key="" /></DownThrustButton>
            <LeftThrustButton><Primary Device="Keyboard" Key="Key_LeftBracket" /><Secondary Device="{NoDevice}" Key="" /></LeftThrustButton>
            <RightThrustButton><Primary Device="Keyboard" Key="Key_RightBracket" /><Secondary Device="{NoDevice}" Key="" /></RightThrustButton>
            <PrimaryFire><Primary Device="Keyboard" Key="Key_Quote" /><Secondary Device="{NoDevice}" Key="" /></PrimaryFire>
            <SecondaryFire><Primary Device="Keyboard" Key="Key_Backslash" /><Secondary Device="{NoDevice}" Key="" /></SecondaryFire>
            <CycleNextHostileTarget><Primary Device="Keyboard" Key="Key_Tab" /><Secondary Device="{NoDevice}" Key="" /></CycleNextHostileTarget>
            <SelectHighestThreat><Primary Device="Keyboard" Key="Key_CapsLock" /><Secondary Device="{NoDevice}" Key="" /></SelectHighestThreat>
            <WingNavLock><Primary Device="Keyboard" Key="Key_Insert" /><Secondary Device="{NoDevice}" Key="" /></WingNavLock>
            <TriggerColonisationModule><Primary Device="Keyboard" Key="Key_Delete" /><Secondary Device="{NoDevice}" Key="" /></TriggerColonisationModule>
            <UI_Select><Primary Device="Keyboard" Key="Key_Space" /><Secondary Device="{NoDevice}" Key="" /></UI_Select>
            <!-- [2026-09-09] The starter layout's context pages (ref/docs/vessel-context.md's
                 "The default context pages"). Every action either page names has to be
                 recognized here too, or ValidateForSave rejects the whole save over a slot
                 this test never touches - the same reason the ship page's actions are listed
                 above. NightVisionToggle and FocusLeftPanel are shared with the ship page and
                 are already declared above; the SRV page's other seven and the on-foot page's
                 twelve are new. -->
            <AutoBreakBuggyButton><Primary Device="Keyboard" Key="Key_3" /><Secondary Device="{NoDevice}" Key="" /></AutoBreakBuggyButton>
            <ToggleDriveAssist><Primary Device="Keyboard" Key="Key_4" /><Secondary Device="{NoDevice}" Key="" /></ToggleDriveAssist>
            <ToggleBuggyTurretButton><Primary Device="Keyboard" Key="Key_5" /><Secondary Device="{NoDevice}" Key="" /></ToggleBuggyTurretButton>
            <HeadlightsBuggyButton><Primary Device="Keyboard" Key="Key_6" /><Secondary Device="{NoDevice}" Key="" /></HeadlightsBuggyButton>
            <BuggyCycleFireGroupNext><Primary Device="Keyboard" Key="Key_7" /><Secondary Device="{NoDevice}" Key="" /></BuggyCycleFireGroupNext>
            <ToggleCargoScoop_Buggy><Primary Device="Keyboard" Key="Key_8" /><Secondary Device="{NoDevice}" Key="" /></ToggleCargoScoop_Buggy>
            <RecallDismissShip><Primary Device="Keyboard" Key="Key_9" /><Secondary Device="{NoDevice}" Key="" /></RecallDismissShip>
            <HumanoidPrimaryInteractButton><Primary Device="Keyboard" Key="Key_F1" /><Secondary Device="{NoDevice}" Key="" /></HumanoidPrimaryInteractButton>
            <HumanoidSecondaryInteractButton><Primary Device="Keyboard" Key="Key_F2" /><Secondary Device="{NoDevice}" Key="" /></HumanoidSecondaryInteractButton>
            <HumanoidOpenAccessPanelButton><Primary Device="Keyboard" Key="Key_F3" /><Secondary Device="{NoDevice}" Key="" /></HumanoidOpenAccessPanelButton>
            <HumanoidToggleFlashlightButton><Primary Device="Keyboard" Key="Key_F4" /><Secondary Device="{NoDevice}" Key="" /></HumanoidToggleFlashlightButton>
            <HumanoidToggleNightVisionButton><Primary Device="Keyboard" Key="Key_F5" /><Secondary Device="{NoDevice}" Key="" /></HumanoidToggleNightVisionButton>
            <HumanoidToggleShieldsButton><Primary Device="Keyboard" Key="Key_F6" /><Secondary Device="{NoDevice}" Key="" /></HumanoidToggleShieldsButton>
            <HumanoidHealthPack><Primary Device="Keyboard" Key="Key_F7" /><Secondary Device="{NoDevice}" Key="" /></HumanoidHealthPack>
            <HumanoidBattery><Primary Device="Keyboard" Key="Key_F8" /><Secondary Device="{NoDevice}" Key="" /></HumanoidBattery>
            <HumanoidSwitchToRechargeTool><Primary Device="Keyboard" Key="Key_F9" /><Secondary Device="{NoDevice}" Key="" /></HumanoidSwitchToRechargeTool>
            <HumanoidSwitchToCompAnalyser><Primary Device="Keyboard" Key="Key_F10" /><Secondary Device="{NoDevice}" Key="" /></HumanoidSwitchToCompAnalyser>
            <HumanoidSwitchToSuitTool><Primary Device="Keyboard" Key="Key_F11" /><Secondary Device="{NoDevice}" Key="" /></HumanoidSwitchToSuitTool>
            <HumanoidHideWeaponButton><Primary Device="Keyboard" Key="Key_F12" /><Secondary Device="{NoDevice}" Key="" /></HumanoidHideWeaponButton>
            <!-- [2026-09-12] The SRV page's eight new panel-access buggy
                 variants (ref/docs/vessel-context.md's "The SRV page, and
                 why these eight") - same reasoning as the block above. -->
            <FocusLeftPanel_Buggy><Primary Device="Keyboard" Key="Key_F13" /><Secondary Device="{NoDevice}" Key="" /></FocusLeftPanel_Buggy>
            <FocusRightPanel_Buggy><Primary Device="Keyboard" Key="Key_F14" /><Secondary Device="{NoDevice}" Key="" /></FocusRightPanel_Buggy>
            <GalaxyMapOpen_Buggy><Primary Device="Keyboard" Key="Key_F15" /><Secondary Device="{NoDevice}" Key="" /></GalaxyMapOpen_Buggy>
            <SystemMapOpen_Buggy><Primary Device="Keyboard" Key="Key_F16" /><Secondary Device="{NoDevice}" Key="" /></SystemMapOpen_Buggy>
            <UIFocus_Buggy><Primary Device="Keyboard" Key="Key_F17" /><Secondary Device="{NoDevice}" Key="" /></UIFocus_Buggy>
            <PlayerHUDModeToggle_Buggy><Primary Device="Keyboard" Key="Key_F18" /><Secondary Device="{NoDevice}" Key="" /></PlayerHUDModeToggle_Buggy>
            <OpenCodexGoToDiscovery_Buggy><Primary Device="Keyboard" Key="Key_F19" /><Secondary Device="{NoDevice}" Key="" /></OpenCodexGoToDiscovery_Buggy>
            <PhotoCameraToggle_Buggy><Primary Device="Keyboard" Key="Key_F20" /><Secondary Device="{NoDevice}" Key="" /></PhotoCameraToggle_Buggy>
        </Root>
        """;

    /// <summary>
    /// Writes <see cref="StarterActionsFullyBoundBindingsXml"/> as the active
    /// preset and refreshes discovery so <c>LiveBindingsReader</c> picks it
    /// up without a restart - same mechanism
    /// <c>BindingsRefresh_PicksUpAPresetThatDidNotExistAtStartup_NoRestartNeeded</c>
    /// already drives end to end.
    /// </summary>
    private static async Task UseFullyBoundStarterBindingsAsync(RunningHost host, HttpClient client)
    {
        Directory.CreateDirectory(host.BindingsDirectory);
        File.WriteAllText(Path.Combine(host.BindingsDirectory, "Custom.4.2.binds"), StarterActionsFullyBoundBindingsXml);
        File.WriteAllText(Path.Combine(host.BindingsDirectory, "StartPreset.4.start"), "Custom\n");
        var refreshResponse = await client.PostAsync(ApiPaths.BindingsRefresh, content: null);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task Actions_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.GetAsync(ApiPaths.Actions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Actions_DefaultMode_OmitsAnUnboundUncuratedElement_ShowEverythingIncludesIt()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        Directory.CreateDirectory(host.BindingsDirectory);
        File.WriteAllText(
            Path.Combine(host.BindingsDirectory, "Custom.4.2.binds"),
            """
            <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
                <LandingGearToggle><Primary Device="Keyboard" Key="Key_A" /><Secondary Device="{NoDevice}" Key="" /></LandingGearToggle>
                <SomeUnboundUncuratedElement><Primary Device="{NoDevice}" Key="" /><Secondary Device="{NoDevice}" Key="" /></SomeUnboundUncuratedElement>
            </Root>
            """);
        File.WriteAllText(Path.Combine(host.BindingsDirectory, "StartPreset.4.start"), "Custom\n");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(ApiPaths.BindingsRefresh, content: null)).StatusCode);

        var defaultResponse = await client.GetAsync(ApiPaths.Actions);
        var defaultBody = await defaultResponse.Content.ReadFromJsonAsync<List<ActionsEndpoint.ActionDto>>(JsonOptions);
        Assert.DoesNotContain(defaultBody!, a => a.ActionName == "SomeUnboundUncuratedElement");

        var allResponse = await client.GetAsync($"{ApiPaths.Actions}?all=true");
        var allBody = await allResponse.Content.ReadFromJsonAsync<List<ActionsEndpoint.ActionDto>>(JsonOptions);
        var expanded = Assert.Single(allBody!, a => a.ActionName == "SomeUnboundUncuratedElement");
        Assert.False(expanded.IsBound);
        Assert.False(expanded.IsCurated);
    }

    [Fact]
    public async Task SlotAssign_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(
            ApiPaths.SlotAssign, new SlotEditEndpoint.AssignRequest(0, 0, "LandingGearToggle", null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SlotAssign_UnknownActionName_Returns400_NoBindingsFileAtAllInThisEnvironment()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640"); // seed the starter

        var response = await client.PostAsJsonAsync(
            ApiPaths.SlotAssign, new SlotEditEndpoint.AssignRequest(0, 0, "TotallyFictionalAction", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SlotAssign_RealBoundAction_Persists_AndAFollowUpGetReflectsIt()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640"); // seed the starter (t30)

        // Slot 0 is the starter's own deliberately-degraded macro
        // (request-docking) - reassigning it to a real, bound action is
        // exactly the "one button changed" scenario ref/docs/editor.md was
        // written to fix.
        var assignResponse = await client.PostAsJsonAsync(
            ApiPaths.SlotAssign, new SlotEditEndpoint.AssignRequest(0, 0, "SetSpeed75", null, "SPEED"));
        Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);

        var panelResponse = await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var panel = await panelResponse.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);
        var slot0 = panel!.Slots.Single(s => s.Index == 0);
        Assert.Equal("Ok", slot0.Status);
        Assert.Equal("SPEED", slot0.Label);
        Assert.Equal("W", slot0.Chord);
    }

    // -------------------------------------------------------------------
    // GET/POST /api/macros - the macro builder (ref/docs/macro-builder.md).
    // The mappings and the save decision are pinned directly by
    // MacrosEndpointTests; these prove the real Kestrel wiring, that
    // UserMacroStore is actually written, and - the point of the whole
    // MacroCatalogue change - that a macro saved through the API is visible
    // to the panel and press handlers on the very next request, with no
    // restart.
    // -------------------------------------------------------------------

    /// <summary>
    /// Saves a macro through the real API and returns its server-minted id.
    /// Deliberately goes through HTTP rather than the store directly - the
    /// id being minted server-side is half of what is under test.
    /// </summary>
    /// <remarks>
    /// [2026-09-10] Takes the <see cref="RunningHost"/> rather than an
    /// <see cref="HttpClient"/>, and always authors through
    /// <see cref="RunningHost.HostClient"/>. Before the builder moved to the
    /// PC (<c>ref/docs/macro-builder.md</c>) this was handed a paired
    /// device's client, which is now refused 403 by
    /// <c>HostOnlyRoutes</c> - the change is the feature, not a workaround
    /// for it. Every caller therefore starts its host with
    /// <c>withHostAccess: true</c>.
    /// </remarks>
    private static async Task<string> SaveMacroAsync(RunningHost host, string bodyJson)
    {
        var response = await host.HostClient.PostAsync(ApiPaths.Macros, new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Macros_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync(ApiPaths.Macros)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync(ApiPaths.MacrosVocabulary)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsync(ApiPaths.Macros, content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsync(ApiPaths.MacrosCopy, content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsync(ApiPaths.MacrosDelete, content: null)).StatusCode);
    }

    /// <summary>
    /// The list endpoint <c>ref/docs/editor.md</c> has named as missing
    /// since the editor shipped: shipped and user macros together, each
    /// marked for which it is, so the action picker has something to offer
    /// for its MACROS group and the <c>macro</c> field the assign route
    /// already accepted finally has something to carry.
    /// </summary>
    [Fact]
    public async Task Macros_ListsShippedAndUserMacrosTogether_AfterOneIsSavedThroughTheApi()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);

        var beforeSave = await client.GetFromJsonAsync<List<MacrosEndpoint.MacroDto>>(ApiPaths.Macros, JsonOptions);
        Assert.Contains(beforeSave!, m => m.Id == "disembark" && m.IsShipped);
        Assert.DoesNotContain(beforeSave!, m => !m.IsShipped);

        var id = await SaveMacroAsync(host, """{ "name": "Mine", "steps": [ { "press": "LandingGearToggle", "repeat": 1 } ] }""");

        var afterSave = await client.GetFromJsonAsync<List<MacrosEndpoint.MacroDto>>(ApiPaths.Macros, JsonOptions);
        Assert.Contains(afterSave!, m => m.Id == "disembark" && m.IsShipped);
        var mine = Assert.Single(afterSave!, m => m.Id == id);
        Assert.False(mine.IsShipped);
        Assert.Equal("Mine", mine.Name);
        Assert.StartsWith(UserMacroIds.Prefix, id);
    }

    /// <summary>
    /// [2026-09-09] The field the on-device builder edits, proved over a real
    /// round trip rather than in isolation. <c>MacroDto.Definition</c> is a
    /// <c>JsonNode</c>, which is the one property on any of these responses
    /// whose serialization is not obviously the framework's default shape -
    /// and if it arrived as an escaped string, or as <c>{}</c>, the builder
    /// would load a macro with no steps and quietly offer to save it that
    /// way.
    ///
    /// Asserted by parsing what came off the wire back through the grammar
    /// and checking a value the sibling <c>Steps</c> projection does not
    /// carry at all - <c>repeat</c> - since that is exactly what this field
    /// exists to deliver.
    /// </summary>
    [Fact]
    public async Task Macros_ListRowsCarryTheStoredDefinition_AndItSurvivesTheWireIntactEnoughToParse()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);

        await SaveMacroAsync(host, """{ "name": "Mine", "steps": [ { "press": "LandingGearToggle", "repeat": 7 } ] }""");

        var macros = await client.GetFromJsonAsync<List<MacrosEndpoint.MacroDto>>(ApiPaths.Macros, JsonOptions);
        var mine = Assert.Single(macros!, m => !m.IsShipped);

        Assert.NotNull(mine.Definition);
        var parsed = MacroDefinition.Parse(mine.Definition!.ToJsonString());
        Assert.Equal(7, Assert.IsType<PressStep>(Assert.Single(parsed.Steps)).Repeat);
    }

    /// <summary>
    /// The whole reason <c>MacroCatalogue</c> exists. Before it,
    /// <c>MacroLoader.LoadShipped()</c> ran once at startup and the press
    /// handler closed over that list, so a macro saved on the device would
    /// not have fired until LunaPanel was restarted - the "no file editing,
    /// no restart" promise (<c>ref/docs/editor.md</c>) broken in the most
    /// visible place possible.
    ///
    /// "Fires" here means the press reached
    /// <c>MacroPresser.Start</c> - past the annotation, past
    /// <c>PressEndpoint.Evaluate</c>'s refusals - and was answered by the
    /// injection guard, which is as far as anything can get in a synthetic
    /// environment with no Elite Dangerous in the foreground. That is the
    /// same boundary every other press test in this file stops at, and the
    /// distinction that matters: a macro the handler could not see would
    /// have been refused as <c>NotUsable</c> long before this point.
    ///
    /// [2026-09-17, O28 - SUPERSEDED. The accepted set used to be
    /// <c>Sent</c>/<c>GameNotForeground</c>/<c>UipiSuspected</c>/<c>MacroAborted</c>/
    /// <c>Busy</c>/<c>Cancelled</c>: the press response carried the run's
    /// final outcome. It now answers <c>Started</c> the moment a run is in
    /// flight and the final outcome arrives on the live channel instead
    /// (<see cref="Press_MacroSlot_WithGuardPassing_AnswersStartedAtOnce_AndTheOutcomeArrivesOnTheLiveChannel"/>),
    /// so <c>Sent</c>/<c>MacroAborted</c> can never be an immediate answer
    /// any more and <c>Started</c> can.]
    /// </summary>
    [Fact]
    public async Task Macros_AMacroSavedThroughTheApi_FiresFromASlot_WithNoRestart()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");

        var id = await SaveMacroAsync(host, """{ "name": "Gear", "steps": [ { "press": "LandingGearToggle", "repeat": 1 } ] }""");

        var assignResponse = await client.PostAsJsonAsync(ApiPaths.SlotAssign, new SlotEditEndpoint.AssignRequest(0, 0, null, id, null));
        Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);

        var panel = await client.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        var slot = panel!.Slots.Single(s => s.Index == 0);
        Assert.Equal("Ok", slot.Status);
        Assert.Equal("Gear", slot.Label);

        var pressResponse = await client.PostAsJsonAsync(ApiPaths.Press, new { page = 0, slot = 0 }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, pressResponse.StatusCode);
        using var body = JsonDocument.Parse(await pressResponse.Content.ReadAsStringAsync());
        var outcome = body.RootElement.GetProperty("outcome").GetString();

        // Not "NotUsable"/"Refused": those are the answers a macro the
        // handler could not see would have produced. And never a FINAL run
        // outcome - those no longer come back on this response at all.
        Assert.Contains(outcome, new[] { "Started", "GameNotForeground", "UipiSuspected", "Busy", "Cancelled" });
        Assert.DoesNotContain(outcome, new[] { "Sent", "MacroAborted" });
    }

    /// <summary>
    /// The three synchronous outcomes keep their pre-O28 wire shape,
    /// unchanged: a macro press the guard refuses is answered at once, in the
    /// same request, with <c>steps</c> (empty - nothing ran) and
    /// <c>failedStepIndex</c> both present, exactly as before. This host uses
    /// the default <see cref="SafeGuardRefusingKeyInjector"/> (like every test
    /// in this file that does not ask for a different one), whose fake
    /// foreground context deterministically never matches
    /// <see cref="InjectionGuard.EliteProcessNames"/> - so this test takes the
    /// guard-refused branch by construction, not by luck of whatever window
    /// happens to be focused on the machine running the suite.
    ///
    /// [2026-09-17, O28 - SUPERSEDED. This was
    /// <c>Press_MacroSlot_ResponseCarriesAStepsArray_AlongsideFailedStepIndex</c>,
    /// pinning that EVERY macro press response carried <c>steps</c>. That is
    /// no longer the claim: a run that genuinely starts now answers
    /// <c>{fired:true, outcome:"Started"}</c> with neither field, and the
    /// steps travel on the live channel - see
    /// <see cref="Press_MacroSlot_WithGuardPassing_AnswersStartedAtOnce_AndTheOutcomeArrivesOnTheLiveChannel"/>.
    /// What survives here is the synchronous-refusal half of the old claim,
    /// which the guard-refusing default injector was the only thing this test
    /// ever actually drove anyway.]
    /// </summary>
    [Fact]
    public async Task Press_MacroSlot_RefusedByTheGuard_StillAnswersSynchronously_WithAStepsArrayAndFailedStepIndex()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");

        var id = await SaveMacroAsync(host, """{ "name": "Gear", "steps": [ { "press": "LandingGearToggle", "repeat": 1 } ] }""");
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync(ApiPaths.SlotAssign, new SlotEditEndpoint.AssignRequest(0, 0, null, id, null))).StatusCode);

        var pressResponse = await client.PostAsJsonAsync(ApiPaths.Press, new { page = 0, slot = 0 }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, pressResponse.StatusCode);
        using var body = JsonDocument.Parse(await pressResponse.Content.ReadAsStringAsync());
        var root = body.RootElement;

        Assert.Equal("GameNotForeground", root.GetProperty("outcome").GetString());
        Assert.False(root.GetProperty("fired").GetBoolean());
        Assert.True(root.TryGetProperty("steps", out var steps));
        Assert.Equal(JsonValueKind.Array, steps.ValueKind);
        // No Elite in this environment - the guard refuses before any step
        // runs, so the array is empty rather than absent.
        Assert.Equal(0, steps.GetArrayLength());
        Assert.True(root.TryGetProperty("failedStepIndex", out _));
    }

    /// <summary>
    /// Closes O28's last remaining clause (<c>tests/notes/open-items.md</c>):
    /// <c>POST /api/press</c> no longer blocks on a macro run. Driven the way
    /// <see cref="Press_LatchSlot_WithGuardPassing_ActuallySendsTheChord_AndTheLiveChannelReportsItHeld"/>
    /// is - a real host, a guard-PASSING recording injector (this test's own
    /// fakes, never a real Win32 API), a real SSE round trip - so both halves
    /// of the new contract are observed rather than inferred: the press
    /// answers <c>{fired:true, outcome:"Started"}</c> with neither
    /// <c>steps</c> nor <c>failedStepIndex</c>, and the run's real outcome,
    /// with its per-step read-back, then arrives as <c>macroFinished</c> on
    /// the SAME device's already-open live channel, keyed by the macro's id.
    /// </summary>
    [Fact]
    public async Task Press_MacroSlot_WithGuardPassing_AnswersStartedAtOnce_AndTheOutcomeArrivesOnTheLiveChannel()
    {
        var sent = new List<NativeMethods.INPUT>();
        var recordingInjector = new Win32KeyInjector(
            new DiagnosticRingBuffer(64),
            () => false,
            () => new ForegroundContext("EliteDangerous64", null, null),
            inputs =>
            {
                sent.AddRange(inputs);
                return ((uint)inputs.Length, 0);
            });

        await using var host = await RunningHost.StartAsync(withHostAccess: true, keyInjector: recordingInjector);
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var id = await SaveMacroAsync(host, """{ "name": "Gear", "steps": [ { "press": "LandingGearToggle", "repeat": 1 } ] }""");
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync(ApiPaths.SlotAssign, new SlotEditEndpoint.AssignRequest(0, 0, null, id, null))).StatusCode);

        using var response = await client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var initial = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Equal(JsonValueKind.Null, initial.RootElement.GetProperty("macroFinished").ValueKind);

        var pressResponse = await client.PostAsJsonAsync(ApiPaths.Press, new { page = 0, slot = 0 }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, pressResponse.StatusCode);
        using var pressBody = JsonDocument.Parse(await pressResponse.Content.ReadAsStringAsync());
        var root = pressBody.RootElement;

        // The immediate answer: started, and nothing else - the fields that
        // used to carry the final outcome are ABSENT, not null, so a client
        // cannot mistake this for a finished run with no steps.
        Assert.True(root.GetProperty("fired").GetBoolean());
        Assert.Equal("Started", root.GetProperty("outcome").GetString());
        Assert.False(root.TryGetProperty("steps", out _));
        Assert.False(root.TryGetProperty("failedStepIndex", out _));

        // The real outcome, on the live channel, once the run has ended.
        using var finished = await ReadUntilMacroFinishedAsync(reader, TimeSpan.FromSeconds(5));
        var macroFinished = finished.RootElement.GetProperty("macroFinished");
        Assert.Equal(id, macroFinished.GetProperty("macroId").GetString());
        Assert.True(macroFinished.GetProperty("fired").GetBoolean());
        Assert.Equal("Sent", macroFinished.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, macroFinished.GetProperty("failedStepIndex").ValueKind);
        var step = Assert.Single(macroFinished.GetProperty("steps").EnumerateArray());
        Assert.Equal(0, step.GetProperty("stepIndex").GetInt32());
        Assert.Equal("press", step.GetProperty("stepKind").GetString());
        Assert.Equal("Succeeded", step.GetProperty("outcome").GetString());

        // By the time the outcome arrives the run is over, so the same push
        // already draws the button dark - the glow and the outcome never
        // disagree. And the press genuinely reached this test's own fake.
        Assert.Equal("Off", LitOf(finished, 0));
        Assert.Equal(2, sent.Count); // one down, one up
    }

    /// <summary>
    /// The correlation is by macro id, not by arrival order. Two different
    /// macros are in flight at once (both wait-only, so neither needs the
    /// keyboard and the injection lock lets them overlap); the SECOND one
    /// pressed finishes FIRST, and each outcome names its own id - so a
    /// client matching outcomes to presses by "next one to arrive" would be
    /// told the wrong thing, and one keyed by <c>macroId</c> is not.
    /// </summary>
    [Fact]
    public async Task Press_TwoMacrosInFlightAtOnce_EachOutcomeArrivesKeyedByItsOwnMacroId_NotInPressOrder()
    {
        var recordingInjector = new Win32KeyInjector(
            new DiagnosticRingBuffer(64),
            () => false,
            () => new ForegroundContext("EliteDangerous64", null, null),
            inputs => ((uint)inputs.Length, 0));

        await using var host = await RunningHost.StartAsync(withHostAccess: true, keyInjector: recordingInjector);
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var slowId = await SaveMacroAsync(host, """{ "name": "Slow", "steps": [ { "wait": 1000 } ] }""");
        var quickId = await SaveMacroAsync(host, """{ "name": "Quick", "steps": [ { "wait": 100 } ] }""");
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync(ApiPaths.SlotAssign, new SlotEditEndpoint.AssignRequest(0, 0, null, slowId, null))).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync(ApiPaths.SlotAssign, new SlotEditEndpoint.AssignRequest(0, 1, null, quickId, null))).StatusCode);

        using var response = await client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var initial = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));

        // The slow one first, then the quick one - so press order and
        // finish order are opposites.
        using var slowPress = JsonDocument.Parse(await (await client.PostAsJsonAsync(ApiPaths.Press, new { page = 0, slot = 0 }, JsonOptions)).Content.ReadAsStringAsync());
        Assert.Equal("Started", slowPress.RootElement.GetProperty("outcome").GetString());
        using var quickPress = JsonDocument.Parse(await (await client.PostAsJsonAsync(ApiPaths.Press, new { page = 0, slot = 1 }, JsonOptions)).Content.ReadAsStringAsync());
        Assert.Equal("Started", quickPress.RootElement.GetProperty("outcome").GetString());

        using var first = await ReadUntilMacroFinishedAsync(reader, TimeSpan.FromSeconds(5));
        var firstFinished = first.RootElement.GetProperty("macroFinished");
        Assert.Equal(quickId, firstFinished.GetProperty("macroId").GetString());
        Assert.Equal("Sent", firstFinished.GetProperty("outcome").GetString());
        Assert.Equal("wait", Assert.Single(firstFinished.GetProperty("steps").EnumerateArray()).GetProperty("stepKind").GetString());

        using var second = await ReadUntilMacroFinishedAsync(reader, TimeSpan.FromSeconds(5));
        var secondFinished = second.RootElement.GetProperty("macroFinished");
        Assert.Equal(slowId, secondFinished.GetProperty("macroId").GetString());
        Assert.Equal("Sent", secondFinished.GetProperty("outcome").GetString());
    }

    /// <summary>
    /// The outcome is ADDRESSED: it reaches the live channel of the device
    /// that pressed, and no other - the same <c>deviceId</c> filter
    /// <c>OnLayoutSaved</c> applies. Without this pin, dropping that filter
    /// reddens nothing (every other test here has one device). Proven
    /// deterministically rather than by waiting-and-hoping: after the
    /// pressing device has SEEN its outcome (so the broadcast has already
    /// run for every subscriber), a timing save - which every device
    /// receives - is posted, and the other device's channel is read up to
    /// that push. Anything wrongly delivered to it would have to be in that
    /// window, because its channel is FIFO and the broadcast preceded the
    /// save.
    /// </summary>
    [Fact]
    public async Task PanelLive_AMacroOutcome_ReachesOnlyTheDeviceThatPressed_NotAnotherPairedDevice()
    {
        var recordingInjector = new Win32KeyInjector(
            new DiagnosticRingBuffer(64),
            () => false,
            () => new ForegroundContext("EliteDangerous64", null, null),
            inputs => ((uint)inputs.Length, 0));

        await using var host = await RunningHost.StartAsync(withHostAccess: true, keyInjector: recordingInjector);

        // Device A: paired, layout seeded, the macro on slot 0, live channel
        // open - and left open while the cookie moves on to device B.
        var deviceA = await PairAsync(host, deviceName: "Luna phone", deviceClass: "phone");
        await UseFullyBoundStarterBindingsAsync(host, host.Client);
        await host.Client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var id = await SaveMacroAsync(host, """{ "name": "Quick", "steps": [ { "wait": 50 } ] }""");
        Assert.Equal(
            HttpStatusCode.OK,
            (await host.Client.PostAsJsonAsync(ApiPaths.SlotAssign, new SlotEditEndpoint.AssignRequest(0, 0, null, id, null))).StatusCode);
        using var responseA = await host.Client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        using var streamA = await responseA.Content.ReadAsStreamAsync();
        using var readerA = new StreamReader(streamA);
        using var initialA = await ReadNextSseEventAsync(readerA, TimeSpan.FromSeconds(5));

        // Device B: a genuinely different device (PairAsync's own remarks),
        // same macro on ITS slot 0, its own live channel.
        var deviceB = await PairAsync(host, deviceName: "Bridge slate", deviceClass: "tablet");
        Assert.NotEqual(deviceA.DeviceId, deviceB.DeviceId);
        await host.Client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        Assert.Equal(
            HttpStatusCode.OK,
            (await host.Client.PostAsJsonAsync(ApiPaths.SlotAssign, new SlotEditEndpoint.AssignRequest(0, 0, null, id, null))).StatusCode);
        using var responseB = await host.Client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        using var streamB = await responseB.Content.ReadAsStreamAsync();
        using var readerB = new StreamReader(streamB);
        using var initialB = await ReadNextSseEventAsync(readerB, TimeSpan.FromSeconds(5));

        // B presses. B sees the outcome.
        using var press = JsonDocument.Parse(await (await host.Client.PostAsJsonAsync(ApiPaths.Press, new { page = 0, slot = 0 }, JsonOptions)).Content.ReadAsStringAsync());
        Assert.Equal("Started", press.RootElement.GetProperty("outcome").GetString());
        using var finishedOnB = await ReadUntilMacroFinishedAsync(readerB, TimeSpan.FromSeconds(5));
        Assert.Equal(id, finishedOnB.RootElement.GetProperty("macroFinished").GetProperty("macroId").GetString());

        // The broadcast has now run for every subscriber. A marker push A
        // WILL receive, posted strictly after it.
        Assert.Equal(
            HttpStatusCode.OK,
            (await host.Client.PostAsJsonAsync(ApiPaths.MacroTiming, new MacroTimingEndpoint.Request(275, 180), JsonOptions)).StatusCode);

        // Everything on A's channel up to and including that marker: the
        // run's own lit/dark pushes are fine (the glow is machine-wide by
        // design), but the OUTCOME must not be among them.
        for (var i = 0; i < 8; i++)
        {
            using var onA = await ReadNextSseEventAsync(readerA, TimeSpan.FromSeconds(5));
            Assert.Equal(JsonValueKind.Null, onA.RootElement.GetProperty("macroFinished").ValueKind);
            if (onA.RootElement.GetProperty("timing").ValueKind != JsonValueKind.Null)
            {
                return;
            }
        }

        Assert.Fail("Device A never received the timing marker push.");
    }

    /// <summary>
    /// Renaming changes the name and never the id, so every slot naming the
    /// macro keeps working. Driven through a slot rather than only the
    /// macro list, because "the id did not move" is only interesting as
    /// "the button still works and now says something else".
    /// </summary>
    [Fact]
    public async Task Macros_Renaming_KeepsTheId_SoASlotNamingItKeepsWorking()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");

        var id = await SaveMacroAsync(host, """{ "name": "Before", "steps": [ { "press": "LandingGearToggle", "repeat": 1 } ] }""");
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync(ApiPaths.SlotAssign, new SlotEditEndpoint.AssignRequest(0, 0, null, id, null))).StatusCode);

        var renamedId = await SaveMacroAsync(host, $$"""{ "id": "{{id}}", "name": "After", "steps": [ { "press": "LandingGearToggle", "repeat": 1 } ] }""");
        Assert.Equal(id, renamedId);

        var panel = await client.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        var slot = panel!.Slots.Single(s => s.Index == 0);
        Assert.Equal("Ok", slot.Status);
        Assert.Equal("After", slot.Label);
    }

    /// <summary>
    /// Copy-to-edit, end to end: the copy is a user macro with its own id,
    /// and editing it does not reach the shipped original, which is still
    /// listed and still shipped.
    /// </summary>
    [Fact]
    public async Task Macros_CopyingAShippedMacro_ProducesAnIndependentUserMacro()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);

        var copyResponse = await host.HostClient.PostAsJsonAsync(ApiPaths.MacrosCopy, new { id = "disembark", name = "Mine" }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, copyResponse.StatusCode);
        using var copyBody = JsonDocument.Parse(await copyResponse.Content.ReadAsStringAsync());
        var copyId = copyBody.RootElement.GetProperty("id").GetString()!;

        Assert.NotEqual("disembark", copyId);
        Assert.StartsWith(UserMacroIds.Prefix, copyId);

        var macros = await client.GetFromJsonAsync<List<MacrosEndpoint.MacroDto>>(ApiPaths.Macros, JsonOptions);
        var shipped = Assert.Single(macros!, m => m.Id == "disembark");
        var copy = Assert.Single(macros!, m => m.Id == copyId);
        Assert.True(shipped.IsShipped);
        Assert.False(copy.IsShipped);
        Assert.Equal("Disembark", shipped.Name);
        Assert.Equal("Mine", copy.Name);
        Assert.Equal(
            shipped.Steps.Select(s => s.Summary),
            copy.Steps.Select(s => s.Summary));

        // Editing the copy leaves the shipped macro exactly where it was.
        await SaveMacroAsync(host, $$"""{ "id": "{{copyId}}", "name": "Mine", "steps": [ { "wait": 1 } ] }""");
        var after = await client.GetFromJsonAsync<List<MacrosEndpoint.MacroDto>>(ApiPaths.Macros, JsonOptions);
        // [SUPERSEDED 2026-09-17: was 4, disembark's step count before it
        // became a single `branch` whose two arms hold what used to be four
        // top-level steps. This projection lists TOP-LEVEL steps only, by
        // design - see MacrosEndpoint.SummaryOf's branch case.]
        Assert.Single(Assert.Single(after!, m => m.Id == "disembark").Steps);
        Assert.Single(Assert.Single(after!, m => m.Id == copyId).Steps);
    }

    /// <summary>
    /// The staleness line end to end (<c>ref/docs/macro-builder.md</c>,
    /// question 3): a fresh copy names its source and reports itself as not
    /// stale, and a later plain edit of the copy - one that never mentions
    /// the source again - still carries that provenance afterwards, because
    /// <c>ServerHostBuilder.SaveMacro</c> carries it forward rather than
    /// treating an ordinary save as "no source" and erasing it.
    /// </summary>
    [Fact]
    public async Task Macros_ACopy_NamesItsSourceAndIsNotStale_AndKeepsThatAfterAPlainEdit()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);

        var copyResponse = await host.HostClient.PostAsJsonAsync(ApiPaths.MacrosCopy, new { id = "disembark", name = "Mine" }, JsonOptions);
        using var copyBody = JsonDocument.Parse(await copyResponse.Content.ReadAsStringAsync());
        var copyId = copyBody.RootElement.GetProperty("id").GetString()!;

        var macros = await client.GetFromJsonAsync<List<MacrosEndpoint.MacroDto>>(ApiPaths.Macros, JsonOptions);
        var copy = Assert.Single(macros!, m => m.Id == copyId);
        Assert.Equal("disembark", copy.SourceMacroId);
        Assert.Equal("Disembark", copy.SourceMacroName);
        Assert.False(copy.IsStale);

        await SaveMacroAsync(host, $$"""{ "id": "{{copyId}}", "name": "Mine", "steps": [ { "wait": 1 } ] }""");

        var after = await client.GetFromJsonAsync<List<MacrosEndpoint.MacroDto>>(ApiPaths.Macros, JsonOptions);
        var editedCopy = Assert.Single(after!, m => m.Id == copyId);
        Assert.Equal("disembark", editedCopy.SourceMacroId);
        Assert.False(editedCopy.IsStale);
    }

    [Fact]
    public async Task Macros_AShippedMacro_CannotBeEditedOrDeleted()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);

        var editResponse = await host.HostClient.PostAsync(
            ApiPaths.Macros,
            new StringContent("""{ "id": "disembark", "name": "Mine", "steps": [ { "wait": 1 } ] }""", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, editResponse.StatusCode);

        var deleteResponse = await host.HostClient.PostAsJsonAsync(ApiPaths.MacrosDelete, new { id = "disembark" }, JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, deleteResponse.StatusCode);

        var macros = await client.GetFromJsonAsync<List<MacrosEndpoint.MacroDto>>(ApiPaths.Macros, JsonOptions);
        var stillThere = Assert.Single(macros!, m => m.Id == "disembark");
        Assert.Equal("Disembark", stillThere.Name);
        // [SUPERSEDED 2026-09-17: was 4 - disembark's top-level step count
        // before it became a single `branch`. The claim ("the refused edit
        // changed nothing") is unchanged; the edit attempted above supplies
        // one `wait` step, so a count of 1 here would be ambiguous with the
        // edit having landed - hence the shape assertion beside it, which
        // the attempted edit could not have produced.]
        Assert.Single(stillThere.Steps);
        Assert.Equal("branch", Assert.Single(stillThere.Steps).Kind);
    }

    /// <summary>
    /// Deleting a macro a slot still names does not break the layout, and
    /// nothing counts references to stop it: the slot degrades to
    /// <c>UnknownMacro</c> and refuses at press time, exactly as the starter
    /// layout's slot 0 did for months before <c>request-docking</c> existed
    /// (<c>ref/docs/macro-builder.md</c>).
    /// </summary>
    [Fact]
    public async Task Macros_DeletingOneASlotNames_LeavesTheSlotDegraded_NeverBreaksTheLayout()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");

        var id = await SaveMacroAsync(host, """{ "name": "Doomed", "steps": [ { "press": "LandingGearToggle", "repeat": 1 } ] }""");
        await client.PostAsJsonAsync(ApiPaths.SlotAssign, new SlotEditEndpoint.AssignRequest(0, 0, null, id, null));

        var deleteResponse = await host.HostClient.PostAsJsonAsync(ApiPaths.MacrosDelete, new { id }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var panelResponse = await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        Assert.Equal(HttpStatusCode.OK, panelResponse.StatusCode);
        var panel = await panelResponse.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);
        Assert.Equal("UnknownMacro", panel!.Slots.Single(s => s.Index == 0).Status);

        var pressResponse = await client.PostAsJsonAsync(ApiPaths.Press, new { page = 0, slot = 0 }, JsonOptions);
        using var pressBody = JsonDocument.Parse(await pressResponse.Content.ReadAsStringAsync());
        Assert.False(pressBody.RootElement.GetProperty("fired").GetBoolean());
    }

    /// <summary>
    /// A macro the grammar cannot read is refused with the grammar's own
    /// message - which names the offending step - and nothing is written.
    /// Never a 500: user content that fails to parse is a reported outcome
    /// here, not an exception escaping a handler
    /// (<c>ref/docs/macro-builder.md</c>'s first code change).
    /// </summary>
    [Fact]
    public async Task Macros_MalformedContent_Returns400_NamingTheStep_AndSavesNothing()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);

        var response = await host.HostClient.PostAsync(
            ApiPaths.Macros,
            new StringContent("""{ "name": "Bad", "steps": [ { "wait": 5 }, { "press": "UI_Down" } ] }""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = body.RootElement.GetProperty("error").GetString();
        Assert.Contains("step 1", error!);

        var macros = await client.GetFromJsonAsync<List<MacrosEndpoint.MacroDto>>(ApiPaths.Macros, JsonOptions);
        Assert.DoesNotContain(macros!, m => !m.IsShipped);
    }

    [Fact]
    public async Task Macros_NotEvenJson_Returns400_NotAnUnhandledException()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);

        var response = await host.HostClient.PostAsync(ApiPaths.Macros, new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A macro is machine-wide, not per device: a second paired device -
    /// which is exactly what a re-pair produces, a brand new device id -
    /// sees the first one's macros. This is the property that stops a
    /// recovered layout from arriving with every macro slot rendering
    /// <c>UnknownMacro</c> (<c>ref/docs/macro-builder.md</c>, question 2).
    /// </summary>
    [Fact]
    public async Task Macros_AreSharedAcrossDevices_SoARePairDoesNotOrphanThem()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);

        var first = await PairAsync(host, deviceName: "Luna phone", deviceClass: "phone");
        var id = await SaveMacroAsync(host, """{ "name": "Shared", "steps": [ { "wait": 5 } ] }""");

        // Pairing a second device replaces host.Client's cookie with the
        // SECOND device's - PairAsync's own remarks. A re-pair is exactly
        // this: a brand new device id for the same physical device.
        var second = await PairAsync(host, deviceName: "Bridge slate", deviceClass: "tablet");
        Assert.NotEqual(first.DeviceId, second.DeviceId);

        var macros = await host.Client.GetFromJsonAsync<List<MacrosEndpoint.MacroDto>>(ApiPaths.Macros, JsonOptions);

        Assert.Equal("Shared", Assert.Single(macros!, m => m.Id == id).Name);
    }

    // -------------------------------------------------------------------
    // Authoring is the PC's job (ref/docs/macro-builder.md's "Authoring
    // moved to the PC"). Everything below drives BOTH listeners of a real
    // two-listener host: host.Client is a paired device on the LAN listener
    // and host.HostClient is the PC's own browser on the loopback-only one.
    // They differ in exactly the way production's differ - the port the
    // connection arrived on - which is what HostRequest.IsFromHost reads.
    // -------------------------------------------------------------------

    /// <summary>
    /// The refusal, and the one that has to hold: a fully paired,
    /// fully authenticated device is still refused every route that writes
    /// a macro. 403 and not 401 - its identity was never in doubt - and the
    /// body names where the builder actually is.
    /// </summary>
    [Fact]
    public async Task MacroAuthoring_FromAPairedDevice_Returns403_NamingWhereTheBuilderIs()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);

        var save = await client.PostAsync(
            ApiPaths.Macros,
            new StringContent("""{ "name": "Mine", "steps": [ { "wait": 5 } ] }""", System.Text.Encoding.UTF8, "application/json"));
        var copy = await client.PostAsJsonAsync(ApiPaths.MacrosCopy, new { id = "disembark", name = "Mine" }, JsonOptions);
        var delete = await client.PostAsJsonAsync(ApiPaths.MacrosDelete, new { id = "user-whatever" }, JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, save.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, copy.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);

        using var body = JsonDocument.Parse(await save.Content.ReadAsStringAsync());
        Assert.Equal(HostOnlyRoutes.NotTheHostAdvice, body.RootElement.GetProperty("error").GetString());

        // And nothing was written on the way to being refused.
        var macros = await client.GetFromJsonAsync<List<MacrosEndpoint.MacroDto>>(ApiPaths.Macros, JsonOptions);
        Assert.DoesNotContain(macros!, m => !m.IsShipped);
    }

    /// <summary>
    /// The other half, and the one that would be quietly broken by an
    /// over-wide gate: a tablet still reads the macro set. This is how a
    /// macro reaches the action picker's MACROS group and therefore a
    /// button, which is the whole of what a device is still meant to do
    /// with macros.
    /// </summary>
    [Fact]
    public async Task MacroReading_FromAPairedDevice_StillServes_IncludingOneAuthoredOnThePc()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);

        var id = await SaveMacroAsync(host, """{ "name": "From the PC", "steps": [ { "wait": 5 } ] }""");

        var list = await client.GetAsync(ApiPaths.Macros);
        var vocabulary = await client.GetAsync(ApiPaths.MacrosVocabulary);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, vocabulary.StatusCode);

        var macros = await list.Content.ReadFromJsonAsync<List<MacrosEndpoint.MacroDto>>(JsonOptions);
        Assert.Equal("From the PC", Assert.Single(macros!, m => m.Id == id).Name);
    }

    // -------------------------------------------------------------------
    // Import and export as files (ref/docs/transfer.md). Driven through a
    // real running host with both listeners bound, because the claim that
    // matters is about WHICH LISTENER a request arrived on - and that is
    // exactly the thing a unit test of the handler cannot see.
    // -------------------------------------------------------------------

    /// <summary>
    /// The enforcement half of the pane the client hides on a device. All
    /// four routes, under the method each is actually served with AND under
    /// the other one, because the transfer rule is "host-only whatever the
    /// method" and a gate copied from the authoring three would refuse only
    /// <c>POST</c> - leaving <c>GET</c> handing a paired tablet every
    /// device name in the household and any commander's whole arrangement.
    /// </summary>
    [Fact]
    public async Task Transfer_FromAPairedDevice_Returns403_NamingWhereItLives()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);

        var responses = new[]
        {
            await client.GetAsync(ApiPaths.TransferTargets),
            await client.GetAsync($"{ApiPaths.TransferProfile}?deviceId=whatever"),
            await client.GetAsync(ApiPaths.TransferMacro),
            await client.PostAsync(ApiPaths.TransferTargets, content: null),
            await client.PostAsync($"{ApiPaths.TransferProfile}?deviceId=whatever", new StringContent("{}", System.Text.Encoding.UTF8, "application/json")),
            await client.PostAsync(ApiPaths.TransferMacro, new StringContent("{}", System.Text.Encoding.UTF8, "application/json")),
            await client.PostAsync($"{ApiPaths.TransferUndo}?deviceId=whatever", content: null),
        };

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using var body = JsonDocument.Parse(await responses[0].Content.ReadAsStringAsync());
        Assert.Equal(HostOnlyRoutes.NotTheHostTransferAdvice, body.RootElement.GetProperty("error").GetString());
    }

    /// <summary>
    /// The other direction, and the one a too-wide refusal would break: the
    /// PC reaches all of it with no pairing code at all.
    /// </summary>
    [Fact]
    public async Task Transfer_FromTheHost_IsServed_WithNoPairingCode()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);

        var targets = await host.HostClient.GetAsync(ApiPaths.TransferTargets);

        Assert.Equal(HttpStatusCode.OK, targets.StatusCode);
        Assert.Empty(host.App.Services.GetRequiredService<DeviceRegistry>().ListDevices());

        using var body = JsonDocument.Parse(await targets.Content.ReadAsStringAsync());
        var listed = body.RootElement.GetProperty("targets").EnumerateArray().ToList();
        Assert.Contains(listed, t => t.GetProperty("deviceId").GetString() == HostRequest.HostDeviceId);
    }

    /// <summary>
    /// The whole trip, through a real host: draw the PC's panel so it has an
    /// arrangement, export it, change it, import the file back, and read the
    /// arrangement out again.
    ///
    /// The export is asserted to arrive as a <b>download</b>, with the file
    /// name the server chose. Nothing else in this suite sees that header,
    /// and without it the page would navigate a commander onto a screenful
    /// of JSON instead of saving a file.
    /// </summary>
    [Fact]
    public async Task Transfer_TheHostsOwnProfile_ExportsAsADownload_AndImportsBack()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);

        // Without a preset that binds the starter's own actions,
        // LayoutValidator refuses the seeded starter and layout-host.json is
        // never written - so there would be nothing to export and this test
        // would fail for a reason that has nothing to do with transfer.
        await UseFullyBoundStarterBindingsAsync(host, host.HostClient);

        // Seeds layout-host.json through the ordinary panel path.
        Assert.Equal(HttpStatusCode.OK, (await host.HostClient.GetAsync($"{ApiPaths.Panel}?w=1280&h=800")).StatusCode);

        var export = await host.HostClient.GetAsync($"{ApiPaths.TransferProfile}?deviceId={HostRequest.HostDeviceId}");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("attachment", export.Content.Headers.ContentDisposition!.DispositionType);
        Assert.EndsWith(
            LunaPanel.Core.Transfer.TransferFile.FileExtension,
            export.Content.Headers.ContentDisposition.FileName!.Trim('"'),
            StringComparison.Ordinal);

        var file = await export.Content.ReadAsStringAsync();
        var before = await ExportedLayoutAsync(host);

        // Change something first, so the import has to be what puts it back.
        var cleared = await host.HostClient.PostAsJsonAsync(ApiPaths.SlotClear, new SlotEditEndpoint.ClearRequest(0, 1), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.NotEqual(before, await ExportedLayoutAsync(host));

        var import = await host.HostClient.PostAsync(
            $"{ApiPaths.TransferProfile}?deviceId={HostRequest.HostDeviceId}",
            new StringContent(file, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        Assert.Equal(before, await ExportedLayoutAsync(host));
    }

    /// <summary>
    /// A macro authored on the PC leaves as a file and comes back as the
    /// same macro - and the second import of that same file adds nothing,
    /// which is the "add, never overwrite" ruling
    /// (<c>ref/docs/transfer.md</c>) seen from the wire.
    /// </summary>
    [Fact]
    public async Task Transfer_AMacro_ExportsAndImportsBack_AndTheSecondImportAddsNothing()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var id = await SaveMacroAsync(host, """{ "name": "Travelling", "steps": [ { "wait": 5 } ] }""");

        var export = await host.HostClient.GetAsync($"{ApiPaths.TransferMacro}?id={id}");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var file = await export.Content.ReadAsStringAsync();

        // Delete it, so the import is what brings it back rather than the
        // copy that was there all along.
        Assert.Equal(HttpStatusCode.OK, (await host.HostClient.PostAsJsonAsync(ApiPaths.MacrosDelete, new { id }, JsonOptions)).StatusCode);
        var afterDelete = await host.HostClient.GetFromJsonAsync<List<MacrosEndpoint.MacroDto>>(ApiPaths.Macros, JsonOptions);
        Assert.DoesNotContain(afterDelete!, m => m.Id == id);

        var first = await host.HostClient.PostAsync(ApiPaths.TransferMacro, new StringContent(file, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using (var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, firstBody.RootElement.GetProperty("macrosAdded").GetInt32());
        }

        var second = await host.HostClient.PostAsync(ApiPaths.TransferMacro, new StringContent(file, System.Text.Encoding.UTF8, "application/json"));
        using (var secondBody = JsonDocument.Parse(await second.Content.ReadAsStringAsync()))
        {
            Assert.Equal(0, secondBody.RootElement.GetProperty("macrosAdded").GetInt32());
            Assert.Equal(1, secondBody.RootElement.GetProperty("macrosAlreadyPresent").GetInt32());
        }

        var afterImport = await host.HostClient.GetFromJsonAsync<List<MacrosEndpoint.MacroDto>>(ApiPaths.Macros, JsonOptions);
        Assert.Equal("Travelling", Assert.Single(afterImport!, m => m.Id == id).Name);
    }

    /// <summary>
    /// A file that is not one of ours is refused through the real pipeline,
    /// with the sentence a commander reads, and the PC's own arrangement is
    /// untouched afterwards.
    /// </summary>
    [Fact]
    public async Task Transfer_AFileThatIsNotOurs_IsRefused_AndTheArrangementIsUnchanged()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        await UseFullyBoundStarterBindingsAsync(host, host.HostClient);
        Assert.Equal(HttpStatusCode.OK, (await host.HostClient.GetAsync($"{ApiPaths.Panel}?w=1280&h=800")).StatusCode);

        var before = await ExportedLayoutAsync(host);

        var refused = await host.HostClient.PostAsync(
            $"{ApiPaths.TransferProfile}?deviceId={HostRequest.HostDeviceId}",
            new StringContent("""{ "name": "something-else", "version": "1.0.0" }""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        using var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Contains("not a LunaPanel export", body.RootElement.GetProperty("error").GetString()!, StringComparison.Ordinal);

        Assert.Equal(before, await ExportedLayoutAsync(host));
    }

    /// <summary>
    /// The PC's arrangement as the export sees it, reduced to just the
    /// layout. Two exports of an unchanged arrangement are NOT byte-equal -
    /// the envelope carries the moment it was written - so comparing whole
    /// files would fail for a reason that has nothing to do with the
    /// buttons.
    /// </summary>
    private static async Task<string> ExportedLayoutAsync(RunningHost host)
    {
        var response = await host.HostClient.GetAsync($"{ApiPaths.TransferProfile}?deviceId={HostRequest.HostDeviceId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var parsed = LunaPanel.Core.Transfer.TransferFile.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(parsed.Success, parsed.Error);
        return LayoutJson.Serialize(parsed.Layout!);
    }

    /// <summary>
    /// "I don't want to have to use a pairing code if on the PC" - driven.
    /// Nothing pairs anywhere in this test: the registry has no devices at
    /// all, no cookie is ever set, and the panel, the macro list and a macro
    /// save all answer on the loopback listener.
    /// </summary>
    [Fact]
    public async Task HostRequests_NeedNoPairingCodeAtAll()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);

        var panel = await host.HostClient.GetAsync($"{ApiPaths.Panel}?w=1280&h=800");
        var macros = await host.HostClient.GetAsync(ApiPaths.Macros);
        var id = await SaveMacroAsync(host, """{ "name": "No code", "steps": [ { "wait": 5 } ] }""");

        Assert.Equal(HttpStatusCode.OK, panel.StatusCode);
        Assert.Equal(HttpStatusCode.OK, macros.StatusCode);
        Assert.StartsWith(UserMacroIds.Prefix, id);

        // No cookie was ever set on the host client, and no device exists.
        Assert.Empty(host.App.Services.GetRequiredService<DeviceRegistry>().ListDevices());
        Assert.False(panel.Headers.Contains("Set-Cookie"));
    }

    /// <summary>
    /// The same request through the LAN listener with every header a caller
    /// could forge to look local. None of them is read: the decision is the
    /// connection's own endpoints, and there is no <c>UseForwardedHeaders</c>
    /// in this pipeline to turn <c>X-Forwarded-For</c> into one (pinned
    /// separately by <c>HostAccessSourceGuardTests</c>).
    ///
    /// <b>The upward half of this test is the one that bites.</b> Measured
    /// during authoring: no mutation that makes the server read
    /// <c>X-Forwarded-For</c> as the peer address can turn the device's
    /// request into a host one, because the request still arrived on the
    /// device's listener and the port check refuses it on its own. The
    /// downward assertion is therefore a duplicate of the plain refusal with
    /// headers attached - real, but not discriminating. The second
    /// assertion, a <em>host</em> request carrying a foreign forwarded
    /// address that must stay the host, is what a forwarded-headers
    /// middleware would actually break, and it reddens under exactly that
    /// mutation.
    /// </summary>
    [Fact]
    public async Task SpoofedHeaders_DoNotGrantHostStatus_AndDoNotTakeItAwayEither()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);

        var spoofed = new HttpRequestMessage(HttpMethod.Post, ApiPaths.Macros)
        {
            Content = new StringContent("""{ "name": "Spoof", "steps": [ { "wait": 5 } ] }""", System.Text.Encoding.UTF8, "application/json"),
        };
        AddSpoofedLocalHeaders(spoofed, "127.0.0.1");

        var response = await client.SendAsync(spoofed);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // The other direction: the PC's own request, claiming through every
        // forwarded header to be a machine on the far side of the world. It
        // is still the host, because the header was never what said so.
        var disowned = new HttpRequestMessage(HttpMethod.Post, ApiPaths.Macros)
        {
            Content = new StringContent("""{ "name": "Still me", "steps": [ { "wait": 5 } ] }""", System.Text.Encoding.UTF8, "application/json"),
        };
        AddSpoofedLocalHeaders(disowned, "203.0.113.9");

        Assert.Equal(HttpStatusCode.OK, (await host.HostClient.SendAsync(disowned)).StatusCode);
    }

    private static void AddSpoofedLocalHeaders(HttpRequestMessage request, string claimedAddress)
    {
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", claimedAddress);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", claimedAddress);
        request.Headers.TryAddWithoutValidation("X-Real-IP", claimedAddress);
        request.Headers.TryAddWithoutValidation("Host", claimedAddress);
        request.Headers.TryAddWithoutValidation("Origin", $"http://{claimedAddress}");
        request.Headers.TryAddWithoutValidation("Referer", $"http://{claimedAddress}/");
    }

    /// <summary>
    /// The page is the same page; only its authoring constant differs, and
    /// it differs by where the request arrived rather than by anything the
    /// page asked for. Both halves asserted in one test on purpose - the
    /// claim is the difference, and two tests each pinning one value would
    /// both pass against a server that had stopped varying it.
    /// </summary>
    [Fact]
    public async Task ThePage_OffersAuthoringToTheHostOnly()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);

        var fromHost = await (await host.HostClient.GetAsync(ApiPaths.App)).Content.ReadAsStringAsync();
        var fromDevice = await (await host.Client.GetAsync(ApiPaths.App)).Content.ReadAsStringAsync();

        Assert.Contains("const CAN_AUTHOR_MACROS = true;", fromHost, StringComparison.Ordinal);
        Assert.Contains("const CAN_AUTHOR_MACROS = false;", fromDevice, StringComparison.Ordinal);

        // Same page otherwise - the builder was gated, never removed.
        Assert.Contains("id=\"macroBuilder\"", fromDevice, StringComparison.Ordinal);
        Assert.Contains("id=\"macroNew\"", fromDevice, StringComparison.Ordinal);
    }

    /// <summary>
    /// A host with no host listener bound at all - which is every other test
    /// in this file, and what a misconfigured production run would be - has
    /// no unauthenticated way in. Pinned because the whole feature is an
    /// exception to "everything needs a cookie", and an exception that
    /// applied when it was switched off would be a hole rather than a
    /// feature.
    /// </summary>
    [Fact]
    public async Task WithNoHostListener_NothingIsTheHost_AndThePageOffersNoAuthoring()
    {
        await using var host = await RunningHost.StartAsync();

        var panel = await host.Client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var page = await (await host.Client.GetAsync(ApiPaths.App)).Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, panel.StatusCode);
        Assert.Contains("const CAN_AUTHOR_MACROS = false;", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The consequence of the host owning a layout like any other device:
    /// <c>layout-host.json</c> exists on disk and belongs to nobody in
    /// <c>DeviceRegistry.ListDevices()</c>, which is exactly the shape
    /// <c>LayoutImport</c> calls an orphan. Offering the PC's own
    /// arrangement to every tablet as something left behind would be wrong,
    /// and would look like a real orphan rather than a defect.
    /// </summary>
    [Fact]
    public async Task TheHostsOwnLayout_IsNeverOfferedAsAnOrphan()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);

        // Real bindings first, then a panel request from the PC. Both are
        // needed for layout-host.json to actually exist: LayoutAccess.LoadOrSeed
        // falls back to "in memory only for this request" when the seeded
        // layout cannot be validated, which is what an environment with no
        // bindings at all produces - and a layout file that was never
        // written cannot be mistaken for an orphan, so this test passed
        // vacuously until this line was added. Found by mutation: removing
        // the exclusion under test reddened nothing.
        await UseFullyBoundStarterBindingsAsync(host, client);
        Assert.Equal(HttpStatusCode.OK, (await host.HostClient.GetAsync($"{ApiPaths.Panel}?w=1280&h=800")).StatusCode);
        Assert.Contains(
            HostRequest.HostDeviceId,
            host.App.Services.GetRequiredService<LayoutStore>().ListDeviceIds());

        var candidates = await client.GetFromJsonAsync<LayoutImportEndpoint.ListResponse>(ApiPaths.LayoutImport, JsonOptions);

        Assert.DoesNotContain(candidates!.Candidates, c => c.DeviceId == HostRequest.HostDeviceId);
    }

    /// <summary>
    /// The host is a device to every per-device store and to nothing else:
    /// its layout is its own, and it never appears in the devices list,
    /// which reads the registry and the registry alone.
    /// </summary>
    [Fact]
    public async Task TheHost_HasItsOwnLayout_AndIsNotAPairedDevice()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);

        // Real bindings first: without them a saved layout does not survive
        // its next load at all, and this test would compare two starter
        // layouts and pass without either side having its own anything.
        // (Measured, not assumed - the first draft of this test omitted it
        // and stayed green under a mutation that gave the device and the
        // host the same id.)
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync(ApiPaths.SlotLabel, new SlotEditEndpoint.LabelRequest(0, 1, "TABLET"))).StatusCode);

        var devicePanel = await client.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        Assert.Equal("TABLET", devicePanel!.Slots.Single(s => s.Index == 1).Label);

        var hostPanel = await host.HostClient.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        Assert.NotEqual("TABLET", hostPanel!.Slots.Single(s => s.Index == 1).Label);

        var devices = await client.GetFromJsonAsync<DevicesEndpoint.ListResponse>(ApiPaths.Devices, JsonOptions);
        Assert.DoesNotContain(devices!.Devices, d => d.DeviceId == HostRequest.HostDeviceId);
    }

    [Fact]
    public async Task MacrosVocabulary_ServesTheRealTables_NotAClientSideCopy()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var vocabulary = await client.GetFromJsonAsync<MacrosEndpoint.VocabularyResponse>(ApiPaths.MacrosVocabulary, JsonOptions);

        // Some grammar members are deliberately NOT offered by the "add a
        // step" picker - gotoLeftPanelTab (macro-builder.md, question 1,
        // superseded 2026-09-12) and branch (no arm editor yet, 2026-09-17).
        // MacrosEndpointTests holds the exclusions themselves; the served
        // count here is the grammar's count minus that list.
        // [SUPERSEDED 2026-09-17: was `StepKindKeys.Count - 1`, back when
        // gotoLeftPanelTab was the only exception.]
        Assert.Equal(
            MacroDefinition.StepKindKeys.Count - MacrosEndpoint.NotOfferedStepKinds.Count,
            vocabulary!.StepKinds.Count);
        Assert.Equal(
            StatusVocabulary.FlagsConditions.Count + StatusVocabulary.Flags2Conditions.Count,
            vocabulary.Flags.Count);
        Assert.Equal(StatusVocabulary.GuiFocusValues.Count, vocabulary.GuiFocus.Count);
        Assert.Equal(JournalVocabulary.Events.Count, vocabulary.JournalEvents.Count);
    }

    [Fact]
    public async Task SlotAssign_BothActionAndMacro_Returns400()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");

        var response = await client.PostAsJsonAsync(
            ApiPaths.SlotAssign, new SlotEditEndpoint.AssignRequest(0, 0, "LandingGearToggle", "request-docking", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SlotLabel_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(ApiPaths.SlotLabel, new SlotEditEndpoint.LabelRequest(0, 1, "GEAR"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SlotLabel_ExistingSlot_Persists_AndAFollowUpGetReflectsIt()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640"); // seed the starter (t30)

        var labelResponse = await client.PostAsJsonAsync(ApiPaths.SlotLabel, new SlotEditEndpoint.LabelRequest(0, 1, "GEAR"));
        Assert.Equal(HttpStatusCode.OK, labelResponse.StatusCode);

        var panelResponse = await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var panel = await panelResponse.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);
        Assert.Equal("GEAR", panel!.Slots.Single(s => s.Index == 1).Label);
    }

    [Fact]
    public async Task SlotLabel_SlotDoesNotExist_Returns400()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");

        // t30's own active range is [0, 30) - index 30 is out of range, so
        // it can never be an already-assigned slot to rename.
        var response = await client.PostAsJsonAsync(ApiPaths.SlotLabel, new SlotEditEndpoint.LabelRequest(0, 30, "X"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SlotClear_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(ApiPaths.SlotClear, new SlotEditEndpoint.ClearRequest(0, 1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SlotClear_ExistingSlot_Persists_AndAFollowUpGetNoLongerListsIt()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640"); // seed the starter (t30)

        var clearResponse = await client.PostAsJsonAsync(ApiPaths.SlotClear, new SlotEditEndpoint.ClearRequest(0, 1));
        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);

        var panelResponse = await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var panel = await panelResponse.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);
        Assert.DoesNotContain(panel!.Slots, s => s.Index == 1);

        // Clearing a slot must never remove the page itself.
        // [2026-09-09, superseded: was Assert.Equal(1, ...). The starter
        // layout gained five context pages (ref/docs/vessel-context.md's
        // "The default context pages"), so the shipped page count is 6, not
        // 1. The claim - clearing a slot removes no page - is unchanged.]
        Assert.Equal(6, panel.PageCount);
    }

    [Fact]
    public async Task SlotClear_DoesNotExist_IsStillOk_Idempotent()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        await client.PostAsJsonAsync(ApiPaths.SlotClear, new SlotEditEndpoint.ClearRequest(0, 1));

        var response = await client.PostAsJsonAsync(ApiPaths.SlotClear, new SlotEditEndpoint.ClearRequest(0, 1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // -------------------------------------------------------------------
    // POST /api/panel/slot/longpress (ref/docs/editor.md's long-press
    // wiring) - same set-or-clear convention as /slot/label.
    // -------------------------------------------------------------------

    [Fact]
    public async Task SlotLongPress_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(
            ApiPaths.SlotLongPress, new SlotEditEndpoint.LongPressRequest(0, 1, "LandingGearToggle", null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SlotLongPress_ExistingSlot_Persists_AndAFollowUpGetReflectsIt()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640"); // seed the starter (t30)

        var lpResponse = await client.PostAsJsonAsync(
            ApiPaths.SlotLongPress, new SlotEditEndpoint.LongPressRequest(0, 1, "SetSpeed75", null));
        Assert.Equal(HttpStatusCode.OK, lpResponse.StatusCode);

        var panelResponse = await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var panel = await panelResponse.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);
        var slot1 = panel!.Slots.Single(s => s.Index == 1);
        Assert.NotNull(slot1.LongPress);
        Assert.Equal("Ok", slot1.LongPress!.Status);
        Assert.Equal("W", slot1.LongPress.Chord);
        // The primary action (LandingGearToggle) is unaffected.
        Assert.Equal("Ok", slot1.Status);
    }

    [Fact]
    public async Task SlotLongPress_NullBoth_ClearsAnExistingOne()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        await client.PostAsJsonAsync(ApiPaths.SlotLongPress, new SlotEditEndpoint.LongPressRequest(0, 1, "SetSpeed75", null));

        var clearResponse = await client.PostAsJsonAsync(ApiPaths.SlotLongPress, new SlotEditEndpoint.LongPressRequest(0, 1, null, null));
        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);

        var panelResponse = await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var panel = await panelResponse.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);
        Assert.Null(panel!.Slots.Single(s => s.Index == 1).LongPress);
    }

    [Fact]
    public async Task SlotLongPress_SlotDoesNotExist_Returns400()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");

        // t30's own active range is [0, 30) - index 30 is out of range, so
        // it can never be an already-assigned slot to attach a long-press to.
        var response = await client.PostAsJsonAsync(
            ApiPaths.SlotLongPress, new SlotEditEndpoint.LongPressRequest(0, 30, "LandingGearToggle", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SlotLongPress_BothActionAndMacro_Returns400()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");

        var response = await client.PostAsJsonAsync(
            ApiPaths.SlotLongPress, new SlotEditEndpoint.LongPressRequest(0, 1, "LandingGearToggle", "request-docking"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -------------------------------------------------------------------
    // POST /api/panel/slot/latch (ref/docs/latching-keys.md).
    //
    // DELIBERATELY NOT DRIVEN HERE: an actual latching press. Every other
    // press test in this file stops at the injection guard, which refuses
    // because Elite is not the foreground window - but that is a property of
    // the machine running the tests, not a guarantee, and the one existing
    // test that does reach the guard through a real host
    // (Macros_AMacroSavedThroughTheApi_FiresFromASlot_WithNoRestart) accepts
    // "Sent" as a legal answer for exactly that reason. A gear toggle that
    // slips through is a keystroke; a LATCH that slips through is a key left
    // held down in a commander's live game. The press route's latch branch is
    // therefore covered at PressEndpoint.Evaluate (LatchLayoutTests) and at
    // LatchRegistry (LatchRegistryTests) instead, and its ~30 lines of
    // handler glue are read, not driven. Stated here rather than left as a
    // silence.
    // -------------------------------------------------------------------

    [Fact]
    public async Task SlotLatch_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(ApiPaths.SlotLatch, new SlotEditEndpoint.LatchRequest(0, 1, true));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SlotLatch_ExistingActionSlot_Persists_AndTheNextPanelReadReportsIt()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        // Needed for the save to actually reach disk: with no bindings file
        // at all, every starter action is an unknown name and
        // LayoutValidator.ValidateForSave refuses, leaving the edit in memory
        // for one response only - which is exactly what the first version of
        // this test measured, and why it read false on the follow-up GET.
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");

        var response = await client.PostAsJsonAsync(ApiPaths.SlotLatch, new SlotEditEndpoint.LatchRequest(0, 1, true));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var panel = await client.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        Assert.True(panel!.Slots.Single(s => s.Index == 1).Latch);

        var off = await client.PostAsJsonAsync(ApiPaths.SlotLatch, new SlotEditEndpoint.LatchRequest(0, 1, false));
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);

        var after = await client.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        Assert.False(after!.Slots.Single(s => s.Index == 1).Latch);
    }

    /// <summary>
    /// Slot 0 of the seeded starter layout is the "request-docking" macro.
    /// The commander is told why a macro cannot be held, in words, rather
    /// than being handed a validation error about a layout they did not know
    /// they had broken.
    /// </summary>
    [Fact]
    public async Task SlotLatch_MacroSlot_Returns400_ExplainingThereIsNoOneKeyToHold()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");

        var response = await client.PostAsJsonAsync(ApiPaths.SlotLatch, new SlotEditEndpoint.LatchRequest(0, 0, true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("sequence of keystrokes", body.RootElement.GetProperty("error").GetString());
    }

    /// <summary>
    /// The empty index is discovered from the seeded layout rather than
    /// hardcoded: the starter fills 29 of T30's 30 cells (26 as of
    /// 2026-09-07, since grown by the pip-management task), and which one is
    /// spare is the starter layout's business, not this test's. The first
    /// version of this test assumed index 29 was one of them and it is not -
    /// it passed nothing and proved nothing about latching.
    /// </summary>
    [Fact]
    public async Task SlotLatch_EmptySlot_Returns400()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var panel = await client.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        var occupied = panel!.Slots.Select(s => s.Index).ToHashSet();
        var emptyIndex = Enumerable.Range(0, panel.Cols * panel.Rows).First(i => !occupied.Contains(i));

        var response = await client.PostAsJsonAsync(ApiPaths.SlotLatch, new SlotEditEndpoint.LatchRequest(0, emptyIndex, true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SlotLatch_MalformedBody_Returns400_NotAnUnhandledException()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.PostAsync(
            ApiPaths.SlotLatch,
            new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -------------------------------------------------------------------
    // POST /api/panel/slot/hold (ref/docs/latching-keys.md's hold-to-thrust
    // extension) - exact mirror of SlotLatch above, one route per gesture.
    //
    // DELIBERATELY NOT DRIVEN HERE, for the identical reason stated at the
    // SlotLatch section above: an actual hold press. The press route's hold
    // branch is covered at PressEndpoint.Evaluate (HoldLayoutTests) and at
    // LatchRegistry (LatchRegistryHoldTests) instead.
    // -------------------------------------------------------------------

    [Fact]
    public async Task SlotHold_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(ApiPaths.SlotHold, new SlotEditEndpoint.HoldRequest(0, 1, true));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SlotHold_ExistingActionSlot_Persists_AndTheNextPanelReadReportsIt()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");

        var response = await client.PostAsJsonAsync(ApiPaths.SlotHold, new SlotEditEndpoint.HoldRequest(0, 1, true));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var panel = await client.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        Assert.True(panel!.Slots.Single(s => s.Index == 1).Hold);

        var off = await client.PostAsJsonAsync(ApiPaths.SlotHold, new SlotEditEndpoint.HoldRequest(0, 1, false));
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);

        var after = await client.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        Assert.False(after!.Slots.Single(s => s.Index == 1).Hold);
    }

    [Fact]
    public async Task SlotHold_MacroSlot_Returns400_ExplainingThereIsNoOneKeyToHold()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");

        var response = await client.PostAsJsonAsync(ApiPaths.SlotHold, new SlotEditEndpoint.HoldRequest(0, 0, true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("sequence of keystrokes", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task SlotHold_EmptySlot_Returns400()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var panel = await client.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        var occupied = panel!.Slots.Select(s => s.Index).ToHashSet();
        var emptyIndex = Enumerable.Range(0, panel.Cols * panel.Rows).First(i => !occupied.Contains(i));

        var response = await client.PostAsJsonAsync(ApiPaths.SlotHold, new SlotEditEndpoint.HoldRequest(0, emptyIndex, true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SlotHold_MalformedBody_Returns400_NotAnUnhandledException()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.PostAsync(
            ApiPaths.SlotHold,
            new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Turning holding ON must imply latching turns OFF - end to end through
    /// the real routes, not only at the <c>SlotEditEndpoint</c> unit level
    /// (<c>HoldLayoutTests.SetHold_On_ClearsAnExistingLatch</c>).
    /// </summary>
    [Fact]
    public async Task SlotHold_On_ClearsAnExistingLatch_EndToEnd()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        await client.PostAsJsonAsync(ApiPaths.SlotLatch, new SlotEditEndpoint.LatchRequest(0, 1, true));

        var response = await client.PostAsJsonAsync(ApiPaths.SlotHold, new SlotEditEndpoint.HoldRequest(0, 1, true));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var panel = await client.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        var slot = panel!.Slots.Single(s => s.Index == 1);
        Assert.True(slot.Hold);
        Assert.False(slot.Latch);
    }

    /// <summary>
    /// Release rule 4 (<c>ref/docs/latching-keys.md</c>) has to be REGISTERED,
    /// not merely implemented - a <c>LatchRegistry.ReleaseAll</c> nothing
    /// calls on the way out is a rule that does not fire. Resolving the
    /// registry from the running host's own container is what proves the
    /// press route, the live channel, the sweeper and the shutdown hook are
    /// all talking to one instance rather than each holding their own.
    /// </summary>
    [Fact]
    public async Task LatchRegistry_IsASingleSharedInstanceInTheHostsContainer()
    {
        await using var host = await RunningHost.StartAsync();

        var first = host.App.Services.GetRequiredService<LunaPanel.Core.Latching.LatchRegistry>();
        var second = host.App.Services.GetRequiredService<LunaPanel.Core.Latching.LatchRegistry>();

        Assert.Same(first, second);
        Assert.False(first.Any);
    }

    // -------------------------------------------------------------------
    // POST /api/panel/slot/move (ref/docs/editor.md's drag-to-move) -
    // driven end to end: the route, its refusals, and that a move actually
    // reaches disk and is still there on the next read.
    //
    // NOT drivable here, and stated rather than left as a silence: the
    // *gesture*. Whether a drag starts, whether a tap still opens the slot
    // sheet, and whether the move is even offered outside edit mode are all
    // browser behaviour, and there is no headless browser in this suite -
    // they are source-scan pins in PanelClientSourceGuardTests instead.
    // -------------------------------------------------------------------

    [Fact]
    public async Task SlotMove_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(ApiPaths.SlotMove, new SlotEditEndpoint.MoveRequest(0, 1, 2));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The whole feature in one test: drag a button to an empty cell and it
    /// is still there on a fresh read. The bindings setup matters for the
    /// same reason <c>SlotLatch_ExistingActionSlot_Persists</c> records -
    /// without it every starter action is an unknown name,
    /// <c>LayoutValidator.ValidateForSave</c> refuses, and the edit would
    /// live for exactly one response while this test read true.
    /// </summary>
    [Fact]
    public async Task SlotMove_ToAnEmptySlot_Persists_AndSurvivesAReload()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);

        var before = await client.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        var occupied = before!.Slots.Select(s => s.Index).ToHashSet();
        var emptyIndex = Enumerable.Range(0, before.Cols * before.Rows).First(i => !occupied.Contains(i));
        var source = before.Slots.Single(s => s.Index == 1);

        var response = await client.PostAsJsonAsync(ApiPaths.SlotMove, new SlotEditEndpoint.MoveRequest(0, 1, emptyIndex));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await client.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        Assert.Equal(source.Label, after!.Slots.Single(s => s.Index == emptyIndex).Label);
        Assert.DoesNotContain(after.Slots, s => s.Index == 1);
        Assert.Equal(before.Slots.Count, after.Slots.Count);
    }

    /// <summary>
    /// The occupied-target decision, driven: a swap, never a push-along.
    /// Both labels are asserted in both directions, because a router that
    /// only ever copied one way would satisfy a single-direction assertion.
    /// </summary>
    [Fact]
    public async Task SlotMove_ToAnOccupiedSlot_SwapsTheTwo_AndBothSurviveAReload()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);

        var before = await client.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        var one = before!.Slots.Single(s => s.Index == 1);
        var two = before.Slots.Single(s => s.Index == 2);
        Assert.NotEqual(one.Label, two.Label);

        var response = await client.PostAsJsonAsync(ApiPaths.SlotMove, new SlotEditEndpoint.MoveRequest(0, 1, 2));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await client.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        Assert.Equal(one.Label, after!.Slots.Single(s => s.Index == 2).Label);
        Assert.Equal(two.Label, after.Slots.Single(s => s.Index == 1).Label);
        Assert.Equal(before.Slots.Count, after.Slots.Count);
    }

    [Fact]
    public async Task SlotMove_FromAnEmptySlot_Returns400()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var panel = await client.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        var occupied = panel!.Slots.Select(s => s.Index).ToHashSet();
        var emptyIndex = Enumerable.Range(0, panel.Cols * panel.Rows).First(i => !occupied.Contains(i));

        var response = await client.PostAsJsonAsync(ApiPaths.SlotMove, new SlotEditEndpoint.MoveRequest(0, emptyIndex, 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SlotMove_TargetOutsideTheTemplate_Returns400_AndChangesNothing()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);

        var before = await client.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        var past = before!.Cols * before.Rows;

        var response = await client.PostAsJsonAsync(ApiPaths.SlotMove, new SlotEditEndpoint.MoveRequest(0, 1, past));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await client.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        Assert.Equal(before.Slots.Single(s => s.Index == 1).Label, after!.Slots.Single(s => s.Index == 1).Label);
    }

    [Fact]
    public async Task SlotMove_NoSuchPage_Returns400()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");

        var response = await client.PostAsJsonAsync(ApiPaths.SlotMove, new SlotEditEndpoint.MoveRequest(99, 1, 2));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SlotMove_MalformedBody_Returns400_NotAnUnhandledException()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.PostAsync(
            ApiPaths.SlotMove,
            new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A moved button still FIRES the thing it named, from its new index -
    /// the move rewrote which slot index holds which action and nothing
    /// else. This stops at the injection guard like every other press test
    /// in this file (Elite is not the foreground window here), so what is
    /// actually proved is that the press route resolves index 1's NEW
    /// occupant rather than reporting the slot unassigned - which is what a
    /// move that had silently dropped the action would produce.
    /// </summary>
    [Fact]
    public async Task SlotMove_ThePressRouteResolvesTheButtonAtItsNewIndex()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);

        var before = await client.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        var occupied = before!.Slots.Select(s => s.Index).ToHashSet();
        var emptyIndex = Enumerable.Range(0, before.Cols * before.Rows).First(i => !occupied.Contains(i));

        await client.PostAsJsonAsync(ApiPaths.SlotMove, new SlotEditEndpoint.MoveRequest(0, 1, emptyIndex));

        var press = await client.PostAsJsonAsync(ApiPaths.Press, new { page = 0, slot = emptyIndex });
        Assert.Equal(HttpStatusCode.OK, press.StatusCode);
        using var body = JsonDocument.Parse(await press.Content.ReadAsStringAsync());
        // "No such slot on this page." is exactly what a move that dropped
        // the action would produce here, and it is the only refusal this
        // assertion has to exclude - anything further down the chain
        // (unbound, foreground-window guard) means the slot WAS found.
        Assert.DoesNotContain("No such slot", body.RootElement.GetProperty("reason").GetString());

        // And the index it came from now refuses, in exactly those words,
        // because a move vacates its source rather than copying out of it.
        var vacated = await client.PostAsJsonAsync(ApiPaths.Press, new { page = 0, slot = 1 });
        using var vacatedBody = JsonDocument.Parse(await vacated.Content.ReadAsStringAsync());
        Assert.False(vacatedBody.RootElement.GetProperty("fired").GetBoolean());
        Assert.Contains("No such slot", vacatedBody.RootElement.GetProperty("reason").GetString());
    }

    // -------------------------------------------------------------------
    // GET/POST /api/theme, POST /api/theme/reset - the settings gear's
    // colour pane (ref/docs/theme.md's colour chain). This synthetic
    // environment has no EDHM and no graphics config files at all, so
    // automatic resolution always lands on Stock (Elite orange) - exactly
    // the "no EDHM at all" guarantee the whole design rests on.
    // -------------------------------------------------------------------

    [Fact]
    public async Task Theme_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.GetAsync(ApiPaths.Theme);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Theme_NoOverrideStored_ReportsStock_AndOverrideInactive()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.GetAsync(ApiPaths.Theme);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonSerializer.Deserialize<ThemeEndpoint.StatusResponse>(raw, JsonOptions);
        Assert.Equal("Stock", body!.SourceKind);
        Assert.False(body.OverrideActive);
        Assert.Equal("#ff8000", body.Text, StringComparer.OrdinalIgnoreCase);

        // This host's discovery environment has no EDHM at all (see this
        // file's own environment setup above) - "absent or empty" is the
        // settings gear's "From your HUD" contract (ref/docs/web-client.md);
        // the pane must still work with just the standard palette.
        Assert.Empty(body.HudColours);

        // No field here is ever a path (source/sourceKind are enum-derived
        // strings, border/text/lit are hex, overrideActive is a bool) - true
        // by construction, same discipline as HealthEndpoint - but the
        // camelCase casing itself is worth pinning directly, same as
        // GET /api/panel's own casing check.
        Assert.Contains("\"sourceKind\"", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\"SourceKind\"", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Theme_PostOverride_PersistsAndReportsOverrideActive_AndPanelHonoursIt()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var postResponse = await client.PostAsJsonAsync(
            ApiPaths.Theme, new { border = "#00ff00", text = "#0000ff", lit = "#ff00ff" }, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var postBody = await postResponse.Content.ReadFromJsonAsync<ThemeEndpoint.StatusResponse>(JsonOptions);
        Assert.Equal("Override", postBody!.SourceKind);
        Assert.True(postBody.OverrideActive);
        Assert.Equal("#0000ff", postBody.Text, StringComparer.OrdinalIgnoreCase);

        // A fresh GET reflects the persisted override, not just the POST's
        // own echo of it.
        var getResponse = await client.GetAsync(ApiPaths.Theme);
        var getBody = await getResponse.Content.ReadFromJsonAsync<ThemeEndpoint.StatusResponse>(JsonOptions);
        Assert.True(getBody!.OverrideActive);
        Assert.Equal("#0000ff", getBody.Text, StringComparer.OrdinalIgnoreCase);

        // GET /api/panel's themeCss honours the same stored override.
        var panelResponse = await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var panelBody = await panelResponse.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);
        Assert.Contains("--lp-text: #0000ff;", panelBody!.ThemeCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--lp-frame: #00ff00;", panelBody.ThemeCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--lp-lit: #ff00ff;", panelBody.ThemeCss, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Theme_PostInvalidColour_Returns400_AndDoesNotPersist()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.PostAsJsonAsync(
            ApiPaths.Theme, new { border = "not-a-colour", text = "#0000ff", lit = "#ff00ff" }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var getResponse = await client.GetAsync(ApiPaths.Theme);
        var getBody = await getResponse.Content.ReadFromJsonAsync<ThemeEndpoint.StatusResponse>(JsonOptions);
        Assert.False(getBody!.OverrideActive);
    }

    [Fact]
    public async Task Theme_CorruptOverrideFile_DegradesToAutomatic_NeverBlankOrErrored()
    {
        // The one guarantee this whole feature rests on: a broken override
        // must never take the panel down with it. Unlike a corrupt layout
        // (which has no automatic fallback and surfaces as a 500), a
        // corrupt theme override degrades cleanly to automatic resolution.
        await using var host = await RunningHost.StartAsync();
        var code = host.App.Services.GetRequiredService<DeviceRegistry>().CurrentCode;
        var pairResponse = await host.Client.PostAsJsonAsync(
            ApiPaths.Pair, new { code, deviceName = "Test device", deviceClass = "tablet" }, JsonOptions);
        var deviceId = (await pairResponse.Content.ReadFromJsonAsync<PairEndpoint.SuccessResponse>(JsonOptions))!.DeviceId;

        var layout = LunaPanelDirectories.Resolve(host.LocalAppData);
        File.WriteAllText(Path.Combine(layout.LayoutsDirectory, $"theme-override-{deviceId}.json"), "{ not valid json");

        var themeResponse = await host.Client.GetAsync(ApiPaths.Theme);
        var panelResponse = await host.Client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");

        Assert.Equal(HttpStatusCode.OK, themeResponse.StatusCode);
        var themeBody = await themeResponse.Content.ReadFromJsonAsync<ThemeEndpoint.StatusResponse>(JsonOptions);
        Assert.False(themeBody!.OverrideActive);
        Assert.Equal("Stock", themeBody.SourceKind);

        Assert.Equal(HttpStatusCode.OK, panelResponse.StatusCode);
        var panelBody = await panelResponse.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);
        Assert.Contains("--lp-text: #ff8000;", panelBody!.ThemeCss, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Theme_Reset_ReturnsToAutomatic_NotToWhateverTheOverrideWasShowing()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await client.PostAsJsonAsync(ApiPaths.Theme, new { border = "#00ff00", text = "#0000ff", lit = "#ff00ff" }, JsonOptions);

        var resetResponse = await client.PostAsync(ApiPaths.ThemeReset, content: null);

        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
        var resetBody = await resetResponse.Content.ReadFromJsonAsync<ThemeEndpoint.StatusResponse>(JsonOptions);
        Assert.False(resetBody!.OverrideActive);
        Assert.Equal("Stock", resetBody.SourceKind);
        Assert.Equal("#ff8000", resetBody.Text, StringComparer.OrdinalIgnoreCase);

        var getResponse = await client.GetAsync(ApiPaths.Theme);
        var getBody = await getResponse.Content.ReadFromJsonAsync<ThemeEndpoint.StatusResponse>(JsonOptions);
        Assert.False(getBody!.OverrideActive);
    }

    // -------------------------------------------------------------------
    // Background as a fourth commander-chosen role (2026-09-10,
    // ref/docs/theme.md). Same route, same store, same reset - the only
    // new thing on the wire is one optional field.
    // -------------------------------------------------------------------

    [Fact]
    public async Task Theme_PostOverrideWithBackground_PersistsIt_AndPanelGroundHonoursItExactly()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var postResponse = await client.PostAsJsonAsync(
            ApiPaths.Theme,
            new { border = "#00ff00", text = "#0000ff", lit = "#ff00ff", background = "#3378ff" },
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var postBody = await postResponse.Content.ReadFromJsonAsync<ThemeEndpoint.StatusResponse>(JsonOptions);
        Assert.Equal("#3378ff", postBody!.Background, StringComparer.OrdinalIgnoreCase);
        Assert.True(postBody.BackgroundChosen);

        // A fresh GET reads it back off disk, not out of the POST's echo.
        var getBody = await client.GetFromJsonAsync<ThemeEndpoint.StatusResponse>(ApiPaths.Theme, JsonOptions);
        Assert.Equal("#3378ff", getBody!.Background, StringComparer.OrdinalIgnoreCase);
        Assert.True(getBody.BackgroundChosen);

        // And it reaches the page as --lp-ground, unaltered: a chosen
        // background is never tinted toward black the way a derived one is.
        var panelResponse = await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var panelBody = await panelResponse.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);
        Assert.Contains("--lp-ground: #3378ff;", panelBody!.ThemeCss, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Theme_PostOverrideWithBackground_LeavesTheOtherThreeRolesExactlyAsPosted()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        await client.PostAsJsonAsync(
            ApiPaths.Theme,
            new { border = "#00ff00", text = "#0000ff", lit = "#ff00ff", background = "#3378ff" },
            JsonOptions);

        var panelResponse = await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var panelBody = await panelResponse.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);

        // The same three values the pre-background test asserts, unchanged
        // by a background being chosen alongside them.
        Assert.Contains("--lp-frame: #00ff00;", panelBody!.ThemeCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--lp-text: #0000ff;", panelBody.ThemeCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--lp-lit: #ff00ff;", panelBody.ThemeCss, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Theme_PostOverrideWithoutBackground_LeavesTheGroundDerivedFromText()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var postResponse = await client.PostAsJsonAsync(
            ApiPaths.Theme, new { border = "#00ff00", text = "#0000ff", lit = "#ff00ff" }, JsonOptions);

        var postBody = await postResponse.Content.ReadFromJsonAsync<ThemeEndpoint.StatusResponse>(JsonOptions);
        Assert.False(postBody!.BackgroundChosen);

        // #0000ff kept at 8% is #000014 - the near-black tint --lp-ground
        // has always been, still reported so the pane's fourth swatch has
        // something to show.
        Assert.Equal("#000014", postBody.Background, StringComparer.OrdinalIgnoreCase);

        var panelResponse = await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var panelBody = await panelResponse.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);
        Assert.Contains("--lp-ground: #000014;", panelBody!.ThemeCss, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Theme_ResetAfterChoosingABackground_ReturnsTheAutomaticGround_NotTheChosenOne()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await client.PostAsJsonAsync(
            ApiPaths.Theme,
            new { border = "#00ff00", text = "#0000ff", lit = "#ff00ff", background = "#3378ff" },
            JsonOptions);

        var resetResponse = await client.PostAsync(ApiPaths.ThemeReset, content: null);

        var resetBody = await resetResponse.Content.ReadFromJsonAsync<ThemeEndpoint.StatusResponse>(JsonOptions);
        Assert.False(resetBody!.BackgroundChosen);

        // This host has no EDHM at all, so automatic lands on stock orange
        // (#ff8000) and its 8% tint, #140a00 - never #3378ff again.
        Assert.Equal("#140a00", resetBody.Background, StringComparer.OrdinalIgnoreCase);

        var panelResponse = await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var panelBody = await panelResponse.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);
        Assert.Contains("--lp-ground: #140a00;", panelBody!.ThemeCss, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#3378ff", panelBody.ThemeCss, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Theme_PostInvalidBackground_Returns400_AndPersistsNothingAtAll()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.PostAsJsonAsync(
            ApiPaths.Theme,
            new { border = "#00ff00", text = "#0000ff", lit = "#ff00ff", background = "not-a-colour" },
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Not "three of the four applied": a refused body changes nothing.
        var getBody = await client.GetFromJsonAsync<ThemeEndpoint.StatusResponse>(ApiPaths.Theme, JsonOptions);
        Assert.False(getBody!.OverrideActive);
    }

    [Fact]
    public async Task Theme_LitPickedEqualToTheBackground_IsAppliedAnyway_AndOnlyWarnedAbout()
    {
        // The standing rule this pins: the commander's judgement on their
        // own panel is final. A colour that will be invisible is still
        // saved, still served, and still reported back as in effect - the
        // only consequence is that lowContrast names the role.
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.PostAsJsonAsync(
            ApiPaths.Theme,
            new { border = "#00ff00", text = "#ffffff", lit = "#3378ff", background = "#3378ff" },
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ThemeEndpoint.StatusResponse>(JsonOptions);
        Assert.Equal("#3378ff", body!.Lit, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(new[] { "Lit" }, body.LowContrast);

        var panelResponse = await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var panelBody = await panelResponse.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);
        Assert.Contains("--lp-lit: #3378ff;", panelBody!.ThemeCss, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Theme_NoOverrideStored_ReportsTheDerivedStockGround_AndNoContrastWarning()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var raw = await client.GetStringAsync(ApiPaths.Theme);
        var body = JsonSerializer.Deserialize<ThemeEndpoint.StatusResponse>(raw, JsonOptions);

        Assert.False(body!.BackgroundChosen);
        Assert.Equal("#140a00", body.Background, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(body.LowContrast);

        // Same camelCase pin the sibling status test makes for sourceKind -
        // the two new fields are on the wire under the names the pane reads.
        Assert.Contains("\"backgroundChosen\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"lowContrast\"", raw, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------
    // GET /api/bindings/status, POST /api/bindings/refresh
    // (ref/docs/bindings-source.md). This synthetic environment's
    // BindingsDirectory does not exist at startup, so the very first status
    // read is the "commander who never rebinds" case this task's whole
    // brief is about - and the refresh test proves it stops being dead
    // without a restart, once a real file appears.
    // -------------------------------------------------------------------

    [Fact]
    public async Task BindingsStatus_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.GetAsync(ApiPaths.BindingsStatus);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BindingsRefresh_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PostAsync(ApiPaths.BindingsRefresh, content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BindingsStatus_NoBindingsDirectoryAtAll_ReportsNotFound_NotAnError()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.GetAsync(ApiPaths.BindingsStatus);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BindingsStatusEndpoint.StatusResponse>(JsonOptions);
        Assert.Equal("NotFound", body!.SourceKind);
        Assert.Null(body.PresetName);
        Assert.Equal(0, body.BoundActionCount);
    }

    /// <summary>
    /// The actual defect this whole task fixes, end to end through a real
    /// Kestrel host: a commander who starts LunaPanel with no bindings file
    /// at all, then creates one (or, as here, one appears because they
    /// finally rebind something) while LunaPanel keeps running. Path
    /// discovery only runs once at startup, so without the refresh route
    /// this would stay dead until a restart - that's exactly the "frozen
    /// path" limitation <c>ref/docs/bindings-source.md</c>'s "Refresh"
    /// section names.
    /// </summary>
    [Fact]
    public async Task BindingsRefresh_PicksUpAPresetThatDidNotExistAtStartup_NoRestartNeeded()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var beforeResponse = await client.GetAsync(ApiPaths.BindingsStatus);
        var before = await beforeResponse.Content.ReadFromJsonAsync<BindingsStatusEndpoint.StatusResponse>(JsonOptions);
        Assert.Equal("NotFound", before!.SourceKind);

        Directory.CreateDirectory(host.BindingsDirectory);
        File.WriteAllText(
            Path.Combine(host.BindingsDirectory, "Custom.4.2.binds"),
            """<Root PresetName="Custom" MajorVersion="4" MinorVersion="2"><LandingGearToggle><Primary Device="Keyboard" Key="Key_G" /><Secondary Device="{NoDevice}" Key="" /></LandingGearToggle></Root>""");
        File.WriteAllText(Path.Combine(host.BindingsDirectory, "StartPreset.4.start"), "Custom\n");

        var refreshResponse = await client.PostAsync(ApiPaths.BindingsRefresh, content: null);

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<BindingsStatusEndpoint.StatusResponse>(JsonOptions);
        Assert.Equal("CommanderAuthored", refreshed!.SourceKind);
        Assert.Equal("Custom", refreshed.PresetName);
        Assert.Equal("4.2", refreshed.Version);
        Assert.Equal(1, refreshed.BoundActionCount);

        // A follow-up GET (not just the refresh's own response) sees it too.
        var afterResponse = await client.GetAsync(ApiPaths.BindingsStatus);
        var after = await afterResponse.Content.ReadFromJsonAsync<BindingsStatusEndpoint.StatusResponse>(JsonOptions);
        Assert.Equal("CommanderAuthored", after!.SourceKind);

        // The panel itself reflects it too - LandingGearToggle (slot 1 of
        // the starter layout) is now genuinely bound, not merely reported as
        // bound by the status endpoint alone.
        var panelResponse = await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var panelBody = await panelResponse.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);
        var slot1 = panelBody!.Slots.Single(s => s.Index == 1);
        Assert.Equal("Ok", slot1.Status);
        Assert.Equal("G", slot1.Chord);
    }

    /// <summary>
    /// Defect 1 from <c>ref/docs/bindings-source.md</c>, driven end to end
    /// through a real running host rather than only unit-tested against
    /// <see cref="PresetSelector"/> in isolation: a commander who has never
    /// rebound anything at all (no file anywhere in
    /// <c>Options\Bindings</c>) still gets a real, live panel from the
    /// game's shipped stock scheme - not a wall of dead buttons. This is
    /// also the one test in this file that exercises
    /// <see cref="LiveBindingsReader"/> against a path that only
    /// <c>BindingsSelection</c> (never <c>Bindings.LatestBindsFilePath</c>,
    /// which stays <see langword="null"/> here) can resolve, so it is the
    /// specific witness that the reader was actually rewired to the new
    /// field rather than merely compiling against it.
    /// </summary>
    [Fact]
    public async Task Panel_NoCommanderBindingsFile_OnlyStockControlScheme_ResolvesViaStock_PanelIsNotDead()
    {
        await using var host = await RunningHost.StartAsync(beforeStart: temp =>
        {
            var steamLibrary = temp.CreateSubdirectory("SteamLibrary");
            temp.CreateFile("SteamLibrary/steamapps/common/Elite Dangerous/Products/elite-dangerous-odyssey-64/EliteDangerous64.exe");
            temp.CreateFile(
                "SteamLibrary/steamapps/common/Elite Dangerous/Products/elite-dangerous-odyssey-64/ControlSchemes/KeyboardMouseOnly.binds",
                """<Root PresetName="KeyboardMouseOnly" SortOrder="0"><LandingGearToggle><Primary Device="Keyboard" Key="Key_G" /><Secondary Device="{NoDevice}" Key="" /></LandingGearToggle></Root>""");
            temp.CreateFile(
                "Steam/steamapps/libraryfolders.vdf",
                $$"""
                "libraryfolders"
                {
                	"0"
                	{
                		"path"		"{{steamLibrary.Replace(@"\", @"\\")}}"
                	}
                }
                """);
            Directory.CreateDirectory(temp.Combine("NoBindings"));
            File.WriteAllText(Path.Combine(temp.Combine("NoBindings"), "StartPreset.4.start"), "KeyboardMouseOnly\n");
        });
        var client = await PairedClientAsync(host);

        var statusResponse = await client.GetAsync(ApiPaths.BindingsStatus);
        var status = await statusResponse.Content.ReadFromJsonAsync<BindingsStatusEndpoint.StatusResponse>(JsonOptions);
        Assert.Equal("Stock", status!.SourceKind);
        Assert.Equal("KeyboardMouseOnly", status.PresetName);
        Assert.Null(status.Version);
        Assert.Equal(1, status.BoundActionCount);

        var panelResponse = await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var panelBody = await panelResponse.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);
        var slot1 = panelBody!.Slots.Single(s => s.Index == 1);
        Assert.Equal("Ok", slot1.Status);
        Assert.Equal("G", slot1.Chord);
    }

    // -------------------------------------------------------------------
    // GET /api/panel/live - the server-sent-events live update channel.
    // -------------------------------------------------------------------

    /// <summary>Reads one "data: ..." line from an SSE stream, bounded by <paramref name="timeout"/> rather than a fixed sleep.</summary>
    private static async Task<JsonDocument> ReadNextSseEventAsync(StreamReader reader, TimeSpan timeout)
    {
        var readTask = ReadOneEventLineAsync(reader);
        var winner = await Task.WhenAny(readTask, Task.Delay(timeout));
        Assert.True(winner == readTask, $"No SSE event arrived within {timeout}.");
        return await readTask;
    }

    /// <summary>
    /// Reads events until one carries a non-null <c>macroFinished</c>, skipping
    /// the lit/dark pushes a run also produces on the way (O28). Bounded by
    /// <paramref name="timeout"/> PER EVENT and by a small count overall, so
    /// a channel that never delivers the outcome fails rather than reads
    /// forever.
    /// </summary>
    private static async Task<JsonDocument> ReadUntilMacroFinishedAsync(StreamReader reader, TimeSpan timeout)
    {
        const int MaxEventsToSkip = 8;
        for (var i = 0; i < MaxEventsToSkip; i++)
        {
            var candidate = await ReadNextSseEventAsync(reader, timeout);
            if (candidate.RootElement.TryGetProperty("macroFinished", out var finished) && finished.ValueKind != JsonValueKind.Null)
            {
                return candidate;
            }

            candidate.Dispose();
        }

        throw new Xunit.Sdk.XunitException($"No event carrying macroFinished arrived within {MaxEventsToSkip} events.");
    }

    private static async Task<JsonDocument> ReadOneEventLineAsync(StreamReader reader)
    {
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                return JsonDocument.Parse(line["data: ".Length..]);
            }
        }

        throw new IOException("SSE stream ended without another event.");
    }

    [Fact]
    public async Task PanelLive_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.GetAsync(ApiPaths.PanelLive);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PanelLive_OnConnect_SendsCurrentStateImmediately()
    {
        const string statusJson = """{ "timestamp": "2026-09-06T00:00:00Z", "event": "Status", "Flags": 4, "Pips": [4, 4, 4] }""";
        await using var host = await RunningHost.StartAsync(statusJsonContent: statusJson);
        var client = await PairedClientAsync(host);

        using var response = await client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var initial = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));

        Assert.True(initial.RootElement.GetProperty("gameRunning").GetBoolean());
        var slot1 = initial.RootElement.GetProperty("slots").EnumerateArray().Single(s => s.GetProperty("index").GetInt32() == 1);
        Assert.Equal("Full", slot1.GetProperty("lit").GetString());
    }

    [Fact]
    public async Task Host_ResolvesAJournalStateStore()
    {
        await using var host = await RunningHost.StartAsync();

        Assert.NotNull(host.App.Services.GetRequiredService<JournalStateStore>());
    }

    [Fact]
    public async Task Host_TailsTheJournalInElitesLiveStateDirectory_AndBackScansItAtStartup()
    {
        // The journal shares Elite's live-state directory with Status.json,
        // so JournalTailer is gated on the same discovery result. This drives
        // the real wiring: a real file on disk, a real tailer constructed by
        // ServerHostBuilder, and the vessel type already known by the time
        // the host has started - because LoadGame sits at the top of the file
        // and the back-scan reads it there.
        await using var host = await RunningHost.StartAsync(
            statusJsonContent: "{ \"timestamp\":\"2026-01-01T12:00:00Z\", \"event\":\"Status\", \"Flags\":0 }",
            beforeStart: temp =>
            {
                var statusDir = temp.CreateSubdirectory("StatusDir");
                File.WriteAllText(
                    Path.Combine(statusDir, "Journal.2026-01-01T120000.01.log"),
                    "{ \"timestamp\":\"2026-01-01T12:00:05Z\", \"event\":\"LoadGame\", \"Ship\":\"Lander01\", \"Ship_Localised\":\"Nomad\", \"ShipID\":17 }" + Environment.NewLine);
            });

        var journal = host.App.Services.GetRequiredService<JournalStateStore>();

        Assert.Equal("lander01", journal.CurrentVesselType);
        Assert.Equal("Nomad", journal.CurrentVesselTypeLocalised);
    }

    [Fact]
    public async Task Host_WithABackScannedLoadGame_TrackerIsAtNavigation_SameAsThePresumption()
    {
        // Fix 4's whole hazard: a LoadGame the back-scan finds is history,
        // not "the session just started" - it could be hours old, from a
        // commander who has been using the left panel the whole time
        // LunaPanel was not yet running. Seeding NAVIGATION from it would be
        // confidently wrong, and nothing could ever correct it (the
        // staleness detector only sees GuiFocus edges from the moment
        // LunaPanel starts watching).
        //
        // [2026-09-07, Fix 7 note: this assertion has lost its original
        // discriminating power. PanelTabTracker's own constructor now
        // presumes Navigation regardless of any journal event (see that
        // type's remarks, "Fix 7"), which is the SAME value RecordLoadGame
        // produces - so "the back-scan incorrectly called RecordLoadGame"
        // and "it correctly didn't, and the constructor's own presumption
        // produced the identical value anyway" are no longer distinguishable
        // through CurrentTab. The underlying isBackScan-gating mechanism
        // this test meant to guard remains soundly, independently pinned at
        // JournalStateStoreTests.Changed_DoesNotFire_ForAnEventRecordedAsBackScan
        // and JournalTailerTests.Changed_DoesNotFireForBackScannedEvents_ButDoesForALiveOne,
        // which check Changed firing directly and are unaffected by this
        // value collision. Kept as a value regression pin, not a mechanism
        // pin - renamed to say so honestly rather than leave a false name
        // standing.]
        await using var host = await RunningHost.StartAsync(
            statusJsonContent: "{ \"timestamp\":\"2026-01-01T12:00:00Z\", \"event\":\"Status\", \"Flags\":0 }",
            beforeStart: temp =>
            {
                var statusDir = temp.CreateSubdirectory("StatusDir");
                File.WriteAllText(
                    Path.Combine(statusDir, "Journal.2026-01-01T120000.01.log"),
                    "{ \"timestamp\":\"2026-01-01T12:00:05Z\", \"event\":\"LoadGame\", \"Ship\":\"Lander01\", \"Ship_Localised\":\"Nomad\", \"ShipID\":17 }" + Environment.NewLine);
            });

        var tracker = host.App.Services.GetRequiredService<PanelTabTracker>();

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
    }

    [Fact]
    public async Task Host_LiveLoadGame_SeedsThePanelTabTrackerToNavigation()
    {
        // The positive case Fix 4 exists to deliver: a LoadGame the game
        // writes AFTER LunaPanel has started - a real server or game
        // restart - is exactly the moment the commander confirmed the left
        // panel is reliably on NAVIGATION. Seeded via the real, running
        // JournalTailer (real FileSystemWatcher plus the 1s safety poll),
        // not a simulated hook - this is the one path that proves
        // ServerHostBuilder's own wiring, not just the units underneath it.
        string? journalPath = null;
        await using var host = await RunningHost.StartAsync(
            statusJsonContent: "{ \"timestamp\":\"2026-01-01T12:00:00Z\", \"event\":\"Status\", \"Flags\":0 }",
            beforeStart: temp =>
            {
                var statusDir = temp.CreateSubdirectory("StatusDir");
                journalPath = Path.Combine(statusDir, "Journal.2026-01-01T120000.01.log");
                // A back-scanned FSDJump ahead of the live LoadGame - proves
                // the seed the test asserts on came from the appended line,
                // not from something the back-scan already produced.
                File.WriteAllText(journalPath, "{ \"timestamp\":\"2026-01-01T12:00:05Z\", \"event\":\"FSDJump\" }" + Environment.NewLine);
            });

        var tracker = host.App.Services.GetRequiredService<PanelTabTracker>();
        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab); // the Fix 7 constructor presumption, not yet from any journal event

        // Fix 7 (2026-09-07) made the presumed default the SAME value the
        // live LoadGame below produces, so simply re-checking CurrentTab
        // after appending it would no longer prove the live wiring fired at
        // all - it would pass identically even if that wiring were deleted.
        // Move the tracker away from Navigation first (an own advance,
        // unrelated to the journal path this test actually exercises) so the
        // reset below is observable. (Reversed 2026-09-08: an uncredited
        // edge no longer nulls CurrentTab - it only lowers confidence - so
        // this test can no longer use that to force a distinguishable
        // starting value; RecordOwnTabAdvance does the same job.)
        tracker.RecordOwnTabAdvance();
        Assert.Equal(PanelTab.Transactions, tracker.CurrentTab);

        File.AppendAllText(
            journalPath!,
            "{ \"timestamp\":\"2026-01-01T12:00:10Z\", \"event\":\"LoadGame\", \"Ship\":\"Lander01\", \"Ship_Localised\":\"Nomad\", \"ShipID\":17 }" + Environment.NewLine);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (tracker.CurrentTab != PanelTab.Navigation && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
    }

    // -----------------------------------------------------------------
    // The Destination re-sync (LC20, 2026-09-08) wired through the REAL
    // host: PanelTabTracker.RecordDestinationChange is only useful if
    // something calls it, and ServerHostBuilder's gameStateStore.Changed
    // handler is the only caller. These four drive that handler directly
    // via GameStateStore.UpdateSnapshot (the same device the PanelLive
    // tests below use) rather than through files, so no StatusFileWatcher
    // exists to push a competing snapshot mid-test - RunningHost.StartAsync
    // with no statusJsonContent builds no watcher at all.
    // -----------------------------------------------------------------

    private static readonly StatusDestination DestinationA =
        new(46946810740905L, 0, "SELENE'S HAVEN BZK-L9K"); // LC18's real capture

    private static readonly StatusDestination DestinationB =
        new(46946810740905L, 3, "Eorld Flyao KA-A b20-21 A Belt Cluster 3"); // LC20's

    /// <summary>
    /// <c>GuiFocus</c> 2 is <c>ExternalPanel</c>, the LEFT panel; 1 is
    /// <c>InternalPanel</c>, the right (per <c>StatusVocabulary</c>).
    /// </summary>
    private static StatusSnapshot SnapshotWith(int? guiFocus, StatusDestination? destination) =>
        new(4u, null, guiFocus, GameRunning: true, SignedIn: true, destination);

    /// <summary>
    /// Walks the tracker's LEFT belief to <see cref="PanelTab.Contacts"/> -
    /// the stale position that actually cost the commander a docking request.
    /// Bounded, never a plain <c>while</c>: a mutation that stops
    /// <c>RecordOwnTabAdvance</c> moving would otherwise spin the test host
    /// forever instead of reddening, which is exactly what happened once
    /// already (see <c>ref/docs/panel-tab-tracking.md</c>, Fix 7's mutation
    /// notes, and the <c>WalkRightTo</c> helper it produced).
    /// </summary>
    private static void WalkLeftToContacts(PanelTabTracker tracker)
    {
        for (var i = 0; i < 4 && tracker.CurrentTab != PanelTab.Contacts; i++)
        {
            tracker.RecordOwnTabAdvance();
        }

        Assert.Equal(PanelTab.Contacts, tracker.CurrentTab);
    }

    private static PanelTabTracker TrackerMovedToContacts(RunningHost host)
    {
        var tracker = host.App.Services.GetRequiredService<PanelTabTracker>();
        WalkLeftToContacts(tracker);
        return tracker;
    }

    [Fact]
    public async Task Host_DestinationChangesWhileTheLeftPanelIsOpen_SnapsTheTrackerToNavigation()
    {
        // The deliverable: a commander sitting docked, holding a stale
        // Contacts belief, who picks something on NAVIGATION. Before this,
        // nothing short of an FSD jump or an on-foot round trip could
        // correct that - and neither is available on a landing pad.
        //
        // This also pins half of the absent/present decision: nothing has
        // been seen before this snapshot, so the transition here is
        // absent -> present, and it COUNTS as a change. LC20's own first
        // attributed transition has exactly that shape.
        await using var host = await RunningHost.StartAsync();
        var gameState = host.App.Services.GetRequiredService<GameStateStore>();
        var tracker = TrackerMovedToContacts(host);

        gameState.UpdateSnapshot(SnapshotWith(guiFocus: 2, DestinationA));

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);
    }

    [Fact]
    public async Task Host_DestinationUnchangedAcrossSnapshots_LeavesTheTrackerAlone()
    {
        // Status.json is rewritten on a ~11.8s idle heartbeat as well as on
        // change (LC6), and GameStateStore.Changed fires for every one of
        // those rewrites - so "a snapshot arrived" must never be mistaken
        // for "the destination changed", or the tracker would be dragged to
        // Navigation roughly five times a minute for as long as the panel
        // sits open.
        await using var host = await RunningHost.StartAsync();
        var gameState = host.App.Services.GetRequiredService<GameStateStore>();
        var tracker = TrackerMovedToContacts(host);

        // Open the panel and establish DestinationA first, then walk back to
        // Contacts so the assertion below can actually see a wrong snap.
        gameState.UpdateSnapshot(SnapshotWith(guiFocus: 2, DestinationA));
        WalkLeftToContacts(tracker);

        gameState.UpdateSnapshot(SnapshotWith(guiFocus: 2, DestinationA));
        gameState.UpdateSnapshot(SnapshotWith(guiFocus: 2, DestinationA));

        Assert.Equal(PanelTab.Contacts, tracker.CurrentTab);
    }

    [Fact]
    public async Task Host_DestinationDisappearsWhileTheLeftPanelIsOpen_LeavesTheTrackerAlone()
    {
        // The other half of the absent/present decision, and deliberately
        // NOT symmetrical with the positive case above: a Navigation
        // selection always produces a destination, so present -> absent is
        // not a selection at all. Nobody has measured what does produce it
        // (an arrival clearing the lock is the obvious candidate), and an
        // unmeasured cause is exactly the sort of thing the Low-confidence
        // ceiling exists to keep out of the belief entirely rather than
        // record badly.
        await using var host = await RunningHost.StartAsync();
        var gameState = host.App.Services.GetRequiredService<GameStateStore>();
        var tracker = TrackerMovedToContacts(host);

        gameState.UpdateSnapshot(SnapshotWith(guiFocus: 2, DestinationA));
        WalkLeftToContacts(tracker);

        gameState.UpdateSnapshot(SnapshotWith(guiFocus: 2, destination: null));

        Assert.Equal(PanelTab.Contacts, tracker.CurrentTab);
    }

    [Fact]
    public async Task Host_DestinationChangesWhileNoPanelIsOpen_LeavesTheTrackerAlone()
    {
        // GuiFocus 0 is NoFocus - the commander is flying, and a route can
        // advance or a destination can be set from surfaces this signal says
        // nothing about. LC20's finding is gated on the panel being open and
        // this is the gate.
        await using var host = await RunningHost.StartAsync();
        var gameState = host.App.Services.GetRequiredService<GameStateStore>();
        var tracker = TrackerMovedToContacts(host);

        gameState.UpdateSnapshot(SnapshotWith(guiFocus: 0, DestinationA));
        gameState.UpdateSnapshot(SnapshotWith(guiFocus: 0, DestinationB));

        Assert.Equal(PanelTab.Contacts, tracker.CurrentTab);
        Assert.Equal(TabConfidence.High, tracker.LeftTabConfidence);
    }

    [Fact]
    public async Task Host_DestinationChangesWhileTheRightPanelIsOpen_LeavesTheLeftTrackerAlone()
    {
        await using var host = await RunningHost.StartAsync();
        var gameState = host.App.Services.GetRequiredService<GameStateStore>();
        var tracker = TrackerMovedToContacts(host);

        gameState.UpdateSnapshot(SnapshotWith(guiFocus: 1, DestinationA));
        gameState.UpdateSnapshot(SnapshotWith(guiFocus: 1, DestinationB));

        Assert.Equal(PanelTab.Contacts, tracker.CurrentTab);
    }

    [Fact]
    public async Task PanelLive_PushesUpdate_WhenGameStateStoreChanges()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        using var response = await client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var initial = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.False(initial.RootElement.GetProperty("gameRunning").GetBoolean());

        var gameState = host.App.Services.GetRequiredService<GameStateStore>();
        gameState.UpdateSnapshot(new StatusSnapshot(4u, null, null, GameRunning: true, SignedIn: true));

        using var pushed = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.True(pushed.RootElement.GetProperty("gameRunning").GetBoolean());
        var slot1 = pushed.RootElement.GetProperty("slots").EnumerateArray().Single(s => s.GetProperty("index").GetInt32() == 1);
        Assert.Equal("Full", slot1.GetProperty("lit").GetString());
    }

    [Fact]
    public async Task PanelLive_DoesNotPush_WhenAnUpdateProducesNoObservableChange()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        using var response = await client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var initial = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.False(initial.RootElement.GetProperty("gameRunning").GetBoolean());

        var gameState = host.App.Services.GetRequiredService<GameStateStore>();
        // Flags=4 (bit 2, LandingGearDown) rather than an all-zero snapshot -
        // same non-ambiguous device the neighbouring test above uses. A
        // null snapshot (no Status.json read yet) makes every slot report
        // Off (LitStateResolver), so an all-zero real snapshot would be
        // indistinguishable from that initial connect state for every
        // bare-list lit condition on the starter layout - LandingGearToggle's
        // own bit flips it to Full, which is what actually makes this a
        // genuinely different push rather than a coincidental no-op.
        // [2026-09-07, superseded: this used to lean on PlayerHUDModeToggle's
        // now-removed lit condition for the same purpose - see
        // ref/docs/lit-state.md's "The HUD Mode reversal".]
        var unchangedSnapshot = new StatusSnapshot(4u, null, null, GameRunning: false, SignedIn: false);

        // A genuinely different push, deliberately consumed first and not
        // part of the dedup claim below.
        gameState.UpdateSnapshot(unchangedSnapshot);
        using var afterFirstRealSnapshot = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.False(afterFirstRealSnapshot.RootElement.GetProperty("gameRunning").GetBoolean());

        // Simulates Status.json's idle heartbeat (LC6): the file is rewritten
        // with unchanged content, so GameStateStore.Changed fires, but the
        // computed lit/gameRunning payload is identical to what was already
        // sent (the state established immediately above) - twice, to make
        // sure this isn't a one-shot coincidence.
        gameState.UpdateSnapshot(unchangedSnapshot);
        gameState.UpdateSnapshot(unchangedSnapshot);

        // A genuinely different change now - if the two no-op updates above
        // had each produced a push, this would be the third event since the
        // one consumed above; since they are correctly deduped, it must be
        // the very next one.
        gameState.UpdateSnapshot(new StatusSnapshot(4u, null, null, GameRunning: true, SignedIn: true));

        using var nextEvent = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.True(nextEvent.RootElement.GetProperty("gameRunning").GetBoolean());
    }

    /// <summary>
    /// How many delegates are currently on a field-like event, read through
    /// its compiler-generated backing field - the same reflection
    /// <c>PanelLive_ClientDisconnect_LogsChannelClosed_AndUnsubscribes</c>
    /// already uses on <c>GameStateStore.Changed</c>, and for the same
    /// reason: a leaked subscription has no other externally observable
    /// symptom.
    /// </summary>
    private static int SubscriberCount(object instance, string eventName)
    {
        var field = instance.GetType().GetField(eventName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return ((Delegate?)field!.GetValue(instance))?.GetInvocationList().Length ?? 0;
    }

    /// <summary>
    /// The live channel gained two more subscriptions on 2026-09-10 -
    /// <c>MacroRunner.Changed</c> and <c>LayoutStore.Saved</c>, both on
    /// host-lifetime singletons. A missing <c>-=</c> on either would grow
    /// that singleton's invocation list by one delegate per reconnect, each
    /// holding a closed connection's layout and channel alive, and nothing
    /// else would ever say so. The existing disconnect test covers only
    /// <c>GameStateStore.Changed</c>; this covers the two it does not.
    /// </summary>
    [Fact]
    public async Task PanelLive_ClientDisconnect_AlsoUnsubscribesFromTheRunnerAndTheLayoutStore()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        var runner = host.App.Services.GetRequiredService<MacroRunner>();
        var store = host.App.Services.GetRequiredService<LayoutStore>();

        // Baselines taken before the connection, never assumed to be zero -
        // the same lesson the neighbouring disconnect test records.
        var runnerBaseline = SubscriberCount(runner, "Changed");
        var storeBaseline = SubscriberCount(store, "Saved");

        var response = await client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        var stream = await response.Content.ReadAsStreamAsync();
        using (var reader = new StreamReader(stream))
        {
            await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        }

        // Proves the baselines are not trivially equal to the after-close
        // counts: the connection really did subscribe to both.
        Assert.Equal(runnerBaseline + 1, SubscriberCount(runner, "Changed"));
        Assert.Equal(storeBaseline + 1, SubscriberCount(store, "Saved"));

        response.Dispose();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline &&
               (SubscriberCount(runner, "Changed") != runnerBaseline || SubscriberCount(store, "Saved") != storeBaseline))
        {
            await Task.Delay(20);
        }

        Assert.Equal(runnerBaseline, SubscriberCount(runner, "Changed"));
        Assert.Equal(storeBaseline, SubscriberCount(store, "Saved"));
    }

    // ------------------------------------------------------------------
    // Macro timing, adjusted on the PC, reaching a connected device
    // (ref/docs/macro-timing.md). host.HostClient is the commander's own
    // browser on the loopback-only listener; client is a paired tablet with
    // its live channel already open.
    // ------------------------------------------------------------------

    private static (int HoldMs, int GapMs)? TimingOf(JsonDocument sseEvent)
    {
        var timing = sseEvent.RootElement.GetProperty("timing");
        return timing.ValueKind == JsonValueKind.Null
            ? null
            : (timing.GetProperty("holdMs").GetInt32(), timing.GetProperty("interPressGapMs").GetInt32());
    }

    private static Task<HttpResponseMessage> SaveTimingAsync(HttpClient client, int holdMs, int gapMs) =>
        client.PostAsJsonAsync(ApiPaths.MacroTiming, new { holdMs, interPressGapMs = gapMs }, JsonOptions);

    /// <summary>
    /// The commander's ask in full: <i>"Is there a way for changes made here
    /// to auto push to the clients?"</i> - a timing change made on the PC
    /// reaching a tablet that is already connected, with nobody touching the
    /// tablet. Driven end to end through the real host: a real POST on the
    /// loopback listener, a real SSE stream on the device listener.
    ///
    /// The game state is deliberately never touched during the connection,
    /// so a push that arrives can only have been caused by the save.
    /// </summary>
    [Fact]
    public async Task PanelLive_MacroTimingSavedOnTheHost_ReachesTheOpenChannel_WithNoReconnect()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640"); // seed the starter
        host.App.Services.GetRequiredService<GameStateStore>().UpdateSnapshot(Running(LandingGearDownFlag));

        using var response = await client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var initial = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));

        // An edge, not a level: the connect-time payload says nothing about
        // timing at all, so its presence later means "this just changed".
        Assert.Null(TimingOf(initial));

        Assert.Equal(HttpStatusCode.OK, (await SaveTimingAsync(host.HostClient, 275, 180)).StatusCode);

        using var pushed = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Equal((275, 180), TimingOf(pushed));
    }

    /// <summary>
    /// <b>The pushed value is the one just saved, never one this connection
    /// cached when it opened.</b> That is the precise defect
    /// <c>ref/docs/lit-state.md</c>'s "The glow that never arrived" records
    /// against the layout, and the reason a value already on disk BEFORE the
    /// stream opens is a different number from either of the two saved
    /// during it: an implementation that answered from a connect-time copy
    /// would report 200/150 twice and pass a single-save test.
    /// </summary>
    [Fact]
    public async Task PanelLive_TwoSuccessiveTimingSaves_PushEachSavedValue_NotOneCachedAtConnect()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        host.App.Services.GetRequiredService<GameStateStore>().UpdateSnapshot(Running(LandingGearDownFlag));

        // On disk before the stream opens - the value a cached implementation
        // would have to hand.
        Assert.Equal(HttpStatusCode.OK, (await SaveTimingAsync(host.HostClient, 200, 150)).StatusCode);

        using var response = await client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var initial = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Null(TimingOf(initial));

        Assert.Equal(HttpStatusCode.OK, (await SaveTimingAsync(host.HostClient, 275, 180)).StatusCode);
        using var first = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Equal((275, 180), TimingOf(first));

        Assert.Equal(HttpStatusCode.OK, (await SaveTimingAsync(host.HostClient, 310, 220)).StatusCode);
        using var second = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Equal((310, 220), TimingOf(second));
    }

    /// <summary>
    /// Out of range is still refused, and a refusal persists nothing and
    /// pushes nothing. The "pushes nothing" half is what a naive wiring gets
    /// wrong (raise first, validate later): proven the same way the
    /// idle-heartbeat dedupe test proves its own negative, by asserting that
    /// the NEXT event to arrive is the one caused by a later, valid save -
    /// which it could not be if the refused one had produced an event of its
    /// own.
    /// </summary>
    [Fact]
    public async Task PanelLive_TimingSaveRefusedAsOutOfRange_PushesNothing_AndPersistsNothing()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        host.App.Services.GetRequiredService<GameStateStore>().UpdateSnapshot(Running(LandingGearDownFlag));

        using var response = await client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var initial = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Null(TimingOf(initial));

        // Both ends of the hard boundary (MacroTimingSettings.MinMs/MaxMs),
        // so this cannot pass by refusing only the low one.
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveTimingAsync(host.HostClient, 0, 150)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveTimingAsync(host.HostClient, 150, 99999)).StatusCode);

        var onDisk = await host.HostClient.GetFromJsonAsync<MacroTimingEndpoint.Response>(ApiPaths.MacroTiming, JsonOptions);
        Assert.Equal(150, onDisk!.HoldMs);
        Assert.Equal(100, onDisk.InterPressGapMs);

        Assert.Equal(HttpStatusCode.OK, (await SaveTimingAsync(host.HostClient, 275, 180)).StatusCode);
        using var next = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Equal((275, 180), TimingOf(next));
    }

    /// <summary>
    /// The live channel gained a third host-lifetime subscription on
    /// 2026-09-10 (<c>MacroTimingSettingsStore.Saved</c>). A missing
    /// <c>-=</c> would grow that singleton's invocation list by one delegate
    /// per reconnect, each holding a closed connection's channel alive, and
    /// nothing else would ever say so - the same reason the neighbouring
    /// runner/layout-store test exists.
    /// </summary>
    [Fact]
    public async Task PanelLive_ClientDisconnect_AlsoUnsubscribesFromTheTimingStore()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        var timingStore = host.App.Services.GetRequiredService<MacroTimingSettingsStore>();

        var baseline = SubscriberCount(timingStore, "Saved");

        var response = await client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        var stream = await response.Content.ReadAsStreamAsync();
        using (var reader = new StreamReader(stream))
        {
            await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        }

        // Proves the baseline is not trivially equal to the after-close count.
        Assert.Equal(baseline + 1, SubscriberCount(timingStore, "Saved"));

        response.Dispose();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && SubscriberCount(timingStore, "Saved") != baseline)
        {
            await Task.Delay(20);
        }

        Assert.Equal(baseline, SubscriberCount(timingStore, "Saved"));
    }

    /// <summary>
    /// Same leak shape as the runner/layout-store/timing-store tests above,
    /// for <c>BindsFileWatcher.Changed</c>. A missing <c>-=</c> here would
    /// grow this host-lifetime singleton's invocation list by one delegate
    /// per reconnect, each holding a closed connection's channel alive.
    /// </summary>
    [Fact]
    public async Task PanelLive_ClientDisconnect_AlsoUnsubscribesFromTheBindsWatcher()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        var bindsWatcher = host.App.Services.GetRequiredService<BindsFileWatcher>();

        var baseline = SubscriberCount(bindsWatcher, "Changed");

        var response = await client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        var stream = await response.Content.ReadAsStreamAsync();
        using (var reader = new StreamReader(stream))
        {
            await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        }

        // Proves the baseline is not trivially equal to the after-close count.
        Assert.Equal(baseline + 1, SubscriberCount(bindsWatcher, "Changed"));

        response.Dispose();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && SubscriberCount(bindsWatcher, "Changed") != baseline)
        {
            await Task.Delay(20);
        }

        Assert.Equal(baseline, SubscriberCount(bindsWatcher, "Changed"));
    }

    /// <summary>
    /// The commander asked for a server-side area, <b>not</b> for the
    /// device's own Timing pane to be taken away - so
    /// <c>/api/macro-timing</c> is deliberately NOT a host-only route, under
    /// either method, and a paired tablet can still read and change it.
    /// Pinned rather than left implicit because "tidy this up like the
    /// authoring routes" is a plausible later edit that would silently
    /// remove a pane the commander uses.
    /// </summary>
    [Fact]
    public async Task MacroTiming_IsStillReachableFromAPairedDevice_NotHostOnly()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ApiPaths.MacroTiming)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SaveTimingAsync(client, 275, 180)).StatusCode);

        // And what the device saved is what the PC now reads - one machine-wide
        // value, not a per-device one (ref/docs/macro-timing.md's "Scope").
        var fromHost = await host.HostClient.GetFromJsonAsync<MacroTimingEndpoint.Response>(ApiPaths.MacroTiming, JsonOptions);
        Assert.Equal(275, fromHost!.HoldMs);
        Assert.Equal(180, fromHost.InterPressGapMs);
    }

    // ------------------------------------------------------------------
    // A rebind made in Elite, reaching an already-open device
    // (BindsFileWatcher, ref/docs/bindings-source.md). Driven end to end
    // through a real host: a real FileSystemWatcher watching a real file, a
    // real SSE stream on the device listener. The bindings file is planted
    // BEFORE the host starts (via beforeStart) so discovery's own startup
    // sweep - not a later POST /api/bindings/refresh - is what
    // BindsFileWatcher's constructor sees, exactly like
    // Panel_NoCommanderBindingsFile_OnlyStockControlScheme_ResolvesViaStock_PanelIsNotDead
    // does for the stock-scheme path above.
    // ------------------------------------------------------------------

    private static bool? BindingsChangedOf(JsonDocument sseEvent)
    {
        var prop = sseEvent.RootElement.GetProperty("bindingsChanged");
        return prop.ValueKind == JsonValueKind.Null ? null : prop.GetBoolean();
    }

    /// <summary>
    /// The commander's ask, for bindings rather than timing: a rebind made in
    /// Elite while a device's live channel is already open reaches it with no
    /// reconnect and no tap on "Refresh controls". Bounded (5s), not a fixed
    /// sleep, because this is the one path in this file that depends on a
    /// real OS FileSystemWatcher plus BindsFileWatcher's own real debounce
    /// window - both real timing StatusFileWatcherTests' own end-to-end test
    /// already accepts for the same reason.
    /// </summary>
    [Fact]
    public async Task PanelLive_ARebindMadeInElite_ReachesTheOpenChannel_WithNoReconnect()
    {
        await using var host = await RunningHost.StartAsync(beforeStart: temp =>
        {
            var bindingsDir = temp.Combine("NoBindings");
            Directory.CreateDirectory(bindingsDir);
            File.WriteAllText(
                Path.Combine(bindingsDir, "Custom.4.2.binds"),
                """<Root PresetName="Custom" MajorVersion="4" MinorVersion="2"><LandingGearToggle><Primary Device="Keyboard" Key="Key_G" /><Secondary Device="{NoDevice}" Key="" /></LandingGearToggle></Root>""");
            File.WriteAllText(Path.Combine(bindingsDir, "StartPreset.4.start"), "Custom\n");
        });
        var client = await PairedClientAsync(host);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640"); // seed the starter layout

        using var response = await client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var initial = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));

        // An edge, not a level: the connect-time payload says nothing about
        // a rebind at all, so its presence later means "this just changed".
        Assert.Null(BindingsChangedOf(initial));

        // The rebind itself: Elite rewriting the SAME file this host already
        // selected, with a different key bound.
        File.WriteAllText(
            Path.Combine(host.BindingsDirectory, "Custom.4.2.binds"),
            """<Root PresetName="Custom" MajorVersion="4" MinorVersion="2"><LandingGearToggle><Primary Device="Keyboard" Key="Key_H" /><Secondary Device="{NoDevice}" Key="" /></LandingGearToggle></Root>""");

        using var pushed = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.True(BindingsChangedOf(pushed));

        // The signal alone proves nothing about content - GET /api/panel is
        // the actual re-fetch path a client follows it with
        // (ref/docs/bindings-source.md), and it already reflects the rebind
        // with no restart, no refresh tap, and no caching in between.
        var panelResponse = await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var panelBody = await panelResponse.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);
        var slot1 = panelBody!.Slots.Single(s => s.Index == 1);
        Assert.Equal("H", slot1.Chord);
    }

    private static string LitOf(JsonDocument sseEvent, int slotIndex) =>
        sseEvent.RootElement
            .GetProperty("slots")
            .EnumerateArray()
            .Single(s => s.GetProperty("index").GetInt32() == slotIndex)
            .GetProperty("lit")
            .GetString()!;

    /// <summary>
    /// The regression test for the defect this whole behaviour was
    /// found by: <b>a layout edited while the live channel is open never
    /// reached that channel at all</b>, because the layout was loaded once
    /// at connect and reused for every later push. The commander latched
    /// secondary fire, the key held correctly for its whole duration, and
    /// the button did not light - until a full browser reload, which is the
    /// only thing that opens a new stream. A Controls refresh does not, and
    /// neither does any layout edit (the client re-fetches
    /// <c>GET /api/panel</c> and leaves the <c>EventSource</c> alone,
    /// deliberately - reopening it would release every latch under release
    /// rule 2).
    ///
    /// Driven through the real edit route rather than the store, because
    /// that is the commander's own path. The latch flag itself cannot be
    /// driven here for the reason stated at the SlotLatch section above (it
    /// would mean a real KeyDown through the production injector), so this
    /// pins the identical mechanism on the lit level of a REASSIGNED slot,
    /// which needs no injection at all: the slot's action goes from a macro
    /// (never lit) to LandingGearToggle (Full while the gear is down).
    /// </summary>
    [Fact]
    public async Task PanelLive_ASlotEditedMidConnection_ReachesTheOpenChannel_WithNoReconnect()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640"); // seed the starter

        // Before the stream opens, so the ONLY thing that changes during the
        // connection below is the edit itself - a push that arrives has to
        // have been caused by it.
        host.App.Services.GetRequiredService<GameStateStore>().UpdateSnapshot(Running(LandingGearDownFlag));

        using var response = await client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var initial = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Equal("Off", LitOf(initial, 0));
        Assert.Equal("Full", LitOf(initial, 1));

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync(
                ApiPaths.SlotAssign,
                new SlotEditEndpoint.AssignRequest(0, 0, "LandingGearToggle", null, null))).StatusCode);

        using var pushed = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Equal("Full", LitOf(pushed, 0));
    }

    private static bool? LayoutChangedOf(JsonDocument sseEvent)
    {
        var prop = sseEvent.RootElement.GetProperty("layoutChanged");
        return prop.ValueKind == JsonValueKind.Null ? null : prop.GetBoolean();
    }

    /// <summary>
    /// The bug this task fixes: editing a device's layout live from the PC
    /// (<c>?asDevice=&lt;id&gt;</c>) never told that device's own open live
    /// channel to re-fetch - only each slot's lit level replayed, which very
    /// often looks identical before and after a save (a label change here:
    /// no lit level moves at all, so <see cref="PanelLiveEndpoint.ShouldPush"/>'s
    /// own lit-state dedupe would silently swallow the push without the new
    /// <c>LayoutChanged</c> edge bypassing it - unlike
    /// <see cref="PanelLive_ASlotEditedMidConnection_ReachesTheOpenChannel_WithNoReconnect"/>
    /// above, which happens to also change a lit level and so cannot tell
    /// the two mechanisms apart).
    /// </summary>
    [Fact]
    public async Task PanelLive_ALabelEditMidConnection_CarriesLayoutChanged_DespiteNoLitLevelMoving()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        // Real bindings first: without them the layout save below fails
        // LayoutValidator's own check (every slot action must be a known
        // binding), the route tolerates that failure and still returns 200,
        // and LayoutStore.Saved is never raised - so the push this test
        // exists to prove would silently never arrive for a reason that has
        // nothing to do with LayoutChanged at all (measured, not assumed:
        // the first draft of this test omitted it and hung on the very next
        // ReadNextSseEventAsync).
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640"); // seed the starter layout

        using var response = await client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var initial = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Null(LayoutChangedOf(initial));

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync(ApiPaths.SlotLabel, new SlotEditEndpoint.LabelRequest(0, 1, "TABLET"))).StatusCode);

        using var pushed = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.True(LayoutChangedOf(pushed));

        // An edge, not a level: a later, unrelated push must not still carry
        // it - exactly the replay bug BindingsChanged's own fix closed
        // (2026-09-11's "rebind locks the panel"). A macro-timing save is
        // used as the "something else pushed" trigger because ShouldPush
        // never dedupes it (unconditional, like every edge field), so this
        // cannot itself be swallowed by the lit-state dedupe the way an
        // unrelated game-state change touching no bound slot would be.
        Assert.Equal(HttpStatusCode.OK, (await SaveTimingAsync(client, 275, 180)).StatusCode);
        using var later = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Null(LayoutChangedOf(later));
    }

    /// <summary>
    /// The filter this feature must not bypass: a layout save belongs to one
    /// device, so a save to device B's own layout must never reach device
    /// A's live connection at all - not with <c>layoutChanged</c>, and not
    /// with anything else. Proved by causing a genuinely unrelated push
    /// right after B's save and checking THAT push - a bare "no event
    /// arrived within N seconds" assertion would also pass if the push
    /// merely arrived late, which proves nothing.
    /// </summary>
    [Fact]
    public async Task PanelLive_ALayoutSaveOnADifferentDevice_NeverReachesThisConnection()
    {
        await using var host = await RunningHost.StartAsync();
        var codeA = host.App.Services.GetRequiredService<DeviceRegistry>().CurrentCode;
        var pairAResponse = await host.Client.PostAsJsonAsync(
            ApiPaths.Pair, new { code = codeA, deviceName = "A", deviceClass = "tablet" }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, pairAResponse.StatusCode);
        // Real bindings first - same reasoning as the single-device test
        // above: B's label save below must actually persist and raise
        // LayoutStore.Saved, or this test would pass even with the device
        // filter removed, for the unrelated reason that B's save never
        // reached the store at all. One bindings directory serves the whole
        // host, so this covers both A and B.
        await UseFullyBoundStarterBindingsAsync(host, host.Client);
        await host.Client.GetAsync($"{ApiPaths.Panel}?w=360&h=640"); // seed A's starter layout

        var openResponse = await host.Client.PostAsync(ApiPaths.DevicesOpenPairing, null);
        var openBody = await openResponse.Content.ReadFromJsonAsync<DevicesEndpoint.OpenPairingResponse>(JsonOptions);

        using var bClient = new HttpClient { BaseAddress = host.Client.BaseAddress };
        var pairBResponse = await bClient.PostAsJsonAsync(
            ApiPaths.Pair, new { code = openBody!.Code, deviceName = "B", deviceClass = "tablet" }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, pairBResponse.StatusCode);
        await bClient.GetAsync($"{ApiPaths.Panel}?w=360&h=640"); // seed B's starter layout

        using var response = await host.Client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var initial = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Null(LayoutChangedOf(initial));

        // Device B edits ITS OWN layout - never through A's connection.
        Assert.Equal(
            HttpStatusCode.OK,
            (await bClient.PostAsJsonAsync(ApiPaths.SlotLabel, new SlotEditEndpoint.LabelRequest(0, 1, "TABLET"))).StatusCode);

        // A genuinely unrelated push, so A's connection has something to
        // deliver: if B's save had incorrectly reached it, THIS is the event
        // that push would have arrived as, carrying layoutChanged. A
        // macro-timing save is used (rather than a game-state change) so
        // this cannot itself be swallowed by the lit-state dedupe.
        Assert.Equal(HttpStatusCode.OK, (await SaveTimingAsync(host.Client, 275, 180)).StatusCode);
        using var pushed = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Null(LayoutChangedOf(pushed));
    }

    /// <summary>
    /// Puts a macro whose only step is a long <c>wait</c> on slot 0 and
    /// returns its id. <b>The wait is what makes this drivable at all:</b> a
    /// macro that presses nothing never reaches <c>IKeyInjector</c>, so this
    /// runs the production <c>MacroRunner</c> the host actually built without
    /// a single Win32 call - unlike a press macro, which would depend on
    /// whether Elite happened to be the foreground window of the machine
    /// running the suite.
    /// </summary>
    private static async Task<string> MacroOnSlotZeroThatJustWaitsAsync(RunningHost host, HttpClient client)
    {
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640"); // seed the starter

        var id = await SaveMacroAsync(host, """{ "name": "Glow", "steps": [ { "wait": 30000 } ] }""");
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync(
                ApiPaths.SlotAssign, new SlotEditEndpoint.AssignRequest(0, 0, null, id, null))).StatusCode);
        return id;
    }

    private static (MacroRunner Runner, MacroDefinition Definition, LunaPanel.Core.Bindings.BindingsFile Bindings) MacroRunPartsFor(RunningHost host, string macroId)
    {
        var runner = host.App.Services.GetRequiredService<MacroRunner>();
        var definition = host.App.Services
            .GetRequiredService<LunaPanel.Server.Macros.MacroCatalogue>()
            .All()
            .Single(m => m.Id == macroId);
        var bindings = host.App.Services.GetRequiredService<LiveBindingsReader>().Read();
        return (runner, definition, bindings);
    }

    /// <summary>
    /// The commander's ruling of 2026-09-10: "do that for any macro as long as it's
    /// running as well". The button glows for the duration of the run and
    /// goes out when it ends - here by the commander stopping it, which is
    /// the ending most likely to be forgotten, because nothing later would
    /// ever come along to correct a button left lit by it.
    ///
    /// Driven through the real host, the real live channel and the real
    /// MacroRunner the host built - see
    /// <see cref="MacroOnSlotZeroThatJustWaitsAsync"/> for why that reaches
    /// no injector.
    /// </summary>
    [Fact]
    public async Task PanelLive_AMacroRunning_LightsItsSlot_AndGoesDarkWhenItIsStopped()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);
        var macroId = await MacroOnSlotZeroThatJustWaitsAsync(host, client);
        host.App.Services.GetRequiredService<GameStateStore>().UpdateSnapshot(Running(LandingGearDownFlag));

        using var response = await client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var initial = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Equal("Off", LitOf(initial, 0));

        var (runner, definition, bindings) = MacroRunPartsFor(host, macroId);
        var run = runner.RunAsync(definition, bindings);

        using var lit = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Equal("Full", LitOf(lit, 0));

        // Stopped the way a commander stops one: the same macro again.
        Assert.Equal(
            MacroRunOutcome.Cancelled,
            (await runner.RunAsync(definition, bindings).WaitAsync(TimeSpan.FromSeconds(10))).Outcome);
        Assert.Equal(MacroRunOutcome.Cancelled, (await run.WaitAsync(TimeSpan.FromSeconds(10))).Outcome);

        using var dark = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Equal("Off", LitOf(dark, 0));
    }

    /// <summary>
    /// The other half of the running-macro glow, and the failure
    /// <c>ref/docs/lit-state.md</c> warns about for the latch in exactly the
    /// same words: a slot lit on one endpoint and not the other lights up on
    /// load and goes dark on the next push, or the reverse. A device opening
    /// its panel while a macro is already running must see it lit on the
    /// FIRST paint, with no live event needed.
    /// </summary>
    [Fact]
    public async Task Panel_AMacroAlreadyRunning_IsLitOnTheVeryFirstPaint()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        var client = await PairedClientAsync(host);
        var macroId = await MacroOnSlotZeroThatJustWaitsAsync(host, client);

        var (runner, definition, bindings) = MacroRunPartsFor(host, macroId);
        var run = runner.RunAsync(definition, bindings);

        var panel = await client.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        Assert.Equal("Full", panel!.Slots.Single(s => s.Index == 0).Lit);

        Assert.Equal(
            MacroRunOutcome.Cancelled,
            (await runner.RunAsync(definition, bindings).WaitAsync(TimeSpan.FromSeconds(10))).Outcome);
        Assert.Equal(MacroRunOutcome.Cancelled, (await run.WaitAsync(TimeSpan.FromSeconds(10))).Outcome);

        var after = await client.GetFromJsonAsync<PanelEndpoint.PanelResponse>($"{ApiPaths.Panel}?w=360&h=640", JsonOptions);
        Assert.Equal("Off", after!.Slots.Single(s => s.Index == 0).Lit);
    }

    /// <summary>
    /// Closes O36 (<c>tests/notes/open-items.md</c>): a latch has never been
    /// driven all the way onto the wire before this test - <c>LatchedLitTests</c>,
    /// <c>PanelLiveEndpointTests.BuildState_ASlotWhoseKeyIsCurrentlyHeld_GoesOutAsFull</c>
    /// and <c>LatchWiringSourceGuardTests</c> each pin one piece of the chain
    /// in isolation, but nothing latches a real key through a real host with
    /// a recording fake and reads the resulting SSE event back. This is the
    /// one test in this whole file that does NOT use the default
    /// <see cref="SafeGuardRefusingKeyInjector"/> - it supplies its own
    /// <see cref="Win32KeyInjector"/>, built the same way (the test-seam
    /// constructor), but with a foreground fake that reports Elite so the
    /// press route's <c>CheckGuard</c> call actually passes, and a recording
    /// <c>sendInput</c> fake instead of an inert one, so what actually got
    /// sent can be read back rather than merely inferred from the outcome
    /// string. Still never reaches a real Win32 API: both delegates handed
    /// to the constructor are this test's own fakes, exactly like every
    /// other test in <c>Win32KeyInjectorTests</c>.
    /// </summary>
    [Fact]
    public async Task Press_LatchSlot_WithGuardPassing_ActuallySendsTheChord_AndTheLiveChannelReportsItHeld()
    {
        const uint KeyUpFlag = 0x0002;

        var sent = new List<NativeMethods.INPUT>();
        var recordingInjector = new Win32KeyInjector(
            new DiagnosticRingBuffer(64),
            () => false,
            () => new ForegroundContext("EliteDangerous64", null, null),
            inputs =>
            {
                sent.AddRange(inputs);
                return ((uint)inputs.Length, 0);
            });

        await using var host = await RunningHost.StartAsync(keyInjector: recordingInjector);
        var client = await PairedClientAsync(host);
        await UseFullyBoundStarterBindingsAsync(host, client);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync(ApiPaths.SlotLatch, new SlotEditEndpoint.LatchRequest(0, 1, true))).StatusCode);

        using var response = await client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var initial = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Equal("Off", LitOf(initial, 1));

        var pressResponse = await client.PostAsJsonAsync(ApiPaths.Press, new { page = 0, slot = 1 });
        Assert.Equal(HttpStatusCode.OK, pressResponse.StatusCode);
        using var pressBody = JsonDocument.Parse(await pressResponse.Content.ReadAsStringAsync());
        Assert.Equal("Latched", pressBody.RootElement.GetProperty("outcome").GetString());

        // The KeyDown(s) genuinely reached this test's own recording fake -
        // not merely believed to have, from the outcome string alone - and
        // none of them is a release (a latch's press branch calls KeyDown
        // only; ChordPresser's unconditional KeyUp is never in this path).
        Assert.NotEmpty(sent);
        Assert.All(sent, i => Assert.Equal(0u, i.U.ki.dwFlags & KeyUpFlag));
        sent.Clear();

        using var lit = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Equal("Full", LitOf(lit, 1));

        var releaseResponse = await client.PostAsJsonAsync(ApiPaths.Press, new { page = 0, slot = 1 });
        Assert.Equal(HttpStatusCode.OK, releaseResponse.StatusCode);
        using var releaseBody = JsonDocument.Parse(await releaseResponse.Content.ReadAsStringAsync());
        Assert.Equal("Released", releaseBody.RootElement.GetProperty("outcome").GetString());

        // The release genuinely reached the fake too, and every one of these
        // is a KeyUp - LatchRegistry.ReleaseChordLocked's own contract.
        Assert.NotEmpty(sent);
        Assert.All(sent, i => Assert.NotEqual(0u, i.U.ki.dwFlags & KeyUpFlag));

        using var dark = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Equal("Off", LitOf(dark, 1));
    }

    // -------------------------------------------------------------------
    // Automatic vessel-context page switching, driven end to end through a
    // real Kestrel round trip (ref/docs/vessel-context.md). AutoPageSwitcher
    // and ContextPageSelector are unit-tested in isolation; what these prove
    // is the WIRING - that a Status.json change and a journal event both
    // reach the switcher, that the decision survives the live channel's
    // dedupe, and that it arrives on the wire as switchToPage.
    // -------------------------------------------------------------------

    private const uint InSrvFlag = 1u << 26;
    private const uint LandingGearDownFlag = 1u << 2;

    private static StatusSnapshot Running(uint flags) => new(flags, null, null, GameRunning: true, SignedIn: true);

    private static LayoutPage ContextPage(string name, params string[] showWhen) =>
        new(name, "t6", Array.Empty<LayoutSlot>(), Array.Empty<LayoutSlot>(), showWhen);

    /// <summary>
    /// Appends context-declaring pages to a paired device's real, stored
    /// layout. There is no route that sets <c>showWhen</c> today (see
    /// <c>ref/docs/vessel-context.md</c>'s "Not decided"), so the store is
    /// the only way in - the same way the layout-reset test above reaches it.
    /// </summary>
    private static async Task<string> DeviceWithContextPagesAsync(RunningHost host, params LayoutPage[] extraPages)
    {
        var device = await PairAsync(host, deviceName: "Context device", deviceClass: "tablet");
        // The synthetic environment has no bindings at all, so every starter
        // action would be unknown and LayoutValidator would refuse the save.
        // This is the same real bindings file the editor tests above use.
        await UseFullyBoundStarterBindingsAsync(host, host.Client);
        await host.Client.GetAsync($"{ApiPaths.Panel}?w=1024&h=768");

        var store = host.App.Services.GetRequiredService<LayoutStore>();
        var liveBindings = host.App.Services.GetRequiredService<LiveBindingsReader>();
        var knownActionNames = liveBindings.Read().Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

        // The starter layout is what LayoutAccess.LoadOrSeed hands this
        // device on its first panel request; nothing has SAVED one yet (the
        // seed is in-memory until an edit persists it), so this builds on the
        // same shipped content rather than on a file that is not there.
        var current = StarterLayout.Load();
        var updated = new Layout(current.SchemaVersion, current.Pages.Concat(extraPages).ToList());
        Assert.Equal(LayoutSaveOutcome.Saved, store.Save(device.DeviceId, updated, knownActionNames).Outcome);
        return device.DeviceId;
    }

    private static int? SwitchToPageOf(JsonDocument sseEvent)
    {
        var property = sseEvent.RootElement.GetProperty("switchToPage");
        return property.ValueKind == JsonValueKind.Null ? null : property.GetInt32();
    }

    [Fact]
    public async Task PanelLive_AContextChange_PushesASwitchToTheMatchingPage()
    {
        await using var host = await RunningHost.StartAsync();
        await DeviceWithContextPagesAsync(host, ContextPage("SRV", "InSrv"));

        using var response = await host.Client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var initial = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Null(SwitchToPageOf(initial));

        host.App.Services.GetRequiredService<GameStateStore>().UpdateSnapshot(Running(InSrvFlag));

        using var pushed = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        // [2026-09-09, superseded: was 1, when the starter was a single
        // context-free page and the appended one landed at index 1. The
        // starter now ships five context pages of its own (indices 1-5), so
        // the appended page is index 6. Index 6 and not 1: the shipped SRV
        // and NOMAD pages both name a Vessel: term and this synthetic host's
        // journal has never named a vessel type, so neither of them matches
        // an InSrv snapshot - which is the vessel term biting, visible here
        // for free.]
        Assert.Equal(6, SwitchToPageOf(pushed));
    }

    /// <summary>
    /// The non-change half, and the one a single-direction test would miss.
    /// The second snapshot is a real, observable change to the panel (the
    /// landing gear comes down, so slot 1 lights) - so it definitely
    /// produces a push - while the vessel context is identical. A switcher
    /// that answered the level rather than the change would re-assert here,
    /// and LC6's ~11.8s idle heartbeat would do it every twelve seconds
    /// forever.
    /// </summary>
    [Fact]
    public async Task PanelLive_AFurtherPushInTheSameContext_CarriesNoSwitch()
    {
        await using var host = await RunningHost.StartAsync();
        await DeviceWithContextPagesAsync(host, ContextPage("SRV", "InSrv"));
        var gameState = host.App.Services.GetRequiredService<GameStateStore>();

        using var response = await host.Client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var initial = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Null(SwitchToPageOf(initial));

        gameState.UpdateSnapshot(Running(InSrvFlag));
        using var switched = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        // [2026-09-09, superseded: was 1 - see the sibling test above for
        // why the appended page is now index 6.]
        Assert.Equal(6, SwitchToPageOf(switched));

        gameState.UpdateSnapshot(Running(InSrvFlag | LandingGearDownFlag));

        using var pushed = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Null(SwitchToPageOf(pushed));
        var slot1 = pushed.RootElement.GetProperty("slots").EnumerateArray().Single(s => s.GetProperty("index").GetInt32() == 1);
        Assert.Equal("Full", slot1.GetProperty("lit").GetString());
    }

    /// <summary>
    /// "When no page matches: do nothing. Stay where you are." A commander
    /// who has built no page for the SRV they are in is not thrown to a
    /// blank one.
    ///
    /// <para>[2026-09-09: this used to read "the starter layout's single
    /// page declares no context at all". That is no longer why it passes.
    /// The starter now ships five context pages, but both SRV ones name a
    /// <c>Vessel:</c> term and this host's journal has never named a vessel
    /// type, while the fighter and on-foot pages want different flags - so
    /// an <c>InSrv</c>-only snapshot still matches nothing. The assertion is
    /// unchanged; it now proves rather more than it did.]</para>
    /// </summary>
    [Fact]
    public async Task PanelLive_NoPageDeclaresTheNewContext_PushesNoSwitch()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        using var response = await client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var initial = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Null(SwitchToPageOf(initial));

        host.App.Services.GetRequiredService<GameStateStore>().UpdateSnapshot(Running(InSrvFlag));

        using var pushed = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.True(pushed.RootElement.GetProperty("gameRunning").GetBoolean());
        Assert.Null(SwitchToPageOf(pushed));
    }

    /// <summary>
    /// The journal half of the context, wired through the real host: the
    /// flags say whether (<c>InSrv</c>), the journal says which. Nothing in
    /// <c>Status.json</c> changes between the two pushes below - only a
    /// <c>LaunchVessel</c> naming the Nomad - so a channel subscribed to
    /// <c>GameStateStore</c> alone would never fire at all.
    ///
    /// <para>[2026-09-10, superseded: the naming event here was
    /// <c>DockSRV</c> carrying <c>"SRVType" = "lander01"</c>. <c>DockSRV</c>
    /// names the vessel the commander has just got OUT of and now clears the
    /// current vessel rather than setting it, so it can no longer name an
    /// SRV to switch to. The Nomad's entry event is <c>LaunchVessel</c>; the
    /// Scarab's and the Scorpion's is <c>LaunchSRV</c>, driven end to end by
    /// <see cref="PanelLive_LaunchSrvNamingTheScarab_PushesASwitchToTheScarabsOwnPage"/>
    /// below. The expected page index, 2, is unchanged.]</para>
    ///
    /// <para>[2026-09-09, superseded: this used to append its own
    /// <c>ContextPage("NOMAD", "InSrv", "Vessel:lander01")</c> and expect
    /// index 1. The starter layout now SHIPS that page, at index 2
    /// (<c>ref/docs/vessel-context.md</c>'s "The default context pages"), so
    /// the appended copy could never be reached - the shipped one always
    /// matched first. Appending nothing and expecting the shipped page is
    /// the same wiring proof against real content rather than against a page
    /// the test built for itself.]</para>
    /// </summary>
    [Fact]
    public async Task PanelLive_AJournalEventNamingTheSrv_PushesASwitch_WithNoStatusChangeAtAll()
    {
        await using var host = await RunningHost.StartAsync();
        await DeviceWithContextPagesAsync(host);
        var gameState = host.App.Services.GetRequiredService<GameStateStore>();
        var journal = host.App.Services.GetRequiredService<JournalStateStore>();

        using var response = await host.Client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var initial = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Null(SwitchToPageOf(initial));

        // In an SRV, but the journal has not said which one yet - both
        // shipped SRV pages name a Vessel: term, so nothing matches and
        // nothing moves.
        gameState.UpdateSnapshot(Running(InSrvFlag));
        using var inAnUnknownSrv = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Null(SwitchToPageOf(inAnUnknownSrv));

        journal.Record(new JournalEvent(
            "LaunchVessel",
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string> { ["VesselType"] = "lander01" }));

        using var pushed = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        // The shipped NOMAD page - index 2, after SHIP (0) and SRV (1).
        // [2026-09-09, superseded: was 1, the test's own appended page.]
        Assert.Equal(2, SwitchToPageOf(pushed));
    }

    /// <summary>
    /// <b>The commander's defect of 2026-09-10, driven end to end through a
    /// real running host.</b> <c>LaunchSRV</c> is the Scarab's and the
    /// Scorpion's launch event - <c>LaunchVessel</c> is the Nomad's, and
    /// only the Nomad's. Nothing here knew the first spelling, so boarding
    /// the Scarab named no vessel and whatever the journal had last said
    /// still stood.
    ///
    /// <para>The <c>InSrv</c> push before it is the load-bearing half: it is
    /// there so this proves the switch came from the journal line, over a
    /// channel with no <c>Status.json</c> change left to ride on.</para>
    /// </summary>
    [Fact]
    public async Task PanelLive_LaunchSrvNamingTheScarab_PushesASwitchToTheScarabsOwnPage()
    {
        await using var host = await RunningHost.StartAsync();
        await DeviceWithContextPagesAsync(host);
        var gameState = host.App.Services.GetRequiredService<GameStateStore>();
        var journal = host.App.Services.GetRequiredService<JournalStateStore>();

        using var response = await host.Client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var initial = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Null(SwitchToPageOf(initial));

        gameState.UpdateSnapshot(Running(InSrvFlag));
        using var inAnUnknownSrv = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.Null(SwitchToPageOf(inAnUnknownSrv));

        journal.Record(new JournalEvent(
            "LaunchSRV",
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string> { ["SRVType"] = "testbuggy" }));

        using var pushed = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        // The shipped SRV page - index 1. NOT 2, which is the Nomad's.
        Assert.Equal(1, SwitchToPageOf(pushed));
    }

    [Fact]
    public async Task PanelLive_ClientDisconnect_LogsChannelClosed_AndUnsubscribes()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        var ringBuffer = host.App.Services.GetRequiredService<DiagnosticRingBuffer>();

        // Baseline taken BEFORE the live connection opens, not assumed to be
        // zero. [2026-09-07, superseded: before this, the baseline WAS zero,
        // and this test asserted GameStateStore.Changed was null outright
        // after disconnect.] ServerHostBuilder now installs its own
        // permanent subscriber (PanelTabTracker's GuiFocus-edge watcher,
        // ref/docs/panel-tab-tracking.md) for the whole host's lifetime, so
        // "zero subscribers" is no longer the correct at-rest state - "back
        // to whatever it was before this connection" is, and is what
        // actually distinguishes a real per-connection leak from this
        // permanent, single, expected subscription.
        var changedField = typeof(GameStateStore).GetField("Changed", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(changedField);
        var gameStateForBaseline = host.App.Services.GetRequiredService<GameStateStore>();
        var baselineSubscriberCount = ((Delegate?)changedField!.GetValue(gameStateForBaseline))?.GetInvocationList().Length ?? 0;

        var response = await client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
        var stream = await response.Content.ReadAsStreamAsync();
        using (var reader = new StreamReader(stream))
        {
            await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        }

        // Disposing the response mid-stream is what a real client
        // navigating away or closing the app looks like from the server's
        // side - RequestAborted should fire and the handler's finally block
        // should run. Bounded poll-until-true, not a fixed sleep, since
        // exactly how long the server takes to notice varies.
        response.Dispose();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline &&
               !ringBuffer.Snapshot().Any(e => e.Category == "Status" && e.Message == "Live channel closed"))
        {
            await Task.Delay(20);
        }

        Assert.Contains(ringBuffer.Snapshot(), e => e.Category == "Status" && e.Message == "Live channel opened");
        Assert.Contains(ringBuffer.Snapshot(), e => e.Category == "Status" && e.Message == "Live channel closed");

        // The log lines above prove the finally block ran, but they are
        // written unconditionally by that block, so they cannot by
        // themselves distinguish "cleaned up properly" from "logged closed
        // but forgot to unsubscribe" - a real, silent leak that would grow
        // GameStateStore.Changed's invocation list by one delegate (and keep
        // this connection's closed HttpContext alive) per reconnect, with no
        // other externally observable symptom. GameStateStore.Changed is a
        // plain field-like event, so its compiler-generated backing field
        // shares its name and is inspectable via reflection - the only way
        // to check this without adding test-only surface to GameStateStore
        // itself.
        var gameState = host.App.Services.GetRequiredService<GameStateStore>();
        var subscriberCountAfterClose = ((Delegate?)changedField!.GetValue(gameState))?.GetInvocationList().Length ?? 0;
        Assert.Equal(baselineSubscriberCount, subscriberCountAfterClose);
    }

    [Fact]
    public async Task PanelLive_TwoSequentialConnections_EachOpensAndClosesIndependently_NoLeak()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        var ringBuffer = host.App.Services.GetRequiredService<DiagnosticRingBuffer>();

        for (var i = 0; i < 2; i++)
        {
            var response = await client.GetAsync(ApiPaths.PanelLive, HttpCompletionOption.ResponseHeadersRead);
            var stream = await response.Content.ReadAsStreamAsync();
            using (var reader = new StreamReader(stream))
            {
                await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
            }

            response.Dispose();
        }

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline &&
               ringBuffer.Snapshot().Count(e => e.Category == "Status" && e.Message == "Live channel closed") < 2)
        {
            await Task.Delay(20);
        }

        Assert.Equal(2, ringBuffer.Snapshot().Count(e => e.Category == "Status" && e.Message == "Live channel opened"));
        Assert.Equal(2, ringBuffer.Snapshot().Count(e => e.Category == "Status" && e.Message == "Live channel closed"));
    }

    [Fact]
    public async Task Press_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(ApiPaths.Press, new { page = 0, slot = 1 }, JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Press_DegradedMacroSlot_RefusesWithoutFiring_AndNamesTheMissingMacro()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        // Slot 0 of the seeded starter layout - the deliberately-missing
        // "request-docking" macro.
        var response = await client.PostAsJsonAsync(ApiPaths.Press, new { page = 0, slot = 0 }, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("fired").GetBoolean());
        Assert.Contains("request-docking", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Press_DegradedDisembarkMacroSlot_RefusesWithoutFiring_AndNeverReachesTheInjector()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        // Slot 17 of the seeded starter layout - "disembark", degraded here
        // because UI_Down/UI_Select aren't bound in this synthetic
        // environment (same reasoning as Press_DegradedMacroSlot's slot 0,
        // for a macro that genuinely exists rather than one that doesn't).
        // Never reaches MacroPresser.RunAsync, let alone a real Win32 API -
        // same structural guarantee this whole file's header comment states
        // for ChordPresser.PressAsync.
        var response = await client.PostAsJsonAsync(ApiPaths.Press, new { page = 0, slot = 17 }, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("fired").GetBoolean());
        Assert.Equal("NotUsable", body.RootElement.GetProperty("outcome").GetString());
        Assert.Contains("disembark", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Press_UnboundRealAction_Refuses_ReasonSaysNotBound()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.PostAsJsonAsync(ApiPaths.Press, new { page = 0, slot = 1 }, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("fired").GetBoolean());
        Assert.Equal("NotUsable", body.RootElement.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Press_NoSuchSlotIndex_RefusesCleanly_NeverThrows()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.PostAsJsonAsync(ApiPaths.Press, new { page = 0, slot = 999 }, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("fired").GetBoolean());
        Assert.Equal("NoSuchSlot", body.RootElement.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Press_MalformedJsonBody_Returns400_NotAnUnhandledException()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        var response = await client.PostAsync(
            ApiPaths.Press, new StringContent("{not valid json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Press_EveryRefusal_IsLoggedThroughDiagnostics()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        await client.PostAsJsonAsync(ApiPaths.Press, new { page = 0, slot = 0 }, JsonOptions);

        var ringBuffer = host.App.Services.GetRequiredService<DiagnosticRingBuffer>();
        Assert.Contains(ringBuffer.Snapshot(), e => e.Category == "Press" && e.Level == DiagnosticLevel.Warn);
    }

    [Fact]
    public async Task Press_LongPressTrue_SlotHasNoLongPress_RefusesAsNoLongPress()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);

        // Starter slot 1 (LandingGearToggle) has no long-press assigned.
        var response = await client.PostAsJsonAsync(ApiPaths.Press, new { page = 0, slot = 1, longPress = true }, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("fired").GetBoolean());
        Assert.Equal("NoLongPress", body.RootElement.GetProperty("outcome").GetString());
    }

    /// <summary>
    /// Every starter-layout action known (same set as
    /// <see cref="StarterActionsFullyBoundBindingsXml"/>, needed so
    /// <c>LayoutValidator.ValidateForSave</c> doesn't reject the OTHER 28
    /// slots' actions as unrecognized when this save happens to touch the
    /// whole starter layout) - but with <c>ToggleCargoScoop</c> specifically
    /// left unbound (empty key), "known, just not bound to anything," the
    /// exact case needed to prove a long-press degrades independently of
    /// its primary without ever making anything fireable (the "fully
    /// bound" fixture binds a real key to literally everything, which would
    /// make this scenario actually fireable instead).
    /// </summary>
    private const string StarterActionsBoundExceptToggleCargoScoopBindingsXml = """
        <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
            <LandingGearToggle><Primary Device="Keyboard" Key="Key_A" /><Secondary Device="{NoDevice}" Key="" /></LandingGearToggle>
            <ToggleCargoScoop><Primary Device="{NoDevice}" Key="" /><Secondary Device="{NoDevice}" Key="" /></ToggleCargoScoop>
            <NightVisionToggle><Primary Device="Keyboard" Key="Key_C" /><Secondary Device="{NoDevice}" Key="" /></NightVisionToggle>
            <ShipSpotLightToggle><Primary Device="Keyboard" Key="Key_D" /><Secondary Device="{NoDevice}" Key="" /></ShipSpotLightToggle>
            <DeployHeatSink><Primary Device="Keyboard" Key="Key_E" /><Secondary Device="{NoDevice}" Key="" /></DeployHeatSink>
            <ResetPowerDistribution><Primary Device="Keyboard" Key="Key_F" /><Secondary Device="{NoDevice}" Key="" /></ResetPowerDistribution>
            <IncreaseSystemsPower><Primary Device="Keyboard" Key="Key_G" /><Secondary Device="{NoDevice}" Key="" /></IncreaseSystemsPower>
            <IncreaseEnginesPower><Primary Device="Keyboard" Key="Key_H" /><Secondary Device="{NoDevice}" Key="" /></IncreaseEnginesPower>
            <IncreaseWeaponsPower><Primary Device="Keyboard" Key="Key_I" /><Secondary Device="{NoDevice}" Key="" /></IncreaseWeaponsPower>
            <Supercruise><Primary Device="Keyboard" Key="Key_J" /><Secondary Device="{NoDevice}" Key="" /></Supercruise>
            <Hyperspace><Primary Device="Keyboard" Key="Key_K" /><Secondary Device="{NoDevice}" Key="" /></Hyperspace>
            <FocusLeftPanel><Primary Device="Keyboard" Key="Key_L" /><Secondary Device="{NoDevice}" Key="" /></FocusLeftPanel>
            <FocusRightPanel><Primary Device="Keyboard" Key="Key_M" /><Secondary Device="{NoDevice}" Key="" /></FocusRightPanel>
            <FocusCommsPanel><Primary Device="Keyboard" Key="Key_N" /><Secondary Device="{NoDevice}" Key="" /></FocusCommsPanel>
            <FocusRadarPanel><Primary Device="Keyboard" Key="Key_O" /><Secondary Device="{NoDevice}" Key="" /></FocusRadarPanel>
            <CycleNextPanel><Primary Device="Keyboard" Key="Key_P" /><Secondary Device="{NoDevice}" Key="" /></CycleNextPanel>
            <CyclePreviousPanel><Primary Device="Keyboard" Key="Key_Q" /><Secondary Device="{NoDevice}" Key="" /></CyclePreviousPanel>
            <ExplorationFSSEnter><Primary Device="Keyboard" Key="Key_R" /><Secondary Device="{NoDevice}" Key="" /></ExplorationFSSEnter>
            <ExplorationFSSQuit><Primary Device="Keyboard" Key="Key_S" /><Secondary Device="{NoDevice}" Key="" /></ExplorationFSSQuit>
            <GalaxyMapOpen><Primary Device="Keyboard" Key="Key_T" /><Secondary Device="{NoDevice}" Key="" /></GalaxyMapOpen>
            <SystemMapOpen><Primary Device="Keyboard" Key="Key_U" /><Secondary Device="{NoDevice}" Key="" /></SystemMapOpen>
            <TargetNextRouteSystem><Primary Device="Keyboard" Key="Key_V" /><Secondary Device="{NoDevice}" Key="" /></TargetNextRouteSystem>
            <SetSpeed75><Primary Device="Keyboard" Key="Key_W" /><Secondary Device="{NoDevice}" Key="" /></SetSpeed75>
            <SelectTarget><Primary Device="Keyboard" Key="Key_X" /><Secondary Device="{NoDevice}" Key="" /></SelectTarget>
            <CycleNextTarget><Primary Device="Keyboard" Key="Key_Y" /><Secondary Device="{NoDevice}" Key="" /></CycleNextTarget>
            <RadarIncreaseRange><Primary Device="Keyboard" Key="Key_Z" /><Secondary Device="{NoDevice}" Key="" /></RadarIncreaseRange>
            <RadarDecreaseRange><Primary Device="Keyboard" Key="Key_0" /><Secondary Device="{NoDevice}" Key="" /></RadarDecreaseRange>
            <OrbitLinesToggle><Primary Device="Keyboard" Key="Key_1" /><Secondary Device="{NoDevice}" Key="" /></OrbitLinesToggle>
            <PlayerHUDModeToggle><Primary Device="Keyboard" Key="Key_2" /><Secondary Device="{NoDevice}" Key="" /></PlayerHUDModeToggle>
            <!-- [2026-09-16] The pip-management task's two new SHIP action
                 slots (DeployHardpointToggle, ToggleFlightAssist) - same
                 reasoning as every other block here. -->
            <DeployHardpointToggle><Primary Device="Keyboard" Key="Key_Comma" /><Secondary Device="{NoDevice}" Key="" /></DeployHardpointToggle>
            <ToggleFlightAssist><Primary Device="Keyboard" Key="Key_Period" /><Secondary Device="{NoDevice}" Key="" /></ToggleFlightAssist>
            <!-- [2026-09-16] The tablet SHIP page's grouped reorg
                 (expressive-kindling-starfish.md's Part D) added several
                 actions no phone-layout slot had ever named: page-cycling,
                 the four thrust hold-buttons, weapon fire/targeting,
                 WingNavLock, TriggerColonisationModule and the new Confirm
                 (UI_Select) button - same reasoning as every other block
                 here, this fixture must bind every action ANY shipped
                 starter layout (phone or tablet) names. -->
            <CycleNextPage><Primary Device="Keyboard" Key="Key_Semicolon" /><Secondary Device="{NoDevice}" Key="" /></CycleNextPage>
            <CyclePreviousPage><Primary Device="Keyboard" Key="Key_Slash" /><Secondary Device="{NoDevice}" Key="" /></CyclePreviousPage>
            <UpThrustButton><Primary Device="Keyboard" Key="Key_Equals" /><Secondary Device="{NoDevice}" Key="" /></UpThrustButton>
            <DownThrustButton><Primary Device="Keyboard" Key="Key_Minus" /><Secondary Device="{NoDevice}" Key="" /></DownThrustButton>
            <LeftThrustButton><Primary Device="Keyboard" Key="Key_LeftBracket" /><Secondary Device="{NoDevice}" Key="" /></LeftThrustButton>
            <RightThrustButton><Primary Device="Keyboard" Key="Key_RightBracket" /><Secondary Device="{NoDevice}" Key="" /></RightThrustButton>
            <PrimaryFire><Primary Device="Keyboard" Key="Key_Quote" /><Secondary Device="{NoDevice}" Key="" /></PrimaryFire>
            <SecondaryFire><Primary Device="Keyboard" Key="Key_Backslash" /><Secondary Device="{NoDevice}" Key="" /></SecondaryFire>
            <CycleNextHostileTarget><Primary Device="Keyboard" Key="Key_Tab" /><Secondary Device="{NoDevice}" Key="" /></CycleNextHostileTarget>
            <SelectHighestThreat><Primary Device="Keyboard" Key="Key_CapsLock" /><Secondary Device="{NoDevice}" Key="" /></SelectHighestThreat>
            <WingNavLock><Primary Device="Keyboard" Key="Key_Insert" /><Secondary Device="{NoDevice}" Key="" /></WingNavLock>
            <TriggerColonisationModule><Primary Device="Keyboard" Key="Key_Delete" /><Secondary Device="{NoDevice}" Key="" /></TriggerColonisationModule>
            <UI_Select><Primary Device="Keyboard" Key="Key_Space" /><Secondary Device="{NoDevice}" Key="" /></UI_Select>
            <!-- [2026-09-09] The starter layout's context pages (ref/docs/vessel-context.md's
                 "The default context pages"). Every action either page names has to be
                 recognized here too, or ValidateForSave rejects the whole save over a slot
                 this test never touches - the same reason the ship page's actions are listed
                 above. NightVisionToggle and FocusLeftPanel are shared with the ship page and
                 are already declared above; the SRV page's other seven and the on-foot page's
                 twelve are new. -->
            <AutoBreakBuggyButton><Primary Device="Keyboard" Key="Key_3" /><Secondary Device="{NoDevice}" Key="" /></AutoBreakBuggyButton>
            <ToggleDriveAssist><Primary Device="Keyboard" Key="Key_4" /><Secondary Device="{NoDevice}" Key="" /></ToggleDriveAssist>
            <ToggleBuggyTurretButton><Primary Device="Keyboard" Key="Key_5" /><Secondary Device="{NoDevice}" Key="" /></ToggleBuggyTurretButton>
            <HeadlightsBuggyButton><Primary Device="Keyboard" Key="Key_6" /><Secondary Device="{NoDevice}" Key="" /></HeadlightsBuggyButton>
            <BuggyCycleFireGroupNext><Primary Device="Keyboard" Key="Key_7" /><Secondary Device="{NoDevice}" Key="" /></BuggyCycleFireGroupNext>
            <ToggleCargoScoop_Buggy><Primary Device="Keyboard" Key="Key_8" /><Secondary Device="{NoDevice}" Key="" /></ToggleCargoScoop_Buggy>
            <RecallDismissShip><Primary Device="Keyboard" Key="Key_9" /><Secondary Device="{NoDevice}" Key="" /></RecallDismissShip>
            <HumanoidPrimaryInteractButton><Primary Device="Keyboard" Key="Key_F1" /><Secondary Device="{NoDevice}" Key="" /></HumanoidPrimaryInteractButton>
            <HumanoidSecondaryInteractButton><Primary Device="Keyboard" Key="Key_F2" /><Secondary Device="{NoDevice}" Key="" /></HumanoidSecondaryInteractButton>
            <HumanoidOpenAccessPanelButton><Primary Device="Keyboard" Key="Key_F3" /><Secondary Device="{NoDevice}" Key="" /></HumanoidOpenAccessPanelButton>
            <HumanoidToggleFlashlightButton><Primary Device="Keyboard" Key="Key_F4" /><Secondary Device="{NoDevice}" Key="" /></HumanoidToggleFlashlightButton>
            <HumanoidToggleNightVisionButton><Primary Device="Keyboard" Key="Key_F5" /><Secondary Device="{NoDevice}" Key="" /></HumanoidToggleNightVisionButton>
            <HumanoidToggleShieldsButton><Primary Device="Keyboard" Key="Key_F6" /><Secondary Device="{NoDevice}" Key="" /></HumanoidToggleShieldsButton>
            <HumanoidHealthPack><Primary Device="Keyboard" Key="Key_F7" /><Secondary Device="{NoDevice}" Key="" /></HumanoidHealthPack>
            <HumanoidBattery><Primary Device="Keyboard" Key="Key_F8" /><Secondary Device="{NoDevice}" Key="" /></HumanoidBattery>
            <HumanoidSwitchToRechargeTool><Primary Device="Keyboard" Key="Key_F9" /><Secondary Device="{NoDevice}" Key="" /></HumanoidSwitchToRechargeTool>
            <HumanoidSwitchToCompAnalyser><Primary Device="Keyboard" Key="Key_F10" /><Secondary Device="{NoDevice}" Key="" /></HumanoidSwitchToCompAnalyser>
            <HumanoidSwitchToSuitTool><Primary Device="Keyboard" Key="Key_F11" /><Secondary Device="{NoDevice}" Key="" /></HumanoidSwitchToSuitTool>
            <HumanoidHideWeaponButton><Primary Device="Keyboard" Key="Key_F12" /><Secondary Device="{NoDevice}" Key="" /></HumanoidHideWeaponButton>
            <!-- [2026-09-12] The SRV page's eight new panel-access buggy
                 variants (ref/docs/vessel-context.md's "The SRV page, and
                 why these eight") - same reasoning as the block above. -->
            <FocusLeftPanel_Buggy><Primary Device="Keyboard" Key="Key_F13" /><Secondary Device="{NoDevice}" Key="" /></FocusLeftPanel_Buggy>
            <FocusRightPanel_Buggy><Primary Device="Keyboard" Key="Key_F14" /><Secondary Device="{NoDevice}" Key="" /></FocusRightPanel_Buggy>
            <GalaxyMapOpen_Buggy><Primary Device="Keyboard" Key="Key_F15" /><Secondary Device="{NoDevice}" Key="" /></GalaxyMapOpen_Buggy>
            <SystemMapOpen_Buggy><Primary Device="Keyboard" Key="Key_F16" /><Secondary Device="{NoDevice}" Key="" /></SystemMapOpen_Buggy>
            <UIFocus_Buggy><Primary Device="Keyboard" Key="Key_F17" /><Secondary Device="{NoDevice}" Key="" /></UIFocus_Buggy>
            <PlayerHUDModeToggle_Buggy><Primary Device="Keyboard" Key="Key_F18" /><Secondary Device="{NoDevice}" Key="" /></PlayerHUDModeToggle_Buggy>
            <OpenCodexGoToDiscovery_Buggy><Primary Device="Keyboard" Key="Key_F19" /><Secondary Device="{NoDevice}" Key="" /></OpenCodexGoToDiscovery_Buggy>
            <PhotoCameraToggle_Buggy><Primary Device="Keyboard" Key="Key_F20" /><Secondary Device="{NoDevice}" Key="" /></PhotoCameraToggle_Buggy>
        </Root>
        """;

    [Fact]
    public async Task Press_LongPressTrue_UnboundLongPressAction_Refuses_PrimaryUnaffected()
    {
        await using var host = await RunningHost.StartAsync();
        var client = await PairedClientAsync(host);
        await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640"); // seed the starter

        Directory.CreateDirectory(host.BindingsDirectory);
        File.WriteAllText(Path.Combine(host.BindingsDirectory, "Custom.4.2.binds"), StarterActionsBoundExceptToggleCargoScoopBindingsXml);
        File.WriteAllText(Path.Combine(host.BindingsDirectory, "StartPreset.4.start"), "Custom\n");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(ApiPaths.BindingsRefresh, content: null)).StatusCode);

        // ToggleCargoScoop is a real, KNOWN element (just unbound), so this
        // save succeeds - unlike the default no-bindings-file environment,
        // where SetLongPress would fail validation for ANY action name.
        var lpAssignResponse = await client.PostAsJsonAsync(
            ApiPaths.SlotLongPress, new SlotEditEndpoint.LongPressRequest(0, 1, "ToggleCargoScoop", null));
        Assert.Equal(HttpStatusCode.OK, lpAssignResponse.StatusCode);

        // The primary (LandingGearToggle, bound) reads Ok via GET /api/panel
        // - proven WITHOUT ever calling POST /api/press on it, since that
        // would genuinely inject a keystroke through the real, production
        // Win32KeyInjector this host wires up (forbidden in this suite).
        var panelResponse = await client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var panel = await panelResponse.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);
        var slot1 = panel!.Slots.Single(s => s.Index == 1);
        Assert.Equal("Ok", slot1.Status);
        Assert.NotNull(slot1.LongPress);
        Assert.Equal("Unbound", slot1.LongPress!.Status);

        // The long-press itself safely refuses (Unbound never resolves a
        // chord, so this can never reach ChordPresser.PressAsync either).
        var lpPressResponse = await client.PostAsJsonAsync(ApiPaths.Press, new { page = 0, slot = 1, longPress = true }, JsonOptions);
        using var lpPressBody = JsonDocument.Parse(await lpPressResponse.Content.ReadAsStringAsync());
        Assert.False(lpPressBody.RootElement.GetProperty("fired").GetBoolean());
        Assert.Equal("NotUsable", lpPressBody.RootElement.GetProperty("outcome").GetString());
    }

    // -------------------------------------------------------------------
    // GET/POST /api/devices, /api/devices/forget and /api/devices/open-pairing
    // (ref/docs/pairing-and-devices.md). DevicesEndpointTests already covers
    // this endpoint's own handler logic in isolation; these are the real
    // Kestrel round trips proving the wiring (device auth on all three
    // routes, cookie revocation taking effect on the very next request, and
    // the window's closed-by-default behaviour surviving an actual restart
    // over a real persisted state file).
    // -------------------------------------------------------------------

    [Fact]
    public async Task Devices_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.GetAsync(ApiPaths.Devices);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DevicesOpenPairing_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PostAsync(ApiPaths.DevicesOpenPairing, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DevicesForget_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(ApiPaths.DevicesForget, new { deviceId = "ANYTHING" }, JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Devices_List_ReturnsThePairedDevice_WithSelfDeviceIdSet_NeverAToken()
    {
        await using var host = await RunningHost.StartAsync();
        var code = host.App.Services.GetRequiredService<DeviceRegistry>().CurrentCode;
        var pairResponse = await host.Client.PostAsJsonAsync(
            ApiPaths.Pair, new { code, deviceName = "Kitchen tablet", deviceClass = "tablet" }, JsonOptions);
        var paired = await pairResponse.Content.ReadFromJsonAsync<PairEndpoint.SuccessResponse>(JsonOptions);
        var token = ExtractTokenFromSetCookie(pairResponse);

        var response = await host.Client.GetAsync(ApiPaths.Devices);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Read the raw body BEFORE deserializing, so the token check runs
        // against the actual bytes on the wire rather than a re-serialized
        // approximation of them.
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(token, raw, StringComparison.Ordinal);

        var body = System.Text.Json.JsonSerializer.Deserialize<DevicesEndpoint.ListResponse>(raw, JsonOptions);
        Assert.Equal(paired!.DeviceId, body!.SelfDeviceId);
        var device = Assert.Single(body.Devices);
        Assert.Equal(paired.DeviceId, device.DeviceId);
        Assert.Equal("Kitchen tablet", device.Name);
        Assert.Equal("tablet", device.DeviceClass);
    }

    [Fact]
    public async Task Devices_ForgetAnotherDevice_RevokesItsCookie_OnTheVeryNextRequest_AndLeavesTheCallerAuthorised()
    {
        await using var host = await RunningHost.StartAsync();
        var codeA = host.App.Services.GetRequiredService<DeviceRegistry>().CurrentCode;
        var pairAResponse = await host.Client.PostAsJsonAsync(
            ApiPaths.Pair, new { code = codeA, deviceName = "A", deviceClass = "tablet" }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, pairAResponse.StatusCode);

        // A opens pairing for a second device - the authenticated "add
        // another device" route.
        var openResponse = await host.Client.PostAsync(ApiPaths.DevicesOpenPairing, null);
        Assert.Equal(HttpStatusCode.OK, openResponse.StatusCode);
        var openBody = await openResponse.Content.ReadFromJsonAsync<DevicesEndpoint.OpenPairingResponse>(JsonOptions);

        using var bHandler = new HttpClientHandler { UseCookies = false };
        using var bClient = new HttpClient(bHandler) { BaseAddress = host.Client.BaseAddress };
        var pairBRequest = new HttpRequestMessage(HttpMethod.Post, ApiPaths.Pair)
        {
            Content = JsonContent.Create(new { code = openBody!.Code, deviceName = "B", deviceClass = "phone" }, options: JsonOptions),
        };
        var pairBResponse = await bClient.SendAsync(pairBRequest);
        Assert.Equal(HttpStatusCode.OK, pairBResponse.StatusCode);
        var pairedB = await pairBResponse.Content.ReadFromJsonAsync<PairEndpoint.SuccessResponse>(JsonOptions);
        var tokenB = ExtractTokenFromSetCookie(pairBResponse);

        HttpRequestMessage BRequest(string path)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("Cookie", $"{DeviceCookieAuth.CookieName}={tokenB}");
            return request;
        }

        var beforeForget = await bClient.SendAsync(BRequest(ApiPaths.Diagnostics));
        Assert.Equal(HttpStatusCode.OK, beforeForget.StatusCode);

        var forgetResponse = await host.Client.PostAsJsonAsync(ApiPaths.DevicesForget, new { deviceId = pairedB!.DeviceId }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, forgetResponse.StatusCode);

        var afterForget = await bClient.SendAsync(BRequest(ApiPaths.Diagnostics));
        Assert.Equal(HttpStatusCode.Unauthorized, afterForget.StatusCode);

        // A itself, the device that issued the forget, is never the one
        // affected by it.
        var aStillWorks = await host.Client.GetAsync(ApiPaths.Diagnostics);
        Assert.Equal(HttpStatusCode.OK, aStillWorks.StatusCode);
    }

    /// <summary>
    /// The server-side half of "a device never forgets itself" driven
    /// through a real request, not just <c>DevicesEndpointTests</c>' direct
    /// call - proving <c>context.Items["DeviceId"]</c> (the caller's own id,
    /// set by the auth middleware) is what actually reaches the refusal,
    /// not a value the request body could have supplied on its own behalf.
    /// </summary>
    [Fact]
    public async Task Devices_ForgetOwnDeviceId_Returns400_AndItStaysAuthorised()
    {
        await using var host = await RunningHost.StartAsync();
        var code = host.App.Services.GetRequiredService<DeviceRegistry>().CurrentCode;
        var pairResponse = await host.Client.PostAsJsonAsync(
            ApiPaths.Pair, new { code, deviceName = "A", deviceClass = "tablet" }, JsonOptions);
        var paired = await pairResponse.Content.ReadFromJsonAsync<PairEndpoint.SuccessResponse>(JsonOptions);

        var forgetResponse = await host.Client.PostAsJsonAsync(ApiPaths.DevicesForget, new { deviceId = paired!.DeviceId }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, forgetResponse.StatusCode);
        var stillWorks = await host.Client.GetAsync(ApiPaths.Diagnostics);
        Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);
    }

    // -------------------------------------------------------------------
    // Editing a specific paired device's real layout live, from the PC
    // (?asDevice=<id> on the host listener - DeviceAuthMiddlewareExtensions).
    // DeviceAuthMiddlewareExtensionsTests already pins ResolveHostDeviceId's
    // own branches directly with no HTTP pipeline; these four drive the
    // wiring end to end - a real save landing on the NAMED device's own file
    // rather than the host's, the safety-critical refusal never falling back
    // silently, the security-critical guarantee that a cookie-authenticated
    // request cannot use this parameter to impersonate another device at
    // all, and the live channel actually being scoped by the resolved id.
    // -------------------------------------------------------------------

    /// <summary>
    /// The central claim: a slot edit made by the host session while
    /// <c>asDevice</c> names a real paired tablet lands on THAT tablet's own
    /// <c>layout-&lt;id&gt;.json</c>, never on <c>layout-host.json</c> - and
    /// the tablet's own, ordinary cookie-authenticated session sees the
    /// change on its very next request, proving the write reached the same
    /// file its own session reads from rather than merely one that happens
    /// to share its id.
    /// </summary>
    [Fact]
    public async Task AsDevice_HostRequest_ValidRealDevice_SavesToThatDevicesOwnLayoutFile_NeverTheHostsOwn()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        await UseFullyBoundStarterBindingsAsync(host, host.HostClient);

        var code = host.App.Services.GetRequiredService<DeviceRegistry>().CurrentCode;
        var pairResponse = await host.Client.PostAsJsonAsync(
            ApiPaths.Pair, new { code, deviceName = "Kitchen tablet", deviceClass = "tablet" }, JsonOptions);
        var deviceId = (await pairResponse.Content.ReadFromJsonAsync<PairEndpoint.SuccessResponse>(JsonOptions))!.DeviceId;

        // Seed the TABLET's own starter layout - the host session acting AS
        // it, never the host's own layout-host.json.
        Assert.Equal(
            HttpStatusCode.OK,
            (await host.HostClient.GetAsync($"{ApiPaths.Panel}?w=360&h=640&{DeviceAuthMiddlewareExtensions.AsDeviceQueryParam}={deviceId}")).StatusCode);

        var assignResponse = await host.HostClient.PostAsJsonAsync(
            $"{ApiPaths.SlotAssign}?{DeviceAuthMiddlewareExtensions.AsDeviceQueryParam}={deviceId}",
            new SlotEditEndpoint.AssignRequest(0, 0, "LandingGearToggle", null, null));
        Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);

        var layout = LunaPanelDirectories.Resolve(host.LocalAppData);
        var tabletLayoutText = File.ReadAllText(Path.Combine(layout.LayoutsDirectory, $"layout-{deviceId}.json"));
        Assert.Contains("LandingGearToggle", tabletLayoutText, StringComparison.Ordinal);

        var hostLayoutPath = Path.Combine(layout.LayoutsDirectory, $"layout-{HostRequest.HostDeviceId}.json");
        if (File.Exists(hostLayoutPath))
        {
            Assert.DoesNotContain("LandingGearToggle", File.ReadAllText(hostLayoutPath), StringComparison.Ordinal);
        }

        // The tablet's own ordinary, cookie-authenticated session (never
        // asDevice) sees the change on its very next GET.
        var panelAsTheTablet = await host.Client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        var panelBody = await panelAsTheTablet.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);
        var slot0 = panelBody!.Slots.Single(s => s.Index == 0);
        Assert.Equal("Ok", slot0.Status);
    }

    /// <summary>
    /// The safety-critical refusal, driven over real HTTP: a typo or an
    /// orphaned id must 400 rather than silently falling back to editing the
    /// host's own layout - proved by seeding the host's own layout first
    /// (with an action the refused write never uses) and confirming it is
    /// untouched afterwards, not merely by the response code alone.
    /// </summary>
    [Fact]
    public async Task AsDevice_HostRequest_UnknownDevice_Returns400_NeverFallsBackToEditingTheHostsOwnLayout()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        await UseFullyBoundStarterBindingsAsync(host, host.HostClient);

        Assert.Equal(HttpStatusCode.OK, (await host.HostClient.GetAsync($"{ApiPaths.Panel}?w=360&h=640")).StatusCode);

        var layout = LunaPanelDirectories.Resolve(host.LocalAppData);
        var hostLayoutPath = Path.Combine(layout.LayoutsDirectory, $"layout-{HostRequest.HostDeviceId}.json");
        // The starter layout's OWN slots already use LandingGearToggle
        // elsewhere (a bare Assert.DoesNotContain on that action name was
        // wrong - it is not the needle a fallback write would introduce).
        // Byte-for-byte equality of the whole file before and after the
        // refused call is what actually proves nothing was written.
        var beforeText = File.ReadAllText(hostLayoutPath);

        var response = await host.HostClient.PostAsJsonAsync(
            $"{ApiPaths.SlotAssign}?{DeviceAuthMiddlewareExtensions.AsDeviceQueryParam}=NOT-A-REAL-DEVICE",
            new SlotEditEndpoint.AssignRequest(0, 0, "LandingGearToggle", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var afterText = File.ReadAllText(hostLayoutPath);
        Assert.Equal(beforeText, afterText);
    }

    /// <summary>
    /// <b>The security-critical case named explicitly in this task's own
    /// brief.</b> A cookie-authenticated (real paired) device sending its own
    /// <c>asDevice=&lt;another real device&gt;</c> must not even be refused -
    /// the parameter is simply never read on that path
    /// (<see cref="DeviceAuthMiddlewareExtensions"/>'s own remarks) - so the
    /// request resolves to the COOKIE's own device every time, never the
    /// named one. Driven with two real, live, currently-paired devices (never
    /// a made-up id) so this cannot pass merely because the target named
    /// something that would have been refused anyway.
    /// </summary>
    [Fact]
    public async Task AsDevice_OnACookieAuthenticatedRequest_IsIgnoredEntirely_ResolvesToTheCookiesOwnDevice_NeverTheNamedOne()
    {
        await using var host = await RunningHost.StartAsync();
        var codeA = host.App.Services.GetRequiredService<DeviceRegistry>().CurrentCode;
        var pairAResponse = await host.Client.PostAsJsonAsync(
            ApiPaths.Pair, new { code = codeA, deviceName = "A", deviceClass = "tablet" }, JsonOptions);
        var deviceA = (await pairAResponse.Content.ReadFromJsonAsync<PairEndpoint.SuccessResponse>(JsonOptions))!.DeviceId;

        var openResponse = await host.Client.PostAsync(ApiPaths.DevicesOpenPairing, null);
        var openBody = await openResponse.Content.ReadFromJsonAsync<DevicesEndpoint.OpenPairingResponse>(JsonOptions);
        using var bHandler = new HttpClientHandler { UseCookies = false };
        using var bClient = new HttpClient(bHandler) { BaseAddress = host.Client.BaseAddress };
        var pairBRequest = new HttpRequestMessage(HttpMethod.Post, ApiPaths.Pair)
        {
            Content = JsonContent.Create(new { code = openBody!.Code, deviceName = "B", deviceClass = "tablet" }, options: JsonOptions),
        };
        var pairBResponse = await bClient.SendAsync(pairBRequest);
        var deviceB = (await pairBResponse.Content.ReadFromJsonAsync<PairEndpoint.SuccessResponse>(JsonOptions))!.DeviceId;

        var response = await host.Client.GetAsync($"{ApiPaths.Devices}?{DeviceAuthMiddlewareExtensions.AsDeviceQueryParam}={deviceB}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DevicesEndpoint.ListResponse>(JsonOptions);
        Assert.Equal(deviceA, body!.SelfDeviceId);
        Assert.NotEqual(deviceB, body.SelfDeviceId);
    }

    /// <summary>
    /// Item 2 of this task's brief: the live channel falls out of item 1
    /// automatically because it already reads <c>context.Items["DeviceId"]</c>
    /// like every other route - proved here rather than merely inspected. The
    /// host session opens the live channel AS the tablet; the tablet's own
    /// ordinary cookie session (never asDevice) then edits ITS layout, and
    /// the host's asDevice-scoped connection receives the push - proving the
    /// scoping is keyed off the resolved device id, not off whichever session
    /// happened to make either call.
    /// </summary>
    [Fact]
    public async Task PanelLive_AsDeviceScopesTheLiveChannel_PushesWhenTheNamedDevicesLayoutChanges()
    {
        await using var host = await RunningHost.StartAsync(withHostAccess: true);
        await UseFullyBoundStarterBindingsAsync(host, host.HostClient);

        var code = host.App.Services.GetRequiredService<DeviceRegistry>().CurrentCode;
        var pairResponse = await host.Client.PostAsJsonAsync(
            ApiPaths.Pair, new { code, deviceName = "Kitchen tablet", deviceClass = "tablet" }, JsonOptions);
        var deviceId = (await pairResponse.Content.ReadFromJsonAsync<PairEndpoint.SuccessResponse>(JsonOptions))!.DeviceId;

        // Seed the tablet's starter layout through its own cookie session.
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync($"{ApiPaths.Panel}?w=360&h=640")).StatusCode);

        // Gear down BEFORE the slot reassignment below, so LandingGearToggle
        // actually lights (Full) rather than landing on the very same "Off"
        // slot 0 already showed as its never-lit starter macro -
        // ShouldPush's own dedupe (PanelLiveEndpoint.ShouldPush) drops a push
        // whose lit levels are unchanged from the last one sent, which is
        // exactly what a gear-up reassignment would be here.
        host.App.Services.GetRequiredService<GameStateStore>().UpdateSnapshot(Running(LandingGearDownFlag));

        using var liveResponse = await host.HostClient.GetAsync(
            $"{ApiPaths.PanelLive}?{DeviceAuthMiddlewareExtensions.AsDeviceQueryParam}={deviceId}",
            HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        using var stream = await liveResponse.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var initial = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        Assert.True(initial.RootElement.TryGetProperty("slots", out _));

        // The tablet's OWN cookie session edits its OWN layout - never
        // through asDevice at all - so a push reaching the host's
        // asDevice-scoped channel proves the two are keyed off the same
        // device id rather than off which session made which call.
        Assert.Equal(
            HttpStatusCode.OK,
            (await host.Client.PostAsJsonAsync(
                ApiPaths.SlotAssign, new SlotEditEndpoint.AssignRequest(0, 0, "LandingGearToggle", null, null))).StatusCode);

        using var pushed = await ReadNextSseEventAsync(reader, TimeSpan.FromSeconds(5));
        var slot0 = pushed.RootElement.GetProperty("slots").EnumerateArray().Single(s => s.GetProperty("index").GetInt32() == 0);
        Assert.Equal("Full", slot0.GetProperty("lit").GetString());
    }

    /// <summary>
    /// The whole feature's central claim, driven end to end over a real
    /// restart rather than only at the <c>DeviceRegistry</c> unit level: a
    /// fresh process pointed at a state file that already has one device
    /// registered must NOT reopen pairing on its own, and the correct
    /// (still-persisted) code must be refused - closed by default really
    /// does survive a restart, not just a single process's lifetime.
    /// </summary>
    [Fact]
    public async Task Pair_SecondDevice_AfterARealRestart_WithoutOpeningTheWindow_Returns401_EvenWithTheCorrectCode()
    {
        var temp = TempDirectory.Create();
        try
        {
            var localAppData = temp.CreateSubdirectory("LocalAppData");
            var options = BuildOptions(temp, localAppData);

            var app1 = ServerHostBuilder.Build(Array.Empty<string>(), options);
            await app1.StartAsync();
            try
            {
                var registry1 = app1.Services.GetRequiredService<DeviceRegistry>();
                Assert.True(registry1.IsPairingWindowOpen);

                var address1 = app1.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                using var client1 = new HttpClient { BaseAddress = new Uri(address1) };
                var pairResponse = await client1.PostAsJsonAsync(
                    ApiPaths.Pair, new { code = registry1.CurrentCode, deviceName = "A", deviceClass = "tablet" }, JsonOptions);
                Assert.Equal(HttpStatusCode.OK, pairResponse.StatusCode);
            }
            finally
            {
                await app1.StopAsync();
                await app1.DisposeAsync();
            }

            // Restart against the exact same LocalAppData - the persisted
            // device-registry.json now has one device registered, so the
            // window must NOT auto-open this time.
            var app2 = ServerHostBuilder.Build(Array.Empty<string>(), options);
            await app2.StartAsync();
            try
            {
                var registry2 = app2.Services.GetRequiredService<DeviceRegistry>();
                Assert.False(registry2.IsPairingWindowOpen);
                var correctCode = registry2.CurrentCode;

                var address2 = app2.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                using var client2 = new HttpClient { BaseAddress = new Uri(address2) };
                var secondPairResponse = await client2.PostAsJsonAsync(
                    ApiPaths.Pair, new { code = correctCode, deviceName = "B", deviceClass = "phone" }, JsonOptions);

                Assert.Equal(HttpStatusCode.Unauthorized, secondPairResponse.StatusCode);
            }
            finally
            {
                await app2.StopAsync();
                await app2.DisposeAsync();
            }
        }
        finally
        {
            temp.Dispose();
        }
    }

    private static string ExtractTokenFromSetCookie(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var cookieHeader = Assert.Single(cookies!);
        var namePrefix = DeviceCookieAuth.CookieName + "=";
        var start = cookieHeader.IndexOf(namePrefix, StringComparison.Ordinal) + namePrefix.Length;
        var end = cookieHeader.IndexOf(';', start);
        return end < 0 ? cookieHeader[start..] : cookieHeader[start..end];
    }

    // -------------------------------------------------------------------
    // Device names, orphan markers and layout import, driven end to end
    // over a real Kestrel round trip (ref/docs/layout-import.md).
    //
    // The other classes cover each piece in isolation; these prove the
    // wiring - that the client's name and class survive the JSON boundary
    // into the registry, that the ordinal is assigned by the server that
    // can actually see the device list, that a forget through the real
    // route leaves a marker on the real filesystem, and that the import
    // routes copy a real layout file onto a real second device.
    // -------------------------------------------------------------------

    private sealed record PairOutcome(string DeviceId, string DeviceName, LayoutImportEndpoint.RecoveryResponse? Recovery);

    private static async Task<PairOutcome> PairAsync(RunningHost host, string? deviceName, string deviceClass)
    {
        var registry = host.App.Services.GetRequiredService<DeviceRegistry>();
        if (!registry.IsPairingWindowOpen)
        {
            registry.OpenPairingWindow();
        }

        var response = await host.Client.PostAsJsonAsync(
            ApiPaths.Pair, new { code = registry.CurrentCode, deviceName, deviceClass }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<PairEndpoint.SuccessResponse>(JsonOptions);
        Assert.True(body!.Paired);
        return new PairOutcome(body.DeviceId, body.DeviceName, body.Recovery);
    }

    /// <summary>
    /// The whole of Task A over the wire: a class the client reported and a
    /// name the server assigned both reach the registry, the ordinal is
    /// scoped per class, and a first-ever pairing carries <b>no recovery
    /// object at all</b> - arm 3 of the recovery flow, which has to be an
    /// absent field rather than an empty chooser.
    /// </summary>
    [Fact]
    public async Task Pair_NameAndClass_ReachTheRegistry_AndTheOrdinalIsAssignedServerSide()
    {
        await using var host = await RunningHost.StartAsync();

        var first = await PairAsync(host, deviceName: null, deviceClass: "phone");
        Assert.Equal("Phone", first.DeviceName);
        Assert.Null(first.Recovery);

        var second = await PairAsync(host, deviceName: null, deviceClass: "phone");
        Assert.Equal("Phone 2", second.DeviceName);

        var third = await PairAsync(host, deviceName: null, deviceClass: "tablet");
        Assert.Equal("Tablet", third.DeviceName);

        var typed = await PairAsync(host, deviceName: "  Bridge slate  ", deviceClass: "tablet");
        Assert.Equal("Bridge slate", typed.DeviceName);

        var devices = host.App.Services.GetRequiredService<DeviceRegistry>().ListDevices();
        Assert.Equal(new[] { "Phone", "Phone 2", "Tablet", "Bridge slate" }, devices.Select(d => d.Name));
        Assert.Equal(new[] { "phone", "phone", "tablet", "tablet" }, devices.Select(d => d.DeviceClass));
        Assert.DoesNotContain(devices, d => d.Name == "Unnamed device" || d.DeviceClass == "unknown");
    }

    // -------------------------------------------------------------------
    // Device-size-aware seeding at pairing (2026-09-16): StarterLayout's own
    // tests pin LoadForDeviceClass itself; these prove the pairing route
    // actually calls it with the pairing request's own device class, and
    // never when a layout was just recovered from an orphan.
    // -------------------------------------------------------------------

    /// <summary>
    /// Writes a real, fully-bound bindings file (and its start preset) into
    /// the host's bindings directory BEFORE the host is built, via
    /// <see cref="RunningHost.StartAsync"/>'s <c>beforeStart</c> hook - so
    /// <see cref="LiveBindingsReader"/> already knows every starter action at
    /// the very first request, including the pairing route's own proactive
    /// seed. Distinct from <see cref="UseFullyBoundStarterBindingsAsync"/>,
    /// which writes the same content but AFTER the host is already running
    /// (needed there because those tests pair first) - a device-size-aware
    /// seed happening at pairing itself needs the bindings to already exist
    /// at that moment, not moments later.
    /// </summary>
    private static void SeedFullyBoundBindingsBeforeStart(TempDirectory temp)
    {
        temp.CreateFile("NoBindings/Custom.4.2.binds", StarterActionsFullyBoundBindingsXml);
        temp.CreateFile("NoBindings/StartPreset.4.start", "Custom\n");
    }

    [Fact]
    public async Task Pair_FreshTabletPairing_SeedsTheTabletVariant()
    {
        await using var host = await RunningHost.StartAsync(beforeStart: SeedFullyBoundBindingsBeforeStart);

        var tablet = await PairAsync(host, deviceName: "Bridge slate", deviceClass: "tablet");

        var store = host.App.Services.GetRequiredService<LayoutStore>();
        var seeded = store.Load(tablet.DeviceId).Layout;
        Assert.NotNull(seeded);
        Assert.Equal("t64", seeded!.Pages[0].TemplateId);
    }

    [Fact]
    public async Task Pair_FreshPhonePairing_SeedsThePhoneVariant()
    {
        await using var host = await RunningHost.StartAsync(beforeStart: SeedFullyBoundBindingsBeforeStart);

        var phone = await PairAsync(host, deviceName: "Luna phone", deviceClass: "phone");

        var store = host.App.Services.GetRequiredService<LayoutStore>();
        var seeded = store.Load(phone.DeviceId).Layout;
        Assert.NotNull(seeded);
        Assert.Equal("t30", seeded!.Pages[0].TemplateId);
    }

    /// <summary>
    /// The safety rail the plan calls out explicitly: a device whose layout
    /// was just adopted from an orphan (<see cref="Pair_APhoneRePairingUnderItsOldName_AdoptsThatLayout_AndSaysSo"/>
    /// already proves the label survives end to end) must never have that
    /// adopted content immediately clobbered by a fresh seed call - checked
    /// here directly against the store, before any Panel GET could also
    /// incidentally re-seed it.
    /// </summary>
    [Fact]
    public async Task Pair_ARecoveredDevice_IsNeverOverwrittenByAFreshSeed()
    {
        await using var host = await RunningHost.StartAsync(beforeStart: SeedFullyBoundBindingsBeforeStart);

        var phone = await PairAsync(host, deviceName: "Luna phone", deviceClass: "phone");
        await host.Client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        await host.Client.PostAsJsonAsync(ApiPaths.SlotLabel, new SlotEditEndpoint.LabelRequest(0, 1, "MINE"));

        // A device cannot forget itself - a second device does the forgetting,
        // same as Pair_APhoneRePairingUnderItsOldName_AdoptsThatLayout_AndSaysSo.
        await PairAsync(host, deviceName: "Bridge slate", deviceClass: "tablet");
        await host.Client.PostAsJsonAsync(ApiPaths.DevicesForget, new { deviceId = phone.DeviceId }, JsonOptions);

        var rePaired = await PairAsync(host, deviceName: "Luna phone", deviceClass: "phone");
        Assert.NotNull(rePaired.Recovery?.Adopted);

        var store = host.App.Services.GetRequiredService<LayoutStore>();
        var afterPair = store.Load(rePaired.DeviceId).Layout!;
        Assert.Contains("MINE", afterPair.Pages.SelectMany(p => p.Slots).Select(s => s.Label ?? string.Empty));
    }

    [Fact]
    public async Task LayoutImport_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var list = await host.Client.GetAsync(ApiPaths.LayoutImport);
        var import = await host.Client.PostAsJsonAsync(ApiPaths.LayoutImport, new { deviceId = "whatever" }, JsonOptions);
        var undo = await host.Client.PostAsync(ApiPaths.LayoutImportUndo, content: null);
        var discard = await host.Client.PostAsJsonAsync(ApiPaths.LayoutImportDiscard, new { deviceId = "whatever" }, JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, import.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, undo.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, discard.StatusCode);
    }

    /// <summary>
    /// The whole loop over real HTTP, mirroring
    /// <see cref="LayoutImport_AForgottenDevicesLayout_IsOfferedByName_Imported_AndUndone"/>:
    /// a forgotten device's layout is listed, then permanently discarded, and
    /// afterward no longer appears in the list at all - not merely hidden,
    /// genuinely gone, since this route has no undo the way import does.
    /// </summary>
    [Fact]
    public async Task LayoutImportDiscard_AForgottenDevicesLayout_IsRemoved_AndNoLongerListed()
    {
        await using var host = await RunningHost.StartAsync();

        var phone = await PairAsync(host, deviceName: null, deviceClass: "phone");
        // A layout file only exists once a device has actually rendered a
        // panel - the same reason LayoutImport_AForgottenDevicesLayout_...
        // does this before forgetting the first device.
        await UseFullyBoundStarterBindingsAsync(host, host.Client);
        await host.Client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        // A second device, whose cookie the rest of this test authenticates
        // as - the same reason that other test pairs a second device before
        // forgetting the first.
        await PairAsync(host, deviceName: null, deviceClass: "phone");
        Assert.Equal(
            HttpStatusCode.OK,
            (await host.Client.PostAsJsonAsync(ApiPaths.DevicesForget, new { deviceId = phone.DeviceId }, JsonOptions)).StatusCode);

        var listed = await host.Client.GetFromJsonAsync<LayoutImportEndpoint.ListResponse>(ApiPaths.LayoutImport, JsonOptions);
        Assert.Equal(phone.DeviceId, Assert.Single(listed!.Candidates).DeviceId);

        Assert.Equal(
            HttpStatusCode.OK,
            (await host.Client.PostAsJsonAsync(ApiPaths.LayoutImportDiscard, new { deviceId = phone.DeviceId }, JsonOptions)).StatusCode);

        var afterDiscard = await host.Client.GetFromJsonAsync<LayoutImportEndpoint.ListResponse>(ApiPaths.LayoutImport, JsonOptions);
        Assert.Empty(afterDiscard!.Candidates);
    }

    /// <summary>
    /// The one access-control property this route has: a live-paired
    /// device's own layout can never be discarded through it, even by naming
    /// its id directly.
    /// </summary>
    [Fact]
    public async Task LayoutImportDiscard_ALiveDevicesOwnId_IsRefused()
    {
        await using var host = await RunningHost.StartAsync();
        var phone = await PairAsync(host, deviceName: null, deviceClass: "phone");

        var response = await host.Client.PostAsJsonAsync(ApiPaths.LayoutImportDiscard, new { deviceId = phone.DeviceId }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The whole loop, over real HTTP: a phone with its own arrangement is
    /// forgotten from another device, its layout is then offered by name and
    /// last-seen rather than as a line of hex, importing it copies it onto
    /// the second device, and the undo puts that device back.
    ///
    /// Every step goes through a real route: no test-only shortcut writes a
    /// layout file, a marker, or a device record.
    /// </summary>
    [Fact]
    public async Task LayoutImport_AForgottenDevicesLayout_IsOfferedByName_Imported_AndUndone()
    {
        await using var host = await RunningHost.StartAsync();

        var phone = await PairAsync(host, deviceName: null, deviceClass: "phone");
        Assert.Equal("Phone", phone.DeviceName);
        await UseFullyBoundStarterBindingsAsync(host, host.Client);
        await host.Client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        Assert.Equal(
            HttpStatusCode.OK,
            (await host.Client.PostAsJsonAsync(ApiPaths.SlotLabel, new SlotEditEndpoint.LabelRequest(0, 1, "MINE"))).StatusCode);

        // A second device. The first is still paired, so it owns its layout
        // and there is nothing to offer - no recovery object at all.
        var second = await PairAsync(host, deviceName: null, deviceClass: "phone");
        Assert.Equal("Phone 2", second.DeviceName);
        Assert.Null(second.Recovery);

        // Forgotten through the real route, by the device that is now
        // holding the cookie - never by itself.
        Assert.Equal(
            HttpStatusCode.OK,
            (await host.Client.PostAsJsonAsync(ApiPaths.DevicesForget, new { deviceId = phone.DeviceId }, JsonOptions)).StatusCode);

        var listed = await host.Client.GetFromJsonAsync<LayoutImportEndpoint.ListResponse>(ApiPaths.LayoutImport, JsonOptions);
        var candidate = Assert.Single(listed!.Candidates);
        Assert.Equal(phone.DeviceId, candidate.DeviceId);
        Assert.Equal("Phone", candidate.Name);
        Assert.Equal("phone", candidate.DeviceClass);
        Assert.NotNull(candidate.LastSeenAt);

        Assert.Equal(
            HttpStatusCode.OK,
            (await host.Client.PostAsJsonAsync(ApiPaths.LayoutImport, new { deviceId = phone.DeviceId }, JsonOptions)).StatusCode);
        Assert.Contains("MINE", await ReadPanelLabelsAsync(host, second.DeviceId));

        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsync(ApiPaths.LayoutImportUndo, content: null)).StatusCode);
        Assert.DoesNotContain("MINE", await ReadPanelLabelsAsync(host, second.DeviceId));

        // The source is untouched: adoption copies, never moves.
        var stillListed = await host.Client.GetFromJsonAsync<LayoutImportEndpoint.ListResponse>(ApiPaths.LayoutImport, JsonOptions);
        Assert.Equal(phone.DeviceId, Assert.Single(stillListed!.Candidates).DeviceId);
    }

    [Fact]
    public async Task LayoutReset_Unauthenticated_Returns401()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PostAsync(ApiPaths.LayoutReset, content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The route looks up the CALLING device's own stored class from the
    /// registry - never a hardcoded default - so a tablet resetting gets its
    /// own 64-slot SHIP page back, not the phone's 30-slot one.
    /// </summary>
    [Fact]
    public async Task LayoutReset_ForATabletDevice_RestoresTheTabletVariant_NotThePhoneOne()
    {
        await using var host = await RunningHost.StartAsync();
        var tablet = await PairAsync(host, deviceName: "Bridge slate", deviceClass: "tablet");
        await UseFullyBoundStarterBindingsAsync(host, host.Client);
        await host.Client.GetAsync($"{ApiPaths.Panel}?w=1024&h=768");
        await host.Client.PostAsJsonAsync(ApiPaths.SlotLabel, new SlotEditEndpoint.LabelRequest(0, 1, "CUSTOM"));

        var resetResponse = await host.Client.PostAsync(ApiPaths.LayoutReset, content: null);
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        var store = host.App.Services.GetRequiredService<LayoutStore>();
        var afterReset = store.Load(tablet.DeviceId).Layout!;
        Assert.Equal("t64", afterReset.Pages[0].TemplateId);
        Assert.DoesNotContain("CUSTOM", afterReset.Pages.SelectMany(p => p.Slots).Select(s => s.Label ?? string.Empty));
    }

    /// <summary>
    /// The whole feature over real HTTP (<c>ref/docs/reset-to-default.md</c>):
    /// a two-page custom arrangement is replaced by the starter's own on
    /// BOTH pages - not just whichever one the panel endpoint happens to
    /// render - a second, unrelated device's own layout and a saved theme
    /// override are both left exactly as they were, and undoing it is
    /// <see cref="ApiPaths.LayoutImportUndo"/> itself: reset never got a
    /// second undo route of its own.
    /// </summary>
    [Fact]
    public async Task LayoutReset_ReplacesEveryPage_LeavesOtherDeviceAndThemeOverrideAlone_AndUndoesViaTheExistingRoute()
    {
        await using var host = await RunningHost.StartAsync();

        // A second, unrelated device - the control "this device only" is
        // judged against.
        var other = await PairAsync(host, deviceName: "Bridge slate", deviceClass: "tablet");
        await UseFullyBoundStarterBindingsAsync(host, host.Client);
        await host.Client.GetAsync($"{ApiPaths.Panel}?w=1024&h=768");
        await host.Client.PostAsJsonAsync(ApiPaths.SlotLabel, new SlotEditEndpoint.LabelRequest(0, 1, "OTHERDEVICE"));

        // The device under test: its own custom label, a manual colour
        // override, and a whole second page that a visible-page-only bug
        // would leave standing.
        var phone = await PairAsync(host, deviceName: "Phone", deviceClass: "phone");
        await host.Client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        await host.Client.PostAsJsonAsync(ApiPaths.SlotLabel, new SlotEditEndpoint.LabelRequest(0, 1, "MINE"));
        Assert.Equal(
            HttpStatusCode.OK,
            (await host.Client.PostAsJsonAsync(ApiPaths.Theme, new { border = "#00ff00", text = "#0000ff", lit = "#ff00ff" }, JsonOptions)).StatusCode);

        var store = host.App.Services.GetRequiredService<LayoutStore>();
        var liveBindings = host.App.Services.GetRequiredService<LiveBindingsReader>();
        var knownActionNames = liveBindings.Read().Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

        var current = store.Load(phone.DeviceId).Layout!;
        var withExtraPage = new Layout(current.SchemaVersion, current.Pages.Concat(new[]
        {
            new LayoutPage("EXTRA", current.Pages[0].TemplateId, new[] { new LayoutSlot(0, "ToggleCargoScoop", null, "EXTRAPAGE", null) }, Array.Empty<LayoutSlot>()),
        }).ToList());
        Assert.Equal(LayoutSaveOutcome.Saved, store.Save(phone.DeviceId, withExtraPage, knownActionNames).Outcome);

        var resetResponse = await host.Client.PostAsync(ApiPaths.LayoutReset, content: null);
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        var afterReset = store.Load(phone.DeviceId).Layout!;
        Assert.Equal(StarterLayout.Load().Pages.Select(p => p.Name), afterReset.Pages.Select(p => p.Name));
        Assert.DoesNotContain(afterReset.Pages, p => p.Name == "EXTRA");
        Assert.DoesNotContain(
            afterReset.Pages.SelectMany(p => p.Slots).Select(s => s.Label ?? string.Empty),
            label => label is "MINE" or "EXTRAPAGE");

        // Another device's own arrangement is untouched.
        var otherAfter = store.Load(other.DeviceId).Layout!;
        Assert.Contains("OTHERDEVICE", otherAfter.Pages.SelectMany(p => p.Slots).Select(s => s.Label ?? string.Empty));

        // The theme override is untouched - reset is button placement only.
        var themeAfter = await host.Client.GetFromJsonAsync<ThemeEndpoint.StatusResponse>(ApiPaths.Theme, JsonOptions);
        Assert.True(themeAfter!.OverrideActive);
        Assert.Equal("#0000ff", themeAfter.Text, StringComparer.OrdinalIgnoreCase);

        // Undo is the SAME route the import flow already uses.
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsync(ApiPaths.LayoutImportUndo, content: null)).StatusCode);
        var undone = store.Load(phone.DeviceId).Layout!;
        Assert.Contains(undone.Pages, p => p.Name == "EXTRA");
        Assert.Contains("MINE", undone.Pages.SelectMany(p => p.Slots).Select(s => s.Label ?? string.Empty));
    }

    /// <summary>
    /// Arm 1 end to end - the case the user actually described: a phone
    /// re-pairs under the same name and its buttons are simply there. The
    /// adoption is reported on the pair response so the client can say so;
    /// it is never allowed to be silent.
    /// </summary>
    [Fact]
    public async Task Pair_APhoneRePairingUnderItsOldName_AdoptsThatLayout_AndSaysSo()
    {
        await using var host = await RunningHost.StartAsync();

        var phone = await PairAsync(host, deviceName: "Luna phone", deviceClass: "phone");
        await UseFullyBoundStarterBindingsAsync(host, host.Client);
        await host.Client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        await host.Client.PostAsJsonAsync(ApiPaths.SlotLabel, new SlotEditEndpoint.LabelRequest(0, 1, "MINE"));

        var other = await PairAsync(host, deviceName: "Bridge slate", deviceClass: "tablet");
        await host.Client.PostAsJsonAsync(ApiPaths.DevicesForget, new { deviceId = phone.DeviceId }, JsonOptions);

        var rePaired = await PairAsync(host, deviceName: "Luna phone", deviceClass: "phone");

        Assert.NotNull(rePaired.Recovery);
        Assert.NotNull(rePaired.Recovery!.Adopted);
        Assert.Equal(phone.DeviceId, rePaired.Recovery.Adopted!.DeviceId);
        Assert.Equal("Luna phone", rePaired.Recovery.Adopted.Name);
        Assert.Empty(rePaired.Recovery.Candidates);
        Assert.NotEqual(phone.DeviceId, rePaired.DeviceId);

        Assert.Contains("MINE", await ReadPanelLabelsAsync(host, rePaired.DeviceId));

        // And the undo the client offers alongside that announcement really
        // does return this device to the starter layout.
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsync(ApiPaths.LayoutImportUndo, content: null)).StatusCode);
        Assert.DoesNotContain("MINE", await ReadPanelLabelsAsync(host, rePaired.DeviceId));
        Assert.NotEqual(other.DeviceId, rePaired.DeviceId);
    }

    /// <summary>
    /// Arm 2 end to end: a name that matches nothing gets the list rather
    /// than an adoption, and nothing is written for it.
    /// </summary>
    [Fact]
    public async Task Pair_ADeviceWhoseNameMatchesNoOrphan_IsOfferedTheListWithoutAdopting()
    {
        await using var host = await RunningHost.StartAsync();

        var phone = await PairAsync(host, deviceName: "Luna phone", deviceClass: "phone");
        await UseFullyBoundStarterBindingsAsync(host, host.Client);
        await host.Client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        await host.Client.PostAsJsonAsync(ApiPaths.SlotLabel, new SlotEditEndpoint.LabelRequest(0, 1, "MINE"));

        await PairAsync(host, deviceName: "Bridge slate", deviceClass: "tablet");
        await host.Client.PostAsJsonAsync(ApiPaths.DevicesForget, new { deviceId = phone.DeviceId }, JsonOptions);

        var stranger = await PairAsync(host, deviceName: "Someone else's phone", deviceClass: "phone");

        Assert.NotNull(stranger.Recovery);
        Assert.Null(stranger.Recovery!.Adopted);
        Assert.Equal(phone.DeviceId, Assert.Single(stranger.Recovery.Candidates).DeviceId);
        Assert.DoesNotContain("MINE", await ReadPanelLabelsAsync(host, stranger.DeviceId));
    }

    /// <summary>
    /// Every user label currently rendered for the device holding the
    /// client's cookie - the only way to see, from outside, which layout a
    /// device actually ended up with.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReadPanelLabelsAsync(RunningHost host, string expectedDeviceId)
    {
        var response = await host.Client.GetAsync($"{ApiPaths.Panel}?w=360&h=640");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PanelEndpoint.PanelResponse>(JsonOptions);
        Assert.NotNull(body);

        // Guards against the whole assertion silently measuring the wrong
        // device if a later edit changes which cookie is in play.
        var registry = host.App.Services.GetRequiredService<DeviceRegistry>();
        Assert.Contains(registry.ListDevices(), d => d.DeviceId == expectedDeviceId);

        return body!.Slots.Select(slot => slot.Label ?? string.Empty).ToArray();
    }
}
