using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Macros;
using LunaPanel.Core.Pairing;
using LunaPanel.Core.Transfer;
using LunaPanel.Server.Layouts;
using LunaPanel.Server.Macros;

namespace LunaPanel.Server.Http;

/// <summary>
/// Import and export, as files (<c>ref/docs/transfer.md</c>) - the handler
/// logic, kept free of any ASP.NET type exactly as
/// <see cref="LayoutImportEndpoint"/> and <see cref="MacrosEndpoint"/> are.
///
/// <b>Host-only, both directions, deliberately.</b> Exporting a profile hands
/// out a commander's whole arrangement and importing one rewrites it, so both
/// live on the loopback listener a paired device cannot reach
/// (<see cref="HostRequest"/>, <see cref="HostOnlyRoutes"/>). That is a wider
/// line than the macro-authoring one - there, <em>reading</em> stayed open to
/// every device - and it is wider on purpose: there is no per-device half of
/// this feature that a tablet needs.
///
/// <b>Nothing here is a second way to write anything.</b> A layout goes
/// through <see cref="LayoutStore.Save"/>, which validates it and leaves the
/// same single <c>.bak</c> generation every other layout write leaves, so the
/// existing undo route is this feature's undo too. A macro goes through
/// <see cref="UserMacroStore.Save"/>. Neither store learned a new method for
/// this.
///
/// <b>An import is checked in full before it writes anything.</b>
/// <see cref="TransferFile.Parse"/> either understands the whole file or
/// refuses it; the macro ids are checked; the layout is validated. Only then
/// does anything reach disk. A file that is going to be refused is refused
/// while the device it was aimed at is still exactly as it was.
/// </summary>
public static class TransferEndpoint
{
    private const string LogCategory = "Transfer";

    /// <summary>
    /// What the PC's own arrangement is called in a picker.
    /// <see cref="HostRequest.HostDeviceId"/> is in no registry and has no
    /// orphan marker, so nothing else would name it - and "host" is a word
    /// for the machine, not for a commander.
    /// </summary>
    public const string ThisPcName = "This PC";

    /// <param name="HasLayout">
    /// Whether there is anything to export. A device that has paired but
    /// never drawn a panel has no layout file yet, and is still a perfectly
    /// good <em>target</em> for an import - so it is listed either way and
    /// the flag says which halves of the pane it belongs in.
    /// </param>
    public sealed record Target(string DeviceId, string Name, bool IsThisPc, bool HasLayout);

    public sealed record TargetsResponse(IReadOnlyList<Target> Targets);

    public enum ExportOutcome
    {
        Exported,

        /// <summary>No device was named, or the one named is not one of this machine's.</summary>
        NoSuchDevice,

        /// <summary>That device has never had an arrangement to export.</summary>
        NoLayout,

        /// <summary>No user macro by that id. A shipped macro lands here too - see <see cref="ExportMacros"/>.</summary>
        NoSuchMacro,

        /// <summary>"Export all my macros" on a machine that has none.</summary>
        NothingToExport,
    }

    public sealed record ExportResult(ExportOutcome Outcome, string? Json, string? FileName, string? Error)
    {
        public static ExportResult Ok(string json, string fileName) => new(ExportOutcome.Exported, json, fileName, null);

        public static ExportResult Fail(ExportOutcome outcome, string error) => new(outcome, null, null, error);
    }

    public enum ImportOutcome
    {
        Imported,

        /// <summary>The file was not usable, and nothing was written. <see cref="ImportResult.Error"/> says why in one sentence.</summary>
        Refused,

        /// <summary>The device it was aimed at is not one of this machine's.</summary>
        NoSuchTarget,

        /// <summary>Everything parsed, and <see cref="LayoutStore.Save"/> still refused it.</summary>
        CouldNotSave,
    }

    /// <param name="MacrosAdded">Macros in the file that this PC did not already have, now saved.</param>
    /// <param name="MacrosAlreadyPresent">
    /// Macros whose id this PC already has. <b>Left exactly as they were</b> -
    /// see <see cref="TryWriteMacros"/> for why that is the rule and what it
    /// costs.
    /// </param>
    /// <param name="ReferencesToMissingMacros">
    /// How many places in the imported arrangement name a macro this PC has
    /// no definition for - counting a long-press separately from the tap on
    /// the same button, because they are two things a commander set. Not a
    /// failure: those buttons behave exactly as any slot naming an absent
    /// macro already does. Reported so the pane can say so rather than
    /// leaving it to be discovered by pressing one.
    /// </param>
    public sealed record ImportResult(
        ImportOutcome Outcome,
        string? Error,
        int MacrosAdded,
        int MacrosAlreadyPresent,
        int ReferencesToMissingMacros)
    {
        public static ImportResult Fail(ImportOutcome outcome, string error) => new(outcome, error, 0, 0, 0);
    }

    /// <summary>
    /// Every device this machine could export from or import onto: the ones
    /// that are paired now, the layouts left behind by ones that are not
    /// (<c>ref/docs/layout-import.md</c>'s orphans), and the PC itself.
    ///
    /// <b>This list is also the guard.</b> Both routes take a device id from
    /// a request and <see cref="LayoutStore"/> turns a device id into a file
    /// name, so an id that is not on this list is refused rather than used -
    /// the identical rule <see cref="LayoutImportEndpoint.Import"/> already
    /// applies to its own source id, and the reason neither can be handed a
    /// path.
    /// </summary>
    public static IReadOnlyList<Target> Targets(
        LayoutStore store,
        OrphanMarkerStore markers,
        IReadOnlyList<DeviceSummary> liveDevices)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(markers);
        ArgumentNullException.ThrowIfNull(liveDevices);

        var withLayouts = store.ListDeviceIds().ToHashSet(StringComparer.Ordinal);
        var named = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var marker in markers.List())
        {
            named[marker.DeviceId] = marker.Name;
        }

        // A live device wins over any marker naming the same id, for the
        // reason LayoutImport.BuildCandidates gives: a device forgotten and
        // later re-paired leaves a marker behind, and its current name is
        // the true one.
        foreach (var device in liveDevices)
        {
            named[device.DeviceId] = device.Name;
        }

        var targets = new List<Target>
        {
            new(HostRequest.HostDeviceId, ThisPcName, true, withLayouts.Contains(HostRequest.HostDeviceId)),
        };

        foreach (var deviceId in named.Keys.Concat(withLayouts).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
        {
            if (string.Equals(deviceId, HostRequest.HostDeviceId, StringComparison.Ordinal))
            {
                continue;
            }

            targets.Add(new Target(
                deviceId,
                named.TryGetValue(deviceId, out var name) ? name : $"Unknown device {deviceId}",
                false,
                withLayouts.Contains(deviceId)));
        }

        return targets;
    }

    /// <summary>
    /// One device's whole arrangement, plus the definitions of every user
    /// macro its buttons name - and no others. A shipped macro is not
    /// carried: the receiving copy of LunaPanel already has it, and a
    /// shipped definition travelling in a file is how a later release's fix
    /// gets shadowed by a stale copy nobody remembers importing.
    /// </summary>
    public static ExportResult ExportProfile(
        LayoutStore store,
        MacroCatalogue catalogue,
        IReadOnlyList<Target> targets,
        string? deviceId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(targets);

        var target = Find(targets, deviceId);
        if (target is null)
        {
            return ExportResult.Fail(ExportOutcome.NoSuchDevice, "There is no such device on this PC.");
        }

        var loaded = store.Load(target.DeviceId);
        if (loaded.Outcome != LayoutLoadOutcome.Loaded)
        {
            return ExportResult.Fail(ExportOutcome.NoLayout, $"{target.Name} has no buttons to export yet.");
        }

        var layout = loaded.Layout!;
        var referenced = ReferencedMacroIds(layout);
        var carried = catalogue.All()
            .Where(m => referenced.Contains(m.Id) && !catalogue.IsShipped(m.Id))
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .ToList();

        return ExportResult.Ok(
            TransferFile.WriteProfile(layout, carried, target.Name, now),
            TransferFile.FileNameFor(TransferKind.Profile, target.Name, now));
    }

    /// <summary>
    /// One user macro, or every user macro on this machine when
    /// <paramref name="macroId"/> is absent.
    ///
    /// A shipped macro cannot be exported, and is refused with the same
    /// outcome an unknown id gets. It is not a permission rule so much as a
    /// pointless one to allow: the receiving copy ships the identical macro
    /// under the identical id, so the file could only ever be a no-op or a
    /// stale shadow. Copy it first (the builder's own copy-to-edit) and
    /// export the copy.
    /// </summary>
    public static ExportResult ExportMacros(MacroCatalogue catalogue, string? macroId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        var userMacros = catalogue.All().Where(m => !catalogue.IsShipped(m.Id)).ToList();

        if (string.IsNullOrWhiteSpace(macroId))
        {
            if (userMacros.Count == 0)
            {
                return ExportResult.Fail(ExportOutcome.NothingToExport, "There are no macros of your own on this PC to export.");
            }

            return ExportResult.Ok(
                TransferFile.WriteMacros(userMacros, ThisPcName, now),
                TransferFile.FileNameFor(TransferKind.Macros, null, now));
        }

        var macro = userMacros.FirstOrDefault(m => string.Equals(m.Id, macroId, StringComparison.Ordinal));
        if (macro is null)
        {
            return ExportResult.Fail(ExportOutcome.NoSuchMacro, "There is no macro of your own with that name on this PC.");
        }

        return ExportResult.Ok(
            TransferFile.WriteMacros(new[] { macro }, ThisPcName, now),
            TransferFile.FileNameFor(TransferKind.Macros, macro.Name ?? macro.Id, now));
    }

    /// <summary>
    /// Writes a profile file onto one device, macros first, arrangement
    /// last.
    ///
    /// The order matters and is not an accident. Everything that can refuse
    /// this import - the file's own grammar, the macro ids, the layout's
    /// validity against this machine's bindings - is decided before the
    /// first write. After that only two things can happen: both writes
    /// succeed, or <see cref="LayoutStore.Save"/> refuses for a reason
    /// <see cref="LayoutValidator"/> did not already give, which would leave
    /// the macros added and the arrangement untouched. That residue is
    /// harmless (a macro nothing names is invisible) and is the price of not
    /// inventing a transaction across two stores.
    /// </summary>
    public static ImportResult ImportProfile(
        LayoutStore store,
        MacroCatalogue catalogue,
        IReadOnlyList<Target> targets,
        string? targetDeviceId,
        string? fileText,
        IReadOnlySet<string> knownActionNames,
        IDiagnosticLog log)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(knownActionNames);
        ArgumentNullException.ThrowIfNull(log);

        var target = Find(targets, targetDeviceId);
        if (target is null)
        {
            return ImportResult.Fail(ImportOutcome.NoSuchTarget, "There is no such device on this PC.");
        }

        var parsed = TransferFile.Parse(fileText);
        if (!parsed.Success)
        {
            return ImportResult.Fail(ImportOutcome.Refused, parsed.Error!);
        }

        if (parsed.Kind != TransferKind.Profile)
        {
            return ImportResult.Fail(ImportOutcome.Refused, "That file holds macros, not buttons. Import it under Macros instead.");
        }

        var idError = CheckMacroIds(catalogue, parsed.Macros);
        if (idError is not null)
        {
            return ImportResult.Fail(ImportOutcome.Refused, idError);
        }

        // Validated here, before anything is written, as well as inside
        // LayoutStore.Save - which validates again and is still the only
        // thing that decides whether a file is written. This is not a second
        // opinion: it is the same call, made early enough that a refusal
        // costs no macros.
        var validation = LayoutValidator.ValidateForSave(parsed.Layout!, knownActionNames);
        if (!validation.IsValid)
        {
            log.Warn(LogCategory, "Refused a profile import: the buttons in it are not valid on this PC", string.Join("; ", validation.Errors));
            return ImportResult.Fail(ImportOutcome.Refused, $"Those buttons cannot be used on this PC: {validation.Errors[0]}");
        }

        TryWriteMacros(catalogue, parsed.Macros, out var added, out var alreadyPresent);

        // Seeded first when the device has none, for the reason
        // LayoutImportEndpoint.Import gives: the write has to displace
        // something into LayoutStore's .bak, or the undo route has nothing
        // to put back.
        LayoutAccess.LoadOrSeed(store, target.DeviceId, knownActionNames, log);

        var saved = store.Save(target.DeviceId, parsed.Layout!, knownActionNames);
        if (saved.Outcome != LayoutSaveOutcome.Saved)
        {
            return ImportResult.Fail(ImportOutcome.CouldNotSave, "Those buttons could not be saved.");
        }

        log.Info(LogCategory, "Imported a profile from a file", $"deviceId={target.DeviceId}, macrosAdded={added}");

        return new ImportResult(
            ImportOutcome.Imported,
            null,
            added,
            alreadyPresent,
            CountMissingMacroReferences(catalogue, parsed.Layout!));
    }

    /// <summary>Macros with no arrangement - the other half of the ask.</summary>
    public static ImportResult ImportMacros(MacroCatalogue catalogue, string? fileText, IDiagnosticLog log)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(log);

        var parsed = TransferFile.Parse(fileText);
        if (!parsed.Success)
        {
            return ImportResult.Fail(ImportOutcome.Refused, parsed.Error!);
        }

        if (parsed.Kind != TransferKind.Macros)
        {
            return ImportResult.Fail(ImportOutcome.Refused, "That file holds a whole set of buttons, not macros. Import it under Profile instead.");
        }

        var idError = CheckMacroIds(catalogue, parsed.Macros);
        if (idError is not null)
        {
            return ImportResult.Fail(ImportOutcome.Refused, idError);
        }

        TryWriteMacros(catalogue, parsed.Macros, out var added, out var alreadyPresent);
        log.Info(LogCategory, "Imported macros from a file", $"added={added}, alreadyPresent={alreadyPresent}");

        return new ImportResult(ImportOutcome.Imported, null, added, alreadyPresent, 0);
    }

    /// <summary>
    /// Puts back whatever the last write to <paramref name="targetDeviceId"/>
    /// displaced - <see cref="LayoutStore"/>'s own single kept generation,
    /// the same one <see cref="LayoutImportEndpoint.Undo"/> restores.
    /// <see langword="false"/> when the device is not one of this machine's,
    /// or when there is nothing to go back to.
    ///
    /// Named by device rather than taken from the caller because the caller
    /// is the PC and the arrangement that was overwritten is somebody's
    /// tablet - which is the one thing <c>POST /api/layout/import/undo</c>
    /// structurally cannot do, since it reads the calling device's id.
    /// </summary>
    public static bool Undo(LayoutStore store, IReadOnlyList<Target> targets, string? targetDeviceId)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(targets);

        var target = Find(targets, targetDeviceId);
        return target is not null && store.RestorePrevious(target.DeviceId);
    }

    /// <summary>
    /// <see langword="null"/> when every macro in the file may be written;
    /// the refusal otherwise. Checked over the <em>whole</em> list before a
    /// single one is saved, so a file with one bad id costs nothing.
    ///
    /// Two rules, and they guard different things:
    ///
    /// <list type="bullet">
    /// <item><see cref="UserMacroIds.IsWellFormed"/> - because
    /// <see cref="UserMacroStore"/> turns an id into a file name, and this is
    /// the first path in this project where an id arrives from outside it.
    /// <c>IsUserMacroId</c> alone is satisfied by <c>user-../../anything</c>
    /// and is not enough here.</item>
    /// <item>Not a shipped id - a file cannot introduce a macro that shadows
    /// one this build ships. <see cref="MacroCatalogue"/> would ignore such a
    /// file anyway ("shipped wins a collision"), which is exactly why writing
    /// it would be worse than refusing: the commander would see an import
    /// succeed and nothing change.</item>
    /// </list>
    ///
    /// <b>[2026-09-10, measured] The second clause is currently unreachable,
    /// and is kept deliberately.</b> Every id that gets past
    /// <see cref="UserMacroIds.IsWellFormed"/> begins with
    /// <see cref="UserMacroIds.Prefix"/>, and
    /// <c>UserMacroIdsTests.NoShippedMacroId_UsesTheReservedUserPrefix</c>
    /// sweeps every shipped macro to prove none ever does - so
    /// <see cref="MacroCatalogue.IsShipped"/> cannot be true here. Deleting
    /// the clause reddens nothing, which was <em>predicted before it was
    /// run</em> rather than discovered afterwards (mutation M18: predicted 1,
    /// actual 0). It stays because the alternative is production code whose
    /// safety depends silently on a rule enforced in another file's test, and
    /// because it becomes the live guard the moment that reservation is ever
    /// relaxed. <c>TransferEndpointTests.ImportMacros_AShippedId_IsRefused</c>
    /// characterises the outcome; no mutation can make it name this line.
    /// </summary>
    private static string? CheckMacroIds(MacroCatalogue catalogue, IReadOnlyList<MacroDefinition> macros)
    {
        foreach (var macro in macros)
        {
            if (!UserMacroIds.IsWellFormed(macro.Id) || catalogue.IsShipped(macro.Id))
            {
                return "That file has a macro in it that this version of LunaPanel will not accept.";
            }
        }

        return null;
    }

    /// <summary>
    /// Saves every macro whose id this machine does not already have, and
    /// leaves the rest exactly as they are.
    ///
    /// <b>Add, never overwrite</b> (<c>ref/docs/transfer.md</c>). Ids are
    /// minted from a GUID (<see cref="UserMacroIds.Mint"/>), so two
    /// independently authored macros do not collide: a collision means the
    /// two are the <em>same</em> macro, exported from a common ancestor.
    /// Keeping what is already here therefore keeps the commander's own
    /// version - possibly one they have since edited - and makes importing
    /// the same file twice a no-op instead of a pile of duplicates.
    ///
    /// The cost is real and is accepted: you cannot receive an <em>updated</em>
    /// version of a macro you already have. The way round it is the builder's
    /// own copy-to-edit, which mints a fresh id, so the sender exports a copy
    /// and the receiver gets it as a new macro alongside theirs.
    /// </summary>
    private static void TryWriteMacros(
        MacroCatalogue catalogue,
        IReadOnlyList<MacroDefinition> macros,
        out int added,
        out int alreadyPresent)
    {
        added = 0;
        alreadyPresent = 0;

        var existing = catalogue.All().Select(m => m.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var macro in macros)
        {
            if (existing.Contains(macro.Id))
            {
                alreadyPresent++;
                continue;
            }

            catalogue.UserMacros.Save(macro);
            existing.Add(macro.Id);
            added++;
        }
    }

    /// <summary>
    /// How many places in <paramref name="layout"/> name a macro that does
    /// not exist here.
    ///
    /// <b>This is a count, never a refusal.</b> A slot naming an absent macro
    /// is an already-handled state - it renders as unknown and says so at
    /// press time (<c>ref/docs/macro-builder.md</c>), which is the same thing
    /// the starter layout's own slot did for months before the macro it named
    /// existed. Refusing the import instead would throw away an arrangement
    /// over one button.
    /// </summary>
    private static int CountMissingMacroReferences(MacroCatalogue catalogue, Layout layout)
    {
        var known = catalogue.All().Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        return ReferencedMacroIdsWithRepeats(layout).Count(id => !known.Contains(id));
    }

    private static IReadOnlySet<string> ReferencedMacroIds(Layout layout) =>
        ReferencedMacroIdsWithRepeats(layout).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Every macro reference in the layout, long-press ones included and
    /// counted separately from the tap on the same button. Both halves
    /// matter: an export that carried only the tap's macro would produce a
    /// profile whose long-presses died on arrival.
    /// </summary>
    private static IEnumerable<string> ReferencedMacroIdsWithRepeats(Layout layout)
    {
        foreach (var page in layout.Pages)
        {
            foreach (var slot in page.Slots.Concat(page.Parked))
            {
                if (slot.Macro is not null)
                {
                    yield return slot.Macro;
                }

                if (slot.LongPress?.Macro is not null)
                {
                    yield return slot.LongPress.Macro;
                }
            }
        }
    }

    private static Target? Find(IReadOnlyList<Target> targets, string? deviceId) =>
        string.IsNullOrWhiteSpace(deviceId)
            ? null
            : targets.FirstOrDefault(t => string.Equals(t.DeviceId, deviceId, StringComparison.Ordinal));
}
