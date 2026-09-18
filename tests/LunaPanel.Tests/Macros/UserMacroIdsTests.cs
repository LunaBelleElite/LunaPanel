using LunaPanel.Core.Macros;
using LunaPanel.Server.Macros;

namespace LunaPanel.Tests.Macros;

/// <summary>
/// Pins the one rule that makes a server-minted id collision-proof without a
/// registry: <see cref="UserMacroIds.Prefix"/> is reserved, and no shipped
/// macro may ever use it (<c>ref/docs/macro-builder.md</c>, "Ids and names
/// are different things").
/// </summary>
public class UserMacroIdsTests
{
    /// <summary>
    /// The sweep that actually enforces the reservation. Every shipped macro
    /// the real loader produces is checked, so a macro added later with an
    /// id like <c>user-favourites</c> fails here rather than silently
    /// shadowing - or being shadowed by - something a commander authored.
    /// </summary>
    [Fact]
    public void NoShippedMacroId_UsesTheReservedUserPrefix()
    {
        var shipped = MacroLoader.LoadShipped();
        Assert.NotEmpty(shipped);

        foreach (var macro in shipped)
        {
            Assert.False(
                UserMacroIds.IsUserMacroId(macro.Id),
                $"Shipped macro '{macro.Id}' uses the reserved '{UserMacroIds.Prefix}' prefix; shipped ids must never collide with minted ones.");
        }
    }

    [Fact]
    public void Mint_ProducesAUserMacroId()
    {
        Assert.True(UserMacroIds.IsUserMacroId(UserMacroIds.Mint()));
    }

    [Fact]
    public void Mint_NeverRepeatsItself()
    {
        var minted = Enumerable.Range(0, 500).Select(_ => UserMacroIds.Mint()).ToList();

        Assert.Equal(minted.Count, minted.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// A minted id becomes a file name component
    /// (<c>macro-&lt;id&gt;.json</c>), so it must carry nothing a filesystem
    /// would read as structure. Pinned against the actual invalid-character
    /// set this platform reports rather than a hand-typed list of the
    /// characters someone remembered.
    /// </summary>
    [Fact]
    public void Mint_IsSafeAsAFileNameComponent()
    {
        var invalid = Path.GetInvalidFileNameChars();

        for (var i = 0; i < 50; i++)
        {
            var id = UserMacroIds.Mint();
            Assert.DoesNotContain(id, c => invalid.Contains(c));
            Assert.DoesNotContain("..", id, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("disembark")]
    [InlineData("request-docking")]
    [InlineData("")]
    [InlineData("user")]
    [InlineData("user-")]
    [InlineData("User-abc")]
    public void IsUserMacroId_RejectsAnythingThatIsNotAPrefixedNonEmptyId(string id)
    {
        Assert.False(UserMacroIds.IsUserMacroId(id));
    }

    [Fact]
    public void IsUserMacroId_Null_IsFalse_NotAnException()
    {
        Assert.False(UserMacroIds.IsUserMacroId(null));
    }

    // -----------------------------------------------------------------
    // IsWellFormed - the stricter question an IMPORTED id has to answer
    // (ref/docs/transfer.md)
    // -----------------------------------------------------------------

    /// <summary>
    /// The relation that makes the rule safe to tighten: whatever
    /// <see cref="UserMacroIds.Mint"/> produces, this accepts. A mint changed
    /// to something this rejects would orphan every id already on disk, and
    /// it fails here rather than in a commander's macro list.
    /// </summary>
    [Fact]
    public void IsWellFormed_AcceptsEverythingMintProduces()
    {
        for (var i = 0; i < 200; i++)
        {
            var id = UserMacroIds.Mint();
            Assert.True(UserMacroIds.IsWellFormed(id), id);
        }
    }

    /// <summary>
    /// The pair that matters, in one test: these ids pass
    /// <see cref="UserMacroIds.IsUserMacroId"/> - the permission question -
    /// and must fail the shape question, because
    /// <c>UserMacroStore</c> turns an id straight into a file name and an
    /// imported id is content, not something this project minted.
    ///
    /// Asserting both directions here rather than in two tests is deliberate:
    /// the claim IS that the two questions differ, and a test pinning only
    /// the second would go on passing against an <c>IsWellFormed</c> that had
    /// become an alias of the first.
    /// </summary>
    [Theory]
    [InlineData("user-../../../evil")]
    [InlineData("user-a/b")]
    [InlineData("user-a\\b")]
    [InlineData("user-a.b")]
    [InlineData("user-CON")]
    [InlineData("user-ABC123")]
    [InlineData("user-zzz")]
    [InlineData("user- 123")]
    public void IsWellFormed_RejectsIdsThatIsUserMacroIdAccepts(string id)
    {
        Assert.True(UserMacroIds.IsUserMacroId(id), "the fixture is only interesting if the looser check passes it");
        Assert.False(UserMacroIds.IsWellFormed(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("user-")]
    [InlineData("disembark")]
    public void IsWellFormed_RejectsWhatIsUserMacroIdRejectsToo(string? id)
    {
        Assert.False(UserMacroIds.IsWellFormed(id));
    }

    [Fact]
    public void IsWellFormed_RejectsAnIdLongerThanTheCap_AndAcceptsOneExactlyAtIt()
    {
        var atTheCap = UserMacroIds.Prefix + new string('a', UserMacroIds.MaxMintedBodyLength);
        var overIt = UserMacroIds.Prefix + new string('a', UserMacroIds.MaxMintedBodyLength + 1);

        Assert.True(UserMacroIds.IsWellFormed(atTheCap));
        Assert.False(UserMacroIds.IsWellFormed(overIt));
    }
}
