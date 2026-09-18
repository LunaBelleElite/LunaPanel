namespace LunaPanel.Server.Http;

/// <summary>
/// Which routes may only be called from the machine LunaPanel is running on
/// (<see cref="HostRequest"/>), and the one sentence a device is refused
/// with.
///
/// <b>The line is authoring, not macros.</b> Everything that <em>creates,
/// changes or destroys</em> a macro is host-only; everything that merely
/// <em>reads</em> the macro set is not. So a tablet still lists macros, still
/// sees which are degraded, still puts one on a button and still fires it -
/// <c>GET</c> <see cref="ApiPaths.Macros"/> and
/// <see cref="ApiPaths.MacrosVocabulary"/> are ordinary device routes - while
/// <c>POST</c> <see cref="ApiPaths.Macros"/>,
/// <see cref="ApiPaths.MacrosCopy"/> and <see cref="ApiPaths.MacrosDelete"/>
/// are refused. That boundary is the one worth enforcing: it is exactly the
/// set of calls that can change what every device's buttons do.
/// <see cref="ApiPaths.MacrosVocabulary"/> in particular is a constant table
/// of flag and event names read off <c>StatusVocabulary</c>/
/// <c>JournalVocabulary</c>; refusing it would hide the builder's pickers
/// from a device that already cannot save anything, which is UI-hiding
/// dressed as a security rule.
///
/// The client hides the authoring buttons on a device
/// (<c>PanelClientEndpoint</c>'s <c>CAN_AUTHOR_MACROS</c>), but that is a
/// courtesy and this is the enforcement - the same division
/// <c>SlotEditEndpoint.SetLatch</c>'s refusal already has with the slot
/// sheet's absent latch button. A device that calls the route directly is
/// refused here.
///
/// <b>The transfer routes are the exception to "the line is authoring", and
/// it is a deliberate one</b> (<c>ref/docs/transfer.md</c>, 2026-09-10).
/// <see cref="ApiPaths.TransferTargets"/>,
/// <see cref="ApiPaths.TransferProfile"/>,
/// <see cref="ApiPaths.TransferMacro"/> and
/// <see cref="ApiPaths.TransferUndo"/> are host-only under <em>every</em>
/// method, reading included. The reasoning that kept <c>GET</c>
/// <see cref="ApiPaths.Macros"/> open does not carry: a tablet lists macros
/// because that is how one gets onto a button, whereas nothing a tablet does
/// needs a file - and a <c>GET</c> here hands out a commander's entire
/// arrangement, and the list of every device in the household by name.
/// </summary>
public static class HostOnlyRoutes
{
    /// <summary>
    /// What a paired device is told when it calls an authoring route. Names
    /// the tray item that actually opens the builder, for the same reason
    /// the pairing screen names the tray rather than the console: a refusal
    /// that does not say where the thing lives is a dead end.
    /// </summary>
    public const string NotTheHostAdvice =
        "Macros are built on the PC running LunaPanel. Open the LunaPanel tray icon there and choose \"Build a macro\".";

    /// <summary>
    /// What a paired device is told when it calls a transfer route. Names
    /// its own tray item for the same reason
    /// <see cref="NotTheHostAdvice"/> names the builder's: a refusal that
    /// does not say where the thing lives is a dead end. A separate sentence
    /// rather than a reworded shared one, because sending a commander who
    /// wanted to export a profile to "Build a macro" would be worse than
    /// saying nothing.
    /// </summary>
    public const string NotTheHostTransferAdvice =
        "Importing and exporting happen on the PC running LunaPanel. Open the LunaPanel tray icon there and choose \"Import or export\".";

    /// <summary>
    /// Exact path match (case-insensitively, matching
    /// <see cref="DeviceAuthMiddlewareExtensions.IsExemptPath"/>) <b>and</b>
    /// method - deliberately not a prefix or a path-only check, because
    /// <c>GET</c> and <c>POST</c> on <see cref="ApiPaths.Macros"/> are the
    /// reading and the authoring halves of the same literal.
    ///
    /// The transfer routes are matched on path alone, under every method,
    /// which is the whole difference between them and the authoring three -
    /// see this type's own remarks.
    /// </summary>
    public static bool RequiresHost(string? path, string? method)
    {
        if (path is null)
        {
            return false;
        }

        if (IsTransferRoute(path))
        {
            return true;
        }

        if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Matches(path, ApiPaths.Macros)
            || Matches(path, ApiPaths.MacrosCopy)
            || Matches(path, ApiPaths.MacrosDelete);
    }

    /// <summary>
    /// The sentence to refuse <paramref name="path"/> with. Asked
    /// <em>after</em> <see cref="RequiresHost"/> has said yes, so the
    /// authoring wording is the fallback rather than a guess: a path that
    /// got here and is not a transfer route is one of the three authoring
    /// ones.
    /// </summary>
    public static string AdviceFor(string? path) =>
        path is not null && IsTransferRoute(path) ? NotTheHostTransferAdvice : NotTheHostAdvice;

    private static bool IsTransferRoute(string path) =>
        Matches(path, ApiPaths.TransferTargets)
        || Matches(path, ApiPaths.TransferProfile)
        || Matches(path, ApiPaths.TransferMacro)
        || Matches(path, ApiPaths.TransferUndo);

    private static bool Matches(string path, string route) =>
        string.Equals(path, route, StringComparison.OrdinalIgnoreCase);
}
