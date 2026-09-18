namespace LunaPanel.Core.Transfer;

/// <summary>
/// The two things a LunaPanel transfer file can be. One format, two kinds,
/// discriminated by <see cref="TransferFile.KindProperty"/> - never two
/// formats, and never "work out what this is from which properties are
/// present", which is how a reader ends up importing half of something.
/// </summary>
public enum TransferKind
{
    /// <summary>A whole device arrangement, plus the definitions of the user macros its buttons name.</summary>
    Profile,

    /// <summary>Macros on their own, with no arrangement.</summary>
    Macros,
}
