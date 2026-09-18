namespace LunaPanel.Core.Input;

/// <summary>
/// Looks up the Win32 <c>SendInput</c> scan code for one of Elite
/// Dangerous's own Key_* binding names, as written in a player's binds
/// file (e.g. <c>Key_UpArrow</c>). Pure data plus a case-insensitive
/// lookup - no interop lives here, that belongs to the process that
/// actually calls <c>SendInput</c>.
///
/// Frontier's Key_* names line up, in order, with DirectInput's own
/// DIK_* constants. This table is transcribed from that constant list
/// (Microsoft's dinput.h) and Frontier's own Key_* vocabulary
/// (ControlSchemes/Help.txt, shipped with the game), and is hardcoded here
/// rather than read from either at run time - <c>LunaPanel.Core</c> does not
/// depend on the game being installed, and the DIK_* values are fixed,
/// documented Win32/DirectInput interop constants, not third-party content.
///
/// Every value below is already the low 7 bits SendInput's wScan field
/// takes; DIK_*'s own >= 0x80 "extended" encoding is folded into the
/// IsExtended flag instead of being stored directly (see
/// <see cref="ScancodeInfo"/>).
///
/// Two of Frontier's Key_* names are deliberately absent:
/// Key_GreenModifier and Key_OrangeModifier are virtual chord modifiers
/// Frontier invented for its own bindings UI and correspond to no physical
/// key or scan code at all, so there is nothing for SendInput to press.
/// </summary>
public static class Scancodes
{
    private static readonly Dictionary<string, ScancodeInfo> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Key_Escape"] = new ScancodeInfo(0x01, false),
        ["Key_1"] = new ScancodeInfo(0x02, false),
        ["Key_2"] = new ScancodeInfo(0x03, false),
        ["Key_3"] = new ScancodeInfo(0x04, false),
        ["Key_4"] = new ScancodeInfo(0x05, false),
        ["Key_5"] = new ScancodeInfo(0x06, false),
        ["Key_6"] = new ScancodeInfo(0x07, false),
        ["Key_7"] = new ScancodeInfo(0x08, false),
        ["Key_8"] = new ScancodeInfo(0x09, false),
        ["Key_9"] = new ScancodeInfo(0x0A, false),
        ["Key_0"] = new ScancodeInfo(0x0B, false),
        ["Key_Minus"] = new ScancodeInfo(0x0C, false),
        ["Key_Equals"] = new ScancodeInfo(0x0D, false),
        ["Key_Backspace"] = new ScancodeInfo(0x0E, false),
        ["Key_Tab"] = new ScancodeInfo(0x0F, false),
        ["Key_Q"] = new ScancodeInfo(0x10, false),
        ["Key_W"] = new ScancodeInfo(0x11, false),
        ["Key_E"] = new ScancodeInfo(0x12, false),
        ["Key_R"] = new ScancodeInfo(0x13, false),
        ["Key_T"] = new ScancodeInfo(0x14, false),
        ["Key_Y"] = new ScancodeInfo(0x15, false),
        ["Key_U"] = new ScancodeInfo(0x16, false),
        ["Key_I"] = new ScancodeInfo(0x17, false),
        ["Key_O"] = new ScancodeInfo(0x18, false),
        ["Key_P"] = new ScancodeInfo(0x19, false),
        ["Key_LeftBracket"] = new ScancodeInfo(0x1A, false),
        ["Key_RightBracket"] = new ScancodeInfo(0x1B, false),
        ["Key_Enter"] = new ScancodeInfo(0x1C, false),
        ["Key_LeftControl"] = new ScancodeInfo(0x1D, false),
        ["Key_A"] = new ScancodeInfo(0x1E, false),
        ["Key_S"] = new ScancodeInfo(0x1F, false),
        ["Key_D"] = new ScancodeInfo(0x20, false),
        ["Key_F"] = new ScancodeInfo(0x21, false),
        ["Key_G"] = new ScancodeInfo(0x22, false),
        ["Key_H"] = new ScancodeInfo(0x23, false),
        ["Key_J"] = new ScancodeInfo(0x24, false),
        ["Key_K"] = new ScancodeInfo(0x25, false),
        ["Key_L"] = new ScancodeInfo(0x26, false),
        ["Key_SemiColon"] = new ScancodeInfo(0x27, false),
        ["Key_Apostrophe"] = new ScancodeInfo(0x28, false),
        ["Key_Grave"] = new ScancodeInfo(0x29, false),
        ["Key_LeftShift"] = new ScancodeInfo(0x2A, false),
        ["Key_BackSlash"] = new ScancodeInfo(0x2B, false),
        ["Key_Z"] = new ScancodeInfo(0x2C, false),
        ["Key_X"] = new ScancodeInfo(0x2D, false),
        ["Key_C"] = new ScancodeInfo(0x2E, false),
        ["Key_V"] = new ScancodeInfo(0x2F, false),
        ["Key_B"] = new ScancodeInfo(0x30, false),
        ["Key_N"] = new ScancodeInfo(0x31, false),
        ["Key_M"] = new ScancodeInfo(0x32, false),
        ["Key_Comma"] = new ScancodeInfo(0x33, false),
        ["Key_Period"] = new ScancodeInfo(0x34, false),
        ["Key_Slash"] = new ScancodeInfo(0x35, false),
        ["Key_RightShift"] = new ScancodeInfo(0x36, false),
        ["Key_Numpad_Multiply"] = new ScancodeInfo(0x37, false),
        ["Key_LeftAlt"] = new ScancodeInfo(0x38, false),
        ["Key_Space"] = new ScancodeInfo(0x39, false),
        ["Key_CapsLock"] = new ScancodeInfo(0x3A, false),
        ["Key_F1"] = new ScancodeInfo(0x3B, false),
        ["Key_F2"] = new ScancodeInfo(0x3C, false),
        ["Key_F3"] = new ScancodeInfo(0x3D, false),
        ["Key_F4"] = new ScancodeInfo(0x3E, false),
        ["Key_F5"] = new ScancodeInfo(0x3F, false),
        ["Key_F6"] = new ScancodeInfo(0x40, false),
        ["Key_F7"] = new ScancodeInfo(0x41, false),
        ["Key_F8"] = new ScancodeInfo(0x42, false),
        ["Key_F9"] = new ScancodeInfo(0x43, false),
        ["Key_F10"] = new ScancodeInfo(0x44, false),
        ["Key_NumLock"] = new ScancodeInfo(0x45, false),
        ["Key_ScrollLock"] = new ScancodeInfo(0x46, false),
        ["Key_Numpad_7"] = new ScancodeInfo(0x47, false),
        ["Key_Numpad_8"] = new ScancodeInfo(0x48, false),
        ["Key_Numpad_9"] = new ScancodeInfo(0x49, false),
        ["Key_Numpad_Subtract"] = new ScancodeInfo(0x4A, false),
        ["Key_Numpad_4"] = new ScancodeInfo(0x4B, false),
        ["Key_Numpad_5"] = new ScancodeInfo(0x4C, false),
        ["Key_Numpad_6"] = new ScancodeInfo(0x4D, false),
        ["Key_Numpad_Add"] = new ScancodeInfo(0x4E, false),
        ["Key_Numpad_1"] = new ScancodeInfo(0x4F, false),
        ["Key_Numpad_2"] = new ScancodeInfo(0x50, false),
        ["Key_Numpad_3"] = new ScancodeInfo(0x51, false),
        ["Key_Numpad_0"] = new ScancodeInfo(0x52, false),
        ["Key_Numpad_Decimal"] = new ScancodeInfo(0x53, false),
        ["Key_OEM_102"] = new ScancodeInfo(0x56, false),
        ["Key_F11"] = new ScancodeInfo(0x57, false),
        ["Key_F12"] = new ScancodeInfo(0x58, false),
        ["Key_F13"] = new ScancodeInfo(0x64, false),
        ["Key_F14"] = new ScancodeInfo(0x65, false),
        ["Key_F15"] = new ScancodeInfo(0x66, false),
        ["Key_Kana"] = new ScancodeInfo(0x70, false),
        ["Key_ABNT_C1"] = new ScancodeInfo(0x73, false),
        ["Key_Convert"] = new ScancodeInfo(0x79, false),
        ["Key_NoConvert"] = new ScancodeInfo(0x7B, false),
        ["Key_Yen"] = new ScancodeInfo(0x7D, false),
        ["Key_ABNT_C2"] = new ScancodeInfo(0x7E, false),

        // From here on, DIK_* itself is >= 0x80: the low 7 bits go in
        // ScanCode and the high bit becomes IsExtended = true instead.
        ["Key_Numpad_Equals"] = new ScancodeInfo(0x0D, true),
        ["Key_PrevTrack"] = new ScancodeInfo(0x10, true),
        ["Key_AT"] = new ScancodeInfo(0x11, true),
        ["Key_Colon"] = new ScancodeInfo(0x12, true),
        ["Key_Underline"] = new ScancodeInfo(0x13, true),
        ["Key_Kanji"] = new ScancodeInfo(0x14, true),
        ["Key_Stop"] = new ScancodeInfo(0x15, true),
        ["Key_AX"] = new ScancodeInfo(0x16, true),
        ["Key_Unlabeled"] = new ScancodeInfo(0x17, true),
        ["Key_NextTrack"] = new ScancodeInfo(0x19, true),
        ["Key_Numpad_Enter"] = new ScancodeInfo(0x1C, true),
        ["Key_RightControl"] = new ScancodeInfo(0x1D, true),
        ["Key_Mute"] = new ScancodeInfo(0x20, true),
        ["Key_Calculator"] = new ScancodeInfo(0x21, true),
        ["Key_PlayPause"] = new ScancodeInfo(0x22, true),
        ["Key_MediaStop"] = new ScancodeInfo(0x24, true),
        ["Key_VolumeDown"] = new ScancodeInfo(0x2E, true),
        ["Key_VolumeUp"] = new ScancodeInfo(0x30, true),
        ["Key_WebHome"] = new ScancodeInfo(0x32, true),
        ["Key_Numpad_Comma"] = new ScancodeInfo(0x33, true),
        ["Key_Numpad_Divide"] = new ScancodeInfo(0x35, true),
        ["Key_SYSRQ"] = new ScancodeInfo(0x37, true),
        ["Key_RightAlt"] = new ScancodeInfo(0x38, true),
        ["Key_Pause"] = new ScancodeInfo(0x45, true),
        ["Key_Home"] = new ScancodeInfo(0x47, true),
        ["Key_UpArrow"] = new ScancodeInfo(0x48, true),
        ["Key_PageUp"] = new ScancodeInfo(0x49, true),
        ["Key_LeftArrow"] = new ScancodeInfo(0x4B, true),
        ["Key_RightArrow"] = new ScancodeInfo(0x4D, true),
        ["Key_End"] = new ScancodeInfo(0x4F, true),
        ["Key_DownArrow"] = new ScancodeInfo(0x50, true),
        ["Key_PageDown"] = new ScancodeInfo(0x51, true),
        ["Key_Insert"] = new ScancodeInfo(0x52, true),
        ["Key_Delete"] = new ScancodeInfo(0x53, true),
        ["Key_LeftWin"] = new ScancodeInfo(0x5B, true),
        ["Key_RightWin"] = new ScancodeInfo(0x5C, true),
        ["Key_Apps"] = new ScancodeInfo(0x5D, true),
        ["Key_Power"] = new ScancodeInfo(0x5E, true),
        ["Key_Sleep"] = new ScancodeInfo(0x5F, true),
        ["Key_Wake"] = new ScancodeInfo(0x63, true),
        ["Key_WebSearch"] = new ScancodeInfo(0x65, true),
        ["Key_WebFavourites"] = new ScancodeInfo(0x66, true),
        ["Key_WebRefresh"] = new ScancodeInfo(0x67, true),
        ["Key_WebStop"] = new ScancodeInfo(0x68, true),
        ["Key_WebForward"] = new ScancodeInfo(0x69, true),
        ["Key_WebBack"] = new ScancodeInfo(0x6A, true),
        ["Key_MyComputer"] = new ScancodeInfo(0x6B, true),
        ["Key_Mail"] = new ScancodeInfo(0x6C, true),
        ["Key_MediaSelect"] = new ScancodeInfo(0x6D, true),
    };

    /// <summary>
    /// The full table, keyed by Frontier's canonical Key_* spelling
    /// (lookup itself is case-insensitive via <see cref="TryGet"/>).
    /// </summary>
    public static IReadOnlyDictionary<string, ScancodeInfo> All => Table;

    /// <summary>
    /// Resolves a Key_* binding name (case-insensitive, ordinal) to what
    /// SendInput needs. Returns false for a name not in the table rather
    /// than throwing - an unrecognised key name is data to report to the
    /// user, not a program error.
    /// </summary>
    public static bool TryGet(string keyName, out ScancodeInfo info)
    {
        return Table.TryGetValue(keyName, out info);
    }
}
