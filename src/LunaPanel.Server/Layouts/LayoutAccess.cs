using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Layouts;

namespace LunaPanel.Server.Layouts;

/// <summary>
/// Loads a device's layout, seeding it from <see cref="StarterLayout"/> the
/// first time <see cref="LayoutStore.Load"/> reports
/// <see cref="LayoutLoadOutcome.NotFound"/> for that device - a device that
/// already has a layout on disk keeps it, byte for byte, untouched.
///
/// <see cref="LayoutLoadOutcome.Corrupt"/> and
/// <see cref="LayoutLoadOutcome.TooNewSchema"/> pass straight through
/// <see cref="LayoutStore.Load"/>'s own outcome, unchanged - seeding only
/// ever replaces "nothing has been saved for this device yet", never a
/// layout <see cref="LayoutStore"/> itself judged unreadable. Silently
/// overwriting a user's own (if damaged) layout with the starter is exactly
/// the failure this split exists to avoid.
///
/// If persisting the freshly-seeded starter layout fails (e.g. the player's
/// current bindings file doesn't recognize one of the starter's action
/// names at all - see <see cref="LayoutValidator.ValidateForSave"/>), the
/// seeded layout is still returned in memory so the caller has something
/// usable to render; only the write to disk is skipped, and the next
/// request tries seeding again.
/// </summary>
public static class LayoutAccess
{
    private const string LogCategory = "Layout";

    public static LayoutLoadResult LoadOrSeed(LayoutStore store, string deviceId, IReadOnlySet<string> knownActionNames, IDiagnosticLog log)
    {
        var result = store.Load(deviceId);
        if (result.Outcome != LayoutLoadOutcome.NotFound)
        {
            return result;
        }

        var starter = StarterLayout.Load();
        var saveResult = store.Save(deviceId, starter, knownActionNames);

        if (saveResult.Outcome == LayoutSaveOutcome.Saved)
        {
            log.Info(LogCategory, "Seeded starter layout for new device", deviceId);
        }
        else
        {
            log.Warn(
                LogCategory,
                "Seeded starter layout for new device but could not persist it; using it in memory only for this request",
                string.Join("; ", saveResult.ValidationErrors));
        }

        return LayoutLoadResult.Loaded(starter);
    }

    /// <summary>
    /// Seeds a brand-new device proactively, once, right at pairing - with
    /// the starter variant matching its own device class
    /// (<see cref="StarterLayout.LoadForDeviceClass"/>), rather than waiting
    /// for <see cref="LoadOrSeed"/>'s own lazy seed (which has no device
    /// class to consult and always falls back to the phone variant). Callers
    /// must skip this entirely when a layout was already adopted from an
    /// orphan for this device - never call this after a successful recovery.
    ///
    /// Same tolerant treatment as <see cref="LoadOrSeed"/>'s own seed branch:
    /// if persisting fails (e.g. no bindings file recognized yet), the
    /// seeded layout is still returned in memory so the caller has something
    /// usable, and only the write to disk is skipped.
    /// </summary>
    public static Layout SeedForNewDevice(LayoutStore store, string deviceId, string deviceClass, IReadOnlySet<string> knownActionNames, IDiagnosticLog log)
    {
        var starter = StarterLayout.LoadForDeviceClass(deviceClass);
        var saveResult = store.Save(deviceId, starter, knownActionNames);

        if (saveResult.Outcome == LayoutSaveOutcome.Saved)
        {
            log.Info(LogCategory, "Seeded starter layout for new device", deviceId);
        }
        else
        {
            log.Warn(
                LogCategory,
                "Seeded starter layout for new device but could not persist it; using it in memory only for this request",
                string.Join("; ", saveResult.ValidationErrors));
        }

        return starter;
    }
}
