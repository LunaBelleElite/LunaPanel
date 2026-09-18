using LunaPanel.Core.Layouts;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// Pins <see cref="LayoutMigrator.Migrate"/>: migration from the synthetic
/// older schema fixture, a current-schema file passing through unchanged,
/// and a newer-than-known schema being refused with its source text
/// untouched.
/// </summary>
public class LayoutMigratorTests
{
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "layouts", name);
    private static string ReadFixture(string name) => File.ReadAllText(FixturePath(name));

    [Fact]
    public void Migrate_LegacyV0Fixture_RenamesPageNameToName_AndReportsMigrated()
    {
        var original = ReadFixture("legacy-v0.json");

        var result = LayoutMigrator.Migrate(original);

        Assert.Equal(LayoutMigrationOutcome.Migrated, result.Outcome);
        Assert.Equal(0, result.FileSchemaVersion);
        Assert.NotNull(result.MigratedJson);

        var parsed = LayoutJson.Parse(result.MigratedJson!);
        Assert.True(parsed.Success, parsed.Error);
        Assert.Equal(LayoutMigrator.CurrentSchemaVersion, parsed.Layout!.SchemaVersion);
        Assert.Equal("SHIP", parsed.Layout.Pages[0].Name);
    }

    [Fact]
    public void Migrate_LegacyV0Fixture_DoesNotMutateTheOriginalSourceText()
    {
        var original = ReadFixture("legacy-v0.json");

        LayoutMigrator.Migrate(original);

        // The migrator works on a parsed copy - the original string handed
        // in must be unaffected either way, and re-reading the fixture from
        // disk must still show the untouched v0 content.
        Assert.Contains("\"pageName\"", ReadFixture("legacy-v0.json"));
        Assert.Contains("\"schemaVersion\": 0", original);
    }

    [Fact]
    public void Migrate_CurrentSchemaFixture_ReturnsAlreadyCurrent()
    {
        var result = LayoutMigrator.Migrate(ReadFixture("valid-multi-page.json"));

        Assert.Equal(LayoutMigrationOutcome.AlreadyCurrent, result.Outcome);
        Assert.Equal(1, result.FileSchemaVersion);
        Assert.NotNull(result.MigratedJson);
    }

    [Fact]
    public void Migrate_NewerSchemaFixture_ReturnsTooNew_AndDoesNotProduceMigratedJson()
    {
        var result = LayoutMigrator.Migrate(ReadFixture("newer-schema.json"));

        Assert.Equal(LayoutMigrationOutcome.TooNew, result.Outcome);
        Assert.Equal(999, result.FileSchemaVersion);
        Assert.Null(result.MigratedJson);
    }

    [Fact]
    public void Migrate_CorruptFixture_ReturnsMalformed_WithExceptionTypeNameOnly()
    {
        var result = LayoutMigrator.Migrate(ReadFixture("corrupt.json"));

        Assert.Equal(LayoutMigrationOutcome.Malformed, result.Outcome);
        Assert.Null(result.MigratedJson);
        Assert.NotNull(result.ErrorDetail);
        // Must be a bare exception type name, never a message that could
        // carry a file path or other detail.
        Assert.DoesNotContain(" ", result.ErrorDetail);
    }

    [Fact]
    public void Migrate_EmptyString_ReturnsMalformed_NotException()
    {
        var result = LayoutMigrator.Migrate(string.Empty);

        Assert.Equal(LayoutMigrationOutcome.Malformed, result.Outcome);
    }

    [Fact]
    public void Migrate_ValidJsonButNotAnObject_ReturnsMalformed()
    {
        var result = LayoutMigrator.Migrate("[1, 2, 3]");

        Assert.Equal(LayoutMigrationOutcome.Malformed, result.Outcome);
    }
}
