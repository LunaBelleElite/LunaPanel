using LunaPanel.Core.Bindings;
using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Tests.Bindings;

/// <summary>
/// Pins <see cref="BindResolver"/> against every branch
/// <c>Fixtures/bindings/minimal.binds</c> was built for (by name), plus
/// hand-crafted XML for the branches no fixture exercises (an unknown key
/// name, a non-keyboard-only bind, a non-keyboard modifier on an otherwise
/// valid keyboard primary, and genuine primary/secondary preference when
/// both slots are independently usable). Finishes with a whole-file sweep
/// over <c>sample.binds</c> and a check of <see cref="BindResolver.LogSummary"/>.
/// </summary>
public class BindResolverTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "bindings");

    private static BindingsFile ParseFixture(string name)
    {
        var result = BindingsFile.Parse(File.ReadAllText(Path.Combine(FixturesRoot, name)));
        Assert.True(result.Success, result.Error);
        return result.File!;
    }

    private static BindingsFile ParseXml(string xml)
    {
        var result = BindingsFile.Parse(xml);
        Assert.True(result.Success, result.Error);
        return result.File!;
    }

    // ---------------------------------------------------------------
    // Every branch minimal.binds was built for, by name.
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_PrimaryKeyboardSecondaryNoDevice_ResolvesViaPrimary()
    {
        var file = ParseFixture("minimal.binds");

        var resolution = BindResolver.Resolve(file, "PrimaryKeyboardSecondaryNoDevice");

        Assert.True(resolution.IsBound);
        Assert.Equal("A", resolution.Chord!.DisplayText);
    }

    [Fact]
    public void Resolve_PrimaryNoDeviceSecondaryKeyboard_FallsBackToSecondary()
    {
        var file = ParseFixture("minimal.binds");

        var resolution = BindResolver.Resolve(file, "PrimaryNoDeviceSecondaryKeyboard");

        Assert.True(resolution.IsBound);
        Assert.Equal("B", resolution.Chord!.DisplayText);
    }

    [Fact]
    public void Resolve_PrimaryMouseSecondaryKeyboard_SkipsMousePrimary_UsesKeyboardSecondary()
    {
        var file = ParseFixture("minimal.binds");

        var resolution = BindResolver.Resolve(file, "PrimaryMouseSecondaryKeyboard");

        Assert.True(resolution.IsBound);
        Assert.Equal("C", resolution.Chord!.DisplayText);
    }

    [Fact]
    public void Resolve_FullyUnbound_BothNoDevice_ReasonIsNoDevice()
    {
        var file = ParseFixture("minimal.binds");

        var resolution = BindResolver.Resolve(file, "FullyUnbound");

        Assert.False(resolution.IsBound);
        Assert.Equal(UnboundReason.NoDevice, resolution.Reason);
    }

    [Fact]
    public void Resolve_SingleModifier_ResolvesScanCodesAndDisplayText()
    {
        var file = ParseFixture("minimal.binds");

        var resolution = BindResolver.Resolve(file, "SingleModifier");

        Assert.True(resolution.IsBound);
        var chord = resolution.Chord!;
        Assert.Equal((ushort)0x1F, chord.MainKey.ScanCode); // Key_S
        Assert.Single(chord.ModifierKeys);
        Assert.Equal((ushort)0x2A, chord.ModifierKeys[0].ScanCode); // Key_LeftShift
        Assert.Equal("Shift+S", chord.DisplayText);
    }

    [Fact]
    public void Resolve_TwoModifiers_ResolvesBothScanCodes_AndDisplayTextInCanonicalOrder()
    {
        var file = ParseFixture("minimal.binds");

        var resolution = BindResolver.Resolve(file, "TwoModifiers");

        Assert.True(resolution.IsBound);
        var chord = resolution.Chord!;
        Assert.Equal((ushort)0x39, chord.MainKey.ScanCode); // Key_Space
        Assert.Equal(2, chord.ModifierKeys.Count);
        Assert.Equal((ushort)0x1D, chord.ModifierKeys[0].ScanCode); // Key_LeftControl
        Assert.Equal((ushort)0x38, chord.ModifierKeys[1].ScanCode); // Key_LeftAlt
        Assert.Equal("Ctrl+Alt+Space", chord.DisplayText);
    }

    [Fact]
    public void Resolve_DuplicatedElement_UsesFirstOccurrence()
    {
        var file = ParseFixture("minimal.binds");

        var resolution = BindResolver.Resolve(file, "DuplicatedElement");

        Assert.True(resolution.IsBound);
        Assert.Equal((ushort)0x20, resolution.Chord!.MainKey.ScanCode); // Key_D, not Key_Z (0x2C)
    }

    [Fact]
    public void Resolve_LowercaseKeyName_ResolvesCaseInsensitively()
    {
        var file = ParseFixture("minimal.binds");

        var resolution = BindResolver.Resolve(file, "LowercaseKeyName");

        Assert.True(resolution.IsBound);
        Assert.Equal((ushort)0x2B, resolution.Chord!.MainKey.ScanCode); // Key_BackSlash
        Assert.False(resolution.Chord!.MainKey.IsExtended);
    }

    // ---------------------------------------------------------------
    // NotPresent
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_ActionNotInFile_ReasonIsNotPresent()
    {
        var file = ParseFixture("minimal.binds");

        var resolution = BindResolver.Resolve(file, "ThisActionDoesNotExist");

        Assert.False(resolution.IsBound);
        Assert.Equal(UnboundReason.NotPresent, resolution.Reason);
    }

    // ---------------------------------------------------------------
    // NonKeyboardOnly - not exercised by minimal.binds (its only mouse
    // case falls back to a keyboard secondary), so hand-crafted here.
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_BothSlotsNonKeyboard_ReasonIsNonKeyboardOnly()
    {
        var file = ParseXml("""
            <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
              <MouseOnlyAction>
                <Primary Device="Mouse" Key="Mouse_1" />
                <Secondary Device="{NoDevice}" Key="" />
              </MouseOnlyAction>
            </Root>
            """);

        var resolution = BindResolver.Resolve(file, "MouseOnlyAction");

        Assert.False(resolution.IsBound);
        Assert.Equal(UnboundReason.NonKeyboardOnly, resolution.Reason);
    }

    [Fact]
    public void Resolve_UnrecognisedJoystickDevice_IsTreatedAsNonKeyboard_NotCrash()
    {
        // The brief is explicit that joystick device names must not be
        // assumed away just because none appear on the reference machine -
        // any unrecognised device name must be treated as "not keyboard".
        var file = ParseXml("""
            <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
              <JoystickOnlyAction>
                <Primary Device="VKBGladiatorEVO" Key="Joy_1" />
                <Secondary Device="{NoDevice}" Key="" />
              </JoystickOnlyAction>
            </Root>
            """);

        var resolution = BindResolver.Resolve(file, "JoystickOnlyAction");

        Assert.False(resolution.IsBound);
        Assert.Equal(UnboundReason.NonKeyboardOnly, resolution.Reason);
    }

    // ---------------------------------------------------------------
    // UnknownKey - not exercised by any fixture (both fixtures only use
    // real Scancodes entries), so hand-crafted here.
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_KeyboardBindingNamesUnknownKey_ReasonIsUnknownKey_AndNamesTheKey()
    {
        var file = ParseXml("""
            <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
              <BadKeyAction>
                <Primary Device="Keyboard" Key="Key_ThisIsNotARealKey" />
                <Secondary Device="{NoDevice}" Key="" />
              </BadKeyAction>
            </Root>
            """);

        var resolution = BindResolver.Resolve(file, "BadKeyAction");

        Assert.False(resolution.IsBound);
        Assert.Equal(UnboundReason.UnknownKey, resolution.Reason);
        Assert.Equal("Key_ThisIsNotARealKey", resolution.ReasonDetail);
    }

    [Fact]
    public void Resolve_UnknownModifierKey_ReasonIsUnknownKey_AndNamesTheModifier()
    {
        var file = ParseXml("""
            <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
              <BadModifierAction>
                <Primary Device="Keyboard" Key="Key_S">
                  <Modifier Device="Keyboard" Key="Key_NotARealModifier" />
                </Primary>
                <Secondary Device="{NoDevice}" Key="" />
              </BadModifierAction>
            </Root>
            """);

        var resolution = BindResolver.Resolve(file, "BadModifierAction");

        Assert.False(resolution.IsBound);
        Assert.Equal(UnboundReason.UnknownKey, resolution.Reason);
        Assert.Equal("Key_NotARealModifier", resolution.ReasonDetail);
    }

    // ---------------------------------------------------------------
    // Primary/secondary preference when BOTH slots are independently
    // usable - the fixtures never put a usable binding in both slots at
    // once, so this is the dedicated pin for "prefer Primary" itself,
    // distinct from "fall back to Secondary because Primary is unusable".
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_BothSlotsIndependentlyUsable_PrefersPrimary()
    {
        var file = ParseXml("""
            <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
              <BothUsable>
                <Primary Device="Keyboard" Key="Key_A" />
                <Secondary Device="Keyboard" Key="Key_B" />
              </BothUsable>
            </Root>
            """);

        var resolution = BindResolver.Resolve(file, "BothUsable");

        Assert.True(resolution.IsBound);
        Assert.Equal("A", resolution.Chord!.DisplayText);
    }

    // ---------------------------------------------------------------
    // A non-keyboard modifier on an otherwise-valid keyboard Primary must
    // disqualify that slot (not just be ignored) - falls back to Secondary.
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_PrimaryHasKeyboardMainKeyButNonKeyboardModifier_FallsBackToSecondary()
    {
        var file = ParseXml("""
            <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
              <MixedDeviceModifier>
                <Primary Device="Keyboard" Key="Key_S">
                  <Modifier Device="Mouse" Key="Mouse_1" />
                </Primary>
                <Secondary Device="Keyboard" Key="Key_T" />
              </MixedDeviceModifier>
            </Root>
            """);

        var resolution = BindResolver.Resolve(file, "MixedDeviceModifier");

        Assert.True(resolution.IsBound);
        Assert.Equal("T", resolution.Chord!.DisplayText);
    }

    [Fact]
    public void Resolve_PrimaryModifierDeviceIsNotKeyboard_DisqualifiesPrimary_EvenIfModifierKeyNameIsValid()
    {
        // The Modifier's Key text ("Key_LeftShift") is deliberately a real
        // Scancodes entry, so this proves the DEVICE check is what
        // disqualifies the slot - not a coincidental unknown-key failure on
        // the modifier. No usable Secondary exists, so this must end up
        // unbound rather than silently accepting a non-keyboard modifier.
        var file = ParseXml("""
            <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
              <MixedDeviceModifierNoFallback>
                <Primary Device="Keyboard" Key="Key_S">
                  <Modifier Device="Mouse" Key="Key_LeftShift" />
                </Primary>
                <Secondary Device="{NoDevice}" Key="" />
              </MixedDeviceModifierNoFallback>
            </Root>
            """);

        var resolution = BindResolver.Resolve(file, "MixedDeviceModifierNoFallback");

        Assert.False(resolution.IsBound);
        Assert.Equal(UnboundReason.NonKeyboardOnly, resolution.Reason);
    }

    // ---------------------------------------------------------------
    // Whole-file sweep over sample.binds
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("FocusLeftPanel")]
    [InlineData("FocusCommsPanel")]
    [InlineData("FocusRightPanel")]
    [InlineData("CycleNextPanel")]
    [InlineData("CyclePreviousPanel")]
    [InlineData("UI_Up")]
    [InlineData("UI_Down")]
    [InlineData("UI_Left")]
    [InlineData("UI_Right")]
    [InlineData("UI_Select")]
    [InlineData("UI_Back")]
    [InlineData("LandingGearToggle")]
    [InlineData("ToggleCargoScoop")]
    [InlineData("ShipSpotLightToggle")]
    public void Resolve_SampleBindsFile_EveryStarterLayoutAction_ResolvesToAKeyboardChord(string actionName)
    {
        var file = ParseFixture("sample.binds");

        var resolution = BindResolver.Resolve(file, actionName);

        Assert.True(resolution.IsBound, $"Expected '{actionName}' to resolve to a keyboard chord, but got: {resolution.Reason} {resolution.ReasonDetail}");
    }

    // ---------------------------------------------------------------
    // LogSummary
    // ---------------------------------------------------------------

    [Fact]
    public void LogSummary_SampleBindsFile_LogsOneEventToBindsCategory_WithPresetVersionAndCounts()
    {
        var file = ParseFixture("sample.binds");
        var ring = new DiagnosticRingBuffer(10);

        BindResolver.LogSummary(file, ring);

        var events = ring.Snapshot();
        Assert.Single(events);
        var evt = events[0];

        Assert.Equal("Binds", evt.Category);
        Assert.Contains("Custom", evt.Message);
        Assert.Contains("4.2", evt.Message);
        Assert.Contains("14 elements", evt.Message);
        Assert.Contains("14 resolved", evt.Message);
        Assert.NotNull(evt.Detail);
        Assert.Contains("noDevice=0", evt.Detail);
        Assert.Contains("nonKeyboardOnly=0", evt.Detail);
        Assert.Contains("unknownKey=0", evt.Detail);
    }

    [Fact]
    public void LogSummary_MinimalBindsFile_BreaksDownUnboundByReason()
    {
        var file = ParseFixture("minimal.binds");
        var ring = new DiagnosticRingBuffer(10);

        BindResolver.LogSummary(file, ring);

        var evt = ring.Snapshot().Single();

        // minimal.binds: 8 unique elements after dedup (DuplicatedElement
        // counts once). Bound: PrimaryKeyboardSecondaryNoDevice,
        // PrimaryNoDeviceSecondaryKeyboard, PrimaryMouseSecondaryKeyboard,
        // SingleModifier, TwoModifiers, DuplicatedElement, LowercaseKeyName
        // = 7. Unbound: FullyUnbound (NoDevice) = 1.
        Assert.Contains("8 elements", evt.Message);
        Assert.Contains("7 resolved", evt.Message);
        Assert.Contains("noDevice=1", evt.Detail!);
        Assert.Contains("nonKeyboardOnly=0", evt.Detail!);
        Assert.Contains("unknownKey=0", evt.Detail!);
    }
}
