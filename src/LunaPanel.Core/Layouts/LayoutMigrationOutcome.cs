namespace LunaPanel.Core.Layouts;

/// <summary>Outcome of <see cref="LayoutMigrator.Migrate"/>.</summary>
public enum LayoutMigrationOutcome
{
    /// <summary>The JSON couldn't be read at all, or no readable <c>schemaVersion</c> was found.</summary>
    Malformed,

    /// <summary><c>schemaVersion</c> is newer than <see cref="LayoutMigrator.CurrentSchemaVersion"/> - refused, the source text is untouched.</summary>
    TooNew,

    /// <summary><c>schemaVersion</c> already equals <see cref="LayoutMigrator.CurrentSchemaVersion"/> - nothing changed.</summary>
    AlreadyCurrent,

    /// <summary><c>schemaVersion</c> was older than current and has been brought up to it, in memory only.</summary>
    Migrated
}
