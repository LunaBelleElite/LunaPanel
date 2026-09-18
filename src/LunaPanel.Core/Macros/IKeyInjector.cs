using LunaPanel.Core.Input;

namespace LunaPanel.Core.Macros;

/// <summary>
/// Raw keyboard injection, one key event at a time. Deliberately the
/// smallest possible surface - a chord's press choreography (modifier
/// settle gap, hold duration, release in reverse order - see
/// <see cref="MacroTimingDefaults"/>) lives in <see cref="MacroRunner"/>,
/// not here, so that choreography stays covered by a Core test against a
/// recording fake instead of being duplicated inside every implementation
/// of this interface.
///
/// <b>No implementation of this interface lives in LunaPanel.Core.</b> Real
/// key injection needs P/Invoke into Win32 <c>SendInput</c>, which is
/// <c>LunaPanel.Server</c>'s job - Core stays testable with no game and no
/// Windows input by depending only on this interface and a recording fake.
/// </summary>
public interface IKeyInjector
{
    void KeyDown(ScancodeInfo key);

    void KeyUp(ScancodeInfo key);
}
