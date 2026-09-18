using System.Text.Json;
using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Core.Tray;

/// <summary>
/// Loads and saves <see cref="TrayBehaviorSettings"/> under an injected
/// directory - never discovers that directory itself, the same discipline
/// <c>LunaPanel.Core.Network.PortSettingsStore</c> follows, which this class
/// otherwise mirrors exactly: one fixed file (<c>tray-behavior.json</c>),
/// server-wide rather than per device, atomic temp-file-then-move writes,
/// and a corrupt or missing file falling back to
/// <see cref="TrayBehaviorSettings.Default"/> rather than throwing.
///
/// Lives in the same directory
/// <c>LayoutStore</c>/<c>ThemeOverrideStore</c>/<c>PanelSettingsStore</c>/
/// <c>OrphanMarkerStore</c>/<c>MacroTimingSettingsStore</c>/<c>PortSettingsStore</c>
/// already share (<c>LunaPanelDirectories.LayoutsDirectory</c>).
/// </summary>
public sealed class TrayBehaviorStore
{
    private const string LogCategory = "Tray";
    private const string FileName = "tray-behavior.json";

    private readonly string _directory;
    private readonly IDiagnosticLog _log;
    private readonly object _gate = new();

    public TrayBehaviorStore(string directory, IDiagnosticLog log)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        Directory.CreateDirectory(_directory);
    }

    /// <summary>
    /// Raised, carrying the settings just written, after <see cref="Save"/>
    /// has actually changed <c>tray-behavior.json</c> on disk. Never raised
    /// by <see cref="Load"/> - reading is not a change.
    /// </summary>
    public event Action<TrayBehaviorSettings>? Saved;

    public TrayBehaviorSettings Load()
    {
        lock (_gate)
        {
            var mainPath = MainPath();
            if (!File.Exists(mainPath))
            {
                return TrayBehaviorSettings.Default;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(mainPath));
                var root = document.RootElement;

                var minimizeToTrayEnabled = root.TryGetProperty("minimizeToTrayEnabled", out var element)
                    && element.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? element.GetBoolean()
                    : TrayBehaviorSettings.Default.MinimizeToTrayEnabled;

                return new TrayBehaviorSettings(minimizeToTrayEnabled);
            }
            catch (JsonException ex)
            {
                _log.Error(LogCategory, "Tray behavior file was corrupt; falling back to the default", ex.Message);
                return TrayBehaviorSettings.Default;
            }
        }
    }

    public void Save(TrayBehaviorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_gate)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteBoolean("minimizeToTrayEnabled", settings.MinimizeToTrayEnabled);
                writer.WriteEndObject();
            }

            var mainPath = MainPath();
            var tempPath = mainPath + ".tmp";
            File.WriteAllBytes(tempPath, stream.ToArray());
            File.Move(tempPath, mainPath, overwrite: true);

            _log.Info(LogCategory, "Saved tray behavior settings", $"minimizeToTrayEnabled={settings.MinimizeToTrayEnabled}");
        }

        Saved?.Invoke(settings);
    }

    private string MainPath() => Path.Combine(_directory, FileName);
}
