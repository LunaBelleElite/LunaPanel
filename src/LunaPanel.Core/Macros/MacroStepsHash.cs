using System.Security.Cryptography;
using System.Text;

namespace LunaPanel.Core.Macros;

/// <summary>
/// A deterministic fingerprint of a macro's <b>steps only</b> - id and name
/// deliberately excluded, so renaming a macro or minting it a fresh id (as
/// copy-to-edit does) never changes the hash on its own.
///
/// Built on <see cref="MacroJson.Serialize"/> rather than a second writer,
/// the same "one grammar in one place" discipline
/// <c>MacrosEndpoint.Assemble</c> follows for the same reason: two ways to
/// turn steps into bytes could disagree about what "unchanged" means, and
/// this type would then rather silently.
///
/// Used by the macro builder's copy-to-edit staleness line
/// (<c>ref/docs/macro-builder.md</c>, question 3): a copy records this over
/// its source's steps at copy time, and a later mismatch against the
/// source's <em>current</em> steps is what "stale" means.
/// </summary>
public static class MacroStepsHash
{
    /// <summary>
    /// Hex-encoded SHA-256 over the steps as <see cref="MacroJson"/> would
    /// write them, with a fixed placeholder id and no name - the same
    /// short-public-handle shape <c>DeviceRegistry.ComputeDeviceId</c> uses
    /// for a token, applied here to step content instead.
    /// </summary>
    public static string Compute(IReadOnlyList<MacroStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var canonical = MacroJson.Serialize(new MacroDefinition("_", null, steps));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }
}
