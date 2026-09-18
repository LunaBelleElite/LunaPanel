using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Pairing;
using LunaPanel.Server.Layouts;

namespace LunaPanel.Server.Http;

/// <summary>
/// <c>GET /api/layout/import</c>, <c>POST /api/layout/import</c> and
/// <c>POST /api/layout/import/undo</c>'s handler logic, kept free of any
/// ASP.NET type - same discipline as <see cref="PairEndpoint"/> and
/// <see cref="DevicesEndpoint"/>.
///
/// <b>Recovery at pairing and import from settings are one operation seen
/// from two places</b> (<c>ref/docs/layout-import.md</c>): copy a layout that
/// no live device owns onto this device. <see cref="Recover"/> is the pairing
/// entry point and <see cref="Import"/> the settings one, and both go through
/// the same <see cref="Candidates"/> list and the same
/// <see cref="LayoutStore.Save"/> - never a second way to write a layout file.
///
/// <b>Adoption copies; it never moves.</b> The source layout is left exactly
/// where it is, so adopting the wrong one costs nothing and one layout can
/// seed several devices - which is what makes "recover my phone's buttons"
/// and "import that arrangement I like" the same feature rather than two.
///
/// <b>Only an orphan may be imported.</b> A device id that belongs to a
/// currently-paired device is refused
/// (<see cref="ImportOutcome.NotAnOrphan"/>) rather than copied, so this
/// route can never be used by one paired device to read another's current
/// arrangement.
/// </summary>
public static class LayoutImportEndpoint
{
    private const string LogCategory = "Layout";

    public sealed record CandidateResponse(string DeviceId, string Name, string DeviceClass, DateTimeOffset? LastSeenAt);

    public sealed record ListResponse(IReadOnlyList<CandidateResponse> Candidates);

    /// <summary>
    /// What a freshly-paired device is told about layouts left behind.
    ///
    /// <see langword="null"/> - not an empty instance of this - is what
    /// "no orphans" looks like on the wire, because the third rule of the
    /// recovery flow is that a first-ever pairing must not be interrupted by
    /// an empty chooser. A client that receives no recovery object has
    /// nothing to draw and no decision to make.
    /// </summary>
    /// <param name="Adopted">
    /// The orphan adopted automatically, when exactly one answered to the
    /// name this device just gave. Present so the client can <em>say so on
    /// screen</em>: automatic is fine, invisible is not.
    /// </param>
    /// <param name="Candidates">
    /// Everything on offer when nothing was adopted automatically. Empty
    /// whenever <paramref name="Adopted"/> is set - the choice has been made.
    /// </param>
    public sealed record RecoveryResponse(CandidateResponse? Adopted, IReadOnlyList<CandidateResponse> Candidates);

    public sealed record ImportRequest(string? DeviceId);

    public enum ImportOutcome
    {
        Imported,

        /// <summary>No source device id was supplied at all.</summary>
        MissingSource,

        /// <summary>
        /// The named device is not on the orphan list - either it has no
        /// layout, or it belongs to a device that is still paired.
        /// </summary>
        NotAnOrphan,

        /// <summary>Its layout file exists but could not be read.</summary>
        SourceUnreadable,

        /// <summary>The copy was refused by <see cref="LayoutValidator"/>.</summary>
        CouldNotSave,
    }

    /// <summary>
    /// Every layout on disk that no currently-paired device owns.
    /// </summary>
    public static IReadOnlyList<LayoutImportCandidate> Candidates(
        LayoutStore store,
        OrphanMarkerStore markers,
        IReadOnlyList<DeviceSummary> liveDevices)
    {
        var liveIds = liveDevices.Select(d => d.DeviceId).ToHashSet(StringComparer.Ordinal);

        // The PC's own layout is not an orphan. HostRequest.HostDeviceId
        // owns layout-host.json exactly as a paired device owns its file,
        // but it is in no registry and DeviceRegistry.ListDevices() will
        // never name it - so without this line the host's arrangement would
        // be offered to every device as something left behind by a device
        // that has gone away, which it has not.
        liveIds.Add(HostRequest.HostDeviceId);

        return LayoutImport.BuildCandidates(store.ListDeviceIds(), liveIds, markers.List());
    }

    public static ListResponse BuildListResponse(IReadOnlyList<LayoutImportCandidate> candidates) =>
        new(candidates.Select(ToResponse).ToArray());

    /// <summary>
    /// Copies <paramref name="sourceDeviceId"/>'s layout onto
    /// <paramref name="callerDeviceId"/>.
    ///
    /// The caller's own current layout is deliberately seeded first when it
    /// has none (<see cref="LayoutAccess.LoadOrSeed"/>), so that the copy
    /// always displaces something into <see cref="LayoutStore"/>'s existing
    /// <c>.bak</c> generation - which is what makes the undo work identically
    /// whether the import happened seconds after pairing (previous = the
    /// starter layout) or months later from settings (previous = the
    /// commander's own arrangement). Without it, a device that had never
    /// rendered a panel yet would import onto nothing and have no way back.
    /// </summary>
    public static ImportOutcome Import(
        LayoutStore store,
        string callerDeviceId,
        string? sourceDeviceId,
        IReadOnlyList<LayoutImportCandidate> candidates,
        IReadOnlySet<string> knownActionNames,
        IDiagnosticLog log)
    {
        if (string.IsNullOrWhiteSpace(sourceDeviceId))
        {
            return ImportOutcome.MissingSource;
        }

        if (!candidates.Any(c => string.Equals(c.DeviceId, sourceDeviceId, StringComparison.Ordinal)))
        {
            return ImportOutcome.NotAnOrphan;
        }

        var source = store.Load(sourceDeviceId);
        if (source.Outcome != LayoutLoadOutcome.Loaded)
        {
            log.Warn(LogCategory, "Refused a layout import: the source layout could not be read", $"outcome={source.Outcome}");
            return ImportOutcome.SourceUnreadable;
        }

        LayoutAccess.LoadOrSeed(store, callerDeviceId, knownActionNames, log);

        var saved = store.Save(callerDeviceId, source.Layout!, knownActionNames);
        if (saved.Outcome != LayoutSaveOutcome.Saved)
        {
            return ImportOutcome.CouldNotSave;
        }

        log.Info(LogCategory, "Imported a layout from an orphaned device", $"deviceId={callerDeviceId}, from={sourceDeviceId}");
        return ImportOutcome.Imported;
    }

    /// <summary>
    /// The three-armed decision at the end of a successful pair
    /// (<c>ref/docs/layout-import.md</c>):
    /// <list type="number">
    /// <item>exactly one orphan answering to the name this device just gave
    /// - adopt it, and report it so the client can say so;</item>
    /// <item>anything else, with at least one orphan - report the list and
    /// let the commander choose, "start fresh" being an equal option;</item>
    /// <item>no orphans at all - return <see langword="null"/>, and the
    /// client says nothing whatsoever.</item>
    /// </list>
    ///
    /// An automatic adoption that fails to save falls through to arm 2
    /// rather than being reported as an adoption that did not happen - the
    /// commander still gets the choice, and nothing claims on screen to have
    /// restored buttons that were not restored.
    /// </summary>
    public static RecoveryResponse? Recover(
        LayoutStore store,
        OrphanMarkerStore markers,
        IReadOnlyList<DeviceSummary> liveDevices,
        string deviceId,
        string deviceName,
        IReadOnlySet<string> knownActionNames,
        IDiagnosticLog log)
    {
        var candidates = Candidates(store, markers, liveDevices);
        if (candidates.Count == 0)
        {
            return null;
        }

        var automatic = LayoutImport.ChooseAutoAdopt(candidates, deviceName);
        if (automatic is not null &&
            Import(store, deviceId, automatic.DeviceId, candidates, knownActionNames, log) == ImportOutcome.Imported)
        {
            return new RecoveryResponse(ToResponse(automatic), Array.Empty<CandidateResponse>());
        }

        return new RecoveryResponse(null, candidates.Select(ToResponse).ToArray());
    }

    public enum DiscardOutcome
    {
        Discarded,

        /// <summary>No device id was supplied at all.</summary>
        MissingSource,

        /// <summary>
        /// The named device is not on the orphan list - either it has no
        /// layout, or it belongs to a device that is still paired. A live
        /// device's layout, and this device's own host layout, can never be
        /// discarded through this route for exactly the reason
        /// <see cref="Import"/> refuses the same id: recomputing
        /// <see cref="Candidates"/> here rather than trusting the caller is
        /// what makes that true.
        /// </summary>
        NotAnOrphan,
    }

    /// <summary>
    /// Permanently removes an orphan's layout file and its marker - the
    /// delete half of the import/recovery chooser
    /// (<c>ref/docs/layout-import.md</c>), which until now could adopt a
    /// forgotten device's layout but never discard one. Genuinely
    /// irreversible: unlike <see cref="Import"/>, there is no <c>.bak</c>
    /// generation left behind to undo this with.
    ///
    /// Validated exactly the way <see cref="Import"/> already is - recompute
    /// <see cref="Candidates"/> and refuse if the requested id is not in it -
    /// so a live-paired device or the host's own layout can never be
    /// discarded through this route.
    /// </summary>
    public static DiscardOutcome Discard(
        LayoutStore store,
        OrphanMarkerStore markers,
        IReadOnlyList<DeviceSummary> liveDevices,
        string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return DiscardOutcome.MissingSource;
        }

        var candidates = Candidates(store, markers, liveDevices);
        if (!candidates.Any(c => string.Equals(c.DeviceId, deviceId, StringComparison.Ordinal)))
        {
            return DiscardOutcome.NotAnOrphan;
        }

        store.Delete(deviceId);
        markers.Delete(deviceId);

        return DiscardOutcome.Discarded;
    }

    /// <summary>
    /// Puts back whatever the last import displaced - <see cref="LayoutStore"/>'s
    /// own single kept generation, not a second history mechanism. Returns
    /// <see langword="false"/> when there is nothing to go back to.
    /// </summary>
    public static bool Undo(LayoutStore store, string callerDeviceId) => store.RestorePrevious(callerDeviceId);

    private static CandidateResponse ToResponse(LayoutImportCandidate candidate) =>
        new(candidate.DeviceId, candidate.Name, candidate.DeviceClass, candidate.LastSeenAt);
}
