namespace LunaPanel.Core.Layouts;

/// <summary>
/// The two pure decisions layout recovery rests on
/// (<c>ref/docs/layout-import.md</c>): which layouts on disk are orphans, and
/// whether a freshly-paired device may adopt one without being asked.
///
/// Both are separated from every filesystem and HTTP concern on purpose -
/// "recover at pairing" and "import from settings" are the same operation
/// seen from two places, and the way to keep them the same operation is for
/// the deciding to happen once, here.
/// </summary>
public static class LayoutImport
{
    /// <summary>
    /// Every layout file that no currently-paired device owns, most recently
    /// used first, described from its <see cref="OrphanMarker"/> where one
    /// exists.
    ///
    /// <b>The layout files are the authority, not the markers.</b> A marker
    /// whose layout is gone describes nothing importable and is dropped; a
    /// layout with no marker is still offered, under a synthesised name, on
    /// the grounds that it is still the commander's own arrangement - the
    /// layouts left behind before markers existed are exactly the ones this
    /// feature was asked for.
    ///
    /// A device id that is live wins over any marker naming it: a device
    /// forgotten and later re-paired leaves a marker behind, and offering a
    /// commander their own current layout as if it were abandoned is the one
    /// outcome here that would read as a bug.
    /// </summary>
    public static IReadOnlyList<LayoutImportCandidate> BuildCandidates(
        IReadOnlyList<string> layoutDeviceIds,
        IReadOnlySet<string> liveDeviceIds,
        IReadOnlyList<OrphanMarker> markers)
    {
        ArgumentNullException.ThrowIfNull(layoutDeviceIds);
        ArgumentNullException.ThrowIfNull(liveDeviceIds);
        ArgumentNullException.ThrowIfNull(markers);

        var markersById = new Dictionary<string, OrphanMarker>(StringComparer.Ordinal);
        foreach (var marker in markers)
        {
            markersById[marker.DeviceId] = marker;
        }

        var candidates = new List<LayoutImportCandidate>();

        foreach (var deviceId in layoutDeviceIds)
        {
            if (liveDeviceIds.Contains(deviceId))
            {
                continue;
            }

            candidates.Add(markersById.TryGetValue(deviceId, out var marker)
                ? new LayoutImportCandidate(deviceId, marker.Name, marker.DeviceClass, marker.LastSeenAt)
                : new LayoutImportCandidate(deviceId, UndescribedName(deviceId), "unknown", null));
        }

        return candidates
            .OrderByDescending(c => c.LastSeenAt.HasValue)
            .ThenByDescending(c => c.LastSeenAt ?? DateTimeOffset.MinValue)
            .ThenBy(c => c.DeviceId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// What an orphan with no marker is called. Carries the device id so two
    /// of them are still distinguishable from each other, and says plainly
    /// that nothing is known about it rather than inventing a class or a
    /// date.
    /// </summary>
    private static string UndescribedName(string deviceId) => $"Unknown device {deviceId}";

    /// <summary>
    /// The candidate a freshly-paired device may adopt without being asked,
    /// or <see langword="null"/> when it must ask instead.
    ///
    /// Exactly one candidate whose name matches
    /// <paramref name="deviceName"/> - not "exactly one candidate, which
    /// happens to match" (coordinator's reading, 2026-09-08). Several
    /// orphans is normal in a household; what makes an automatic adoption
    /// safe is that precisely one of them answers to the name this device
    /// just gave.
    ///
    /// Zero matches and two-or-more matches both return
    /// <see langword="null"/>, and both mean the same thing to the caller:
    /// present the list. An <b>undescribed</b> orphan is never eligible at
    /// all - its name was synthesised here, and the entire justification for
    /// adopting without asking is that a person chose that name.
    ///
    /// Whole-name comparison, ordinal and case-insensitive, after trimming.
    /// A prefix or contains comparison would let "Phone" adopt "Phone 2" -
    /// which is precisely the household this feature exists for.
    /// </summary>
    public static LayoutImportCandidate? ChooseAutoAdopt(
        IReadOnlyList<LayoutImportCandidate> candidates,
        string deviceName)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(deviceName);

        var wanted = deviceName.Trim();
        if (wanted.Length == 0)
        {
            return null;
        }

        LayoutImportCandidate? match = null;

        foreach (var candidate in candidates)
        {
            if (!candidate.LastSeenAt.HasValue)
            {
                continue;
            }

            if (!string.Equals(candidate.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (match is not null)
            {
                // Ambiguous: two orphans answer to this name. Guessing is
                // worse than asking.
                return null;
            }

            match = candidate;
        }

        return match;
    }
}
