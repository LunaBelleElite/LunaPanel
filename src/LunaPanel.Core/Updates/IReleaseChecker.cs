namespace LunaPanel.Core.Updates;

/// <summary>
/// Asks wherever releases are published what the latest one is.
///
/// <b>No implementation of this interface lives in LunaPanel.Core</b> - the
/// real one needs an outbound HTTP call, which is <c>LunaPanel.Server</c>'s
/// job (<c>LunaPanel.Server.Updates.GitHubReleaseChecker</c>), exactly the
/// split <see cref="LunaPanel.Core.Macros.IKeyInjector"/> already makes for
/// Win32 key injection: Core depends only on the interface, so it stays
/// testable with no network and no Windows.
///
/// <b>Failure is null, never an exception.</b> Network down, rate-limited,
/// no release published yet, a response shape that has changed - all of them
/// are "we could not find out", which is a clean first-class result here the
/// same way "not found" is for every Discovery component. A caller therefore
/// never needs a try/catch around this.
/// </summary>
public interface IReleaseChecker
{
    Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken);
}
