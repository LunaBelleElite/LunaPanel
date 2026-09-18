using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Input;

namespace LunaPanel.Core.Bindings;

/// <summary>
/// Resolves a parsed <see cref="BindingsFile"/> action to a keyboard chord,
/// or explains why it can't be. Rules: prefer Primary if it is Keyboard
/// with a non-empty key and every modifier is also Keyboard; otherwise try
/// Secondary on the same terms; otherwise unbound with the most specific
/// reason available. Every key involved - main and modifiers alike - must
/// resolve through <see cref="Scancodes.TryGet"/>.
/// </summary>
public static class BindResolver
{
    private const string KeyboardDevice = "Keyboard";
    private const string NoDeviceMarker = "{NoDevice}";

    private static readonly Dictionary<string, string> ModifierLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Key_LeftControl"] = "Ctrl",
        ["Key_RightControl"] = "Ctrl",
        ["Key_LeftAlt"] = "Alt",
        ["Key_RightAlt"] = "Alt",
        ["Key_LeftShift"] = "Shift",
        ["Key_RightShift"] = "Shift",
    };

    // Display order for the three collapsed modifier labels above. A
    // modifier that isn't one of Ctrl/Alt/Shift (rare - Elite doesn't
    // normally offer one) is appended after these, in file order.
    private static readonly string[] ModifierDisplayOrder = { "Ctrl", "Alt", "Shift" };

    /// <summary>
    /// Resolves <paramref name="actionName"/> against <paramref name="file"/>.
    /// Returns <see cref="UnboundReason.NotPresent"/> when no element with
    /// that name was found at all.
    /// </summary>
    public static BindResolution Resolve(BindingsFile file, string actionName)
    {
        var element = file.Elements.FirstOrDefault(e => e.Name == actionName);
        return element is null ? BindResolution.Unbound(UnboundReason.NotPresent) : Resolve(element);
    }

    /// <summary>Resolves one already-found <see cref="BindingElement"/> directly.</summary>
    public static BindResolution Resolve(BindingElement element)
    {
        var primary = ClassifySlot(element.Primary);
        if (primary.Chord is not null)
        {
            return BindResolution.Bound(primary.Chord);
        }

        var secondary = ClassifySlot(element.Secondary);
        if (secondary.Chord is not null)
        {
            return BindResolution.Bound(secondary.Chord);
        }

        // Neither slot produced a usable keyboard chord. Priority order for
        // the reason reported: a keyboard binding naming a key we can't map
        // is the most specific and most actionable thing to tell a player;
        // failing that, a real (non-keyboard) device binding existing at
        // all is more specific than nothing being bound whatsoever.
        if (primary.UnknownKeyName is not null)
        {
            return BindResolution.Unbound(UnboundReason.UnknownKey, primary.UnknownKeyName);
        }

        if (secondary.UnknownKeyName is not null)
        {
            return BindResolution.Unbound(UnboundReason.UnknownKey, secondary.UnknownKeyName);
        }

        if (primary.HasNonKeyboardDevice || secondary.HasNonKeyboardDevice)
        {
            return BindResolution.Unbound(UnboundReason.NonKeyboardOnly);
        }

        return BindResolution.Unbound(UnboundReason.NoDevice);
    }

    /// <summary>
    /// Resolves every element in <paramref name="file"/> and logs one
    /// summary line to category <c>Binds</c>: the preset name and version
    /// parsed, total elements, how many resolved to a keyboard chord, and
    /// how many were unbound broken down by reason. This is a whole-file
    /// sweep, not a per-action lookup, so <see cref="UnboundReason.NotPresent"/>
    /// never appears in the breakdown - every element here was, by
    /// definition, present.
    /// </summary>
    public static void LogSummary(BindingsFile file, IDiagnosticLog log)
    {
        var keyboardBound = 0;
        var noDevice = 0;
        var nonKeyboardOnly = 0;
        var unknownKey = 0;

        foreach (var element in file.Elements)
        {
            var resolution = Resolve(element);
            if (resolution.IsBound)
            {
                keyboardBound++;
                continue;
            }

            switch (resolution.Reason)
            {
                case UnboundReason.NoDevice:
                    noDevice++;
                    break;
                case UnboundReason.NonKeyboardOnly:
                    nonKeyboardOnly++;
                    break;
                case UnboundReason.UnknownKey:
                    unknownKey++;
                    break;
            }
        }

        var preset = file.PresetName ?? "(unknown)";
        var version = $"{file.MajorVersion?.ToString() ?? "?"}.{file.MinorVersion?.ToString() ?? "?"}";
        var message = $"Parsed bindings preset={preset} v{version}: {file.Elements.Count} elements, {keyboardBound} resolved to a keyboard chord.";
        var detail = $"unbound: noDevice={noDevice}, nonKeyboardOnly={nonKeyboardOnly}, unknownKey={unknownKey}";

        log.Info("Binds", message, detail);
    }

    private readonly record struct SlotOutcome(ResolvedChord? Chord, string? UnknownKeyName, bool HasNonKeyboardDevice);

    private static SlotOutcome ClassifySlot(BindingSlot? slot)
    {
        if (slot is null || string.IsNullOrEmpty(slot.Device) || slot.Device == NoDeviceMarker)
        {
            return default;
        }

        if (!string.Equals(slot.Device, KeyboardDevice, StringComparison.Ordinal))
        {
            return new SlotOutcome(null, null, true);
        }

        if (string.IsNullOrEmpty(slot.Key))
        {
            return default;
        }

        foreach (var modifier in slot.Modifiers)
        {
            if (!string.Equals(modifier.Device, KeyboardDevice, StringComparison.Ordinal))
            {
                return new SlotOutcome(null, null, true);
            }
        }

        if (!Scancodes.TryGet(slot.Key, out var mainInfo))
        {
            return new SlotOutcome(null, slot.Key, false);
        }

        var modifierInfos = new List<ScancodeInfo>();
        foreach (var modifier in slot.Modifiers)
        {
            if (!Scancodes.TryGet(modifier.Key, out var modInfo))
            {
                return new SlotOutcome(null, modifier.Key, false);
            }

            modifierInfos.Add(modInfo);
        }

        var display = BuildDisplayText(slot.Key, slot.Modifiers);
        return new SlotOutcome(new ResolvedChord(mainInfo, modifierInfos, display), null, false);
    }

    private static string BuildDisplayText(string mainKey, IReadOnlyList<BoundKey> modifiers)
    {
        var labels = new List<string>();

        foreach (var canonicalLabel in ModifierDisplayOrder)
        {
            if (modifiers.Any(m => ModifierLabels.TryGetValue(m.Key, out var label) && label == canonicalLabel))
            {
                labels.Add(canonicalLabel);
            }
        }

        foreach (var modifier in modifiers)
        {
            if (!ModifierLabels.ContainsKey(modifier.Key))
            {
                labels.Add(PrettifyKeyName(modifier.Key));
            }
        }

        labels.Add(PrettifyKeyName(mainKey));
        return string.Join("+", labels);
    }

    /// <summary>
    /// [2026-09-12] Made <c>public</c> (was <c>private</c>) for the "press a
    /// key" macro step (<c>ref/docs/macros.md</c>): the key picker's own
    /// labels, and <c>PressKeyStep</c>'s display chord, both need the exact
    /// same "Key_F5" -&gt; "F5" formatting this class already uses for a
    /// bound modifier/main key, rather than a second hand-typed copy of it.
    /// </summary>
    public static string PrettifyKeyName(string rawKeyName)
    {
        var canonical = Scancodes.All.Keys.FirstOrDefault(k => string.Equals(k, rawKeyName, StringComparison.OrdinalIgnoreCase)) ?? rawKeyName;
        var stripped = canonical.StartsWith("Key_", StringComparison.OrdinalIgnoreCase) ? canonical[4..] : canonical;
        return stripped.Replace('_', ' ');
    }
}
