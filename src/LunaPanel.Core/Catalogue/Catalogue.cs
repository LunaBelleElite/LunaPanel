using System.Text.Json;
using LunaPanel.Core.GameState;

namespace LunaPanel.Core.Catalogue;

/// <summary>
/// The curated action catalogue: shipped categories and curated action
/// entries. Parsed from the <c>catalogue.json</c> text embedded in
/// LunaPanel.Server - this type never discovers or reads that file itself,
/// matching the discipline already used by
/// <c>LunaPanel.Core.GameState.StatusJsonParser</c> and
/// <c>LunaPanel.Core.Bindings.BindingsFile</c>.
///
/// Unlike those two, <c>catalogue.json</c> is content LunaPanel ships, not
/// runtime data from the player's machine that can be caught mid-write - so
/// <see cref="Parse"/> deliberately does NOT follow their "never throws"
/// discipline. A malformed catalogue is a build-time defect, and
/// <see cref="Parse"/> throws loudly rather than silently producing an
/// empty catalogue that would hide the defect until a player notices a
/// missing action.
/// </summary>
public sealed class Catalogue
{
    public int Version { get; }
    public IReadOnlyDictionary<string, CatalogueCategory> Categories { get; }
    public IReadOnlyDictionary<string, CatalogueAction> Actions { get; }

    private Catalogue(
        int version,
        IReadOnlyDictionary<string, CatalogueCategory> categories,
        IReadOnlyDictionary<string, CatalogueAction> actions)
    {
        Version = version;
        Categories = categories;
        Actions = actions;
    }

    /// <summary>
    /// Parses already-read catalogue JSON text.
    /// </summary>
    /// <exception cref="FormatException">
    /// The JSON is malformed, missing a required field, an action names an
    /// undeclared category, or a <c>lit</c> condition fails
    /// <see cref="ConditionList.Parse"/> (unknown condition/GuiFocus name).
    /// </exception>
    public static Catalogue Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Malformed catalogue JSON: {ex.Message}", ex);
        }

        using (document)
        {
            var root = document.RootElement;

            if (!root.TryGetProperty("catalogueVersion", out var versionElement) ||
                versionElement.ValueKind != JsonValueKind.Number)
            {
                throw new FormatException("Catalogue JSON is missing a numeric 'catalogueVersion' property.");
            }

            var version = versionElement.GetInt32();

            if (!root.TryGetProperty("categories", out var categoriesElement) ||
                categoriesElement.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("Catalogue JSON is missing a 'categories' array.");
            }

            var categories = new Dictionary<string, CatalogueCategory>(StringComparer.Ordinal);
            foreach (var categoryElement in categoriesElement.EnumerateArray())
            {
                var id = RequireString(categoryElement, "id", "A category");
                var label = RequireString(categoryElement, "label", $"Category '{id}'");
                if (!categories.TryAdd(id, new CatalogueCategory(id, label)))
                {
                    throw new FormatException($"Catalogue JSON declares category '{id}' more than once.");
                }
            }

            if (!root.TryGetProperty("actions", out var actionsElement) ||
                actionsElement.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Catalogue JSON is missing an 'actions' object.");
            }

            var actions = new Dictionary<string, CatalogueAction>(StringComparer.Ordinal);
            foreach (var actionProperty in actionsElement.EnumerateObject())
            {
                var actionName = actionProperty.Name;
                if (string.IsNullOrEmpty(actionName))
                {
                    throw new FormatException("Catalogue JSON has an action with an empty name.");
                }

                var actionElement = actionProperty.Value;
                var label = RequireString(actionElement, "label", $"Action '{actionName}'");
                var category = RequireString(actionElement, "category", $"Action '{actionName}'");

                if (!categories.ContainsKey(category))
                {
                    throw new FormatException($"Action '{actionName}' names undeclared category '{category}'.");
                }

                IReadOnlyList<LitCondition>? lit = null;
                if (actionElement.TryGetProperty("lit", out var litElement))
                {
                    lit = ParseLit(actionName, litElement);
                }

                if (!actions.TryAdd(actionName, new CatalogueAction(label, category, lit)))
                {
                    throw new FormatException($"Catalogue JSON declares action '{actionName}' more than once.");
                }
            }

            return new Catalogue(version, categories, actions);
        }
    }

    /// <summary>
    /// Parses an action's <c>lit</c> property into an ordered
    /// <see cref="LitCondition"/> list. Two shapes, distinguished by the
    /// <see cref="JsonValueKind"/> of the array's own elements - not mixable
    /// within one array:
    /// <list type="bullet">
    /// <item>every element a JSON string (the original, bare shape,
    /// <c>"lit": ["Name", "!Other"]</c>) - a single condition token list,
    /// normalized to one <see cref="LitCondition"/> at
    /// <see cref="SlotLitLevel.Full"/>. This is what all twenty pre-existing
    /// curated entries use, and it must keep meaning exactly what it always
    /// has: full when true, off when false.</item>
    /// <item>every element a JSON object (<c>{ "when": [...], "level":
    /// "..." }</c>) - the ordered, levelled shape. <c>level</c> must be
    /// <c>"partial"</c> or <c>"full"</c> (case-sensitive, matching every
    /// other lower-camel token this JSON uses) - <c>"off"</c> is never
    /// written explicitly, since it is already the answer when nothing in
    /// the list matches.</item>
    /// </list>
    /// </summary>
    /// <exception cref="FormatException">
    /// The <c>lit</c> property is not an array, mixes string and object
    /// entries, an object entry is missing <c>when</c>/<c>level</c> or
    /// carries an unrecognized <c>level</c>, or any condition token fails
    /// <see cref="ConditionList.Parse"/>.
    /// </exception>
    private static IReadOnlyList<LitCondition> ParseLit(string actionName, JsonElement litElement)
    {
        if (litElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"Action '{actionName}' has a 'lit' property that is not an array.");
        }

        var elements = litElement.EnumerateArray().ToList();
        if (elements.Count > 0 && elements.All(e => e.ValueKind == JsonValueKind.String))
        {
            var tokens = elements.Select(e => e.GetString()!).ToList();
            return new[] { new LitCondition(ParseConditionList(actionName, tokens), SlotLitLevel.Full) };
        }

        var conditions = new List<LitCondition>();
        foreach (var entry in elements)
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException($"Action '{actionName}' has a 'lit' entry that is neither a string (the bare shape) nor an object (the levelled shape).");
            }

            if (!entry.TryGetProperty("when", out var whenElement) || whenElement.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException($"Action '{actionName}' has a 'lit' entry missing a 'when' array.");
            }

            var whenTokens = new List<string>();
            foreach (var tokenElement in whenElement.EnumerateArray())
            {
                if (tokenElement.ValueKind != JsonValueKind.String)
                {
                    throw new FormatException($"Action '{actionName}' has a non-string entry in a 'lit' entry's 'when'.");
                }

                whenTokens.Add(tokenElement.GetString()!);
            }

            var levelText = RequireString(entry, "level", $"Action '{actionName}''s 'lit' entry");
            var level = levelText switch
            {
                "partial" => SlotLitLevel.Partial,
                "full" => SlotLitLevel.Full,
                _ => throw new FormatException($"Action '{actionName}' has a 'lit' entry with an unrecognized 'level' value '{levelText}' (expected 'partial' or 'full')."),
            };

            conditions.Add(new LitCondition(ParseConditionList(actionName, whenTokens), level));
        }

        return conditions;
    }

    private static ConditionList ParseConditionList(string actionName, IReadOnlyList<string> tokens)
    {
        try
        {
            return ConditionList.Parse(tokens);
        }
        catch (FormatException ex)
        {
            throw new FormatException($"Action '{actionName}' has an invalid 'lit' condition: {ex.Message}", ex);
        }
    }

    private static string RequireString(JsonElement element, string propertyName, string context)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"{context} is missing a string '{propertyName}' property.");
        }

        var value = property.GetString();
        if (string.IsNullOrEmpty(value))
        {
            throw new FormatException($"{context} has an empty '{propertyName}' property.");
        }

        return value;
    }
}
