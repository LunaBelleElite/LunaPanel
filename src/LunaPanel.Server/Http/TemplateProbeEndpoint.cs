using System.Text.Json;
using LunaPanel.Core.Layouts;

namespace LunaPanel.Server.Http;

/// <summary>
/// <c>GET /probe</c> - a throwaway calibration page, not part of the product.
/// It exists to answer one question no test can: does a given rung of the
/// template ladder actually work in a human hand, on a real device?
///
/// The verdicts it shows come from the real <see cref="CellSizeEstimator"/>
/// over HTTP, deliberately **not** from a JavaScript re-implementation of the
/// same arithmetic - a re-implementation would be testing itself rather than
/// the code that ships, and would drift the moment either side changed.
///
/// Its own chrome is held to a single row matching the estimator's tablet
/// <c>headerStrip</c> allowance, so what the page shows is what the real panel
/// would actually give a user. An earlier revision stacked a rung bar, a
/// fullscreen bar and a facts block on top of that allowance, which pushed the
/// bottom row off screen - a probe whose chrome exceeds the allowance it is
/// calibrating misreports the thing it exists to measure. See
/// <c>tests/notes/live-checks.md</c> LC9.
///
/// The page also reports the device's true viewport and device pixel ratio
/// (which cannot be inferred from a model number), attempts a service worker
/// registration (LC8), and offers fullscreen - which, unlike service workers,
/// is not secure-context gated and therefore works over plain HTTP (LC9).
///
/// Unauthenticated, like <c>/api/health</c>: it exposes no user data, and
/// requiring pairing before the device can be measured would be circular.
/// </summary>
public static class TemplateProbeEndpoint
{
    public sealed record RungEstimate(
        string TemplateId,
        int SlotCount,
        int Cols,
        int Rows,
        double CellWidth,
        double CellHeight,
        string Verdict);

    /// <summary>
    /// Every rung of the ladder measured against one real viewport, using the
    /// shipping estimator. Orientation is decided from the viewport itself
    /// rather than trusted from the client, so a rotated device cannot report
    /// one thing and be measured as another.
    /// </summary>
    public static IReadOnlyList<RungEstimate> Estimate(double width, double height)
    {
        var orientation = width >= height ? PanelOrientation.Landscape : PanelOrientation.Portrait;

        var results = new List<RungEstimate>();
        foreach (var template in Templates.All)
        {
            var geometry = template.Geometry(orientation);
            var estimate = CellSizeEstimator.Estimate(width, height, orientation, template);

            results.Add(new RungEstimate(
                template.Id,
                template.Slots,
                geometry.Cols,
                geometry.Rows,
                Math.Round(estimate.CellWidth, 1),
                Math.Round(estimate.CellHeight, 1),
                estimate.Verdict.ToString()));
        }

        return results;
    }

    /// <summary>
    /// The real curated labels, read from the same embedded catalogue the
    /// product ships - not a copy pasted into this page, which would drift and
    /// would be testing itself rather than the shipped data.
    ///
    /// Ordered **longest first, deliberately**. The point of putting names on
    /// the probe is to judge legibility, and a sample taken in catalogue order
    /// would flatter the design by showing mostly short labels. Whoever looks
    /// at this should be looking at the worst case.
    /// </summary>
    public static IReadOnlyList<string> CuratedLabels()
    {
        using var stream = typeof(TemplateProbeEndpoint).Assembly
            .GetManifestResourceStream("LunaPanel.Server.definitions.catalogue.json");

        if (stream is null)
        {
            return Array.Empty<string>();
        }

        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("actions", out var actions))
        {
            return Array.Empty<string>();
        }

        var byElement = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var action in actions.EnumerateObject())
        {
            if (action.Value.TryGetProperty("label", out var label) &&
                label.GetString() is { Length: > 0 } text)
            {
                byElement[action.Name] = text;
            }
        }

        // The commander's chosen demo set, in order. These lead the grid so the
        // probe shows a panel someone would actually fly with, rather than the
        // longest labels in the catalogue.
        var leading = new[]
        {
            "LandingGearToggle",
            "ToggleCargoScoop",
            "NightVisionToggle",
            "ResetPowerDistribution",
            "HyperSuperCombination",
            "Supercruise",
        };

        // Dropped from the demo at the commander's request - real actions that
        // stay in the catalogue, simply not wanted on this panel.
        var excluded = new HashSet<string>(StringComparer.Ordinal)
        {
            "BuggyToggleReverseThrottleInput",
            "BuggySecondaryFireButton",
            "BuggyPrimaryFireButton",
            "EjectAllCargo",
            "EjectAllCargo_Buggy",
        };

        // The request-docking macro has no label of its own yet - the macro
        // does not exist - so it leads as a stand-in. Confirmed good on a real
        // device, and the only label here that exercises a user-chosen break.
        var ordered = new List<string> { "Request\nDocking" };
        foreach (var element in leading)
        {
            if (byElement.TryGetValue(element, out var text))
            {
                ordered.Add(text);
            }
        }

        // Everything else longest-first, so the rungs past the demo set still
        // stress-test wrapping rather than flattering it.
        ordered.AddRange(byElement
            .Where(pair => !excluded.Contains(pair.Key) && !leading.Contains(pair.Key))
            .Select(pair => pair.Value)
            .OrderByDescending(label => label.Length)
            .ThenBy(label => label, StringComparer.Ordinal));

        return ordered;
    }

    /// <summary>
    /// One self-contained document on purpose: this page must work before any
    /// static-file pipeline exists, and it is meant to be deleted rather than
    /// grown into the real client.
    /// </summary>
    public static string BuildPage() => PageHtml;

    private const string PageHtml = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<title>LunaPanel template probe</title>
<style>
  :root {
    --ground: #07090c;
    --frame: #ff7100;
    --text: #ffb000;
    --dim: #6b4a12;
    --bad: #d13b2e;
    --ok: #38a169;
  }
  * { box-sizing: border-box; }
  html, body { height: 100%; overflow: hidden; }
  body {
    margin: 0; background: var(--ground); color: var(--text);
    font-family: "Segoe UI", Roboto, system-ui, sans-serif;
    -webkit-text-size-adjust: 100%;
    display: flex; flex-direction: column;
  }

  /* One row only. This is the estimator's headerStrip allowance made real:
     if this grows, the grid stops fitting and the probe misreports. */
  #bar {
    flex: 0 0 auto; display: flex; gap: 6px; align-items: stretch;
    padding: 4px 8px; height: 48px;
  }
  .rung {
    background: transparent; color: var(--text);
    border: 1px solid var(--dim); padding: 3px 10px;
    font: inherit; font-size: 12px; letter-spacing: .08em; cursor: pointer;
    display: flex; flex-direction: column; align-items: center; justify-content: center;
    line-height: 1.2; white-space: nowrap;
  }
  .rung[aria-pressed="true"] { border-color: var(--frame); color: #fff; background: #1a1005; }
  .rung small { font-size: 9px; letter-spacing: 0; }
  #fs { margin-left: auto; border-color: var(--frame); }
  .v-Comfortable { color: var(--ok); }
  .v-Compact { color: var(--text); }
  .v-TooSmall { color: var(--bad); }

  #grid { flex: 1 1 auto; display: grid; align-content: start; justify-content: center; }
  .slot {
    border: 1px solid var(--frame);
    display: flex; align-items: center; justify-content: center;
    color: var(--text); user-select: none;
    text-align: center;
    overflow-wrap: break-word; hyphens: none; line-height: 1.15;
    white-space: pre-line;
    letter-spacing: .06em; font-weight: 600;
  }
  .slot:active { background: #2a1602; }

  #foot {
    flex: 0 0 auto; padding: 2px 8px; font-size: 10px; color: var(--dim);
    white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  }
  #foot b { color: var(--text); font-weight: 600; }
</style>
</head>
<body>
<div id="bar"></div>
<div id="grid"></div>
<div id="foot">measuring…</div>

<script>
const PHONE_MAX = 600;
function gutterFor() {
  return Math.min(innerWidth, innerHeight) < PHONE_MAX ? 8 : 12;
}
// CellSizeEstimator subtracts this from each edge before dividing up the
// viewport. If the grid does not actually apply it, the buttons sit flush to
// the screen edge in space the arithmetic already spent - the probe showing
// something the real panel would not.
function framePadFor() {
  return Math.min(innerWidth, innerHeight) < PHONE_MAX ? 12 : 16;
}

let rungs = [], current = null, swText = 'checking…', labels = [];

async function measure() {
  const w = Math.round(innerWidth), h = Math.round(innerHeight);
  const res = await fetch(`/probe/estimate?w=${w}&h=${h}`);
  rungs = await res.json();
  if (!labels.length) labels = await (await fetch('/probe/labels')).json();
  if (!current) current = rungs[rungs.length - 1].templateId;   // start at the max rung
  render();
}

function foot() {
  const w = Math.round(innerWidth), h = Math.round(innerHeight);
  const shortest = Math.min(w, h);
  document.getElementById('foot').innerHTML =
    `viewport <b>${w}&times;${h}</b> css &middot; dpr <b>${devicePixelRatio}</b> &middot; ` +
    `shortest <b>${shortest}</b> &rarr; <b>${shortest < PHONE_MAX ? 'PHONE' : 'TABLET'}</b> &middot; ` +
    `<b>${w >= h ? 'landscape' : 'portrait'}</b> &middot; service worker: <b>${swText}</b>`;
}

function render() {
  const bar = document.getElementById('bar');
  bar.innerHTML = '';
  for (const r of rungs) {
    const b = document.createElement('button');
    b.className = 'rung';
    b.setAttribute('aria-pressed', String(r.templateId === current));
    b.innerHTML = `${r.slotCount}<small class="v-${r.verdict}">${r.cellWidth}&times;${r.cellHeight}</small>`;
    b.onclick = () => { current = r.templateId; render(); };
    bar.appendChild(b);
  }

  // Same row, same size, hard right.
  const fs = document.createElement('button');
  fs.className = 'rung';
  fs.id = 'fs';
  fs.innerHTML = document.fullscreenElement
    ? 'EXIT<small>fullscreen</small>'
    : 'FULL<small>screen</small>';
  fs.onclick = async () => {
    try {
      if (document.fullscreenElement) await document.exitFullscreen();
      else await document.documentElement.requestFullscreen({ navigationUI: 'hide' });
    } catch (e) {
      swText = `fullscreen refused (${e.name})`;
      foot();
    }
  };
  bar.appendChild(fs);

  const r = rungs.find(x => x.templateId === current);
  if (!r) return;
  const grid = document.getElementById('grid');
  grid.style.gridTemplateColumns = `repeat(${r.cols}, ${r.cellWidth}px)`;
  grid.style.gridAutoRows = `${r.cellHeight}px`;
  grid.style.gap = `${gutterFor()}px`;
  grid.style.padding = `${framePadFor()}px`;
  grid.innerHTML = '';
  // Scale type to the cell so a 30-slot phone rung and a 6-slot tablet rung
  // are both judged on their own terms rather than at one fixed size.
  // Start optimistically large, then shrink each button individually until its
  // own text fits. Per-button, not per-rung: "Engage FSD" and "Previous SRV
  // Fire Group" share a cell size but not a sensible type size, and picking one
  // font for the whole grid means either the short labels look timid or the
  // long ones break mid-word.
  const startPx = Math.max(10, Math.min(30, Math.round(Math.min(r.cellWidth / 4, r.cellHeight / 2.4))));
  const MIN_PX = 8;
  // Breathing room inside each button, scaled to it - a 30-slot cell cannot
  // afford what a 6-slot cell should have. Applied before the fit loop runs, so
  // shrinking measures against the box the text actually gets.
  const padPx = Math.max(3, Math.min(12, Math.round(Math.min(r.cellWidth, r.cellHeight) * 0.08)));
  const made = [];
  for (let i = 0; i < r.slotCount; i++) {
    const d = document.createElement('div');
    d.className = 'slot';
    d.style.fontSize = startPx + 'px';
    d.style.padding = padPx + 'px';
    d.textContent = labels[i] || String(i + 1);
    grid.appendChild(d);
    made.push(d);
  }
  // One size for the whole grid, chosen as the largest at which EVERY label
  // fits - i.e. the minimum of each button's own fitting size.
  //
  // Per-button sizing was tried first and rejected on sight: individually
  // correct, collectively scrappy. A panel reads as one instrument, and real
  // instruments are silkscreened at one size, so a grid where every button
  // differs looks broken even when each is optimal on its own.
  //
  // The cost is real and worth stating: one long label drags the whole grid
  // down. That is the trade being made deliberately, and it argues for keeping
  // labels short rather than for abandoning uniformity.
  let fit = startPx;
  for (const d of made) {
    let px = startPx;
    while (px > MIN_PX && (d.scrollHeight > d.clientHeight + 1 || d.scrollWidth > d.clientWidth + 1)) {
      px -= 1;
      d.style.fontSize = px + 'px';
    }
    if (px < fit) fit = px;
  }
  for (const d of made) d.style.fontSize = fit + 'px';
  foot();
}

const secure = window.isSecureContext;
if (!('serviceWorker' in navigator)) {
  swText = `API ABSENT (isSecureContext=${secure})`;
} else {
  navigator.serviceWorker.register('/probe/sw.js')
    .then(() => { swText = 'REGISTERED'; foot(); })
    .catch(e => { swText = `REFUSED (${e.name})`; foot(); });
}

addEventListener('resize', measure);
addEventListener('orientationchange', () => setTimeout(measure, 250));
document.addEventListener('fullscreenchange', () => setTimeout(measure, 250));
measure();
</script>
</body>
</html>
""";
}
