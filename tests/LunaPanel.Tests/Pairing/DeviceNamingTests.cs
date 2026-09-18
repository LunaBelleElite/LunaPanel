using LunaPanel.Core.Pairing;

namespace LunaPanel.Tests.Pairing;

/// <summary>
/// Drives <see cref="DeviceNaming"/> directly - a pure function over a list
/// of already-registered devices, so every case here is a literal in/out
/// pair rather than anything read back off a registry.
///
/// The rule being pinned (<c>ref/docs/layout-import.md</c>, coordinator's
/// decision 2026-09-08): the default name is the class label plus <b>the
/// next free ordinal among devices already registered with that class</b> -
/// free, not "count + 1", so forgetting the second of three phones makes
/// "Phone 2" available again rather than minting "Phone 4".
/// </summary>
public class DeviceNamingTests
{
    private static DeviceSummary Device(string name, string deviceClass) =>
        new($"id-{name}-{deviceClass}", name, deviceClass, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData("phone", "phone")]
    [InlineData("PHONE", "phone")]
    [InlineData("  tablet  ", "tablet")]
    [InlineData("tablet", "tablet")]
    [InlineData("desktop", "unknown")]
    [InlineData("", "unknown")]
    [InlineData(null, "unknown")]
    public void NormalizeClass_AcceptsOnlyPhoneAndTablet_EverythingElseIsUnknown(string? given, string expected) =>
        Assert.Equal(expected, DeviceNaming.NormalizeClass(given));

    [Theory]
    [InlineData("phone", "Phone")]
    [InlineData("tablet", "Tablet")]
    [InlineData("unknown", "Device")]
    public void LabelForClass_MapsEachClassToItsDisplayLabel(string deviceClass, string expected) =>
        Assert.Equal(expected, DeviceNaming.LabelForClass(deviceClass));

    [Fact]
    public void NextDefaultName_NoDevicesAtAll_IsTheBareLabel() =>
        Assert.Equal("Phone", DeviceNaming.NextDefaultName("phone", Array.Empty<DeviceSummary>()));

    [Fact]
    public void NextDefaultName_OnePhoneAlready_IsPhone2() =>
        Assert.Equal("Phone 2", DeviceNaming.NextDefaultName("phone", new[] { Device("Phone", "phone") }));

    [Fact]
    public void NextDefaultName_PhoneAndPhone2Already_IsPhone3() =>
        Assert.Equal(
            "Phone 3",
            DeviceNaming.NextDefaultName("phone", new[] { Device("Phone", "phone"), Device("Phone 2", "phone") }));

    /// <summary>
    /// The "free", not "next after the highest", half of the rule. With
    /// "Phone" and "Phone 3" registered, 2 is the gap and 2 is what gets
    /// used - a count-based implementation would say "Phone 3" (already
    /// taken) or "Phone 4" (skipping the gap), and both look right until
    /// someone forgets a device in the middle.
    /// </summary>
    [Fact]
    public void NextDefaultName_GapInTheOrdinals_FillsTheGap() =>
        Assert.Equal(
            "Phone 2",
            DeviceNaming.NextDefaultName("phone", new[] { Device("Phone", "phone"), Device("Phone 3", "phone") }));

    /// <summary>
    /// The ordinal is scoped to the class, per the coordinator's decision.
    /// A household with one tablet still calls its first phone "Phone", not
    /// "Phone 2".
    /// </summary>
    [Fact]
    public void NextDefaultName_ADeviceOfAnotherClass_DoesNotConsumeAnOrdinal() =>
        Assert.Equal(
            "Phone",
            DeviceNaming.NextDefaultName("phone", new[] { Device("Tablet", "tablet"), Device("Tablet 2", "tablet") }));

    /// <summary>
    /// The case that actually discriminates the class filter, added
    /// 2026-09-08 after mutation M2 (deleting the class check entirely) left
    /// the whole suite green: a tablet the commander happened to <em>name</em>
    /// "Phone" was still counted, because every other test's names differ by
    /// label as well as by class, so the label match alone was doing all the
    /// work and the class filter was pinned by nothing.
    ///
    /// The rule ("the next free ordinal among devices already registered
    /// with that class") is the coordinator's, so it is what is pinned. Its
    /// consequence is worth stating: this household ends up with a tablet
    /// called "Phone" and a phone called "Phone", which is allowed - names
    /// are display data, never identity - but it does mean an automatic
    /// adoption for either of them would see an ambiguous match and ask
    /// instead of guessing, which is the right failure to have.
    /// </summary>
    [Fact]
    public void NextDefaultName_ATabletTheCommanderNamedPhone_DoesNotConsumeThePhoneOrdinal() =>
        Assert.Equal(
            "Phone",
            DeviceNaming.NextDefaultName("phone", new[] { Device("Phone", "tablet") }));

    /// <summary>
    /// A commander-typed name that happens to look nothing like the label
    /// consumes no ordinal, so a household whose only phone is called
    /// "Luna's phone" still gets a plain "Phone" for the next one.
    /// </summary>
    [Fact]
    public void NextDefaultName_ACommanderTypedName_DoesNotConsumeAnOrdinal() =>
        Assert.Equal(
            "Phone",
            DeviceNaming.NextDefaultName("phone", new[] { Device("Luna's phone", "phone") }));

    [Fact]
    public void NextDefaultName_MatchesTheLabelCaseInsensitively() =>
        Assert.Equal("Phone 2", DeviceNaming.NextDefaultName("phone", new[] { Device("phone", "phone") }));

    [Fact]
    public void NextDefaultName_UnknownClass_UsesTheDeviceLabel() =>
        Assert.Equal("Device 2", DeviceNaming.NextDefaultName("unknown", new[] { Device("Device", "unknown") }));

    /// <summary>
    /// "Phone 02" is not the ordinal 2 written differently - it is a name
    /// the commander typed. Treating it as 2 would silently make the next
    /// default "Phone 3" and leave two devices whose names differ only by a
    /// leading zero, which is the least readable outcome available.
    /// </summary>
    [Fact]
    public void NextDefaultName_ALeadingZeroOrdinal_IsNotTreatedAsThatOrdinal() =>
        Assert.Equal(
            "Phone 2",
            DeviceNaming.NextDefaultName("phone", new[] { Device("Phone", "phone"), Device("Phone 02", "phone") }));

    [Fact]
    public void SanitizeTypedName_TrimsSurroundingWhitespace() =>
        Assert.Equal("Kitchen tablet", DeviceNaming.SanitizeTypedName("  Kitchen tablet  "));

    [Fact]
    public void SanitizeTypedName_TruncatesToMaxNameLength()
    {
        var typed = new string('x', DeviceNaming.MaxNameLength + 25);

        var sanitized = DeviceNaming.SanitizeTypedName(typed);

        Assert.Equal(DeviceNaming.MaxNameLength, sanitized.Length);
        Assert.Equal(new string('x', DeviceNaming.MaxNameLength), sanitized);
    }

    /// <summary>
    /// A name is written into the registry state file and rendered into two
    /// device lists; a newline or a control character in it makes both
    /// unreadable and neither is anything a commander meant to type.
    /// </summary>
    [Fact]
    public void SanitizeTypedName_StripsControlCharacters() =>
        Assert.Equal("Kitchen tablet", DeviceNaming.SanitizeTypedName("Kitchen\r\n\ttablet"));

    [Fact]
    public void MaxNameLength_IsForty() => Assert.Equal(40, DeviceNaming.MaxNameLength);
}
