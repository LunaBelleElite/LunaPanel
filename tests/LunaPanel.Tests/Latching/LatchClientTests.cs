using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Latching;

/// <summary>
/// The client half of latching, pinned the only way anything on that page
/// can be pinned in this suite: as static content. There is no headless
/// browser here (<c>ref/docs/web-client.md</c>'s "What's tested, and what
/// plainly isn't"), so nothing below drives a tap - these assert that the
/// page a commander is served actually contains the affordance, the toggle
/// and the route, rather than that tapping them works.
/// </summary>
public class LatchClientTests
{
    /// <summary>
    /// Brace-matches one CSS rule's body so every assertion below is scoped
    /// to the rule it is about. A needle searched across the whole 2,400-line
    /// page would be satisfied by a comment or by an unrelated rule, which is
    /// the difference between a pin and a decoration.
    /// </summary>
    private static string CssRuleBody(string html, string selector)
    {
        var start = html.IndexOf(selector, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected selector '{selector}' in the page.");
        var open = html.IndexOf('{', start);
        var close = html.IndexOf('}', open);
        Assert.True(close > open, $"Expected a body for '{selector}'.");
        return html.Substring(open, close - open);
    }

    private static string JsFunctionBody(string html, string declaration)
    {
        var start = html.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected '{declaration}' in the page.");
        var open = html.IndexOf('{', start + declaration.Length);
        Assert.True(open >= 0, $"Expected a body after '{declaration}'.");

        var depth = 0;
        for (var i = open; i < html.Length; i++)
        {
            if (html[i] == '{')
            {
                depth++;
            }
            else if (html[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return html.Substring(open, i - open + 1);
                }
            }
        }

        throw new InvalidOperationException($"Unbalanced braces after '{declaration}'.");
    }

    /// <summary>
    /// A commander must be able to see which buttons latch before tapping
    /// one - a touch surface has no tooltips, exactly the reasoning
    /// <c>.slot.has-long-press</c> already exists for.
    /// </summary>
    [Fact]
    public void BuildPage_LatchSlot_GetsItsOwnAffordanceMarker()
    {
        var html = PanelClientEndpoint.BuildPage();

        var body = CssRuleBody(html, ".slot.has-latch::after");
        Assert.Contains("position: absolute", body, StringComparison.Ordinal);
        Assert.Contains("background: currentColor", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The brief's constraint, made unfailable-proof by scoping the needle to
    /// the one rule it guards rather than to the whole file: the latch
    /// affordance must not introduce a fourth visual language or a new colour
    /// role (<c>ref/docs/lit-state.md</c>). A rule that declared any
    /// <c>--lp-</c> custom property would be doing exactly that; the LATCHED
    /// state itself is carried by the existing lit vocabulary, applied
    /// server-side.
    /// </summary>
    [Fact]
    public void BuildPage_LatchAffordance_DeclaresNoColourRoleOfItsOwn()
    {
        var body = CssRuleBody(PanelClientEndpoint.BuildPage(), ".slot.has-latch::after");

        Assert.DoesNotContain("--lp-", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The corollary, and the one that would actually be reintroduced by
    /// accident: no <c>.slot.latched</c> style may exist. A latched button
    /// looks latched through <c>.slot.lit</c>/<c>.slot.lit-full</c>, which the
    /// server sets via <c>LatchedLit</c> - a second, client-side latched style
    /// would be a fourth visual language, and would also drift out of step
    /// with the first paint.
    /// </summary>
    [Fact]
    public void BuildPage_NeverInventsASeparateLatchedStyle()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.DoesNotContain(".slot.latched", html, StringComparison.Ordinal);
        Assert.DoesNotContain("--lp-latch", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_LayoutGrid_AppliesHasLatchFromTheSlotsOwnFlag()
    {
        var body = JsFunctionBody(PanelClientEndpoint.BuildPage(), "function layoutGrid(data)");

        Assert.Contains("slot.latch", body, StringComparison.Ordinal);
        Assert.Contains("' has-latch'", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The slot sheet is the only way to assign a latch at all. A button that
    /// cannot be turned into a latch is the "server half with no client"
    /// failure this feature was explicitly told not to repeat.
    /// </summary>
    [Fact]
    public void BuildPage_SlotSheet_CarriesALatchToggle_LabelledBothWays()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("id=\"slotSheetLatch\"", html, StringComparison.Ordinal);

        var body = JsFunctionBody(html, "function openSlotSheet(index, slot)");
        Assert.Contains("Hold until tapped again", body, StringComparison.Ordinal);
        Assert.Contains("Stop holding", body, StringComparison.Ordinal);
        Assert.Contains("classList.toggle('hidden', !slot)", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The toggle has to flip what the slot currently is, not set it to a
    /// constant - a handler that always posted <c>true</c> would let a
    /// commander turn latching on and never off, and every "the route exists"
    /// assertion would still pass.
    /// </summary>
    [Fact]
    public void BuildPage_SlotSheetLatchButton_PostsTheOppositeOfTheSlotsCurrentState()
    {
        var html = PanelClientEndpoint.BuildPage();
        var handlerStart = html.IndexOf("el('slotSheetLatch').addEventListener", StringComparison.Ordinal);
        Assert.True(handlerStart >= 0, "Expected a click handler for the latch toggle.");

        var handler = html.Substring(handlerStart, 320);
        Assert.Contains("postSlotLatch(editorSlotIndex, !(editorSlotData && editorSlotData.latch))", handler, StringComparison.Ordinal);
    }

    /// <summary>
    /// The route literal is substituted from <see cref="ApiPaths.SlotLatch"/>
    /// rather than typed a second time in JavaScript - the same "one place
    /// this is spelled" discipline every other route on this page follows.
    /// </summary>
    [Fact]
    public void BuildPage_PostSlotLatch_TargetsTheRealRoute_AndReloadsThePanel()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.DoesNotContain("__API_SLOT_LATCH__", html, StringComparison.Ordinal);

        var body = JsFunctionBody(html, "async function postSlotLatch(slotIndex, latch)");
        Assert.Contains($"fetch('{ApiPaths.SlotLatch}'", body, StringComparison.Ordinal);
        Assert.Contains("await loadPanel();", body, StringComparison.Ordinal);
    }
}
