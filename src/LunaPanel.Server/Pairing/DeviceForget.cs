using LunaPanel.Core.Layouts;
using LunaPanel.Core.Pairing;

namespace LunaPanel.Server.Pairing;

/// <summary>
/// Forgetting a device, plus the marker that has to outlive it.
///
/// <b>Why this is a shared call and not code inside either caller.</b> There
/// are two forget paths, not one: <c>POST /api/devices/forget</c> (a paired
/// device's Settings -&gt; Devices pane) and the tray's own Devices window,
/// which holds the same <see cref="DeviceRegistry"/> in-process and used to
/// call <see cref="DeviceRegistry.Forget"/> directly. A marker written in
/// only one of them would leave the import list correct or a column of hex
/// depending on which window the commander happened to use, which is the
/// hardest kind of gap to notice.
///
/// <b>Why not inside <see cref="DeviceRegistry.Forget"/> itself.</b> That
/// type is the pairing state machine, and its file has source-scan pins on
/// it because a reasonable-looking edit near its constant-time comparison
/// path reopens a timing side-channel (<c>ref/docs/pairing.md</c>). Teaching
/// it about a layouts directory would couple the two subsystems and add
/// traffic to exactly the file that should stay boring.
///
/// The marker is written from the record captured <em>before</em> the
/// forget, since the record is gone afterwards, and its failure is not the
/// forget's failure: revoking a lost device must never depend on a
/// descriptive sidecar the commander never asked for.
/// </summary>
public static class DeviceForget
{
    public static bool Forget(DeviceRegistry registry, OrphanMarkerStore markers, string deviceId)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(markers);

        var summary = registry.ListDevices().FirstOrDefault(d => string.Equals(d.DeviceId, deviceId, StringComparison.Ordinal));

        if (!registry.Forget(deviceId))
        {
            return false;
        }

        if (summary is not null)
        {
            markers.Write(new OrphanMarker(summary.DeviceId, summary.Name, summary.DeviceClass, summary.LastSeenAt));
        }

        return true;
    }
}
