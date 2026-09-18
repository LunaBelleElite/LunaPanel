using LunaPanel.Core.Macros;

namespace LunaPanel.Server.Macros;

/// <summary>
/// Every macro that currently exists - the ones this build ships, plus the
/// ones the commander authored - as one list, re-read on every call.
///
/// <b>Why this exists at all.</b> <see cref="MacroLoader.LoadShipped"/> is
/// called once at startup and the panel/press handlers used to close over
/// its result. That is correct for embedded resources, which cannot change
/// while the host runs, and exactly wrong for user macros, which change
/// <em>because a commander just saved one</em>: closing over a startup list
/// would mean a macro authored on the device did not fire until LunaPanel
/// was restarted, which is the "no file editing, no restart" promise
/// (<c>ref/docs/editor.md</c>) broken in the one place it was most obvious.
/// <see cref="All"/> therefore re-reads the user store every time - the same
/// "recompute on every read" discipline <c>MacroKnowledgeBuilder</c> already
/// follows for bindings, and the same one <c>LayoutAccess.LoadOrSeed</c>
/// follows for the layout file itself on every panel request.
///
/// <b>Shipped wins a collision.</b> A user file whose id matches a shipped
/// macro cannot be produced by this codebase - ids are minted under a
/// reserved prefix no shipped id uses (<see cref="UserMacroIds"/>) - so a
/// collision means a hand-placed file. The shipped definition is kept and
/// the user one ignored, rather than letting a dropped-in file silently
/// shadow a measured macro that a layout slot already names.
/// </summary>
public sealed class MacroCatalogue
{
    private readonly IReadOnlyList<MacroDefinition> _shipped;

    public MacroCatalogue(IReadOnlyList<MacroDefinition> shipped, UserMacroStore userMacros)
    {
        _shipped = shipped ?? throw new ArgumentNullException(nameof(shipped));
        UserMacros = userMacros ?? throw new ArgumentNullException(nameof(userMacros));
        ShippedIds = _shipped.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The store the user half comes from - the save/delete verbs' own target.</summary>
    public UserMacroStore UserMacros { get; }

    /// <summary>
    /// Which ids are read-only. A shipped macro is never edited in place:
    /// copy-to-edit is the ruling (<c>ref/docs/macro-builder.md</c>,
    /// question 3), because a user override of a shipped id would either
    /// shadow a later fix silently or need merge rules nobody has written.
    /// </summary>
    public IReadOnlySet<string> ShippedIds { get; }

    public IReadOnlyList<MacroDefinition> All()
    {
        var all = new List<MacroDefinition>(_shipped);

        foreach (var userMacro in UserMacros.LoadAll())
        {
            if (!ShippedIds.Contains(userMacro.Id))
            {
                all.Add(userMacro);
            }
        }

        return all;
    }

    public MacroDefinition? Find(string id) => All().FirstOrDefault(m => m.Id == id);

    public bool IsShipped(string id) => ShippedIds.Contains(id);
}
