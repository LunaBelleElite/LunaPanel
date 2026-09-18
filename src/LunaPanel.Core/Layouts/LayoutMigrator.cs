using System.Text.Json.Nodes;

namespace LunaPanel.Core.Layouts;

/// <summary>
/// Brings a layout's raw JSON text from whatever schema version it was
/// written at up to <see cref="CurrentSchemaVersion"/>, entirely in memory -
/// nothing here writes to disk. The upgraded text is only ever written back
/// by <see cref="LayoutStore"/> on the user's next save; a load alone never
/// rewrites the file.
///
/// A file whose <c>schemaVersion</c> is newer than <see cref="CurrentSchemaVersion"/>
/// is refused outright (<see cref="LayoutMigrationOutcome.TooNew"/>). The
/// caller must leave that file byte-for-byte intact - never rewrite or
/// discard it - so someone running an older build against a newer file
/// doesn't lose it.
///
/// Schema history:
/// <list type="bullet">
/// <item>v0 (synthetic - this project has never actually shipped anything
/// older than v1; kept only to exercise the migration mechanism before it's
/// ever needed for real): a page's display name was written as <c>pageName</c>
/// instead of <c>name</c>.</item>
/// <item>v1: the shape documented in <c>ref/docs/layouts.md</c>.</item>
/// <item>v2 (current): adds folders - <c>folderPage</c> on a slot, and
/// <c>id</c>/<c>folderOwned</c> on a page. <b>There is deliberately no
/// <c>MigrateV1ToV2</c> body</b>: all three fields are additive and
/// optional, so a v1 file already parses correctly as v2 with no rewrite at
/// all. The bump is not for reading older files - it is for the OTHER
/// direction. Without it, a build that predates folders would meet a
/// folder-bearing file, see a version it recognizes, load it with no
/// folder-aware validation, and re-save it with every folder silently
/// stripped. With it, that build reports
/// <see cref="LayoutMigrationOutcome.TooNew"/> and leaves the file
/// byte-for-byte alone.</item>
/// </list>
/// </summary>
public static class LayoutMigrator
{
    public const int CurrentSchemaVersion = 2;

    public static LayoutMigrationResult Migrate(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (Exception ex)
        {
            // Deliberately broad, matching LunaPanel.Core.Pairing.DeviceRegistry's
            // documented reasoning: truncated or hand-edited garbage can surface
            // as several different exception types, and the contract here is
            // "never throws", not "never throws for the exception types we
            // thought of".
            return LayoutMigrationResult.Malformed(ex.GetType().Name);
        }

        if (root is not JsonObject obj)
        {
            return LayoutMigrationResult.Malformed(nameof(FormatException));
        }

        if (!obj.TryGetPropertyValue("schemaVersion", out var versionNode) || versionNode is null)
        {
            return LayoutMigrationResult.Malformed(nameof(FormatException));
        }

        int fileVersion;
        try
        {
            fileVersion = versionNode.AsValue().GetValue<int>();
        }
        catch (Exception ex)
        {
            return LayoutMigrationResult.Malformed(ex.GetType().Name);
        }

        if (fileVersion > CurrentSchemaVersion)
        {
            return LayoutMigrationResult.TooNew(fileVersion);
        }

        var wasMigrated = false;

        if (fileVersion < 1)
        {
            MigrateV0ToV1(obj);
            wasMigrated = true;
        }

        obj["schemaVersion"] = JsonValue.Create(CurrentSchemaVersion);
        var migratedJson = obj.ToJsonString();

        return wasMigrated
            ? LayoutMigrationResult.Migrated(migratedJson, fileVersion)
            : LayoutMigrationResult.Current(migratedJson, fileVersion);
    }

    private static void MigrateV0ToV1(JsonObject root)
    {
        if (root["pages"] is not JsonArray pages)
        {
            return;
        }

        foreach (var pageNode in pages)
        {
            if (pageNode is JsonObject page
                && page.TryGetPropertyValue("pageName", out var nameNode)
                && nameNode is not null)
            {
                page.Remove("pageName");
                page["name"] = nameNode.DeepClone();
            }
        }
    }
}
