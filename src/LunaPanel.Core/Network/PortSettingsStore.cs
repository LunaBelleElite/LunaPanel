using System.Text.Json;
using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Core.Network;

/// <summary>
/// Loads and saves <see cref="PortSettings"/> under an injected directory -
/// never discovers that directory itself, the same discipline
/// <c>LunaPanel.Core.Macros.MacroTimingSettingsStore</c> follows, which this
/// class otherwise mirrors exactly: one fixed file
/// (<c>port-settings.json</c>), server-wide rather than per device, atomic
/// temp-file-then-move writes, and a corrupt or out-of-range file falling
/// back to <see cref="PortSettings.Default"/> rather than throwing.
///
/// Lives in the same directory
/// <c>LayoutStore</c>/<c>ThemeOverrideStore</c>/<c>PanelSettingsStore</c>/
/// <c>OrphanMarkerStore</c>/<c>MacroTimingSettingsStore</c> already share
/// (<c>LunaPanelDirectories.LayoutsDirectory</c>).
/// </summary>
public sealed class PortSettingsStore
{
    private const string LogCategory = "Network";
    private const string FileName = "port-settings.json";

    private readonly string _directory;
    private readonly IDiagnosticLog _log;
    private readonly object _gate = new();

    public PortSettingsStore(string directory, IDiagnosticLog log)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        Directory.CreateDirectory(_directory);
    }

    /// <summary>
    /// Raised, carrying the settings just written, after <see cref="Save"/>
    /// has actually changed <c>port-settings.json</c> on disk. Never raised by
    /// <see cref="Load"/> - reading is not a change.
    /// </summary>
    public event Action<PortSettings>? Saved;

    public PortSettings Load()
    {
        lock (_gate)
        {
            var mainPath = MainPath();
            if (!File.Exists(mainPath))
            {
                return PortSettings.Default;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(mainPath));
                var root = document.RootElement;

                var port = root.TryGetProperty("port", out var portElement) && portElement.ValueKind == JsonValueKind.Number
                    ? portElement.GetInt32()
                    : PortSettings.DefaultPort;

                if (!PortSettings.IsValid(port))
                {
                    _log.Error(
                        LogCategory,
                        "Port settings file held an out-of-range value; falling back to the default",
                        $"port={port}");
                    return PortSettings.Default;
                }

                return new PortSettings(port);
            }
            catch (JsonException ex)
            {
                _log.Error(LogCategory, "Port settings file was corrupt; falling back to the default", ex.Message);
                return PortSettings.Default;
            }
        }
    }

    /// <summary>
    /// Persists <paramref name="settings"/> unconditionally - range
    /// validation is the caller's job (<c>PortSettingsForm</c>). <see cref="Load"/>
    /// still guards against an out-of-range value reaching this store some
    /// other way (a hand-edited file), as defense in depth.
    /// </summary>
    public void Save(PortSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_gate)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("port", settings.Port);
                writer.WriteEndObject();
            }

            var mainPath = MainPath();
            var tempPath = mainPath + ".tmp";
            File.WriteAllBytes(tempPath, stream.ToArray());
            File.Move(tempPath, mainPath, overwrite: true);

            _log.Info(LogCategory, "Saved port settings", $"port={settings.Port}");
        }

        Saved?.Invoke(settings);
    }

    private string MainPath() => Path.Combine(_directory, FileName);
}
