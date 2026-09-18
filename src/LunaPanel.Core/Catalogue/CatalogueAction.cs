using LunaPanel.Core.GameState;

namespace LunaPanel.Core.Catalogue;

/// <summary>
/// One curated action entry: the label and category we ship for it, plus an
/// optional ordered "lit" list describing how engaged a physical control for
/// this action should show as, at any given moment. <see cref="Lit"/> is
/// already parsed - see <see cref="Catalogue.Parse"/>, which runs every
/// condition list through <see cref="ConditionList.Parse"/> at load time so
/// a typo in the curated data fails loudly there rather than silently
/// reaching a player.
///
/// Two JSON shapes both parse into this same ordered list (see
/// <c>ref/docs/lit-state.md</c>): a bare <c>"lit": ["Name"]</c> array of
/// condition tokens normalizes to a single <see cref="LitCondition"/> at
/// <see cref="SlotLitLevel.Full"/> - "full when true, off when false", the
/// only meaning this shape has ever had, kept working unchanged for every
/// curated action that doesn't need more than two states - and a
/// <c>"lit": [ { "when": [...], "level": "..." } ]</c> array of objects
/// lets an action distinguish <see cref="SlotLitLevel.Partial"/> from
/// <see cref="SlotLitLevel.Full"/> (first entry, in declaration order,
/// whose condition list evaluates true wins; <see cref="SlotLitLevel.Off"/>
/// when none match).
/// </summary>
/// <param name="Label">The curated, display-ready label: real words in Title Case (genuine acronyms upper case), at most two lines, with the break written as an explicit newline where the author wants it rather than left to the renderer - roughly 10 characters per line, 12 at most. See <c>ref/docs/button-naming.md</c>.</param>
/// <param name="Category">The id of the <see cref="CatalogueCategory"/> this action is grouped under.</param>
/// <param name="Lit">The parsed, ordered list of levelled conditions describing how engaged this action's control currently is, or <see langword="null"/> when the action has no observable on/off state.</param>
public sealed record CatalogueAction(string Label, string Category, IReadOnlyList<LitCondition>? Lit);
