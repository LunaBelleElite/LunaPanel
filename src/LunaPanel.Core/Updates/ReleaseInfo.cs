namespace LunaPanel.Core.Updates;

/// <summary>
/// One published release, reduced to the only two things an update check
/// needs: the version string as the release itself names it (a
/// <c>ver-A.B.C.D</c> tag, per this project's versioning scheme) and the
/// direct URL of the <c>.msi</c> asset hanging off it.
///
/// Nothing else off a release is carried - no notes, no published date, no
/// asset size. Every field here has to be produced by whatever
/// <see cref="IReleaseChecker"/> implementation is in play, so the record
/// stays the smallest thing that can drive "is there something newer, and
/// where do I get it".
/// </summary>
public sealed record ReleaseInfo(string Version, string DownloadUrl);
