namespace LunaPanel.Core.Input;

/// <summary>
/// What Win32 <c>SendInput</c> needs to press one key: the low-order scan
/// code byte to put in <c>wScan</c>, and whether the caller must also set
/// <c>KEYEVENTF_EXTENDEDKEY</c>. DirectInput's own DIK_* constants fold
/// "extended" into the value itself (anything &gt;= 0x80), but <c>SendInput</c>
/// takes only the low 7 bits of that plus this separate flag - never the raw
/// DIK value.
/// </summary>
public readonly record struct ScancodeInfo(ushort ScanCode, bool IsExtended);
