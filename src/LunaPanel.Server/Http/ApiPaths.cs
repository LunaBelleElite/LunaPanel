namespace LunaPanel.Server.Http;

/// <summary>
/// The one place every endpoint's route literal is spelled, shared between
/// <see cref="ServerHostBuilder"/>'s route mapping and
/// <see cref="DeviceAuthMiddlewareExtensions"/>'s exempt-path check - a route
/// rename here cannot silently drift the two apart, which mapping and
/// exemption independently as string literals in two files would risk.
/// </summary>
public static class ApiPaths
{
    // The web client itself - a single self-contained page, auth-exempt like
    // /api/health, because the pairing screen it draws for an unpaired
    // device has to be reachable before that device has any cookie at all.
    public const string App = "/";

    public const string Health = "/api/health";
    public const string Pair = "/api/pair";
    public const string Diagnostics = "/api/diagnostics";
    public const string Panel = "/api/panel";
    public const string PanelLive = "/api/panel/live";
    public const string Press = "/api/press";

    // The template picker, moved into settings (ref/docs/panels-and-pages.md).
    // Templates: the ladder's per-rung comfort verdict for the device
    // actually asking, authenticated (unlike /probe/estimate) since it's
    // product, not the calibration surface. PanelTemplate: applies a rung
    // change to one page - spills or parks the surplus depending on the
    // "merging and expanding panels" setting below. PanelSettings: that
    // setting itself.
    public const string Templates = "/api/templates";
    public const string PanelTemplate = "/api/panel/template";
    public const string PanelSettings = "/api/panel/settings";

    // The settings gear's Timing pane (ref/docs/macro-timing.md): hold
    // duration and inter-press gap. Server-wide, unlike PanelSettings above -
    // MacroTimingSettingsStore takes no device id at all, so this route's
    // handlers never read one either, even though the route itself still
    // requires a valid paired device like every other settings route.
    public const string MacroTiming = "/api/macro-timing";

    // The on-device editor (ref/docs/editor.md): Actions is the action
    // picker's own list (CatalogueMerger.Merge by default; MergeAll behind
    // the picker's "show everything" toggle, via the ?all= query flag) -
    // GET, since it changes nothing. The four Slot* routes are the assign/
    // rename/clear/long-press verbs the editor's slot sheet and action
    // picker actually commit through; all four go through LayoutStore.Save
    // like every other layout mutation - never a second way to write a
    // layout file. SlotLongPress sets (exactly one of action/macro) or
    // clears (both null) a slot's LongPressAction - same set-or-clear
    // convention SlotLabel already uses, rather than a fifth route just for
    // clearing.
    // The macro builder (ref/docs/macro-builder.md), and the macro-list
    // endpoint ref/docs/editor.md has been naming as missing since the
    // editor shipped - one route, two consumers: the action picker's MACROS
    // group and the builder itself. GET lists shipped and user macros
    // together, each row carrying its degraded state and a per-step
    // breakdown; POST saves one (the server mints the id when the body
    // carries none, so a rename can never orphan a slot). Copy and delete
    // are POST routes rather than PUT/DELETE verbs, matching DevicesForget
    // and LayoutImportUndo rather than introducing a second convention.
    // Vocabulary is the token picker's own data - the real
    // StatusVocabulary/JournalVocabulary tables and the ordered step-kind
    // list, so the client never restates either as its own hand-typed copy.
    // All four require device auth like every other product route - and,
    // since 2026-09-10, the three that WRITE are host-only on top of that:
    // POST Macros/MacrosCopy/MacrosDelete are refused 403 to anything but
    // the machine LunaPanel is running on (HostOnlyRoutes, HostRequest,
    // ref/docs/macro-builder.md's "Authoring moved to the PC"). GET Macros
    // and MacrosVocabulary are unchanged for every paired device: the line
    // is authoring, not macros.
    public const string Macros = "/api/macros";
    public const string MacrosCopy = "/api/macros/copy";
    public const string MacrosDelete = "/api/macros/delete";
    public const string MacrosVocabulary = "/api/macros/vocabulary";

    // The page bar's own verbs (ref/docs/panels-and-pages.md's "Pages from
    // the start"): a "+" to add a page, a rename for an existing one, and
    // the ShowWhen picker that finally puts a UI on a field the data model
    // already had. All three go through LayoutStore.Save like every other
    // layout mutation. PageShowWhen's tokens are validated against
    // ShowWhenConditionList.Parse before anything is saved - see
    // PageEditEndpoint.SetShowWhen's own remarks for why that check has to
    // live somewhere, since nothing else in the save path touches ShowWhen
    // at all.
    public const string PageAdd = "/api/panel/page/add";
    public const string PageRename = "/api/panel/page/rename";
    public const string PageShowWhen = "/api/panel/page/showwhen";

    // [2026-09-15] Delete a page outright, and drag-to-reorder the tab bar
    // (ref/docs/panels-and-pages.md) - reversing that doc's earlier "No
    // delete-page control" decision on explicit request. Both go through
    // LayoutStore.Save like every other layout mutation. PageDelete refuses
    // (server-side, authoritatively) to remove a layout's last remaining
    // page - see PageEditEndpoint.DeletePage's own remarks. PageMove is a
    // plain from/to swap, same shape as SlotMove above but for the page
    // list itself rather than one page's slots.
    public const string PageDelete = "/api/panel/page/delete";
    public const string PageMove = "/api/panel/page/move";

    public const string Actions = "/api/actions";

    // [2026-09-12] "Press a key" macro step (ref/docs/macros.md): Keys is
    // the physical-key list the key picker's grid is built from
    // (LunaPanel.Core.Input.Scancodes.All, with each key's prettified
    // label); ActionForKey is the reverse lookup the picker calls the moment
    // a key is chosen - "is anything in Elite already bound to exactly this
    // key?" - so the builder can offer a real `press` step instead of a raw
    // `pressKey` one whenever the answer is yes. Both GET, both ordinary
    // device routes like Actions above: reading either changes nothing and
    // needs no host-only gate.
    // [2026-09-13] ActionForKey's own query parameter is `domCode`, not a
    // Key_* name: the client-side redesign captures a real browser
    // KeyboardEvent.code (e.g. "KeyW"), and BrowserKeyCodeMap.TryMap does the
    // domCode -> Key_* translation server-side before this lookup ever runs.
    public const string Keys = "/api/keys";
    public const string ActionForKey = "/api/actions/for-key";
    public const string SlotAssign = "/api/panel/slot/assign";
    public const string SlotLabel = "/api/panel/slot/label";
    public const string SlotClear = "/api/panel/slot/clear";
    public const string SlotLongPress = "/api/panel/slot/longpress";

    // Latching keys (ref/docs/latching-keys.md): turns a slot's tap into a
    // hold-until-tapped-again. A boolean set-or-clear through one route,
    // matching SlotLabel/SlotLongPress rather than adding a second route
    // just to switch it off.
    public const string SlotLatch = "/api/panel/slot/latch";

    // [2026-09-12] The hold gesture (ref/docs/latching-keys.md's
    // hold-to-thrust extension): a genuinely held key for as long as a
    // finger stays on the button, released the instant it lifts - separate
    // from tap-to-latch above, and mutually exclusive with it on one slot.
    // Same boolean set-or-clear-through-one-route convention as SlotLatch.
    public const string SlotHold = "/api/panel/slot/hold";

    // Drag a button to a different slot (ref/docs/editor.md): the verb the
    // tablet's edit-mode drag gesture commits through. A SWAP between two
    // indices on ONE page - the body carries from/to and a single page,
    // never a second page index, because crossing pages is deliberately out
    // of scope (see SlotEditEndpoint.Move's own remarks). Goes through
    // LayoutStore.Save like every other layout mutation.
    public const string SlotMove = "/api/panel/slot/move";

    // [2026-09-16] Turn an empty slot into a FOLDER - a button that opens a
    // nested page of its own buttons (ref/docs/editor.md). Shaped exactly
    // like SlotAssign (parse body, load, edit, LayoutStore.Save) and, like
    // it, the only way this particular slot job is ever written. Its response
    // carries the new interior page's index so the client can navigate
    // straight in; nesting is capped at one level by LayoutValidator, not by
    // this route.
    public const string SlotMakeFolder = "/api/panel/slot/makefolder";

    // The settings gear's colour pane (ref/docs/theme.md's colour chain):
    // GET reports what's currently in effect and whether a manual override
    // is stored; POST sets one; the reset route clears it back to automatic.
    // All three require device auth like every other per-device route -
    // never exempt, unlike the page shell itself.
    public const string Theme = "/api/theme";
    public const string ThemeReset = "/api/theme/reset";

    // The settings gear's Devices pane (ref/docs/pairing-and-devices.md): GET
    // lists every paired device (never the token); the forget route revokes
    // one immediately (a device may never forget itself - enforced here, not
    // just hidden in the UI); the open-pairing route is the authenticated
    // "add another device" action - only a paired device may open pairing
    // for another, which is an escalation of trust deliberately accepted for
    // a household LAN. Device auth required like every other settings route.
    public const string Devices = "/api/devices";
    public const string DevicesForget = "/api/devices/forget";
    public const string DevicesOpenPairing = "/api/devices/open-pairing";

    // Layout recovery and import (ref/docs/layout-import.md). ONE pair of
    // routes, not two features: GET lists every layout no live device owns,
    // POST copies one onto the calling device (through LayoutStore.Save like
    // every other layout write, so the .bak generation an accidental import
    // needs comes for free), and the undo route puts that .bak back. The
    // recovery offered at the end of a successful pair is the same list and
    // the same copy, reported inline on /api/pair's own response rather than
    // needing a fourth route. Device auth required - only a paired device
    // may see or copy anything here.
    public const string LayoutImport = "/api/layout/import";
    public const string LayoutImportUndo = "/api/layout/import/undo";

    // Permanently deletes one orphan's layout file and its marker - the
    // delete half of the chooser above, reachable from the same place
    // (Settings -> Import/export) rather than only the pairing screen, since
    // deleting old clutter is an unhurried settings action, not a
    // pairing-time decision. Genuinely irreversible: unlike LayoutImport,
    // there is no .bak generation left behind to undo it with. Same
    // reachability as LayoutImport itself - any paired device, not host-only.
    public const string LayoutImportDiscard = "/api/layout/import/discard";

    // Reset this device's buttons to the shipped starter
    // (ref/docs/reset-to-default.md) - the same operation as the pair above
    // with a different source, so it shares LayoutImportUndo rather than
    // getting a second undo route: both write through LayoutStore.Save, so
    // both leave the same kind of .bak generation behind.
    public const string LayoutReset = "/api/layout/reset";

    // Import and export, as files (ref/docs/transfer.md). Targets is the
    // one list both halves pick from - every device this PC could export
    // from or import onto, the PC itself included - and is also the guard
    // both write paths check a device id against, since LayoutStore turns a
    // device id into a file name. Profile carries a whole arrangement plus
    // the definitions of the user macros its buttons name; Macro carries
    // macros alone. GET exports, POST imports, on the same literal in both
    // cases - the same reading/writing pair Macros above already is.
    //
    // ALL THREE are host-only in BOTH directions (HostOnlyRoutes), which is
    // wider than the macro-authoring line: there, reading stayed open to
    // every device because a tablet has to list macros to put one on a
    // button. Nothing on a tablet needs this at all, and a GET here hands
    // out a commander's whole arrangement.
    //
    // TransferUndo exists rather than reusing LayoutImportUndo because that
    // one restores the CALLING device's layout, and a commander at the PC
    // importing onto their tablet is not the calling device. It is the same
    // LayoutStore.RestorePrevious underneath - one kept generation, not a
    // second history - just told which device to put back.
    public const string TransferTargets = "/api/transfer/targets";
    public const string TransferProfile = "/api/transfer/profile";
    public const string TransferMacro = "/api/transfer/macro";
    public const string TransferUndo = "/api/transfer/undo";

    // The settings gear's Bindings pane (ref/docs/bindings-source.md): GET
    // reports which preset is actually in effect (name, commander/stock/
    // fallback, version, how many actions resolved to a real key); the
    // refresh route re-runs the whole discovery sweep and applies the
    // selection rule again, so a preset switched or created in Elite after
    // startup does not require a LunaPanel restart to be picked up. Device
    // auth required like every other settings route.
    public const string BindingsStatus = "/api/bindings/status";
    public const string BindingsRefresh = "/api/bindings/refresh";

    // Calibration surface, not product. Unauthenticated for the same reason
    // /api/health is: it exposes no user data, and requiring a paired device
    // before the device can be measured would be circular.
    public const string Probe = "/probe";
    public const string ProbeEstimate = "/probe/estimate";
    public const string ProbeServiceWorker = "/probe/sw.js";
    public const string ProbeLabels = "/probe/labels";
}
