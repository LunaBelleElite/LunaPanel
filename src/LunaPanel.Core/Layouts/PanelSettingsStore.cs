using System.Text.Json;
using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Core.Layouts;

/// <summary>
/// Loads and saves a device's <see cref="PanelSettings"/> under an injected
/// directory - never discovers that directory itself, the same discipline
/// <see cref="LayoutStore"/> and <c>LunaPanel.Core.Theme.ThemeOverrideStore</c>
/// already follow. Mirrors <c>ThemeOverrideStore</c>'s own conventions
/// rather than inventing new ones: an absent or corrupt file quietly
/// resolves to <see cref="PanelSettings.Default"/> (there is nothing here a
/// player could lose by a corrupt file the way a layout or a colour choice
/// could be - "merge/expand ON" is a safe, reversible default), and
/// <see cref="Save"/> writes to a temp file before an atomic move into
/// place.
/// </summary>
public sealed class PanelSettingsStore
{
    private const string LogCategory = "Layout";

    private readonly string _directory;
    private readonly IDiagnosticLog _log;
    private readonly object _gate = new();

    public PanelSettingsStore(string directory, IDiagnosticLog log)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        Directory.CreateDirectory(_directory);
    }

    public PanelSettings Load(string deviceId)
    {
        lock (_gate)
        {
            var mainPath = MainPath(deviceId);
            if (!File.Exists(mainPath))
            {
                return PanelSettings.Default;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(mainPath));
                var mergeExpand = true;
                if (document.RootElement.TryGetProperty("mergeExpand", out var element) && element.ValueKind == JsonValueKind.False)
                {
                    mergeExpand = false;
                }

                var showMacroStepResults = true;
                if (document.RootElement.TryGetProperty("showMacroStepResults", out var stepResultsElement) && stepResultsElement.ValueKind == JsonValueKind.False)
                {
                    showMacroStepResults = false;
                }

                // Deliberately true on absence, matching PanelSettings.Default -
                // not copied from the two fields above, whose own local
                // defaults here are a known, pre-existing inconsistency with
                // Default that this new field does not repeat.
                var autoSwitchEnabled = true;
                if (document.RootElement.TryGetProperty("autoSwitchEnabled", out var autoSwitchElement) && autoSwitchElement.ValueKind == JsonValueKind.False)
                {
                    autoSwitchEnabled = false;
                }

                return new PanelSettings(mergeExpand, showMacroStepResults, autoSwitchEnabled);
            }
            catch (JsonException ex)
            {
                _log.Error(LogCategory, "Panel settings file was corrupt; falling back to defaults", ex.Message);
                return PanelSettings.Default;
            }
        }
    }

    public void Save(string deviceId, PanelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_gate)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteBoolean("mergeExpand", settings.MergeExpand);
                writer.WriteBoolean("showMacroStepResults", settings.ShowMacroStepResults);
                writer.WriteBoolean("autoSwitchEnabled", settings.AutoSwitchEnabled);
                writer.WriteEndObject();
            }

            var mainPath = MainPath(deviceId);
            var tempPath = mainPath + ".tmp";
            File.WriteAllBytes(tempPath, stream.ToArray());
            File.Move(tempPath, mainPath, overwrite: true);

            _log.Info(LogCategory, "Saved panel settings", $"deviceId={deviceId}, mergeExpand={settings.MergeExpand}, showMacroStepResults={settings.ShowMacroStepResults}, autoSwitchEnabled={settings.AutoSwitchEnabled}");
        }
    }

    private string MainPath(string deviceId) => Path.Combine(_directory, $"panel-settings-{deviceId}.json");
}
