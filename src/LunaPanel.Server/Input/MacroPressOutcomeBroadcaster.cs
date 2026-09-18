namespace LunaPanel.Server.Input;

/// <summary>
/// Carries a finished macro run's outcome from the press that started it to
/// the live channel of the device that pressed it (2026-09-17, O28).
///
/// <c>POST /api/press</c> answers a macro press the moment the run has
/// genuinely started; the run's real result arrives later, and this is how
/// it gets from the request that is long gone to the device's still-open
/// <c>GET /api/panel/live</c> connection. The same shape as
/// <c>LayoutStore.Saved</c>, deliberately: an event carrying the
/// <c>deviceId</c> it is addressed to, which each live handler filters on
/// (<c>OnLayoutSaved</c>'s <c>savedDeviceId == deviceId</c>, exactly).
///
/// Owned by the server and registered beside the other singletons in
/// <c>ServerHostBuilder.Build</c>, rather than raised by
/// <see cref="LunaPanel.Core.Macros.MacroRunner"/> itself, because the
/// runner knows nothing about devices, pages or slots and must stay that way
/// - <c>RunningMacroLit</c>'s own remarks and <c>ref/docs/macros.md</c> both
/// state the running-macro glow is machine-wide on purpose. The runner
/// answers "which macro id"; the press that arrived on a device is the only
/// thing that knows "which device", so the press is what publishes.
///
/// Raised on whichever thread completed the run (a thread-pool continuation
/// in production, never a request thread), synchronously, after the runner
/// has already taken the id out of its running set - so a handler that
/// rebuilds lit state on receipt draws the button dark, not still lit.
/// </summary>
public sealed class MacroPressOutcomeBroadcaster
{
    /// <summary>
    /// A macro run started by a press from <c>deviceId</c> has ended, however
    /// it ended - success, abort, or the commander stopping it. Carries the
    /// macro id (the correlation key: the runner allows at most one run per
    /// id, so no minted request id is needed) and the full
    /// <see cref="MacroPresser.MacroPressResult"/> the press response used
    /// to carry.
    /// </summary>
    public event Action<string, string, MacroPresser.MacroPressResult>? Finished;

    public void Publish(string deviceId, string macroId, MacroPresser.MacroPressResult result)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentNullException.ThrowIfNull(macroId);
        ArgumentNullException.ThrowIfNull(result);

        Finished?.Invoke(deviceId, macroId, result);
    }
}
