using LunaPanel.Core.Bindings;
using LunaPanel.Core.Catalogue;
using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Core.Layouts;

/// <summary>
/// Joins a <see cref="Layout"/> with a catalogue merge (see
/// <see cref="CatalogueMerger.Merge"/>), injected <see cref="MacroKnowledge"/>,
/// and the set of action names that genuinely exist in the player's bindings
/// file to produce a fresh <see cref="SlotAnnotation"/> for every slot - this
/// is where "a layout can't break, only degrade" (see
/// <c>ref/docs/design-decisions.md</c>) actually happens. Nothing here is
/// ever written back to the layout, so the exact same file annotates
/// differently the moment the player's bindings or the macro set change -
/// that's the whole point.
///
/// An action name present and bound in <c>catalogueMerge</c> -&gt;
/// <see cref="SlotStatus.Ok"/>; present and unbound -&gt;
/// <see cref="SlotStatus.Unbound"/>. When it is absent from
/// <c>catalogueMerge</c> entirely - which happens for a genuinely nonexistent
/// action, but also for a real, uncurated action that is currently unbound,
/// since <see cref="CatalogueMerger"/> omits that one cell of its four-way
/// classification (see <c>ref/docs/catalogue.md</c>'s "The four-way
/// classification") - the injected <c>knownActionNames</c> set (typically
/// every element name <c>BindingsFile.Parse</c> found, bound or not) tells
/// the two apart: present there -&gt; <see cref="SlotStatus.Unbound"/> (it
/// exists, just needs binding in Elite); absent from both -&gt;
/// <see cref="SlotStatus.UnknownAction"/> (it doesn't exist at all).
/// </summary>
public static class LayoutAnnotator
{
    public static IReadOnlyList<LayoutAnnotation> Annotate(Layout layout, IReadOnlyList<CataloguePickerEntry> catalogueMerge, MacroKnowledge macros, IReadOnlySet<string> knownActionNames, IReadOnlyList<LayoutPage>? folderPageSource = null)
    {
        var byName = catalogueMerge.ToDictionary(e => e.ActionName, e => e, StringComparer.Ordinal);

        // [2026-09-16] id -> name for every page a folder button could open,
        // built once per annotate rather than per slot.
        //
        // folderPageSource exists because PanelEndpoint.BuildResponse hands
        // this method a SINGLE-page synthetic layout on purpose (so a result
        // can never be confused with another page of the same name) - which
        // means the page a folder opens is, by construction, absent from the
        // layout being annotated. Defaulting to layout.Pages keeps every
        // other caller unchanged; the endpoint passes the whole page list.
        var folderPageNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var page in folderPageSource ?? layout.Pages)
        {
            if (page.Id is not null)
            {
                folderPageNames[page.Id] = page.Name;
            }
        }

        var results = new List<LayoutAnnotation>();

        foreach (var page in layout.Pages)
        {
            foreach (var slot in page.Slots.Concat(page.Parked))
            {
                results.Add(new LayoutAnnotation(page.Name, AnnotateSlot(slot, byName, macros, knownActionNames, folderPageNames)));
            }
        }

        return results;
    }

    private static SlotAnnotation AnnotateSlot(LayoutSlot slot, IReadOnlyDictionary<string, CataloguePickerEntry> byName, MacroKnowledge macros, IReadOnlySet<string> knownActionNames, IReadOnlyDictionary<string, string> folderPageNames)
    {
        var (status, reason, label) = slot.FolderPage is not null
            ? AnnotateFolder(slot.FolderPage, slot.Label, folderPageNames)
            : AnnotateReference(slot.Action, slot.Macro, slot.Label, byName, macros, knownActionNames);

        LongPressAnnotation? longPress = null;
        if (slot.LongPress is not null)
        {
            // No override label to consult here - LongPressAction carries no
            // label field of its own (see ref/docs/button-naming.md).
            var (lpStatus, lpReason, lpLabel) = AnnotateReference(slot.LongPress.Action, slot.LongPress.Macro, null, byName, macros, knownActionNames);
            longPress = new LongPressAnnotation(lpStatus, lpReason, lpLabel);
        }

        return new SlotAnnotation(slot.Index, label, status, reason, longPress);
    }

    /// <summary>
    /// [2026-09-16] A folder button. It names no action and no macro, so
    /// without this branch it would fall into
    /// <see cref="AnnotateReference"/>'s "names neither" fallback and be
    /// reported as broken - which a commander would read as "this button is
    /// empty" and clear.
    ///
    /// Its default label is the interior page's own NAME: a folder's name
    /// and its page's name are one thing (a real folder's mental model), so
    /// there is nothing else to show and nothing to keep in step. An
    /// override still wins, exactly as it does for an action or a macro
    /// (<c>ref/docs/button-naming.md</c>).
    ///
    /// A reference that does not resolve degrades rather than throwing: a
    /// load never runs <see cref="LayoutValidator"/> (see
    /// <c>LayoutStore</c>), so a hand-edited or partially-restored file can
    /// genuinely arrive here pointing at nothing.
    /// </summary>
    private static (SlotStatus Status, string Reason, string Label) AnnotateFolder(string folderPageId, string? overrideLabel, IReadOnlyDictionary<string, string> folderPageNames)
    {
        if (folderPageNames.TryGetValue(folderPageId, out var name))
        {
            return (SlotStatus.Ok, $"Folder - opens '{name}'.", overrideLabel ?? name);
        }

        return (
            SlotStatus.UnknownAction,
            "This button opens a folder page that is no longer in this layout - clear it, or make it a folder again.",
            overrideLabel ?? "Folder");
    }

    private static (SlotStatus Status, string Reason, string Label) AnnotateReference(
        string? action,
        string? macro,
        string? overrideLabel,
        IReadOnlyDictionary<string, CataloguePickerEntry> byName,
        MacroKnowledge macros,
        IReadOnlySet<string> knownActionNames)
    {
        if (action is not null)
        {
            var (status, reason) = AnnotateAction(action, byName, knownActionNames);
            var label = overrideLabel ?? ResolveActionLabel(action, byName);
            return (status, reason, label);
        }

        if (macro is not null)
        {
            var (status, reason) = AnnotateMacro(macro, macros);
            var label = overrideLabel ?? macros.NameFor(macro) ?? Prettifier.Prettify(macro);
            return (status, reason, label);
        }

        // Neither set is LayoutValidator's job to reject at save time; the
        // annotator still needs to produce something sensible rather than
        // throw, since a load never runs the validator (see LayoutStore).
        return (SlotStatus.UnknownAction, "This slot names neither an action nor a macro.", overrideLabel ?? string.Empty);
    }

    /// <summary>
    /// <c>slot.Label ?? catalogue DisplayLabel ?? Prettify(elementName)</c> -
    /// the override is applied by the caller, so this only ever covers the
    /// last two steps: a curated or uncurated-but-bound action already has a
    /// <see cref="CataloguePickerEntry.DisplayLabel"/> in <paramref name="byName"/>
    /// (curated label or an already-prettified fallback - see
    /// <see cref="CatalogueMerger"/>); anything absent from the merge entirely
    /// (unknown action, or uncurated-and-unbound) falls back to
    /// <see cref="Prettifier.Prettify"/> directly. See <c>ref/docs/button-naming.md</c>.
    /// </summary>
    private static string ResolveActionLabel(string action, IReadOnlyDictionary<string, CataloguePickerEntry> byName) =>
        byName.TryGetValue(action, out var entry) ? entry.DisplayLabel : Prettifier.Prettify(action);

    private static (SlotStatus, string) AnnotateAction(string action, IReadOnlyDictionary<string, CataloguePickerEntry> byName, IReadOnlySet<string> knownActionNames)
    {
        if (!byName.TryGetValue(action, out var entry))
        {
            if (knownActionNames.Contains(action))
            {
                // Real (it's in the player's bindings file) but uncurated-and-
                // -unbound, so CatalogueMerger deliberately left it out of the
                // merge - that omission must not be misread as "doesn't exist".
                return (SlotStatus.Unbound, $"'{action}' exists in Elite Dangerous but isn't currently bound to anything - bind it in the game's controls and this button will start working.");
            }

            return (SlotStatus.UnknownAction, $"'{action}' is not present in your Elite Dangerous bindings file - it may have been renamed or removed by a game update.");
        }

        if (entry.IsBound)
        {
            return (SlotStatus.Ok, $"Bound ({entry.DisplayChord}).");
        }

        var reasonText = entry.UnboundReason switch
        {
            // Actionable, and matching the uncurated-unbound wording above:
            // a commander seeing this needs to know what to do AND that
            // nothing here has to be redone afterwards. A layout stores the
            // element name, never a resolved key, and bindings are re-read
            // every request - so binding it in Elite heals the slot on the
            // next load with no edit, restart or re-pair.
            UnboundReason.NotPresent => "not bound to anything in Elite Dangerous - bind it in the game's controls and this button will start working.",
            UnboundReason.NoDevice => "not bound to anything in Elite Dangerous - bind it in the game's controls and this button will start working.",
            UnboundReason.NonKeyboardOnly => "bound to a non-keyboard device, which LunaPanel cannot use.",
            UnboundReason.UnknownKey => "bound to a key LunaPanel does not recognize.",
            _ => "not currently usable."
        };

        return (SlotStatus.Unbound, $"'{entry.DisplayLabel}' is {reasonText}");
    }

    private static (SlotStatus, string) AnnotateMacro(string macroId, MacroKnowledge macros)
    {
        if (macros.DegradedIds.Contains(macroId))
        {
            return (SlotStatus.MacroDegraded, $"Macro '{macroId}' has one or more steps that are no longer bound.");
        }

        if (macros.KnownIds.Contains(macroId))
        {
            return (SlotStatus.Ok, $"Macro '{macroId}' is fully resolvable.");
        }

        return (SlotStatus.UnknownMacro, $"'{macroId}' is not a recognized macro.");
    }

    /// <summary>
    /// Same as <see cref="Annotate"/>, plus a single <c>Layout</c>-category
    /// log line reporting how many of the returned annotations are degraded
    /// (any status other than <see cref="SlotStatus.Ok"/>) - this is what
    /// lets the diagnostics view explain, in aggregate, why a panel might
    /// look broken without walking every slot by hand. Same split as
    /// <c>LunaPanel.Core.Bindings.BindResolver.Resolve</c> vs <c>LogSummary</c>:
    /// <see cref="Annotate"/> itself stays log-free so it can be called as
    /// often as needed (e.g. on every UI refresh) without spamming the log.
    /// </summary>
    public static IReadOnlyList<LayoutAnnotation> AnnotateAndLog(
        Layout layout,
        IReadOnlyList<CataloguePickerEntry> catalogueMerge,
        MacroKnowledge macros,
        IReadOnlySet<string> knownActionNames,
        IDiagnosticLog log)
    {
        var annotations = Annotate(layout, catalogueMerge, macros, knownActionNames);

        var degraded = annotations.Count(a => a.Slot.Status != SlotStatus.Ok);
        degraded += annotations.Count(a => a.Slot.LongPress is not null && a.Slot.LongPress.Status != SlotStatus.Ok);

        log.Info("Layout", $"Annotated layout: {degraded} of {annotations.Count} slot(s) degraded.");

        return annotations;
    }
}
