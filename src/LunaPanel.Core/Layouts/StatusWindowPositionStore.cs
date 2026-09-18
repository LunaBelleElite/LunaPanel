using System.Text.Json;
using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Core.Layouts;

/// <summary>
/// The Status window's last on-screen position, so it reopens where the
/// commander left it rather than always centered - mirrors
/// <c>LunaPanel.Core.Network.PortSettingsStore</c>'s shape exactly: one fixed
/// file (<c>status-window-position.json</c>), server-wide rather than
/// per-device, atomic temp-file-then-move writes, and a corrupt file
/// tolerated rather than thrown.
///
/// <see cref="Load"/> returns null - not a default position - when nothing
/// has ever been saved or the file is corrupt, because "never saved" (today's
/// <c>CenterScreen</c> behavior) and "saved but now invalid, e.g. the monitor
/// it was on is gone" (the fallback-and-correct path,
/// <see cref="MonitorPositionValidator"/>) are different cases the caller
/// needs to be able to tell apart. Unlike <c>PortSettingsStore</c>, there is
/// no shipped default position to fall back to.
/// </summary>
public sealed class StatusWindowPositionStore
{
    private const string LogCategory = "Layouts";
    private const string FileName = "status-window-position.json";

    private readonly string _directory;
    private readonly IDiagnosticLog _log;
    private readonly object _gate = new();

    public StatusWindowPositionStore(string directory, IDiagnosticLog log)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        Directory.CreateDirectory(_directory);
    }

    public StatusWindowPosition? Load()
    {
        lock (_gate)
        {
            var mainPath = MainPath();
            if (!File.Exists(mainPath))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(mainPath));
                var root = document.RootElement;

                if (!root.TryGetProperty("x", out var xElement) || xElement.ValueKind != JsonValueKind.Number
                    || !root.TryGetProperty("y", out var yElement) || yElement.ValueKind != JsonValueKind.Number)
                {
                    _log.Error(LogCategory, "Status window position file was missing x/y; ignoring it", mainPath);
                    return null;
                }

                return new StatusWindowPosition(xElement.GetInt32(), yElement.GetInt32());
            }
            catch (JsonException ex)
            {
                _log.Error(LogCategory, "Status window position file was corrupt; ignoring it", ex.Message);
                return null;
            }
        }
    }

    public void Save(StatusWindowPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        lock (_gate)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("x", position.X);
                writer.WriteNumber("y", position.Y);
                writer.WriteEndObject();
            }

            var mainPath = MainPath();
            var tempPath = mainPath + ".tmp";
            File.WriteAllBytes(tempPath, stream.ToArray());
            File.Move(tempPath, mainPath, overwrite: true);

            _log.Info(LogCategory, "Saved status window position", $"x={position.X}, y={position.Y}");
        }
    }

    private string MainPath() => Path.Combine(_directory, FileName);
}

/// <summary>
/// A saved top-left corner for the Status window. See
/// <see cref="StatusWindowPositionStore"/>'s own remarks for why <c>Load</c>
/// returns null rather than a default instance of this record when nothing
/// has been saved.
/// </summary>
public sealed record StatusWindowPosition(int X, int Y);
