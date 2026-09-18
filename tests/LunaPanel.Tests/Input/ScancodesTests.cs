using LunaPanel.Core.Input;

namespace LunaPanel.Tests.Input;

/// <summary>
/// Pins <see cref="Scancodes"/>' Key_* -> SendInput scan-code lookup,
/// including the trap where DirectInput's own &gt;= 0x80 "extended" encoding
/// must never be written straight into wScan: SendInput takes the low 7
/// bits plus a separate extended-key flag instead.
/// </summary>
public class ScancodesTests
{
    [Theory]
    [InlineData("Key_Escape", (ushort)0x01)]
    [InlineData("Key_1", (ushort)0x02)]
    [InlineData("Key_W", (ushort)0x11)]
    [InlineData("Key_LeftShift", (ushort)0x2A)]
    [InlineData("Key_Space", (ushort)0x39)]
    public void TryGet_OrdinaryKeys_ReturnsExpectedScanCode_NotExtended(string keyName, ushort expectedScanCode)
    {
        var found = Scancodes.TryGet(keyName, out var info);

        Assert.True(found, $"Expected '{keyName}' to resolve.");
        Assert.Equal(expectedScanCode, info.ScanCode);
        Assert.False(info.IsExtended);
    }

    [Theory]
    [InlineData("Key_UpArrow", (ushort)0x48)]
    [InlineData("Key_LeftArrow", (ushort)0x4B)]
    [InlineData("Key_RightArrow", (ushort)0x4D)]
    [InlineData("Key_DownArrow", (ushort)0x50)]
    [InlineData("Key_RightControl", (ushort)0x1D)]
    [InlineData("Key_RightAlt", (ushort)0x38)]
    [InlineData("Key_Numpad_Enter", (ushort)0x1C)]
    [InlineData("Key_Numpad_Divide", (ushort)0x35)]
    public void TryGet_ExtendedKeys_ReturnsLowByteScanCode_AndExtendedFlag(string keyName, ushort expectedScanCode)
    {
        var found = Scancodes.TryGet(keyName, out var info);

        Assert.True(found, $"Expected '{keyName}' to resolve.");
        Assert.Equal(expectedScanCode, info.ScanCode);
        Assert.True(info.IsExtended, $"'{keyName}' must set the extended flag - its DIK_* value is >= 0x80.");
    }

    // Contrast pins: each of these shares its low-byte scan code with an
    // extended twin above, and must NOT be extended - proving the flag
    // (not the numeric value) is what actually distinguishes them.
    [Theory]
    [InlineData("Key_LeftControl", (ushort)0x1D)]
    [InlineData("Key_LeftAlt", (ushort)0x38)]
    [InlineData("Key_Enter", (ushort)0x1C)]
    public void TryGet_NonExtendedTwins_ShareScanCodeButAreNotExtended(string keyName, ushort expectedScanCode)
    {
        var found = Scancodes.TryGet(keyName, out var info);

        Assert.True(found, $"Expected '{keyName}' to resolve.");
        Assert.Equal(expectedScanCode, info.ScanCode);
        Assert.False(info.IsExtended);
    }

    [Theory]
    [InlineData("Key_backslash")]
    [InlineData("KEY_BACKSLASH")]
    [InlineData("Key_BackSlash")]
    public void TryGet_IsCaseInsensitive_AllSpellingsResolveIdentically(string keyName)
    {
        var found = Scancodes.TryGet(keyName, out var info);

        Assert.True(found, $"Expected '{keyName}' to resolve.");
        Assert.Equal((ushort)0x2B, info.ScanCode);
        Assert.False(info.IsExtended);
    }

    [Fact]
    public void TryGet_UnknownKeyName_ReturnsFalse_AndDoesNotThrow()
    {
        var found = Scancodes.TryGet("Key_ThisIsNotARealKey", out var info);

        Assert.False(found);
        Assert.Equal(default, info);
    }

    [Fact]
    public void AllEntries_NoScanCodeIsGreaterThanOrEqualTo0x80()
    {
        // A single sweep over the whole table, not a spot check: this is
        // the one assertion that catches a raw (unmasked) DIK_* value being
        // written into any entry, across the entire vocabulary at once.
        var violations = Scancodes.All
            .Where(entry => entry.Value.ScanCode >= 0x80)
            .Select(entry => $"{entry.Key} = 0x{entry.Value.ScanCode:X2}")
            .ToList();

        Assert.True(violations.Count == 0,
            "Entries below must never hold a raw DIK_* value (>= 0x80) - SendInput's wScan " +
            "takes only the low 7 bits plus KEYEVENTF_EXTENDEDKEY:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void AllEntries_CoversAtLeastTheFullFrontierKeyboardVocabulary()
    {
        // 144 is every keyboard Key_* name Frontier's own ControlSchemes
        // Help.txt lists (Key_Escape through Key_MediaSelect), excluding
        // the two virtual chord modifiers (GreenModifier/OrangeModifier)
        // that have no physical scan code at all.
        Assert.True(Scancodes.All.Count >= 144,
            $"Expected at least 144 entries, found {Scancodes.All.Count}.");
    }
}
