namespace LunaPanel.Core.Layouts;

/// <summary>Outcome of <see cref="LayoutStore.Load"/>.</summary>
public enum LayoutLoadOutcome
{
    /// <summary>No file exists for this device yet - first run, not an error.</summary>
    NotFound,

    /// <summary>Loaded successfully (after migration, if any was needed).</summary>
    Loaded,

    /// <summary>The file was unreadable and has been renamed aside; nothing was loaded.</summary>
    Corrupt,

    /// <summary>The file's schema version is newer than this build understands; refused and left untouched.</summary>
    TooNewSchema
}
