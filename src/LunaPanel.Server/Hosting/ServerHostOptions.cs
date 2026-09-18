using System.Net;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Server.Discovery;
using LunaPanel.Server.Input;

namespace LunaPanel.Server.Hosting;

/// <summary>
/// Every environment-dependent input <see cref="ServerHostBuilder"/> needs to
/// build the app, bundled so a test can substitute a fully synthetic
/// environment (a temp-directory-backed <see cref="PathDiscoveryEnvironment"/>,
/// a loopback bind address, a redactor over synthetic roots) for all of it at
/// once - the same shape of split <c>ref/docs/discovery.md</c> describes for
/// <c>PathDiscoveryEnvironment</c> itself. Production builds this from real,
/// already-resolved values (see <see cref="RealServerEnvironment"/>); nothing
/// under <see cref="ServerHostBuilder"/> discovers anything itself.
/// </summary>
/// <param name="BindCandidates">
/// The full candidate list <see cref="LanAddressResolver.Resolve"/> chose
/// <paramref name="BindAddress"/> from, carried through only so
/// <see cref="ServerHostBuilder"/> can log all of them and name the one
/// chosen (O12) - empty by default, since most tests construct
/// <see cref="BindAddress"/> directly without going through
/// <see cref="LanAddressResolver"/> at all.
/// </param>
/// <param name="DiagnosticModeEnabled">
/// Whether a successful key injection logs an <c>Info</c> line for every
/// send, not just every refusal - passed straight through to
/// <see cref="Input.Win32KeyInjector"/>'s <c>diagnosticModeEnabled</c>
/// delegate. Off by default: a refusal always logs regardless of this flag
/// (see <c>ref/docs/injection.md</c>), so the "explains a dead button"
/// guarantee holds either way: this only controls the noisier
/// every-successful-press logging.
/// </param>
/// <param name="HostAccessPort">
/// The port of the second, <b>loopback-only</b> listener - the one a request
/// has to arrive on to count as coming from this machine
/// (<see cref="Http.HostRequest"/>, <c>ref/docs/hosting.md</c>). Zero means
/// no such listener is bound at all and nothing is ever treated as the host,
/// which is the deny direction and is what every test that does not
/// deliberately ask for one gets. Production sets it from
/// <see cref="RealServerEnvironment"/>.
/// </param>
/// <param name="KeyInjectorOverride">
/// Substitutes the <see cref="Win32KeyInjector"/> <see cref="Hosting.ServerHostBuilder.Build"/>
/// would otherwise construct itself. <c>null</c> (the default, and what
/// every production caller leaves it as) means "build the real one" -
/// <see cref="Win32KeyInjector"/>'s single-arg constructor, wired to the
/// real Win32 foreground inspector and the real <c>SendInput</c> call. A
/// non-null value lets a test supply an instance built via
/// <see cref="Win32KeyInjector"/>'s own test-seam constructor instead, with
/// no Win32 API reachable through it at all - see
/// <c>tests/LunaPanel.Tests/Injection/</c> for that constructor's fakes. This
/// exists because <see cref="Hosting.ServerHostBuilder.Build"/> previously
/// had no way to avoid wiring the real injector, which made every real-host
/// test in <c>ServerHostBuilderTests</c> silently dependent on whatever
/// window actually has focus on the machine running the suite - up to and
/// including a real keystroke reaching a real, running copy of Elite
/// Dangerous during an automated test run.
/// </param>
public sealed record ServerHostOptions(
    IPAddress BindAddress,
    int Port,
    PathDiscoveryEnvironment DiscoveryEnvironment,
    PathRedactor Redactor,
    TimeProvider Clock,
    int LogRetentionDays = 7,
    int DiagnosticsRingBufferCapacity = 500,
    IReadOnlyList<LanCandidate>? BindCandidates = null,
    bool DiagnosticModeEnabled = false,
    int HostAccessPort = 0,
    Win32KeyInjector? KeyInjectorOverride = null);
