using LunaPanel.Core.Bindings;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Server.Discovery;

namespace LunaPanel.Server.Bindings;

/// <summary>
/// Re-reads and re-parses the player's <c>.binds</c> file fresh on every
/// call, from whichever path <see cref="PresetSelector"/>'s rule currently
/// selects (<c>ref/docs/bindings-source.md</c>) - so a rebind made in Elite
/// while LunaPanel is running is picked up on the very next panel request,
/// no restart required. This is the same "a layout can't break, only
/// degrade" philosophy <c>LayoutAnnotator</c> already relies on (see
/// <c>ref/docs/layouts.md</c>), extended to cover the file content itself,
/// not only the annotation step.
///
/// Reads through a <see cref="DiscoveryResultHolder"/>, not a frozen
/// <see cref="PathDiscoveryResult"/> directly - this is what makes "Refresh
/// bindings" actually take effect without a restart: the holder's
/// <see cref="DiscoveryResultHolder.Current"/> is re-read at the top of
/// every <see cref="Read"/> call, so a refresh that lands between two panel
/// requests is picked up by the very next one. Switching preset in Elite, or
/// creating a preset that did not exist when the server started, is still
/// invisible until either a restart or an explicit refresh - discovery does
/// not run on a timer or a file-system watch, only on demand.
///
/// Never throws and never returns <see langword="null"/>: an absent,
/// unreadable, or unparseable bindings file degrades to an empty
/// <see cref="BindingsFile"/> (no elements at all), so every action in a
/// panel request shows as unbound/unknown rather than the request failing.
/// </summary>
public sealed class LiveBindingsReader
{
    private static readonly BindingsFile Empty = new(null, null, null, Array.Empty<BindingElement>());

    private readonly DiscoveryResultHolder _discovery;
    private readonly IDiagnosticLog _log;

    public LiveBindingsReader(DiscoveryResultHolder discovery, IDiagnosticLog log)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public BindingsFile Read()
    {
        var path = _discovery.Current.BindingsSelection.SelectedFilePath;
        if (path is null)
        {
            return Empty;
        }

        string xml;
        try
        {
            xml = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn("Binds", "Bindings file could not be read for a live panel/press request", ex.GetType().Name);
            return Empty;
        }

        var result = BindingsFile.Parse(xml);
        if (!result.Success)
        {
            _log.Warn("Binds", "Bindings file could not be parsed for a live panel/press request", result.Error);
            return Empty;
        }

        return result.File!;
    }
}
