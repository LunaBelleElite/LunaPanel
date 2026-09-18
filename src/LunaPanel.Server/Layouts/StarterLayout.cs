using LunaPanel.Core.Layouts;
using LunaPanel.Core.Pairing;

namespace LunaPanel.Server.Layouts;

/// <summary>
/// The layout a brand-new device is seeded with (see <see cref="LayoutAccess"/>),
/// read from the embedded <c>starter-layout.json</c> (phone/default) or
/// <c>starter-layout-tablet.json</c> resource - shipped content, same
/// discipline as <see cref="Catalogue.CatalogueLoader"/>: a malformed
/// embedded resource is a packaging defect and fails loudly rather than
/// silently seeding an empty or broken layout.
///
/// <para>Two full, separately-embedded files rather than one shared file plus
/// a merge/inheritance mechanism - this project has no templating system for
/// layouts, and duplicating the pages that are the same between the two
/// reads more locally than building one. SRV/NOMAD/RHINO/FIGHTER/ON FOOT are
/// byte-for-byte identical between the two files; only SHIP differs, per the
/// device class's screen size. See each file's own <c>"//"</c> comment.</para>
///
/// [2026-09-09, superseded: this used to say "slot 0 deliberately names a
/// macro id (<c>request-docking</c>) that does not exist anywhere yet... do
/// not fix it by inventing the macro". The <c>request-docking</c> macro
/// shipped on 2026-09-07 (<c>ref/docs/panel-tab-tracking.md</c>), so the
/// ship page's slot 0 genuinely resolves and no longer demonstrates
/// anything about degraded slots.]
///
/// <para>Six pages as of 2026-09-09: the <c>t30</c> ship page, then five
/// vessel-context pages - <c>SRV</c>, <c>NOMAD</c>, <c>RHINO</c>,
/// <c>FIGHTER</c>, <c>ON FOOT</c>.
/// [2026-09-09, superseded: this used to say "Four declare a
/// <c>showWhen</c>" - the ship page was context-free, so switching back to
/// it was one-way. It now declares <c>InMainShip</c>, so five of the six
/// pages declare a context.] <c>RHINO</c> is deliberately blank AND
/// deliberately context-free. See <c>ref/docs/vessel-context.md</c>'s "The
/// default context pages" before changing any of it - the page order is
/// load-bearing (first match wins) and the blank page is not an
/// accident.</para>
/// </summary>
public static class StarterLayout
{
    private const string PhoneResourceName = "LunaPanel.Server.definitions.starter-layout.json";
    private const string TabletResourceName = "LunaPanel.Server.definitions.starter-layout-tablet.json";

    /// <exception cref="InvalidOperationException">The embedded resource is missing, or its content does not parse into a valid <see cref="Layout"/> - both packaging defects.</exception>
    public static Layout Load() => LoadFrom(PhoneResourceName);

    /// <summary>
    /// The device-class-aware entry point (called once, at pairing - see
    /// <see cref="LayoutAccess.SeedForNewDevice"/>). <c>"unknown"</c> - a
    /// device class that could not be determined - falls through to the
    /// phone variant deliberately: a too-small grid on an actual tablet is a
    /// minor inconvenience, while a 64-slot grid on an actually-small unknown
    /// screen could be unusable.
    /// </summary>
    /// <exception cref="InvalidOperationException">The embedded resource is missing, or its content does not parse into a valid <see cref="Layout"/> - both packaging defects.</exception>
    public static Layout LoadForDeviceClass(string deviceClass) =>
        deviceClass == DeviceNaming.TabletClass ? LoadFrom(TabletResourceName) : LoadFrom(PhoneResourceName);

    private static Layout LoadFrom(string resourceName)
    {
        using var stream = typeof(StarterLayout).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        var result = LayoutJson.Parse(json);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Embedded starter layout failed to parse: {result.Error}");
        }

        return result.Layout!;
    }
}
