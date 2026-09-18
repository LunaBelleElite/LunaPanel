namespace LunaPanel.Core.Theme;

/// <summary>Outcome of <see cref="ThemeOverrideStore.Load"/>.</summary>
public enum ThemeOverrideLoadOutcome
{
    /// <summary>No override stored for this device - resolve automatically.</summary>
    NotFound,

    /// <summary>Loaded successfully.</summary>
    Loaded,

    /// <summary>
    /// The file was unreadable and has been renamed aside; nothing was
    /// loaded. Unlike a corrupt layout (which has no automatic fallback),
    /// a corrupt override degrades to automatic resolution - never a blank
    /// or errored panel, matching this whole feature's guarantee.
    /// </summary>
    Corrupt,
}
