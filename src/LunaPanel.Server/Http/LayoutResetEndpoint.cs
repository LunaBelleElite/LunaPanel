using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Layouts;
using LunaPanel.Server.Layouts;

namespace LunaPanel.Server.Http;

/// <summary>
/// <c>POST /api/layout/reset</c>'s handler logic, kept free of any ASP.NET
/// type - same discipline as <see cref="LayoutImportEndpoint"/>.
///
/// <b>Reset and import are the same operation with a different source</b>
/// (<c>ref/docs/reset-to-default.md</c>): both write a whole
/// <see cref="Layout"/> through <see cref="LayoutStore.Save"/>, so the
/// device it lands on gets exactly the same <c>.bak</c> generation and the
/// same <see cref="LayoutImportEndpoint.Undo"/> either way. Reset's source is
/// always <see cref="StarterLayout.Load"/> rather than another device's file
/// - there is no candidate to choose, so there is no candidate list here.
///
/// <b>Whole device, every page.</b> <see cref="LayoutStore.Save"/> writes the
/// entire <see cref="Layout"/> it is given, replacing whatever was on disk -
/// there is no per-page merge path to accidentally take, the same way
/// <see cref="LayoutImportEndpoint.Import"/> replaces a device's whole layout
/// rather than one page of it.
/// </summary>
public static class LayoutResetEndpoint
{
    private const string LogCategory = "Layout";

    public enum ResetOutcome
    {
        Reset,

        /// <summary>The write was refused by <see cref="LayoutValidator"/>.</summary>
        CouldNotSave,
    }

    /// <summary>
    /// Returns <paramref name="deviceId"/>'s layout to the shipped starter -
    /// every page, not just whichever one the commander was looking at.
    /// Reuses <see cref="LayoutStore.Save"/>'s own generation-keeping, so the
    /// arrangement this replaces survives as <c>.bak</c> and
    /// <see cref="LayoutImportEndpoint.Undo"/> puts it back - nothing here is
    /// a second persistence mechanism.
    ///
    /// <paramref name="deviceClass"/> selects which starter variant comes
    /// back (<see cref="StarterLayout.LoadForDeviceClass"/>) - a tablet that
    /// resets gets its own 64-slot SHIP page back, not the phone's 30-slot
    /// one.
    /// </summary>
    public static ResetOutcome Reset(LayoutStore store, string deviceId, string deviceClass, IReadOnlySet<string> knownActionNames, IDiagnosticLog log)
    {
        var starter = StarterLayout.LoadForDeviceClass(deviceClass);
        var saved = store.Save(deviceId, starter, knownActionNames);
        if (saved.Outcome != LayoutSaveOutcome.Saved)
        {
            log.Warn(LogCategory, "Refused to reset a device's buttons to default", string.Join("; ", saved.ValidationErrors));
            return ResetOutcome.CouldNotSave;
        }

        log.Info(LogCategory, "Reset a device's buttons to the starter layout", $"deviceId={deviceId}");
        return ResetOutcome.Reset;
    }
}
