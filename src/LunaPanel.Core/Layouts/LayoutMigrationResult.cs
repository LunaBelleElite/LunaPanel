namespace LunaPanel.Core.Layouts;

/// <summary>Outcome of <see cref="LayoutMigrator.Migrate"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="MigratedJson">JSON text at <see cref="LayoutMigrator.CurrentSchemaVersion"/>, ready for <see cref="LayoutJson.Parse"/>. <see langword="null"/> for <see cref="LayoutMigrationOutcome.Malformed"/>/<see cref="LayoutMigrationOutcome.TooNew"/>.</param>
/// <param name="FileSchemaVersion">The schema version the source text was actually written at, or <see langword="null"/> when it couldn't even be read (<see cref="LayoutMigrationOutcome.Malformed"/>).</param>
/// <param name="ErrorDetail">For <see cref="LayoutMigrationOutcome.Malformed"/>, the exception type name only - never a message that could carry a file path, since this reaches public-facing logs.</param>
public sealed record LayoutMigrationResult(LayoutMigrationOutcome Outcome, string? MigratedJson, int? FileSchemaVersion, string? ErrorDetail)
{
    public static LayoutMigrationResult Malformed(string exceptionTypeName) =>
        new(LayoutMigrationOutcome.Malformed, null, null, exceptionTypeName);

    public static LayoutMigrationResult TooNew(int fileSchemaVersion) =>
        new(LayoutMigrationOutcome.TooNew, null, fileSchemaVersion, null);

    public static LayoutMigrationResult Current(string migratedJson, int fileSchemaVersion) =>
        new(LayoutMigrationOutcome.AlreadyCurrent, migratedJson, fileSchemaVersion, null);

    public static LayoutMigrationResult Migrated(string migratedJson, int fileSchemaVersion) =>
        new(LayoutMigrationOutcome.Migrated, migratedJson, fileSchemaVersion, null);
}
