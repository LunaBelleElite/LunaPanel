using System.Text.Json;
using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Core.Layouts;

/// <summary>
/// Reads and writes <see cref="OrphanMarker"/> sidecar files under an
/// injected directory - never discovers that directory itself, the same
/// discipline <see cref="LayoutStore"/>, <see cref="PanelSettingsStore"/> and
/// <c>ThemeOverrideStore</c> already follow, and in practice the same
/// directory all three use.
///
/// <b>Why a sidecar file rather than a field on the device registry.</b> The
/// marker has to survive the very operation that removes the registry
/// record, so it cannot live in that record. It could have lived in a second
/// section of the registry's own state file, but that file is the one
/// <c>DeviceRegistry</c> loads, migrates and rewrites around its
/// constant-time comparison path, which has source-scan pins on it precisely
/// because a plausible-looking edit there reopens a timing side-channel
/// (<c>ref/docs/pairing.md</c>). Keeping the marker beside the layout it
/// describes leaves that path untouched, puts the description next to the
/// only thing it describes, and makes the import list a directory read
/// rather than a cross-subsystem lookup.
///
/// One file per forgotten device, <c>orphan-&lt;deviceId&gt;.json</c>, in the
/// same <c>&lt;kind&gt;-&lt;deviceId&gt;.json</c> shape the sibling stores
/// already use. Writing one is best-effort: a marker that cannot be written
/// costs a readable line on an import screen, and must never be the reason a
/// commander cannot revoke a lost device.
/// </summary>
public sealed class OrphanMarkerStore
{
    private const string LogCategory = "Layout";
    private const string FilePrefix = "orphan-";
    private const string FileSuffix = ".json";

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);

    private readonly string _directory;
    private readonly IDiagnosticLog _log;
    private readonly object _gate = new();

    public OrphanMarkerStore(string directory, IDiagnosticLog log)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        Directory.CreateDirectory(_directory);
    }

    /// <summary>
    /// Writes (or replaces) the marker for <see cref="OrphanMarker.DeviceId"/>.
    /// Returns <see langword="false"/>, having logged, when the write failed -
    /// the caller is a forget, and a commander revoking a lost device must
    /// never be blocked by a descriptive file they did not ask for.
    /// </summary>
    public bool Write(OrphanMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);

        lock (_gate)
        {
            var mainPath = MainPath(marker.DeviceId);
            var tempPath = mainPath + ".tmp";

            try
            {
                using (var stream = new MemoryStream())
                {
                    using (var writer = new Utf8JsonWriter(stream))
                    {
                        writer.WriteStartObject();
                        writer.WriteString("deviceId", marker.DeviceId);
                        writer.WriteString("name", marker.Name);
                        writer.WriteString("deviceClass", marker.DeviceClass);
                        writer.WriteString("lastSeenAt", marker.LastSeenAt);
                        writer.WriteEndObject();
                    }

                    File.WriteAllBytes(tempPath, stream.ToArray());
                }

                File.Move(tempPath, mainPath, overwrite: true);
                _log.Info(LogCategory, "Wrote an orphan layout marker", $"deviceId={marker.DeviceId}");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warn(LogCategory, "Could not write an orphan layout marker", ex.GetType().Name);
                return false;
            }
        }
    }

    /// <summary>
    /// Every marker in the directory. A file that will not parse, or that
    /// parses but names no device, is skipped rather than throwing: one
    /// damaged sidecar must not take down the whole import list, which is
    /// the only route back to every other layout it describes.
    /// </summary>
    public IReadOnlyList<OrphanMarker> List()
    {
        lock (_gate)
        {
            var markers = new List<OrphanMarker>();

            foreach (var path in Directory.EnumerateFiles(_directory))
            {
                var fileName = Path.GetFileName(path);
                if (!fileName.StartsWith(FilePrefix, StringComparison.Ordinal) ||
                    !fileName.EndsWith(FileSuffix, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    var marker = JsonSerializer.Deserialize<OrphanMarker>(File.ReadAllText(path), ReadOptions);
                    if (marker is null || string.IsNullOrWhiteSpace(marker.DeviceId))
                    {
                        _log.Warn(LogCategory, "Skipped an orphan layout marker that named no device");
                        continue;
                    }

                    markers.Add(marker);
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    _log.Warn(LogCategory, "Skipped an unreadable orphan layout marker", ex.GetType().Name);
                }
            }

            return markers;
        }
    }

    /// <summary>
    /// Removes the marker for <paramref name="deviceId"/>, if present. A
    /// missing marker is a no-op, not a failure - the same reasoning
    /// <see cref="Write"/>'s own remarks give: a marker that fails to delete
    /// must never be the reason a discard (<c>ref/docs/layout-import.md</c>)
    /// is refused, so this never throws for an ordinary IO failure either.
    /// </summary>
    public bool Delete(string deviceId)
    {
        lock (_gate)
        {
            var mainPath = MainPath(deviceId);
            if (!File.Exists(mainPath))
            {
                return false;
            }

            try
            {
                File.Delete(mainPath);
                _log.Info(LogCategory, "Deleted an orphan layout marker", $"deviceId={deviceId}");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warn(LogCategory, "Could not delete an orphan layout marker", ex.GetType().Name);
                return false;
            }
        }
    }

    private string MainPath(string deviceId) => Path.Combine(_directory, $"{FilePrefix}{deviceId}{FileSuffix}");
}
