using System.Text.Json;

namespace LunaPanel.Core.GameState;

/// <summary>
/// Parses the raw text content of Status.json into a <see cref="StatusSnapshot"/>.
/// This type never discovers the file itself - it takes the already-read
/// string content, because the real file can be read mid-rewrite by
/// whatever watches it, and this parser's only job is to turn that text
/// into a snapshot or a reported failure, never to throw.
/// </summary>
public static class StatusJsonParser
{
    /// <summary>
    /// The three keys a closed game's Status.json still writes. Any other
    /// key present is the signal the game process is actually running -
    /// see <see cref="StatusSnapshot.GameRunning"/>.
    /// </summary>
    private static readonly string[] AlwaysPresentKeys = { "timestamp", "event", "Flags" };

    public static StatusParseResult Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return StatusParseResult.Fail($"Malformed Status.json text: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return StatusParseResult.Fail($"Malformed Status.json: expected a JSON object at the root, got {root.ValueKind}.");
            }

            if (!root.TryGetProperty("Flags", out var flagsElement))
            {
                return StatusParseResult.Fail("Malformed Status.json: missing required 'Flags' property.");
            }

            // JsonElement.TryGetUInt32 (and TryGetInt32) THROW InvalidOperationException
            // rather than returning false when the element's ValueKind is not
            // Number at all (e.g. a string or array) - they only return false
            // for a Number that doesn't fit. Since this parser must never
            // throw on malformed input, ValueKind is checked explicitly first.
            if (flagsElement.ValueKind != JsonValueKind.Number || !flagsElement.TryGetUInt32(out var flags))
            {
                return StatusParseResult.Fail("Malformed Status.json: 'Flags' is not a valid 32-bit unsigned integer.");
            }

            uint? flags2 = null;
            if (root.TryGetProperty("Flags2", out var flags2Element))
            {
                if (flags2Element.ValueKind != JsonValueKind.Number || !flags2Element.TryGetUInt32(out var flags2Value))
                {
                    return StatusParseResult.Fail("Malformed Status.json: 'Flags2' is not a valid 32-bit unsigned integer.");
                }

                flags2 = flags2Value;
            }

            int? guiFocus = null;
            if (root.TryGetProperty("GuiFocus", out var guiFocusElement))
            {
                if (guiFocusElement.ValueKind != JsonValueKind.Number || !guiFocusElement.TryGetInt32(out var guiFocusValue))
                {
                    return StatusParseResult.Fail("Malformed Status.json: 'GuiFocus' is not a valid integer.");
                }

                guiFocus = guiFocusValue;
            }

            // Deliberately lenient, unlike Flags2/GuiFocus above: anything
            // this parser cannot make sense of here yields a null (or a
            // defaulted member) rather than failing the whole snapshot.
            // Destination feeds exactly one opportunistic, low-confidence
            // re-sync (PanelTabTracker.RecordDestinationChange, LC20);
            // rejecting the snapshot over it would take out every lit
            // condition and every macro gate at the same time, for a field
            // none of them read.
            StatusDestination? destination = null;
            if (root.TryGetProperty("Destination", out var destinationElement) &&
                destinationElement.ValueKind == JsonValueKind.Object)
            {
                var systemAddress = destinationElement.TryGetProperty("System", out var systemElement) &&
                    systemElement.ValueKind == JsonValueKind.Number &&
                    systemElement.TryGetInt64(out var systemValue)
                        ? systemValue
                        : 0L;

                var body = destinationElement.TryGetProperty("Body", out var bodyElement) &&
                    bodyElement.ValueKind == JsonValueKind.Number &&
                    bodyElement.TryGetInt32(out var bodyValue)
                        ? bodyValue
                        : 0;

                var name = destinationElement.TryGetProperty("Name", out var nameElement) &&
                    nameElement.ValueKind == JsonValueKind.String
                        ? nameElement.GetString() ?? string.Empty
                        : string.Empty;

                destination = new StatusDestination(systemAddress, body, name);
            }

            var gameRunning = false;
            foreach (var property in root.EnumerateObject())
            {
                if (Array.IndexOf(AlwaysPresentKeys, property.Name) < 0)
                {
                    gameRunning = true;
                    break;
                }
            }

            var signedIn = !(flags == 0 && (flags2 ?? 0) == 0);

            return StatusParseResult.Ok(new StatusSnapshot(flags, flags2, guiFocus, gameRunning, signedIn, destination));
        }
    }
}
