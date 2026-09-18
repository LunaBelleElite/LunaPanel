namespace LunaPanel.Core.Input;

/// <summary>
/// Translates a browser <c>KeyboardEvent.code</c> value (the standard,
/// layout-independent DOM "code" string - e.g. <c>"KeyW"</c>,
/// <c>"Digit1"</c>, <c>"ArrowUp"</c>, <c>"ShiftLeft"</c>) into this
/// project's own <see cref="Scancodes"/> <c>Key_*</c> naming, for the
/// "press a key" macro builder's real keypress-capture picker
/// (<c>ref/docs/macros.md</c>).
///
/// Only DOM codes with a real, existing <see cref="Scancodes"/> entry are
/// mapped here - a DOM code with no plausible <c>Key_*</c> counterpart
/// (e.g. <c>F16</c>-<c>F24</c>, which <see cref="Scancodes"/> does not
/// carry at all) is simply absent from the table rather than guessed at.
/// </summary>
public static class BrowserKeyCodeMap
{
    private static readonly Dictionary<string, string> Table = new(StringComparer.Ordinal)
    {
        // Letters
        ["KeyA"] = "Key_A",
        ["KeyB"] = "Key_B",
        ["KeyC"] = "Key_C",
        ["KeyD"] = "Key_D",
        ["KeyE"] = "Key_E",
        ["KeyF"] = "Key_F",
        ["KeyG"] = "Key_G",
        ["KeyH"] = "Key_H",
        ["KeyI"] = "Key_I",
        ["KeyJ"] = "Key_J",
        ["KeyK"] = "Key_K",
        ["KeyL"] = "Key_L",
        ["KeyM"] = "Key_M",
        ["KeyN"] = "Key_N",
        ["KeyO"] = "Key_O",
        ["KeyP"] = "Key_P",
        ["KeyQ"] = "Key_Q",
        ["KeyR"] = "Key_R",
        ["KeyS"] = "Key_S",
        ["KeyT"] = "Key_T",
        ["KeyU"] = "Key_U",
        ["KeyV"] = "Key_V",
        ["KeyW"] = "Key_W",
        ["KeyX"] = "Key_X",
        ["KeyY"] = "Key_Y",
        ["KeyZ"] = "Key_Z",

        // Digits (top row)
        ["Digit0"] = "Key_0",
        ["Digit1"] = "Key_1",
        ["Digit2"] = "Key_2",
        ["Digit3"] = "Key_3",
        ["Digit4"] = "Key_4",
        ["Digit5"] = "Key_5",
        ["Digit6"] = "Key_6",
        ["Digit7"] = "Key_7",
        ["Digit8"] = "Key_8",
        ["Digit9"] = "Key_9",

        // Numpad
        ["Numpad0"] = "Key_Numpad_0",
        ["Numpad1"] = "Key_Numpad_1",
        ["Numpad2"] = "Key_Numpad_2",
        ["Numpad3"] = "Key_Numpad_3",
        ["Numpad4"] = "Key_Numpad_4",
        ["Numpad5"] = "Key_Numpad_5",
        ["Numpad6"] = "Key_Numpad_6",
        ["Numpad7"] = "Key_Numpad_7",
        ["Numpad8"] = "Key_Numpad_8",
        ["Numpad9"] = "Key_Numpad_9",
        ["NumpadAdd"] = "Key_Numpad_Add",
        ["NumpadSubtract"] = "Key_Numpad_Subtract",
        ["NumpadMultiply"] = "Key_Numpad_Multiply",
        ["NumpadDivide"] = "Key_Numpad_Divide",
        ["NumpadDecimal"] = "Key_Numpad_Decimal",
        ["NumpadEnter"] = "Key_Numpad_Enter",

        // Function keys - only F1-F15 exist in Scancodes; F16-F24 have no
        // Key_* counterpart at all and are deliberately left unmapped.
        ["F1"] = "Key_F1",
        ["F2"] = "Key_F2",
        ["F3"] = "Key_F3",
        ["F4"] = "Key_F4",
        ["F5"] = "Key_F5",
        ["F6"] = "Key_F6",
        ["F7"] = "Key_F7",
        ["F8"] = "Key_F8",
        ["F9"] = "Key_F9",
        ["F10"] = "Key_F10",
        ["F11"] = "Key_F11",
        ["F12"] = "Key_F12",
        ["F13"] = "Key_F13",
        ["F14"] = "Key_F14",
        ["F15"] = "Key_F15",

        // Arrows
        ["ArrowUp"] = "Key_UpArrow",
        ["ArrowDown"] = "Key_DownArrow",
        ["ArrowLeft"] = "Key_LeftArrow",
        ["ArrowRight"] = "Key_RightArrow",

        // Modifiers
        ["ShiftLeft"] = "Key_LeftShift",
        ["ShiftRight"] = "Key_RightShift",
        ["ControlLeft"] = "Key_LeftControl",
        ["ControlRight"] = "Key_RightControl",
        ["AltLeft"] = "Key_LeftAlt",
        ["AltRight"] = "Key_RightAlt",
        ["MetaLeft"] = "Key_LeftWin",
        ["MetaRight"] = "Key_RightWin",

        // Whitespace/editing/navigation
        ["Escape"] = "Key_Escape",
        ["Tab"] = "Key_Tab",
        ["Space"] = "Key_Space",
        ["Enter"] = "Key_Enter",
        ["Backspace"] = "Key_Backspace",
        ["Delete"] = "Key_Delete",
        ["Insert"] = "Key_Insert",
        ["Home"] = "Key_Home",
        ["End"] = "Key_End",
        ["PageUp"] = "Key_PageUp",
        ["PageDown"] = "Key_PageDown",
        ["CapsLock"] = "Key_CapsLock",
        ["NumLock"] = "Key_NumLock",
        ["ScrollLock"] = "Key_ScrollLock",
        ["PrintScreen"] = "Key_SYSRQ",
        ["Pause"] = "Key_Pause",
        ["ContextMenu"] = "Key_Apps",

        // Punctuation
        ["Minus"] = "Key_Minus",
        ["Equal"] = "Key_Equals",
        ["BracketLeft"] = "Key_LeftBracket",
        ["BracketRight"] = "Key_RightBracket",
        ["Backslash"] = "Key_BackSlash",
        ["Semicolon"] = "Key_SemiColon",
        ["Quote"] = "Key_Apostrophe",
        ["Backquote"] = "Key_Grave",
        ["Comma"] = "Key_Comma",
        ["Period"] = "Key_Period",
        ["Slash"] = "Key_Slash",
        ["IntlBackslash"] = "Key_OEM_102",

        // Media / browser
        ["AudioVolumeUp"] = "Key_VolumeUp",
        ["AudioVolumeDown"] = "Key_VolumeDown",
        ["AudioVolumeMute"] = "Key_Mute",
        ["MediaTrackNext"] = "Key_NextTrack",
        ["MediaTrackPrevious"] = "Key_PrevTrack",
        ["MediaPlayPause"] = "Key_PlayPause",
        ["MediaStop"] = "Key_MediaStop",
        ["BrowserHome"] = "Key_WebHome",
        ["BrowserBack"] = "Key_WebBack",
        ["BrowserForward"] = "Key_WebForward",
        ["BrowserRefresh"] = "Key_WebRefresh",
        ["BrowserSearch"] = "Key_WebSearch",
        ["BrowserFavorites"] = "Key_WebFavourites",
    };

    /// <summary>
    /// Resolves a DOM <c>KeyboardEvent.code</c> string (exact spelling,
    /// case-sensitive per the DOM spec) to its <see cref="Scancodes"/>
    /// <c>Key_*</c> name. Returns <see langword="false"/> for a code this
    /// table does not carry, rather than throwing - an unrecognised code is
    /// data to report back to the caller, not a program error.
    /// </summary>
    public static bool TryMap(string domCode, out string keyName)
    {
        if (domCode is null || !Table.TryGetValue(domCode, out var found))
        {
            keyName = string.Empty;
            return false;
        }

        keyName = found;
        return true;
    }
}
