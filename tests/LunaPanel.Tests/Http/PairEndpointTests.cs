using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Pairing;
using LunaPanel.Server.Http;
using LunaPanel.Server.Tray;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Drives <see cref="PairEndpoint.TryPair"/> against a real
/// <see cref="DeviceRegistry"/> over a temp state file, exactly like
/// <c>DeviceRegistryTests</c> does - this only pins the endpoint's own thin
/// layer (default device name/class substitution), not the registry's
/// pairing state machine itself, which is already covered there.
/// </summary>
public class PairEndpointTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();

        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static string NewStatePath([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "pair-endpoint", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "device-registry-state.json");
    }

    [Fact]
    public void TryPair_CorrectCode_ReturnsTrue_WithTokenAndDeviceId()
    {
        var registry = new DeviceRegistry(NewStatePath(), new CapturingDiagnosticLog(), TimeProvider.System);
        var request = new PairEndpoint.Request(registry.CurrentCode, "Kitchen tablet", "tablet");

        var result = PairEndpoint.TryPair(registry, request, out var token, out var deviceId, out var deviceName);

        Assert.True(result);
        Assert.NotNull(token);
        Assert.NotNull(deviceId);
        Assert.Equal("Kitchen tablet", deviceName);
    }

    [Fact]
    public void TryPair_WrongCode_ReturnsFalse_WithNullTokenAndDeviceId()
    {
        var registry = new DeviceRegistry(NewStatePath(), new CapturingDiagnosticLog(), TimeProvider.System);
        var wrongCode = WrongCodeFor(registry.CurrentCode);
        var request = new PairEndpoint.Request(wrongCode, "Kitchen tablet", "tablet");

        var result = PairEndpoint.TryPair(registry, request, out var token, out var deviceId, out _);

        Assert.False(result);
        Assert.Null(token);
        Assert.Null(deviceId);
    }

    /// <summary>A 6-digit code guaranteed to differ from <paramref name="currentCode"/>: increments its first digit, wrapping 9 to 0.</summary>
    private static string WrongCodeFor(string currentCode)
    {
        var firstDigit = (currentCode[0] - '0' + 1) % 10;
        return firstDigit + currentCode[1..];
    }

    /// <summary>
    /// SUPERSEDED 2026-09-08 (was
    /// <c>TryPair_MissingDeviceNameAndClass_DefaultsToUnnamedAndUnknown</c>,
    /// which pinned <c>Name == "Unnamed device"</c>). Every device on the
    /// reference machine was called "Unnamed device" because the web client
    /// sent no name at all, which is precisely the blocker
    /// <c>ref/docs/layout-import.md</c> names: name-based recovery is
    /// impossible while every device matches every other. The default is now
    /// the class label plus the next free ordinal
    /// (<see cref="DeviceNaming"/>). The class default is unchanged.
    ///
    /// Still read back off the persisted state file rather than only through
    /// <see cref="DeviceRegistry.ListDevices"/>, which proves the value was
    /// actually stored rather than merely returned.
    /// </summary>
    [Fact]
    public void TryPair_MissingDeviceNameAndClass_DefaultsToTheClassLabel_AndUnknownClass()
    {
        var statePath = NewStatePath();
        var registry = new DeviceRegistry(statePath, new CapturingDiagnosticLog(), TimeProvider.System);
        var request = new PairEndpoint.Request(registry.CurrentCode, DeviceName: null, DeviceClass: "  ");

        var paired = PairEndpoint.TryPair(registry, request, out _, out _, out var deviceName);
        Assert.True(paired);
        Assert.Equal("Device", deviceName);

        var persistedJson = File.ReadAllText(statePath);
        Assert.Contains("\"Name\":\"Device\"", persistedJson, StringComparison.Ordinal);
        Assert.Contains("\"DeviceClass\":\"unknown\"", persistedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Unnamed device", persistedJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole point of Task A: a name and a class the client actually
    /// sent have to reach the registry, not be silently replaced by
    /// placeholders. Read back through <see cref="DeviceRegistry.ListDevices"/>,
    /// which is what both device lists render from.
    /// </summary>
    [Fact]
    public void TryPair_NameAndClassFromTheClient_ReachTheRegistry()
    {
        var registry = new DeviceRegistry(NewStatePath(), new CapturingDiagnosticLog(), TimeProvider.System);

        Assert.True(PairEndpoint.TryPair(
            registry,
            new PairEndpoint.Request(registry.CurrentCode, "Kitchen tablet", "tablet"),
            out _,
            out var deviceId,
            out _));

        var device = Assert.Single(registry.ListDevices());
        Assert.Equal(deviceId, device.DeviceId);
        Assert.Equal("Kitchen tablet", device.Name);
        Assert.Equal("tablet", device.DeviceClass);
    }

    /// <summary>
    /// The ordinal disambiguator, driven end to end through two real
    /// pairings rather than against <see cref="DeviceNaming"/> alone - the
    /// endpoint has to actually consult the registry's current device list,
    /// and handing it an empty one would leave both devices called "Phone"
    /// with every <c>DeviceNamingTests</c> case still green.
    /// </summary>
    [Fact]
    public void TryPair_SecondPhoneWithNoTypedName_IsNamedPhone2()
    {
        var registry = new DeviceRegistry(NewStatePath(), new CapturingDiagnosticLog(), TimeProvider.System);

        Assert.True(PairEndpoint.TryPair(
            registry, new PairEndpoint.Request(registry.CurrentCode, null, "phone"), out _, out _, out var firstName));
        registry.OpenPairingWindow();
        Assert.True(PairEndpoint.TryPair(
            registry, new PairEndpoint.Request(registry.CurrentCode, null, "phone"), out _, out _, out var secondName));

        Assert.Equal("Phone", firstName);
        Assert.Equal("Phone 2", secondName);
        Assert.Equal(new[] { "Phone", "Phone 2" }, registry.ListDevices().Select(d => d.Name));
    }

    /// <summary>
    /// A tablet pairing after a phone starts its own ordinal run - the
    /// disambiguator is scoped to the class, per the coordinator's decision
    /// (2026-09-08).
    /// </summary>
    [Fact]
    public void TryPair_ATabletAfterAPhone_IsNamedTablet_NotPhone2()
    {
        var registry = new DeviceRegistry(NewStatePath(), new CapturingDiagnosticLog(), TimeProvider.System);

        Assert.True(PairEndpoint.TryPair(
            registry, new PairEndpoint.Request(registry.CurrentCode, null, "phone"), out _, out _, out _));
        registry.OpenPairingWindow();
        Assert.True(PairEndpoint.TryPair(
            registry, new PairEndpoint.Request(registry.CurrentCode, null, "tablet"), out _, out _, out var tabletName));

        Assert.Equal("Tablet", tabletName);
    }

    /// <summary>
    /// <b>Names are display data, never identity</b>
    /// (<c>ref/docs/layout-import.md</c>). Two devices deliberately given the
    /// same name must both pair, get distinct ids and distinct tokens, and
    /// both keep authorizing - nothing anywhere may key off the name.
    /// </summary>
    [Fact]
    public void TryPair_TwoDevicesGivenTheSameName_BothPair_AndBothStayIndependentlyAuthorized()
    {
        var registry = new DeviceRegistry(NewStatePath(), new CapturingDiagnosticLog(), TimeProvider.System);

        Assert.True(PairEndpoint.TryPair(
            registry, new PairEndpoint.Request(registry.CurrentCode, "Phone", "phone"), out var firstToken, out var firstId, out _));
        registry.OpenPairingWindow();
        Assert.True(PairEndpoint.TryPair(
            registry, new PairEndpoint.Request(registry.CurrentCode, "Phone", "phone"), out var secondToken, out var secondId, out _));

        Assert.NotEqual(firstId, secondId);
        Assert.NotEqual(firstToken, secondToken);

        Assert.True(registry.TryAuthorize(firstToken, out var authorizedFirst));
        Assert.Equal(firstId, authorizedFirst);
        Assert.True(registry.TryAuthorize(secondToken, out var authorizedSecond));
        Assert.Equal(secondId, authorizedSecond);

        Assert.Equal(new[] { "Phone", "Phone" }, registry.ListDevices().Select(d => d.Name));
    }

    /// <summary>
    /// A class the client did not send, or made up, becomes "unknown" rather
    /// than being stored verbatim - the devices list and the ordinal rule
    /// both read this value back, and neither has anything to do with a
    /// third value invented by a caller.
    /// </summary>
    [Theory]
    [InlineData("desktop")]
    [InlineData("PHONE-ish")]
    [InlineData("")]
    public void TryPair_AnUnrecognizedClass_IsStoredAsUnknown(string sentClass)
    {
        var registry = new DeviceRegistry(NewStatePath(), new CapturingDiagnosticLog(), TimeProvider.System);

        Assert.True(PairEndpoint.TryPair(
            registry, new PairEndpoint.Request(registry.CurrentCode, "Something", sentClass), out _, out _, out _));

        Assert.Equal("unknown", Assert.Single(registry.ListDevices()).DeviceClass);
    }

    /// <summary>
    /// <c>/api/pair</c> is reachable without a cookie by construction (an
    /// unpaired device has none), so what it accepts into the registry's own
    /// state file is worth bounding. A typed name is trimmed, capped, and
    /// stripped of the control characters that would make both device lists
    /// unreadable - and is otherwise used exactly as typed.
    /// </summary>
    [Fact]
    public void TryPair_ATypedName_IsSanitizedButOtherwiseUsedVerbatim()
    {
        var registry = new DeviceRegistry(NewStatePath(), new CapturingDiagnosticLog(), TimeProvider.System);
        var typed = "  Luna phone\r\n" + new string('x', 60);

        Assert.True(PairEndpoint.TryPair(
            registry, new PairEndpoint.Request(registry.CurrentCode, typed, "phone"), out _, out _, out var deviceName));

        Assert.Equal(DeviceNaming.SanitizeTypedName(typed), deviceName);
        Assert.StartsWith("Luna phone x", deviceName, StringComparison.Ordinal);
        Assert.Equal(DeviceNaming.MaxNameLength, deviceName.Length);
    }

    /// <summary>
    /// A name of nothing but whitespace is not a name - it falls through to
    /// the assigned default rather than registering a device with a blank
    /// row in the devices list.
    /// </summary>
    [Fact]
    public void TryPair_AWhitespaceOnlyName_FallsBackToTheAssignedDefault()
    {
        var registry = new DeviceRegistry(NewStatePath(), new CapturingDiagnosticLog(), TimeProvider.System);

        Assert.True(PairEndpoint.TryPair(
            registry, new PairEndpoint.Request(registry.CurrentCode, "   ", "phone"), out _, out _, out var deviceName));

        Assert.Equal("Phone", deviceName);
    }

    /// <summary>
    /// The regression this method exists for. The tray's "Add a device"
    /// window shows the code through
    /// <see cref="PairingCodeFormatter.GroupForDisplay"/> and pre-selects it
    /// so it can be copied - so the value that actually arrives is
    /// "123 456", never "123456", and pairing could not succeed for anyone
    /// who did the thing that window invites. Four consecutive live failures
    /// on 2026-09-08 before it was spotted.
    ///
    /// Drives the REAL formatter rather than a hand-typed "123 456" literal:
    /// if the display grouping ever changes to something
    /// <see cref="PairEndpoint.NormalizeCode"/> does not strip, this fails
    /// here instead of in someone's living room.
    /// </summary>
    [Fact]
    public void TryPair_CodeExactlyAsTheTrayDisplaysIt_Pairs()
    {
        var registry = new DeviceRegistry(NewStatePath(), new CapturingDiagnosticLog(), TimeProvider.System);
        var displayed = PairingCodeFormatter.GroupForDisplay(registry.CurrentCode);

        // If these ever stop differing the test still passes but stops
        // proving anything, so say so out loud rather than pass vacuously.
        Assert.NotEqual(registry.CurrentCode, displayed);

        var paired = PairEndpoint.TryPair(
            registry,
            new PairEndpoint.Request(displayed, "PC", "desktop"),
            out var token,
            out var deviceId,
            out _);

        Assert.True(paired);
        Assert.NotNull(token);
        Assert.NotNull(deviceId);
    }

    [Theory]
    [InlineData("123 456", "123456")]
    [InlineData("123-456", "123456")]
    [InlineData("  123456  ", "123456")]
    [InlineData("1 2 3 4 5 6", "123456")]
    public void NormalizeCode_StripsSpacesAndDashes(string typed, string expected) =>
        Assert.Equal(expected, PairEndpoint.NormalizeCode(typed));

    /// <summary>
    /// Leniency about separators must not become leniency about digits - a
    /// wrong code stays wrong, and a null stays null rather than becoming an
    /// empty string that could match an empty stored code.
    /// </summary>
    [Fact]
    public void NormalizeCode_DoesNotRepairAWrongCode()
    {
        Assert.Equal("123457", PairEndpoint.NormalizeCode("123 457"));
        Assert.Null(PairEndpoint.NormalizeCode(null));
    }
}
