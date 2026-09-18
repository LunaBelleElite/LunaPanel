using LunaPanel.Server.Discovery;

namespace LunaPanel.Tests.Discovery;

/// <summary>
/// Pins <see cref="DiscoveryResultHolder"/>'s whole contract: constructed
/// with an initial value, <see cref="DiscoveryResultHolder.Current"/> reads
/// it back, and <see cref="DiscoveryResultHolder.Replace"/> swaps in a new
/// one that every subsequent read sees - the mechanism "Refresh bindings"
/// (<c>ref/docs/bindings-source.md</c>) depends on.
/// </summary>
public class DiscoveryResultHolderTests
{
    private static PathDiscoveryResult Fake(string? bindsPath) => new(
        EliteInstallations: Array.Empty<EliteInstallation>(),
        Bindings: new BindingsDiscoveryResult(bindsPath, bindsPath is not null ? new BindsVersion(4, 2) : null, Array.Empty<string>()),
        BindingsSelection: new PresetSelectionResult(bindsPath, null, PresetSelectionMethod.Fallback, bindsPath is not null ? PresetOrigin.CommanderAuthored : null, null, false, null),
        Edhm: new EdhmDiscoveryResult(false, null, null, Array.Empty<EdhmEditionData>()),
        LunaPanelDirectories: new LunaPanelDirectoryLayout(@"C:\fake\Logs", @"C:\fake\Layouts", @"C:\fake\Pairing\device-registry.json"),
        StatusJson: new StatusJsonDiscoveryResult(null));

    [Fact]
    public void Current_ReturnsTheValueSuppliedAtConstruction()
    {
        var initial = Fake(@"C:\fake\Custom.4.2.binds");
        var holder = new DiscoveryResultHolder(initial);

        Assert.Same(initial, holder.Current);
    }

    [Fact]
    public void Replace_SwapsTheValue_SubsequentReadsSeeTheNewOne()
    {
        var initial = Fake(null);
        var holder = new DiscoveryResultHolder(initial);

        var replacement = Fake(@"C:\fake\Custom.4.9.binds");
        holder.Replace(replacement);

        Assert.Same(replacement, holder.Current);
        Assert.NotSame(initial, holder.Current);
    }

    [Fact]
    public void Constructor_NullInitial_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DiscoveryResultHolder(null!));
    }

    [Fact]
    public void Replace_Null_Throws()
    {
        var holder = new DiscoveryResultHolder(Fake(null));

        Assert.Throws<ArgumentNullException>(() => holder.Replace(null!));
    }
}
