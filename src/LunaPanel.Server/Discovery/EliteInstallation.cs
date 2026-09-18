namespace LunaPanel.Server.Discovery;

/// <summary>
/// Elite Dangerous ships two distinct product folders side by side -
/// confirmed on the authoring machine's real Steam install, which has both
/// under the same library's <c>Products/</c> folder. Detected here by which
/// executable a product folder actually contains, not by the folder's own
/// name (Frontier's internal product folder names are not a documented or
/// stable contract - <c>elite-dangerous-odyssey-64</c> and
/// <c>FORC-FDEV-D-1010</c> were what the authoring machine happened to have).
/// </summary>
public enum EliteEdition
{
    Odyssey,
    Horizons,
}

/// <summary>Which storefront/launcher a found install came from.</summary>
public enum EliteSource
{
    Steam,
    Epic,
    Frontier,

    /// <summary>
    /// The commander pointed LunaPanel at this install directly (the About
    /// window's "Select…" button, <c>PathOverrideStore</c>), rather than it
    /// being found by any storefront probe.
    /// </summary>
    Manual,
}

/// <summary>One discovered Elite Dangerous product installation.</summary>
public sealed record EliteInstallation(EliteEdition Edition, string ProductPath, EliteSource Source);
