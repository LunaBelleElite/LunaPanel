using System.Linq;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Pairing;

namespace LunaPanel.Server.Http;

/// <summary>
/// Maps the device cookie to a <c>deviceId</c> via
/// <see cref="DeviceRegistry.TryAuthorize"/> for every request except the
/// handful in <see cref="ExemptPaths"/> below (<see cref="ApiPaths.Health"/>
/// and <see cref="ApiPaths.Pair"/>, which must both work before any device
/// has ever paired, and <see cref="ApiPaths.App"/>, the web client's own page
/// shell).
///
/// [2026-09-17] The <c>/probe*</c> calibration routes are no longer exempt
/// (O14, closed): an unauthenticated, uncovered surface was a real release
/// hazard on its own terms regardless of how throwaway the instrument is.
/// They still don't require the host - any paired device can reach them,
/// same as <see cref="ApiPaths.Templates"/> already does for the identical
/// underlying <c>CellSizeEstimator</c> call. An
/// unauthenticated request
/// gets a bare 401 with no detail in the body - never a reason, which would
/// tell a caller whether the cookie was merely missing, expired, or actively
/// wrong (the same "don't leak why" discipline
/// <see cref="DeviceRegistry.TryAuthorize"/> itself already applies to
/// timing).
///
/// <b>There is one identity here that never pairs.</b> A request that
/// arrived on the loopback-only host listener is authorised as
/// <see cref="HostRequest.HostDeviceId"/> with no cookie at all
/// (<c>ref/docs/hosting.md</c>) - the commander opening LunaPanel on the PC
/// it is running on should not have to type a pairing code at their own
/// machine. See <see cref="HostRequest"/> for what establishes that and what
/// would defeat it. The same middleware is where the reverse rule lives:
/// <see cref="HostOnlyRoutes"/>'s authoring and transfer routes are refused
/// to everyone else, so a device cannot author a macro - or export somebody
/// else's whole arrangement to a file - by calling the route the client
/// declined to show it a button for.
/// </summary>
public static class DeviceAuthMiddlewareExtensions
{
    private static readonly HashSet<string> ExemptPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ApiPaths.App,
        ApiPaths.Health,
        ApiPaths.Pair,
    };

    /// <summary>
    /// Exact match only - deliberately not a prefix check. A path that
    /// merely starts with an exempt route (e.g. <c>/api/pairing</c>) must
    /// still require authentication; only spelled here as its own method so
    /// a test can pin that directly, without needing a real HTTP pipeline.
    /// </summary>
    public static bool IsExemptPath(string? path) => path is not null && ExemptPaths.Contains(path);

    /// <summary>
    /// The key <c>HttpContext.Items</c> carries
    /// <see cref="HostRequest.IsFromHost"/>'s answer under, set on
    /// <b>every</b> request including the exempt ones - <c>GET /</c> is
    /// exempt and still needs it, because the page it serves is built with
    /// the authoring UI in or out depending on it.
    /// </summary>
    public const string IsHostRequestItemKey = "IsHostRequest";

    /// <summary>
    /// Whether this request arrived on the loopback-only host listener. Reads
    /// only what the middleware already decided, so no call site can reach a
    /// second, differing opinion.
    /// </summary>
    public static bool IsHostRequest(HttpContext context) =>
        context.Items.TryGetValue(IsHostRequestItemKey, out var flag) && flag is true;

    /// <summary>
    /// The query parameter a host-listener request may carry to edit a
    /// specific real, paired device's layout live from the PC, instead of
    /// the PC's own private one - the feature named in this project's own
    /// task brief for "edit a specific paired device's real layout live,
    /// from the PC". Spelled once here and substituted into the client's own
    /// JavaScript from this exact constant (<see cref="PanelClientEndpoint"/>),
    /// the same "one place this is spelled" discipline every route literal
    /// in <see cref="ApiPaths"/> already gets.
    /// </summary>
    public const string AsDeviceQueryParam = "asDevice";

    /// <summary>
    /// What a host-listener request should be authorised as: the plain host
    /// identity when <paramref name="asDeviceParam"/> is absent or blank, the
    /// named device when it names one of <paramref name="liveDevices"/>, or
    /// <see langword="null"/> to signal an outright refusal when it names
    /// anything else (a typo, a forgotten device's orphaned id, or garbage).
    ///
    /// <b>Deliberately pure</b> - no <see cref="HttpContext"/>, no
    /// <see cref="DeviceRegistry"/> - so <c>DeviceAuthMiddlewareExtensionsTests</c>
    /// can pin every branch directly, the same discipline
    /// <see cref="IsExemptPath"/> already gets. <b>Never called for a
    /// non-host request</b> - see <see cref="UseDeviceAuthentication"/>'s own
    /// remarks for why a cookie-authenticated request can never reach this at
    /// all, which is the security property that matters more than anything
    /// this method itself decides.
    /// </summary>
    public static string? ResolveHostDeviceId(string? asDeviceParam, IReadOnlyList<DeviceSummary> liveDevices)
    {
        if (string.IsNullOrWhiteSpace(asDeviceParam))
        {
            return HostRequest.HostDeviceId;
        }

        foreach (var device in liveDevices)
        {
            if (string.Equals(device.DeviceId, asDeviceParam, StringComparison.Ordinal))
            {
                return asDeviceParam;
            }
        }

        return null;
    }

    public static IApplicationBuilder UseDeviceAuthentication(
        this IApplicationBuilder app,
        DeviceRegistry registry,
        IDiagnosticLog log,
        int hostAccessPort)
    {
        return app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value;

            // Decided from the connection alone - see HostRequest's remarks
            // for why no header can reach this, and what that does and does
            // not buy. Set before the exempt check, because GET / is exempt
            // and is precisely the request that needs the answer.
            var fromHost = HostRequest.IsFromHost(
                context.Connection.RemoteIpAddress,
                context.Connection.LocalIpAddress,
                context.Connection.LocalPort,
                hostAccessPort);
            context.Items[IsHostRequestItemKey] = fromHost;

            if (IsExemptPath(path))
            {
                await next(context);
                return;
            }

            if (fromHost)
            {
                // No cookie, no pairing code, no registry record: the host
                // is authorised by where it connected from. Nothing is
                // Touch()ed, because it owns no registry row to touch.
                //
                // asDevice is read ONLY inside this fromHost branch - a
                // cookie-authenticated request below never reaches this code
                // at all, so a paired device sending its own ?asDevice=<x>
                // is not "refused"; the parameter is simply never looked at,
                // and TryAuthorize's own token resolves its identity exactly
                // as it always has (ref/docs and this task's own brief call
                // this the security-critical property).
                var asDeviceParam = context.Request.Query[AsDeviceQueryParam].FirstOrDefault();
                var resolved = ResolveHostDeviceId(asDeviceParam, registry.ListDevices());
                if (resolved is null)
                {
                    log.Warn("Auth", "asDevice named a device that is not currently paired", asDeviceParam);
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(new { error = "That device is not currently paired." });
                    return;
                }

                context.Items["DeviceId"] = resolved;
            }
            else
            {
                var token = context.Request.Cookies[DeviceCookieAuth.CookieName];
                if (!registry.TryAuthorize(token, out var deviceId))
                {
                    log.Warn("Auth", "Unauthenticated request rejected", path);
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
                    return;
                }

                registry.Touch(deviceId!);
                context.Items["DeviceId"] = deviceId;
            }

            // Authenticated, but not necessarily permitted: authoring a
            // macro happens on the PC (ref/docs/macro-builder.md). 403
            // rather than 401 on purpose - the caller's identity was never
            // in doubt, so telling it to pair again would be a lie.
            if (!fromHost && HostOnlyRoutes.RequiresHost(path, context.Request.Method))
            {
                log.Warn("Auth", "Host-only route refused: the request did not come from the host machine", path);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                // Which sentence is HostOnlyRoutes' to decide, not this
                // middleware's - authoring and transfer send a commander to
                // two different tray items, and picking here would put the
                // choice one file away from the list it depends on.
                await context.Response.WriteAsJsonAsync(new { error = HostOnlyRoutes.AdviceFor(path) });
                return;
            }

            await next(context);
        });
    }
}
