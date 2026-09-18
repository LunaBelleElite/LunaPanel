namespace LunaPanel.Core.Pairing;

/// <summary>
/// The default display name a device gets when its commander does not type
/// one at pairing, and the normalisation of the class the client reports.
///
/// <b>Names here are display data, never identity</b>
/// (<c>ref/docs/layout-import.md</c>). Two devices may hold the same name
/// and nothing may break: authorization is <see cref="DeviceRegistry.TryAuthorize"/>
/// against a token, and lookup is always by <c>deviceId</c>. The one place a
/// name is ever compared is layout recovery
/// (<see cref="LunaPanel.Core.Layouts.LayoutImport.ChooseAutoAdopt"/>), which
/// deliberately refuses to act on an ambiguous match rather than picking one.
///
/// <b>Why the ordinal is computed on the server.</b> The disambiguator is the
/// next free ordinal among devices already registered with the same class,
/// and a device that has not paired yet cannot see the device list to compute
/// it - the list is behind device authentication precisely so an unpaired
/// client cannot enumerate the household. So the client sends a bare class
/// and no name, and this decides; a name the commander actually typed is used
/// verbatim instead.
/// </summary>
public static class DeviceNaming
{
    public const string PhoneClass = "phone";
    public const string TabletClass = "tablet";
    public const string UnknownClass = "unknown";

    /// <summary>
    /// The longest device name accepted from a client. A name is display
    /// data written into the registry state file and rendered in two device
    /// lists; nothing needs an unbounded one, and the pairing route is
    /// reachable without a cookie.
    /// </summary>
    public const int MaxNameLength = 40;

    /// <summary>
    /// Reduces whatever the client reported to exactly one of
    /// <see cref="PhoneClass"/>, <see cref="TabletClass"/> or
    /// <see cref="UnknownClass"/>. An unrecognised value becomes
    /// <see cref="UnknownClass"/> rather than being stored verbatim: the
    /// class is read back by the devices list and by
    /// <see cref="NextDefaultName"/>, and neither has anything sensible to
    /// do with a third value invented by a caller.
    /// </summary>
    public static string NormalizeClass(string? deviceClass)
    {
        var trimmed = deviceClass?.Trim() ?? string.Empty;

        if (string.Equals(trimmed, PhoneClass, StringComparison.OrdinalIgnoreCase))
        {
            return PhoneClass;
        }

        return string.Equals(trimmed, TabletClass, StringComparison.OrdinalIgnoreCase)
            ? TabletClass
            : UnknownClass;
    }

    public static string LabelForClass(string deviceClass) => NormalizeClass(deviceClass) switch
    {
        PhoneClass => "Phone",
        TabletClass => "Tablet",
        _ => "Device",
    };

    /// <summary>
    /// The class label, plus the smallest ordinal not already used by a
    /// registered device <em>of the same class</em> whose name is that label
    /// (ordinal 1, written bare) or that label followed by a number.
    ///
    /// Smallest <em>free</em>, deliberately, not "one past the highest":
    /// forgetting the second of three phones has to make "Phone 2" available
    /// again, or the household accumulates ever-higher numbers with gaps in
    /// them and the name stops being a short handle.
    /// </summary>
    public static string NextDefaultName(string deviceClass, IReadOnlyList<DeviceSummary> existingDevices)
    {
        ArgumentNullException.ThrowIfNull(existingDevices);

        var normalizedClass = NormalizeClass(deviceClass);
        var label = LabelForClass(normalizedClass);
        var taken = new HashSet<int>();

        foreach (var device in existingDevices)
        {
            if (!string.Equals(NormalizeClass(device.DeviceClass), normalizedClass, StringComparison.Ordinal))
            {
                continue;
            }

            var ordinal = OrdinalOf(device.Name, label);
            if (ordinal is int value)
            {
                taken.Add(value);
            }
        }

        var next = 1;
        while (taken.Contains(next))
        {
            next++;
        }

        return next == 1 ? label : $"{label} {next}";
    }

    /// <summary>
    /// Which ordinal <paramref name="name"/> occupies for
    /// <paramref name="label"/>, or <see langword="null"/> when it is a name
    /// the commander typed rather than one of this scheme's own.
    ///
    /// A leading zero ("Phone 02") is deliberately <em>not</em> read as
    /// ordinal 2: it is a name someone typed, and treating it as 2 would mint
    /// "Phone 3" next, leaving two device names differing only by a zero.
    /// </summary>
    private static int? OrdinalOf(string name, string label)
    {
        var trimmed = (name ?? string.Empty).Trim();

        if (string.Equals(trimmed, label, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        var prefix = label + " ";
        if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var suffix = trimmed[prefix.Length..];
        if (suffix.Length == 0 || suffix[0] == '0' || !suffix.All(char.IsAsciiDigit))
        {
            return null;
        }

        return int.TryParse(suffix, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// A commander-typed name is used verbatim, minus the two things that
    /// would make it unreadable where it is displayed: control characters
    /// (which land in the registry's own JSON state file and in two device
    /// lists) and unbounded length. Each control character becomes a space
    /// rather than vanishing, so "Kitchen\ntablet" reads as two words rather
    /// than one run-together one.
    /// </summary>
    public static string SanitizeTypedName(string typedName)
    {
        ArgumentNullException.ThrowIfNull(typedName);

        var builder = new System.Text.StringBuilder(typedName.Length);
        var lastWasSpace = true;

        foreach (var character in typedName)
        {
            var normalized = char.IsControl(character) ? ' ' : character;
            var isSpace = char.IsWhiteSpace(normalized);

            if (isSpace)
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            builder.Append(normalized);
            lastWasSpace = false;
        }

        var collapsed = builder.ToString().TrimEnd();
        return collapsed.Length > MaxNameLength ? collapsed[..MaxNameLength] : collapsed;
    }
}
