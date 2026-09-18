using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Core.Pairing;

/// <summary>
/// The second half of LunaPanel's LAN-facing defence, and the source of truth
/// for which physical devices are paired. A tablet must present a device's
/// own token before any command endpoint answers it, and a token is only ever
/// handed out after someone types a shared one-time 6-digit code (shown on
/// the PC console) into the tablet once.
///
/// This is the successor to the single-token <c>PairingService</c>: every
/// successful pair now mints a *new* token and a *new* device record, rather
/// than re-issuing one shared token to every device that pairs. Token
/// identity and device identity are the same thing - see
/// <c>ref/docs/device-registry-plan.md</c> for why the earlier single-token
/// design could not tell two devices apart, and why per-device layouts need
/// this rework to identify a device at all.
///
/// State (the shared code, the consecutive-failure count, and every paired
/// device) round-trips through JSON at an injected file path - this type
/// never discovers that path itself, exactly like every other file-backed
/// type under <c>LunaPanel.Core</c>. An absent file is first-run, not an
/// error; a corrupt or truncated file is regenerated rather than allowed to
/// crash the app; a pre-registry single-token file is migrated into a
/// one-device registry rather than treated as corrupt.
///
/// Neither a token nor the pairing code is ever passed to
/// <see cref="IDiagnosticLog"/> - logs here record only that an attempt
/// happened, its outcome, and (safe to log by construction) a device's id,
/// because these logs are expected to end up pasted into public bug reports.
/// </summary>
public sealed class DeviceRegistry
{
    private const int MaxConsecutiveFailures = 5;
    private const string LogCategory = "Pairing";
    private static readonly TimeSpan TouchDebounceWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long a pairing window stays open once opened - two minutes, the
    /// user's own figure. Deliberately not configurable (considered and left
    /// unasked, per <c>ref/docs/pairing-and-devices.md</c>).
    /// </summary>
    private static readonly TimeSpan PairingWindowDuration = TimeSpan.FromMinutes(2);

    private readonly string _statePath;
    private readonly IDiagnosticLog _log;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    private readonly List<DeviceRecord> _devices = new();
    private byte[] _codeUtf8 = Array.Empty<byte>();
    private int _consecutiveFailures;

    /// <summary>
    /// <c>null</c> when pairing is closed. Otherwise the instant the
    /// currently-open pairing window stops accepting new pairings. Never
    /// persisted - a restart re-derives whether pairing should start open
    /// purely from whether any device is currently registered (see
    /// <see cref="LoadOrInitialize"/>), not from a stale window left open
    /// across a restart.
    /// </summary>
    private DateTimeOffset? _pairingWindowExpiresAt;

    public DeviceRegistry(string statePath, IDiagnosticLog log, TimeProvider clock)
    {
        _statePath = statePath ?? throw new ArgumentNullException(nameof(statePath));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        LoadOrInitialize();
    }

    /// <summary>
    /// The current shared one-time pairing code to show on the PC console.
    /// One code for the whole household, regardless of how many devices are
    /// already registered. Never logged. Only usable while
    /// <see cref="IsPairingWindowOpen"/> is <c>true</c> - <see cref="TryPair"/>
    /// refuses every attempt outright while the window is closed, whatever
    /// code it presents.
    /// </summary>
    public string CurrentCode
    {
        get { lock (_gate) { return Encoding.UTF8.GetString(_codeUtf8); } }
    }

    /// <summary>
    /// Whether a new device can pair right now. Governs <em>new</em>
    /// pairings only - never touches a device that has already paired; that
    /// device's token keeps authorising every request regardless of this
    /// flag, per <c>ref/docs/pairing-and-devices.md</c>'s central invariant.
    /// </summary>
    public bool IsPairingWindowOpen
    {
        get { lock (_gate) { return IsPairingWindowOpenNoLock(); } }
    }

    private bool IsPairingWindowOpenNoLock() =>
        _pairingWindowExpiresAt is { } expiresAt && _clock.GetUtcNow() < expiresAt;

    /// <summary>
    /// Opens pairing for <see cref="PairingWindowDuration"/> from now, and
    /// rotates to a fresh code first - so a code that may have lingered
    /// unnoticed since before the window last closed is never the one
    /// reactivated. This is the operation behind both routes
    /// <c>ref/docs/pairing-and-devices.md</c> describes for opening pairing
    /// (the tray's "Add a device" and an already-paired device's "Add
    /// another device"); the caller is responsible for authenticating
    /// itself first - this method does not check who is calling.
    /// </summary>
    public void OpenPairingWindow()
    {
        lock (_gate)
        {
            RotateCode();
            _pairingWindowExpiresAt = _clock.GetUtcNow() + PairingWindowDuration;
            Save();
            _log.Info(LogCategory, "Pairing window opened");
        }
    }

    /// <summary>
    /// Every paired device, in a shape safe to hand back over HTTP - never
    /// <see cref="DeviceRecord.TokenHex"/>, which this deliberately omits.
    /// </summary>
    public IReadOnlyList<DeviceSummary> ListDevices()
    {
        lock (_gate)
        {
            return _devices
                .Select(d => new DeviceSummary(d.DeviceId, d.Name, d.DeviceClass, d.PairedAt, d.LastSeenAt))
                .ToList();
        }
    }

    /// <summary>
    /// Checks <paramref name="code"/> against the current shared pairing code
    /// in constant time. On success, mints a brand-new token and device
    /// record for <paramref name="deviceName"/> / <paramref name="deviceClass"/>,
    /// invalidates the used code (by rotating to a fresh one) so it cannot be
    /// replayed, resets the consecutive-failure count, and returns both the
    /// new token and its derived device id. On failure, counts the attempt;
    /// after <see cref="MaxConsecutiveFailures"/> consecutive failures the
    /// current code is discarded and replaced, so a guesser is chasing a
    /// moving target rather than grinding a fixed one.
    ///
    /// <b>A successful pair also closes the pairing window immediately</b>,
    /// as part of this same operation - decided with the user 2026-09-07
    /// ("it should close when a pairing happens"), see
    /// <c>ref/docs/pairing-and-devices.md</c>. The window is single-use:
    /// whatever time remained on the original two minutes is discarded, and
    /// a second device requires deliberately opening the window again via
    /// <see cref="OpenPairingWindow"/>. A failed attempt never closes it -
    /// only success does - so a commander who mistypes a digit can still
    /// retry inside their own window.
    ///
    /// <b>Refuses outright, whatever code is presented, while
    /// <see cref="IsPairingWindowOpen"/> is <c>false</c></b> - this check
    /// happens before the code is even compared, so a closed window leaves
    /// nothing to attack: no failure is counted, no rotation happens, and
    /// nothing is persisted, since no attempt against a real, live code
    /// actually took place.
    /// </summary>
    public bool TryPair(string? code, string deviceName, string deviceClass, out string? token, out string? deviceId)
    {
        ArgumentNullException.ThrowIfNull(deviceName);
        ArgumentNullException.ThrowIfNull(deviceClass);

        lock (_gate)
        {
            if (!IsPairingWindowOpenNoLock())
            {
                _log.Warn(LogCategory, "Pairing attempt rejected: window closed");
                token = null;
                deviceId = null;
                return false;
            }

            var matched = ConstantTimeStringEquals(_codeUtf8, code);

            if (matched)
            {
                _consecutiveFailures = 0;
                RotateCode();
                _pairingWindowExpiresAt = null;

                var tokenBytes = PairingSecrets.GenerateToken();
                var tokenHex = Convert.ToHexString(tokenBytes);
                var newDeviceId = ComputeDeviceId(tokenBytes);
                var now = _clock.GetUtcNow();
                _devices.Add(new DeviceRecord(newDeviceId, tokenHex, deviceName, deviceClass, now, now));

                Save();
                _log.Info(LogCategory, "Device paired", newDeviceId);

                token = tokenHex;
                deviceId = newDeviceId;
                return true;
            }

            _consecutiveFailures++;
            _log.Warn(LogCategory, "Pairing attempt failed", $"consecutiveFailures={_consecutiveFailures}");

            if (_consecutiveFailures >= MaxConsecutiveFailures)
            {
                RotateCode();
                _consecutiveFailures = 0;
                _log.Warn(LogCategory, "Pairing code rotated after too many consecutive failures");
            }

            Save();
            token = null;
            deviceId = null;
            return false;
        }
    }

    /// <summary>
    /// Checks <paramref name="presentedToken"/> against every registered
    /// device's token in constant time, returning the matching device's id.
    /// Deliberately iterates <em>every</em> device with no early exit -
    /// ORing each comparison's result together and only then deciding -
    /// so the time this takes does not vary with how many devices are
    /// registered or where in the list a correct guess would have landed.
    /// An early-exit loop would leak exactly that information through
    /// timing, which is the same class of side-channel the constant-time
    /// comparison itself exists to close. Never throws: <c>null</c>, empty,
    /// wrong-length, and wrong-but-right-length input are all just rejected.
    /// </summary>
    public bool TryAuthorize(string? presentedToken, out string? deviceId)
    {
        lock (_gate)
        {
            var anyMatch = false;
            string? matchedId = null;

            // No early exit: every device is checked, every time, regardless
            // of whether an earlier one already matched.
            foreach (var device in _devices)
            {
                var expectedUtf8 = Encoding.UTF8.GetBytes(device.TokenHex);
                var isMatch = ConstantTimeStringEquals(expectedUtf8, presentedToken);
                anyMatch |= isMatch;
                if (isMatch)
                {
                    matchedId = device.DeviceId;
                }
            }

            deviceId = anyMatch ? matchedId : null;

            if (!anyMatch)
            {
                _log.Warn(LogCategory, "Authorization attempt failed");
            }

            return anyMatch;
        }
    }

    /// <summary>
    /// Removes the device record for <paramref name="deviceId"/>. Does not
    /// touch that device's layout - the caller owns deleting or reassigning
    /// that separately. Returns <c>false</c> without effect if no such
    /// device is registered.
    /// </summary>
    public bool Forget(string deviceId)
    {
        lock (_gate)
        {
            var removed = _devices.RemoveAll(d => d.DeviceId == deviceId) > 0;
            if (removed)
            {
                Save();
                _log.Info(LogCategory, "Device forgotten", deviceId);
            }

            return removed;
        }
    }

    /// <summary>
    /// Updates <paramref name="deviceId"/>'s last-seen timestamp, debounced
    /// to at most once a minute so a chatty tablet polling frequently doesn't
    /// rewrite the state file on every request. A call inside the debounce
    /// window is a silent no-op: no field changes, nothing is saved.
    /// </summary>
    public void Touch(string deviceId)
    {
        lock (_gate)
        {
            var index = _devices.FindIndex(d => d.DeviceId == deviceId);
            if (index < 0)
            {
                return;
            }

            var device = _devices[index];
            var now = _clock.GetUtcNow();
            if (now - device.LastSeenAt < TouchDebounceWindow)
            {
                return;
            }

            _devices[index] = device with { LastSeenAt = now };
            Save();
        }
    }

    /// <summary>
    /// The first 8 bytes of SHA-256 over the raw token bytes, hex-encoded:
    /// a short public handle derived from the token, safe to log and safe to
    /// use as a filename (unlike the token itself), and not reversible back
    /// to the secret it was derived from.
    /// </summary>
    private static string ComputeDeviceId(byte[] tokenBytes)
    {
        var hash = SHA256.HashData(tokenBytes);
        return Convert.ToHexString(hash, 0, 8);
    }

    /// <summary>
    /// Compares <paramref name="presented"/> (UTF-8 encoded) against
    /// <paramref name="expectedUtf8"/> without letting the comparison's
    /// timing reveal whether the lengths matched. <paramref name="presented"/>
    /// is always copied into a buffer exactly <paramref name="expectedUtf8"/>'s
    /// length before <see cref="CryptographicOperations.FixedTimeEquals"/>
    /// is called, so that call always sees two equal-length spans and its
    /// own internal length check never takes a different path depending on
    /// what the caller sent. The separate length check below is combined
    /// with a bitwise AND (not <c>&amp;&amp;</c>) so the C# compiler cannot
    /// introduce a short-circuit branch around the comparison either.
    /// </summary>
    private static bool ConstantTimeStringEquals(byte[] expectedUtf8, string? presented)
    {
        var presentedUtf8 = Encoding.UTF8.GetBytes(presented ?? string.Empty);
        var buffer = new byte[expectedUtf8.Length];
        var copyLength = Math.Min(presentedUtf8.Length, buffer.Length);
        Array.Copy(presentedUtf8, buffer, copyLength);

        var lengthMatches = presentedUtf8.Length == expectedUtf8.Length;
        var contentMatches = CryptographicOperations.FixedTimeEquals(buffer, expectedUtf8);
        return lengthMatches & contentMatches;
    }

    private void RotateCode()
    {
        _codeUtf8 = Encoding.UTF8.GetBytes(PairingSecrets.GenerateCode());
    }

    private void LoadOrInitialize()
    {
        lock (_gate)
        {
            if (TryLoadFromDisk(out var state))
            {
                _codeUtf8 = Encoding.UTF8.GetBytes(state.Code);
                _consecutiveFailures = state.ConsecutiveFailures;
                _devices.Clear();
                _devices.AddRange(state.Devices);
                _log.Info(LogCategory, "Loaded device registry state from disk");
            }
            else
            {
                RotateCode();
                _consecutiveFailures = 0;
                _devices.Clear();
                Save();
                _log.Info(LogCategory, "Generated new device registry state (first run or unreadable state file)");
            }

            // First run is the exception to "closed by default": with no
            // device registered - whether this is a genuinely fresh install
            // or every device has since been forgotten - nothing could ever
            // pair without this, since there would be no already-paired
            // device to open the window from and no tray running yet
            // either. Uses whatever code was just loaded or generated above
            // rather than forcing a fresh rotation, since nothing could have
            // exposed or spent that code before the window ever opened.
            if (_devices.Count == 0)
            {
                _pairingWindowExpiresAt = _clock.GetUtcNow() + PairingWindowDuration;
                _log.Info(LogCategory, "Pairing window opened automatically: no devices registered");
            }
        }
    }

    private bool TryLoadFromDisk(out DeviceRegistryPersistedState state)
    {
        state = null!;

        if (!File.Exists(_statePath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(_statePath);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (root.TryGetProperty("Devices", out _))
            {
                return TryLoadCurrentFormat(json, out state);
            }

            if (root.TryGetProperty("TokenHex", out var tokenHexElement) && root.TryGetProperty("Code", out var codeElement))
            {
                return TryMigrateLegacySingleTokenFormat(root, tokenHexElement, codeElement, out state);
            }

            return false;
        }
        catch (Exception ex)
        {
            // A corrupt or truncated state file must never bring the app
            // down - regenerate instead. Deliberately broad: truncation and
            // hand-edited garbage can surface as several different exception
            // types (JsonException, FormatException, and others), and the
            // contract here is "never throws", not "never throws for the
            // exception types we thought of".
            _log.Warn(LogCategory, "Device registry state file unreadable or corrupt; regenerating", ex.GetType().Name);
            return false;
        }
    }

    private bool TryLoadCurrentFormat(string json, out DeviceRegistryPersistedState state)
    {
        state = null!;

        var loaded = JsonSerializer.Deserialize<DeviceRegistryPersistedState>(json);
        if (loaded is null || loaded.Devices is null || !IsValidCode(loaded.Code))
        {
            return false;
        }

        foreach (var device in loaded.Devices)
        {
            if (!IsValidDeviceRecord(device))
            {
                return false;
            }
        }

        state = loaded;
        return true;
    }

    private bool TryMigrateLegacySingleTokenFormat(
        JsonElement root,
        JsonElement tokenHexElement,
        JsonElement codeElement,
        out DeviceRegistryPersistedState state)
    {
        state = null!;

        var tokenHex = tokenHexElement.GetString();
        var code = codeElement.GetString();
        var consecutiveFailures = root.TryGetProperty("ConsecutiveFailures", out var failuresElement)
            ? failuresElement.GetInt32()
            : 0;

        if (string.IsNullOrEmpty(tokenHex) || !IsValidCode(code))
        {
            return false;
        }

        var tokenBytes = Convert.FromHexString(tokenHex);
        if (tokenBytes.Length != PairingSecrets.TokenLengthBytes)
        {
            return false;
        }

        var deviceId = ComputeDeviceId(tokenBytes);
        var now = _clock.GetUtcNow();
        var migratedDevice = new DeviceRecord(deviceId, tokenHex, "Migrated device", "unknown", now, now);

        state = new DeviceRegistryPersistedState(code!, consecutiveFailures, new List<DeviceRecord> { migratedDevice });
        _log.Info(LogCategory, "Migrated legacy single-token pairing state into device registry", deviceId);
        return true;
    }

    private static bool IsValidDeviceRecord(DeviceRecord device)
    {
        if (string.IsNullOrEmpty(device.DeviceId) || string.IsNullOrEmpty(device.TokenHex))
        {
            return false;
        }

        try
        {
            return Convert.FromHexString(device.TokenHex).Length == PairingSecrets.TokenLengthBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsValidCode(string? code) =>
        code is not null && code.Length == 6 && code.All(char.IsAsciiDigit);

    private void Save()
    {
        var state = new DeviceRegistryPersistedState(
            Encoding.UTF8.GetString(_codeUtf8),
            _consecutiveFailures,
            new List<DeviceRecord>(_devices));
        var json = JsonSerializer.Serialize(state);

        var directory = Path.GetDirectoryName(_statePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_statePath, json);
    }
}
