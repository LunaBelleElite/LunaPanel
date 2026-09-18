namespace LunaPanel.Core.Macros;

/// <summary>
/// Where a user-authored macro's id comes from, and how a shipped id is
/// told apart from one.
///
/// <b>The commander never types an id, and never sees one.</b> A layout
/// slot stores the id; the button shows the name
/// (<c>ref/docs/button-naming.md</c>). If the id were what the commander
/// typed, renaming a macro would mean either changing the id - orphaning
/// every slot that named it, rendering <c>UnknownMacro</c> on each - or
/// carrying a separate name anyway. So the commander supplies a
/// <em>name</em> and the server mints the id
/// (<c>ref/docs/macro-builder.md</c>, "Ids and names are different
/// things").
///
/// <b><see cref="Prefix"/> is reserved.</b> A shipped macro id must never
/// begin with it, now or in a later release - that one rule is what makes a
/// minted id collision-proof against every shipped macro without any
/// registry to keep in step, and it is pinned by a sweep over
/// <c>MacroLoader.LoadShipped()</c> rather than left to memory. The exact
/// spelling of the prefix does not matter; that shipped ids avoid it does.
/// </summary>
public static class UserMacroIds
{
    public const string Prefix = "user-";

    /// <summary>
    /// Whether <paramref name="id"/> belongs to the user-authored space -
    /// the check that keeps a save or a delete from ever touching a shipped
    /// macro, which is read-only (<c>ref/docs/macro-builder.md</c>,
    /// question 3: copy-to-edit, never edit-in-place).
    /// </summary>
    public static bool IsUserMacroId([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? id) =>
        id is not null && id.StartsWith(Prefix, StringComparison.Ordinal) && id.Length > Prefix.Length;

    /// <summary>
    /// A fresh id. Hexadecimal after the prefix, so it is safe as a file
    /// name component on any filesystem without escaping - the store writes
    /// one file per macro named for its id
    /// (<c>macro-&lt;id&gt;.json</c>), and an id carrying a path separator
    /// or a device name would be a very unpleasant way to discover that.
    /// </summary>
    public static string Mint() => Prefix + Guid.NewGuid().ToString("n")[..12];

    /// <summary>
    /// Whether <paramref name="id"/> is shaped like something
    /// <see cref="Mint"/> produced: the prefix, then between one and
    /// <see cref="MaxMintedBodyLength"/> lowercase hexadecimal characters
    /// and nothing else.
    ///
    /// <b>Why this is not the same question <see cref="IsUserMacroId"/>
    /// answers.</b> That one asks "is this in the user-authored space",
    /// which is a permission question, and it is satisfied by
    /// <c>user-../../anything</c>. Until 2026-09-10 nothing needed more,
    /// because no id ever came from outside: a save either minted one here
    /// or matched one already on disk. Importing a macro from a file
    /// (<c>ref/docs/transfer.md</c>) is the first path where an id arrives
    /// as content, and <c>UserMacroStore</c> turns an id straight into a
    /// file name - so the import checks this instead, and refuses anything
    /// that is merely prefixed correctly.
    ///
    /// Deliberately a shape rule and not a lookup: it has to be answerable
    /// about an id no machine has ever seen, which is exactly what an
    /// imported one is.
    /// </summary>
    public static bool IsWellFormed([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? id)
    {
        if (!IsUserMacroId(id))
        {
            return false;
        }

        var body = id.AsSpan(Prefix.Length);
        if (body.Length > MaxMintedBodyLength)
        {
            return false;
        }

        foreach (var c in body)
        {
            if (!char.IsAsciiDigit(c) && (c < 'a' || c > 'f'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Comfortably above the twelve characters <see cref="Mint"/> writes
    /// today, so a later, longer mint does not silently invalidate every id
    /// already on disk - and far below any filesystem's component limit, so
    /// a file name built from one cannot be the thing that fails.
    /// </summary>
    public const int MaxMintedBodyLength = 64;
}
