using System.Text.Json;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Macros;
using LunaPanel.Core.Pairing;
using LunaPanel.Core.Theme;

namespace LunaPanel.Server.Http;

/// <summary>
/// <c>GET /</c> - the real panel client: the page a commander actually taps.
/// One self-contained HTML document, same discipline as
/// <see cref="TemplateProbeEndpoint"/> (no static-file pipeline exists in
/// this host), but this one ships - it is not a calibration instrument, and
/// it is never deleted. Plain HTML/CSS/JS only: no client-side framework, no
/// CDN reference, since the device may have no internet at all, only a LAN
/// route to this host.
///
/// Auth-exempt (<see cref="ApiPaths.App"/> is in
/// <see cref="DeviceAuthMiddlewareExtensions"/>'s exempt list) for the same
/// reason <c>/api/pair</c> has to be: an unpaired device has no cookie yet,
/// and the pairing screen this page draws for that case has to be reachable
/// before one exists. Every API call the page makes after that
/// (<c>/api/panel</c>, <c>/api/panel/live</c>, <c>/api/press</c>) still goes
/// through the normal device-auth middleware like any other route - only the
/// page shell itself is exempt.
///
/// <b>Carries three rendering decisions forward verbatim from
/// <c>ref/docs/device-calibration.md</c></b> (settled on real hardware by the
/// commander, via <see cref="TemplateProbeEndpoint"/>, across several
/// rounds) rather than re-deciding them: one uniform type size across the
/// whole grid (the minimum of every label's own fitting size, not a
/// per-button optimum), <c>overflow-wrap: break-word</c> (never
/// <c>anywhere</c> - the latter split words mid-syllable), and
/// <c>white-space: pre-line</c> so a label's own user-chosen line break is
/// honoured. Per-cell padding (~8% of the shorter side, applied before the
/// fit loop measures) and the phone/tablet frame-padding split (12px/16px)
/// are likewise carried forward.
///
/// <b>The chrome-vs-allowance rule <c>device-calibration.md</c> names as a
/// recurring failure</b> - a probe (or client) whose own layout spends more,
/// or less, than the real <see cref="Core.Layouts.CellSizeEstimator"/>'s
/// <c>headerStrip</c>/<c>framePadding</c> allowances misreports the very
/// thing it draws - caused two shipped defects in opposite directions (see
/// <c>tests/notes/live-checks.md</c> LC9) because this page used to restate
/// the estimator's phone/tablet constants a second time as JavaScript
/// functions. <b>That duplication is now closed by construction, not by
/// careful re-syncing (2026-09-07, task 1):</b> <c>GET /api/panel</c> returns
/// <c>headerStrip</c>/<c>framePadding</c>/<c>tabStripHeight</c> directly
/// (<see cref="Core.Layouts.CellSizeEstimator.Allowances"/>, via
/// <c>PanelEndpoint.PanelResponse</c>), and this page's chrome height, tab
/// strip height, and grid padding are all set from those response fields -
/// there is no client-side literal for any of the three any more. A
/// source-scan pin (<c>PanelClientSourceGuardTests</c>) reads this file's own
/// text and fails the build if the removed literal patterns ever reappear
/// under a new name - a behavioural test alone can only prove the current
/// build's output is clean, not that the pattern can never come back. The
/// grid's gutter (<c>8</c>px phone / <c>12</c>px tablet, via
/// <c>isPhone()</c>) is the one allowance still duplicated in JavaScript,
/// matching <see cref="TemplateProbeEndpoint"/>'s own page - it was not
/// implicated in either shipped defect and <c>GET /api/panel</c> does not
/// expose it. See <c>ref/docs/web-client.md</c> for the full account,
/// including the one known gap this shares with the probe (no visual "wider
/// gap" is drawn at a template's <c>BandAfter</c> boundary - only the
/// probe's own already-settled uniform-gap behaviour is carried forward, not
/// a new omission).
/// </summary>
public static class PanelClientEndpoint
{
    /// <summary>
    /// Every route literal the page's JavaScript needs is substituted in from
    /// <see cref="ApiPaths"/> rather than typed twice - the same "one place
    /// this is spelled" discipline <see cref="ApiPaths"/>'s own remarks
    /// describe for the server side, extended to the client. Plain
    /// <see cref="string.Replace(string, string)"/> tokens rather than a C#
    /// interpolated raw string, since the page body is full of literal
    /// <c>{</c>/<c>}</c> (CSS rules, JSON bodies) that interpolation would
    /// otherwise force doubling throughout.
    /// </summary>
    /// <param name="canAuthorMacros">
    /// Whether this request may build, copy or delete a macro - true only
    /// for the host machine (<see cref="HostRequest"/>,
    /// <c>ref/docs/macro-builder.md</c>). <b>Defaults to false, and that
    /// direction is deliberate</b>: a call site that forgets to pass it
    /// serves the page a tablet gets, never the authoring one. The gate it
    /// draws is a courtesy - <see cref="HostOnlyRoutes"/> is what actually
    /// refuses an authoring call, and a device that reaches the route
    /// directly is refused there whatever this page chose to show.
    ///
    /// [2026-09-10] This one argument now fills <b>two</b> page constants -
    /// <c>CAN_AUTHOR_MACROS</c> and <c>IS_HOST</c>
    /// (<c>ref/docs/transfer.md</c>). The name has not been changed to
    /// <c>isHost</c>, which is what it has always literally meant, only
    /// because the caller passing
    /// <see cref="DeviceAuthMiddlewareExtensions.IsHostRequest"/> already
    /// makes that plain at the one call site that matters. Two constants
    /// rather than one alias: they are two capabilities granted by one
    /// condition today, and a pane that hid itself because macro authoring
    /// was off would be answering the wrong question.
    /// </param>
    public static string BuildPage(bool canAuthorMacros = false) => PageHtmlTemplate
        .Replace("__CAN_AUTHOR_MACROS__", canAuthorMacros ? "true" : "false")
        .Replace("__API_PANEL__", ApiPaths.Panel)
        .Replace("__API_PANEL_LIVE__", ApiPaths.PanelLive)
        .Replace("__API_PAIR__", ApiPaths.Pair)
        .Replace("__API_PRESS__", ApiPaths.Press)
        .Replace("__API_THEME__", ApiPaths.Theme)
        .Replace("__API_THEME_RESET__", ApiPaths.ThemeReset)
        .Replace("__API_TEMPLATES__", ApiPaths.Templates)
        .Replace("__API_PANEL_TEMPLATE__", ApiPaths.PanelTemplate)
        .Replace("__API_PANEL_SETTINGS__", ApiPaths.PanelSettings)
        .Replace("__API_PAGE_ADD__", ApiPaths.PageAdd)
        .Replace("__API_PAGE_RENAME__", ApiPaths.PageRename)
        .Replace("__API_PAGE_SHOWWHEN__", ApiPaths.PageShowWhen)
        .Replace("__API_PAGE_DELETE__", ApiPaths.PageDelete)
        .Replace("__API_PAGE_MOVE__", ApiPaths.PageMove)
        .Replace("__API_MACRO_TIMING__", ApiPaths.MacroTiming)
        .Replace("__API_BINDINGS_STATUS__", ApiPaths.BindingsStatus)
        .Replace("__API_BINDINGS_REFRESH__", ApiPaths.BindingsRefresh)
        .Replace("__API_DEVICES__", ApiPaths.Devices)
        .Replace("__API_DEVICES_FORGET__", ApiPaths.DevicesForget)
        .Replace("__API_DEVICES_OPEN_PAIRING__", ApiPaths.DevicesOpenPairing)
        // Editing a specific paired device's real layout live, from the PC.
        // Substituted from DeviceAuthMiddlewareExtensions' own constant
        // rather than typed a second time here - the same "one place this
        // is spelled" discipline every other __API_*__ token already gets.
        .Replace("__ASDEVICE_PARAM__", DeviceAuthMiddlewareExtensions.AsDeviceQueryParam)
        .Replace("__API_ACTIONS__", ApiPaths.Actions)
        // "Press a key" (ref/docs/macros.md): the key picker's own list, and
        // the reverse lookup it calls the moment a key is chosen.
        .Replace("__API_KEYS__", ApiPaths.Keys)
        .Replace("__API_ACTION_FOR_KEY__", ApiPaths.ActionForKey)
        .Replace("__API_SLOT_ASSIGN__", ApiPaths.SlotAssign)
        .Replace("__API_SLOT_LABEL__", ApiPaths.SlotLabel)
        .Replace("__API_SLOT_CLEAR__", ApiPaths.SlotClear)
        .Replace("__API_SLOT_LONGPRESS__", ApiPaths.SlotLongPress)
        .Replace("__API_SLOT_LATCH__", ApiPaths.SlotLatch)
        .Replace("__API_SLOT_HOLD__", ApiPaths.SlotHold)
        .Replace("__API_SLOT_MOVE__", ApiPaths.SlotMove)
        // [2026-09-16] "Make this button a folder" (ref/docs/layouts.md).
        .Replace("__API_SLOT_MAKE_FOLDER__", ApiPaths.SlotMakeFolder)
        .Replace("__API_LAYOUT_IMPORT__", ApiPaths.LayoutImport)
        .Replace("__API_LAYOUT_IMPORT_UNDO__", ApiPaths.LayoutImportUndo)
        .Replace("__API_LAYOUT_IMPORT_DISCARD__", ApiPaths.LayoutImportDiscard)
        .Replace("__API_LAYOUT_RESET__", ApiPaths.LayoutReset)
        // The macro builder (ref/docs/macro-builder.md). Four routes: the
        // list (which the action picker's MACROS group also reads), the
        // save, copy-to-edit and delete verbs, and the vocabulary the step
        // and token pickers are built from. Nothing in this page restates
        // the vocabulary itself - a flag name, a GuiFocus value or a journal
        // event typed here a second time is one that could drift out of
        // agreement with the parser that has to accept it.
        .Replace("__API_MACROS__", ApiPaths.Macros)
        .Replace("__API_MACROS_COPY__", ApiPaths.MacrosCopy)
        .Replace("__API_MACROS_DELETE__", ApiPaths.MacrosDelete)
        .Replace("__API_MACROS_VOCABULARY__", ApiPaths.MacrosVocabulary)
        // Import and export (ref/docs/transfer.md). IS_HOST is substituted
        // from the same argument CAN_AUTHOR_MACROS is, and is deliberately
        // its own constant rather than an alias of it: they are two
        // capabilities that happen to be granted by one condition today, and
        // a page that hid its export pane because macro authoring was off
        // would be answering the wrong question. Neither is enforcement -
        // HostOnlyRoutes refuses every route behind both.
        .Replace("__IS_HOST__", canAuthorMacros ? "true" : "false")
        .Replace("__API_TRANSFER_TARGETS__", ApiPaths.TransferTargets)
        .Replace("__API_TRANSFER_PROFILE__", ApiPaths.TransferProfile)
        .Replace("__API_TRANSFER_MACRO__", ApiPaths.TransferMacro)
        .Replace("__API_TRANSFER_UNDO__", ApiPaths.TransferUndo)
        // The phone/tablet split, substituted from the ONE place it is
        // decided (CellSizeEstimator.PhoneTabletBoundary) rather than typed
        // a second time. The page needs it for two things now - which gutter
        // to draw, and which class to report at pairing
        // (ref/docs/layout-import.md) - and a client that disagreed with the
        // server about where the boundary sits would name a device "Tablet"
        // and then lay it out as a phone.
        .Replace("__PHONE_TABLET_BOUNDARY__", CellSizeEstimator.PhoneTabletBoundary.ToString(System.Globalization.CultureInfo.InvariantCulture))
        // The label-fit rule (ref/docs/button-naming.md's "The flat cap has
        // to go") - the SAME two numbers LayoutValidator.LabelFitsBudget
        // actually validates against, substituted in rather than typed a
        // second time, so the rename dialog can never accept a name the
        // validator would then reject at save.
        .Replace("__LABEL_MAX_LINES__", LayoutValidator.UserLabelMaxLines.ToString())
        .Replace("__LABEL_LINE_CAP__", LayoutValidator.UserLabelLineCap.ToString())
        // The same cap PairEndpoint applies server-side, so the field cannot
        // accept a name that would then be silently truncated.
        .Replace("__DEVICE_NAME_MAX__", DeviceNaming.MaxNameLength.ToString())
        // The one sentence this project uses for an action with no key in
        // Elite, substituted from PressEndpoint's own constant rather than
        // typed here. GET /api/macros already reports it per step for a
        // SAVED macro; the builder needs the same sentence for a step being
        // edited, which no server response has seen yet. Substituting it is
        // what keeps ref/docs/macro-builder.md's question 5 answer true -
        // "the third wording is structurally impossible rather than merely
        // avoided" - now that a second surface says it.
        .Replace("__NOT_BOUND_ADVICE__", PressEndpoint.NotBoundInEliteAdvice)
        // The default a waitForEdge step gets when it names no timeout of
        // its own (MacroTimingDefaults.DefaultWaitForEdgeTimeout, measured
        // live at O28). The builder pre-fills its timeout field with this,
        // so the number a commander sees before touching anything is the
        // number the runner would actually have used.
        .Replace("__EDGE_TIMEOUT_DEFAULT_MS__", ((int)MacroTimingDefaults.DefaultWaitForEdgeTimeout.TotalMilliseconds).ToString())
        .Replace("__PALETTE_JSON__", BuildPaletteJson())
        .Replace("__DARK_PALETTE_JSON__", BuildDarkPaletteJson());

    private static readonly JsonSerializerOptions PaletteJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The built-in override palette (<see cref="HudPalette"/>), embedded
    /// the same "no copy pasted into the page" way
    /// <see cref="TemplateProbeEndpoint"/> already reads its curated labels
    /// from the shipped catalogue - the page's swatch pickers read from the
    /// same data the product ships, never a second hand-typed list that
    /// could drift.
    /// </summary>
    private static string BuildPaletteJson() => JsonSerializer.Serialize(
        HudPalette.Swatches.Select(s => new { name = s.Name, hex = s.Color.ToHex() }),
        PaletteJsonOptions);

    /// <summary>
    /// The Background-only darker palette (<see cref="HudPalette.DarkSwatches"/>,
    /// ref/docs/theme.md's "Known limit, not fixed here: every offered
    /// background is a bright one"). Embedded the same way as
    /// <see cref="BuildPaletteJson"/> above and for the same reason - the
    /// picker reads from the shipped catalogue, never a second hand-typed
    /// list that could drift from it.
    /// </summary>
    private static string BuildDarkPaletteJson() => JsonSerializer.Serialize(
        HudPalette.DarkSwatches.Select(s => new { name = s.Name, hex = s.Color.ToHex() }),
        PaletteJsonOptions);

    private const string PageHtmlTemplate = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<title>LunaPanel</title>
<style>
  /* Stock-orange fallback, overridden wholesale by the injected #theme-vars
     block below once GET /api/panel's themeCss arrives - see HudThemeCssRenderer
     (ref/docs/theme.md). Both blocks declare the same six variables on :root;
     since #theme-vars appears later in the document, its declarations win the
     cascade the instant it is populated, with no per-rule var(...,fallback)
     needed anywhere else in this stylesheet. */
  :root {
    /* Declares that this page paints its own dark theme, which is what
       exempts it from a mobile browser's force-dark ("Darken websites")
       transform. Without it, Chrome on Android rewrites light element
       BACKGROUNDS at paint time while leaving text colours alone - so the
       settings swatches, whose whole job is to show a colour faithfully,
       were the one place it did visible damage.

       Diagnosed 2026-09-08 across two devices showing the SAME response:
       the Lit swatch reported inline "background: rgb(255,255,255)" and
       computed rgb(255,255,255) on both, and painted white on the PC and
       near-black on the tablet. The border colour, a very light cyan,
       darkened to teal; the text colour, a mid-tone pink, was left alone -
       that lightness-dependent selectivity is force-dark's signature.
       (Deliberately described rather than quoted as hex: this page must
       contain no hardcoded palette literals, which
       BuildPage_DeletesTheTemporaryPaletteExperiment enforces - and it
       caught this comment.) Computed style is
       correct in both cases because the transform happens after it, which
       is why several rounds of reading the client's own values proved
       nothing. See ref/docs/theme.md.

       This is a declaration, not a workaround: the page IS dark, and every
       colour in it is the commander's own resolved HUD colour, which no
       browser should be second-guessing. */
    color-scheme: dark;

    --lp-ground: #07090c;
    --lp-frame: #ff7100;
    --lp-text: #ffb000;
    --lp-accent: #ffd27a;
    --lp-lit: #ffe9b0;
    --lp-dim: #6b4a12;

  }
  * { box-sizing: border-box; }
  html, body { height: 100%; margin: 0; overflow: hidden; }
  body {
    background: var(--lp-ground); color: var(--lp-text);
    font-family: "Segoe UI", Roboto, system-ui, sans-serif;
    -webkit-text-size-adjust: 100%;
    -webkit-tap-highlight-color: transparent;
  }

  .screen { position: absolute; inset: 0; display: flex; flex-direction: column; }
  .hidden { display: none !important; }

  #loadingScreen, #pairScreen { align-items: center; justify-content: center; text-align: center; padding: 24px; }

  #pairBox { width: min(340px, 90vw); display: flex; flex-direction: column; gap: 14px; }
  #pairBox h1 { font-size: 15px; letter-spacing: .12em; margin: 0; font-weight: 600; }
  #pairBox p { margin: 0; font-size: 13px; color: var(--lp-dim); }
  #pairCode {
    font: inherit; font-size: 24px; letter-spacing: .3em; text-align: center;
    text-transform: uppercase; background: transparent; color: var(--lp-text);
    border: 1px solid var(--lp-frame); border-radius: 6px; padding: 12px; width: 100%;
  }
  #pairName {
    font: inherit; font-size: 15px; text-align: center;
    background: transparent; color: var(--lp-text);
    border: 1px solid var(--lp-frame); border-radius: 6px; padding: 10px; width: 100%;
  }
  .pairHint { font-size: 11px; color: var(--lp-dim); margin: -6px 0 0; }

  /* The layout import list (ref/docs/layout-import.md) - reached from the
     Devices pane at any time, and from the pairing screen when a layout no
     live device owns exists. Same visual language as the Devices pane's own
     rows rather than a new one. */
  #importIntro { font-size: 12px; color: var(--lp-dim); margin: 0 0 10px; }
  .importRow {
    display: flex; align-items: center; justify-content: space-between; gap: 12px;
    padding: 8px 0; border-bottom: 1px solid var(--lp-dim);
  }
  /* flex: 1 is load-bearing, not decoration (2026-09-17): without it this
     block only takes its natural content width, so "Use these"/Delete
     floated at a different x-position on every row depending on how long
     that device's own name happened to be, instead of sitting at a fixed
     right edge. min-width: 0 lets the name/meta actually truncate instead
     of forcing the row wider once this can grow. */
  .importInfo { display: flex; flex-direction: column; gap: 2px; min-width: 0; flex: 1 1 auto; }
  .importName, .importMeta { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .importName { font-size: 13px; }
  .importMeta { font-size: 11px; color: var(--lp-dim); }
  #importLayouts, #importStartFresh, .importPickBtn {
    font: inherit; font-size: 12px; background: transparent; color: var(--lp-text);
    border: 1px solid var(--lp-frame); border-radius: 6px; padding: 8px 14px;
    cursor: pointer; flex: 0 0 auto; white-space: nowrap;
  }
  #importLayouts { margin-bottom: 10px; }
  /* Secondary/muted styling so this never competes with "Use these" - a
     plain text-style control, not a bordered button, since this action is
     irreversible and should not draw the eye first. */
  .importDeleteBtn {
    font: inherit; font-size: 11px; background: transparent; color: var(--lp-dim);
    border: none; padding: 4px 6px; cursor: pointer; flex: 0 0 auto; white-space: nowrap;
    text-decoration: underline;
  }
  /* "Start fresh" is an equal option, not a fallback - same weight as every
     row's own button, sitting with them rather than styled as a dismissal. */
  #importStartFresh { margin-top: 14px; width: 100%; }

  #pairSubmit, #fsBtn, #gearBtn, #editBtn {
    font: inherit; font-size: 13px; letter-spacing: .06em; background: transparent;
    color: var(--lp-text); border: 1px solid var(--lp-frame); border-radius: 6px;
    padding: 10px 16px; cursor: pointer;
  }
  #pairSubmit:disabled { opacity: .5; cursor: default; }
  #pairError { min-height: 1.2em; font-size: 13px; color: #ff6b5e; }

  #chrome {
    flex: 0 0 auto; display: flex; align-items: center; justify-content: space-between;
    padding: 0 12px; border-bottom: 1px solid var(--lp-frame);
  }
  #chrome .title { font-weight: 600; letter-spacing: .12em; font-size: 13px; white-space: nowrap; }
  #fsBtn, #editBtn { padding: 4px 12px; }

  #tabs {
    flex: 0 0 auto; display: flex; gap: 6px; align-items: center;
    overflow-x: auto; padding: 0 12px; border-bottom: 1px solid var(--lp-frame);
  }
  .tab {
    font: inherit; font-size: 12px; letter-spacing: .06em; background: transparent;
    color: var(--lp-dim); border: 1px solid var(--lp-frame); border-radius: 6px;
    padding: 4px 10px; cursor: pointer; white-space: nowrap; flex: 0 0 auto;
  }
  .tab.active { color: var(--lp-text); border-color: var(--lp-lit); }
  /* [2026-09-16] Drag-to-reorder the tab bar (ref/docs/panels-and-pages.md)
     is edit-mode-only - same "mutually exclusive by construction" split the
     grid's own editMode/wireEditGesture already uses, not an always-on
     gesture layered onto ordinary tap/long-press. Scoped to #tabs.editing,
     not the base .tab rule: outside edit mode a tab row long enough to
     overflow still scrolls normally by touch (#tabs already sets
     overflow-x: auto), and only edit mode's own drag needs touch-action
     suppressed the way #grid.editing .slot already does for the same
     reason (see that rule's own remarks). The dashed border and grip glyph
     are the visible "these can be dragged" cue - same dashed-border language
     .slot.empty already uses for "this is editable". */
  #tabs.editing .tab:not(.tabAdd) {
    touch-action: none; user-select: none; -webkit-user-select: none;
    border-style: dashed;
  }
  #tabs.editing .tab:not(.tabAdd)::before { content: '\22ee\22ee\2004'; opacity: .55; }
  /* Same shape as .slot.dragging/.slot.drag-target above, mirrored here
     because those are scoped to .slot and not reusable as-is. A tab row
     only moves left/right, unlike the grid's 2-D drag. */
  .tab.dragging { opacity: .55; z-index: 5; }
  .tab.drag-target { border-style: solid; border-color: var(--lp-lit); color: var(--lp-lit); }
  /* [2026-09-12] The page bar's "+" (ref/docs/panels-and-pages.md): same
     .tab shape as every named tab, so it sits in the row rather than
     looking like a second control, with only the glyph and a brighter
     resting colour marking it as an action rather than a page. */
  .tabAdd { color: var(--lp-lit); font-weight: 700; }
  /* [2026-09-16] Folders (ref/docs/layouts.md). Inside a folder these two
     REPLACE the whole tab row rather than joining it - the commander's own
     ruling - so they are styled to read as a location and a way out, not as
     two more tabs among several. .tabUp keeps the .tab shape (it is a real
     control in the same row) with the lit colour every actionable chrome
     control here uses; .tabFolderName is deliberately borderless and
     unpadded so it cannot be mistaken for something tappable, which matters
     more than usual on a touch surface where the only way to find out is to
     try it. */
  .tabUp { color: var(--lp-lit); font-weight: 700; }
  .tabFolderName {
    font-size: 12px; letter-spacing: .06em; color: var(--lp-dim);
    white-space: nowrap; flex: 0 0 auto; padding: 4px 2px; cursor: default;
  }

  #stage { position: relative; flex: 1 1 auto; overflow: hidden; }
  #grid { position: absolute; inset: 0; display: grid; align-content: start; justify-content: center; }

  .slot {
    position: relative; border: 1px solid var(--lp-frame); border-radius: 6px;
    background: transparent; color: var(--lp-text);
    display: flex; align-items: center; justify-content: center; text-align: center;
    overflow-wrap: break-word; white-space: pre-line; line-height: 1.15;
    font-weight: 600; letter-spacing: .03em; font-family: inherit; cursor: pointer;
    transition: filter .1s ease;
  }
  .slot.pressed { filter: brightness(1.7); }
  /* [2026-09-12] The hold gesture (ref/docs/latching-keys.md's
     hold-to-thrust extension) does real pointerdown/pointerup, not a tap -
     without this, a sustained touch is claimed by the browser as a
     scroll/pan gesture (touch-action) or a text-selection gesture
     (user-select), either of which can eat the pointerup this feature
     depends on to ever release the key. Scoped to .holdable ONLY - not the
     base .slot rule - because every other button's tap and long-press
     gestures must keep exactly the touch behaviour they already have. */
  .slot.holdable { touch-action: none; user-select: none; -webkit-user-select: none; }
  /* [2026-09-12] The shrink loop below (layoutGrid) floors at MIN_PX and
     stops there even if the label still overflows - reported live on a
     tablet's t64 layout, whose narrower cells spilled "Nomad Dock/Launch"
     past the button's edge. Applied by JS only to a button whose OWN loop
     bottomed at MIN_PX while still overflowing (see layoutGrid's
     overflowingAtFloor pass) - every other button never gets this class, so
     normal-fitting buttons on every other layout are unaffected. Plain
     overflow: hidden, not text-overflow: ellipsis - labels here wrap across
     multiple lines (white-space: pre-line above), and ellipsis only applies
     to a single unwrapped line. */
  .slot.labelClipped { overflow: hidden; }
  /* Lit level (ref/docs/lit-state.md), the commander's own words: "off =
     not lit, regular = partial brightness on the edges, full beam = high
     glowing edges." Off is neither class below - the base .slot style's
     own transparent border/inherited text colour. Partial applies the
     border/text colour alone; Full layers the glow on top of the exact
     same colour, never a second, independently-driftable declaration of
     it. */
  .slot.lit {
    border-color: var(--lp-lit); color: var(--lp-lit);
  }
  /* Spread (the second length) is what makes this read as a halo rather than
     a soft edge. The original 0 0 10px with no spread WAS rendering - proven
     2026-09-08 by temporarily adding a red outline to this same rule, which
     appeared while the glow still did not - it was simply too weak to see
     against the panel's ground at device scale. Two rounds were spent hunting
     a client bug that never existed. Do not reduce the spread to zero. */
  /* Two layered shadows: a hard ring, then a halo behind it.

     The hard ring matters because .slot.lit has already set the border to
     this same colour, so a purely blurred halo starting just outside a
     bright border has little to contrast against and reads as a slightly
     thicker border rather than a glow.

     Diagnosed the long way on 2026-09-08: the class was proven applied (a
     red outline appeared), box-shadow was proven to render (a zero-blur red
     ring appeared), and the real cause turned out to be the COLOUR - the
     commander's --lp-lit was effectively the ground colour, so the border
     and the glow were both being drawn and both invisible. See
     ref/docs/theme.md. Do not collapse these into one blurred shadow. */
  .slot.lit-full {
    box-shadow: 0 0 0 3px var(--lp-lit), 0 0 20px 5px var(--lp-lit);
  }
  .slot.degraded {
    border-style: dashed; border-color: var(--lp-dim); color: var(--lp-dim);
    cursor: not-allowed; opacity: .8;
  }
  .slot.degraded::after {
    content: "!"; position: absolute; top: 2px; right: 6px; font-size: 11px; font-weight: 700;
  }
  /* Visible affordance for a slot that has a long-press action
     (ref/docs/editor.md's long-press wiring): a touch surface has no
     tooltips, so a hidden second action would be undiscoverable - opposite
     corner from .slot.degraded's "!" so the two never collide. Shown
     whenever the slot HAS a long-press action, whether that action is
     currently Ok or degraded - degradation is surfaced by opening the slot
     sheet, same as the primary action. */
  .slot.has-long-press::before {
    content: ""; position: absolute; bottom: 4px; right: 6px;
    width: 5px; height: 5px; border-radius: 50%; background: currentColor; opacity: .7;
  }
  /* Visible affordance for a slot that LATCHES rather than taps
     (ref/docs/latching-keys.md) - same reasoning as .has-long-press right
     above: a touch surface has no tooltips, and a button that will hold a
     key down until tapped again must not be indistinguishable from one that
     taps it. A bar rather than a dot, bottom-LEFT rather than bottom-right,
     so a slot carrying both markers reads as two things and not one smudge.

     Deliberately currentColor and nothing else: the LATCHED state itself is
     shown through the existing lit vocabulary (.slot.lit + .slot.lit-full,
     applied server-side by LatchedLit), so this marker says only "this
     button latches", never "this button is latched". Introducing a colour
     role for either would be the fourth visual language ref/docs/lit-state.md
     exists to prevent. */
  .slot.has-latch::after {
    content: ""; position: absolute; bottom: 5px; left: 6px;
    width: 9px; height: 3px; border-radius: 1px; background: currentColor; opacity: .7;
  }
  /* [2026-09-16] Visible affordance for a FOLDER button (ref/docs/layouts.md)
     - same reasoning as the two markers above, and the same reason they are
     each in their own corner: a button that navigates instead of firing must
     not be indistinguishable from one that fires, or a commander reaches for
     a control mid-fight and changes page instead. Top-left, where nothing
     else draws (.degraded's "!" is top-right, long-press bottom-right, latch
     bottom-left), and a chevron rather than another dot or bar so the three
     markers stay three different shapes. */
  .slot.is-folder::before {
    content: "\25B8"; position: absolute; top: 3px; left: 5px;
    font-size: 11px; line-height: 1; opacity: .75;
  }

  #toast {
    position: fixed; left: 50%; bottom: 24px; transform: translateX(-50%);
    max-width: 82vw; background: rgba(0, 0, 0, .85); border: 1px solid var(--lp-frame);
    color: var(--lp-text); padding: 10px 16px; border-radius: 6px; font-size: 13px;
    text-align: center; opacity: 0; pointer-events: none; transition: opacity .2s ease;
    z-index: 1000;
  }
  #toast.show { opacity: 1; }

  .chromeButtons { display: flex; align-items: center; gap: 8px; }
  #gearBtn { padding: 4px 10px; font-size: 15px; line-height: 1; }

  .sheet {
    /* Centred, not bottom-anchored (2026-09-10). A sheet that rises from the
       bottom edge is the phone idiom, and this page is opened on a desktop
       browser as often as on a tablet - the commander: "I want to move the
       panel for settings and controls from the bottom of the screen to the
       middle of the screen. It's really odd on the web interface, especially
       for a computer."

       Centring does not cost the live-preview behaviour described below: the
       panel's height is still capped, so instead of one band of grid above it
       there is now a band above AND below, which is if anything easier to
       watch a colour change in. */
    position: fixed; inset: 0; z-index: 900; display: flex; align-items: center;
    justify-content: center;
    /* Light enough to still read the grid's real colours through.

       This sheet's own height is deliberately capped so the grid stays
       visible around it, because every swatch pick repaints that grid live -
       seeing the effect IS the feature. A heavy scrim defeats exactly that:
       the grid stays visible but its colours do not, which is how a white
       Lit swatch and its glow came to be read as "no glow" repeatedly on
       2026-09-08 while nothing was wrong with either. Kept non-zero because
       the sheet still needs to separate from a busy grid. */
    background: rgba(0, 0, 0, .25);

  }
  .sheet.hidden { display: none !important; }
  .sheetPanel {
    /* Capped so the grid behind stays visible: this sheet configures that
       grid, and "changes apply immediately" (every swatch pick re-fetches
       and repaints it) is only useful if enough of the grid can be seen to
       actually watch the effect - see ref/docs/web-client.md. Raised from
       46vh when the sheet moved to the centre (2026-09-10): a bottom sheet
       had to stay short to leave one usable band above it, while a centred
       one leaves a band above and below at any height, so the cap can go
       back up and stop scrolling the longer panes. */
    width: min(480px, calc(100vw - 24px)); max-height: 70vh; overflow-y: auto;
    background: var(--lp-ground); color: var(--lp-text);
    /* All four corners now it floats clear of the bottom edge - two square
       corners only ever made sense on something joined to that edge. */
    border: 1px solid var(--lp-frame); border-radius: 12px; padding: 16px 18px;
  }
  .sheetHeader {
    display: flex; justify-content: space-between; align-items: center;
    margin-bottom: 14px; font-weight: 600; letter-spacing: .1em; font-size: 13px;
  }
  .sheetHeader button {
    font: inherit; background: transparent; color: var(--lp-text);
    border: 1px solid var(--lp-frame); border-radius: 6px; padding: 4px 10px; cursor: pointer;
  }
  .sheetTabs { display: flex; gap: 8px; margin-bottom: 14px; }
  .sheetTab {
    font: inherit; font-size: 12px; letter-spacing: .06em; background: transparent;
    color: var(--lp-dim); border: 1px solid var(--lp-frame); border-radius: 6px;
    padding: 6px 12px; cursor: pointer;
  }
  .sheetTab.active { color: var(--lp-text); }
  #colourSource { font-size: 13px; color: var(--lp-dim); margin: 0 0 10px; }
  .roleRow { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin: 12px 0; }
  .roleRow > span { font-size: 12px; letter-spacing: .06em; color: var(--lp-dim); white-space: nowrap; }
  .swatchPicker { display: flex; flex-wrap: wrap; gap: 6px; justify-content: flex-end; }
  .swatch {
    width: 22px; height: 22px; border-radius: 50%; padding: 0; cursor: pointer;
    border: 1px solid rgba(255, 255, 255, .35);
  }
  /* The three always-visible role rows and the "one colour for everything"
     row each show one big swatch of their CURRENT colour - tapping it opens
     #colourPicker rather than expanding a picker inline (ref/docs/web-client.md). */
  .roleSwatch { width: 30px; height: 30px; }
  /* The contrast warning (ref/docs/theme.md's contrast section) - styled
     like the rest of the pane rather than as a browser-red alert, because
     it reports a choice that has already been applied, not an error. */
  #colourWarning {
    font-size: 12px; line-height: 1.4; color: var(--lp-text); margin: 12px 0 0;
    border: 1px solid var(--lp-frame); border-radius: 6px; padding: 8px 10px;
  }
  #resetTheme {
    margin-top: 10px; font: inherit; font-size: 12px; background: transparent;
    color: var(--lp-text); border: 1px solid var(--lp-frame); border-radius: 6px;
    padding: 8px 14px; cursor: pointer;
  }
  .pickerGroup { margin-bottom: 14px; }
  .pickerGroup.hidden { display: none !important; }
  .pickerGroupLabel { font-size: 12px; letter-spacing: .06em; color: var(--lp-dim); margin: 0 0 8px; }

  .sheetPane.hidden { display: none !important; }

  /* The rung ladder (ref/docs/panels-and-pages.md's "Panels" pane) - the
     same per-rung comfort verdict TemplateProbeEndpoint already colour-codes
     on the calibration page, reached here from GET /api/templates instead of
     a second, hand-typed ladder. */
  .rungLadder { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 14px; }
  .rung {
    font: inherit; font-size: 13px; letter-spacing: .04em; background: transparent;
    border: 1px solid var(--lp-dim); border-radius: 6px; padding: 8px 12px; cursor: pointer;
    display: flex; flex-direction: column; align-items: center; line-height: 1.2;
  }
  .rung.active { border-color: var(--lp-frame); }
  .rung.v-Comfortable { color: #38a169; }
  .rung.v-Compact { color: var(--lp-text); }
  .rung.v-TooSmall { color: #d13b2e; }

  /* Never had its own rule at all - rendered as a bare, unstyled browser
     button until 2026-09-18. Same visual language as the other panes'
     action buttons (#resetTheme/#refreshBindings/#addAnotherDevice)
     rather than a new one. */
  #resetToDefault {
    margin-top: 10px; font: inherit; font-size: 12px; background: transparent;
    color: var(--lp-text); border: 1px solid var(--lp-frame); border-radius: 6px;
    padding: 8px 14px; cursor: pointer;
  }

  .mergeExpandRow { display: flex; align-items: center; gap: 8px; }
  #mergeExpandHelp, #autoSwitchEnabledHelp {
    font: inherit; font-size: 12px; background: transparent; color: var(--lp-text);
    border: 1px solid var(--lp-frame); border-radius: 50%; width: 22px; height: 22px;
    padding: 0; cursor: pointer;
  }

  /* The Timing pane (ref/docs/macro-timing.md) - shares #mergeExpandHelp's
     tap-to-open "?" visual language rather than inventing a second one. */
  .timingRow { display: flex; align-items: center; gap: 8px; }
  .timingRow input[type="number"] {
    font: inherit; font-size: 14px; width: 64px; background: transparent;
    color: var(--lp-text); border: 1px solid var(--lp-frame); border-radius: 6px;
    padding: 6px 8px;
  }
  #holdMsHelp, #gapMsHelp {
    font: inherit; font-size: 12px; background: transparent; color: var(--lp-text);
    border: 1px solid var(--lp-frame); border-radius: 50%; width: 22px; height: 22px;
    padding: 0; cursor: pointer;
  }
  /* Shown automatically (never tap-triggered) whenever that field's own
     value is below its measured minimum - ref/docs/macro-timing.md's "the
     cliff": "allow it, label it, and never let a commander arrive there
     without being told." Distinct from the tap-to-open "?" tooltips above,
     which explain the field regardless of its current value. */
  .timingWarning { font-size: 12px; color: #d13b2e; margin: -6px 0 12px; }

  /* The Bindings pane (ref/docs/bindings-source.md's "It has to be
     visible") - one row, the current source with the refresh action beside
     it, same colours as the rest of the sheet (#colourSource/#resetTheme)
     rather than a new visual language. */
  .bindingsRow { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
  #bindingsStatus { font-size: 13px; color: var(--lp-dim); }
  #refreshBindings {
    font: inherit; font-size: 12px; background: transparent;
    color: var(--lp-text); border: 1px solid var(--lp-frame); border-radius: 6px;
    padding: 8px 14px; cursor: pointer; flex: 0 0 auto; white-space: nowrap;
  }
  #refreshBindings:disabled { opacity: .5; cursor: default; }

  /* The Devices pane (ref/docs/pairing-and-devices.md) - same visual
     language as the other panes (#resetTheme/#refreshBindings) rather than
     a new one. */
  #addAnotherDevice {
    font: inherit; font-size: 12px; background: transparent;
    color: var(--lp-text); border: 1px solid var(--lp-frame); border-radius: 6px;
    padding: 8px 14px; cursor: pointer; margin-bottom: 10px;
  }
  #devicePairingCode {
    font-size: 15px; letter-spacing: .08em; text-align: center;
    border: 1px solid var(--lp-frame); border-radius: 6px; padding: 8px; margin: 0 0 12px;
  }
  .deviceRow {
    display: flex; align-items: center; justify-content: space-between; gap: 12px;
    padding: 8px 0; border-bottom: 1px solid var(--lp-dim);
  }
  .deviceInfo { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
  .deviceName { font-size: 13px; }
  .deviceMeta { font-size: 11px; color: var(--lp-dim); }
  .forgetDeviceBtn {
    font: inherit; font-size: 12px; background: transparent; color: var(--lp-text);
    border: 1px solid var(--lp-frame); border-radius: 6px; padding: 6px 12px;
    cursor: pointer; flex: 0 0 auto; white-space: nowrap;
  }
  .editDeviceLiveBtn {
    font: inherit; font-size: 12px; background: transparent; color: var(--lp-text);
    border: 1px solid var(--lp-frame); border-radius: 6px; padding: 6px 12px;
    cursor: pointer; flex: 0 0 auto; white-space: nowrap; margin-right: 8px;
  }

  /* Editing a specific paired device's real layout live, from the PC - a
     fixed overlay banner, never part of the normal flow (see the HTML
     comment beside #editingDeviceBanner for why). */
  #editingDeviceBanner {
    position: fixed; top: 0; left: 0; right: 0; z-index: 10000;
    display: flex; align-items: center; justify-content: center; gap: 12px;
    background: var(--lp-lit); color: var(--lp-ground); font-size: 13px; padding: 6px 10px;
  }
  #editingDeviceBannerStop {
    font: inherit; font-size: 12px; background: transparent; color: inherit;
    border: 1px solid currentColor; border-radius: 6px; padding: 4px 10px; cursor: pointer;
  }
  /* The banner is position: fixed (see above) and draws on top of
     #panelScreen, which would otherwise sit directly under it - overlapping
     #chrome's own row and hiding #editBtn and #gearBtn beneath it
     (live-tested defect, ver-0.43.0.0-dev). This pushes
     panelScreen's whole flex column down by the banner's actual rendered
     height instead - #chrome's own declared height is untouched, only where
     it (and everything below it) starts. --editingBannerOffset is set in JS
     only while the banner is actually shown (initEditingDeviceBanner below);
     the 0px fallback here is what keeps every other screen, and panelScreen
     itself whenever EDITING_DEVICE_ID is unset, completely unaffected. */
  #panelScreen { padding-top: var(--editingBannerOffset, 0px); }

  /* The on-device editor (ref/docs/editor.md). #editBtn toggles the mode -
     leaning-towards-a-mode per the spec's own "Not decided" (always-live
     risks reassigning a button mid-flight from a stray tap). While the mode
     is on, EVERY index in the current template's active range is tappable,
     including one with nothing assigned yet (.slot.empty, a dashed "+"
     placeholder) - not only the occupied ones GET /api/panel's slots[] ever
     lists (ref/docs/panel-api.md), which is what lets an empty slot be
     assigned at all rather than only ever re-assigned. */
  #editBtn.active { border-color: var(--lp-lit); color: var(--lp-lit); }
  .slot.empty {
    border-style: dashed; border-color: var(--lp-dim); color: var(--lp-dim);
  }

  /* Drag a button to a different slot, in edit mode, on the tablet
     (ref/docs/editor.md). touch-action: none is what makes this work with
     a FINGER at all: without it the browser claims a vertical finger move
     as a pan gesture and sends pointercancel mid-drag, so the drop never
     arrives. It is scoped to #grid.editing - outside edit mode the panel's
     buttons keep the browser's default touch handling exactly as they had
     it, because nothing there wants a drag. user-select: none stops a
     mouse drag selecting the button's own label text as it goes. */
  #grid.editing .slot { touch-action: none; user-select: none; -webkit-user-select: none; }
  .slot.dragging { opacity: .55; z-index: 5; }
  .slot.drag-target { border-style: solid; border-color: var(--lp-lit); color: var(--lp-lit); }

  #slotSheetStatus { font-size: 13px; color: var(--lp-dim); margin: 0 0 14px; }
  .editAction {
    display: block; width: 100%; font: inherit; font-size: 13px; text-align: left;
    background: transparent; color: var(--lp-text); border: 1px solid var(--lp-frame);
    border-radius: 6px; padding: 10px 14px; margin-bottom: 8px; cursor: pointer;
  }
  /* The one red-ish accent this app uses, reserved for destructive actions -
     same colour #macroDelete already carries, reused rather than a second
     hand-picked red. */
  .dangerAction { color: #d13b2e; }

  #actionPickerSearch {
    display: block; width: 100%; font: inherit; font-size: 13px; background: transparent;
    color: var(--lp-text); border: 1px solid var(--lp-frame); border-radius: 6px;
    padding: 8px 12px; margin-bottom: 10px;
  }
  #actionPickerList { max-height: 40vh; overflow-y: auto; }
  .actionRow {
    display: flex; align-items: center; justify-content: space-between; gap: 10px;
    width: 100%; font: inherit; font-size: 13px; text-align: left; background: transparent;
    color: var(--lp-text); border: none; border-bottom: 1px solid var(--lp-dim);
    padding: 9px 2px; cursor: pointer;
  }
  .actionRowLabel { white-space: pre-line; }
  .actionRowState { font-size: 11px; color: var(--lp-dim); white-space: nowrap; flex: 0 0 auto; }
  /* Marked as unbound BEFORE assignment, not only after - a commander must
     not have to place a button to find out it will not work
     (ref/docs/editor.md's expanded-picker decision). */
  .actionRow.unbound .actionRowLabel { color: var(--lp-dim); }
  .actionRow.unbound .actionRowState { color: #d13b2e; }

  #renameInput {
    display: block; width: 100%; font: inherit; font-size: 15px; text-align: center;
    background: transparent; color: var(--lp-text); border: 1px solid var(--lp-frame);
    border-radius: 6px; padding: 10px; resize: none; white-space: pre-line;
  }
  .renameButtons { display: flex; justify-content: flex-end; gap: 8px; margin-top: 12px; }
  .renameButtons button {
    font: inherit; font-size: 13px; background: transparent; color: var(--lp-text);
    border: 1px solid var(--lp-frame); border-radius: 6px; padding: 8px 16px; cursor: pointer;
  }

  /* ------------------------------------------------------------------
     The macro builder (ref/docs/macro-builder.md). The commander asked
     for "a friendly, easily usable UI with tooltips and basic
     instructions" - so the instructions are ON the screens, in
     .paneIntro/.stepAbout, not in a document nobody opens, and every
     control that could mislead has a tap-to-open "?" beside it in the
     same visual language #mergeExpandHelp and the Timing pane already
     use. Nothing here reveals anything on pointer-over, here or anywhere
     on this page: these are touch devices, and a tooltip that needs a
     pointer resting on it is invisible on the exact hardware this ships
     to. (Deliberately not naming the CSS pseudo-class it would be
     written with - BuildPage_MergeExpandHelp_IsTapToOpen_NeverHover
     sweeps the whole page for that string, and it caught this comment.)
     ------------------------------------------------------------------ */

  /* Six tabs no longer fit one phone-width row. Wrapping rather than
     scrolling, so a tab can never end up off-screen with nothing saying
     it is there. */
  .sheetTabs { flex-wrap: wrap; }

  /* The "basic instructions on the screen itself" the commander asked
     for. Deliberately full sentences rather than a caption: the person
     reading this has just met the word "macro" in this product. */
  .paneIntro { font-size: 12px; line-height: 1.5; color: var(--lp-dim); margin: 0 0 12px; }

  #macroNew, .macroActions button {
    font: inherit; font-size: 12px; background: transparent; color: var(--lp-text);
    border: 1px solid var(--lp-frame); border-radius: 6px; padding: 8px 14px; cursor: pointer;
  }
  #macroNew { margin-bottom: 12px; }

  /* The import/export pane (ref/docs/transfer.md) - the same visual
     language as #resetTheme/#addAnotherDevice/#macroNew rather than a new
     one. The selects name their own background explicitly: a transparent
     select draws its dropdown list on the browser's default white on
     several platforms, and amber-on-white is not readable. */
  .transferRow { display: flex; align-items: center; gap: 8px; min-width: 0; }
  .transferRow select {
    font: inherit; font-size: 12px; background: var(--lp-ground); color: var(--lp-text);
    border: 1px solid var(--lp-frame); border-radius: 6px; padding: 7px 8px;
    max-width: 42vw;
  }
  .transferRow button, #transferUndo {
    font: inherit; font-size: 12px; background: transparent; color: var(--lp-text);
    border: 1px solid var(--lp-frame); border-radius: 6px; padding: 8px 14px;
    cursor: pointer; white-space: nowrap;
  }
  /* Says what just happened, in the pane, and stays until something else
     happens - never a toast. An import reports how many macros arrived and
     how many buttons name one this PC does not have, and that is a sentence
     somebody needs to be able to re-read. */
  #transferResult {
    font-size: 12px; line-height: 1.5; color: var(--lp-text); margin: 14px 0 0;
    border: 1px solid var(--lp-frame); border-radius: 6px; padding: 8px 10px;
  }
  #transferUndo { margin-top: 10px; }
  .macroActions { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 14px; }
  /* Delete is the one destructive verb on these screens, coloured like
     the below-minimum timing warning rather than like an ordinary verb. */
  #macroDelete { color: #d13b2e; }

  #macroList { max-height: 34vh; overflow-y: auto; }
  .macroRow {
    display: flex; align-items: center; justify-content: space-between; gap: 10px;
    width: 100%; font: inherit; font-size: 13px; text-align: left; background: transparent;
    color: var(--lp-text); border: none; border-bottom: 1px solid var(--lp-dim);
    padding: 9px 2px; cursor: pointer;
  }
  .macroRowText { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
  .macroRowLabel { white-space: pre-line; }
  .macroRowMeta { font-size: 11px; color: var(--lp-dim); }
  /* Marked degraded in the picker BEFORE it is placed on a button, the
     same rule .actionRow.unbound already follows for an action - a
     commander must not have to place a button to find out it will not
     work. The state itself comes from MacroKnowledgeBuilder via
     GET /api/macros, never a second opinion computed here. */
  .macroRow.degraded .macroRowLabel { color: var(--lp-dim); }
  .macroRow.degraded .macroRowMeta { color: #d13b2e; }

  #macroStepList { margin-bottom: 10px; }
  .stepRow {
    display: flex; align-items: flex-start; gap: 8px;
    border-bottom: 1px solid var(--lp-dim); padding: 9px 2px;
  }
  .stepRowMain {
    flex: 1 1 auto; min-width: 0; font: inherit; font-size: 13px; text-align: left;
    background: transparent; color: var(--lp-text); border: none; padding: 0; cursor: pointer;
    display: flex; flex-direction: column; gap: 3px;
  }
  .stepRowText { line-height: 1.4; }
  .stepRowNote { font-size: 11px; color: var(--lp-dim); line-height: 1.4; }
  /* An unbound action inside a step, said in the same sentence
     PressEndpoint refuses a slot with - never a third wording. */
  .stepRowNote.bad { color: #d13b2e; }
  .stepRowButtons { display: flex; gap: 4px; flex: 0 0 auto; }
  .stepRowButtons button {
    font: inherit; font-size: 12px; background: transparent; color: var(--lp-text);
    border: 1px solid var(--lp-frame); border-radius: 6px; width: 26px; height: 26px;
    padding: 0; cursor: pointer;
  }
  .stepRowButtons button:disabled { opacity: .35; cursor: default; }

  /* "A duration estimate and a plain read-back of what a macro will do
     are worth more here than a warning nobody reads" - the brief. Shown
     always, never behind a tap, and computed from the CONFIGURED timing
     read back from GET /api/macro-timing, never a constant. */
  .macroEstimate { font-size: 12px; color: var(--lp-dim); margin: 4px 0 0; line-height: 1.5; }
  /* Notes, not refusals. Nothing on these screens blocks a save
     (.claude-memory/never-gate-a-macro.md) - this is where a symptom is
     named and then the commander decides. */
  .macroNote { font-size: 12px; color: var(--lp-text); margin: 8px 0 0; line-height: 1.5; }

  .macroField { margin: 12px 0; }
  .macroFieldHead { display: flex; align-items: center; gap: 8px; margin-bottom: 6px; }
  .macroFieldHead > span { font-size: 12px; letter-spacing: .06em; color: var(--lp-dim); }
  /* The tap-to-open "?" - identical to #mergeExpandHelp/#holdMsHelp
     rather than a second visual language for the same idea. */
  .helpBtn {
    font: inherit; font-size: 12px; background: transparent; color: var(--lp-text);
    border: 1px solid var(--lp-frame); border-radius: 50%; width: 22px; height: 22px;
    padding: 0; cursor: pointer; flex: 0 0 auto;
  }
  .macroField input[type="text"], .macroField input[type="number"], #macroNameInput {
    font: inherit; font-size: 14px; background: transparent; color: var(--lp-text);
    border: 1px solid var(--lp-frame); border-radius: 6px; padding: 8px 10px;
  }
  .macroField input[type="number"] { width: 90px; }
  .macroField input[type="text"], #macroNameInput { width: 100%; }
  .macroPickBtn {
    display: block; width: 100%; font: inherit; font-size: 13px; text-align: left;
    background: transparent; color: var(--lp-text); border: 1px solid var(--lp-frame);
    border-radius: 6px; padding: 9px 12px; cursor: pointer;
  }
  .macroTokenChips { display: flex; flex-wrap: wrap; gap: 6px; margin-bottom: 8px; }
  .macroChip {
    font: inherit; font-size: 12px; background: transparent; color: var(--lp-text);
    border: 1px solid var(--lp-frame); border-radius: 12px; padding: 5px 10px; cursor: pointer;
  }
  .macroChip .chipToken { color: var(--lp-dim); font-size: 10px; margin-left: 6px; }

  #stepKindList, #tokenPickerList { max-height: 38vh; overflow-y: auto; }
  #tokenPickerSearch, #keyCaptureBox {
    display: block; width: 100%; font: inherit; font-size: 13px; background: transparent;
    color: var(--lp-text); border: 1px solid var(--lp-frame); border-radius: 6px;
    padding: 8px 12px; margin-bottom: 10px;
  }
  /* #keyCaptureBox is a div standing in for a text input (ref/docs/macros.md's
     real-keypress-capture picker) - it needs a focus ring so a commander can
     tell it is the thing about to receive their keypress. */
  #keyCaptureBox:focus { outline: 2px solid var(--lp-frame); outline-offset: 1px; }
  /* "Press a key"'s result area (ref/docs/macros.md) - its buttons share
     .macroPickBtn's look, laid out with a small gap rather than the default
     block stacking's zero gap. */
  #keyCaptureButtons { display: flex; flex-direction: column; gap: 8px; }
  /* One row per offered token. The plain name is what a commander reads
     first; the sentence explains when it is true; the raw token is
     present but last and dim - available, never the first thing anyone
     sees (ref/docs/macro-builder.md's "How the vocabulary picker glosses
     a flag", answered 2026-09-09). */
  .tokenRow, .stepKindRow {
    display: flex; flex-direction: column; gap: 3px; width: 100%; font: inherit;
    font-size: 13px; text-align: left; background: transparent; color: var(--lp-text);
    border: none; border-bottom: 1px solid var(--lp-dim); padding: 10px 2px; cursor: pointer;
  }
  .tokenRowLabel, .stepKindLabel { line-height: 1.3; }
  .tokenRowMeaning, .stepKindDescription { font-size: 11px; color: var(--lp-dim); line-height: 1.45; }
  .tokenRowToken { font-size: 10px; color: var(--lp-dim); letter-spacing: .04em; }
  /* Low-confidence flags are offered like any other and MARKED, never
     withheld - withholding them would be the never-gate rule broken in
     the picker instead of the runner (ref/docs/macro-builder.md). */
  .tokenRowConfidence { font-size: 10px; color: #d13b2e; }
  .stepKindWarning { font-size: 11px; color: #d13b2e; line-height: 1.45; }
  .tokenRow.selected { border-left: 3px solid var(--lp-lit); padding-left: 8px; }
  .tokenNotRow { display: flex; align-items: center; gap: 8px; font-size: 12px; color: var(--lp-dim); margin-bottom: 10px; }
</style>
<style id="theme-vars"></style>
</head>
<body>

<!-- Editing a specific paired device's real layout live, from the PC. Fixed
     overlay rather than part of the normal document flow, deliberately: it
     must never change #chrome's own height, which the fit-loop math already
     treats as an exact allowance from the server (this file's own remarks on
     LC9, above) - adding a row above it would misreport that allowance a
     third time under a new name. Hidden unless EDITING_DEVICE_ID is set. -->
<div id="editingDeviceBanner" class="hidden">
  <span id="editingDeviceBannerText"></span>
  <button id="editingDeviceBannerStop" type="button">Stop editing</button>
</div>

<div id="loadingScreen" class="screen">Loading&hellip;</div>

<div id="pairScreen" class="screen hidden">
  <form id="pairBox">
    <h1>PAIR THIS DEVICE</h1>
    <!-- Says where the code actually is. There has been no console since
         the tray gained its "Add a device" window, and this is the first
         screen a new commander reads, so pointing them at something that
         does not exist is the worst possible place to be wrong. Corrected
         2026-09-08. The code is shown grouped as "123 456"; typing or
         pasting the space is fine (PairEndpoint.NormalizeCode). -->
    <p>Enter the pairing code from the LunaPanel tray icon &rarr; Add a device.</p>
    <input id="pairCode" type="text" inputmode="text" autocomplete="off" autocapitalize="characters" placeholder="CODE" required>
    <!-- What this device is called (ref/docs/layout-import.md). Optional:
         left empty, the server names it after its class plus the next free
         ordinal - "Phone", then "Phone 2" - which it can do and this page
         cannot, since the device list is behind device auth and this device
         has no cookie yet. The placeholder shows what that default will be
         so an ignored field is still a known outcome rather than a
         surprise. A name is display data, never identity: two devices may
         share one. -->
    <input id="pairName" type="text" inputmode="text" autocomplete="off" placeholder="Phone" maxlength="__DEVICE_NAME_MAX__">
    <p class="pairHint">Name this device (optional).</p>
    <button id="pairSubmit" type="submit">Pair</button>
    <div id="pairError"></div>
  </form>
</div>

<div id="panelScreen" class="screen hidden">
  <div id="chrome">
    <span class="title">LUNAPANEL</span>
    <div class="chromeButtons">
      <button id="editBtn" type="button">Edit</button>
      <button id="gearBtn" type="button" aria-label="Settings">&#9881;&#xFE0E;</button>
      <button id="fsBtn" type="button">Full screen</button>
    </div>
  </div>
  <div id="tabs"></div>
  <div id="stage">
    <div id="grid"></div>
  </div>
</div>

<!-- The settings gear's sheet. Built as a shell with room for more panes -
     device management lands here next dispatch, not built yet - so
     .sheetTabs already exists even though "Colour" is the only tab today. -->
<div id="settingsSheet" class="sheet hidden">
  <div class="sheetPanel">
    <div class="sheetHeader">
      <span>SETTINGS</span>
      <button id="settingsClose" type="button">Close</button>
    </div>
    <div class="sheetTabs">
      <button class="sheetTab active" type="button" data-pane="paneColour">Colour</button>
      <button class="sheetTab" type="button" data-pane="panePanels">Panels</button>
      <button class="sheetTab" type="button" data-pane="paneBindings">Controls</button>
      <button class="sheetTab" type="button" data-pane="paneMacros">Macros</button>
      <button class="sheetTab" type="button" data-pane="paneTiming">Timing</button>
      <button class="sheetTab" type="button" data-pane="paneDevices">Devices</button>
      <!-- Hidden outright on anything that is not the PC, unlike the Macros
           tab beside it (ref/docs/transfer.md). The Macros pane still has
           something for a tablet to do - list macros, put one on a button;
           this one has nothing at all, since every route behind it is
           host-only in both directions. A tab that could only ever say
           "not here" is worse than no tab. The enforcement is still the
           server's: HostOnlyRoutes refuses these routes to a device
           whatever this page chose to draw. -->
      <button id="tabTransfer" class="sheetTab" type="button" data-pane="paneTransfer">Import/export</button>
    </div>
    <div id="paneColour" class="sheetPane">
      <p id="colourSource">Current source: &hellip;</p>
      <!-- Four rows, always visible, whether the theme is currently
           resolved automatically or from a stored override - the override
           itself only ever exists once a swatch is actually picked (see
           onSwatchPick below and ref/docs/button-naming.md's override
           principle), never from a checkbox alone. -->
      <div class="roleRow">
        <span>Border</span>
        <button type="button" class="swatch roleSwatch" data-role="border" aria-label="Change Border colour"></button>
      </div>
      <div class="roleRow">
        <span>Text</span>
        <button type="button" class="swatch roleSwatch" data-role="text" aria-label="Change Text colour"></button>
      </div>
      <div class="roleRow">
        <span>Lit</span>
        <button type="button" class="swatch roleSwatch" data-role="lit" aria-label="Change Lit colour"></button>
      </div>
      <!-- The fourth role (2026-09-10, ref/docs/theme.md). Before this the
           panel background was always a near-black tint of Text, resolved
           and never offered; it is the same value until the commander picks
           something here. Deliberately NOT included in "one colour for
           everything" below - one colour for the background as well as the
           foreground is a solid rectangle, not a theme. -->
      <div class="roleRow">
        <span>Background</span>
        <button type="button" class="swatch roleSwatch" data-role="background" aria-label="Change Background colour"></button>
      </div>
      <div class="roleRow">
        <span>One colour for everything</span>
        <button type="button" class="swatch roleSwatch" data-role="all" aria-label="Set one colour for everything"></button>
      </div>
      <!-- Advisory only, and never a refusal: the colour is already saved
           and already showing by the time this appears. It exists because a
           role close enough to the background is simply invisible, which
           reads as a broken panel rather than as a colour choice. -->
      <p id="colourWarning" class="hidden"></p>
      <button id="resetTheme" type="button">Reset to automatic</button>
    </div>
    <!-- The template picker, moved off the front panel surface
         (ref/docs/panels-and-pages.md). #rungLadder is populated from
         GET /api/templates?w=&h= - the same real CellSizeEstimator the
         calibration page already reads, never a second hand-typed ladder. -->
    <div id="panePanels" class="sheetPane hidden">
      <p class="pickerGroupLabel">Panel size</p>
      <div id="rungLadder" class="rungLadder"></div>
      <div class="roleRow">
        <span>Merging and expanding panels</span>
        <div class="mergeExpandRow">
          <input type="checkbox" id="mergeExpandToggle" checked>
          <button id="mergeExpandHelp" type="button" aria-label="What does this do?">?</button>
        </div>
      </div>
      <div class="roleRow">
        <span>Follow me between ship, SRV, and on foot</span>
        <div class="mergeExpandRow">
          <input type="checkbox" id="autoSwitchEnabledToggle" checked>
          <button id="autoSwitchEnabledHelp" type="button" aria-label="What does this do?">?</button>
        </div>
      </div>
      <!-- Reset to default (ref/docs/reset-to-default.md): the whole
           device, every page, back to the shipped starter. Same warn-then-
           write shape as the import sheet's own overwrite confirm, but with
           no second modal afterwards - see that doc for why. -->
      <button id="resetToDefault" type="button">Reset this device's buttons to default&hellip;</button>
    </div>
    <!-- Bindings pane (ref/docs/bindings-source.md) - reports which preset
         is actually in effect (name, whose file it is, version, how many
         actions resolved to a real key) so a wrong or empty pick announces
         itself instead of presenting a silently dead panel. -->
    <div id="paneBindings" class="sheetPane hidden">
      <div class="bindingsRow">
        <span id="bindingsStatus">Current source: &hellip;</span>
        <button id="refreshBindings" type="button">Refresh</button>
      </div>
    </div>
    <!-- Macros pane (ref/docs/macro-builder.md) - the builder's front door.
         The intro paragraph is not decoration: the commander asked for
         "basic instructions" on the screen itself, and this is the first
         place anybody meets the word "macro" in this product. -->
    <div id="paneMacros" class="sheetPane hidden">
      <p class="paneIntro">A macro is a short list of things LunaPanel presses for you, one after another - open a panel, step down three rows, select. Build one here, then put it on a button with Edit, on the panel itself.</p>
      <!-- Shown instead of the build button on anything that is not the PC
           (ref/docs/macro-builder.md's "Authoring moved to the PC"). Not a
           refusal to explain later: a commander who taps Macros on a tablet
           needs to be told where the builder went, in the place they went
           looking for it. -->
      <p id="macroAuthorOnPc" class="paneIntro hidden">Macros are built on the PC running LunaPanel - open the LunaPanel tray icon there and choose "Build a macro". Anything built there shows up in this list, and you can put it on a button from here.</p>
      <div class="roleRow">
        <span>Show what each step did after a macro runs</span>
        <div class="mergeExpandRow">
          <input type="checkbox" id="showMacroStepResultsToggle">
          <button id="showMacroStepResultsHelp" type="button" aria-label="What does this do?">?</button>
        </div>
      </div>
      <button id="macroNew" type="button">Build a new macro&hellip;</button>
      <div id="macroList"></div>
    </div>
    <!-- Timing pane (ref/docs/macro-timing.md): how fast LunaPanel drives
         the keyboard for macros. Server-wide - GET/POST __API_MACRO_TIMING__
         has no device id at all, unlike every other pane on this sheet -
         because input pacing is a property of this machine and this copy of
         Elite, not a commander's own arrangement. Tap-to-open "?" for each
         field (never hover - these are touch devices, same discipline as
         #mergeExpandHelp); the warning paragraphs below each field are
         shown automatically, not tap-triggered - see checkTimingWarnings(). -->
    <div id="paneTiming" class="sheetPane hidden">
      <div class="roleRow">
        <span>Key hold</span>
        <div class="timingRow">
          <input type="number" id="holdMsInput" min="1" step="1" inputmode="numeric">
          <span>ms</span>
          <button id="holdMsHelp" type="button" aria-label="What does this do?">?</button>
        </div>
      </div>
      <p id="holdMsWarning" class="timingWarning hidden"></p>
      <div class="roleRow">
        <span>Gap between presses</span>
        <div class="timingRow">
          <input type="number" id="gapMsInput" min="1" step="1" inputmode="numeric">
          <span>ms</span>
          <button id="gapMsHelp" type="button" aria-label="What does this do?">?</button>
        </div>
      </div>
      <p id="gapMsWarning" class="timingWarning hidden"></p>
    </div>
    <!-- Devices pane (ref/docs/pairing-and-devices.md): every paired
         device, "Add another device" (opens pairing and shows the fresh
         code right here - the route that gets used, since walking to the
         PC to read a number off the console is the friction this whole
         project exists to remove), and a Forget control on every row
         except this device's own. -->
    <div id="paneDevices" class="sheetPane hidden">
      <button id="addAnotherDevice" type="button">Add another device</button>
      <!-- The same list and the same copy the end of a successful pair
           offers, reachable at any time (ref/docs/layout-import.md). Hidden
           entirely when nothing is on offer, rather than shown disabled:
           an empty chooser is the one thing the spec says not to put in
           front of anyone. -->
      <button id="importLayouts" type="button" class="hidden">Import buttons from another device&hellip;</button>
      <p id="devicePairingCode" class="hidden"></p>
      <div id="deviceList"></div>
    </div>
    <!-- Import/export pane (ref/docs/transfer.md). Two halves, because the
         commander asked for two things - profiles and macros - and one file
         format, because a profile carries the macros its buttons name.
         Everything here is host-only; the tab above is not drawn anywhere
         else. -->
    <div id="paneTransfer" class="sheetPane hidden">
      <p class="paneIntro">Save what you have set up to a file, or bring in a file somebody sent you. A profile is one device's whole arrangement - every page, every button, its name and its long-press - and it carries the macros those buttons use. Macros can also be sent on their own.</p>
      <p class="pickerGroupLabel">Profile</p>
      <div class="roleRow">
        <span>Save</span>
        <div class="transferRow">
          <select id="transferExportDevice" aria-label="Which device's buttons to save"></select>
          <button id="transferExportProfile" type="button">To a file</button>
        </div>
      </div>
      <div class="roleRow">
        <span>Load</span>
        <div class="transferRow">
          <select id="transferImportDevice" aria-label="Which device to put the buttons on"></select>
          <button id="transferImportProfile" type="button">From a file&hellip;</button>
        </div>
      </div>
      <!-- Shown only after a profile import actually happened, and it is
           LayoutStore's own single kept generation being put back - not a
           second history mechanism (ref/docs/layout-import.md). -->
      <button id="transferUndo" type="button" class="hidden">Undo that import</button>
      <p class="pickerGroupLabel">Macros</p>
      <div class="roleRow">
        <span>Save</span>
        <div class="transferRow">
          <select id="transferExportMacro" aria-label="Which macro to save"></select>
          <button id="transferExportMacros" type="button">To a file</button>
        </div>
      </div>
      <div class="roleRow">
        <span>Load</span>
        <div class="transferRow">
          <button id="transferImportMacros" type="button">From a file&hellip;</button>
        </div>
      </div>
      <p id="transferResult" class="hidden"></p>
      <!-- One file input for both halves, opened by whichever button was
           tapped (transferImportKind). A second input would be a second
           place to forget to clear .value, which is what makes choosing the
           same file twice in a row silently do nothing. -->
      <input type="file" id="transferFile" accept=".json,application/json" class="hidden">
    </div>
  </div>
</div>

<!-- The layout import / recovery chooser (ref/docs/layout-import.md). ONE
     sheet, opened from two places: the Devices pane at any time, and the end
     of a successful pair when a layout no live device owns exists and none
     was adopted automatically. "Start fresh" is an equal option here, not a
     dismissal - a commander who wants nothing carried over is making a
     choice, not declining one. -->
<div id="importSheet" class="sheet hidden">
  <div class="sheetPanel">
    <div class="sheetHeader">
      <span>BUTTONS FROM ANOTHER DEVICE</span>
      <button id="importClose" type="button">Close</button>
    </div>
    <p id="importIntro"></p>
    <div id="importList"></div>
    <button id="importStartFresh" type="button">Start fresh</button>
  </div>
</div>

<!-- Opened by tapping any role swatch above. Two labelled groups: the
     commander's own EDHM colours (absent/empty with no EDHM - the pane
     still works) and the built-in standard palette (always present, the
     escape hatch for a broken or undiscoverable install). -->
<div id="colourPicker" class="sheet hidden">
  <div class="sheetPanel">
    <div class="sheetHeader">
      <span>PICK A COLOUR</span>
      <button id="colourPickerClose" type="button">Close</button>
    </div>
    <div id="pickerHudGroup" class="pickerGroup hidden">
      <p class="pickerGroupLabel">From your HUD</p>
      <div class="swatchPicker" id="pickerHudSwatches"></div>
    </div>
    <div class="pickerGroup">
      <p class="pickerGroupLabel" id="pickerStandardLabel">Standard colours</p>
      <div class="swatchPicker" id="pickerStandardSwatches"></div>
    </div>
  </div>
</div>

<!-- The on-device editor (ref/docs/editor.md). Opened by tapping any slot
     (occupied or empty) while Edit mode is on. Rename/Clear/long-press only
     make sense for an already-occupied slot, so all four start hidden and
     are only shown when opening a slot that actually names something - see
     openSlotSheet(). Long-press has no name of its own (button-naming.md's
     own "Undecided" - not yet answered), so its own action picker trip
     skips the rename dialog entirely (see onActionPicked's pickerMode
     branch) - only the primary slot can be labelled. -->
<div id="slotSheet" class="sheet hidden">
  <div class="sheetPanel">
    <div class="sheetHeader">
      <span id="slotSheetTitle">SLOT</span>
      <button id="slotSheetClose" type="button">Close</button>
    </div>
    <p id="slotSheetStatus"></p>
    <button id="slotSheetAssign" type="button" class="editAction">Change action&hellip;</button>
    <button id="slotSheetRename" type="button" class="editAction hidden">Rename</button>
    <button id="slotSheetLatch" type="button" class="editAction hidden">Hold until tapped again</button>
    <button id="slotSheetHold" type="button" class="editAction hidden">Hold while pressed</button>
    <button id="slotSheetEditMacro" type="button" class="editAction hidden">Open this macro&hellip;</button>
    <button id="slotSheetLongPress" type="button" class="editAction hidden">Set long-press action&hellip;</button>
    <button id="slotSheetClearLongPress" type="button" class="editAction hidden">Clear long-press action</button>
    <button id="slotSheetClear" type="button" class="editAction hidden">Clear this button</button>
    <!-- [2026-09-16] Folders (ref/docs/layouts.md). Three controls, never
         more than two of them visible at once: MakeFolder is the empty
         slot's third choice beside "Choose an action..."; Open and Delete
         belong to a slot that already IS a folder. Delete is deliberately a
         separate control from "Clear this button" directly above it, and
         the separation is the whole point - Clear leaves the folder's page
         and every button on it intact and merely unreferenced, and only
         this one destroys anything. -->
    <button id="slotSheetMakeFolder" type="button" class="editAction hidden">Make this a folder&hellip;</button>
    <button id="slotSheetOpenFolder" type="button" class="editAction hidden">Open this folder</button>
    <button id="slotSheetDeleteFolder" type="button" class="editAction hidden">Delete this folder and everything in it&hellip;</button>
  </div>
</div>

<!-- Search-first (ref/docs/editor.md: 423 bindable elements on the
     reference machine, roughly doubling with "show everything" - scrolling
     alone is not enough). Default view: curated actions plus anything
     actually bound (GET __API_ACTIONS__ with no query). "Show everything"
     (?all=true) additionally lists every bindable element the game exposes,
     unbound ones included and visibly marked (.actionRow.unbound) BEFORE
     assignment, per the commander's own instruction. -->
<div id="actionPicker" class="sheet hidden">
  <div class="sheetPanel">
    <div class="sheetHeader">
      <span>CHOOSE AN ACTION</span>
      <button id="actionPickerClose" type="button">Close</button>
    </div>
    <input id="actionPickerSearch" type="text" placeholder="Search actions&hellip;" autocomplete="off">
    <label class="roleRow">
      <span>Show everything, even unmapped</span>
      <input type="checkbox" id="actionPickerShowAll">
    </label>
    <div id="actionPickerList"></div>
  </div>
</div>

<!-- The rename prompt (ref/docs/button-naming.md), reached two ways: right
     after picking an action (pre-filled with that action's own default
     label), or standalone from the slot sheet's own Rename (pre-filled with
     the slot's current label). Cancel abandons whatever opened it - the
     whole assignment in the first case, just the rename in the second -
     which falls out for free here since nothing is ever sent to the server
     until OK (see renameCommitHandler below). A <textarea>, not an <input>:
     labels are up to two lines and the commander picks the break with
     Enter, which a single-line input cannot hold and would submit on. -->
<div id="renameDialog" class="sheet hidden">
  <div class="sheetPanel">
    <div class="sheetHeader"><span>NAME THIS BUTTON</span></div>
    <form id="renameForm">
      <textarea id="renameInput" rows="2" autocomplete="off"></textarea>
      <div class="renameButtons">
        <button id="renameCancel" type="button">Cancel</button>
        <button type="submit">OK</button>
      </div>
    </form>
  </div>
</div>

<!-- The page bar's own "name a page" prompt (ref/docs/panels-and-pages.md).
     A lightweight PARALLEL to #renameDialog above, rather than reusing it:
     #renameDialog's own submit handler runs the slot label's line-wrap
     budget (LABEL_MAX_LINES/LABEL_LINE_CAP) unconditionally, which is a
     button-cell constraint that has no reason to apply to a page's own
     name - a page name is drawn in the tab row, not squeezed into a fixed
     grid cell. Same visual sheet-with-textarea-and-Cancel/OK shape either
     way, and the same "nothing is sent until OK" discipline via
     pageNameCommitHandler, mirroring renameCommitHandler. Reached from two
     places: the "+" add-page flow (see openAddPageFlow), and the page
     settings sheet's own Rename control (see openPageSettingsSheet) - each
     supplies its own title text and its own commit handler. -->
<div id="pageNameDialog" class="sheet hidden">
  <div class="sheetPanel">
    <div class="sheetHeader"><span id="pageNameDialogTitle">NAME THIS PAGE</span></div>
    <form id="pageNameForm">
      <textarea id="pageNameInput" rows="1" autocomplete="off"></textarea>
      <div class="renameButtons">
        <button id="pageNameCancel" type="button">Cancel</button>
        <button type="submit">OK</button>
      </div>
    </form>
  </div>
</div>

<!-- The "+" flow's second step: pick the new page's own panel size
     (ref/docs/panels-and-pages.md). Deliberately a SECOND #rungLadder
     instance rather than reusing the settings gear's #rungLadder in place -
     that one is wired to onRungTap, which always targets currentPage and
     POSTs __API_PANEL_TEMPLATE__ (an existing page's RESHAPE); this one has
     no page to reshape yet, so it needs its own click wiring
     (onPageTemplateRungTap) that POSTs __API_PAGE_ADD__ instead. Same
     #rungLadder/.rung markup and CSS either way, populated from the same
     GET __API_TEMPLATES__ - never a second, hand-typed ladder. -->
<div id="pageTemplatePicker" class="sheet hidden">
  <div class="sheetPanel">
    <div class="sheetHeader">
      <span>PICK A PANEL SIZE</span>
      <button id="pageTemplatePickerClose" type="button">Close</button>
    </div>
    <div id="pageTemplateRungLadder" class="rungLadder"></div>
  </div>
</div>

<!-- Page settings (ref/docs/panels-and-pages.md): reached by a long-press
     on any tab in the page bar (wireTabGesture, the same 500ms-hold
     convention wireSlotGesture already uses for a slot's own long-press),
     for the page whose tab was held - not necessarily currentPage, so a
     commander can configure a page without first switching to it. Rename
     reuses #pageNameDialog above; the visibility picker is a CLOSED
     vocabulary of buttons (VISIBILITY_TOKENS), not free text - a typo in a
     showWhen token would otherwise only ever be caught by a reject-and-
     retry round trip against __API_PAGE_SHOWWHEN__, or worse, silently
     never shown by ContextPageSelector.

     [2026-09-15] Delete this page (ref/docs/panels-and-pages.md): reverses
     this file's own earlier "No delete-page control - deliberately out of
     scope" decision, on explicit request. Hidden/disabled (in
     openPageSettingsSheet) whenever lastPanelData.pageNames.length <= 1 -
     the count is known regardless of which page is currently showing, and
     this only avoids a round trip the server would refuse anyway; the
     authoritative floor is PageEditEndpoint.DeletePage's own
     CannotDeleteLastPage check. -->
<div id="pageSettingsSheet" class="sheet hidden">
  <div class="sheetPanel">
    <div class="sheetHeader">
      <span>PAGE SETTINGS</span>
      <button id="pageSettingsClose" type="button">Close</button>
    </div>
    <button id="pageSettingsRename" type="button" class="editAction">Rename this page&hellip;</button>
    <p class="pickerGroupLabel">Show automatically when</p>
    <p class="paneIntro">Turn on what this page is for. Leave everything off to keep it always available by hand, never switched to automatically.</p>
    <div id="pageVisibilityPicker" class="rungLadder"></div>
    <button id="pageSettingsDelete" type="button" class="editAction dangerAction">Delete this page&hellip;</button>
  </div>
</div>

<!-- ===================================================================
     The macro builder's four screens (ref/docs/macro-builder.md).

     #macroBuilder    one macro: its name, its steps in order, what it
                      will do and roughly how long it will take.
     #stepKindPicker  which kind of step to add - ONE list, everything
                      offered, nothing behind an "advanced" tier (that
                      page's question 1, ruled). Friendliness is bought
                      by explaining the powerful steps, not by hiding
                      them: every row carries a sentence, and pressUntil
                      carries its measured warning.
     #stepEditor      that step's own fields, each with a "?" beside it.
     #tokenPicker     a status flag, a GuiFocus value or a journal event,
                      shown by its plain name with the raw token
                      underneath - so "wait for the game" is usable
                      without knowing what GuiFocus:NoFocus means.

     Every list here is populated from GET __API_MACROS_VOCABULARY__ -
     the real StatusVocabulary/JournalVocabulary tables. Nothing in this
     page restates a token, because a restated token is one that can
     drift out of agreement with the parser that has to accept it.
     =================================================================== -->
<div id="macroBuilder" class="sheet hidden">
  <div class="sheetPanel">
    <div class="sheetHeader">
      <span id="macroBuilderTitle">MACRO</span>
      <button id="macroBuilderClose" type="button">Close</button>
    </div>
    <p id="macroBuilderIntro" class="paneIntro"></p>
    <div class="macroField">
      <div class="macroFieldHead">
        <span>Name</span>
        <button id="macroNameHelp" type="button" class="helpBtn" aria-label="What does this do?">?</button>
      </div>
      <input id="macroNameInput" type="text" autocomplete="off" placeholder="What the button will say">
    </div>
    <div class="macroFieldHead">
      <span>Steps, in the order they happen</span>
      <button id="macroStepsHelp" type="button" class="helpBtn" aria-label="What does this do?">?</button>
    </div>
    <div id="macroStepList"></div>
    <button id="macroAddStep" type="button" class="macroPickBtn">Add a step&hellip;</button>
    <p id="macroEstimate" class="macroEstimate"></p>
    <div id="macroNotes"></div>
    <div class="macroActions">
      <button id="macroSave" type="button">Save</button>
      <button id="macroCopy" type="button" class="hidden">Make a copy I can edit</button>
      <button id="macroDelete" type="button" class="hidden">Delete&hellip;</button>
    </div>
  </div>
</div>

<div id="stepKindPicker" class="sheet hidden">
  <div class="sheetPanel">
    <div class="sheetHeader">
      <span>ADD A STEP</span>
      <button id="stepKindPickerClose" type="button">Close</button>
    </div>
    <p class="paneIntro">Steps happen in order, top to bottom. Most macros are just presses and waits; the ones further down wait for the game itself to be ready before carrying on.</p>
    <div id="stepKindList"></div>
  </div>
</div>

<!-- "Press a key" (ref/docs/macros.md): a single-screen real-keypress-
     capture flow, replacing the old search-a-list-then-view-a-result-sheet
     pair. #keyCaptureBox is a focusable, non-typing target - its keydown
     handler captures the physical key directly rather than filtering a
     list by typed text. #keyCaptureResult is rebuilt each time a key is
     captured, since the four outcomes (matched / no match / couldn't check
     / unrecognized) need different wording and different buttons rather
     than one fixed template with parts hidden and shown - the same reason
     the old onKeyPicked rebuilt #keyMatchSheet's contents each time, which
     this folds into this one sheet instead of a second, separate one. -->
<div id="keyPicker" class="sheet hidden">
  <div class="sheetPanel">
    <div class="sheetHeader">
      <span>PRESS A KEY</span>
      <button id="keyPickerClose" type="button">Close</button>
    </div>
    <p class="paneIntro">Press the physical key you want this step to use. If Elite already has a control bound to it, LunaPanel offers to use that control instead - so a later rebind in the game keeps this step working.</p>
    <div id="keyCaptureBox" tabindex="0">Press a key&hellip;</div>
    <div id="keyCaptureResult" class="hidden">
      <p id="keyCaptureText" class="paneIntro"></p>
      <div id="keyCaptureButtons"></div>
    </div>
  </div>
</div>

<div id="stepEditor" class="sheet hidden">
  <div class="sheetPanel">
    <div class="sheetHeader">
      <span id="stepEditorTitle">STEP</span>
      <button id="stepEditorClose" type="button">Close</button>
    </div>
    <p id="stepEditorAbout" class="paneIntro"></p>
    <p id="stepEditorWarning" class="timingWarning hidden"></p>
    <div id="stepEditorFields"></div>
    <div class="macroActions">
      <button id="stepEditorDone" type="button">Done</button>
    </div>
  </div>
</div>

<!-- One branch arm's own step list, opened from #stepEditor's branch fields
     (renderStepEditor's kind === 'branch' block). Nesting is capped at one
     level (the engine's own load-time rule - no branch inside an arm), so a
     single sheet, reused for whichever of Then/Else is currently open, is
     enough - there is never a second one stacked on top of this one.
     #armStepList reuses renderStepList's own row markup, parameterized by a
     { list, isTopLevel: false } context pointing at the arm's own array
     (step.then or step['else']) rather than macroDraft.steps; opening a step
     from here pushes the exact same #stepEditor sheet the top level uses. -->
<div id="armEditor" class="sheet hidden">
  <div class="sheetPanel">
    <div class="sheetHeader">
      <span id="armEditorTitle">STEPS</span>
      <button id="armEditorClose" type="button">Close</button>
    </div>
    <p class="paneIntro">These steps only run when this side of the branch is the one taken.</p>
    <div id="armStepList"></div>
    <button id="armAddStep" type="button" class="macroPickBtn">Add a step&hellip;</button>
  </div>
</div>

<div id="tokenPicker" class="sheet hidden">
  <div class="sheetPanel">
    <div class="sheetHeader">
      <span id="tokenPickerTitle">CHOOSE</span>
      <button id="tokenPickerClose" type="button">Close</button>
    </div>
    <p id="tokenPickerAbout" class="paneIntro"></p>
    <label class="tokenNotRow" id="tokenPickerNotRow">
      <input type="checkbox" id="tokenPickerNot">
      <span>Match when this is <b>not</b> true</span>
    </label>
    <input id="tokenPickerSearch" type="text" placeholder="Search&hellip;" autocomplete="off">
    <div id="tokenPickerList"></div>
  </div>
</div>

<!-- ===================================================================
     The per-step run read-back (ref/docs/macro-builder.md's "What the
     builder shows after a run") - shown after ANY macro fires from a real
     slot press (onSlotTap/onSlotLongPress), not just from inside the
     builder: a macro's own button is the only place a macro is ever
     actually run. Reuses the builder's own .stepRow/.stepRowText/
     .stepRowNote row style (renderMacroDraft above) rather than inventing a
     second look for a step list. Only opened when the press response
     carries a non-empty steps array - a plain action's press response
     never has that field at all.
     =================================================================== -->
<div id="macroRunResult" class="sheet hidden">
  <div class="sheetPanel">
    <div class="sheetHeader">
      <span>MACRO RUN</span>
      <button id="macroRunResultClose" type="button">Close</button>
    </div>
    <p id="macroRunResultSummary" class="paneIntro"></p>
    <div id="macroRunResultList"></div>
  </div>
</div>

<div id="toast"></div>

<script>
'use strict';

// The gutter is the one allowance still duplicated in JavaScript - see this
// file's own remarks above. headerStrip/framePadding/tabStripHeight are read
// straight from GET /api/panel's response instead (data.headerStrip,
// data.framePadding, data.tabStripHeight below) - no client-side constant
// for any of the three.
const PHONE_MAX = __PHONE_TABLET_BOUNDARY__;

// Whether this page may build, copy or delete a macro - substituted by the
// server from where the request arrived (ref/docs/hosting.md), never asked
// for by the page and never sent back to be trusted. False on every device;
// true only on the PC LunaPanel is running on, reached through the tray's
// "Build a macro". A tablet still lists macros, still puts one on a button
// and still fires it - it just cannot author one, because the keyboard it
// would need covers the very field it has to type into
// (ref/docs/macro-builder.md).
const CAN_AUTHOR_MACROS = __CAN_AUTHOR_MACROS__;

// Whether this page is the PC LunaPanel is running on, for the import/export
// pane (ref/docs/transfer.md). Substituted from the same server-side fact
// CAN_AUTHOR_MACROS is, and spelled separately on purpose - see BuildPage's
// own remarks. Also not enforcement: every route the pane calls is refused
// to a device by HostOnlyRoutes whatever this page draws.
const IS_HOST = __IS_HOST__;

// Editing a specific paired device's real layout live, from the PC. Read
// straight off this page's own URL - never trusted as identity, exactly
// like CAN_AUTHOR_MACROS/IS_HOST are not: DeviceAuthMiddlewareExtensions is
// what actually decides whose layout a request touches, and refuses this
// parameter outright if it names anything but a real, currently paired
// device (and ignores it completely on anything but a host-listener
// request - see that file's own remarks). This is decoration: which banner
// to show and which button to draw, nothing more.
const ASDEVICE_PARAM = '__ASDEVICE_PARAM__';
const EDITING_DEVICE_ID = new URLSearchParams(location.search).get(ASDEVICE_PARAM);

// Appended to a URL that may or may not already carry a query string.
function asDeviceQuerySuffix(hasQueryAlready) {
  if (!EDITING_DEVICE_ID) return '';
  return (hasQueryAlready ? '&' : '?') + ASDEVICE_PARAM + '=' + encodeURIComponent(EDITING_DEVICE_ID);
}

// Every API call this page makes goes through window.fetch - overriding it
// HERE, once, is what threads asDevice through all of them (slot edits, page
// management, macro assignment, panel load, everything else) without
// touching each of this file's several dozen individual fetch(...) call
// sites. A no-op whenever EDITING_DEVICE_ID is unset, which is every session
// that never visited this page with ?asDevice= in its own URL - the ordinary
// case for every device, and for the PC's own session before "Edit this
// device live" is ever chosen.
const _nativeFetch = window.fetch.bind(window);
window.fetch = (input, init) => {
  if (EDITING_DEVICE_ID && typeof input === 'string') {
    input = input + asDeviceQuerySuffix(input.includes('?'));
  }
  return _nativeFetch(input, init);
};

function isPhone() { return Math.min(innerWidth, innerHeight) < PHONE_MAX; }
function gutterFor() { return isPhone() ? 8 : 12; }

function el(id) { return document.getElementById(id); }

// Every modal .sheet shares position: fixed; inset: 0; z-index: 900, so
// paint order is pure DOM order - a sheet opened from within another one
// that never gets hidden paints its scrim UNDER a sheet earlier in the
// document, no matter which one the commander actually meant to see (the
// macro-step control picker under #macroBuilder was exactly this). Rather
// than a hand-hide of one specific sibling at each call site - which only
// fixes the pair it names - pushSheet/popSheet track a stack of what got
// suppressed so any sheet can nest inside any other and unwind correctly.
// Suppression is tracked in sheetStack itself, never read back off the
// 'hidden' class alone, because that class is also toggled directly by
// plenty of other code - reading it back would risk resurrecting a sheet
// the commander genuinely closed underneath.
const sheetStack = [];
function pushSheet(id) {
  // Re-opening the sheet that is already on top (e.g. Copy re-populating
  // an already-open macro builder) is a re-render in place, not a nest -
  // pushing another frame here would need a second pop to unwind it.
  if (sheetStack.length && sheetStack[sheetStack.length - 1].id === id) return;
  const suppressed = [];
  document.querySelectorAll('.sheet').forEach(sheet => {
    if (sheet.id !== id && !sheet.classList.contains('hidden')) {
      sheet.classList.add('hidden');
      suppressed.push(sheet.id);
    }
  });
  sheetStack.push({ id, suppressed });
  el(id).classList.remove('hidden');
}
function popSheet(id) {
  el(id).classList.add('hidden');
  const top = sheetStack.length && sheetStack[sheetStack.length - 1].id === id
    ? sheetStack.pop()
    : null;
  if (top) top.suppressed.forEach(sid => el(sid).classList.remove('hidden'));
}

// The default name this device will be given if the commander types
// nothing - shown as a PLACEHOLDER rather than as a value, because the
// ordinal half ("Phone 2") can only be decided by the server, which is the
// only side that can see the device list (ref/docs/layout-import.md).
// Pre-filling "Phone" as a real value would send "Phone" verbatim from
// every device whose commander ignored the field, and the disambiguator
// would never fire at all.
function applyPairNamePlaceholder() {
  el('pairName').placeholder = isPhone() ? 'Phone' : 'Tablet';
}

function showScreen(name) {
  ['loadingScreen', 'pairScreen', 'panelScreen'].forEach(id => {
    el(id).classList.toggle('hidden', id !== name);
  });
}

let toastTimer = null;
function showToast(message) {
  const t = el('toast');
  t.textContent = message;
  t.classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => t.classList.remove('show'), 3500);
}

// Called with the latest panel response after every fetch, and with no
// argument from a debounced resize/orientation handler that fires before
// loadPanel()'s own re-fetch completes - the latter re-applies the last
// known allowances rather than guessing new ones.
let lastPanelData = null;
function applyChrome(data) {
  if (data) lastPanelData = data;
  data = data || lastPanelData;
  if (!data) return;
  el('chrome').style.height = data.headerStrip + 'px';
  el('tabs').style.height = data.tabStripHeight + 'px';
}

let currentPage = 0;

function panelUrl() {
  return `__API_PANEL__?w=${Math.round(innerWidth)}&h=${Math.round(innerHeight)}&page=${currentPage}`;
}

// ---------------------------------------------------------------------
// Automatic vessel-context page switching (ref/docs/vessel-context.md).
// The server decides WHETHER to switch and to WHICH page - it pushes
// switchToPage on the live channel, on the one push caused by a context
// change and never as a level. Everything below is about WHEN it is safe to
// apply that here.
//
// IT MUST NEVER SWITCH UNDER A THUMB ALREADY MOVING. These transitions are
// the busiest moments in the game, and if the page changes between a
// commander deciding to press something and their finger arriving, they
// press whatever moved into that spot instead. So a switch that lands while
// any pointer is down is HELD, not dropped, and applied on the release.
//
// The release listener is on `window`, deliberately, so it runs in the
// bubble phase AFTER the pressed button's own `pointerup` handler has
// already fired onSlotTap() and read currentPage. Moving it to the capture
// phase, or onto the button, would let the page change first and send the
// press to the new page's slot of the same index - the exact wrong-control
// press this whole section exists to prevent.
let activePointers = 0;
let pendingPageSwitch = null;

function requestPageSwitch(index) {
  if (index === currentPage) return;
  if (activePointers > 0) {
    pendingPageSwitch = index;
    return;
  }
  applyPageSwitch(index);
}

function applyPageSwitch(index) {
  if (index === currentPage) return;
  currentPage = index;
  loadPanel();
}

// A rebind reaching an already-open device (BindsFileWatcher, via the live
// channel's bindingsChanged edge - ref/docs/bindings-source.md) is handled
// the same way a page switch is, and for the same reason: it must never fire
// under a thumb already moving, or a button can be yanked out from under a
// press mid-tap. Also coalesced against its OWN in-flight fetch
// (bindingsRefetchInFlight), independent of activePointers, so a burst of
// pushes - Elite can rewrite a bindings file more than once for one rebind,
// same as BindsFileWatcher's own debounce already allows for - never queues
// more than a single extra re-fetch.
let pendingBindingsRefetch = false;
let bindingsRefetchInFlight = false;

function requestBindingsRefetch() {
  if (activePointers > 0 || bindingsRefetchInFlight) {
    pendingBindingsRefetch = true;
    return;
  }
  runBindingsRefetch();
}

async function runBindingsRefetch() {
  bindingsRefetchInFlight = true;
  try {
    await loadPanel();
  } finally {
    bindingsRefetchInFlight = false;
  }
  if (pendingBindingsRefetch) {
    pendingBindingsRefetch = false;
    requestBindingsRefetch();
  }
}

window.addEventListener('pointerdown', () => { activePointers++; });

function releasePointer() {
  if (activePointers > 0) activePointers--;
  if (activePointers > 0) return;

  if (pendingPageSwitch !== null) {
    const target = pendingPageSwitch;
    pendingPageSwitch = null;
    // applyPageSwitch's own loadPanel() already re-fetches with live
    // bindings, so a held rebind signal needs no separate fetch of its own.
    pendingBindingsRefetch = false;
    applyPageSwitch(target);
    return;
  }

  if (pendingBindingsRefetch) {
    pendingBindingsRefetch = false;
    requestBindingsRefetch();
  }
}

window.addEventListener('pointerup', releasePointer);
window.addEventListener('pointercancel', releasePointer);

// Every real page tab drawn by the CURRENT renderTabs pass - [{ index, btn }],
// rebuilt at the top of every render exactly like editButtons is for the
// grid. Deliberately excludes the trailing "+": it is not a page and is
// never a drag source or a drop target, so tabIndexAtPoint below can never
// land on it.
let tabButtons = [];

// Which real tab a point falls in, hit-tested against rectangles measured
// ONCE at the start of the drag - same reasoning as the grid's own
// slotIndexAtPoint (the dragged tab is translated under the finger, so its
// own live rectangle stops describing where it came from). A tab row only
// moves left/right, so this is a 1-D containment check against x alone,
// unlike the grid's 2-D rect test.
function tabIndexAtPoint(rects, x) {
  for (const r of rects) {
    if (x >= r.rect.left && x <= r.rect.right) {
      return r.index;
    }
  }
  return null;
}

// Reorders the page bar (ref/docs/panels-and-pages.md) - mirrors
// postSlotMove's shape, hitting __API_PAGE_MOVE__ instead. currentPage is
// adjusted locally with the same shift-by-one-if-past-the-moved-range
// arithmetic the delete flow above uses: the moved page's own new position
// is toIndex; anything strictly between the old and new position shifts by
// one in the opposite direction the move travelled.
async function postPageMove(fromIndex, toIndex) {
  try {
    const res = await fetch('__API_PAGE_MOVE__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ from: fromIndex, to: toIndex }),
    });
    if (res.ok) {
      if (fromIndex === currentPage) {
        currentPage = toIndex;
      } else if (fromIndex < currentPage && toIndex >= currentPage) {
        currentPage -= 1;
      } else if (fromIndex > currentPage && toIndex <= currentPage) {
        currentPage += 1;
      }
      await loadPanel();
    } else {
      const body = await res.json().catch(() => null);
      showToast((body && body.error) || 'Could not move that page.');
    }
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
}

// A long-press on a tab opens that tab's own "page settings" - the same
// 500ms-hold convention wireSlotGesture uses for a slot's own long-press
// (LONG_PRESS_MS, defined below). Deliberately targets the PRESSED tab's
// page, not currentPage, so a commander can open settings for a page
// without first switching to it. firedLongPress suppresses the ordinary
// tap-to-switch that otherwise follows the same pointer sequence - exactly
// the "timer firing cancels itself out of the primary action" discipline
// wireSlotGesture's own remarks describe.
//
// [2026-09-15] Rewritten to fold tap-to-switch, long-press, and a new
// drag-to-reorder together, using the exact technique wireEditGesture
// already established for slot-to-slot dragging: pointer capture,
// DRAG_START_PX distance threshold (never elapsed time, so a drag can
// never fire alongside the 500ms long-press), target rectangles captured
// once at drag start, and the drop handled entirely in pointerup - no
// separate click listener, which is exactly why wireEditGesture does not
// use one either: a click can still synthesize after a completed drag and
// would otherwise re-trigger tap-to-switch on the source tab.
//
// pointermove cancelling the long-press timer once past DRAG_START_PX is a
// fix, not carried over from the old version: before this, a drag attempt
// would also fire the long-press mid-drag, because nothing here ever
// watched pointermove at all.
function wireTabGesture(btn, index) {
  let timer = null;
  let firedLongPress = false;
  let pointerId = null;
  let startX = 0, startY = 0;
  let moved = false;
  let dragging = false;
  let rects = null;
  let hoverIndex = null;

  const cancelTimer = () => {
    if (timer !== null) { clearTimeout(timer); timer = null; }
  };

  const highlight = (target, on) => {
    const entry = tabButtons.find(b => b.index === target);
    if (entry) entry.btn.classList.toggle('drag-target', on);
  };

  const endDrag = () => {
    if (hoverIndex !== null) highlight(hoverIndex, false);
    hoverIndex = null;
    dragging = false;
    rects = null;
    btn.classList.remove('dragging');
    btn.style.transform = '';
  };

  btn.addEventListener('pointerdown', e => {
    firedLongPress = false;
    pointerId = e.pointerId;
    startX = e.clientX;
    startY = e.clientY;
    moved = false;
    dragging = false;
    // Pointer capture, so the moves and the release keep arriving here
    // once the finger has left this button - without it a drag ending over
    // a DIFFERENT tab would never report its drop.
    try { btn.setPointerCapture(e.pointerId); } catch (err) { /* not fatal */ }
    timer = setTimeout(() => {
      timer = null;
      firedLongPress = true;
      openPageSettingsSheet(index);
    }, LONG_PRESS_MS);
  });

  btn.addEventListener('pointermove', e => {
    if (pointerId === null || e.pointerId !== pointerId) return;
    const dx = e.clientX - startX;
    const dy = e.clientY - startY;
    if (Math.abs(dx) > DRAG_START_PX || Math.abs(dy) > DRAG_START_PX) {
      moved = true;
      // Real movement cancels the long-press timer immediately, so a drag
      // (or, outside edit mode, an ordinary scroll of an overflowing tab
      // row) can never also fire it - this part applies regardless of mode.
      cancelTimer();
    }
    // Dragging itself is edit-mode-only (see #tabs.editing's own remarks) -
    // outside edit mode, movement still cancels the long-press above, but
    // never becomes a drag, leaving the browser's normal touch scrolling of
    // an overflowing tab row untouched.
    if (!moved || !editMode) return;

    if (!dragging) {
      dragging = true;
      rects = tabButtons.map(b => ({ index: b.index, rect: b.btn.getBoundingClientRect() }));
      btn.classList.add('dragging');
    }
    // A tab row only moves left/right, unlike the grid's 2-D translate.
    btn.style.transform = 'translate(' + dx + 'px, 0)';

    const over = tabIndexAtPoint(rects, e.clientX);
    // Its own position is not a target: a drag that ends where it began is
    // a cancel, so it never even highlights.
    const next = (over === null || over === index) ? null : over;
    if (next !== hoverIndex) {
      if (hoverIndex !== null) highlight(hoverIndex, false);
      if (next !== null) highlight(next, true);
      hoverIndex = next;
    }
  });

  btn.addEventListener('pointerup', e => {
    if (pointerId === null || e.pointerId !== pointerId) return;
    pointerId = null;
    cancelTimer();
    const target = hoverIndex;
    const wasDragging = dragging;
    endDrag();

    if (wasDragging) {
      // A completed drag never opens page settings and never switches
      // pages by itself - a drop either moves a page or does nothing.
      // Dropping on itself, or on anything that is not a tab, leaves
      // target null and is simply a cancel.
      if (target !== null) postPageMove(index, target);
      return;
    }

    if (firedLongPress) {
      firedLongPress = false;
      return;
    }
    if (index === currentPage) return;
    currentPage = index;
    loadPanel();
  });

  btn.addEventListener('pointercancel', e => {
    if (pointerId !== null && e.pointerId !== pointerId) return;
    pointerId = null;
    cancelTimer();
    endDrag();
  });

  // Same reason wireHoldGesture suppresses it: a sustained touch otherwise
  // triggers the mobile browser's own text-selection/copy menu.
  btn.addEventListener('contextmenu', e => e.preventDefault());
}

function renderTabs(data) {
  currentPage = data.pageIndex;
  const tabs = el('tabs');
  tabs.innerHTML = '';
  // Same render-time placement as grid.classList.toggle('editing', ...)
  // above - tied to actual render state rather than only the editBtn click
  // that triggers a re-render, so it can never drift out of sync with what
  // wireTabGesture's own editMode check is honoring.
  tabs.classList.toggle('editing', editMode);
  // Every real page tab this pass draws - the drag-to-reorder gesture's own
  // drop targets, rebuilt here so a stale button from a previous render can
  // never be one (same reasoning as the grid's editButtons).
  tabButtons = [];

  // [2026-09-16] Inside a folder (ref/docs/layouts.md), the ordinary tabs are
  // REPLACED - not supplemented - by a way out and a label saying where you
  // are. The commander chose this over showing both: a folder is a place, and
  // a row offering SHIP/SRV/FOOT alongside "back to SHIP" says two different
  // things about where the buttons on screen belong.
  //
  // data.parentPageIndex is the whole signal, and it is absent for an
  // ORPHANED folder page (its button was cleared - an ordinary edit never
  // destroys the page behind it), which correctly falls through to the normal
  // tab row rather than offering a way "back" to a button that no longer
  // points anywhere.
  //
  // Returns before the ordinary row is built. Nothing is pushed onto
  // tabButtons either: neither of these is a page, so neither may become a
  // drag source or a drop target - the same reasoning the "+" already gets.
  if (data.parentPageIndex !== null && data.parentPageIndex !== undefined) {
    const up = document.createElement('button');
    up.type = 'button';
    up.className = 'tab tabUp';
    up.textContent = '↑ ' + (data.parentPageName || 'Back');
    up.setAttribute('aria-label', 'Up one level');
    up.addEventListener('click', () => requestPageSwitch(data.parentPageIndex));
    tabs.appendChild(up);

    // A span, not a button: this names where you are, and on a touch surface
    // the only way to discover that something is not tappable is to tap it.
    // currentPageName, not pageNames[pageIndex]: pageNames deliberately
    // excludes folder pages, so the page being viewed right now is the one
    // name that array can never contain.
    const here = document.createElement('span');
    here.className = 'tabFolderName';
    here.textContent = data.currentPageName || '';
    tabs.appendChild(here);
    return;
  }

  // pageNames EXCLUDES folder pages, so a tab's position in this row is no
  // longer its page index - data.pageIndices[i] is. Every place that used the
  // loop variable as an index now reads the real one; using `index` here
  // again is the exact defect this parallel array exists to prevent.
  data.pageNames.forEach((name, index) => {
    const realIndex = data.pageIndices[index];
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'tab' + (realIndex === data.pageIndex ? ' active' : '');
    b.textContent = name;
    wireTabGesture(b, realIndex);
    tabs.appendChild(b);
    tabButtons.push({ index: realIndex, btn: b });
  });

  // The "+" (ref/docs/panels-and-pages.md): appended AFTER the loop above,
  // never inside it - it is not one of data.pageNames and must never be
  // mistaken for one by anything that walks #tabs' children by index. Not
  // pushed onto tabButtons either, for the same reason: it is not a page
  // and must never become a drag source or a drop target.
  const addBtn = document.createElement('button');
  addBtn.type = 'button';
  addBtn.className = 'tab tabAdd';
  addBtn.textContent = '+';
  addBtn.setAttribute('aria-label', 'Add a page');
  addBtn.addEventListener('click', openAddPageFlow);
  tabs.appendChild(addBtn);
}

let slotButtons = new Map();
let lastLiveState = null;
let liveSource = null;
let liveSourcePage = null;

// The on-device editor's mode toggle (ref/docs/editor.md - "leaning toward
// a mode, unasked": always-live risks reassigning a button from a stray
// tap). Off by default; toggled by #editBtn below.
let editMode = false;

// [{ index, btn }] for every cell the CURRENT edit-mode render drew -
// empty outside edit mode, which is one of the two reasons a drag cannot
// move anything with the mode off (the other, and the one that actually
// matters, is that wireEditGesture is never attached at all).
let editButtons = [];

function layoutGrid(data) {
  applyChrome(data);
  const grid = el('grid');
  grid.style.gridTemplateColumns = `repeat(${data.cols}, ${data.cellWidth}px)`;
  grid.style.gridAutoRows = `${data.cellHeight}px`;
  grid.style.gap = gutterFor() + 'px';
  grid.style.padding = data.framePadding + 'px';
  grid.innerHTML = '';
  slotButtons = new Map();
  // Every cell drawn this pass, in edit mode only - the drop targets a
  // drag hit-tests against (ref/docs/editor.md's drag-to-move). Rebuilt
  // here rather than queried at drop time so a stale button from a
  // previous render can never be a target.
  editButtons = [];
  grid.classList.toggle('editing', editMode);

  const cellW = data.cellWidth, cellH = data.cellHeight;
  // Start optimistically large, then shrink each button individually until
  // its own text fits - then take the MINIMUM fitting size across every
  // button and apply that one size to the whole grid. Per-button sizing was
  // tried first and rejected on sight (individually correct, collectively
  // scrappy) - see ref/docs/device-calibration.md.
  const startPx = Math.max(10, Math.min(30, Math.round(Math.min(cellW / 4, cellH / 2.4))));
  const MIN_PX = 8;
  // [2026-09-18] clientWidth/clientHeight already exclude padPx (box-sizing:
  // border-box on every element, see the stylesheet's universal selector) -
  // so a bare "+ 1" here only guarantees the label doesn't overflow the
  // PADDED box, not that any visible whitespace is left beyond padPx. That
  // let a single long unbreakable word ("Colonisation", "Dock/Launch")
  // converge to a size where the text touches the padded edge with no
  // margin at all, and since `fit` below is the page-wide MINIMUM, that
  // worst-fitting button's bare-edge size became every other button's size
  // too. A real margin here, not a leftover-overflow tolerance, is what
  // keeps that from happening again.
  const OVERFLOW_TOLERANCE_PX = 6;
  // Breathing room inside each button, scaled to the cell, applied BEFORE
  // the fit loop so text is measured against the box it actually gets.
  const padPx = Math.max(3, Math.min(12, Math.round(Math.min(cellW, cellH) * 0.08)));

  const byIndex = new Map(data.slots.map(s => [s.index, s]));
  // Outside edit mode this is exactly the previous behaviour: only
  // occupied (active) slots are ever rendered - a parked slot has no cell
  // in the current template to draw into, and GET /api/panel's own slots[]
  // never lists an empty one at all (ref/docs/panel-api.md - unchanged by
  // this dispatch). Edit mode additionally walks every index in the
  // template's own active range [0, cols*rows) so an EMPTY one is tappable
  // too, which is what lets a commander assign a brand new button rather
  // than only ever re-assign an occupied one.
  const indices = editMode
    ? Array.from({ length: data.cols * data.rows }, (_, i) => i)
    : data.slots.map(s => s.index);

  const made = [];
  for (const index of indices) {
    const slot = byIndex.get(index) || null;
    // Explicit placement, not document-order auto-flow: the active indices
    // are not necessarily contiguous outside edit mode. Plain row-major,
    // matching PanelGeometry.IndexToCell - BandAfter is spacing only and
    // never factors into this.
    const col = index % data.cols;
    const row = Math.floor(index / data.cols);

    const btn = document.createElement('button');
    btn.type = 'button';
    const degraded = !!slot && slot.status !== 'Ok';
    const hasLongPress = !!(slot && slot.longPress);
    const hasLatch = !!(slot && slot.latch);
    const hasHold = !!(slot && slot.hold);
    // [2026-09-16] A folder button navigates rather than firing, and has to
    // look different before it is tapped - same discoverability reasoning as
    // has-long-press/has-latch above it.
    const isFolder = !!(slot && slot.isFolder);
    // GET /api/panel carries each slot's lit level and this render used to
    // ignore it entirely, leaving the FIRST paint unlit no matter what the
    // ship was doing. Buttons only lit once the live channel pushed - and
    // that pushes on CHANGE, so a control already engaged when the page
    // loaded (landing gear down, reported 2026-09-07) stayed dark until it
    // was toggled. Apply it here so the first paint is already correct;
    // applyLive() then keeps it current and is idempotent over these.
    const litLevel = slot ? slot.lit : 'Off';
    const litClass = (litLevel === 'Partial' || litLevel === 'Full') ? ' lit' : '';
    const litFullClass = litLevel === 'Full' ? ' lit-full' : '';
    btn.className = 'slot' + (degraded ? ' degraded' : '') + (slot ? '' : ' empty') + (hasLongPress ? ' has-long-press' : '') + (hasLatch ? ' has-latch' : '') + (hasHold ? ' holdable' : '') + (isFolder ? ' is-folder' : '') + litClass + litFullClass;
    btn.style.gridColumn = String(col + 1);
    btn.style.gridRow = String(row + 1);
    btn.style.fontSize = startPx + 'px';
    btn.style.padding = padPx + 'px';
    btn.textContent = slot ? slot.label : (editMode ? '+' : '');
    if (degraded) {
      btn.title = slot.reason;
    }

    // Immediate visual acknowledgement on press, independent of the
    // fetch round trip below - a control surface that waits for a server
    // response before showing anything feels laggy and broken.
    btn.addEventListener('pointerdown', () => btn.classList.add('pressed'));
    ['pointerup', 'pointerleave', 'pointercancel'].forEach(evt =>
      btn.addEventListener(evt, () => btn.classList.remove('pressed')));

    // The two gestures are mutually exclusive BY CONSTRUCTION, and that is
    // what keeps a drag from ever firing a control: wireSlotGesture is the
    // only thing on this page that presses a button, and it is wired only
    // when the mode is OFF. wireEditGesture never presses anything - it
    // opens the slot sheet or it moves a slot, nothing else.
    if (editMode) {
      wireEditGesture(btn, index, slot);
      editButtons.push({ index: index, btn: btn });
    } else if (slot) {
      wireSlotGesture(btn, slot);
    }

    grid.appendChild(btn);
    if (slot) slotButtons.set(slot.index, btn);
    made.push(btn);
  }

  let fit = startPx;
  // Buttons whose OWN shrink loop bottomed out at MIN_PX and still overflow -
  // the uniform final size below can only ever be <= MIN_PX for these too
  // (fit is the minimum across every button, and this one's own floor is
  // already MIN_PX), so the overflow measured here survives the final pass
  // unchanged. Never populated by a button that fit above MIN_PX, and never
  // by one that fit AT MIN_PX with no overflow - both cases leave scrollWidth/
  // scrollHeight within budget when this check runs.
  const overflowingAtFloor = [];
  for (const b of made) {
    let px = startPx;
    while (px > MIN_PX && (b.scrollHeight > b.clientHeight + OVERFLOW_TOLERANCE_PX || b.scrollWidth > b.clientWidth + OVERFLOW_TOLERANCE_PX)) {
      px -= 1;
      b.style.fontSize = px + 'px';
    }
    if (px < fit) fit = px;
    if (px <= MIN_PX && (b.scrollHeight > b.clientHeight + OVERFLOW_TOLERANCE_PX || b.scrollWidth > b.clientWidth + OVERFLOW_TOLERANCE_PX)) {
      overflowingAtFloor.push(b);
    }
  }
  for (const b of made) b.style.fontSize = fit + 'px';
  // Applied after the uniform size, not before: clipping a button whose label
  // will end up shrinking further under the shared `fit` size would be
  // premature - though in practice fit can never rise above this button's own
  // MIN_PX floor once it is in this list. Ensures no unclipped mid-state on
  // this button is ever the last mutation applied to it.
  for (const b of overflowingAtFloor) b.classList.add('labelClipped');
}

// The per-step run read-back (ref/docs/macro-builder.md's "What the builder
// shows after a run"). body.steps is only present, and only non-empty, when
// the press ran an actual macro past the injection guard - a plain action's
// press response has no such field, and a macro refused before it ever
// started (busy, guard) reports an empty array, so neither opens this sheet.
function showMacroRunResult(body) {
  if (!body || !body.steps || !body.steps.length) return;

  el('macroRunResultSummary').textContent = body.fired
    ? 'Every step ran.'
    : (body.reason || 'The macro stopped partway through.');

  const list = el('macroRunResultList');
  list.innerHTML = '';
  body.steps.forEach(step => {
    const row = document.createElement('div');
    row.className = 'stepRow';

    const main = document.createElement('div');
    main.className = 'stepRowMain';

    const text = document.createElement('span');
    text.className = 'stepRowText';
    text.textContent = (step.stepIndex + 1) + '. ' + step.stepKind;
    main.appendChild(text);

    const failed = step.outcome === 'Failed';
    const note = document.createElement('span');
    note.className = 'stepRowNote' + (failed ? ' bad' : '');
    note.textContent = (failed ? 'Failed' : 'Sent') + ' - ' + Math.round(step.elapsedMs) + 'ms';
    main.appendChild(note);

    row.appendChild(main);
    list.appendChild(row);
  });

  pushSheet('macroRunResult');
}
el('macroRunResultClose').addEventListener('click', () => popSheet('macroRunResult'));

// What used to happen inline on a macro press's response (O28, 2026-09-17),
// now driven by the live channel's macroFinished edge instead - the press
// answers 'Started' at once and the run's real outcome lands here when it
// ends. `finished` carries the same fields the press response did (fired,
// reason, steps...) plus macroId, so showMacroRunResult reads it unchanged.
// The toast is NOT gated on the step-results setting: a run that stopped or
// aborted must always say so, the same as a refused press always does.
function onMacroFinished(finished) {
  if (!finished.fired) showToast(finished.reason || 'The macro did not finish.');
  if (showMacroStepResults) showMacroRunResult(finished);
}

async function onSlotTap(slot) {
  // [2026-09-16] A folder navigates rather than firing (ref/docs/layouts.md),
  // and this branch comes FIRST deliberately: a folder has no binding that
  // could be missing, so routing it through the degraded-slot refusal below
  // would make a perfectly healthy folder untappable the moment its own
  // status was ever anything but Ok. requestPageSwitch, never a direct
  // switch - it is the one path that will not change the page under a thumb
  // already moving (see its own remarks).
  if (slot.isFolder) {
    requestPageSwitch(slot.folderPageIndex);
    return;
  }

  if (slot.status !== 'Ok') {
    // A degraded slot never fires - it only explains itself.
    showToast(slot.reason || 'This button is not set up yet.');
    return;
  }

  try {
    const res = await fetch('__API_PRESS__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ page: currentPage, slot: slot.index }),
    });
    const body = await res.json().catch(() => null);
    if (!body || !body.fired) {
      // The one behaviour this whole feature exists for: the reason a
      // press did nothing reaches the phone, not just the PC's own log.
      // Still where a macro's SYNCHRONOUS refusals (guard, busy, the
      // second press that stops a run) are shown; a macro that genuinely
      // starts answers fired:true/'Started' here and its real outcome
      // arrives via applyLive's macroFinished edge (O28).
      showToast((body && body.reason) || 'The button did not fire.');
    }
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
}

async function onSlotLongPress(slot) {
  if (!slot.longPress) {
    // Nothing assigned - a normal state (most slots have no second
    // action), not an error to explain.
    return;
  }
  if (slot.longPress.status !== 'Ok') {
    showToast(slot.longPress.reason || 'The long press action is not set up yet.');
    return;
  }

  try {
    const res = await fetch('__API_PRESS__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ page: currentPage, slot: slot.index, longPress: true }),
    });
    const body = await res.json().catch(() => null);
    if (!body || !body.fired) {
      // Same as onSlotTap: synchronous refusals only - a started macro's
      // outcome comes down the live channel (O28).
      showToast((body && body.reason) || 'The button did not fire.');
    }
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
}

// Long press vs. tap (ref/docs/editor.md's long-press wiring), outside edit
// mode only - editMode's own click handler (openSlotSheet) is unaffected.
// 500ms matches the common OS long-press convention (e.g. Android's default
// ViewConfiguration long-press timeout) - chosen as a familiar default, not
// measured against this project's own hardware. A long press fires ONLY the
// long-press action, never the primary as well: the timer firing cancels
// itself out of the pointerup handler below. A pointer that moves past
// MOVE_CANCEL_PX before the timer fires is a scroll or a drag, not a tap or
// a hold - the timer is cancelled and neither action fires.
const LONG_PRESS_MS = 500;
const MOVE_CANCEL_PX = 10;

// [2026-09-12] The hold gesture (ref/docs/latching-keys.md's hold-to-thrust
// extension): a genuinely held key for as long as a finger stays on the
// button, for a control like ship thrust where Elite needs to see the key
// stay down rather than a quick tap or the long-press timer's fixed
// duration. Completely separate from onSlotTap/onSlotLongPress - real
// pointerdown/pointerup, immediately, with no timer at all.
async function postHoldPhase(slot, phase) {
  try {
    const res = await fetch('__API_PRESS__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ page: currentPage, slot: slot.index, hold: phase }),
    });
    const body = await res.json().catch(() => null);
    if (!body || !body.fired) {
      showToast((body && body.reason) || 'The button did not fire.');
    }
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
}

function wireHoldGesture(btn, slot) {
  // A sustained touch-hold otherwise triggers the mobile browser's own
  // text-selection/copy menu on some platforms even with user-select: none
  // set - suppressing contextmenu directly is what actually stops it
  // (live-tested regression from the same round that led to this feature).
  btn.addEventListener('contextmenu', e => e.preventDefault());
  btn.addEventListener('pointerdown', () => postHoldPhase(slot, 'down'));
  ['pointerup', 'pointercancel', 'pointerleave'].forEach(evt =>
    btn.addEventListener(evt, () => postHoldPhase(slot, 'up')));
}

function wireSlotGesture(btn, slot) {
  // A hold-flagged slot skips the tap/long-press machinery entirely - the
  // 500ms timer below has no meaning for a control that must react to the
  // very first frame of contact. Every other slot falls through unchanged:
  // this guard is the only thing added to this function for the hold
  // gesture to exist.
  if (slot.hold) {
    wireHoldGesture(btn, slot);
    return;
  }

  let timer = null;
  let firedLongPress = false;
  let moved = false;
  let startX = 0, startY = 0;

  btn.addEventListener('pointerdown', e => {
    firedLongPress = false;
    moved = false;
    startX = e.clientX;
    startY = e.clientY;
    timer = setTimeout(() => {
      timer = null;
      firedLongPress = true;
      onSlotLongPress(slot);
    }, LONG_PRESS_MS);
  });

  btn.addEventListener('pointermove', e => {
    if (moved || timer === null) return;
    if (Math.abs(e.clientX - startX) > MOVE_CANCEL_PX || Math.abs(e.clientY - startY) > MOVE_CANCEL_PX) {
      moved = true;
      clearTimeout(timer);
      timer = null;
    }
  });

  const cancelTimer = () => {
    if (timer !== null) {
      clearTimeout(timer);
      timer = null;
    }
  };
  btn.addEventListener('pointerleave', cancelTimer);
  btn.addEventListener('pointercancel', cancelTimer);

  btn.addEventListener('pointerup', () => {
    const wasPending = timer !== null;
    cancelTimer();
    if (wasPending && !moved && !firedLongPress) {
      onSlotTap(slot);
    }
  });
}

// ---------------------------------------------------------------------
// Drag a button to a different slot, in edit mode, on the tablet
// (ref/docs/editor.md). The commander's own words: "I want to be able to
// click and drag panel boxes around if possible on the tablet to move them
// when in edit mode."
//
// HOW THIS AVOIDS COLLIDING WITH LONG-PRESS, which already means something
// on a slot that has a second action: it cannot collide, because the two
// gestures are never live on the same button. wireSlotGesture (tap, and
// the 500ms hold that fires a long-press action) is attached only when
// edit mode is OFF; wireEditGesture is attached only when it is ON -
// layoutGrid's two arms, one or the other, never both. Even if that ever
// changed, these two would still be distinguishable: a long press is a
// pointer that STAYS PUT for 500ms, and a drag is a pointer that MOVES.
// This gesture starts on distance and never on elapsed time, which is also
// why a slow tap is still a tap - the brief's own requirement, and the
// reason DRAG_START_PX is a distance and there is no timer anywhere below.
//
// It shares the 10px figure with wireSlotGesture's MOVE_CANCEL_PX quite
// deliberately: that is already the distance at which this page stops
// believing a pointer is stationary, and one threshold means a gesture
// cannot be a drag here and a tap there.
const DRAG_START_PX = 10;

// Which cell a point falls in, hit-tested against rectangles measured ONCE
// at the start of the drag. Measured once because the dragged button is
// translated under the finger while it moves, so its own live rectangle
// stops describing the cell it came from - and because the grid cannot
// reflow mid-drag anyway. document.elementFromPoint is deliberately not
// used: with the pointer captured, the element under the finger is the
// dragged button itself.
function slotIndexAtPoint(rects, x, y) {
  for (const r of rects) {
    if (x >= r.rect.left && x <= r.rect.right && y >= r.rect.top && y <= r.rect.bottom) {
      return r.index;
    }
  }
  return null;
}

function wireEditGesture(btn, index, slot) {
  let pointerId = null;
  let startX = 0, startY = 0;
  let moved = false;
  let dragging = false;
  let rects = null;
  let hoverIndex = null;

  const highlight = (target, on) => {
    const entry = editButtons.find(b => b.index === target);
    if (entry) entry.btn.classList.toggle('drag-target', on);
  };

  const endDrag = () => {
    if (hoverIndex !== null) highlight(hoverIndex, false);
    hoverIndex = null;
    dragging = false;
    rects = null;
    btn.classList.remove('dragging');
    btn.style.transform = '';
  };

  btn.addEventListener('pointerdown', e => {
    pointerId = e.pointerId;
    startX = e.clientX;
    startY = e.clientY;
    moved = false;
    dragging = false;
    // Pointer capture, so the moves and the release keep arriving here
    // once the finger has left this button - without it a drag ending over
    // a DIFFERENT cell would never report its drop.
    try { btn.setPointerCapture(e.pointerId); } catch (err) { /* not fatal */ }
  });

  btn.addEventListener('pointermove', e => {
    if (pointerId === null || e.pointerId !== pointerId) return;
    const dx = e.clientX - startX;
    const dy = e.clientY - startY;
    if (Math.abs(dx) > DRAG_START_PX || Math.abs(dy) > DRAG_START_PX) moved = true;
    // An empty cell has nothing to pick up: it still tracks `moved` above,
    // so a swipe across it is not mistaken for a tap, but it never becomes
    // a drag.
    if (!moved || !slot) return;

    if (!dragging) {
      dragging = true;
      rects = editButtons.map(b => ({ index: b.index, rect: b.btn.getBoundingClientRect() }));
      btn.classList.add('dragging');
    }
    btn.style.transform = 'translate(' + dx + 'px, ' + dy + 'px)';

    const over = slotIndexAtPoint(rects, e.clientX, e.clientY);
    // Its own cell is not a target: a drag that ends where it began is a
    // cancel, so it never even highlights.
    const next = (over === null || over === index) ? null : over;
    if (next !== hoverIndex) {
      if (hoverIndex !== null) highlight(hoverIndex, false);
      if (next !== null) highlight(next, true);
      hoverIndex = next;
    }
  });

  btn.addEventListener('pointerup', e => {
    if (pointerId === null || e.pointerId !== pointerId) return;
    pointerId = null;
    const target = hoverIndex;
    const wasDragging = dragging;
    const wasMoved = moved;
    endDrag();

    if (wasDragging) {
      // A completed drag NEVER opens the slot sheet, and there is nothing
      // here that could press the button - a drop either moves a slot or
      // does nothing at all. Dropping on itself, or on anything that is
      // not a cell (the tab row, the chrome, off the grid), leaves target
      // null and is simply a cancel.
      if (target !== null) postSlotMove(index, target);
      return;
    }

    // A tap - and only movement, never elapsed time, decides that. A slow
    // tap still opens the slot sheet.
    if (!wasMoved) openSlotSheet(index, slot);
  });

  btn.addEventListener('pointercancel', e => {
    if (pointerId !== null && e.pointerId !== pointerId) return;
    pointerId = null;
    endDrag();
  });
}

function applyLive(state) {
  // Cache a copy with the one-shot edge fields already cleared, BEFORE the
  // edge handlers below run off the original `state` object. renderPanel
  // replays lastLiveState unconditionally at the end of every panel reload
  // (including the render the refetch below itself produces) - caching the
  // raw state here left bindingsChanged/switchToPage still true on the
  // replay, which re-fired requestBindingsRefetch() forever (only the
  // in-flight/pending guard above stopped it from being a stack overflow,
  // turning it into a steady poll loop instead - the "rebind locks the
  // panel" symptom, user-confirmed live 2026-09-11). state.timing and
  // state.slots are LEVELS, not edges, and must keep replaying untouched.
  lastLiveState = { ...state, bindingsChanged: false, switchToPage: null, layoutChanged: false, macroFinished: null, themeChanged: false };
  // A switch, a pushed timing change, or a rebind can all arrive well
  // before Elite has ever launched (a commander adjusting macro timing or
  // rebinding on the PC, or a switch queued while the game was closed) -
  // none of these depend on gameRunning, so all three are handled
  // unconditionally. requestPageSwitch, never applyPageSwitch - going
  // straight to apply here is what would switch the page under a thumb
  // already moving.
  if (state.switchToPage !== null && state.switchToPage !== undefined) {
    requestPageSwitch(state.switchToPage);
  }
  if (state.timing) applyPushedTiming(state.timing);
  // Carries no bindings content of its own (see
  // PanelLiveEndpoint.LiveState.BindingsChanged) - the request below is the
  // one path that actually re-fetches, via GET /api/panel.
  if (state.bindingsChanged) requestBindingsRefetch();
  // A layout edited live from the PC (?asDevice=<id>) carries no content of
  // its own either, same as a rebind - reuses the exact same re-fetch path
  // rather than a second one, since both are "something changed server-side,
  // re-fetch GET /api/panel" with nothing more to say.
  if (state.layoutChanged) requestBindingsRefetch();
  // A macro this device pressed has finished (O28, 2026-09-17): the outcome
  // the press response used to carry arrives here instead, once. The fifth
  // one-shot edge - cleared on the cached copy above like the other four,
  // or renderPanel's replay would re-toast and re-open the sheet on the
  // next unrelated grid rebuild for a run that ended minutes ago.
  if (state.macroFinished) onMacroFinished(state.macroFinished);
  // An EDHM colour edit reaching an already-open device (ThemeFileWatcher,
  // via the live channel's themeChanged edge). Carries no theme content of
  // its own (see PanelLiveEndpoint.LiveState.ThemeChanged) - reuses the
  // exact same pointer-safe, coalesced re-fetch path a rebind already uses,
  // since both are "something changed server-side, re-fetch GET /api/panel"
  // with nothing more to say, and a grid rebuild must not yank a button out
  // from under a thumb already moving either way. The settings gear's own
  // "From your HUD" swatches are a SEPARATE fetch (GET /api/theme) that
  // loadPanel does not make, so it is only refreshed here if the sheet
  // showing them is actually open right now - matching the same condition
  // boot() already uses to decide whether to call loadTheme() at all.
  if (state.themeChanged) {
    requestBindingsRefetch();
    if (!el('settingsSheet').classList.contains('hidden')) loadTheme();
  }
  // The grid stays visible and tappable whether or not Elite is running -
  // that's what lets a commander lay out and test buttons without the game
  // open. state.gameRunning only ever affected lit state below: with no
  // live snapshot, LitStateResolver already reports every slot "Off"
  // (ref/docs/lit-state.md), so there's nothing else to gate here. Whether
  // Elite is actually running - or even the active window - is enforced
  // separately and unconditionally at press time by InjectionGuard.
  // 'lit' is now a level string ("Off"/"Partial"/"Full", see
  // ref/docs/lit-state.md), not a boolean - 'lit' the CSS class applies at
  // Partial and Full alike (partial brightness on the edges); 'lit-full'
  // layers the extra glow on top, at Full only.
  const litByIndex = new Map(state.slots.map(s => [s.index, s.lit]));
  for (const [index, btn] of slotButtons) {
    const level = litByIndex.get(index);
    btn.classList.toggle('lit', level === 'Partial' || level === 'Full');
    btn.classList.toggle('lit-full', level === 'Full');
  }
}

// Idempotent for the SAME page (a resize/orientation re-fetch never reopens
// the stream) but reconnects when the page actually changed (lit state is
// page-specific) - ref/docs/panels-and-pages.md's "Tabs".
function connectLive() {
  if (liveSource && liveSourcePage === currentPage) return;
  if (liveSource) {
    liveSource.close();
  }
  liveSourcePage = currentPage;
  // EventSource never goes through window.fetch, so the asDevice override
  // above cannot reach it - appended here directly, the one other place this
  // page opens a connection of its own.
  liveSource = new EventSource('__API_PANEL_LIVE__' + '?page=' + currentPage + asDeviceQuerySuffix(true));
  liveSource.onmessage = ev => {
    try {
      applyLive(JSON.parse(ev.data));
    } catch (e) {
      // A malformed push is dropped rather than tearing the connection down
      // - EventSource itself already retries on a real network error.
    }
  };
}

function renderPanel(data) {
  el('theme-vars').textContent = data.themeCss;
  renderTabs(data);
  layoutGrid(data);
  // Reapply the last known live state immediately (e.g. after a
  // resize/orientation re-fetch rebuilt every button) rather than leaving
  // every slot looking unlit until the next real game-state change.
  if (lastLiveState) applyLive(lastLiveState);
}

// ---------------------------------------------------------------------
// The on-device editor (ref/docs/editor.md): tap a slot, choose an action,
// no file editing, no restart. #editBtn toggles the mode; the slot sheet,
// action picker and rename prompt below are the three screens the spec
// names. NOT driven by any automated test in this suite (no headless
// browser here, same as every other client behaviour on this page - see
// ref/docs/web-client.md's "What's tested, and what plainly isn't") -
// everything past this point is reasoned from the server contract
// (SlotEditEndpointTests/ActionsEndpointTests/ServerHostBuilderTests), not
// driven in a browser.
// ---------------------------------------------------------------------

el('editBtn').addEventListener('click', () => {
  editMode = !editMode;
  el('editBtn').classList.toggle('active', editMode);
  el('editBtn').textContent = editMode ? 'Done' : 'Edit';
  if (lastPanelData) renderPanel(lastPanelData);
});

let editorSlotIndex = null;
let editorSlotData = null;

// "What this is, its status and why if degraded, and: change action,
// rename, clear, set a long-press action" (ref/docs/editor.md's "Slot
// sheet").
function openSlotSheet(index, slot) {
  editorSlotIndex = index;
  editorSlotData = slot;

  el('slotSheetTitle').textContent = slot ? slot.label : 'Empty slot';
  if (!slot) {
    el('slotSheetStatus').textContent = 'Nothing is assigned to this button yet.';
  } else if (slot.status === 'Ok') {
    // A folder's own reason already says what it opens - "Working." would
    // throw that away and tell a commander nothing about where the button
    // goes.
    el('slotSheetStatus').textContent = slot.isFolder
      ? slot.reason
      : (slot.chord ? `Bound (${slot.chord}).` : 'Working.');
  } else {
    el('slotSheetStatus').textContent = slot.reason;
  }

  // Rename/Clear/long-press only make sense once something is actually
  // assigned - a long-press attaches to an already-occupied slot, exactly
  // like SlotEditEndpoint.SetLongPress refuses NoSuchSlot for an empty one.
  // [2026-09-16] Folders (ref/docs/layouts.md). Computed before every
  // visibility decision below, because a folder is not an action-shaped slot
  // and nearly every control on this sheet is action-shaped.
  const isFolderSlot = !!(slot && slot.isFolder);

  el('slotSheetRename').classList.toggle('hidden', !slot);
  el('slotSheetClear').classList.toggle('hidden', !slot);
  // "Change action…" would be a lie on a folder - assigning an action here
  // does not change the folder, it REPLACES it (leaving its page intact as
  // an orphan), which is a different enough thing to say differently.
  el('slotSheetAssign').textContent = isFolderSlot
    ? 'Replace this folder with an action…'
    : (slot ? 'Change action…' : 'Choose an action…');

  // The empty slot's third choice, and the two an existing folder gets.
  el('slotSheetMakeFolder').classList.toggle('hidden', !!slot);
  el('slotSheetOpenFolder').classList.toggle('hidden', !isFolderSlot);
  el('slotSheetDeleteFolder').classList.toggle('hidden', !isFolderSlot);

  // ref/docs/latching-keys.md.
  //
  // [2026-09-09] No longer offered on a MACRO slot. This section used to
  // say the control was offered on every occupied slot because
  // GET /api/panel's shape could not tell an action slot from a macro one,
  // and the server's refusal ("Only a single control can be held down - a
  // macro is a sequence of keystrokes, so there is no one key to hold")
  // carried the explanation instead. ref/docs/editor.md called that the
  // roughest edge in the feature, and the shape change turned out to be
  // one field: slot.macroId, which the panel response now carries because
  // this screen needed it for two things at once - hiding this button, and
  // offering "Edit this macro..." below. The server refusal is untouched
  // and still enforces it; the button being absent is the courtesy.
  const isMacroSlot = !!(slot && slot.macroId);
  // [2026-09-16] Hidden on a FOLDER slot for a sharper version of the same
  // reason it is hidden on a macro: a macro has no single key to hold, and a
  // folder has no key at all. Same division as ever - the server refusal
  // stays and is the enforcement; the button being absent is the courtesy.
  const isLatch = !!(slot && slot.latch);
  el('slotSheetLatch').classList.toggle('hidden', !slot || isMacroSlot || isFolderSlot);
  el('slotSheetLatch').textContent = isLatch ? 'Stop holding - go back to a normal tap' : 'Hold until tapped again';

  // [2026-09-12] The hold gesture (ref/docs/latching-keys.md's
  // hold-to-thrust extension) - exact mirror of the latch toggle above,
  // hidden on a macro slot for the same "no single key to hold" reason.
  const isHold = !!(slot && slot.hold);
  el('slotSheetHold').classList.toggle('hidden', !slot || isMacroSlot || isFolderSlot);
  el('slotSheetHold').textContent = isHold ? 'Stop holding - go back to a normal tap' : 'Hold while pressed';

  // The shortest path from a button that does nearly the right thing to
  // changing what it does (ref/docs/macro-builder.md). Opens the same
  // builder the settings sheet's Macros pane does - a shipped macro opens
  // read-only there, with its copy verb, so this cannot become a second
  // way to edit one.
  el('slotSheetEditMacro').classList.toggle('hidden', !isMacroSlot);

  // A folder has no primary keystroke, so a SECOND one attached to it has
  // nothing to be second to - hidden for the same reason latch and hold are.
  const hasLongPress = !!(slot && slot.longPress);
  el('slotSheetLongPress').classList.toggle('hidden', !slot || isFolderSlot);
  el('slotSheetLongPress').textContent = hasLongPress ? 'Change long-press action…' : 'Set long-press action…';
  el('slotSheetClearLongPress').classList.toggle('hidden', !hasLongPress);

  pushSheet('slotSheet');
}
el('slotSheetClose').addEventListener('click', () => popSheet('slotSheet'));

el('slotSheetAssign').addEventListener('click', () => {
  popSheet('slotSheet');
  openActionPicker();
});

el('slotSheetRename').addEventListener('click', () => {
  popSheet('slotSheet');

  // [2026-09-16] Renaming a FOLDER renames its interior PAGE, not the slot's
  // label override - a folder's name and its page's name are one thing (a
  // real folder's mental model). Two independently-editable names for one
  // object is how a button ends up reading THRUST while the bar inside it
  // reads something else. Goes through the page-rename route and the page
  // name dialog, which is also why it is not held to the slot label's
  // two-lines-of-fifteen budget: a page name is drawn in the bar, not
  // squeezed into a cell.
  if (editorSlotData && editorSlotData.isFolder) {
    const folderIndex = editorSlotData.folderPageIndex;
    openPageNameDialog('RENAME THIS FOLDER', editorSlotData.label, async name => {
      try {
        const res = await fetch('__API_PAGE_RENAME__', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          credentials: 'same-origin',
          body: JSON.stringify({ page: folderIndex, name }),
        });
        const body = await res.json().catch(() => null);
        if (!res.ok) {
          showToast((body && body.error) || 'Could not rename that folder.');
          return;
        }
        await loadPanel();
      } catch (e) {
        showToast('Could not reach LunaPanel.');
      }
    });
    return;
  }

  const currentLabel = editorSlotData.label;
  openRenameDialog(currentLabel, async finalText => {
    // Unchanged from what was already showing - a no-op either way (it was
    // already exactly this text, whether that was an override or the
    // catalogue's own default), so nothing is sent at all.
    if (finalText === currentLabel) return;
    await postSlotLabel(editorSlotIndex, finalText.trim() === '' ? null : finalText);
  });
});

el('slotSheetLatch').addEventListener('click', async () => {
  popSheet('slotSheet');
  await postSlotLatch(editorSlotIndex, !(editorSlotData && editorSlotData.latch));
});

el('slotSheetHold').addEventListener('click', async () => {
  popSheet('slotSheet');
  await postSlotHold(editorSlotIndex, !(editorSlotData && editorSlotData.hold));
});

el('slotSheetEditMacro').addEventListener('click', async () => {
  popSheet('slotSheet');
  const macroId = editorSlotData && editorSlotData.macroId;
  await loadMacrosPane();
  const macro = macroList.find(m => m.id === macroId);
  if (macro) openMacroBuilder(macro);
  else showToast('That macro no longer exists.');
});

el('slotSheetLongPress').addEventListener('click', () => {
  popSheet('slotSheet');
  openActionPicker('longPress');
});

el('slotSheetClearLongPress').addEventListener('click', async () => {
  popSheet('slotSheet');
  await postSlotLongPress(editorSlotIndex, null, null);
});

el('slotSheetClear').addEventListener('click', async () => {
  popSheet('slotSheet');
  // Distinct confirm wording from "forget this device" etc - clearing a
  // slot must not read as anything close to deleting the page it lives on.
  if (!confirm('Clear this button? The page itself is not affected - only this one slot.')) return;
  try {
    const res = await fetch('__API_SLOT_CLEAR__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ page: currentPage, slot: editorSlotIndex }),
    });
    if (res.ok) {
      await loadPanel();
    } else {
      showToast('Could not clear that button.');
    }
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
});

// ---------------------------------------------------------------------
// [2026-09-16] Folders (ref/docs/layouts.md): make one, open one, delete
// one. Three verbs across two routes, and the split matters - "Make this a
// folder" is the only thing that creates a page, and "Delete this folder"
// is the only thing that destroys one. Clearing a folder button (the
// handler directly above) goes to the slot-clear route, which cannot remove
// a page at all, so an ordinary edit can never take a page full of buttons
// with it. That is the commander's own ruling, kept true by which route
// each control posts to rather than by remembering.
// ---------------------------------------------------------------------

el('slotSheetMakeFolder').addEventListener('click', () => {
  popSheet('slotSheet');
  const slotIndex = editorSlotIndex;
  openPageNameDialog('NAME THE NEW FOLDER', '', async name => {
    try {
      const res = await fetch('__API_SLOT_MAKE_FOLDER__', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        // The folder's interior gets the SAME panel size as the page it
        // sits on, so its buttons are the size the commander's thumb is
        // already calibrated to - and there is no second size picker to
        // answer before a folder can exist.
        body: JSON.stringify({ page: currentPage, slot: slotIndex, name, templateId: lastPanelData ? lastPanelData.templateId : null }),
      });
      const body = await res.json().catch(() => null);
      if (!res.ok) {
        showToast((body && body.error) || 'Could not make that a folder.');
        return;
      }
      // Straight in, exactly like the "+" flow lands on the page it just
      // added - a folder created and then left unopened is a button the
      // commander now has to find again to fill.
      currentPage = body.pageIndex;
      await loadPanel();
    } catch (e) {
      showToast('Could not reach LunaPanel.');
    }
  });
});

el('slotSheetOpenFolder').addEventListener('click', () => {
  popSheet('slotSheet');
  if (editorSlotData && editorSlotData.isFolder) requestPageSwitch(editorSlotData.folderPageIndex);
});

el('slotSheetDeleteFolder').addEventListener('click', async () => {
  popSheet('slotSheet');
  const folderIndex = editorSlotData ? editorSlotData.folderPageIndex : null;
  if (folderIndex === null || folderIndex === undefined) return;
  // Wording deliberately unlike Clear's directly above it: this is the one
  // control on this sheet that destroys buttons, and the confirm has to say
  // so rather than reading as another tidy-up.
  if (!confirm('Delete this folder and every button inside it? This cannot be undone.')) return;
  try {
    const res = await fetch('__API_PAGE_DELETE__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ page: folderIndex }),
    });
    const body = await res.json().catch(() => null);
    if (!res.ok) {
      showToast((body && body.error) || 'Could not delete that folder.');
      return;
    }
    // The server clears the button that pointed at it, so nothing local
    // needs adjusting - but currentPage can be past the removed page, the
    // same shift the page-settings delete flow already handles.
    if (folderIndex < currentPage) currentPage -= 1;
    await loadPanel();
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
});

// ---------------------------------------------------------------------
// Action picker (ref/docs/editor.md's "Action picker"): search-first,
// grouped by category for curated actions ("OTHER CONTROLS" for
// uncurated), bound state and chord visible on every row, with the
// "show everything" toggle. Unbound rows are marked BEFORE assignment,
// never only discovered after (the commander's own instruction).
// ---------------------------------------------------------------------

let pickerActions = [];

async function loadPickerActions() {
  try {
    const res = await fetch(`__API_ACTIONS__?all=${el('actionPickerShowAll').checked}`, { credentials: 'same-origin' });
    pickerActions = res.ok ? await res.json() : [];
  } catch (e) {
    pickerActions = [];
  }
  renderPickerList();
}

function appendPickerGroup(list, heading, actions) {
  if (actions.length === 0) return;
  const label = document.createElement('p');
  label.className = 'pickerGroupLabel';
  label.textContent = heading;
  list.appendChild(label);

  for (const a of actions) {
    const row = document.createElement('button');
    row.type = 'button';
    row.className = 'actionRow' + (a.isBound ? '' : ' unbound');

    const name = document.createElement('span');
    name.className = 'actionRowLabel';
    name.textContent = a.displayLabel;
    row.appendChild(name);

    const state = document.createElement('span');
    state.className = 'actionRowState';
    state.textContent = a.isBound ? (a.displayChord || 'Bound') : 'Not mapped in Elite';
    row.appendChild(state);

    row.addEventListener('click', () => onActionPicked(a));
    list.appendChild(row);
  }
}

// One row per macro, shipped and commander-authored together, each marked
// with its degraded state exactly as an unbound action row is - and for
// the same reason: a commander must not have to place a button to find out
// it will not work. The degraded state is MacroKnowledgeBuilder's, carried
// on GET __API_MACROS__, never a second opinion computed here.
function appendMacroPickerGroup(list, macros) {
  if (macros.length === 0) return;

  const label = document.createElement('p');
  label.className = 'pickerGroupLabel';
  label.textContent = 'MACROS';
  list.appendChild(label);

  for (const macro of macros) {
    const row = document.createElement('button');
    row.type = 'button';
    row.className = 'actionRow' + (macro.isDegraded ? ' unbound' : '');

    const name = document.createElement('span');
    name.className = 'actionRowLabel';
    name.textContent = macro.displayLabel;
    row.appendChild(name);

    const state = document.createElement('span');
    state.className = 'actionRowState';
    state.textContent = macro.isDegraded
      ? 'A control it uses has no key'
      : (macro.steps.length === 1 ? '1 step' : macro.steps.length + ' steps');
    row.appendChild(state);

    row.addEventListener('click', () => onMacroPicked(macro));
    list.appendChild(row);
  }
}

function renderPickerList() {
  const q = el('actionPickerSearch').value.trim().toLowerCase();
  const list = el('actionPickerList');
  list.innerHTML = '';

  const filtered = q
    ? pickerActions.filter(a => a.displayLabel.toLowerCase().includes(q) || a.actionName.toLowerCase().includes(q))
    : pickerActions;

  const curated = filtered.filter(a => a.isCurated);
  const uncurated = filtered.filter(a => !a.isCurated);

  const byCategory = new Map();
  for (const a of curated) {
    const key = a.category || '';
    if (!byCategory.has(key)) byCategory.set(key, []);
    byCategory.get(key).push(a);
  }
  // The MACROS group (ref/docs/macro-builder.md; ref/docs/editor.md has
  // been naming this as missing since the editor shipped). FIRST in the
  // list, above the controls: a commander who has just built a macro is
  // here to put it on a button, and it is the shorter of the two lists by
  // three hundred rows.
  //
  // Never offered when the picker was opened to fill in a macro STEP -
  // a macro cannot call a macro. That is not in the grammar, and a step
  // naming another macro id would need cycle detection nothing has built
  // (ref/docs/macro-builder.md's "Not decided").
  const macroMatches = pickerMode === 'macroStep'
    ? []
    : (q ? macroList.filter(m => m.displayLabel.toLowerCase().includes(q)) : macroList);
  appendMacroPickerGroup(list, macroMatches);

  // Category ids uppercase directly to the catalogue's own real labels
  // today (checked against catalogue.json - "ship" -> "SHIP" etc.) - a
  // second /api/actions field carrying the real label was judged
  // unnecessary complexity for what this heuristic already gets right; see
  // this dispatch's own report if that ever stops being true.
  for (const [category, actions] of byCategory) {
    appendPickerGroup(list, category.toUpperCase() || 'OTHER', actions);
  }
  appendPickerGroup(list, 'OTHER CONTROLS', uncurated);

  if (filtered.length === 0 && macroMatches.length === 0) {
    const p = document.createElement('p');
    p.className = 'pickerGroupLabel';
    p.textContent = 'No matching controls.';
    list.appendChild(p);
  }
}

// Which verb the picker's own pick commits to - 'primary' (the default,
// re-running the whole assign flow with its rename prompt) or 'longPress'
// (set from the slot sheet's own "Set long-press action…", skips the
// rename dialog entirely since a long-press carries no name of its own).
let pickerMode = 'primary';

async function openActionPicker(mode) {
  pickerMode = mode || 'primary';
  el('actionPickerSearch').value = '';
  // A macro step names any control, mapped or not, so the picker opens
  // already showing everything - the short list would hide exactly the
  // control a commander is about to go and map in Elite.
  el('actionPickerShowAll').checked = pickerMode === 'macroStep';
  if (pickerMode !== 'macroStep') {
    // The MACROS group's own data. Loaded here rather than at boot: it
    // changes whenever a macro is saved, and this is the moment it is
    // about to be read.
    await loadMacros();
  }
  await loadPickerActions();
  pushSheet('actionPicker');
}
el('actionPickerClose').addEventListener('click', () => {
  // popSheet restores whichever sheet this picker suppressed when it
  // opened - the step editor, when opened as pickerMode === 'macroStep',
  // or nothing at all otherwise. Closing without choosing must land back
  // where it started, not on a builder whose step editor silently
  // vanished.
  popSheet('actionPicker');
});
el('actionPickerSearch').addEventListener('input', renderPickerList);
el('actionPickerShowAll').addEventListener('change', loadPickerActions);

function onActionPicked(action) {
  popSheet('actionPicker');

  // Filling in a macro step's own control. Nothing is sent to the server
  // here at all - the step is part of an unsaved draft, and the builder's
  // Save is the only thing that posts.
  if (pickerMode === 'macroStep') {
    const step = editingContext.list[editingContext.index];
    if (step) {
      if (stepKindOf(step) === 'pressUntil') step.pressUntil = action.actionName;
      else step.press = action.actionName;
    }
    renderStepEditor();
    refreshStepListsForCurrentEdit();
    return;
  }

  if (pickerMode === 'longPress') {
    // No rename dialog - a long-press has no override label of its own
    // (button-naming.md's own "Undecided", not yet answered).
    postSlotLongPress(editorSlotIndex, action.actionName, null);
    return;
  }

  // Pre-filled with the action's own default label - button-naming.md's
  // flow. OK unchanged stores no override at all (null), so a later
  // curated-label improvement still reaches this button; only a genuine
  // edit becomes one.
  openRenameDialog(action.displayLabel, async finalText => {
    const label = finalText === action.displayLabel || finalText.trim() === '' ? null : finalText;
    await postSlotAssign(editorSlotIndex, action.actionName, null, label);
  });
}

// Putting a macro on a button. The same two commit paths an action has,
// and the same rename flow - a macro's own name is the default label, and
// only a genuine edit becomes an override, so renaming the macro later
// still reaches a button whose commander accepted that default.
function onMacroPicked(macro) {
  popSheet('actionPicker');

  if (pickerMode === 'longPress') {
    postSlotLongPress(editorSlotIndex, null, macro.id);
    return;
  }

  openRenameDialog(macro.displayLabel, async finalText => {
    const label = finalText === macro.displayLabel || finalText.trim() === '' ? null : finalText;
    await postSlotAssign(editorSlotIndex, null, macro.id, label);
  });
}

// Exactly one of actionName/macroId is ever non-null - SlotEditEndpoint
// refuses both at once, and a layout slot can only hold one of the two.
async function postSlotAssign(slotIndex, actionName, macroId, label) {
  try {
    const res = await fetch('__API_SLOT_ASSIGN__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ page: currentPage, slot: slotIndex, action: actionName, macro: macroId, label }),
    });
    if (res.ok) {
      await loadPanel();
    } else {
      const body = await res.json().catch(() => null);
      showToast((body && body.error) || 'Could not assign that action.');
    }
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
}

async function postSlotLabel(slotIndex, label) {
  try {
    const res = await fetch('__API_SLOT_LABEL__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ page: currentPage, slot: slotIndex, label }),
    });
    if (res.ok) {
      await loadPanel();
    } else {
      showToast('Could not rename that button.');
    }
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
}

// Sets (actionName/macroId one non-null) or clears (both null) a slot's
// long-press action - one endpoint for both, same convention postSlotLabel
// already uses for the primary label.
async function postSlotLongPress(slotIndex, actionName, macroId) {
  try {
    const res = await fetch('__API_SLOT_LONGPRESS__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ page: currentPage, slot: slotIndex, action: actionName, macro: macroId }),
    });
    if (res.ok) {
      await loadPanel();
    } else {
      const body = await res.json().catch(() => null);
      showToast((body && body.error) || 'Could not set that long-press action.');
    }
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
}

// Turns a slot's tap into a hold-until-tapped-again, or back
// (ref/docs/latching-keys.md). One route with a boolean, matching
// postSlotLabel/postSlotLongPress's own set-or-clear convention.
async function postSlotLatch(slotIndex, latch) {
  try {
    const res = await fetch('__API_SLOT_LATCH__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ page: currentPage, slot: slotIndex, latch: latch }),
    });
    if (res.ok) {
      await loadPanel();
    } else {
      const body = await res.json().catch(() => null);
      showToast((body && body.error) || 'Could not change that button.');
    }
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
}

// Turns a slot's tap into a hold-while-pressed, or back
// (ref/docs/latching-keys.md's hold-to-thrust extension) - exact mirror of
// postSlotLatch above, targeting the hold route instead.
async function postSlotHold(slotIndex, hold) {
  try {
    const res = await fetch('__API_SLOT_HOLD__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ page: currentPage, slot: slotIndex, hold: hold }),
    });
    if (res.ok) {
      await loadPanel();
    } else {
      const body = await res.json().catch(() => null);
      showToast((body && body.error) || 'Could not change that button.');
    }
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
}

// Moves the button at fromIndex to toIndex - a SWAP when something is
// already there, a plain move when it is empty (SlotEditEndpoint.Move
// decides that, not this). One page only: currentPage is sent once and
// used for both ends, because crossing pages is deliberately not part of
// this gesture (ref/docs/editor.md).
async function postSlotMove(fromIndex, toIndex) {
  try {
    const res = await fetch('__API_SLOT_MOVE__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ page: currentPage, from: fromIndex, to: toIndex }),
    });
    if (res.ok) {
      await loadPanel();
    } else {
      const body = await res.json().catch(() => null);
      showToast((body && body.error) || 'Could not move that button.');
    }
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
}

// ---------------------------------------------------------------------
// Rename dialog (ref/docs/button-naming.md). Reached two ways - right
// after picking an action, or standalone from the slot sheet's Rename -
// each supplying its own default text and its own commit handler. Nothing
// is ever sent to the server until OK, so Cancel abandoning "whatever
// opened it" (the whole assignment in the first case, just the rename in
// the second - button-naming.md's "one principle, two starting points")
// falls out for free: Cancel simply discards the pending handler.
// ---------------------------------------------------------------------

let renameCommitHandler = null;

function openRenameDialog(initialText, onCommit) {
  renameCommitHandler = onCommit;
  el('renameInput').value = initialText || '';
  el('renameDialog').classList.remove('hidden');
  el('renameInput').focus();
}

el('renameCancel').addEventListener('click', () => {
  renameCommitHandler = null;
  el('renameDialog').classList.add('hidden');
});

// The label-fit rule (ref/docs/button-naming.md's "Fifteen characters, and
// the break the commander cannot type"): at most LABEL_MAX_LINES lines ONCE
// WRAPPED, each within LABEL_LINE_CAP characters unless it is a single word
// with nowhere to break, and no empty line.
// Mirrors LayoutValidator.LabelFitsBudget/WrapLabel exactly, against the SAME
// two numbers (substituted in above, never a second hand-typed pair) - checked
// here BEFORE the request ever leaves the device, so a name this dialog
// accepts is a name LayoutValidator.ValidateForSave then accepts too,
// closing the "accepted here, rejected by a toast at save" gap. Reasoned
// against the server contract, not driven - there is no headless browser in
// this suite (ref/docs/web-client.md's "What's tested, and what plainly
// isn't").
const LABEL_MAX_LINES = __LABEL_MAX_LINES__;
const LABEL_LINE_CAP = __LABEL_LINE_CAP__;

// The lines a label actually occupies: a typed break is hard, everything
// between two of them is greedily wrapped at whitespace. Surrounding
// whitespace is absorbed rather than refused - an on-screen keyboard appends
// a space when a word suggestion is tapped, and refusing over an invisible
// character is what cost the commander a ten-character name on 2026-09-09.
function wrapLabel(text) {
  const wrapped = [];
  for (const segment of text.split('\n')) {
    const words = segment.split(/\s+/).filter(w => w.length > 0);
    if (words.length === 0) { wrapped.push(''); continue; }
    let line = words[0];
    for (let i = 1; i < words.length; i++) {
      if (line.length + 1 + words[i].length <= LABEL_LINE_CAP) line += ' ' + words[i];
      else { wrapped.push(line); line = words[i]; }
    }
    wrapped.push(line);
  }
  return wrapped;
}

function labelFitsBudget(text) {
  const lines = wrapLabel(text);
  if (lines.length > LABEL_MAX_LINES) return false;
  for (const line of lines) {
    // Over the cap survives only where there is nowhere to wrap - one word
    // longer than a line, let through cramped rather than refused.
    if (line.length === 0 || (line.length > LABEL_LINE_CAP && line.includes(' '))) return false;
  }
  return true;
}

el('renameForm').addEventListener('submit', async e => {
  e.preventDefault();
  const finalText = el('renameInput').value;

  // Clearing back to null (blank, or the row's own default text, checked by
  // the commit handler itself) is always allowed regardless of budget - the
  // budget only ever governs a genuine override.
  if (finalText.trim() !== '' && !labelFitsBudget(finalText)) {
    showToast(`Keep it to ${LABEL_MAX_LINES} line(s) of ${LABEL_LINE_CAP} characters or fewer. It splits at the spaces by itself, so you do not need to type a break.`);
    return;
  }

  const handler = renameCommitHandler;
  renameCommitHandler = null;
  el('renameDialog').classList.add('hidden');
  if (handler) await handler(finalText);
});

// ---------------------------------------------------------------------
// The page bar: add a page, rename a page, set a page's visibility, delete
// a page, and reorder the tab bar (ref/docs/panels-and-pages.md). Five
// server verbs (__API_PAGE_ADD__/__API_PAGE_RENAME__/__API_PAGE_SHOWWHEN__/
// __API_PAGE_DELETE__/__API_PAGE_MOVE__), all going through
// renderTabs()/loadPanel() afterwards exactly like every other layout
// mutation on this page - GET __API_PANEL__ is always the one source of
// truth for what the tab row shows next.
// ---------------------------------------------------------------------

// #pageNameDialog's own commit handler - a lightweight parallel to
// renameCommitHandler above (see the sheet's own HTML comment for why it
// is not the same dialog reused).
let pageNameCommitHandler = null;

function openPageNameDialog(title, initialText, onCommit) {
  pageNameCommitHandler = onCommit;
  el('pageNameDialogTitle').textContent = title;
  el('pageNameInput').value = initialText || '';
  el('pageNameDialog').classList.remove('hidden');
  el('pageNameInput').focus();
}

el('pageNameCancel').addEventListener('click', () => {
  pageNameCommitHandler = null;
  el('pageNameDialog').classList.add('hidden');
});

el('pageNameForm').addEventListener('submit', async e => {
  e.preventDefault();
  const finalText = el('pageNameInput').value.trim();
  if (finalText === '') {
    showToast('Give the page a name first.');
    return;
  }

  const handler = pageNameCommitHandler;
  pageNameCommitHandler = null;
  el('pageNameDialog').classList.add('hidden');
  if (handler) await handler(finalText);
});

// -------------------------------------------------------------------
// Add a page: name, then panel size, then create.
// -------------------------------------------------------------------

let pendingNewPageName = null;

function openAddPageFlow() {
  openPageNameDialog('NAME THE NEW PAGE', '', async name => {
    pendingNewPageName = name;
    await openPageTemplatePicker();
  });
}

async function openPageTemplatePicker() {
  const ladder = el('pageTemplateRungLadder');
  ladder.innerHTML = '';
  el('pageTemplatePicker').classList.remove('hidden');
  try {
    const w = Math.round(innerWidth), h = Math.round(innerHeight);
    const res = await fetch(`__API_TEMPLATES__?w=${w}&h=${h}`, { credentials: 'same-origin' });
    if (!res.ok) return;
    const rungs = await res.json();
    for (const r of rungs) {
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'rung v-' + r.verdict;
      b.innerHTML = `${r.slotCount}`;
      b.addEventListener('click', () => onPageTemplateRungTap(r.templateId));
      ladder.appendChild(b);
    }
  } catch (e) {
    // The ladder just won't populate this time - Close is still available.
  }
}

el('pageTemplatePickerClose').addEventListener('click', () => {
  pendingNewPageName = null;
  el('pageTemplatePicker').classList.add('hidden');
});

async function onPageTemplateRungTap(templateId) {
  const name = pendingNewPageName;
  pendingNewPageName = null;
  el('pageTemplatePicker').classList.add('hidden');
  if (!name) return;

  try {
    const res = await fetch('__API_PAGE_ADD__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ name, templateId }),
    });
    const body = await res.json().catch(() => null);
    if (!res.ok) {
      showToast((body && body.error) || 'Could not add that page.');
      return;
    }
    currentPage = body.pageIndex;
    await loadPanel();
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
}

// -------------------------------------------------------------------
// Page settings: rename, and the visibility picker. Opened per-tab (see
// wireTabGesture above) for whichever page's tab was held, independent of
// currentPage.
// -------------------------------------------------------------------

// The closed vocabulary this picker offers - every showWhen token the
// shipped starter layout's own pages already use
// (src/LunaPanel.Server/definitions/starter-layout.json), not the whole of
// StatusVocabulary. ShowWhenConditionList ANDs whatever is selected
// together, the same composition the starter layout's own SRV/NOMAD pages
// already rely on (["InSrv","Vessel:testbuggy"] / ["InSrv","Vessel:lander01"]
// respectively) - so offering these as independent toggles, rather than a
// free-text field, lets a commander reproduce (or invent) any combination
// those pages use without ever risking a token this server cannot parse.
// Selecting both Vessel: tokens at once is left possible rather than
// guarded against: it is a contradiction (a vessel is never two types at
// once) but not a PARSE error, and this picker's job is to keep every
// token valid, not to reason about every combination's meaning.
const VISIBILITY_TOKENS = [
  { token: 'InMainShip', label: 'In the main ship' },
  { token: 'InFighter', label: 'In a fighter' },
  { token: 'InSrv', label: 'In an SRV' },
  { token: 'Vessel:testbuggy', label: 'SRV type: Scarab' },
  { token: 'Vessel:lander01', label: 'SRV type: Scorpion' },
  { token: 'OnFoot', label: 'On foot' },
];

let pageSettingsPageIndex = null;
let pageSettingsShowWhen = [];

function renderPageVisibilityPicker() {
  const picker = el('pageVisibilityPicker');
  picker.innerHTML = '';
  for (const { token, label } of VISIBILITY_TOKENS) {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'rung' + (pageSettingsShowWhen.includes(token) ? ' active' : '');
    b.textContent = label;
    b.addEventListener('click', () => togglePageVisibilityToken(token));
    picker.appendChild(b);
  }
}

async function togglePageVisibilityToken(token) {
  const next = pageSettingsShowWhen.includes(token)
    ? pageSettingsShowWhen.filter(t => t !== token)
    : pageSettingsShowWhen.concat([token]);

  await postShowWhen(next, false, null);
}

// force=false first, always - a conflict is discovered, never assumed. When
// the response carries a conflict, the commander is asked by name before
// anything is taken over; declining leaves both pages exactly as they were.
// conflictPageIndex is threaded through the forced resubmit purely so the
// success branch below can tell whether the page just cleared out from under
// someone is the one currently on screen - the same "refresh if editing the
// current page" check this already had, extended rather than duplicated.
async function postShowWhen(next, force, conflictPageIndex) {
  try {
    const res = await fetch('__API_PAGE_SHOWWHEN__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ page: pageSettingsPageIndex, showWhen: next, force }),
    });
    const body = await res.json().catch(() => null);
    if (!res.ok) {
      showToast((body && body.error) || 'Could not change that visibility setting.');
      return;
    }
    if (body && body.conflict) {
      if (!confirm(`Page "${body.conflict.pageName}" already auto-switches to this vessel context. Take it over from that page?`)) return;
      await postShowWhen(next, true, body.conflict.pageIndex);
      return;
    }
    pageSettingsShowWhen = next;
    renderPageVisibilityPicker();
    if (pageSettingsPageIndex === currentPage || conflictPageIndex === currentPage) await loadPanel();
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
}

function openPageSettingsSheet(pageIndex) {
  pageSettingsPageIndex = pageIndex;
  // lastPanelData only ever describes currentPage, so a page's current
  // showWhen for any OTHER page is not known here without a fetch - the
  // picker opens as "everything off" for a page not currently showing, and
  // the first toggle tapped will still read/save correctly since
  // togglePageVisibilityToken always POSTs a fresh array rather than a
  // diff. Reflecting an unseen page's true starting state would need a
  // dedicated GET this task did not add.
  pageSettingsShowWhen = (pageIndex === currentPage && lastPanelData && lastPanelData.showWhen) ? lastPanelData.showWhen.slice() : [];
  renderPageVisibilityPicker();
  // The server refuses to delete a layout's last remaining page
  // (PageEditEndpoint.DeletePage's own CannotDeleteLastPage check) - this
  // only avoids a round trip it would refuse anyway. pageNames always
  // describes the whole list regardless of which page is currently showing.
  const onlyOnePage = !!(lastPanelData && lastPanelData.pageNames && lastPanelData.pageNames.length <= 1);
  el('pageSettingsDelete').classList.toggle('hidden', onlyOnePage);
  el('pageSettingsDelete').disabled = onlyOnePage;
  el('pageSettingsSheet').classList.remove('hidden');
}

el('pageSettingsClose').addEventListener('click', () => {
  el('pageSettingsSheet').classList.add('hidden');
});

el('pageSettingsRename').addEventListener('click', () => {
  const index = pageSettingsPageIndex;
  // [2026-09-16] currentPageName, not pageNames[index]: pageNames excludes
  // folder pages, so its positions stopped being page indices - reading it
  // by page index here would have pre-filled the rename dialog with some
  // OTHER page's name the moment any page before this one owned a folder.
  const currentName = (lastPanelData && index === currentPage) ? (lastPanelData.currentPageName || '') : '';
  openPageNameDialog('RENAME THIS PAGE', currentName, async name => {
    try {
      const res = await fetch('__API_PAGE_RENAME__', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify({ page: index, name }),
      });
      const body = await res.json().catch(() => null);
      if (!res.ok) {
        showToast((body && body.error) || 'Could not rename that page.');
        return;
      }
      el('pageSettingsSheet').classList.add('hidden');
      await loadPanel();
    } catch (e) {
      showToast('Could not reach LunaPanel.');
    }
  });
});

// Delete this page outright (ref/docs/panels-and-pages.md), reversing this
// file's own earlier "no delete-page control" decision. Same confirm ->
// POST -> close-sheet -> refresh shape as #macroDelete's own click handler
// above. deletedIndex/currentPage arithmetic mirrors postPageMove's own
// shift-by-one-if-past-the-moved-range logic below: a delete shifts every
// LATER page down by one, so currentPage only needs adjusting when the
// deleted page was at or before it.
el('pageSettingsDelete').addEventListener('click', async () => {
  if (!confirm('Delete this page? Everything on it is lost.')) return;
  try {
    const res = await fetch('__API_PAGE_DELETE__', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin', body: JSON.stringify({ page: pageSettingsPageIndex }),
    });
    const body = await res.json().catch(() => null);
    if (!res.ok) { showToast((body && body.error) || 'Could not delete that page.'); return; }
    const deletedIndex = pageSettingsPageIndex;
    if (deletedIndex === currentPage) {
      // [2026-09-16] Clamped to a REAL page, not to body.pageCount - 1.
      // pageCount counts folder pages too, so the old arithmetic could land
      // on a folder's interior - showing a page that has no tab, with an
      // "up one level" bar, after deleting something else entirely. Every
      // surviving real index, shifted down by one where it sat after the
      // removed page; then the nearest one before the deleted position, or
      // the first remaining page if it was the first.
      const remaining = ((lastPanelData && lastPanelData.pageIndices) || [])
        .filter(i => i !== deletedIndex)
        .map(i => (i > deletedIndex ? i - 1 : i));
      const before = remaining.filter(i => i < deletedIndex);
      currentPage = before.length ? before[before.length - 1] : (remaining.length ? remaining[0] : 0);
    } else if (deletedIndex < currentPage) {
      currentPage -= 1;
    }
    el('pageSettingsSheet').classList.add('hidden');
    await loadPanel();
  } catch (e) { showToast('Could not reach LunaPanel.'); }
});

// ---------------------------------------------------------------------
// Settings gear - colour pane (ref/docs/theme.md's colour chain). Built as
// a shell with room for more panes; device management lands on this same
// surface next dispatch, not built here.
// ---------------------------------------------------------------------

const PALETTE = __PALETTE_JSON__;
// Background-only darker set (ref/docs/theme.md's "Known limit, not fixed
// here: every offered background is a bright one"). Every other role keeps
// reading PALETTE, unchanged - see openColourPicker's role check below.
const DARK_PALETTE = __DARK_PALETTE_JSON__;
const SOURCE_LABEL = {
  Matched: 'Matched from your HUD',
  Stock: 'Elite orange (no HUD colours found)',
  Override: 'Your own choice',
};

let themeState = null;
let pickerRole = null;

function makeSwatchButton(hex, label) {
  const b = document.createElement('button');
  b.type = 'button';
  b.className = 'swatch';
  b.style.background = hex;
  b.title = label;
  b.setAttribute('aria-label', label);
  b.addEventListener('click', () => {
    el('colourPicker').classList.add('hidden');
    onSwatchPick(pickerRole, hex);
  });
  return b;
}

// Opens the two-group picker (ref/docs/web-client.md) for whichever role
// swatch was tapped. The HUD-derived group is populated fresh from the
// current themeState every time - it reflects the commander's live EDHM
// theme, not a value baked into the page at load time - and is hidden
// entirely when there's no EDHM at all, per the settings pane's own spec.
function openColourPicker(role) {
  pickerRole = role;

  const hudColours = (themeState && themeState.hudColours) || [];
  const hudGroup = el('pickerHudGroup');
  hudGroup.classList.toggle('hidden', hudColours.length === 0);
  const hudContainer = el('pickerHudSwatches');
  hudContainer.innerHTML = '';
  for (const c of hudColours) hudContainer.appendChild(makeSwatchButton(c.hex, c.label));

  // Background is the one role that gets the darker set instead of the
  // shared bright PALETTE - Border/Text/Lit/"all" are unchanged.
  const standardPalette = role === 'background' ? DARK_PALETTE : PALETTE;
  el('pickerStandardLabel').textContent = role === 'background' ? 'Dark shades' : 'Standard colours';
  const stdContainer = el('pickerStandardSwatches');
  stdContainer.innerHTML = '';
  for (const swatch of standardPalette) stdContainer.appendChild(makeSwatchButton(swatch.hex, swatch.name));

  el('colourPicker').classList.remove('hidden');
}

document.querySelectorAll('.roleSwatch').forEach(btn => {
  btn.addEventListener('click', () => openColourPicker(btn.dataset.role));
});
el('colourPickerClose').addEventListener('click', () => el('colourPicker').classList.add('hidden'));

function renderThemeState() {
  if (!themeState) return;
  el('colourSource').textContent = 'Current source: ' + (SOURCE_LABEL[themeState.sourceKind] || themeState.sourceKind);

  document.querySelector('.roleSwatch[data-role="border"]').style.background = themeState.border;
  document.querySelector('.roleSwatch[data-role="text"]').style.background = themeState.text;
  document.querySelector('.roleSwatch[data-role="lit"]').style.background = themeState.lit;
  // Shows the background currently in effect whether or not it was chosen -
  // a derived near-black tint is still what the commander is looking at.
  document.querySelector('.roleSwatch[data-role="background"]').style.background = themeState.background;

  // Advisory, never a refusal (ref/docs/theme.md's contrast section). The
  // server names the roles; the sentence is built here so the wording can
  // change without a new API field.
  const warning = el('colourWarning');
  const lost = themeState.lowContrast || [];
  warning.classList.toggle('hidden', lost.length === 0);
  if (lost.length > 0) {
    warning.textContent = lost.join(' and ') + (lost.length === 1 ? ' is' : ' are') +
      ' nearly the same as your background, so ' + (lost.length === 1 ? 'it' : 'they') +
      ' may be invisible on the panel. Your choice still applies.';
  }
}

async function loadTheme() {
  try {
    const res = await fetch('__API_THEME__', { credentials: 'same-origin' });
    if (!res.ok) return;
    themeState = await res.json();
    renderThemeState();
  } catch (e) {
    // The sheet just won't populate this time - loadPanel's own theme-vars
    // wiring is unaffected either way.
  }
}

// Picking a swatch IS the act of creating an override - there is no
// checkbox to tick first (ref/docs/button-naming.md's override principle:
// a resolved value must never be written in as though the commander chose
// it). Every role not being picked keeps its own current live colour,
// unless "one colour for everything" is used, which sets the three
// FOREGROUND roles explicitly at once - never the background, which would
// paint the panel a single flat rectangle.
//
// Background is the one role sent conditionally, and that is the same
// principle applied one level down: while no background has been chosen,
// null is sent, so changing a Border cannot quietly pin the derived ground
// as though the commander had picked it - and the ground goes on tracking
// Text the way it always has.
async function onSwatchPick(role, hex) {
  if (!themeState) return;
  const body = {
    border: role === 'all' ? hex : (role === 'border' ? hex : themeState.border),
    text: role === 'all' ? hex : (role === 'text' ? hex : themeState.text),
    lit: role === 'all' ? hex : (role === 'lit' ? hex : themeState.lit),
    background: role === 'background' ? hex : (themeState.backgroundChosen ? themeState.background : null),
  };

  const res = await fetch('__API_THEME__', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'same-origin',
    body: JSON.stringify(body),
  });
  if (res.ok) {
    themeState = await res.json();
    renderThemeState();
    await loadPanel();
  } else {
    showToast('Could not save that colour.');
  }
}

// The only way back to automatic resolution - absent means "resolve
// automatically", never "whatever was last resolved" (ref/docs/theme.md).
async function resetTheme() {
  try {
    const res = await fetch('__API_THEME_RESET__', { method: 'POST', credentials: 'same-origin' });
    if (res.ok) {
      themeState = await res.json();
      renderThemeState();
      await loadPanel();
    }
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
}

el('gearBtn').addEventListener('click', () => {
  el('settingsSheet').classList.remove('hidden');
  loadTheme();
});
el('settingsClose').addEventListener('click', () => el('settingsSheet').classList.add('hidden'));
el('resetTheme').addEventListener('click', resetTheme);

// ---------------------------------------------------------------------
// Settings gear - Panels pane (ref/docs/panels-and-pages.md): the rung
// ladder (moved off the front panel surface) and the "merging and
// expanding panels" toggle. The first time this sheet has held more than
// one pane, so this tab-switching is genuinely new logic here.
// ---------------------------------------------------------------------

// Pulled out of the click handler so the tray's "Build a macro" can reach
// the same pane the same way (see the #macros handling at the bottom of
// this script) - one path in, so a pane opened by the tray runs exactly the
// loads a tapped tab would.
function showSettingsPane(paneId) {
  document.querySelectorAll('.sheetTab').forEach(t => t.classList.toggle('active', t.dataset.pane === paneId));
  document.querySelectorAll('.sheetPane').forEach(p => p.classList.toggle('hidden', p.id !== paneId));
  if (paneId === 'panePanels') loadPanelsPane();
  if (paneId === 'paneBindings') loadBindingsStatus();
  if (paneId === 'paneMacros') loadMacrosPane();
  if (paneId === 'paneTiming') loadMacroTiming();
  if (paneId === 'paneDevices') loadDevicesPane();
  if (paneId === 'paneTransfer') loadTransferPane();
}

document.querySelectorAll('.sheetTab').forEach(tab => {
  tab.addEventListener('click', () => showSettingsPane(tab.dataset.pane));
});

function renderRungLadder(rungs) {
  const ladder = el('rungLadder');
  ladder.innerHTML = '';
  for (const r of rungs) {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'rung v-' + r.verdict + (lastPanelData && r.templateId === lastPanelData.templateId ? ' active' : '');
    b.innerHTML = `${r.slotCount}`;
    b.addEventListener('click', () => onRungTap(r.templateId));
    ladder.appendChild(b);
  }
}

// Tapping a rung applies it to the page actually showing, reloads the
// panel (the new cell sizes/slots), and re-populates the ladder so its
// active highlight follows the page's real current template rather than
// merely whichever button was tapped.
async function onRungTap(templateId) {
  try {
    const res = await fetch('__API_PANEL_TEMPLATE__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ page: currentPage, templateId }),
    });
    if (res.ok) {
      await loadPanel();
      await loadPanelsPane();
    } else {
      showToast('Could not change that panel.');
    }
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
}

// Loads BOTH panel settings from the one response - GET __API_PANEL_SETTINGS__
// already returns mergeExpand and showMacroStepResults together, so this is
// called from both panePanels' and paneMacros' own pane-open loaders rather
// than each pane running a separate fetch for its own field.
async function loadPanelSettings() {
  try {
    const res = await fetch('__API_PANEL_SETTINGS__', { credentials: 'same-origin' });
    if (!res.ok) return;
    const settings = await res.json();
    el('mergeExpandToggle').checked = settings.mergeExpand;
    el('showMacroStepResultsToggle').checked = settings.showMacroStepResults;
    el('autoSwitchEnabledToggle').checked = settings.autoSwitchEnabled;
    showMacroStepResults = settings.showMacroStepResults;
  } catch (e) {
    // The toggles just stay at their last known state this time.
  }
}

async function postPanelSettings() {
  try {
    await fetch('__API_PANEL_SETTINGS__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({
        mergeExpand: el('mergeExpandToggle').checked,
        showMacroStepResults: el('showMacroStepResultsToggle').checked,
        autoSwitchEnabled: el('autoSwitchEnabledToggle').checked,
      }),
    });
  } catch (e) {
    showToast('Could not save that setting.');
  }
}

el('mergeExpandToggle').addEventListener('change', postPanelSettings);

el('autoSwitchEnabledToggle').addEventListener('change', postPanelSettings);

el('showMacroStepResultsToggle').addEventListener('change', () => {
  showMacroStepResults = el('showMacroStepResultsToggle').checked;
  postPanelSettings();
});

// Tap-to-open, never hover (ref/docs/panels-and-pages.md's own heading,
// verbatim) - these are touch devices, and a hover tooltip is invisible on
// the exact hardware this ships to. Reuses the existing tap-triggered
// #toast rather than a second, parallel popup mechanism.
el('mergeExpandHelp').addEventListener('click', () => {
  showToast('On: shrinking a panel spills the extra buttons onto new panels automatically, and growing pulls them back. Off: each panel keeps its own arrangement and buttons never move between panels.');
});

el('showMacroStepResultsHelp').addEventListener('click', () => {
  showToast('On: pressing a macro-bound button shows a breakdown of what each step in it did afterwards. Off: the button just presses, with no breakdown sheet.');
});

el('autoSwitchEnabledHelp').addEventListener('click', () => {
  showToast('On: this device automatically switches pages when you change vessel - board the SRV, go on foot, and so on. Off: this device stays on whatever page it is already showing, even as you change vessel. Each paired device has its own copy of this setting.');
});

// Reset to default (ref/docs/reset-to-default.md). ONE confirm - not a
// second modal afterwards like the import sheet's own "keep it?" step,
// which the commander has already flagged as one dialog too many. The
// warning itself does the recoverability job that second modal would have:
// it names what is lost (every button, every page) and says plainly that
// undoing is possible, in the same breath as asking.
el('resetToDefault').addEventListener('click', async () => {
  if (!confirm("Reset all buttons on this device to default? Every button on every page returns to the starting layout, replacing what's there now. This can be undone.")) return;

  try {
    const res = await fetch('__API_LAYOUT_RESET__', { method: 'POST', credentials: 'same-origin' });
    if (!res.ok) {
      showToast('Could not reset this device\'s buttons.');
      return;
    }
  } catch (e) {
    showToast('Could not reach LunaPanel.');
    return;
  }

  el('settingsSheet').classList.add('hidden');
  showScreen('loadingScreen');
  await loadPanel();
  showToast('All buttons on this device were reset to default.');
});

async function loadPanelsPane() {
  try {
    const w = Math.round(innerWidth), h = Math.round(innerHeight);
    const res = await fetch(`__API_TEMPLATES__?w=${w}&h=${h}`, { credentials: 'same-origin' });
    if (res.ok) {
      renderRungLadder(await res.json());
    }
  } catch (e) {
    // The ladder just won't populate this time.
  }
  await loadPanelSettings();
}

// ---------------------------------------------------------------------
// Settings gear - Bindings pane (ref/docs/bindings-source.md's "It has to
// be visible"). Reports which preset is actually in effect and lets a
// commander re-run discovery on demand, rather than only ever finding out
// their panel is wrong from a wall of dead buttons.
// ---------------------------------------------------------------------

const BINDINGS_SOURCE_LABEL = {
  CommanderAuthored: 'your own preset',
  Stock: 'stock preset, shipped with the game',
  Fallback: 'highest version found (StartPreset did not resolve)',
};

function renderBindingsStatus(status) {
  if (status.sourceKind === 'NotFound') {
    el('bindingsStatus').textContent = 'No bindings file found - every button will show unbound.';
    return;
  }

  const label = BINDINGS_SOURCE_LABEL[status.sourceKind] || status.sourceKind;
  const version = status.version ? ('v' + status.version) : 'no version';
  let text = `${status.presetName} (${label}) — ${version} — ${status.boundActionCount}/${status.totalActionCount} actions bound`;
  if (status.presetNameMismatch) {
    text += ' (name mismatch - see diagnostics)';
  }
  el('bindingsStatus').textContent = text;
}

async function loadBindingsStatus() {
  try {
    const res = await fetch('__API_BINDINGS_STATUS__', { credentials: 'same-origin' });
    if (!res.ok) return;
    renderBindingsStatus(await res.json());
  } catch (e) {
    // The pane just won't populate this time.
  }
}

async function refreshBindings() {
  el('refreshBindings').disabled = true;
  try {
    const res = await fetch('__API_BINDINGS_REFRESH__', { method: 'POST', credentials: 'same-origin' });
    if (res.ok) {
      renderBindingsStatus(await res.json());
      // A refresh can change which actions are bound at all, which the
      // grid itself needs to reflect immediately - same "changes apply
      // right away" principle onSwatchPick already follows for colour.
      await loadPanel();
    } else {
      showToast('Could not refresh bindings.');
    }
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  } finally {
    el('refreshBindings').disabled = false;
  }
}

el('refreshBindings').addEventListener('click', refreshBindings);

// ---------------------------------------------------------------------
// Settings gear - Timing pane (ref/docs/macro-timing.md): how fast
// LunaPanel drives the keyboard for macros. Server-wide, unlike every other
// pane on this sheet - GET/POST __API_MACRO_TIMING__ carries no device id,
// and the values it returns are shared by every paired device.
// ---------------------------------------------------------------------

// Read from the server rather than hand-typed a second time (the same "ask
// the server rather than restate it" fix this file's own chrome-allowance
// section already applied to headerStrip/framePadding) - populated by
// loadMacroTiming(), used only to decide whether to show the warning below.
let timingHoldMinMs = null;
let timingGapMinMs = null;

// Verbatim from ref/docs/macro-timing.md's "The warning copy" - shown
// automatically (never tap-triggered) whenever the field it sits under is
// below its own measured minimum. Deliberately the exact text, not a
// paraphrase: the closing sentence ("Missed keystrokes are silent...") is
// what tells a commander to suspect this setting rather than hunt for a bug
// elsewhere.
const TIMING_BELOW_MINIMUM_WARNING =
  'Below the tested minimum. This may work fine on your machine — but if buttons start doing nothing at all, or a button that presses something several times lands short, raise this back up. Missed keystrokes are silent: nothing will tell you it happened.';

function checkTimingWarnings() {
  const holdMs = parseInt(el('holdMsInput').value, 10);
  el('holdMsWarning').textContent = TIMING_BELOW_MINIMUM_WARNING;
  el('holdMsWarning').classList.toggle('hidden', !(timingHoldMinMs !== null && !isNaN(holdMs) && holdMs < timingHoldMinMs));

  const gapMs = parseInt(el('gapMsInput').value, 10);
  el('gapMsWarning').textContent = TIMING_BELOW_MINIMUM_WARNING;
  el('gapMsWarning').classList.toggle('hidden', !(timingGapMinMs !== null && !isNaN(gapMs) && gapMs < timingGapMinMs));
}

// A timing change made somewhere else - the PC's own tray item ("Macro
// timing"), or another device's Timing pane - arriving on the live channel
// rather than waiting for this pane to be opened again
// (ref/docs/macro-timing.md). state.timing is an EDGE: it is present on
// exactly the push the save caused, so there is nothing to compare against
// and nothing at all to do on any other push.
//
// Setting .value from script does not fire 'change', so this can never loop
// back into saveMacroTiming() - a commander's own edit stays the only thing
// that ever posts.
//
// The measured minimums are deliberately not pushed (they are constants no
// save can change). On a device that has never opened this pane they are
// still null, so checkTimingWarnings() shows nothing until loadMacroTiming()
// fills them in on first open - which is the only case where it matters, and
// the pane is on screen by then.
function applyPushedTiming(timing) {
  el('holdMsInput').value = timing.holdMs;
  el('gapMsInput').value = timing.interPressGapMs;
  checkTimingWarnings();
}

async function loadMacroTiming() {
  try {
    const res = await fetch('__API_MACRO_TIMING__', { credentials: 'same-origin' });
    if (!res.ok) return;
    const timing = await res.json();
    el('holdMsInput').value = timing.holdMs;
    el('gapMsInput').value = timing.interPressGapMs;
    timingHoldMinMs = timing.holdMinMs;
    timingGapMinMs = timing.interPressGapMinMs;
    checkTimingWarnings();
  } catch (e) {
    // The pane just won't populate this time.
  }
}

async function saveMacroTiming() {
  try {
    const res = await fetch('__API_MACRO_TIMING__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({
        holdMs: parseInt(el('holdMsInput').value, 10),
        interPressGapMs: parseInt(el('gapMsInput').value, 10),
      }),
    });
    if (!res.ok) {
      const body = await res.json().catch(() => ({}));
      showToast(body.error || 'Could not save that setting.');
      return;
    }
    // The server's own effective values, echoed back - never assumed to be
    // whatever was just typed (same discipline resetTheme's own reload
    // already follows for colour).
    const timing = await res.json();
    el('holdMsInput').value = timing.holdMs;
    el('gapMsInput').value = timing.interPressGapMs;
    timingHoldMinMs = timing.holdMinMs;
    timingGapMinMs = timing.interPressGapMinMs;
    checkTimingWarnings();
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
}

// 'input' updates the warning live, as the commander types; 'change' (fires
// on blur/Enter) is what actually persists - the same split a numeric field
// needs that a checkbox's own single 'change' listener (#mergeExpandToggle)
// does not.
el('holdMsInput').addEventListener('input', checkTimingWarnings);
el('gapMsInput').addEventListener('input', checkTimingWarnings);
el('holdMsInput').addEventListener('change', saveMacroTiming);
el('gapMsInput').addEventListener('change', saveMacroTiming);

// Tap-to-open, never hover (ref/docs/panels-and-pages.md's own heading,
// verbatim - these are touch devices, and a hover tooltip is invisible on
// the exact hardware this ships to). Verbatim from
// ref/docs/macro-timing.md's own tooltip copy, which names the SYMPTOM
// rather than defining the term, plus the "slower but more reliable" trade
// the spec asks both tooltips to state plainly.
el('holdMsHelp').addEventListener('click', () => {
  showToast('Raise this first if button presses are being missed entirely. Elite checks the keyboard a few times a second; a key held for less time than that gap can be missed completely. Default 150 ms. Raising this makes macros slower but more reliable — usually the right trade if presses are being missed.');
});
el('gapMsHelp').addEventListener('click', () => {
  showToast('Raise this if a button that presses something several times lands short — stepping three menu items and only moving two. Too small a gap and Elite reads two presses as one key being held down. Default 100 ms. Raising this makes macros slower but more reliable — usually the right trade if repeated presses are landing short.');
});

// ---------------------------------------------------------------------
// The macro builder (ref/docs/macro-builder.md) - authoring a macro on the
// device, the way the editor already lets a commander put a single action
// on a slot. The server half shipped first; this is the other half.
//
// The commander asked for "a friendly, easily usable UI with tooltips and
// basic instructions", and question 1 of that page had already ruled that
// EVERY step kind is offered with nothing behind an "advanced" tier. Those
// two together decide the shape of everything below: friendliness is bought
// by EXPLAINING the powerful steps, never by hiding them. So every token
// picker leads with a plain name and a sentence
// (GET __API_MACROS_VOCABULARY__ carries both), every field has a
// tap-to-open "?" beside it, and the builder shows a plain read-back of
// what the macro will do plus roughly how long it will take.
//
// Three rules that are not negotiable, and are load-bearing here:
//   * A macro is never refused for uncertainty. Nothing below blocks a
//     save on a guess about whether a macro will work - not an unbound
//     action, not a huge repeat, not a pressUntil on a contextual toggle.
//     Those are NOTES, in #macroNotes, and the commander decides.
//   * Intent, never resolution. The draft holds action names and condition
//     tokens - the grammar's own JSON, posted verbatim. Chords and bound
//     state are looked up for display on every render and never written.
//   * On a contextual surface a wrong macro does not fail, it does
//     something else successfully (LC17/LC18). Nothing here can prevent
//     that; what it can do is make what the commander built legible before
//     they put it on a button, which is what the read-back is for.
//
// NOT driven by any automated test - no headless browser in this suite, the
// same limit as every other client behaviour on this page. The pins in
// PanelClientEndpointTests read the built page's text.
// ---------------------------------------------------------------------

// The one sentence this project uses for an action with no key in Elite,
// substituted from PressEndpoint.NotBoundInEliteAdvice rather than typed
// here. GET __API_MACROS__ already reports it per step for a SAVED macro;
// a step being edited has never been near a server response, and a second
// wording for the same situation is exactly what macro-builder.md's
// question 5 set out to make impossible.
// Double-quoted deliberately: the substituted sentence contains an
// apostrophe ("the game's Controls options"), which a single-quoted
// literal would end early - a syntax error taking the whole client's
// script with it, on a page no test in this suite executes.
const NOT_BOUND_ADVICE = "__NOT_BOUND_ADVICE__";

// What a waitForEdge step gets when it names no timeout of its own
// (MacroTimingDefaults.DefaultWaitForEdgeTimeout, measured live at O28), so
// the builder pre-fills the number the runner would really have used.
const EDGE_TIMEOUT_DEFAULT_MS = __EDGE_TIMEOUT_DEFAULT_MS__;

let macroVocabulary = null;
let macroList = [];
// Whether a macro-bound button's press opens the per-step result sheet
// afterwards (ref/docs/macro-builder.md's "What the builder shows after a
// run") - read once when the Macros pane loads, and again whenever the
// toggle itself changes, so onSlotTap/onSlotLongPress never have to fetch
// this on every single press.
let showMacroStepResults = false;
let macroActionsByName = new Map();
// The configured timing, re-read whenever the builder opens - never a
// constant. MacroRunner refuses to use a timer for its own lock for the
// same reason: this is a setting, and a constant here would be wrong the
// moment a commander changed it (ref/docs/macro-timing.md).
let macroTimingHoldMs = null;
let macroTimingGapMs = null;

// The macro being edited, as the grammar's own JSON: { id, name, steps }
// where each step is exactly the object that gets posted. There is
// deliberately no intermediate model - a second shape here is a second
// grammar, and the whole server half was built to avoid having one.
let macroDraft = null;
let macroDraftReadOnly = false;
let macroDraftSourceId = null;
// The staleness line (ref/docs/macro-builder.md, question 3): whether the
// macro currently open was copied from something that has since changed
// (or vanished), and what to call that source in the note. Read once from
// the row that opened the builder, not recomputed from macroDraft, which
// carries no idea where it was copied from.
let macroDraftIsStale = false;
let macroDraftSourceName = null;

// Which array a step-list view is currently pointed at: { list, isTopLevel }.
// topCtx always points at macroDraft.steps and is (re)created once, in
// openMacroBuilder, whenever a new macroDraft object is built - see that
// function's own remarks. armCtx points at whichever branch arm's array
// (step.then or step['else']) the #armEditor sheet currently has open, and
// is null whenever that sheet is not in use (including for a macro with no
// branch step at all). One level of nesting means at most one arm is ever
// open at a time - there is no stack of these.
let topCtx = null;
let armCtx = null;

async function loadMacroVocabulary() {
  if (macroVocabulary) return macroVocabulary;
  try {
    const res = await fetch('__API_MACROS_VOCABULARY__', { credentials: 'same-origin' });
    macroVocabulary = res.ok ? await res.json() : null;
  } catch (e) {
    macroVocabulary = null;
  }
  return macroVocabulary;
}

// Every bindable control, so a step can name one that is not mapped yet -
// the commander's own ruling on unbound actions (ref/docs/editor.md)
// applies to a step as much as to a slot.
async function loadMacroActions() {
  try {
    const res = await fetch('__API_ACTIONS__?all=true', { credentials: 'same-origin' });
    const rows = res.ok ? await res.json() : [];
    macroActionsByName = new Map(rows.map(a => [a.actionName, a]));
  } catch (e) {
    macroActionsByName = new Map();
  }
}

async function loadMacroTimingForEstimate() {
  try {
    const res = await fetch('__API_MACRO_TIMING__', { credentials: 'same-origin' });
    if (!res.ok) return;
    const timing = await res.json();
    macroTimingHoldMs = timing.holdMs;
    macroTimingGapMs = timing.interPressGapMs;
  } catch (e) {
    // The estimate says so rather than guessing - see renderMacroEstimate.
  }
}

async function loadMacros() {
  try {
    const res = await fetch('__API_MACROS__', { credentials: 'same-origin' });
    macroList = res.ok ? await res.json() : [];
  } catch (e) {
    macroList = [];
  }
  return macroList;
}

async function loadMacrosPane() {
  // loadKeyList belongs here, not inside the key-capture picker itself:
  // keyLabelFor (used by describeStep and the step editor to show an
  // EXISTING pressKey step's name) needs keyList populated before this
  // pane's own macro list renders, and the key-capture flow no longer has
  // a call site of its own now that it resolves a key from a single
  // keydown instead of browsing a fetched list.
  await Promise.all([loadMacroVocabulary(), loadMacroActions(), loadMacroTimingForEstimate(), loadMacros(), loadKeyList(), loadPanelSettings()]);
  renderMacroList();
}

function renderMacroList() {
  const list = el('macroList');
  list.innerHTML = '';

  if (macroList.length === 0) {
    const p = document.createElement('p');
    p.className = 'paneIntro';
    p.textContent = 'Nothing here yet. Build one, or start from one that came with LunaPanel and change it.';
    list.appendChild(p);
    return;
  }

  for (const macro of macroList) {
    const row = document.createElement('button');
    row.type = 'button';
    // Degraded is marked in the list, BEFORE the macro is placed on a
    // button - the same rule the action picker's unbound rows follow. The
    // state comes from MacroKnowledgeBuilder via GET __API_MACROS__, never
    // recomputed here, so this row and the button it produces cannot
    // disagree (ref/docs/macro-builder.md, question 5).
    row.className = 'macroRow' + (macro.isDegraded ? ' degraded' : '');

    const text = document.createElement('span');
    text.className = 'macroRowText';

    const label = document.createElement('span');
    label.className = 'macroRowLabel';
    label.textContent = macro.displayLabel;
    text.appendChild(label);

    const meta = document.createElement('span');
    meta.className = 'macroRowMeta';
    meta.textContent = describeMacroMeta(macro);
    text.appendChild(meta);

    row.appendChild(text);
    row.addEventListener('click', () => openMacroBuilder(macro));
    list.appendChild(row);
  }
}

function describeMacroMeta(macro) {
  const parts = [];
  parts.push(macro.isShipped ? 'Came with LunaPanel' : 'Yours');
  parts.push(macro.steps.length === 1 ? '1 step' : macro.steps.length + ' steps');
  if (macro.isDegraded) parts.push('one of its controls has no key in Elite');
  if (macro.isStale) parts.push('copied from a macro that has since changed');
  return parts.join(' - ');
}

// ------------------------------------------------------------------
// Reading a step back in plain words. This is the thing that makes the
// no-tier ruling liveable: a commander looks at what they built and reads
// sentences, not grammar. It runs off the SAME vocabulary the pickers use,
// so a token's plain name is the one they picked it by.
//
// Deliberately NOT GET __API_MACROS__'s own steps[].summary field, which
// this client never renders: that one is written for an API consumer and
// spells raw tokens ("Only run if Docked and GuiFocus:NoFocus"). It also
// only exists for a macro that has already been saved, and a step being
// edited has not been.
// ------------------------------------------------------------------

function actionRowFor(name) {
  return macroActionsByName.get(name) || null;
}

function actionLabelFor(name) {
  const row = actionRowFor(name);
  return row ? row.displayLabel.replace(/\n/g, ' ') : name;
}

// The grammar's own internal spelling for a journal-event token
// (EdgeCondition.Prefix in Core) - macroVocabulary.journalEvents' own
// tokens already carry it, and waitForEdge/failOn store it verbatim, but
// pressUntil's condJournal field is the BARE event name (see
// MacroDefinition.ParsePressUntil's remarks), so this project needs both
// forms and a name for the seam between them.
const JOURNAL_TOKEN_PREFIX = 'Journal:';

function tokenLabelFor(token) {
  if (!macroVocabulary || !token) return token;
  let bare = token;
  let negated = false;
  if (bare.charAt(0) === '!') { negated = true; bare = bare.slice(1); }

  let label = bare;
  const flag = macroVocabulary.flags.find(f => f.name === bare);
  if (flag) {
    label = flag.label;
  } else {
    const focus = macroVocabulary.guiFocus.find(g => g.token === bare);
    if (focus) {
      label = focus.label;
    } else {
      const event = macroVocabulary.journalEvents.find(e => e.token.toLowerCase() === bare.toLowerCase());
      if (event) label = event.label;
    }
  }

  return negated ? 'NOT ' + label : label;
}

function tokenListLabel(tokens) {
  if (!tokens || tokens.length === 0) return 'nothing chosen yet';
  return tokens.map(tokenLabelFor).join(', and ');
}

function tabLabelFor(name) {
  if (!macroVocabulary) return name;
  const tab = macroVocabulary.leftPanelTabs.find(t => t.tab === name);
  return tab ? tab.label : name;
}

// Seconds where seconds read better than milliseconds - "20 s" is a
// duration a person feels, "20000 ms" is a number they have to convert.
function readableMs(ms) {
  if (ms === null || ms === undefined) return '?';
  if (ms < 1000) return ms + ' ms';
  const seconds = ms / 1000;
  return (seconds >= 10 ? Math.round(seconds) : Math.round(seconds * 10) / 10) + ' s';
}

function stepKindOf(step) {
  if (!step) return '';
  for (const kind of ['press', 'pressKey', 'wait', 'gotoLeftPanelTab', 'require', 'waitFor', 'waitForEdge', 'pressUntil', 'branch']) {
    if (Object.prototype.hasOwnProperty.call(step, kind)) return kind;
  }
  return '';
}

// keyList loads lazily, same pattern as macroVocabulary/pickerActions -
// describeStep falls back to the raw Key_* name until it has arrived.
// Loaded from loadMacrosPane(), not from the key-capture picker itself -
// the picker resolves a key from a single keydown now, and never needed
// the full list at all.
let keyList = [];
async function loadKeyList() {
  if (keyList.length > 0) return keyList;
  try {
    const res = await fetch('__API_KEYS__', { credentials: 'same-origin' });
    keyList = res.ok ? await res.json() : [];
  } catch (e) {
    keyList = [];
  }
  return keyList;
}
function keyLabelFor(key) {
  const row = keyList.find(k => k.key === key);
  return row ? row.label : key;
}

function describeStep(step) {
  switch (stepKindOf(step)) {
    case 'press': {
      const times = (step.repeat || 1) === 1 ? 'once' : (step.repeat + ' times');
      return step.press
        ? 'Press ' + actionLabelFor(step.press) + ', ' + times
        : 'Press something - no control chosen yet';
    }
    case 'pressKey': {
      const times = (step.repeat || 1) === 1 ? 'once' : (step.repeat + ' times');
      return 'Press the ' + keyLabelFor(step.pressKey) + ' key, ' + times;
    }
    case 'wait':
      return 'Wait ' + readableMs(step.wait);
    case 'gotoLeftPanelTab':
      return 'Go to the left panel\'s ' + tabLabelFor(step.gotoLeftPanelTab) + ' tab';
    case 'require':
      return 'Only carry on if: ' + tokenListLabel(step.require);
    case 'waitFor':
      return 'Wait up to ' + readableMs(step.timeoutMs) + ' for: ' + tokenListLabel(step.waitFor);
    case 'waitForEdge': {
      let text = 'Wait up to ' + readableMs(step.timeoutMs === undefined ? EDGE_TIMEOUT_DEFAULT_MS : step.timeoutMs) +
        ' for the game to log: ' + tokenListLabel(step.waitForEdge);
      if (step.failOn && step.failOn.length > 0) text += ', giving up if it logs: ' + tokenListLabel(step.failOn);
      return text;
    }
    // Read-only, on purpose: this pass ships the branch ENGINE and no arm
    // editor (macro-builder.md - authoring arms means a step list inside a
    // step). A commander sees the shape of the choice and the size of each
    // arm; the arms themselves are edited by hand in the macro JSON.
    case 'branch': {
      const then = (step.then || []).length;
      const otherwise = (step['else'] || []).length;
      const plural = n => n + (n === 1 ? ' step' : ' steps');
      return 'If ' + tokenListLabel(step.branch) + ': ' + plural(then) + ', otherwise ' + plural(otherwise);
    }
    case 'pressUntil': {
      const what = step.pressUntil ? actionLabelFor(step.pressUntil) : 'something - no control chosen yet';
      const gate = step.condJournal
        ? 'the game logs: ' + tokenLabelFor(JOURNAL_TOKEN_PREFIX + step.condJournal)
        : tokenListLabel(step.cond);
      return 'Press ' + what + ' over and over until: ' + gate +
        ' (up to ' + (step.maxAttempts || 1) + ' tries, ' + readableMs(step.timeoutMs) + ' each)';
    }
    default:
      return 'Unrecognised step';
  }
}

// The chord, or the ONE sentence this project uses for an action with no
// key in Elite. A step that presses nothing gets nothing.
function stepNote(step) {
  const kind = stepKindOf(step);
  let action = null;
  if (kind === 'press') action = step.press;
  else if (kind === 'pressUntil') action = step.pressUntil;
  if (!action) return null;

  const row = actionRowFor(action);
  if (row && row.isBound) return { text: row.displayChord || 'Mapped in Elite', bad: false };
  return { text: NOT_BOUND_ADVICE, bad: true };
}

// ------------------------------------------------------------------
// The builder itself.
// ------------------------------------------------------------------

// Two different reasons a macro cannot be edited here, and they need
// different words on screen: a shipped macro is read-only everywhere,
// including on the PC, because a later update would silently shadow an
// edit; every macro is read-only on a device, because authoring moved to
// the PC entirely (ref/docs/macro-builder.md). Telling a commander their
// own macro "came with LunaPanel" would be a lie, so the reason is carried
// rather than collapsed into one boolean.
function macroReadOnlyReason(macro) {
  if (!CAN_AUTHOR_MACROS) return 'device';
  if (macro && macro.isShipped) return 'shipped';
  return null;
}

function openMacroBuilder(macro) {
  const readOnlyReason = macroReadOnlyReason(macro);
  macroDraftReadOnly = readOnlyReason !== null;
  macroDraftSourceId = macro ? macro.id : null;
  macroDraftIsStale = !!(macro && macro.isStale);
  macroDraftSourceName = macro ? (macro.sourceMacroName || null) : null;

  if (macro && macro.definition) {
    // The macro exactly as the grammar stores it, deep-copied so editing a
    // draft never mutates the list behind it. Read from `definition`, not
    // from `steps[]`, which is a display projection carrying no repeat, no
    // timeout and no condition tokens.
    macroDraft = {
      id: macro.isShipped ? null : macro.id,
      name: macro.definition.name || '',
      steps: JSON.parse(JSON.stringify(macro.definition.steps || [])),
    };
  } else {
    macroDraft = { id: null, name: '', steps: [] };
  }

  // The top-level list's own context, reused everywhere renderStepList/
  // stepMoveButton/openStepEditor need "which array" rather than always
  // reading macroDraft.steps directly - see renderStepList's own remarks.
  // Recreated on every open because macroDraft itself is a whole new object
  // above, not just its steps mutated in place. armCtx is cleared here too:
  // any arm editor left open from a previous macro must not survive into
  // this one.
  topCtx = { list: macroDraft.steps, isTopLevel: true };
  armCtx = null;

  el('macroBuilderTitle').textContent = macro ? (macroDraftReadOnly ? 'MACRO (READ ONLY)' : 'EDIT MACRO') : 'NEW MACRO';
  el('macroBuilderIntro').textContent = readOnlyReason === 'device'
    ? 'This is what this macro does, step by step. Changing it happens on the PC running LunaPanel - open the LunaPanel tray icon there and choose "Build a macro". You can still put this one on a button from here.'
    : readOnlyReason === 'shipped'
      ? 'This one came with LunaPanel, so it stays as it is - a later update can improve it, and an edit here would quietly shadow that. Make a copy and the copy is yours to change.'
      : 'Add steps in the order they should happen. Tap a step to change it, or use the arrows to move it. Nothing here is sent to Elite until you put this macro on a button and press it.';

  el('macroNameInput').value = macroDraft.name;
  el('macroNameInput').disabled = macroDraftReadOnly;
  el('macroAddStep').classList.toggle('hidden', macroDraftReadOnly);
  el('macroSave').classList.toggle('hidden', macroDraftReadOnly);
  // Copy is authoring too - it writes a new macro file - so it is offered
  // for a shipped macro on the PC and nowhere else. Without the second
  // condition a device would show "Make a copy I can edit" on every row and
  // be refused by the server on the tap.
  el('macroCopy').classList.toggle('hidden', !macroDraftReadOnly || !CAN_AUTHOR_MACROS);
  el('macroDelete').classList.toggle('hidden', macroDraftReadOnly || !macroDraft.id);

  renderMacroDraft();
  pushSheet('macroBuilder');
}

// The one list renderer for BOTH the top-level step list and an arm's own
// step list, parameterized by ctx = { list, isTopLevel } rather than the
// bare macroDraft.steps global a single-list builder never needed to name.
// isTopLevel picks which container this renders into (#macroStepList vs
// #armStepList - the two can never both be the visible one at once, since
// #armEditor sits on top of #macroBuilder in the sheet stack and suppresses
// it) and whether the whole-macro duration estimate/notes are recomputed
// afterward - an arm has neither a duration estimate nor its own notes list,
// both are whole-macro concepts computed over macroDraft.steps, which already
// recurses into any arm (stepDurationMs/collectStepNotes).
function renderStepList(ctx) {
  const list = el(ctx.isTopLevel ? 'macroStepList' : 'armStepList');
  list.innerHTML = '';

  if (ctx.list.length === 0) {
    const p = document.createElement('p');
    p.className = 'paneIntro';
    p.textContent = ctx.isTopLevel
      ? 'No steps yet. Most macros start by pressing a control.'
      : 'No steps on this side yet.';
    list.appendChild(p);
  }

  ctx.list.forEach((step, index) => {
    const row = document.createElement('div');
    row.className = 'stepRow';

    const main = document.createElement('button');
    main.type = 'button';
    main.className = 'stepRowMain';

    const text = document.createElement('span');
    text.className = 'stepRowText';
    text.textContent = (index + 1) + '. ' + describeStep(step);
    main.appendChild(text);

    const note = stepNote(step);
    if (note) {
      const noteEl = document.createElement('span');
      noteEl.className = 'stepRowNote' + (note.bad ? ' bad' : '');
      noteEl.textContent = note.text;
      main.appendChild(noteEl);
    }

    main.addEventListener('click', () => { if (!macroDraftReadOnly) openStepEditor(ctx, index); });
    row.appendChild(main);

    if (!macroDraftReadOnly) {
      const buttons = document.createElement('span');
      buttons.className = 'stepRowButtons';
      buttons.appendChild(stepMoveButton(ctx, index, -1, 'Move up'));
      buttons.appendChild(stepMoveButton(ctx, index, 1, 'Move down'));

      const remove = document.createElement('button');
      remove.type = 'button';
      remove.textContent = 'x';
      remove.setAttribute('aria-label', 'Remove this step');
      remove.addEventListener('click', () => {
        ctx.list.splice(index, 1);
        refreshStepLists(ctx);
      });
      buttons.appendChild(remove);
      row.appendChild(buttons);
    }

    list.appendChild(row);
  });

  if (ctx.isTopLevel) {
    renderMacroEstimate();
    renderMacroNotes();
  }
}

function stepMoveButton(ctx, index, delta, label) {
  const button = document.createElement('button');
  button.type = 'button';
  button.textContent = delta < 0 ? '↑' : '↓';
  button.setAttribute('aria-label', label);
  const target = index + delta;
  button.disabled = target < 0 || target >= ctx.list.length;
  button.addEventListener('click', () => {
    if (button.disabled) return;
    const moved = ctx.list.splice(index, 1)[0];
    ctx.list.splice(target, 0, moved);
    refreshStepLists(ctx);
  });
  return button;
}

// Re-renders whichever list(s) a change to ctx.list needs to be reflected
// in. The top level always needs a re-render even when ctx IS the arm -
// describeStep's branch case reads step.then/step['else'].length, so adding
// or removing a step inside an arm changes what the top-level row for that
// branch step itself says (e.g. "otherwise 3 steps" becoming "otherwise 4
// steps") even though the top-level array's own contents did not change.
// Never renders a sibling arm or the top level's OWN array contents wrong,
// since only ctx.list is ever mutated - this just keeps both DISPLAYS
// truthful about arrays that may or may not have just changed.
function refreshStepLists(ctx) {
  renderStepList(topCtx);
  if (!ctx.isTopLevel) renderStepList(ctx);
}

// Thin, zero-argument wrapper kept for every call site that only ever meant
// "the top-level list changed" (loading a macro, Save, etc.) - equivalent to
// renderStepList(topCtx), just under the name most of this file's other code
// already calls it by.
function renderMacroDraft() {
  renderStepList(topCtx);
}

// How long this step spends before the next one starts, in the two cases
// worth telling apart: everything going smoothly, and every bounded wait
// running out. Both come from the CONFIGURED hold and gap, never from a
// constant (ref/docs/macro-builder.md, question 4: "label, do not cap").
function stepDurationMs(step, hold, gap, worstCase) {
  switch (stepKindOf(step)) {
    case 'press':
    case 'pressKey': {
      const repeat = step.repeat || 1;
      return repeat * (step.holdMs || hold) + (repeat - 1) * gap;
    }
    case 'wait':
      return step.wait || 0;
    // Worst case is three presses - the left panel's four tabs cycle and
    // wrap, so nothing is ever more than three away. Often it is none at
    // all, because PanelTabTracker already knows where the panel is.
    case 'gotoLeftPanelTab':
      return worstCase ? (3 * hold + 2 * gap) : 0;
    case 'require':
      return 0;
    case 'waitFor':
      return worstCase ? (step.timeoutMs || 0) : 0;
    case 'waitForEdge':
      return worstCase ? (step.timeoutMs === undefined ? EDGE_TIMEOUT_DEFAULT_MS : step.timeoutMs) : 0;
    case 'pressUntil': {
      const attempts = step.maxAttempts || 1;
      return worstCase ? attempts * (hold + (step.timeoutMs || 0)) : hold;
    }
    // Only one arm ever runs, so the honest best case is the cheaper arm
    // and the honest worst case is the dearer one - not the sum, which
    // would describe a run that cannot happen. Without this case a branch
    // fell to `return 0` below and a macro made entirely of one (disembark)
    // would have been estimated at "about 0 ms, pressing up to 0 keys",
    // which is a number on screen that is simply false.
    case 'branch': {
      const arm = steps => (steps || []).reduce((sum, s) => sum + stepDurationMs(s, hold, gap, worstCase), 0);
      const thenMs = arm(step.then);
      const elseMs = arm(step['else']);
      return worstCase ? Math.max(thenMs, elseMs) : Math.min(thenMs, elseMs);
    }
    default:
      return 0;
  }
}

// How many keys a step presses at most. Extracted from renderMacroEstimate
// (2026-09-17) so a branch's arms can be counted recursively - the worse
// arm, for the same reason stepDurationMs takes the worse one.
function stepKeyCount(step) {
  switch (stepKindOf(step)) {
    case 'press':
    case 'pressKey':
      return step.repeat || 1;
    case 'gotoLeftPanelTab':
      return 3;
    case 'pressUntil':
      return step.maxAttempts || 1;
    case 'branch': {
      const arm = steps => (steps || []).reduce((sum, s) => sum + stepKeyCount(s), 0);
      return Math.max(arm(step.then), arm(step['else']));
    }
    default:
      return 0;
  }
}

function renderMacroEstimate() {
  const target = el('macroEstimate');
  if (macroTimingHoldMs === null || macroTimingGapMs === null) {
    target.textContent = 'LunaPanel could not read your key timing, so it cannot say how long this will take.';
    return;
  }

  let best = 0;
  let worst = 0;
  let keys = 0;
  for (const step of macroDraft.steps) {
    best += stepDurationMs(step, macroTimingHoldMs, macroTimingGapMs, false);
    worst += stepDurationMs(step, macroTimingHoldMs, macroTimingGapMs, true);
    keys += stepKeyCount(step);
  }

  let text = 'About ' + readableMs(Math.round(best)) + ', pressing up to ' + keys +
    (keys === 1 ? ' key' : ' keys') + ', at your current timing (' + macroTimingHoldMs + ' ms hold, ' +
    macroTimingGapMs + ' ms gap).';
  if (worst > best) {
    text += ' Up to ' + readableMs(Math.round(worst)) + ' if every step that waits for the game has to wait its full time.';
  }
  target.textContent = text;
}

// [2026-09-17] Recurses into a branch step's arms, not just the top level -
// so a step that happens to live inside a branch's "then"/"else" still gets
// its own note, and pressesSomething/checksFirst below still see it. Labels
// an arm step distinctly from a plain top-level one ("Step 1's Docked
// branch, step 2") rather than a bare number, since arm membership matters
// to a commander deciding which arm to open - conversational wording, but
// the same idea as this project's own server-side nested-step label
// (MacroDefinition.ParseStepArray's remarks: `step 2.then[1]`). Read-only:
// this walks arm content for DETECTION only and builds no editor for it -
// same scope line as describeStep's branch case.
function collectStepNotes(steps, notes, labelPrefix) {
  (steps || []).forEach((step, index) => {
    const kind = stepKindOf(step);
    const number = labelPrefix ? labelPrefix + ', step ' + (index + 1) : 'Step ' + (index + 1);

    if ((kind === 'press' && !step.press) || (kind === 'pressUntil' && !step.pressUntil)) {
      notes.push(number + ' has no control chosen yet, so this macro cannot be saved until it does.');
    }
    if ((kind === 'require' && (!step.require || step.require.length === 0)) ||
        (kind === 'waitFor' && (!step.waitFor || step.waitFor.length === 0)) ||
        (kind === 'waitForEdge' && (!step.waitForEdge || step.waitForEdge.length === 0))) {
      notes.push(number + ' has nothing to wait for yet, so this macro cannot be saved until it does.');
    }

    const action = kind === 'press' ? step.press : (kind === 'pressUntil' ? step.pressUntil : null);
    if (action) {
      const row = actionRowFor(action);
      if (row && !row.isBound) {
        notes.push(number + ' presses ' + actionLabelFor(action) + '. ' + NOT_BOUND_ADVICE);
      }
    }

    // The measured symptom, named where it applies rather than as a
    // general caution: a second UI_Select on the CONTACTS row cancels the
    // docking clearance the first one just won (live-checks LC18).
    if (kind === 'pressUntil') {
      notes.push(number + ' presses the same control again if the game has not caught up. On a control that toggles, pressing again may undo the first press.');
    }

    if (macroTimingHoldMs !== null && kind === 'press') {
      const ms = stepDurationMs(step, macroTimingHoldMs, macroTimingGapMs, false);
      if (ms >= 5000) {
        notes.push(number + ' alone spends about ' + readableMs(Math.round(ms)) +
          ' pressing keys. While a macro runs, another one is refused - and pressing this button again stops it.');
      }
    }

    if (kind === 'branch') {
      collectStepNotes(step.then, notes, number + '\'s ' + tokenListLabel(step.branch) + ' branch');
      collectStepNotes(step['else'], notes, number + '\'s otherwise branch');
    }
  });
}

// Same recursion as collectStepNotes, for the yes/no scans below - a step
// living inside either arm of a branch counts exactly as one at the top
// level does.
function macroHasStepMatching(steps, predicate) {
  return (steps || []).some(step => {
    if (predicate(step)) return true;
    return stepKindOf(step) === 'branch' &&
      (macroHasStepMatching(step.then, predicate) || macroHasStepMatching(step['else'], predicate));
  });
}

// Notes, never refusals. Every one of these names a symptom and stops
// there - "confidence is information to record, never a gate."
function renderMacroNotes() {
  const target = el('macroNotes');
  target.innerHTML = '';
  const notes = [];

  // The copy-to-edit staleness line (ref/docs/macro-builder.md, question 3):
  // a note, not a refusal - this copy still does exactly what it did when it
  // was made, whether or not the source it came from has since changed.
  if (macroDraftIsStale) {
    notes.push(macroDraftSourceName
      ? 'Copied from ' + macroDraftSourceName + ', which has changed since. This macro still does what it did when you copied it.'
      : 'Copied from a macro that no longer exists in this build. This macro still does what it did when you copied it.');
  }

  collectStepNotes(macroDraft.steps, notes, null);

  // Every macro LunaPanel ships opens by checking that no panel is
  // already up, and the reason is measured: on a contextual surface a
  // press that lands in the wrong place still succeeds, it just does
  // something else (LC17/LC18). Said once, as a note, never as a gate.
  //
  // [2026-09-17] Both scans now recurse into branch arms
  // (macroHasStepMatching) rather than looking only at the top level - the
  // shipped `disembark` macro moved every press and every require inside a
  // branch's arms, and a top-level-only scan saw neither, so this note went
  // silent for a macro that DOES check the game first, just inside its arms.
  const pressesSomething = macroHasStepMatching(macroDraft.steps, s => ['press', 'pressUntil', 'gotoLeftPanelTab'].includes(stepKindOf(s)));
  const checksFirst = macroHasStepMatching(macroDraft.steps, s => stepKindOf(s) === 'require');
  if (pressesSomething && !checksFirst) {
    notes.push('This macro does not check the game first. If a panel is already open when it runs, the presses land in that panel instead - and they will still look like they worked. An "Only run if..." step with "No panel open" is how the shipped macros avoid it.');
  }

  for (const note of notes) {
    const p = document.createElement('p');
    p.className = 'macroNote';
    p.textContent = note;
    target.appendChild(p);
  }
}

el('macroBuilderClose').addEventListener('click', () => popSheet('macroBuilder'));

// Decided once, at load, from a constant the server substituted - not per
// render and not from anything the page could be talked into recomputing.
el('macroNew').classList.toggle('hidden', !CAN_AUTHOR_MACROS);
el('macroAuthorOnPc').classList.toggle('hidden', CAN_AUTHOR_MACROS);

el('macroNew').addEventListener('click', async () => {
  await loadMacrosPane();
  openMacroBuilder(null);
});
el('macroNameInput').addEventListener('input', () => { macroDraft.name = el('macroNameInput').value; });

// [2026-09-09] Reworded (before-value: "...characters. Press Enter for a
// second line. You can rename it later..."). This field is a single-line
// input and a tablet's on-screen keyboard has no Enter key, so that
// sentence told the commander to make a gesture neither the field nor the
// device could accept. The break is found at a space now, by wrapLabel.
el('macroNameHelp').addEventListener('click', () => {
  showToast('This is what the button will say, so keep it to what fits: at most ' + LABEL_MAX_LINES + ' line(s) of ' + LABEL_LINE_CAP + ' characters. Put a space where the second line should start and it splits there by itself. You can rename it later without breaking any button already using it - buttons remember the macro, not its name.');
});
el('macroStepsHelp').addEventListener('click', () => {
  showToast('Steps run top to bottom, one after another. If a step cannot do its job - a control with no key, or a wait that runs out - the macro stops there and the rest do not happen.');
});

// ------------------------------------------------------------------
// Save, copy and delete.
// ------------------------------------------------------------------

async function saveMacroDraft() {
  try {
    const res = await fetch('__API_MACROS__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      // Posted verbatim as the grammar's own JSON. The name is read from
      // the field rather than the draft so a value typed and not yet
      // blurred still saves.
      body: JSON.stringify({ id: macroDraft.id, name: el('macroNameInput').value, steps: macroDraft.steps }),
    });
    if (!res.ok) {
      // The server's own wording, never invented here - and for a
      // malformed macro that wording already names the offending step.
      const body = await res.json().catch(() => null);
      showToast((body && body.error) || 'Could not save that macro.');
      return;
    }
    popSheet('macroBuilder');
    await loadMacrosPane();
    showToast('Saved.');
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
}
el('macroSave').addEventListener('click', saveMacroDraft);

// Copy-to-edit (ref/docs/macro-builder.md, question 3). The copy is a
// plain user macro from this moment: the shipped one is untouched, and a
// later release improving it will not reach the copy.
el('macroCopy').addEventListener('click', async () => {
  try {
    const res = await fetch('__API_MACROS_COPY__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ id: macroDraftSourceId, name: null }),
    });
    if (!res.ok) {
      const body = await res.json().catch(() => null);
      showToast((body && body.error) || 'Could not copy that macro.');
      return;
    }
    const created = await res.json();
    await loadMacrosPane();
    const copy = macroList.find(m => m.id === created.id);
    if (copy) openMacroBuilder(copy);
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
});

el('macroDelete').addEventListener('click', async () => {
  // Deleting a macro a button names does not break the layout - the
  // button renders as unknown and refuses at press time, which is what
  // this sentence says rather than pretending nothing is affected.
  if (!confirm('Delete this macro? Any button using it stops working until you put something else on it.')) return;
  try {
    const res = await fetch('__API_MACROS_DELETE__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ id: macroDraft.id }),
    });
    if (!res.ok) {
      const body = await res.json().catch(() => null);
      showToast((body && body.error) || 'Could not delete that macro.');
      return;
    }
    popSheet('macroBuilder');
    await loadMacrosPane();
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
});

// ------------------------------------------------------------------
// Picking a kind of step. ONE list, everything offered, ordered by how
// often it is needed, with the gated ones under their own heading -
// exactly the order and grouping GET __API_MACROS_VOCABULARY__ serves,
// never a second list typed here that could quietly stop offering a step
// kind the grammar gained.
// ------------------------------------------------------------------

function newStepOfKind(kind) {
  switch (kind) {
    case 'press': return { press: '', repeat: 1 };
    // Not reached from the "add a step" picker - renderStepKindList special-
    // cases 'pressKey' before ever calling this (it opens the key picker
    // instead, and the step it creates depends on what that finds). Still
    // handled here, like every other grammar member, for the same reason
    // gotoLeftPanelTab is: a shipped/copied macro's existing pressKey step
    // reads back through the same machinery this switch is part of.
    case 'pressKey': return { pressKey: '', repeat: 1 };
    case 'wait': return { wait: 250 };
    case 'gotoLeftPanelTab': return { gotoLeftPanelTab: 'Navigation' };
    case 'require': return { require: [] };
    case 'waitFor': return { waitFor: [], timeoutMs: 5000 };
    case 'waitForEdge': return { waitForEdge: [], failOn: [], timeoutMs: EDGE_TIMEOUT_DEFAULT_MS };
    // [SUPERSEDED 2026-09-17] Reachable from the "add a step" picker at the
    // top level now that a real arm editor exists (renderStepKindList's own
    // remarks) - previously excluded everywhere because a step with two
    // empty arms was exactly the shape the server refuses on save, with
    // nothing here to fill either one. Still reached, unfiltered, when
    // adding at the top level; still filtered back OUT of the picker when
    // adding inside an arm (renderStepKindList), since nesting stays capped
    // at one level.
    case 'branch': return { branch: [], then: [] };
    case 'pressUntil': return { pressUntil: '', cond: [], timeoutMs: 2000, maxAttempts: 5 };
    default: return null;
  }
}

function renderStepKindList(ctx) {
  // Whichever list "add a step" was opened against - used by this row's own
  // click handler below, AND by openKeyPicker's downstream success handlers
  // (renderKeyCaptureResult), since "Press a key" leaves this sheet before
  // it knows what step it is building. See pendingAddCtx's own remarks.
  pendingAddCtx = ctx;

  const list = el('stepKindList');
  list.innerHTML = '';
  if (!macroVocabulary) return;

  // branch is offered at the top level like any other kind, but never
  // inside an arm - nesting stays capped at one level (the engine's own
  // load-time rule, no branch inside a then/else arm). This is the client-
  // side half of that cap; MacroDefinition.ParseBranch enforces the real
  // limit at save time regardless, so this is belt-and-suspenders UX, not
  // the only guard.
  const offeredKinds = macroVocabulary.stepKinds.filter(k => ctx.isTopLevel || k.kind !== 'branch');

  let lastGroup = null;
  for (const kind of offeredKinds) {
    if (kind.group !== lastGroup) {
      lastGroup = kind.group;
      if (kind.group) {
        const heading = document.createElement('p');
        heading.className = 'pickerGroupLabel';
        heading.textContent = kind.group.toUpperCase();
        list.appendChild(heading);
      }
    }

    const row = document.createElement('button');
    row.type = 'button';
    row.className = 'stepKindRow';

    const label = document.createElement('span');
    label.className = 'stepKindLabel';
    label.textContent = kind.label;
    row.appendChild(label);

    const description = document.createElement('span');
    description.className = 'stepKindDescription';
    description.textContent = kind.description;
    row.appendChild(description);

    // Shown on the row itself, not behind a tap: the one step kind that
    // can fire the opposite action on a contextual surface should say so
    // before it is chosen, not after.
    if (kind.warning) {
      const warning = document.createElement('span');
      warning.className = 'stepKindWarning';
      warning.textContent = kind.warning;
      row.appendChild(warning);
    }

    row.addEventListener('click', () => {
      popSheet('stepKindPicker');
      // "Press a key" does not create a step directly the way every other
      // row here does - it opens the key picker first, and the step it
      // eventually creates depends on what that picker finds (see
      // openKeyPicker's own remarks).
      if (kind.kind === 'pressKey') {
        openKeyPicker();
        return;
      }
      const step = newStepOfKind(kind.kind);
      if (!step) return;
      ctx.list.push(step);
      refreshStepLists(ctx);
      openStepEditor(ctx, ctx.list.length - 1);
    });
    list.appendChild(row);
  }
}

// ------------------------------------------------------------------
// "Press a key" (ref/docs/macros.md): capture a real keypress directly off
// #keyCaptureBox, then either use a real control already bound to it (a
// normal `press` step, so a later rebind in Elite keeps working) or use the
// raw key anyway (`pressKey`, which never changes no matter what Elite does
// with that key). Replaces the old search-a-list-then-view-a-result-sheet
// flow (loadKeyList/keyMatchesQuery/renderKeyPickerList/onKeyPicked) - see
// git history for that version. keyLabelFor/keyList above are unrelated and
// stay: describeStep still uses keyLabelFor to show an EXISTING pressKey
// step's key name.
// ------------------------------------------------------------------

// Which list's "add a step" flow is in progress - set by renderStepKindList
// right before this sheet's own "Press a key" row can be clicked, since a
// pressKey/press step built here has to land in the SAME list the picker was
// opened against (the top level, or whichever arm), and this flow leaves
// #stepKindPicker (and therefore ctx as a local variable) behind before it
// knows what step it is building.
let pendingAddCtx = null;

function openKeyPicker() {
  const box = el('keyCaptureBox');
  box.textContent = 'Press a key…';
  el('keyCaptureButtons').innerHTML = '';
  el('keyCaptureResult').classList.add('hidden');
  pushSheet('keyPicker');
  box.focus();
}
el('keyPickerClose').addEventListener('click', () => popSheet('keyPicker'));

el('keyCaptureBox').addEventListener('keydown', async (event) => {
  // Unconditional, and the very first thing this handler does, before any
  // branching on which key it was: F5 would otherwise reload the browser,
  // Tab would move focus off this box, Space would scroll the page, and
  // Escape must remain a normal capturable key rather than an alternate way
  // to close this picker (Cancel, below, is the only way to do that).
  event.preventDefault();

  const domCode = event.code;
  let response = null;
  let outcome = 'ok';
  try {
    const res = await fetch('__API_ACTION_FOR_KEY__?domCode=' + encodeURIComponent(domCode), { credentials: 'same-origin' });
    if (res.ok) {
      response = await res.json();
    } else if (res.status === 400) {
      outcome = 'unrecognized';
    } else {
      outcome = 'failed';
    }
  } catch (e) {
    outcome = 'failed';
  }

  renderKeyCaptureResult(domCode, response, outcome);
});

function renderKeyCaptureResult(domCode, response, outcome) {
  const buttons = el('keyCaptureButtons');
  buttons.innerHTML = '';

  if (outcome === 'unrecognized') {
    // No Key_* name exists for this domCode at all (BrowserKeyCodeMap has no
    // entry for it) - there is nothing to build a step from, so this branch
    // offers no "use anyway" button, only Cancel.
    el('keyCaptureBox').textContent = 'Unsupported key';
    el('keyCaptureText').textContent = "This key isn't supported here - try a different one.";
  } else if (outcome === 'failed') {
    // Distinct from a genuine "nothing bound" response below: this is the
    // fetch throwing or coming back non-OK (expired cookie, network hiccup,
    // server error) - not the server successfully reporting
    // response.matched === false. A real failure disguised as "nothing
    // bound" is indistinguishable from a true no-match, same reasoning the
    // old onKeyPicked's checkFailed branch relied on. No resolvedKey comes
    // back from the network on this path, so the raw domCode is used as the
    // pressKey step's key name instead - it won't match a Scancodes Key_*
    // name for every physical key, but it's the only identifier available
    // when the server couldn't be reached to resolve one at all.
    el('keyCaptureBox').textContent = domCode;
    el('keyCaptureText').textContent = "Couldn't check Elite's bindings right now. Try again, or use this key anyway.";
    buttons.appendChild(pickButton('Use ' + domCode + ' anyway', () => {
      const step = { pressKey: domCode, repeat: 1 };
      pendingAddCtx.list.push(step);
      popSheet('keyPicker');
      refreshStepLists(pendingAddCtx);
      openStepEditor(pendingAddCtx, pendingAddCtx.list.length - 1);
    }));
  } else if (response.matched) {
    el('keyCaptureBox').textContent = response.resolvedKeyLabel;
    el('keyCaptureText').textContent = 'The ' + response.resolvedKeyLabel + ' key is bound to ' + response.matchedLabel +
      ' in Elite. Use ' + response.matchedLabel + ' instead so a later rebind in the game keeps this step working, ' +
      'or use ' + response.resolvedKeyLabel + ' as-is if you\'d rather this step never change.';
    // A match is an offer, not a decision made for the commander - they may
    // have a real reason to want the raw key regardless (testing, a control
    // they deliberately don't want tied to this step), so "use the raw key
    // anyway" stays available here too, not only in the no-match/failed
    // branches below.
    buttons.appendChild(pickButton('Use ' + response.resolvedKeyLabel, () => {
      const step = { pressKey: response.resolvedKey, repeat: 1 };
      pendingAddCtx.list.push(step);
      popSheet('keyPicker');
      refreshStepLists(pendingAddCtx);
      openStepEditor(pendingAddCtx, pendingAddCtx.list.length - 1);
    }));
    buttons.appendChild(pickButton('Use ' + response.matchedLabel + ' instead', () => {
      // The EXISTING control-picker step shape, not a new code path: this
      // produces a plain `press` step naming the matched action, identical
      // to what choosing that same control from the action picker would
      // create.
      const step = newStepOfKind('press');
      step.press = response.matched;
      pendingAddCtx.list.push(step);
      popSheet('keyPicker');
      refreshStepLists(pendingAddCtx);
      openStepEditor(pendingAddCtx, pendingAddCtx.list.length - 1);
    }));
  } else {
    el('keyCaptureBox').textContent = response.resolvedKeyLabel;
    el('keyCaptureText').textContent = 'Nothing is currently bound to the ' + response.resolvedKeyLabel + ' key in Elite.';
    buttons.appendChild(pickButton('Use ' + response.resolvedKeyLabel + ' anyway', () => {
      const step = { pressKey: response.resolvedKey, repeat: 1 };
      pendingAddCtx.list.push(step);
      popSheet('keyPicker');
      refreshStepLists(pendingAddCtx);
      openStepEditor(pendingAddCtx, pendingAddCtx.list.length - 1);
    }));
  }

  // Always present once a result is shown: closes the picker without
  // creating any step. New relative to the old flow, whose only escape was
  // the header's Close button - pressing a different key while this box is
  // still focused already lets someone try again, so Cancel's only job is
  // to back all the way out.
  buttons.appendChild(pickButton('Cancel', () => {
    popSheet('keyPicker');
  }));

  el('keyCaptureResult').classList.remove('hidden');
}

el('macroAddStep').addEventListener('click', async () => {
  await loadMacroVocabulary();
  renderStepKindList(topCtx);
  pushSheet('stepKindPicker');
});
el('stepKindPickerClose').addEventListener('click', () => popSheet('stepKindPicker'));

// The arm editor's own "Add a step" - opens the exact same #stepKindPicker
// sheet the top level uses, just against armCtx instead of topCtx, so
// 'branch' is filtered back out (renderStepKindList) and a new step lands in
// this arm's own array, never the top level's.
el('armAddStep').addEventListener('click', async () => {
  await loadMacroVocabulary();
  renderStepKindList(armCtx);
  pushSheet('stepKindPicker');
});
el('armEditorClose').addEventListener('click', () => {
  popSheet('armEditor');
  armCtx = null;
});

// ------------------------------------------------------------------
// Editing one step. Fields are built per kind, each with a tap-to-open
// "?" beside it - never a hover, since this is a touch device.
// ------------------------------------------------------------------

// { list, index }: which array the step currently open in #stepEditor lives
// in, and its position there. Replaces a single module-level int (this
// project's original shape, back when macroDraft.steps was the only list
// that could ever be edited) now that a step being edited might belong to
// an arm's own array instead - renderStepEditor reads
// editingContext.list[editingContext.index], never macroDraft.steps
// directly, so the same step editor serves both the top level and an arm.
let editingContext = { list: null, index: null };

// Re-renders whichever step LIST(s) need to reflect a change just made to
// the step currently open in #stepEditor - the generalized version of the
// old bare renderMacroDraft() call every field handler below used to make,
// back when editingContext.list was always macroDraft.steps and nothing
// else needed telling. Compares by array reference, not by ctx object
// identity, since editingContext only ever stores a list, never a ctx.
function refreshStepListsForCurrentEdit() {
  refreshStepLists(editingContext.list === topCtx.list ? topCtx : armCtx);
}

function macroField(labelText, helpText, control) {
  const wrap = document.createElement('div');
  wrap.className = 'macroField';

  const head = document.createElement('div');
  head.className = 'macroFieldHead';
  const label = document.createElement('span');
  label.textContent = labelText;
  head.appendChild(label);

  if (helpText) {
    const help = document.createElement('button');
    help.type = 'button';
    help.className = 'helpBtn';
    help.textContent = '?';
    help.setAttribute('aria-label', 'What does this do?');
    help.addEventListener('click', () => showToast(helpText));
    head.appendChild(help);
  }

  wrap.appendChild(head);
  wrap.appendChild(control);
  return wrap;
}

function numberField(value, min, onChange) {
  const input = document.createElement('input');
  input.type = 'number';
  input.min = String(min);
  input.step = '1';
  input.inputMode = 'numeric';
  input.value = String(value);
  input.addEventListener('change', () => {
    const parsed = parseInt(input.value, 10);
    // No cap of any kind, on purpose. A commander who genuinely wants
    // forty presses to drain a list has not been gated
    // (ref/docs/macro-builder.md, question 4) - the duration estimate on
    // the builder screen is what tells them what they have asked for.
    if (!isNaN(parsed) && parsed >= min) onChange(parsed);
    renderStepEditor();
    refreshStepListsForCurrentEdit();
  });
  return input;
}

// Seconds in the UI, milliseconds on the wire (step.holdMs, read by
// stepDurationMs() and PressStep/PressKeyStep's Hold on the server -
// MacroDefinition.OptionalPositiveInt requires a positive integer when the
// field is present at all). Blank means "no override": the field clears
// step.holdMs entirely rather than writing 0, so MacroRunner falls back to
// the commander's configured default hold (Timing tab) exactly as it does
// for a step with no holdMs today.
function holdSecondsField(step) {
  const input = document.createElement('input');
  input.type = 'number';
  input.min = '0';
  input.step = '0.1';
  input.inputMode = 'decimal';
  input.placeholder = 'Default';
  input.value = step.holdMs ? String(step.holdMs / 1000) : '';
  input.addEventListener('change', () => {
    const parsed = parseFloat(input.value);
    if (input.value.trim() === '' || isNaN(parsed) || parsed <= 0) {
      delete step.holdMs;
    } else {
      step.holdMs = Math.round(parsed * 1000);
    }
    renderStepEditor();
    refreshStepListsForCurrentEdit();
  });
  return input;
}

function pickButton(text, onClick) {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'macroPickBtn';
  button.textContent = text;
  button.addEventListener('click', onClick);
  return button;
}

function tokenChips(tokens, onRemove) {
  const wrap = document.createElement('div');
  wrap.className = 'macroTokenChips';
  (tokens || []).forEach((token, index) => {
    const chip = document.createElement('button');
    chip.type = 'button';
    chip.className = 'macroChip';
    // Plain name first, raw token after it in small type - available,
    // never the first thing anyone sees.
    chip.appendChild(document.createTextNode(tokenLabelFor(token) + ' x'));
    const raw = document.createElement('span');
    raw.className = 'chipToken';
    raw.textContent = token;
    chip.appendChild(raw);
    chip.setAttribute('aria-label', 'Remove ' + token);
    chip.addEventListener('click', () => { onRemove(index); });
    wrap.appendChild(chip);
  });
  return wrap;
}

const HELP_REPEAT = 'How many times in a row this control is pressed. Elite needs a gap between presses to see them as separate - LunaPanel leaves that gap for you, which is why more presses take longer.';
const HELP_HOLD = 'How long each press is held down, in seconds. Leave blank to use your default hold time, set on the Timing tab.';
const HELP_WAIT = 'A flat pause before the next step. Use it when the game needs a moment to draw a menu that the next press depends on.';
const HELP_TIMEOUT = 'How long to keep waiting before giving up. If it runs out, the macro stops there and the steps after it do not happen - nothing is sent by mistake.';
const HELP_MAX_ATTEMPTS = 'The most times the control will be pressed while waiting for the game to catch up. It stops as soon as the game reports what you asked for.';
const HELP_CONTROL = 'Any of Elite’s controls, including ones you have not mapped yet. LunaPanel stores the control, never a key - so mapping it in Elite later makes this step start working with nothing to change here.';
const HELP_KEY = 'A physical key, pressed directly. Unlike a control, this never changes if you remap something in Elite later - it always presses exactly this key.';
const HELP_CONDITIONS = 'All of these have to be true at once. They are read from what Elite writes about your ship, so they cost nothing and send nothing.';
const HELP_PRESS_UNTIL_JOURNAL = 'Instead of checking how the game is, wait for one thing Elite writes into its journal - some things (a refuel finishing, for instance) show up there and nowhere in ongoing ship status. Picking one here replaces the checks above; you get one or the other, never both.';
const HELP_EDGE_SUCCEED = 'The macro waits for Elite to write one of these into its journal. Any one of them is enough.';
const HELP_EDGE_FAIL = 'If Elite writes one of these instead, stop waiting immediately rather than sitting there until the time runs out. This is how a refused docking request ends in a second rather than twenty.';
const HELP_TAB = 'LunaPanel keeps track of which tab the left panel is on, so this presses only as far as it needs to - often not at all. It is the safest way to reach a tab; walking there with plain presses is what goes wrong when the panel is not where you assumed.';

// gotoLeftPanelTab is not in macroVocabulary.stepKinds - it is deliberately
// not offered by the "add a step" picker (macro-builder.md, question 1,
// superseded 2026-09-12) - but a macro that already has one (a shipped
// macro, or a copy of one) still opens and edits it here, so the editor
// needs its own copy of the about text the picker would otherwise supply.
const GOTO_LEFT_PANEL_TAB_ABOUT = 'Move the open left panel to a tab. LunaPanel tracks where the panel is, so this presses only as far as it needs to - often not at all.';

// [SUPERSEDED 2026-09-17] Before-value: 'Checks the game and runs one of two
// sets of steps. Only one side ever runs. The steps inside each side cannot
// be edited here yet - change them in the macro file itself.' branch is
// offered by the picker now (a real arm editor exists), and macroVocabulary
// normally supplies its description row - this constant only still fires as
// a fallback for the moment before that vocabulary has finished loading, the
// same race GOTO_LEFT_PANEL_TAB_ABOUT's own comment does not need to name
// because that kind is never IN macroVocabulary at all. Wording corrected to
// match: no longer claims the arms cannot be edited here.
const BRANCH_ABOUT = 'Checks the game and runs one of two sets of steps. Only one side ever runs.';

function renderStepEditor() {
  const step = editingContext.list[editingContext.index];
  if (!step) return;
  const kind = stepKindOf(step);
  const kindRow = macroVocabulary ? macroVocabulary.stepKinds.find(k => k.kind === kind) : null;

  el('stepEditorTitle').textContent = 'STEP ' + (editingContext.index + 1);
  el('stepEditorAbout').textContent = kindRow
    ? kindRow.description
    : (kind === 'gotoLeftPanelTab' ? GOTO_LEFT_PANEL_TAB_ABOUT : (kind === 'branch' ? BRANCH_ABOUT : ''));
  el('stepEditorWarning').textContent = kindRow && kindRow.warning ? kindRow.warning : '';
  el('stepEditorWarning').classList.toggle('hidden', !(kindRow && kindRow.warning));

  const fields = el('stepEditorFields');
  fields.innerHTML = '';

  if (kind === 'press' || kind === 'pressUntil') {
    const current = kind === 'press' ? step.press : step.pressUntil;
    fields.appendChild(macroField(
      'Control',
      HELP_CONTROL,
      pickButton(current ? actionLabelFor(current) : 'Choose a control…', () => {
        // openActionPicker's own pushSheet suppresses #stepEditor and
        // restores it on whichever of the picker's two exits happens - a
        // pick, or a close.
        openActionPicker('macroStep');
      })));
  }

  if (kind === 'pressKey') {
    fields.appendChild(macroField('Key', HELP_KEY, pickButton(keyLabelFor(step.pressKey), () => {
      showToast('To change the key, remove this step and add a new "Press a key" step - LunaPanel checks Elite\'s bindings again each time, in case something changed.');
    })));
  }

  if (kind === 'press' || kind === 'pressKey') {
    fields.appendChild(macroField('How many times', HELP_REPEAT, numberField(step.repeat || 1, 1, v => { step.repeat = v; })));
    fields.appendChild(macroField('Hold (seconds)', HELP_HOLD, holdSecondsField(step)));
  }

  if (kind === 'wait') {
    fields.appendChild(macroField('Pause for (ms)', HELP_WAIT, numberField(step.wait || 0, 0, v => { step.wait = v; })));
  }

  if (kind === 'gotoLeftPanelTab') {
    const tabs = macroVocabulary ? macroVocabulary.leftPanelTabs : [];
    const wrap = document.createElement('div');
    for (const tab of tabs) {
      const row = document.createElement('button');
      row.type = 'button';
      row.className = 'tokenRow' + (step.gotoLeftPanelTab === tab.tab ? ' selected' : '');
      const label = document.createElement('span');
      label.className = 'tokenRowLabel';
      label.textContent = tab.label;
      row.appendChild(label);
      const meaning = document.createElement('span');
      meaning.className = 'tokenRowMeaning';
      meaning.textContent = tab.meaning;
      row.appendChild(meaning);
      row.addEventListener('click', () => {
        step.gotoLeftPanelTab = tab.tab;
        renderStepEditor();
        refreshStepListsForCurrentEdit();
      });
      wrap.appendChild(row);
    }
    fields.appendChild(macroField('Which tab', HELP_TAB, wrap));
  }

  // branch's own condition, editable exactly like require's own condition
  // list below - then two buttons opening #armEditor on step.then/step['else']
  // (creating whichever array is missing on first use; newStepOfKind('branch')
  // starts a brand new one with no 'else' key at all). A branch step can only
  // ever appear at the top level (nesting is capped at one - renderStepKindList
  // filters 'branch' out of the picker whenever ctx.isTopLevel is false), so
  // editingContext.list here is always topCtx.list, never an arm's own array.
  if (kind === 'branch') {
    if (!step.branch) step.branch = [];
    const condWrap = document.createElement('div');
    condWrap.appendChild(tokenChips(step.branch, index => { step.branch.splice(index, 1); renderStepEditor(); refreshStepListsForCurrentEdit(); }));
    condWrap.appendChild(pickButton('Add something to check…', () => {
      openTokenPicker('status', token => { step.branch.push(token); renderStepEditor(); refreshStepListsForCurrentEdit(); });
    }));
    fields.appendChild(macroField('If the game is', HELP_CONDITIONS, condWrap));

    const thenCount = (step.then || []).length;
    fields.appendChild(macroField('Then', null, pickButton(
      'Then (' + thenCount + (thenCount === 1 ? ' step' : ' steps') + ')…',
      () => {
        if (!step.then) step.then = [];
        openArmEditor(step, 'then');
      })));

    const elseCount = (step['else'] || []).length;
    fields.appendChild(macroField('Otherwise', null, pickButton(
      'Otherwise (' + elseCount + (elseCount === 1 ? ' step' : ' steps') + ')…',
      () => {
        if (!step['else']) step['else'] = [];
        openArmEditor(step, 'else');
      })));
  }

  if (kind === 'require' || kind === 'waitFor' || (kind === 'pressUntil' && !step.condJournal)) {
    const field = kind === 'require' ? 'require' : (kind === 'waitFor' ? 'waitFor' : 'cond');
    if (!step[field]) step[field] = [];
    const wrap = document.createElement('div');
    wrap.appendChild(tokenChips(step[field], index => { step[field].splice(index, 1); renderStepEditor(); refreshStepListsForCurrentEdit(); }));
    wrap.appendChild(pickButton('Add something to check…', () => {
      openTokenPicker('status', token => { step[field].push(token); renderStepEditor(); refreshStepListsForCurrentEdit(); });
    }));
    fields.appendChild(macroField('The game has to be', HELP_CONDITIONS, wrap));
  }

  // pressUntil-only: a single JOURNAL event gate, mutually exclusive with
  // the status condition list above - the server (MacroDefinition.
  // ParsePressUntil) requires exactly one of 'cond'/'condJournal', never
  // both, never neither, so picking one here always clears the other via
  // `delete` (not just emptying the array - an empty 'cond' array is still
  // PRESENT, which would fail the server's exactly-one check on save).
  // require/waitFor get no such control: they still only support status
  // conditions server-side.
  if (kind === 'pressUntil') {
    const journalWrap = document.createElement('div');
    if (step.condJournal) {
      journalWrap.appendChild(tokenChips([JOURNAL_TOKEN_PREFIX + step.condJournal], () => {
        delete step.condJournal;
        step.cond = [];
        renderStepEditor();
        refreshStepListsForCurrentEdit();
      }));
    } else {
      journalWrap.appendChild(pickButton('Add something to wait for…', () => {
        openTokenPicker('journal', token => {
          step.condJournal = token.startsWith(JOURNAL_TOKEN_PREFIX) ? token.slice(JOURNAL_TOKEN_PREFIX.length) : token;
          delete step.cond;
          renderStepEditor();
          refreshStepListsForCurrentEdit();
        });
      }));
    }
    fields.appendChild(macroField('Or wait for the game to log', HELP_PRESS_UNTIL_JOURNAL, journalWrap));
  }

  if (kind === 'waitForEdge') {
    if (!step.waitForEdge) step.waitForEdge = [];
    if (!step.failOn) step.failOn = [];

    const succeed = document.createElement('div');
    succeed.appendChild(tokenChips(step.waitForEdge, index => { step.waitForEdge.splice(index, 1); renderStepEditor(); refreshStepListsForCurrentEdit(); }));
    succeed.appendChild(pickButton('Add something to wait for…', () => {
      openTokenPicker('journal', token => { step.waitForEdge.push(token); renderStepEditor(); refreshStepListsForCurrentEdit(); });
    }));
    fields.appendChild(macroField('Wait until the game logs', HELP_EDGE_SUCCEED, succeed));

    const fail = document.createElement('div');
    fail.appendChild(tokenChips(step.failOn, index => { step.failOn.splice(index, 1); renderStepEditor(); refreshStepListsForCurrentEdit(); }));
    fail.appendChild(pickButton('Add something to give up on…', () => {
      openTokenPicker('journal', token => { step.failOn.push(token); renderStepEditor(); refreshStepListsForCurrentEdit(); });
    }));
    fields.appendChild(macroField('Or give up if it logs', HELP_EDGE_FAIL, fail));
  }

  if (kind === 'waitFor' || kind === 'waitForEdge' || kind === 'pressUntil') {
    const current = step.timeoutMs === undefined ? EDGE_TIMEOUT_DEFAULT_MS : step.timeoutMs;
    fields.appendChild(macroField('Give up after (ms)', HELP_TIMEOUT, numberField(current, 1, v => { step.timeoutMs = v; })));
  }

  if (kind === 'pressUntil') {
    fields.appendChild(macroField('At most this many presses', HELP_MAX_ATTEMPTS, numberField(step.maxAttempts || 1, 1, v => { step.maxAttempts = v; })));
  }
}

function openStepEditor(ctx, index) {
  editingContext = { list: ctx.list, index };
  renderStepEditor();
  pushSheet('stepEditor');
}
el('stepEditorClose').addEventListener('click', () => popSheet('stepEditor'));
el('stepEditorDone').addEventListener('click', () => popSheet('stepEditor'));

// Opens #armEditor on one branch step's own arm (armField is 'then' or
// 'else') - the sheet the branch's own "Then (N step(s))"/"Otherwise (M
// step(s))" buttons in renderStepEditor push. Reuses renderStepList exactly
// as the top level does, just against step[armField] instead of
// macroDraft.steps, via armCtx.
function openArmEditor(step, armField) {
  if (!step[armField]) step[armField] = [];
  armCtx = { list: step[armField], isTopLevel: false };
  el('armEditorTitle').textContent = armField === 'then' ? 'THEN' : 'OTHERWISE';
  renderStepList(armCtx);
  pushSheet('armEditor');
}

// ------------------------------------------------------------------
// The token picker. This is where the no-tier ruling is paid for: a
// commander who has never heard of GuiFocus reads "No panel open" and one
// sentence saying when that is true, with the raw token underneath for
// anyone who wants it. Both come from GET __API_MACROS_VOCABULARY__,
// which reads the real StatusVocabulary/JournalVocabulary tables - a token
// this picker offers is one the parser that receives it accepts.
// ------------------------------------------------------------------

let tokenPickerCommit = null;
let tokenPickerMode = 'status';

function openTokenPicker(mode, onPick) {
  tokenPickerMode = mode;
  tokenPickerCommit = onPick;
  el('tokenPickerTitle').textContent = mode === 'journal' ? 'SOMETHING THE GAME DOES' : 'HOW THE GAME IS';
  el('tokenPickerAbout').textContent = mode === 'journal'
    ? 'Elite writes a line in its journal when something happens - a docking request granted, an SRV launched. Pick the one that means the thing you are waiting for has actually happened.'
    : 'These are read straight from what Elite reports about your ship, several times a second. Nothing here presses anything.';
  // Negation is only meaningful for a status condition - a journal event
  // either arrived or did not, and the grammar has no "!" for it.
  el('tokenPickerNotRow').classList.toggle('hidden', mode === 'journal');
  el('tokenPickerNot').checked = false;
  el('tokenPickerSearch').value = '';
  renderTokenPicker();
  pushSheet('tokenPicker');
}

function renderTokenPicker() {
  const list = el('tokenPickerList');
  list.innerHTML = '';
  if (!macroVocabulary) return;

  const query = el('tokenPickerSearch').value.trim().toLowerCase();
  const rows = [];

  if (tokenPickerMode === 'journal') {
    for (const event of macroVocabulary.journalEvents) {
      rows.push({ token: event.token, label: event.label, meaning: event.meaning, aside: event.provenance === 'Cited' ? 'Never seen written by the game here - the spelling is taken from Frontier’s own notes.' : '' });
    }
  } else {
    for (const focus of macroVocabulary.guiFocus) {
      rows.push({ token: focus.token, label: focus.label, meaning: focus.meaning, aside: '' });
    }
    for (const flag of macroVocabulary.flags) {
      rows.push({
        token: flag.name,
        label: flag.label,
        meaning: flag.meaning,
        // Offered like any other and MARKED, never withheld - withholding
        // would be the never-gate rule broken in the picker instead of
        // the runner.
        aside: flag.confidence === 'Official' ? '' : (flag.confidence === 'CommunitySourced'
          ? 'Reported by the community, not in Frontier’s own manual.'
          : 'One source only - treat this one as a guess.'),
      });
    }
  }

  const filtered = query
    ? rows.filter(r => r.label.toLowerCase().includes(query) || r.token.toLowerCase().includes(query) || r.meaning.toLowerCase().includes(query))
    : rows;

  if (filtered.length === 0) {
    const p = document.createElement('p');
    p.className = 'pickerGroupLabel';
    p.textContent = 'Nothing matches that.';
    list.appendChild(p);
    return;
  }

  for (const row of filtered) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'tokenRow';

    const label = document.createElement('span');
    label.className = 'tokenRowLabel';
    label.textContent = row.label;
    button.appendChild(label);

    const meaning = document.createElement('span');
    meaning.className = 'tokenRowMeaning';
    meaning.textContent = row.meaning;
    button.appendChild(meaning);

    if (row.aside) {
      const aside = document.createElement('span');
      aside.className = 'tokenRowConfidence';
      aside.textContent = row.aside;
      button.appendChild(aside);
    }

    const raw = document.createElement('span');
    raw.className = 'tokenRowToken';
    raw.textContent = row.token;
    button.appendChild(raw);

    button.addEventListener('click', () => {
      const commit = tokenPickerCommit;
      const negate = tokenPickerMode !== 'journal' && el('tokenPickerNot').checked;
      tokenPickerCommit = null;
      popSheet('tokenPicker');
      if (commit) commit(negate ? '!' + row.token : row.token);
    });
    list.appendChild(button);
  }
}

el('tokenPickerClose').addEventListener('click', () => {
  tokenPickerCommit = null;
  popSheet('tokenPicker');
});
el('tokenPickerSearch').addEventListener('input', renderTokenPicker);

// ---------------------------------------------------------------------
// Settings gear - Devices pane (ref/docs/pairing-and-devices.md). Lists
// every paired device (the server itself never returns a token, in this
// response or any other), lets this device open pairing for a new one
// ("Add another device" - the authenticated route that gets used, since
// walking to the PC to read a number off the console is exactly the
// friction this whole project exists to remove), and forgets any OTHER
// device immediately. A device never forgets itself: its own row never
// gets a Forget button at all, and the server refuses the request anyway
// even if one were sent by hand.
// ---------------------------------------------------------------------

function formatDeviceTimestamp(iso) {
  try {
    return new Date(iso).toLocaleString();
  } catch (e) {
    return iso;
  }
}

function renderDeviceList(data) {
  const list = el('deviceList');
  list.innerHTML = '';
  for (const device of data.devices) {
    const row = document.createElement('div');
    row.className = 'deviceRow';

    const info = document.createElement('div');
    info.className = 'deviceInfo';
    const name = document.createElement('span');
    name.className = 'deviceName';
    name.textContent = `${device.name} (${device.deviceClass})`;
    const meta = document.createElement('span');
    meta.className = 'deviceMeta';
    meta.textContent = `Paired ${formatDeviceTimestamp(device.pairedAt)} - last seen ${formatDeviceTimestamp(device.lastSeenAt)}`;
    info.appendChild(name);
    info.appendChild(meta);
    row.appendChild(info);

    // Edit a specific paired device's real layout live, from the PC. Only
    // the host session ever draws this - a paired device's own screen never
    // reaches this branch, since IS_HOST is only ever true on the machine
    // LunaPanel is running on (DeviceAuthMiddlewareExtensions.IsHostRequest,
    // read once at page load into this same constant CAN_AUTHOR_MACROS/
    // IS_HOST already share). Skipped for the device already being edited -
    // its own row would otherwise offer to do again what "Editing: X" above
    // already says is happening.
    if (IS_HOST && device.deviceId !== EDITING_DEVICE_ID) {
      const editLiveBtn = document.createElement('button');
      editLiveBtn.type = 'button';
      editLiveBtn.className = 'editDeviceLiveBtn';
      editLiveBtn.textContent = 'Edit this device live';
      editLiveBtn.addEventListener('click', () => onEditDeviceLive(device.deviceId));
      row.appendChild(editLiveBtn);
    }

    // A device never forgets itself - no button on its own row at all,
    // not merely one that is hidden or disabled.
    if (device.deviceId !== data.selfDeviceId) {
      const forgetBtn = document.createElement('button');
      forgetBtn.type = 'button';
      forgetBtn.className = 'forgetDeviceBtn';
      forgetBtn.textContent = 'Forget';
      forgetBtn.addEventListener('click', () => onForgetDevice(device.deviceId));
      row.appendChild(forgetBtn);
    }

    list.appendChild(row);
  }
}

// Navigates this session onto the named device's own layout, live - a full
// reload with ?asDevice= set, matching how the tray's own hash-fragment
// entry points (#macros, #timing, #transfer) already reach this one page
// rather than inventing a second navigation mechanism just for this.
function onEditDeviceLive(deviceId) {
  location.href = location.pathname + '?' + ASDEVICE_PARAM + '=' + encodeURIComponent(deviceId);
}

async function loadDevicesPane() {
  try {
    const res = await fetch('__API_DEVICES__', { credentials: 'same-origin' });
    if (!res.ok) return;
    renderDeviceList(await res.json());
  } catch (e) {
    // The list just won't populate this time.
  }

  await refreshImportAvailability();
}

async function onForgetDevice(deviceId) {
  if (!confirm('Forget this device? It will lose access immediately.')) return;
  try {
    const res = await fetch('__API_DEVICES_FORGET__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ deviceId }),
    });
    if (res.ok) {
      await loadDevicesPane();
    } else {
      showToast('Could not forget that device.');
    }
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
}

// Opens pairing and shows the fresh code right here, on this device's own
// screen - never expects anyone to go and read it off the PC console.
async function onAddAnotherDevice() {
  try {
    const res = await fetch('__API_DEVICES_OPEN_PAIRING__', { method: 'POST', credentials: 'same-origin' });
    if (!res.ok) {
      showToast('Could not open pairing.');
      return;
    }
    const body = await res.json();
    const codeEl = el('devicePairingCode');
    codeEl.textContent = `Pairing code: ${body.code} (valid for 2 minutes)`;
    codeEl.classList.remove('hidden');
  } catch (e) {
    showToast('Could not reach LunaPanel.');
  }
}

el('addAnotherDevice').addEventListener('click', onAddAnotherDevice);

// Editing a specific paired device's real layout live, from the PC. Shows
// the persistent overlay banner and wires its exit control - a no-op when
// EDITING_DEVICE_ID is unset, which is every session that did not arrive
// here through "Edit this device live" above.
//
// GET /api/devices is fetched here too (through the same overridden
// window.fetch, so it carries ?asDevice= like every other call this page
// makes) purely to read back the target's own display name for the banner
// text - body.selfDeviceId is what the SERVER resolved asDevice to, which is
// what is actually shown rather than trusting the raw id straight off this
// page's own URL. A failed lookup (there should be none - the button that
// reaches this URL only ever names a device DeviceAuthMiddlewareExtensions
// has already validated) falls back to the raw id rather than leaving the
// banner blank.
async function initEditingDeviceBanner() {
  if (!EDITING_DEVICE_ID) return;

  el('editingDeviceBannerStop').addEventListener('click', () => {
    location.href = location.pathname;
  });

  let name = EDITING_DEVICE_ID;
  try {
    const res = await fetch('__API_DEVICES__', { credentials: 'same-origin' });
    if (res.ok) {
      const body = await res.json();
      const match = (body.devices || []).find(d => d.deviceId === body.selfDeviceId);
      if (match) name = match.name;
    }
  } catch (e) {
    // Falls back to the raw id set above.
  }

  el('editingDeviceBannerText').textContent = `Editing: ${name}`;
  el('editingDeviceBanner').classList.remove('hidden');
  // Offsets #panelScreen below the now-visible banner (see the CSS rule
  // beside #editingDeviceBannerStop for why this is a padding var rather
  // than a change to #chrome's own declared height). Measured, not a
  // hardcoded constant, so it tracks the banner's real rendered height
  // (font size, wrapping) rather than a second guess at it living here.
  document.documentElement.style.setProperty(
    '--editingBannerOffset',
    el('editingDeviceBanner').getBoundingClientRect().height + 'px');
}

// ---------------------------------------------------------------------
// Import and export, as files (ref/docs/transfer.md).
//
// The PC only. Every route below is refused to a device by HostOnlyRoutes,
// and the tab that reaches this pane is not drawn anywhere else - so
// nothing here checks IS_HOST a second time.
// ---------------------------------------------------------------------

// Which half of the pane opened the shared file input. Cleared as soon as
// it has been read, so a stray change event cannot import into the wrong
// half.
let transferImportKind = null;

// The device the last profile import wrote to, which is the only thing the
// undo button can put back. Null whenever there is nothing to undo, and the
// button is hidden on exactly that condition rather than on a separate flag
// that could disagree with it.
let transferUndoDeviceId = null;

async function loadTransferPane() {
  transferUndoDeviceId = null;
  el('transferUndo').classList.add('hidden');

  try {
    const res = await fetch('__API_TRANSFER_TARGETS__', { credentials: 'same-origin' });
    if (res.ok) {
      const body = await res.json();
      const targets = body.targets || [];
      // Only devices that HAVE an arrangement can be exported from; any of
      // them can be imported onto, including one that has paired and never
      // drawn a panel.
      fillTransferDevices(el('transferExportDevice'), targets.filter(t => t.hasLayout));
      fillTransferDevices(el('transferImportDevice'), targets);
    }
  } catch (e) {
    // The selects stay as they were; nothing here writes anything.
  }

  try {
    const res = await fetch('__API_MACROS__', { credentials: 'same-origin' });
    if (res.ok) fillTransferMacros(await res.json());
  } catch (e) {
    // Same.
  }
}

function fillTransferDevices(select, targets) {
  select.innerHTML = '';
  for (const t of targets) {
    const option = document.createElement('option');
    option.value = t.deviceId;
    option.textContent = t.name;
    select.appendChild(option);
  }
}

// Shipped macros are left out on purpose: the receiving copy of LunaPanel
// ships the identical macro under the identical id, so exporting one could
// only ever be a no-op or a stale shadow of a later fix.
function fillTransferMacros(macros) {
  const select = el('transferExportMacro');
  select.innerHTML = '';

  const all = document.createElement('option');
  all.value = '';
  all.textContent = 'All my macros';
  select.appendChild(all);

  for (const m of macros) {
    if (m.isShipped) continue;
    const option = document.createElement('option');
    option.value = m.id;
    option.textContent = m.displayLabel;
    select.appendChild(option);
  }
}

function showTransferResult(text) {
  const p = el('transferResult');
  p.textContent = text;
  p.classList.remove('hidden');
}

async function transferErrorOf(res) {
  try {
    const body = await res.json();
    return body.error || 'That did not work.';
  } catch (e) {
    return 'That did not work.';
  }
}

// Fetched and saved as a blob rather than navigated to, so a refusal is
// read out of the response and shown in the pane - a navigation would put
// the commander on a page of JSON with the panel gone.
async function downloadTransfer(url) {
  try {
    const res = await fetch(url, { credentials: 'same-origin' });
    if (!res.ok) {
      showTransferResult(await transferErrorOf(res));
      return;
    }

    const blob = await res.blob();
    const href = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = href;
    link.download = transferFileNameOf(res.headers.get('Content-Disposition'));
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(href);
    showTransferResult('Saved as ' + link.download + '.');
  } catch (e) {
    showTransferResult('Could not reach LunaPanel.');
  }
}

// The server names the file (TransferFile.FileNameFor); this only reads it
// back off the header, so the two cannot drift into two naming schemes.
function transferFileNameOf(header) {
  const match = header ? /filename="?([^";]+)"?/.exec(header) : null;
  return match ? match[1] : 'lunapanel-export.lunapanel.json';
}

function beginTransferImport(kind) {
  transferImportKind = kind;
  const input = el('transferFile');
  // Cleared before opening, not after: without this, choosing the same file
  // twice in a row fires no change event at all and looks like a dead button.
  input.value = '';
  input.click();
}

async function onTransferFileChosen() {
  const kind = transferImportKind;
  transferImportKind = null;

  const file = el('transferFile').files[0];
  if (!file || !kind) return;

  let text;
  try {
    text = await file.text();
  } catch (e) {
    showTransferResult('That file could not be read.');
    return;
  }

  if (kind === 'macros') {
    await postTransferImport('__API_TRANSFER_MACRO__', text, null);
    return;
  }

  const select = el('transferImportDevice');
  const deviceId = select.value;
  const name = select.selectedOptions.length ? select.selectedOptions[0].textContent : 'that device';
  // Warned before overwriting, the same rule the in-place import already
  // follows (ref/docs/layout-import.md) - and this one can say what the way
  // back is, because the undo button below is it.
  if (!confirm('Replace the buttons on ' + name + ' with the ones in this file? You can undo it straight afterwards.')) return;

  await postTransferImport('__API_TRANSFER_PROFILE__?deviceId=' + encodeURIComponent(deviceId), text, deviceId);
}

async function postTransferImport(url, text, undoDeviceId) {
  try {
    const res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: text,
    });

    if (!res.ok) {
      showTransferResult(await transferErrorOf(res));
      return;
    }

    const body = await res.json();
    transferUndoDeviceId = undoDeviceId;
    el('transferUndo').classList.toggle('hidden', !undoDeviceId);
    showTransferResult(describeTransferImport(body, undoDeviceId !== null));
    await loadPanel();
  } catch (e) {
    showTransferResult('Could not reach LunaPanel.');
  }
}

// Says what actually happened, including the parts that are not failures:
// macros this PC already had were left alone, and buttons naming a macro it
// does not have still work as buttons - they just refuse when pressed.
function describeTransferImport(body, wasProfile) {
  const parts = [wasProfile ? 'Buttons imported.' : 'Macros imported.'];

  if (body.macrosAdded) parts.push(body.macrosAdded + ' macro(s) added.');
  if (body.macrosAlreadyPresent) parts.push(body.macrosAlreadyPresent + ' were already here and were left as they are.');
  if (body.referencesToMissingMacros) parts.push(body.referencesToMissingMacros + ' button(s) name a macro this PC does not have.');
  if (!body.macrosAdded && !body.macrosAlreadyPresent && !wasProfile) parts.push('Nothing new arrived.');

  return parts.join(' ');
}

async function onTransferUndo() {
  if (!transferUndoDeviceId) return;

  try {
    const res = await fetch('__API_TRANSFER_UNDO__?deviceId=' + encodeURIComponent(transferUndoDeviceId), {
      method: 'POST',
      credentials: 'same-origin',
    });

    if (!res.ok) {
      showTransferResult(await transferErrorOf(res));
      return;
    }

    // One kept generation, so undoing twice would put the import back -
    // the button goes as soon as it has been used.
    transferUndoDeviceId = null;
    el('transferUndo').classList.add('hidden');
    showTransferResult('Put back the buttons that were there before.');
    await loadPanel();
  } catch (e) {
    showTransferResult('Could not reach LunaPanel.');
  }
}

el('tabTransfer').classList.toggle('hidden', !IS_HOST);
el('transferExportProfile').addEventListener('click', () =>
  downloadTransfer('__API_TRANSFER_PROFILE__?deviceId=' + encodeURIComponent(el('transferExportDevice').value)));
el('transferExportMacros').addEventListener('click', () =>
  downloadTransfer('__API_TRANSFER_MACRO__?id=' + encodeURIComponent(el('transferExportMacro').value)));
el('transferImportProfile').addEventListener('click', () => beginTransferImport('profile'));
el('transferImportMacros').addEventListener('click', () => beginTransferImport('macros'));
el('transferFile').addEventListener('change', onTransferFileChosen);
el('transferUndo').addEventListener('click', onTransferUndo);

// ---------------------------------------------------------------------
// Layout import and recovery (ref/docs/layout-import.md).
//
// Recovery at pairing and import from settings are ONE operation seen from
// two places - copy a layout no live device owns onto this device - so this
// is one sheet, one renderer and one POST, entered from two places rather
// than built twice. The server decides what is on offer and whether an
// automatic adoption was safe; this only draws it.
// ---------------------------------------------------------------------

// Set when the sheet is opened from a fresh pair, so closing it (or
// choosing "Start fresh") lands on the panel rather than back in settings.
let importIsPostPair = false;

function describeCandidate(candidate) {
  const cls = candidate.deviceClass && candidate.deviceClass !== 'unknown' ? candidate.deviceClass : null;
  if (!candidate.lastSeenAt) {
    return cls ? `${cls} - last used unknown` : 'last used unknown';
  }
  const when = formatDeviceTimestamp(candidate.lastSeenAt);
  return cls ? `${cls} - last used ${when}` : `last used ${when}`;
}

// Delete is deliberately gated on !postPair (renderImportList's own second
// argument) - the moment right after pairing is when a commander is choosing
// which old layout to recover, and offering "permanently delete" in the same
// breath as "recover" is the wrong moment for an irreversible action.
// Settings -> Import/export (postPair === false) is the unhurried place for
// cleanup instead.
function renderImportList(candidates, showDelete) {
  const list = el('importList');
  list.innerHTML = '';
  for (const candidate of candidates) {
    const row = document.createElement('div');
    row.className = 'importRow';

    const info = document.createElement('div');
    info.className = 'importInfo';
    const name = document.createElement('span');
    name.className = 'importName';
    name.textContent = candidate.name;
    const meta = document.createElement('span');
    meta.className = 'importMeta';
    meta.textContent = describeCandidate(candidate);
    info.appendChild(name);
    info.appendChild(meta);
    row.appendChild(info);

    const pick = document.createElement('button');
    pick.type = 'button';
    pick.className = 'importPickBtn';
    pick.textContent = 'Use these';
    pick.addEventListener('click', () => onImportPick(candidate));
    row.appendChild(pick);

    if (showDelete) {
      const del = document.createElement('button');
      del.type = 'button';
      del.className = 'importDeleteBtn';
      del.textContent = 'Delete';
      del.addEventListener('click', () => onImportDiscard(candidate));
      row.appendChild(del);
    }

    list.appendChild(row);
  }
}

function openImportSheet(candidates, postPair) {
  importIsPostPair = !!postPair;
  el('importIntro').textContent = postPair
    ? 'These buttons were left by a device that is no longer paired. Pick a set to copy onto this device, or start fresh.'
    : 'Copy the buttons from a device that is no longer paired onto this one. This replaces what is on this device now.';
  renderImportList(candidates, !importIsPostPair);
  el('importSheet').classList.remove('hidden');
}

function closeImportSheet() {
  el('importSheet').classList.add('hidden');
  if (importIsPostPair) {
    importIsPostPair = false;
    showScreen('loadingScreen');
    loadPanel();
  }
}

el('importClose').addEventListener('click', closeImportSheet);
el('importStartFresh').addEventListener('click', closeImportSheet);

async function onImportPick(candidate) {
  // Warns before overwriting, always - the arrangement on this device may
  // be the commander's own work, and the .bak LayoutStore keeps is what
  // makes saying yes recoverable rather than final.
  const warning = importIsPostPair
    ? `Copy the buttons from "${candidate.name}" onto this device?`
    : `Copy the buttons from "${candidate.name}" onto this device? This replaces the buttons on this device now. You can undo it straight afterwards.`;
  if (!confirm(warning)) return;

  try {
    const res = await fetch('__API_LAYOUT_IMPORT__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ deviceId: candidate.deviceId }),
    });
    if (!res.ok) {
      showToast('Could not import those buttons.');
      return;
    }
  } catch (e) {
    showToast('Could not reach LunaPanel.');
    return;
  }

  el('importSheet').classList.add('hidden');
  importIsPostPair = false;
  el('settingsSheet').classList.add('hidden');
  showScreen('loadingScreen');
  await loadPanel();
  announceAdoption(candidate.name);
}

// Genuinely irreversible - unlike onImportPick's own confirm, there is no
// undo offered afterward, so this MUST ask before it touches anything.
async function onImportDiscard(candidate) {
  if (!confirm(`Permanently delete the buttons saved for "${candidate.name}"? This cannot be undone.`)) return;

  try {
    const res = await fetch('__API_LAYOUT_IMPORT_DISCARD__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ deviceId: candidate.deviceId }),
    });
    if (!res.ok) {
      showToast('Could not delete those buttons.');
      return;
    }
  } catch (e) {
    showToast('Could not reach LunaPanel.');
    return;
  }

  const candidates = await loadImportCandidates();
  renderImportList(candidates, !importIsPostPair);
}

// Never adopt silently: automatic is fine, invisible is not. The undo is
// offered in the same breath as the announcement, and goes through
// LayoutStore's own kept generation rather than a second history.
function announceAdoption(name) {
  if (!confirm(`Buttons copied from "${name}". Keep them?\n\nCancel puts this device back to the buttons it had before.`)) {
    undoImport();
  } else {
    showToast(`Using the buttons from "${name}".`);
  }
}

async function undoImport() {
  try {
    const res = await fetch('__API_LAYOUT_IMPORT_UNDO__', { method: 'POST', credentials: 'same-origin' });
    if (!res.ok) {
      showToast('Could not undo that.');
      return;
    }
  } catch (e) {
    showToast('Could not reach LunaPanel.');
    return;
  }
  showScreen('loadingScreen');
  await loadPanel();
  showToast('Put back the previous buttons.');
}

async function loadImportCandidates() {
  try {
    const res = await fetch('__API_LAYOUT_IMPORT__', { credentials: 'same-origin' });
    if (!res.ok) return [];
    const body = await res.json();
    return body.candidates || [];
  } catch (e) {
    return [];
  }
}

async function refreshImportAvailability() {
  const candidates = await loadImportCandidates();
  el('importLayouts').classList.toggle('hidden', candidates.length === 0);
}

el('importLayouts').addEventListener('click', async () => {
  const candidates = await loadImportCandidates();
  if (candidates.length === 0) {
    showToast('There are no other devices to import from.');
    el('importLayouts').classList.add('hidden');
    return;
  }
  openImportSheet(candidates, false);
});

async function loadPanel() {
  let res;
  try {
    res = await fetch(panelUrl(), { credentials: 'same-origin' });
  } catch (e) {
    showScreen('loadingScreen');
    el('loadingScreen').textContent = 'Could not reach LunaPanel.';
    return;
  }

  if (res.status === 401) {
    el('pairError').textContent = '';
    showScreen('pairScreen');
    return;
  }

  if (!res.ok) {
    showScreen('loadingScreen');
    el('loadingScreen').textContent = `LunaPanel returned an error (${res.status}).`;
    return;
  }

  const data = await res.json();
  renderPanel(data);
  showScreen('panelScreen');
  connectLive();
}

el('pairBox').addEventListener('submit', async e => {
  e.preventDefault();
  // Not .trim(): the tray shows the code grouped as "123 456" and
  // pre-selects it for copying, so the separator lands in the middle of the
  // value where trimming the ends never reaches it (see
  // PairEndpoint.NormalizeCode, which is the authoritative half of this).
  const code = el('pairCode').value.replace(/[\s\-]/g, '');
  if (!code) {
    el('pairError').textContent = 'Enter the pairing code.';
    return;
  }

  el('pairError').textContent = '';
  el('pairSubmit').disabled = true;
  try {
    const res = await fetch('__API_PAIR__', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      // deviceClass comes from isPhone(), which is the SERVER's own
      // phone/tablet boundary substituted into this page - not a second
      // rule invented here (ref/docs/layout-import.md). deviceName is what
      // the commander typed, or nothing at all: an empty name means "you
      // name it", and the server assigns the class label plus the next free
      // ordinal, which is the only side that can see the device list.
      body: JSON.stringify({
        code,
        deviceName: el('pairName').value,
        deviceClass: isPhone() ? 'phone' : 'tablet',
      }),
    });
    if (res.ok) {
      const body = await res.json().catch(() => ({}));
      showScreen('loadingScreen');
      await loadPanel();
      await handleRecovery(body.recovery);
      return;
    }
    const body = await res.json().catch(() => ({}));
    el('pairError').textContent = body.error || 'Pairing failed.';
  } catch (e) {
    el('pairError').textContent = 'Could not reach LunaPanel.';
  } finally {
    el('pairSubmit').disabled = false;
  }
});

// The three arms of ref/docs/layout-import.md's recovery flow, as the
// server already decided them. Arm 3 - no orphans - arrives as no recovery
// object at all, and is deliberately the arm with no code: a first-ever
// pairing must not be interrupted by an empty chooser.
async function handleRecovery(recovery) {
  if (!recovery) return;

  if (recovery.adopted) {
    announceAdoption(recovery.adopted.name);
    return;
  }

  if (recovery.candidates && recovery.candidates.length > 0) {
    openImportSheet(recovery.candidates, true);
  }
}

el('fsBtn').addEventListener('click', async () => {
  try {
    if (document.fullscreenElement) {
      await document.exitFullscreen();
    } else {
      await document.documentElement.requestFullscreen({ navigationUI: 'hide' });
    }
  } catch (e) {
    showToast(`Fullscreen not available (${e.name}).`);
  }
});
document.addEventListener('fullscreenchange', () => {
  el('fsBtn').textContent = document.fullscreenElement ? 'Exit full screen' : 'Full screen';
});

let resizeTimer = null;
function scheduleReload(delay) {
  clearTimeout(resizeTimer);
  resizeTimer = setTimeout(() => {
    applyChrome();
    if (!el('panelScreen').classList.contains('hidden')) {
      loadPanel();
    }
  }, delay);
}
addEventListener('resize', () => scheduleReload(150));
addEventListener('orientationchange', () => scheduleReload(300));

applyPairNamePlaceholder();

// The tray's "Build a macro" opens this page at #macros
// (ref/docs/hosting.md) so the commander lands on the pane they asked for
// rather than on the panel with the builder three taps away. Deliberately a
// hash and not a route: this client is one page and one document, and a
// second URL the server had to serve differently is exactly the second
// client the macro builder was told not to become.
async function boot() {
  await initEditingDeviceBanner();
  // A cold first launch (in particular a mobile WebView/Chrome-for-Android
  // opening this page for the very first time) can report a stale or
  // not-yet-settled innerWidth/innerHeight at the exact instant this runs -
  // panelUrl() reads them synchronously, so the very first /api/panel
  // request can carry the WRONG viewport, and the server then computes a
  // cols/cellWidth for that wrong viewport. The text-fit-shrink pass in
  // layoutGrid() sizes every label against that wrong cellWidth, so the
  // visible symptom is button labels overlapping their neighbours even
  // though the grid cells themselves are placed correctly. A real
  // rotation self-corrects today because resize/orientationchange re-fetch
  // with a genuinely current viewport - this is that same correction,
  // applied automatically instead of waiting on the commander to rotate
  // the device. Two rAFs is the standard, minimal-cost way to guarantee at
  // least one full layout/paint cycle has completed before trusting
  // innerWidth/innerHeight.
  await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
  await loadPanel();
  // Second line of defense: even after the double-rAF wait above, this
  // cannot be proven sufficient on every real device (this codebase has no
  // way to reproduce the actual mobile-browser viewport race). One
  // automatic follow-up re-fetch, reusing the exact same scheduleReload
  // path a real rotation already triggers, gives the page one more chance
  // to correct itself with no action from the commander. Deliberately a
  // SINGLE follow-up, not a loop - scheduleReload's own debounce means this
  // is a harmless no-op if the first read was already correct, and looping
  // it would risk fighting a commander who genuinely resizes/rotates during
  // this window.
  scheduleReload(400);
  if (location.hash === '#macros' && !el('panelScreen').classList.contains('hidden')) {
    el('settingsSheet').classList.remove('hidden');
    loadTheme();
    showSettingsPane('paneMacros');
  }
  // The second tray item, "Import or export" (ref/docs/transfer.md). Spelled
  // out beside the first rather than folded into a hash-to-pane table: the
  // fragment each tray item sends is pinned as a literal, and a table would
  // put the literal one indirection away from the call that uses it.
  if (location.hash === '#transfer' && !el('panelScreen').classList.contains('hidden')) {
    el('settingsSheet').classList.remove('hidden');
    loadTheme();
    showSettingsPane('paneTransfer');
  }
  // The third tray item, "Macro timing" (ref/docs/macro-timing.md). Spelled
  // out beside the other two for the reason given above, not folded into a
  // hash-to-pane table.
  if (location.hash === '#timing' && !el('panelScreen').classList.contains('hidden')) {
    el('settingsSheet').classList.remove('hidden');
    loadTheme();
    showSettingsPane('paneTiming');
  }
  // The fourth tray item, "Edit Live Panels" (ref/docs/pairing-and-devices.md).
  // Spelled out beside the other three for the reason given above, not
  // folded into a hash-to-pane table.
  if (location.hash === '#devices' && !el('panelScreen').classList.contains('hidden')) {
    el('settingsSheet').classList.remove('hidden');
    loadTheme();
    showSettingsPane('paneDevices');
  }
}

boot();
</script>
</body>
</html>
""";
}
