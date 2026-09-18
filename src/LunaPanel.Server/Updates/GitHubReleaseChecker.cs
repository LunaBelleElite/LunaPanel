using System.Net.Http.Headers;
using System.Text.Json;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Updates;

namespace LunaPanel.Server.Updates;

/// <summary>
/// The real <see cref="IReleaseChecker"/>: asks the public LunaPanel
/// repository's GitHub API for its latest release and picks the <c>.msi</c>
/// asset off it.
///
/// <b>Lives in LunaPanel.Server, not Core</b>, for the same reason
/// <c>Win32KeyInjector</c> does - the real thing needs a dependency Core
/// deliberately stays free of (an outbound network call here, Win32 there).
/// Core depends only on <see cref="IReleaseChecker"/>.
///
/// <b>Unauthenticated, deliberately.</b> The repository is public and this
/// runs on a commander's click, not on a timer, so an unauthenticated read
/// sits well inside GitHub's rate limit and there is no token to ship,
/// store, or leak. A rate-limited response degrades to the same "could not
/// check" as every other failure.
///
/// <b>Never throws.</b> Every failure - DNS, TLS, an error status, a body
/// that will not parse, a timeout, a cancelled token, a response shape that
/// has changed - comes back as null and is logged. The only caller is a
/// WinForms click handler, where an escaping exception is a crash.
/// </summary>
public sealed class GitHubReleaseChecker : IReleaseChecker
{
    private const string LogCategory = "Updates";

    /// <summary>
    /// The public repository's latest release. Fixed rather than configurable
    /// on purpose: "where do updates come from" is not a per-machine setting,
    /// and making it one would be a way to point a silent installer at an
    /// arbitrary MSI.
    /// </summary>
    public const string LatestReleaseUrl = "https://api.github.com/repos/LunaBelleElite/LunaPanel/releases/latest";

    /// <summary>
    /// GitHub's API refuses a request that does not identify its caller, so
    /// this is required rather than courtesy.
    /// </summary>
    private const string UserAgent = "LunaPanel-UpdateChecker";

    /// <summary>
    /// A click is waiting on this. Long enough to survive a slow link, short
    /// enough that "couldn't check" arrives while the commander still
    /// remembers clicking.
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _httpClient;
    private readonly IDiagnosticLog _log;

    /// <summary>
    /// Takes its <see cref="HttpClient"/> rather than building one - the
    /// single seam that lets the whole suite drive this against canned
    /// responses with no network at all (a hard constraint of this project).
    /// </summary>
    public GitHubReleaseChecker(HttpClient httpClient, IDiagnosticLog log)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// The production shape: a real <see cref="HttpClient"/> with this
    /// checker's own timeout and User-Agent already set. Sends nothing - no
    /// request happens until <see cref="GetLatestReleaseAsync"/> is called.
    /// </summary>
    public static GitHubReleaseChecker Create(IDiagnosticLog log)
    {
        var client = new HttpClient { Timeout = RequestTimeout };
        return new GitHubReleaseChecker(client, log);
    }

    public async Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            // Set per request rather than on the client's DefaultRequestHeaders:
            // the client may be one this class did not create (see the
            // constructor), and a missing User-Agent is a live-only failure
            // no canned-response test would ever catch.
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, null));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // Not EnsureSuccessStatusCode: a 404 (no release published
                // yet) and a 403 (rate limited) are both ordinary answers
                // here, not faults worth a stack trace in the log.
                _log.Warn(
                    LogCategory,
                    "Could not check for updates; the release API answered with an error",
                    $"status={(int)response.StatusCode} {response.StatusCode}");
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseLatestRelease(body);
        }
        catch (HttpRequestException ex)
        {
            _log.Warn(LogCategory, "Could not reach the release API to check for updates", $"{ex.GetType().Name}: {ex.Message}");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            // Both a timeout and a caller-cancelled token land here. Neither
            // is worth distinguishing: both mean "we did not find out".
            _log.Warn(LogCategory, "The update check did not finish in time", ex.Message);
            return null;
        }
        catch (OperationCanceledException ex)
        {
            _log.Warn(LogCategory, "The update check was cancelled", ex.Message);
            return null;
        }
        catch (JsonException ex)
        {
            _log.Warn(LogCategory, "The release API's answer could not be read", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Reads <c>tag_name</c> and the <c>.msi</c> asset's
    /// <c>browser_download_url</c> out of a latest-release response.
    ///
    /// The asset is found by NAME, not by position: a release can carry
    /// whatever else it likes alongside the installer, and "assets[0]" would
    /// silently start handing back something that is not an MSI the first
    /// time one did.
    ///
    /// Anything missing, blank, or not an object returns null. A release this
    /// cannot read is indistinguishable from no release, which is the safe
    /// reading - never a <see cref="ReleaseInfo"/> with empty fields that a
    /// caller would then try to download.
    /// </summary>
    private ReleaseInfo? ParseLatestRelease(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            _log.Warn(LogCategory, "The release API's answer was not a release", $"kind={root.ValueKind}");
            return null;
        }

        if (!root.TryGetProperty("tag_name", out var tagElement)
            || tagElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(tagElement.GetString()))
        {
            _log.Warn(LogCategory, "The latest release carried no usable tag name", null);
            return null;
        }

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            _log.Warn(LogCategory, "The latest release carried no assets", null);
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!asset.TryGetProperty("name", out var nameElement)
                || nameElement.ValueKind != JsonValueKind.String
                || nameElement.GetString() is not { } name
                || !name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!asset.TryGetProperty("browser_download_url", out var urlElement)
                || urlElement.ValueKind != JsonValueKind.String
                || urlElement.GetString() is not { } url
                || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            return new ReleaseInfo(tagElement.GetString()!, url);
        }

        _log.Warn(LogCategory, "The latest release carried no downloadable .msi asset", null);
        return null;
    }
}
