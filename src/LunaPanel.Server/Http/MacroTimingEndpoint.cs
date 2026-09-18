using LunaPanel.Core.Macros;

namespace LunaPanel.Server.Http;

/// <summary>
/// <c>GET/POST /api/macro-timing</c>'s handler logic (<c>ref/docs/macro-timing.md</c>) -
/// the settings pane's "Timing" tab, exposing hold duration and inter-press
/// gap. Kept free of any ASP.NET type, same discipline as
/// <see cref="ThemeEndpoint"/>/<see cref="PanelSettingsEndpoint"/>.
///
/// Unlike every other settings endpoint in this file, there is no device id
/// anywhere here - <see cref="MacroTimingSettingsStore"/> is server-wide by
/// design (<c>ref/docs/macro-timing.md</c>'s "Scope"), so
/// <c>ServerHostBuilder</c>'s route handlers for this endpoint never read
/// <c>context.Items["DeviceId"]</c> at all, even though the route itself
/// still requires a valid paired device (it is not in
/// <see cref="DeviceAuthMiddlewareExtensions"/>'s exempt list) - a settings
/// route being reachable at all still means "some paired device asked",
/// even though which one made no difference to the answer.
/// </summary>
public static class MacroTimingEndpoint
{
    public sealed record Request(int HoldMs, int InterPressGapMs);

    /// <param name="HoldMs">Current effective hold duration, milliseconds.</param>
    /// <param name="InterPressGapMs">Current effective inter-press gap, milliseconds.</param>
    /// <param name="HoldMinMs">
    /// The measured floor below which the client shows its "below the tested
    /// minimum" warning - read from the same constant the server itself
    /// measures against (<see cref="MacroTimingDefaults.MeasuredMinimumHoldDuration"/>),
    /// never restated as a second hand-typed number in the page's own
    /// JavaScript (the same "ask the server rather than restate it" fix
    /// <c>ref/docs/web-client.md</c>'s chrome-allowance section already
    /// applied to <c>headerStrip</c>/<c>framePadding</c>).
    /// </param>
    /// <param name="InterPressGapMinMs">Same, for the gap field.</param>
    public sealed record Response(int HoldMs, int InterPressGapMs, int HoldMinMs, int InterPressGapMinMs);

    public static Response BuildResponse(MacroTimingSettings settings) => new(
        HoldMs: (int)settings.HoldDuration.TotalMilliseconds,
        InterPressGapMs: (int)settings.InterPressGap.TotalMilliseconds,
        HoldMinMs: (int)MacroTimingDefaults.MeasuredMinimumHoldDuration.TotalMilliseconds,
        InterPressGapMinMs: (int)MacroTimingDefaults.MeasuredMinimumInterPressGap.TotalMilliseconds);

    /// <summary>
    /// Validates <paramref name="request"/> against
    /// <see cref="MacroTimingSettings.IsValid"/> - refused (never persisted)
    /// when either field is out of range or the request itself couldn't be
    /// read. Below-the-measured-minimum is NOT refused here - that is the
    /// softer, allowed-with-a-warning case <c>ref/docs/macro-timing.md</c>'s
    /// "the cliff" describes, and the warning itself is rendered client-side
    /// from <see cref="Response.HoldMinMs"/>/<see cref="Response.InterPressGapMinMs"/>.
    /// </summary>
    public static bool TryParse(Request? request, out MacroTimingSettings settings, out string? error)
    {
        if (request is null)
        {
            settings = MacroTimingSettings.Default;
            error = "Invalid request.";
            return false;
        }

        if (!MacroTimingSettings.IsValid(request.HoldMs, request.InterPressGapMs))
        {
            settings = MacroTimingSettings.Default;
            error = $"Values must be between {MacroTimingSettings.MinMs} and {MacroTimingSettings.MaxMs} ms.";
            return false;
        }

        settings = new MacroTimingSettings(
            TimeSpan.FromMilliseconds(request.HoldMs),
            TimeSpan.FromMilliseconds(request.InterPressGapMs));
        error = null;
        return true;
    }
}
