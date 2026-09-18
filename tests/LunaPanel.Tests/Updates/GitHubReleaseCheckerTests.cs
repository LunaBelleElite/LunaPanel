using System.Net;
using System.Text;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Server.Updates;

namespace LunaPanel.Tests.Updates;

/// <summary>
/// Drives <see cref="GitHubReleaseChecker"/> against a fake
/// <see cref="HttpMessageHandler"/> returning canned bodies.
///
/// <b>No test here touches the network.</b> That is a hard constraint of
/// this project (repo-root CLAUDE.md, and the agent briefs that come off
/// it): the suite must pass with no internet, no GitHub account, and no
/// rate-limit exposure. <see cref="CannedHandler"/> is the whole seam - the
/// checker takes an <see cref="HttpClient"/> rather than building one, so
/// production hands it a real one and this suite hands it one that answers
/// from memory.
/// </summary>
public class GitHubReleaseCheckerTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    /// <summary>
    /// Answers every request with one prepared response (or throws one
    /// prepared exception), and records the request it was asked to send so
    /// a test can assert on the URL and headers that would have gone out.
    /// </summary>
    private sealed class CannedHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly Exception? _throw;

        public CannedHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public CannedHandler(Exception toThrow)
        {
            _status = HttpStatusCode.OK;
            _body = string.Empty;
            _throw = toThrow;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;

            // A real handler observes the token before doing anything else;
            // a fake that ignores it would let a "cancelled" test pass
            // against a checker that never handles cancellation at all.
            cancellationToken.ThrowIfCancellationRequested();

            if (_throw is not null)
            {
                throw _throw;
            }

            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
        }
    }

    /// <summary>
    /// The shape GitHub's <c>/releases/latest</c> actually returns, reduced
    /// to the fields this checker reads plus enough neighbours that a parser
    /// keying on position rather than name would notice. Deliberately carries
    /// a non-.msi asset FIRST, so "picks the .msi" is a real choice here and
    /// not "picks assets[0]".
    /// </summary>
    private const string RealisticLatestReleaseJson = """
    {
      "url": "https://api.github.com/repos/LunaBelleElite/LunaPanel/releases/1",
      "tag_name": "ver-0.54.0.0",
      "name": "ver-0.54.0.0",
      "draft": false,
      "prerelease": false,
      "assets": [
        {
          "name": "source-notes.txt",
          "content_type": "text/plain",
          "browser_download_url": "https://github.com/LunaBelleElite/LunaPanel/releases/download/ver-0.54.0.0/source-notes.txt"
        },
        {
          "name": "LunaPanel-ver-0.54.0.0.msi",
          "content_type": "application/x-msi",
          "browser_download_url": "https://github.com/LunaBelleElite/LunaPanel/releases/download/ver-0.54.0.0/LunaPanel-ver-0.54.0.0.msi"
        }
      ]
    }
    """;

    private static (GitHubReleaseChecker Checker, CannedHandler Handler, CapturingDiagnosticLog Log) Build(CannedHandler handler)
    {
        var log = new CapturingDiagnosticLog();
        var client = new HttpClient(handler);
        return (new GitHubReleaseChecker(client, log), handler, log);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_NormalResponse_ReturnsTheTagAndTheMsiAssetUrl()
    {
        var (checker, _, _) = Build(new CannedHandler(HttpStatusCode.OK, RealisticLatestReleaseJson));

        var result = await checker.GetLatestReleaseAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("ver-0.54.0.0", result!.Version);
        Assert.Equal(
            "https://github.com/LunaBelleElite/LunaPanel/releases/download/ver-0.54.0.0/LunaPanel-ver-0.54.0.0.msi",
            result.DownloadUrl);
    }

    /// <summary>
    /// GitHub's API refuses an unidentified caller outright, so the
    /// User-Agent is not decoration - a checker that forgot it would fail
    /// live while every canned-response test stayed green.
    /// </summary>
    [Fact]
    public async Task GetLatestReleaseAsync_SendsAUserAgent_AndAsksTheRepositorysLatestRelease()
    {
        var (checker, handler, _) = Build(new CannedHandler(HttpStatusCode.OK, RealisticLatestReleaseJson));

        await checker.GetLatestReleaseAsync(CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal(
            "https://api.github.com/repos/LunaBelleElite/LunaPanel/releases/latest",
            handler.LastRequest.RequestUri!.ToString());
        Assert.True(
            handler.LastRequest.Headers.UserAgent.Count > 0
                || handler.LastRequest.Headers.Contains("User-Agent"),
            "GitHub's API rejects a request with no User-Agent, so one must be sent.");
    }

    /// <summary>
    /// No Authorization header, ever. This reads a public repo
    /// unauthenticated by design; a token appearing here would be a real
    /// change of posture, not a tidy-up.
    /// </summary>
    [Fact]
    public async Task GetLatestReleaseAsync_SendsNoAuthorizationHeader()
    {
        var (checker, handler, _) = Build(new CannedHandler(HttpStatusCode.OK, RealisticLatestReleaseJson));

        await checker.GetLatestReleaseAsync(CancellationToken.None);

        Assert.Null(handler.LastRequest!.Headers.Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetLatestReleaseAsync_ErrorStatus_IsNull_AndLogged(HttpStatusCode status)
    {
        var (checker, _, log) = Build(new CannedHandler(status, """{ "message": "Not Found" }"""));

        var result = await checker.GetLatestReleaseAsync(CancellationToken.None);

        Assert.Null(result);
        // The status code itself has to reach the log. Asserting only "null,
        // and something was logged" would stay green with the status check
        // deleted outright - an error body simply fails to parse as a
        // release and logs that instead, which looks identical from here.
        Assert.Contains(
            log.Events,
            e => (e.Level == DiagnosticLevel.Warn || e.Level == DiagnosticLevel.Error)
                && e.Detail is not null
                && e.Detail.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetLatestReleaseAsync_MalformedJson_IsNull_AndLogged()
    {
        var (checker, _, log) = Build(new CannedHandler(HttpStatusCode.OK, "{ this is not json"));

        var result = await checker.GetLatestReleaseAsync(CancellationToken.None);

        Assert.Null(result);
        Assert.Contains(log.Events, e => e.Level == DiagnosticLevel.Warn || e.Level == DiagnosticLevel.Error);
    }

    /// <summary>
    /// Well-formed JSON that simply is not a release - the response shape
    /// changing under us reads the same as a failure, never as a release
    /// called "" hosted at "".
    /// </summary>
    [Theory]
    [InlineData("""{ "assets": [ { "name": "x.msi", "browser_download_url": "https://example/x.msi" } ] }""")]
    [InlineData("""{ "tag_name": "ver-0.54.0.0" }""")]
    [InlineData("""{ "tag_name": "ver-0.54.0.0", "assets": [] }""")]
    [InlineData("""{ "tag_name": "ver-0.54.0.0", "assets": [ { "name": "notes.txt", "browser_download_url": "https://example/notes.txt" } ] }""")]
    [InlineData("""{ "tag_name": "", "assets": [ { "name": "x.msi", "browser_download_url": "https://example/x.msi" } ] }""")]
    [InlineData("""{ "tag_name": "ver-0.54.0.0", "assets": [ { "name": "x.msi" } ] }""")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task GetLatestReleaseAsync_WellFormedButNotAReleaseWithAnMsi_IsNull(string body)
    {
        var (checker, _, _) = Build(new CannedHandler(HttpStatusCode.OK, body));

        var result = await checker.GetLatestReleaseAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_NetworkFailure_IsNull_AndLogged()
    {
        var (checker, _, log) = Build(new CannedHandler(new HttpRequestException("No such host is known.")));

        var result = await checker.GetLatestReleaseAsync(CancellationToken.None);

        Assert.Null(result);
        Assert.Contains(log.Events, e => e.Level == DiagnosticLevel.Warn || e.Level == DiagnosticLevel.Error);
    }

    /// <summary>
    /// A timeout surfaces out of <see cref="HttpClient"/> as
    /// <see cref="TaskCanceledException"/>, which is the one failure mode
    /// easiest to leave uncaught - it is not an
    /// <see cref="HttpRequestException"/>.
    /// </summary>
    [Fact]
    public async Task GetLatestReleaseAsync_Timeout_IsNull_AndLogged()
    {
        var (checker, _, log) = Build(new CannedHandler(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.")));

        var result = await checker.GetLatestReleaseAsync(CancellationToken.None);

        Assert.Null(result);
        Assert.Contains(log.Events, e => e.Level == DiagnosticLevel.Warn || e.Level == DiagnosticLevel.Error);
    }

    /// <summary>
    /// A cancelled token is the caller's own doing, not a fault - but it
    /// still has to come back as null rather than escaping as an exception
    /// into a WinForms click handler.
    /// </summary>
    [Fact]
    public async Task GetLatestReleaseAsync_AlreadyCancelledToken_IsNull_AndDoesNotThrow()
    {
        var (checker, _, _) = Build(new CannedHandler(HttpStatusCode.OK, RealisticLatestReleaseJson));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await checker.GetLatestReleaseAsync(cts.Token);

        Assert.Null(result);
    }

    /// <summary>
    /// The real production entry point, built with no injected client -
    /// pinned because the only thing that ever calls it is the tray, which
    /// this suite cannot reference. Constructing it must not itself touch
    /// the network (it does not: no request is sent until
    /// <c>GetLatestReleaseAsync</c> is called), so this stays a safe test.
    /// </summary>
    [Fact]
    public void Create_BuildsACheckerWithoutTouchingTheNetwork()
    {
        var checker = GitHubReleaseChecker.Create(new CapturingDiagnosticLog());

        Assert.NotNull(checker);
    }
}
