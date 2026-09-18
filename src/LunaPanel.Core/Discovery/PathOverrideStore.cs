using System.Text.Json;
using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Core.Discovery;

/// <summary>
/// Loads and saves <see cref="PathOverrideSettings"/> under an injected
/// directory - never discovers that directory itself, mirroring
/// <c>LunaPanel.Core.Network.PortSettingsStore</c> exactly: one fixed file
/// (<c>path-overrides.json</c>), server-wide rather than per device, atomic
/// temp-file-then-move writes, and a corrupt file falling back to
/// <see cref="PathOverrideSettings.Default"/> rather than throwing.
///
/// Lives in the same directory <c>PortSettingsStore</c>/<c>LayoutStore</c>/
/// <c>ThemeOverrideStore</c>/<c>PanelSettingsStore</c>/
/// <c>OrphanMarkerStore</c>/<c>MacroTimingSettingsStore</c> already share
/// (<c>LunaPanelDirectories.LayoutsDirectory</c>).
/// </summary>
public sealed class PathOverrideStore
{
    private const string LogCategory = "Discovery";
    private const string FileName = "path-overrides.json";

    private readonly string _directory;
    private readonly IDiagnosticLog _log;
    private readonly object _gate = new();

    public PathOverrideStore(string directory, IDiagnosticLog log)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        Directory.CreateDirectory(_directory);
    }

    /// <summary>
    /// Raised, carrying the settings just written, after <see cref="Save"/>
    /// has actually changed <c>path-overrides.json</c> on disk. Never raised
    /// by <see cref="Load"/> - reading is not a change.
    /// </summary>
    public event Action<PathOverrideSettings>? Saved;

    public PathOverrideSettings Load()
    {
        lock (_gate)
        {
            var mainPath = MainPath();
            if (!File.Exists(mainPath))
            {
                return PathOverrideSettings.Default;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(mainPath));
                var root = document.RootElement;

                var eliteInstallPath = root.TryGetProperty("eliteInstallPath", out var eliteElement)
                    && eliteElement.ValueKind == JsonValueKind.String
                    ? eliteElement.GetString()
                    : null;

                var edhmSettingsJsonPath = root.TryGetProperty("edhmSettingsJsonPath", out var edhmElement)
                    && edhmElement.ValueKind == JsonValueKind.String
                    ? edhmElement.GetString()
                    : null;

                var eliteSetupAcknowledged = root.TryGetProperty("eliteSetupAcknowledged", out var acknowledgedElement)
                    && acknowledgedElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                    && acknowledgedElement.GetBoolean();

                return new PathOverrideSettings(eliteInstallPath, edhmSettingsJsonPath, eliteSetupAcknowledged);
            }
            catch (JsonException ex)
            {
                _log.Error(LogCategory, "Path overrides file was corrupt; falling back to no overrides", ex.Message);
                return PathOverrideSettings.Default;
            }
        }
    }

    /// <summary>
    /// Persists <paramref name="settings"/> unconditionally - neither field
    /// is validated here (the caller, <c>AboutForm</c>, only ever writes a
    /// path a picker dialog actually returned).
    /// </summary>
    public void Save(PathOverrideSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_gate)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                if (settings.EliteInstallPath is not null)
                {
                    writer.WriteString("eliteInstallPath", settings.EliteInstallPath);
                }
                if (settings.EdhmSettingsJsonPath is not null)
                {
                    writer.WriteString("edhmSettingsJsonPath", settings.EdhmSettingsJsonPath);
                }
                writer.WriteBoolean("eliteSetupAcknowledged", settings.EliteSetupAcknowledged);
                writer.WriteEndObject();
            }

            var mainPath = MainPath();
            var tempPath = mainPath + ".tmp";
            File.WriteAllBytes(tempPath, stream.ToArray());
            File.Move(tempPath, mainPath, overwrite: true);

            _log.Info(
                LogCategory,
                "Saved path overrides",
                $"eliteInstallPath={settings.EliteInstallPath ?? "(none)"}, edhmSettingsJsonPath={settings.EdhmSettingsJsonPath ?? "(none)"}, eliteSetupAcknowledged={settings.EliteSetupAcknowledged}");
        }

        Saved?.Invoke(settings);
    }

    private string MainPath() => Path.Combine(_directory, FileName);
}
