namespace LunaPanel.Core.Layouts;

/// <summary>
/// One layout on disk that no currently-paired device owns, described well
/// enough for a person to pick it off a list. See
/// <c>ref/docs/layout-import.md</c>.
/// </summary>
/// <param name="LastSeenAt">
/// <see langword="null"/> when no <see cref="OrphanMarker"/> exists for this
/// layout - a file left by a device forgotten before markers were written, or
/// one whose marker could not be read. The layout is still offered (it is
/// still the commander's own arrangement); only its description is thinner.
/// </param>
public sealed record LayoutImportCandidate(
    string DeviceId,
    string Name,
    string DeviceClass,
    DateTimeOffset? LastSeenAt);
