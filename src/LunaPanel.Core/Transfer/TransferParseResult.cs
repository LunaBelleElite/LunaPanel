using LunaPanel.Core.Layouts;
using LunaPanel.Core.Macros;

namespace LunaPanel.Core.Transfer;

/// <summary>What <see cref="TransferFile.Parse"/> found in a file somebody handed LunaPanel.</summary>
/// <param name="Success">Whether anything at all may be read off this result. Every other property is meaningless when this is <see langword="false"/>.</param>
/// <param name="Kind">Which of the two shapes it is.</param>
/// <param name="Layout">
/// The arrangement, for <see cref="TransferKind.Profile"/>; <see langword="null"/>
/// for <see cref="TransferKind.Macros"/>. Already brought up to
/// <see cref="LayoutMigrator.CurrentSchemaVersion"/> by the same migrator
/// <c>LayoutStore.Load</c> runs, so a profile exported by an older build
/// reads here exactly as an older file on disk does.
/// </param>
/// <param name="Macros">
/// Every macro the file carries, in file order. Empty is normal and not an
/// error - a profile whose buttons name only shipped macros carries none.
/// </param>
/// <param name="SourceName">
/// What the exporting device was called, for the sentence a commander is
/// shown before importing. Display only: nothing is keyed to it, and it is
/// run through <c>DeviceNaming.SanitizeTypedName</c> on the way in because
/// it is a string from a file that ends up on a screen.
/// </param>
/// <param name="Error">A plain, commander-readable refusal. Never a file path, never a stack trace.</param>
public sealed record TransferParseResult(
    bool Success,
    TransferKind Kind,
    Layout? Layout,
    IReadOnlyList<MacroDefinition> Macros,
    string? SourceName,
    string? Error)
{
    public static TransferParseResult Profile(Layout layout, IReadOnlyList<MacroDefinition> macros, string? sourceName) =>
        new(true, TransferKind.Profile, layout, macros, sourceName, null);

    public static TransferParseResult MacrosOnly(IReadOnlyList<MacroDefinition> macros, string? sourceName) =>
        new(true, TransferKind.Macros, null, macros, sourceName, null);

    public static TransferParseResult Fail(string error) =>
        new(false, TransferKind.Profile, null, Array.Empty<MacroDefinition>(), null, error);
}
