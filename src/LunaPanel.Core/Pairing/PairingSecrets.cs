using System.Globalization;
using System.Security.Cryptography;

namespace LunaPanel.Core.Pairing;

/// <summary>
/// Generates the two secrets <see cref="PairingService"/> persists - a
/// long-lived 32-byte token and a short-lived 6-digit pairing code - both
/// from a cryptographically secure RNG, never <c>System.Random</c>. Kept as
/// a separate static class (rather than private methods on
/// <see cref="PairingService"/>) so a distribution sanity check can call
/// <see cref="GenerateCode"/> directly, many times, without going through
/// file-backed construction.
/// </summary>
public static class PairingSecrets
{
    public const int TokenLengthBytes = 32;

    /// <summary>
    /// Exclusive upper bound for a 6-digit code: values 0 through 999999.
    /// </summary>
    private const int CodeUpperBoundExclusive = 1_000_000;

    /// <summary>A fresh 32-byte token from a cryptographically secure RNG.</summary>
    public static byte[] GenerateToken() => RandomNumberGenerator.GetBytes(TokenLengthBytes);

    /// <summary>
    /// A uniformly-distributed 6-digit code, zero-padded (e.g. "004213").
    /// Uses <see cref="RandomNumberGenerator.GetInt32(int, int)"/>, which
    /// performs rejection sampling internally rather than a plain modulo, so
    /// this does not introduce modulo bias across the 0-999999 range.
    /// </summary>
    public static string GenerateCode() =>
        RandomNumberGenerator.GetInt32(0, CodeUpperBoundExclusive).ToString("D6", CultureInfo.InvariantCulture);
}
