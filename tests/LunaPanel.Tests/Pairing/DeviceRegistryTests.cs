using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Pairing;

namespace LunaPanel.Tests.Pairing;

/// <summary>
/// Drives <see cref="DeviceRegistry"/> against real temp files under the
/// test output (never the repo tree or the user's real profile), a captured
/// in-memory <see cref="IDiagnosticLog"/>, and a manually-advanced
/// <see cref="TimeProvider"/> (for the <c>Touch</c> debounce window and
/// paired-at/last-seen stamps), so behaviour, persistence, migration, and
/// logging can all be pinned.
/// </summary>
public class DeviceRegistryTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A 32-byte token chosen by hand (not randomly generated) so its
    /// SHA-256-derived device id can be precomputed once, offline, and
    /// asserted as a literal - see the report for how
    /// <c>630DCD2966C43366</c> was computed independently of this
    /// production code.
    /// </summary>
    private const string KnownTokenHexA = "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F";
    private const string KnownDeviceIdA = "630DCD2966C43366";

    /// <summary>A second hand-chosen token/id pair, distinct from A.</summary>
    private const string KnownTokenHexB = "1F1E1D1C1B1A191817161514131211100F0E0D0C0B0A09080706050403020100";
    private const string KnownDeviceIdB = "69C55C9002EB8C7A";

    /// <summary>Captures every <see cref="DiagnosticEvent"/> written to it, for assertions.</summary>
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();

        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    /// <summary>A TimeProvider whose UtcNow is set directly by the test.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private static string NewStatePath([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "pairing", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "device-registry-state.json");
    }

    private static void WriteLegacySingleTokenFile(string path, string tokenHex, string code, int consecutiveFailures)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var json = $"{{\"TokenHex\":\"{tokenHex}\",\"Code\":\"{code}\",\"ConsecutiveFailures\":{consecutiveFailures}}}";
        File.WriteAllText(path, json);
    }

    private static DeviceRegistry NewRegistry(string path, IDiagnosticLog log, TimeProvider? clock = null) =>
        new(path, log, clock ?? new ManualTimeProvider { UtcNow = Epoch });

    // --- PairingSecrets (unchanged by this rework; carried forward) ---

    [Fact]
    public void GenerateToken_Is32BytesAndDiffersAcrossCalls()
    {
        var first = PairingSecrets.GenerateToken();
        var second = PairingSecrets.GenerateToken();

        Assert.Equal(32, first.Length);
        Assert.Equal(32, second.Length);
        Assert.NotEqual(Convert.ToHexString(first), Convert.ToHexString(second));
    }

    [Fact]
    public void GenerateCode_IsExactlySixDigits()
    {
        for (var i = 0; i < 100; i++)
        {
            var code = PairingSecrets.GenerateCode();
            Assert.Equal(6, code.Length);
            Assert.True(code.All(char.IsAsciiDigit), $"Expected all-digit code, got '{code}'");
        }
    }

    [Fact]
    public void GenerateCode_OverManyGenerations_ShowsNoObviousModuloBias()
    {
        const int sampleSize = 60_000;
        var buckets = new int[10];

        for (var i = 0; i < sampleSize; i++)
        {
            var code = PairingSecrets.GenerateCode();
            buckets[code[0] - '0']++;
        }

        const int expectedPerBucket = sampleSize / 10;
        const double tolerance = 0.15;

        foreach (var count in buckets)
        {
            Assert.InRange(count, expectedPerBucket * (1 - tolerance), expectedPerBucket * (1 + tolerance));
        }
    }

    // --- TryPair: minting a device per successful pair ---

    [Fact]
    public void TryPair_CorrectCode_Succeeds_AndReturnsTokenAndDeviceId()
    {
        var log = new CapturingDiagnosticLog();
        var registry = NewRegistry(NewStatePath(), log);
        var code = registry.CurrentCode;

        var paired = registry.TryPair(code, "Kitchen tablet", "tablet", out var token, out var deviceId);

        Assert.True(paired);
        Assert.NotNull(token);
        Assert.NotNull(deviceId);
        Assert.True(registry.TryAuthorize(token, out var authorizedId));
        Assert.Equal(deviceId, authorizedId);
    }

    [Fact]
    public void TryPair_TwoSuccessivePairs_AcrossReopenedWindows_ProduceDifferentTokensAndDifferentDeviceIds()
    {
        var log = new CapturingDiagnosticLog();
        var registry = NewRegistry(NewStatePath(), log);

        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out var token1, out var id1));

        // The first pair closed the window - reopen deliberately for the second.
        registry.OpenPairingWindow();
        Assert.True(registry.TryPair(registry.CurrentCode, "Phone", "phone", out var token2, out var id2));

        Assert.NotEqual(token1, token2);
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void TryAuthorize_EachDevicesOwnToken_ReturnsItsOwnId_NotTheOthers()
    {
        var log = new CapturingDiagnosticLog();
        var registry = NewRegistry(NewStatePath(), log);

        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out var token1, out var id1));
        registry.OpenPairingWindow();
        Assert.True(registry.TryPair(registry.CurrentCode, "Phone", "phone", out var token2, out var id2));

        Assert.True(registry.TryAuthorize(token1, out var resolved1));
        Assert.Equal(id1, resolved1);

        Assert.True(registry.TryAuthorize(token2, out var resolved2));
        Assert.Equal(id2, resolved2);
    }

    [Fact]
    public void TryAuthorize_WrongToken_ReturnsFalse_WithoutThrowing()
    {
        var log = new CapturingDiagnosticLog();
        var registry = NewRegistry(NewStatePath(), log);
        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out var realToken, out _));

        var flippedFirstChar = realToken![0] == 'A' ? 'B' : 'A';
        var wrongToken = flippedFirstChar + realToken[1..];

        Assert.False(registry.TryAuthorize(null, out var d1));
        Assert.Null(d1);
        Assert.False(registry.TryAuthorize(string.Empty, out var d2));
        Assert.Null(d2);
        Assert.False(registry.TryAuthorize("short", out var d3));
        Assert.Null(d3);
        Assert.False(registry.TryAuthorize(realToken + "extra", out var d4));
        Assert.Null(d4);
        Assert.False(registry.TryAuthorize(wrongToken, out var d5));
        Assert.Null(d5);
    }

    [Fact]
    public void TryAuthorize_NoDevicesRegisteredYet_ReturnsFalse_WithoutThrowing()
    {
        var log = new CapturingDiagnosticLog();
        var registry = NewRegistry(NewStatePath(), log);

        Assert.False(registry.TryAuthorize("anything", out var deviceId));
        Assert.Null(deviceId);
    }

    [Fact]
    public void TryPair_ReplayOfSameCode_Fails()
    {
        var log = new CapturingDiagnosticLog();
        var registry = NewRegistry(NewStatePath(), log);
        var code = registry.CurrentCode;

        Assert.True(registry.TryPair(code, "Tablet", "tablet", out _, out _));

        var replayed = registry.TryPair(code, "Tablet2", "tablet", out var token, out var deviceId);

        Assert.False(replayed);
        Assert.Null(token);
        Assert.Null(deviceId);
    }

    [Fact]
    public void TryPair_WrongCode_Fails_AndReturnsNullTokenAndDeviceId()
    {
        var log = new CapturingDiagnosticLog();
        var registry = NewRegistry(NewStatePath(), log);
        var wrongCode = registry.CurrentCode == "000000" ? "111111" : "000000";

        var paired = registry.TryPair(wrongCode, "Tablet", "tablet", out var token, out var deviceId);

        Assert.False(paired);
        Assert.Null(token);
        Assert.Null(deviceId);
    }

    [Fact]
    public void TryPair_FiveWrongAttempts_WithSeveralDevicesRegistered_RotatesCode_PreviousCodeNoLongerWorks()
    {
        var log = new CapturingDiagnosticLog();
        var registry = NewRegistry(NewStatePath(), log);

        // Several devices already registered before the lockout is driven.
        // Each pair closes the window, so it is reopened deliberately before
        // the next one - a window is single-use, not a standing invitation.
        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out _, out _));
        registry.OpenPairingWindow();
        Assert.True(registry.TryPair(registry.CurrentCode, "Phone", "phone", out _, out _));
        registry.OpenPairingWindow();
        Assert.True(registry.TryPair(registry.CurrentCode, "Laptop", "laptop", out _, out _));

        // Laptop's pair above closed the window too; reopen so the five
        // wrong attempts below are actually evaluated against the live code
        // rather than rejected outright by the closed-window gate.
        registry.OpenPairingWindow();
        var originalCode = registry.CurrentCode;
        var wrongCode = originalCode == "000000" ? "111111" : "000000";

        for (var i = 0; i < 5; i++)
        {
            Assert.False(registry.TryPair(wrongCode, "Intruder", "unknown", out _, out _));
        }

        Assert.NotEqual(originalCode, registry.CurrentCode);
        Assert.False(registry.TryPair(originalCode, "Intruder", "unknown", out var token, out var deviceId));
        Assert.Null(token);
        Assert.Null(deviceId);
    }

    [Fact]
    public void TryPair_SuccessfulPair_ResetsFailureCounter_WithSeveralDevicesRegistered()
    {
        var log = new CapturingDiagnosticLog();
        var registry = NewRegistry(NewStatePath(), log);

        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out _, out _));
        registry.OpenPairingWindow();
        Assert.True(registry.TryPair(registry.CurrentCode, "Phone", "phone", out _, out _));

        // Phone's pair closed the window; reopen before probing the failure
        // counter, or every attempt below would be rejected outright by the
        // closed-window gate rather than actually exercising the counter.
        registry.OpenPairingWindow();
        var wrongCode = registry.CurrentCode == "000000" ? "111111" : "000000";

        // 4 consecutive failures - one short of the 5-failure lockout.
        for (var i = 0; i < 4; i++)
        {
            Assert.False(registry.TryPair(wrongCode, "Intruder", "unknown", out _, out _));
        }

        // A successful pair should reset the counter to 0, not leave it at 4.
        var codeBeforeSuccess = registry.CurrentCode;
        Assert.True(registry.TryPair(codeBeforeSuccess, "Laptop", "laptop", out _, out _));

        // That pair closed the window again; reopen before capturing the
        // live code and probing the counter a second time.
        registry.OpenPairingWindow();
        var codeAfterSuccess = registry.CurrentCode;

        // If the counter had NOT reset, it would already sit at 4 and a
        // further 4 wrong attempts (cumulative 8, crossing 5) would rotate
        // the code away before we get to try it. With a real reset, 4 more
        // wrong attempts leaves the counter at 4 again (still under 5), and
        // codeAfterSuccess must still be the live, un-rotated code.
        for (var i = 0; i < 4; i++)
        {
            Assert.False(registry.TryPair(wrongCode, "Intruder", "unknown", out _, out _));
        }

        Assert.True(registry.TryPair(codeAfterSuccess, "FourthDevice", "unknown", out var token, out var deviceId));
        Assert.NotNull(token);
        Assert.NotNull(deviceId);
    }

    // --- Pairing window: closed by default, opened by a two-minute window,
    // auto-open only on first run (no devices registered). See
    // ref/docs/pairing-and-devices.md. ---

    [Fact]
    public void IsPairingWindowOpen_TrueOnAFreshRegistry_WithNoDevices()
    {
        var registry = NewRegistry(NewStatePath(), new CapturingDiagnosticLog());

        Assert.True(registry.IsPairingWindowOpen);
    }

    [Fact]
    public void IsPairingWindowOpen_FalseOnReload_OnceADeviceIsRegistered()
    {
        var path = NewStatePath();
        var registry = NewRegistry(path, new CapturingDiagnosticLog());
        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out _, out _));

        // A fresh instance over the SAME state file - simulating a restart -
        // must NOT auto-open, because a device is already registered.
        var reloaded = NewRegistry(path, new CapturingDiagnosticLog());

        Assert.False(reloaded.IsPairingWindowOpen);
    }

    [Fact]
    public void TryPair_WindowClosed_RejectsTheCorrectCode_AndReturnsNullTokenAndDeviceId()
    {
        var path = NewStatePath();
        var registry = NewRegistry(path, new CapturingDiagnosticLog());
        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out _, out _));

        var reloaded = NewRegistry(path, new CapturingDiagnosticLog());
        Assert.False(reloaded.IsPairingWindowOpen);
        var correctCode = reloaded.CurrentCode;

        var paired = reloaded.TryPair(correctCode, "Phone", "phone", out var token, out var deviceId);

        Assert.False(paired);
        Assert.Null(token);
        Assert.Null(deviceId);
    }

    [Fact]
    public void TryPair_WindowClosed_DoesNotRotateCode_OrTouchTheFailureCounter_OrPersistAnything()
    {
        var path = NewStatePath();
        var registry = NewRegistry(path, new CapturingDiagnosticLog());
        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out _, out _));

        var reloaded = NewRegistry(path, new CapturingDiagnosticLog());
        Assert.False(reloaded.IsPairingWindowOpen);
        var codeBefore = reloaded.CurrentCode;
        var contentBefore = File.ReadAllText(path);

        // Several attempts against a closed window, correct code and wrong,
        // must never rotate the code, never persist, and never move the
        // registry any closer to the 5-failure lockout rotation - there is
        // nothing to defend against while nothing can succeed anyway.
        for (var i = 0; i < 6; i++)
        {
            Assert.False(reloaded.TryPair("000000", "Intruder", "unknown", out _, out _));
        }
        Assert.False(reloaded.TryPair(codeBefore, "Intruder", "unknown", out _, out _));

        Assert.Equal(codeBefore, reloaded.CurrentCode);
        Assert.Equal(contentBefore, File.ReadAllText(path));
    }

    [Fact]
    public void OpenPairingWindow_OpensPairing_AndRotatesTheCode_AndTheNewCodeThenPairs()
    {
        var path = NewStatePath();
        var registry = NewRegistry(path, new CapturingDiagnosticLog());
        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out _, out _));

        var reloaded = NewRegistry(path, new CapturingDiagnosticLog());
        Assert.False(reloaded.IsPairingWindowOpen);
        var codeBeforeOpen = reloaded.CurrentCode;

        reloaded.OpenPairingWindow();

        Assert.True(reloaded.IsPairingWindowOpen);
        Assert.NotEqual(codeBeforeOpen, reloaded.CurrentCode);
        Assert.True(reloaded.TryPair(reloaded.CurrentCode, "Phone", "phone", out var token, out var deviceId));
        Assert.NotNull(token);
        Assert.NotNull(deviceId);
    }

    [Fact]
    public void TryPair_WindowExpiresAfterTwoMinutes_RejectsTheCorrectCode()
    {
        var clock = new ManualTimeProvider { UtcNow = Epoch };
        var registry = NewRegistry(NewStatePath(), new CapturingDiagnosticLog(), clock);
        Assert.True(registry.IsPairingWindowOpen);
        var code = registry.CurrentCode;

        clock.UtcNow = Epoch + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1);

        Assert.False(registry.IsPairingWindowOpen);
        Assert.False(registry.TryPair(code, "Tablet", "tablet", out var token, out var deviceId));
        Assert.Null(token);
        Assert.Null(deviceId);
    }

    /// <summary>
    /// The window is single-use, decided with the user 2026-09-07 ("it
    /// should close when a pairing happens"): a successful pair closes the
    /// window as part of the same operation, whatever time remains on the
    /// original two minutes. A second device is never let in on the strength
    /// of one code being typed once - adding it requires deliberately
    /// opening the window again.
    /// </summary>
    [Fact]
    public void TryPair_SuccessfulPair_ClosesTheWindowImmediately_SecondAttemptFailsUntilReopened()
    {
        var clock = new ManualTimeProvider { UtcNow = Epoch };
        var registry = NewRegistry(NewStatePath(), new CapturingDiagnosticLog(), clock);
        Assert.True(registry.IsPairingWindowOpen);

        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out _, out _));

        // Only a second has passed - well within the original two-minute
        // window - but the successful pair above must have closed it already.
        clock.UtcNow = Epoch + TimeSpan.FromSeconds(1);
        Assert.False(registry.IsPairingWindowOpen);

        var codeAfterPair = registry.CurrentCode;
        var secondAttempt = registry.TryPair(codeAfterPair, "Phone", "phone", out var token, out var deviceId);

        Assert.False(secondAttempt);
        Assert.Null(token);
        Assert.Null(deviceId);

        // Deliberately reopening still works, and pairs a second device fine.
        registry.OpenPairingWindow();
        Assert.True(registry.TryPair(registry.CurrentCode, "Phone", "phone", out var token2, out var deviceId2));
        Assert.NotNull(token2);
        Assert.NotNull(deviceId2);
    }

    /// <summary>
    /// A failed attempt must never close the window - the closing behaviour
    /// above is scoped to success only, so a commander who mistypes a digit
    /// can still try again inside their own two-minute window.
    /// </summary>
    [Fact]
    public void TryPair_FailedAttempt_DoesNotCloseTheWindow()
    {
        var registry = NewRegistry(NewStatePath(), new CapturingDiagnosticLog());
        var wrongCode = registry.CurrentCode == "000000" ? "111111" : "000000";

        Assert.False(registry.TryPair(wrongCode, "Intruder", "unknown", out _, out _));

        Assert.True(registry.IsPairingWindowOpen);
        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out var token, out var deviceId));
        Assert.NotNull(token);
        Assert.NotNull(deviceId);
    }

    /// <summary>
    /// The central invariant of the whole feature, named directly in
    /// <c>ref/docs/pairing-and-devices.md</c>: "a panel that silently dropped
    /// its own devices two minutes after setup would be indistinguishable
    /// from broken." The window governs NEW pairings only - a device that
    /// already paired keeps authorising every request forever, whether the
    /// window that let it in is still open, has expired, or was never
    /// reopened again.
    /// </summary>
    [Fact]
    public void ClosingThePairingWindow_NeverDeauthorisesAnAlreadyPairedDevice()
    {
        var clock = new ManualTimeProvider { UtcNow = Epoch };
        var registry = NewRegistry(NewStatePath(), new CapturingDiagnosticLog(), clock);
        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out var token, out var deviceId));

        // Well past the two-minute window, and with no reopen in between.
        clock.UtcNow = Epoch + TimeSpan.FromHours(6);

        Assert.False(registry.IsPairingWindowOpen);
        Assert.True(registry.TryAuthorize(token, out var authorizedId));
        Assert.Equal(deviceId, authorizedId);
    }

    // --- Devices view: never the token ---

    [Fact]
    public void ListDevices_ReturnsNameClassPairedAtAndLastSeenAt_NeverTheToken()
    {
        var clock = new ManualTimeProvider { UtcNow = Epoch };
        var registry = NewRegistry(NewStatePath(), new CapturingDiagnosticLog(), clock);
        Assert.True(registry.TryPair(registry.CurrentCode, "Kitchen tablet", "tablet", out var token, out var deviceId));

        var devices = registry.ListDevices();

        var summary = Assert.Single(devices);
        Assert.Equal(deviceId, summary.DeviceId);
        Assert.Equal("Kitchen tablet", summary.Name);
        Assert.Equal("tablet", summary.DeviceClass);
        Assert.Equal(Epoch, summary.PairedAt);
        Assert.Equal(Epoch, summary.LastSeenAt);

        // DeviceSummary carries no token property at all, so there is no
        // field to accidentally serialize it through - but pin the
        // stringified form too, in case a future field addition changes that.
        Assert.DoesNotContain(token!, summary.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ListDevices_ReturnsEveryPairedDevice_InAnyOrder()
    {
        var registry = NewRegistry(NewStatePath(), new CapturingDiagnosticLog());
        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out _, out var id1));
        registry.OpenPairingWindow();
        Assert.True(registry.TryPair(registry.CurrentCode, "Phone", "phone", out _, out var id2));

        var ids = registry.ListDevices().Select(d => d.DeviceId).ToList();

        Assert.Contains(id1, ids);
        Assert.Contains(id2, ids);
        Assert.Equal(2, ids.Count);
    }

    // --- deviceId derivation ---

    [Fact]
    public void DeviceId_IsDeterministicForAGivenToken_AndIs16HexCharacters()
    {
        var pathA1 = NewStatePath();
        WriteLegacySingleTokenFile(pathA1, KnownTokenHexA, "123456", 0);
        var registryA1 = NewRegistry(pathA1, new CapturingDiagnosticLog());

        Assert.True(registryA1.TryAuthorize(KnownTokenHexA, out var deviceIdA1));
        Assert.Equal(KnownDeviceIdA, deviceIdA1);
        Assert.Equal(16, deviceIdA1!.Length);

        // A second, independent registry over a second file, given the exact
        // same token bytes, must derive the identical id - proving the
        // derivation is a pure function of the token, not seeded by
        // anything instance- or file-specific.
        var pathA2 = NewStatePath();
        WriteLegacySingleTokenFile(pathA2, KnownTokenHexA, "654321", 3);
        var registryA2 = NewRegistry(pathA2, new CapturingDiagnosticLog());

        Assert.True(registryA2.TryAuthorize(KnownTokenHexA, out var deviceIdA2));
        Assert.Equal(KnownDeviceIdA, deviceIdA2);
    }

    [Fact]
    public void DeviceId_DiffersForDifferentTokens()
    {
        var pathA = NewStatePath();
        WriteLegacySingleTokenFile(pathA, KnownTokenHexA, "123456", 0);
        var registryA = NewRegistry(pathA, new CapturingDiagnosticLog());
        Assert.True(registryA.TryAuthorize(KnownTokenHexA, out var deviceIdA));
        Assert.Equal(KnownDeviceIdA, deviceIdA);

        var pathB = NewStatePath();
        WriteLegacySingleTokenFile(pathB, KnownTokenHexB, "123456", 0);
        var registryB = NewRegistry(pathB, new CapturingDiagnosticLog());
        Assert.True(registryB.TryAuthorize(KnownTokenHexB, out var deviceIdB));
        Assert.Equal(KnownDeviceIdB, deviceIdB);

        Assert.NotEqual(deviceIdA, deviceIdB);
    }

    // --- Forget ---

    [Fact]
    public void Forget_RemovesExactlyOneDevice_LeavesOthersAuthorizing()
    {
        var path = NewStatePath();
        var log = new CapturingDiagnosticLog();
        var registry = NewRegistry(path, log);

        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out var token1, out var id1));
        registry.OpenPairingWindow();
        Assert.True(registry.TryPair(registry.CurrentCode, "Phone", "phone", out var token2, out var id2));
        registry.OpenPairingWindow();
        Assert.True(registry.TryPair(registry.CurrentCode, "Laptop", "laptop", out var token3, out var id3));

        var forgotten = registry.Forget(id2!);

        Assert.True(forgotten);
        Assert.False(registry.TryAuthorize(token2, out var afterForget2));
        Assert.Null(afterForget2);

        Assert.True(registry.TryAuthorize(token1, out var afterForget1));
        Assert.Equal(id1, afterForget1);
        Assert.True(registry.TryAuthorize(token3, out var afterForget3));
        Assert.Equal(id3, afterForget3);

        // Forget must persist, not just mutate memory - reload from disk and
        // confirm the forgotten device stays gone while the others survive.
        var reloaded = NewRegistry(path, new CapturingDiagnosticLog());
        Assert.False(reloaded.TryAuthorize(token2, out _));
        Assert.True(reloaded.TryAuthorize(token1, out var reloadedId1));
        Assert.Equal(id1, reloadedId1);
        Assert.True(reloaded.TryAuthorize(token3, out var reloadedId3));
        Assert.Equal(id3, reloadedId3);
    }

    [Fact]
    public void Forget_UnknownDeviceId_ReturnsFalse_WithoutThrowing()
    {
        var log = new CapturingDiagnosticLog();
        var registry = NewRegistry(NewStatePath(), log);
        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out _, out var realId));

        var exception = Record.Exception(() => registry.Forget("NOTAREALDEVICE0"));

        Assert.Null(exception);
        Assert.False(registry.Forget("NOTAREALDEVICE0"));
        Assert.NotNull(realId);
    }

    // --- Touch debounce ---

    [Fact]
    public void Touch_WithinDebounceWindow_DoesNotRewriteFile()
    {
        var path = NewStatePath();
        var log = new CapturingDiagnosticLog();
        var clock = new ManualTimeProvider { UtcNow = Epoch };
        var registry = NewRegistry(path, log, clock);
        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out _, out var deviceId));

        var contentAfterPair = File.ReadAllText(path);

        clock.UtcNow = Epoch + TimeSpan.FromSeconds(30);
        registry.Touch(deviceId!);

        var contentAfterTouch = File.ReadAllText(path);

        Assert.Equal(contentAfterPair, contentAfterTouch);
    }

    [Fact]
    public void Touch_AfterDebounceWindowElapses_RewritesFile_WithUpdatedLastSeen()
    {
        var path = NewStatePath();
        var log = new CapturingDiagnosticLog();
        var clock = new ManualTimeProvider { UtcNow = Epoch };
        var registry = NewRegistry(path, log, clock);
        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out _, out var deviceId));

        var contentAfterPair = File.ReadAllText(path);

        clock.UtcNow = Epoch + TimeSpan.FromMinutes(2);
        registry.Touch(deviceId!);

        var contentAfterTouch = File.ReadAllText(path);

        Assert.NotEqual(contentAfterPair, contentAfterTouch);
    }

    [Fact]
    public void Touch_UnknownDeviceId_NoThrow_NoWrite()
    {
        var path = NewStatePath();
        var log = new CapturingDiagnosticLog();
        var clock = new ManualTimeProvider { UtcNow = Epoch };
        var registry = NewRegistry(path, log, clock);
        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out _, out _));

        var contentBefore = File.ReadAllText(path);
        var exception = Record.Exception(() => registry.Touch("NOTAREALDEVICE0"));
        var contentAfter = File.ReadAllText(path);

        Assert.Null(exception);
        Assert.Equal(contentBefore, contentAfter);
    }

    // --- Persistence / migration / corruption ---

    [Fact]
    public void State_RoundTripsThroughJson_ReloadedRegistry_BothDevicesStillAuthorizeWithOwnIds()
    {
        var path = NewStatePath();
        var log1 = new CapturingDiagnosticLog();
        var registry1 = NewRegistry(path, log1);
        Assert.True(registry1.TryPair(registry1.CurrentCode, "Tablet", "tablet", out var token1, out var id1));
        registry1.OpenPairingWindow();
        Assert.True(registry1.TryPair(registry1.CurrentCode, "Phone", "phone", out var token2, out var id2));

        var log2 = new CapturingDiagnosticLog();
        var registry2 = NewRegistry(path, log2);

        Assert.True(registry2.TryAuthorize(token1, out var reloadedId1));
        Assert.Equal(id1, reloadedId1);
        Assert.True(registry2.TryAuthorize(token2, out var reloadedId2));
        Assert.Equal(id2, reloadedId2);
    }

    [Fact]
    public void AbsentStateFile_IsFirstRun_NotAnError()
    {
        var path = NewStatePath(); // directory exists, file does not
        var log = new CapturingDiagnosticLog();

        var exception = Record.Exception(() => NewRegistry(path, log));

        Assert.Null(exception);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void LegacySingleTokenFile_MigratesToExactlyOneDevice_TokenStillAuthorizes()
    {
        var path = NewStatePath();
        WriteLegacySingleTokenFile(path, KnownTokenHexA, "123456", 2);
        var log = new CapturingDiagnosticLog();

        var registry = NewRegistry(path, log);

        Assert.True(registry.TryAuthorize(KnownTokenHexA, out var deviceId));
        Assert.Equal(KnownDeviceIdA, deviceId);

        // Exactly one device: a second, unrelated token must not authorize.
        Assert.False(registry.TryAuthorize(KnownTokenHexB, out _));

        // The migrated state is itself persisted in the new shape - reload
        // proves it, rather than just trusting the in-memory result.
        var registryReloaded = NewRegistry(path, new CapturingDiagnosticLog());
        Assert.True(registryReloaded.TryAuthorize(KnownTokenHexA, out var reloadedDeviceId));
        Assert.Equal(KnownDeviceIdA, reloadedDeviceId);
    }

    [Fact]
    public void CorruptStateFile_Regenerates_RatherThanThrowing()
    {
        var path = NewStatePath();
        File.WriteAllText(path, "{ not valid json at all");
        var log = new CapturingDiagnosticLog();

        var exception = Record.Exception(() => NewRegistry(path, log));

        Assert.Null(exception);
        var registry = NewRegistry(path, log);
        Assert.Equal(6, registry.CurrentCode.Length);
        Assert.False(registry.TryAuthorize(KnownTokenHexA, out _));
    }

    // --- Logging never leaks the secrets ---

    [Fact]
    public void Logs_NeverContainTokenOrCode()
    {
        var log = new CapturingDiagnosticLog();
        var registry = NewRegistry(NewStatePath(), log);
        var secrets = new List<string> { registry.CurrentCode };

        // A wrong attempt.
        var wrong1 = registry.CurrentCode == "000000" ? "111111" : "000000";
        registry.TryPair(wrong1, "Intruder", "unknown", out _, out _);

        // Drive a full lockout rotation (4 more wrong attempts; 5 total).
        for (var i = 0; i < 4; i++)
        {
            secrets.Add(registry.CurrentCode);
            var wrong = registry.CurrentCode == "111111" ? "222222" : "111111";
            registry.TryPair(wrong, "Intruder", "unknown", out _, out _);
        }

        // A success.
        secrets.Add(registry.CurrentCode);
        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out var token, out var deviceId));
        secrets.Add(token!);

        // A forget.
        registry.Forget(deviceId!);

        foreach (var evt in log.Events)
        {
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, evt.Message);
                if (evt.Detail is not null)
                {
                    Assert.DoesNotContain(secret, evt.Detail);
                }
            }
        }
    }

    // --- The constant-time comparison loop: see report for why a
    // behavioural unit test cannot detect FixedTimeEquals being replaced by
    // a functionally-equivalent SequenceEqual, or an early-exit loop being
    // substituted for the no-early-exit scan, and why this source-scan pin
    // is used instead. ---

    [Fact]
    public void DeviceRegistrySource_UsesFixedTimeEqualsForEveryComparison_NotNaiveEquality()
    {
        var repoRoot = FindRepoRoot();
        var sourcePath = Path.Combine(repoRoot, "src", "LunaPanel.Core", "Pairing", "DeviceRegistry.cs");
        Assert.True(File.Exists(sourcePath), $"Expected {sourcePath} to exist.");

        var content = File.ReadAllText(sourcePath);

        Assert.Contains("CryptographicOperations.FixedTimeEquals", content);
        Assert.DoesNotContain(".SequenceEqual(", content);
    }

    /// <summary>
    /// Extends the pin above to the property a behavioural test cannot see
    /// either: that <c>TryAuthorize</c>'s loop never exits early on a match.
    /// An early-exit rewrite (e.g. <c>if (isMatch) { deviceId = ...; return
    /// true; }</c> inside the loop) produces the exact same booleans and
    /// device ids for every input this test suite can construct - the only
    /// thing it changes is how long the call takes relative to how many
    /// devices had to be checked, which no behavioural test can observe.
    /// Scoped to <c>TryAuthorize</c>'s own body (not the whole file) so a
    /// legitimate <c>return true;</c> elsewhere (e.g. in <c>TryPair</c>)
    /// cannot trip it.
    /// </summary>
    [Fact]
    public void DeviceRegistrySource_TryAuthorizeLoop_HasNoEarlyExitOnMatch()
    {
        var repoRoot = FindRepoRoot();
        var sourcePath = Path.Combine(repoRoot, "src", "LunaPanel.Core", "Pairing", "DeviceRegistry.cs");
        var content = File.ReadAllText(sourcePath);

        var methodStart = content.IndexOf("public bool TryAuthorize(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "Expected to find TryAuthorize in DeviceRegistry.cs.");

        var nextMemberStart = content.IndexOf("\n    public ", methodStart + 1, StringComparison.Ordinal);
        Assert.True(nextMemberStart > methodStart, "Expected another public member after TryAuthorize.");

        var methodBody = content[methodStart..nextMemberStart];

        Assert.Contains("anyMatch |=", methodBody);
        Assert.DoesNotContain("return true", methodBody);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LunaPanel.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate repo root (a directory containing LunaPanel.sln) above {AppContext.BaseDirectory}.");
    }
}
