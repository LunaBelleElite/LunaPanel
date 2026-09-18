using LunaPanel.Core.Input;

namespace LunaPanel.Tests.Input;

/// <summary>
/// Pins <see cref="BrowserKeyCodeMap"/>'s DOM <c>KeyboardEvent.code</c> -&gt;
/// <see cref="Scancodes"/> <c>Key_*</c> translation used by the "press a key"
/// macro builder's keypress-capture picker.
/// </summary>
public class BrowserKeyCodeMapTests
{
    /// <summary>
    /// A sweep over every mapped target, not a spot check - proves the whole
    /// table points at real <see cref="Scancodes"/> entries rather than at a
    /// name someone typed from memory, the same guard-test pattern
    /// <c>ScancodesTests</c> uses for its own parallel table.
    /// </summary>
    [Fact]
    public void AllMappedTargets_ExistInScancodes()
    {
        foreach (var (domCode, keyName) in AllMappings())
        {
            Assert.True(Scancodes.TryGet(keyName, out _),
                $"'{domCode}' maps to '{keyName}', which is not a real Scancodes entry.");
        }
    }

    private static IEnumerable<(string DomCode, string KeyName)> AllMappings()
    {
        // Re-derive the input set from the DOM codes exercised by the
        // positive-case theory below plus a broader sweep pulled straight
        // off Scancodes.All's own keys is not possible in the other
        // direction (this table is DOM code -> Key_*, not the reverse), so
        // the sweep instead walks every DOM code this class claims to
        // support and asks TryMap for each.
        foreach (var domCode in KnownDomCodes)
        {
            if (BrowserKeyCodeMap.TryMap(domCode, out var keyName))
            {
                yield return (domCode, keyName);
            }
        }
    }

    // Every DOM code named in the brief this class implements - not just the
    // ones with an assertion below - so the sweep above actually walks the
    // whole vocabulary rather than only the handful spot-checked by name.
    private static readonly string[] KnownDomCodes =
    [
        "KeyA", "KeyB", "KeyC", "KeyD", "KeyE", "KeyF", "KeyG", "KeyH", "KeyI", "KeyJ",
        "KeyK", "KeyL", "KeyM", "KeyN", "KeyO", "KeyP", "KeyQ", "KeyR", "KeyS", "KeyT",
        "KeyU", "KeyV", "KeyW", "KeyX", "KeyY", "KeyZ",
        "Digit0", "Digit1", "Digit2", "Digit3", "Digit4", "Digit5", "Digit6", "Digit7", "Digit8", "Digit9",
        "Numpad0", "Numpad1", "Numpad2", "Numpad3", "Numpad4", "Numpad5", "Numpad6", "Numpad7", "Numpad8", "Numpad9",
        "NumpadAdd", "NumpadSubtract", "NumpadMultiply", "NumpadDivide", "NumpadDecimal", "NumpadEnter",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "F13", "F14", "F15",
        "F16", "F17", "F18", "F19", "F20", "F21", "F22", "F23", "F24",
        "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight",
        "ShiftLeft", "ShiftRight", "ControlLeft", "ControlRight", "AltLeft", "AltRight", "MetaLeft", "MetaRight",
        "Escape", "Tab", "Space", "Enter", "Backspace", "Delete", "Insert", "Home", "End", "PageUp", "PageDown",
        "CapsLock", "NumLock", "ScrollLock", "PrintScreen", "Pause", "ContextMenu",
        "Minus", "Equal", "BracketLeft", "BracketRight", "Backslash", "Semicolon", "Quote", "Backquote",
        "Comma", "Period", "Slash", "IntlBackslash",
        "AudioVolumeUp", "AudioVolumeDown", "AudioVolumeMute", "MediaTrackNext", "MediaTrackPrevious",
        "MediaPlayPause", "MediaStop", "BrowserHome", "BrowserBack", "BrowserForward", "BrowserRefresh",
        "BrowserSearch", "BrowserFavorites",
    ];

    [Theory]
    [InlineData("KeyW", "Key_W")]
    [InlineData("Digit1", "Key_1")]
    [InlineData("ArrowUp", "Key_UpArrow")]
    [InlineData("F5", "Key_F5")]
    [InlineData("ShiftLeft", "Key_LeftShift")]
    [InlineData("Comma", "Key_Comma")]
    [InlineData("BrowserHome", "Key_WebHome")]
    [InlineData("Numpad0", "Key_Numpad_0")]
    public void TryMap_KnownDomCode_ReturnsExpectedKeyName(string domCode, string expectedKeyName)
    {
        var found = BrowserKeyCodeMap.TryMap(domCode, out var keyName);

        Assert.True(found, $"Expected '{domCode}' to map.");
        Assert.Equal(expectedKeyName, keyName);
    }

    [Theory]
    [InlineData("ThisIsNotARealDomCode")]
    [InlineData("F16")]
    [InlineData("")]
    public void TryMap_UnrecognizedDomCode_ReturnsFalse(string domCode)
    {
        var found = BrowserKeyCodeMap.TryMap(domCode, out var keyName);

        Assert.False(found);
        Assert.Equal(string.Empty, keyName);
    }
}
