namespace LunaPanel.Core.Layouts;

/// <summary>
/// What is left behind when a device is forgotten, so the layout file it
/// leaves on disk can still be described to a person.
///
/// Forgetting a device removes its <c>DeviceRecord</c> from the registry, and
/// with it the only human-readable account of the layout that stays behind
/// (nothing ever deletes <c>layout-&lt;deviceId&gt;.json</c>). An import list
/// built from filenames alone is a column of hex nobody can choose from -
/// this is what lets it say "Phone - last used yesterday, 20:14" instead.
/// See <c>ref/docs/layout-import.md</c>.
///
/// <b>Deliberately has no field for a token, and never will.</b> The registry
/// does not store a token in a form anything can recover, and a marker that
/// carried one would put the household's actual secret into a file whose only
/// purpose is to be listed on a settings screen. <see cref="DeviceId"/> is
/// derived from the token and safe to keep (<c>ref/docs/pairing.md</c>);
/// nothing else here comes from the secret at all.
/// </summary>
/// <param name="DeviceId">
/// The forgotten device's id - the same value its layout file is named for,
/// which is what ties this marker to that file.
/// </param>
/// <param name="Name">The display name the device had when it was forgotten.</param>
/// <param name="DeviceClass">Its class, as <c>DeviceNaming.NormalizeClass</c> had it.</param>
/// <param name="LastSeenAt">
/// When that device last connected, carried over from its registry record
/// rather than stamped at forget time - "last used" is the question the
/// import list actually answers, and a device can be forgotten long after it
/// was last held.
/// </param>
public sealed record OrphanMarker(
    string DeviceId,
    string Name,
    string DeviceClass,
    DateTimeOffset LastSeenAt);
