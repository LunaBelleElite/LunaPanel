using LunaPanel.Core.Updates;

namespace LunaPanel.Tests.Updates;

/// <summary>
/// Pins <see cref="VersionComparer"/>, the one decision standing between a
/// commander and an unwanted install: "is the published release actually
/// newer than what is running".
///
/// The two shapes it has to reconcile are real and different. A published
/// release's tag comes off <c>main</c>, which never carries a suffix
/// (<c>ver-0.53.0.2</c>); the running build is usually a <c>dev</c> one and
/// does (<c>ver-0.53.0.2-dev</c>). Comparing the suffix at all would make a
/// dev build that is already ahead of the last release read as "please
/// downgrade", which is why the suffix is ignored entirely rather than
/// ordered.
/// </summary>
public class VersionComparerTests
{
    [Fact]
    public void IsNewer_IdenticalVersions_IsFalse()
    {
        Assert.False(VersionComparer.IsNewer("ver-0.53.0.2", "ver-0.53.0.2"));
    }

    /// <summary>
    /// One position at a time, each independently - a comparer that only
    /// ever looks at the first number passes a single "newer" case and fails
    /// this whole theory.
    /// </summary>
    [Theory]
    [InlineData("ver-1.53.0.2", "ver-0.53.0.2")]
    [InlineData("ver-0.54.0.2", "ver-0.53.0.2")]
    [InlineData("ver-0.53.1.2", "ver-0.53.0.2")]
    [InlineData("ver-0.53.0.3", "ver-0.53.0.2")]
    public void IsNewer_HigherAtAnyOneOfTheFourPositions_IsTrue(string candidate, string current)
    {
        Assert.True(VersionComparer.IsNewer(candidate, current));
    }

    /// <summary>
    /// The same four cases mutated the other way round. A router that always
    /// answers "newer" passes every case above and none of these - which is
    /// the whole reason both directions are driven.
    /// </summary>
    [Theory]
    [InlineData("ver-0.53.0.2", "ver-1.53.0.2")]
    [InlineData("ver-0.53.0.2", "ver-0.54.0.2")]
    [InlineData("ver-0.53.0.2", "ver-0.53.1.2")]
    [InlineData("ver-0.53.0.2", "ver-0.53.0.3")]
    public void IsNewer_LowerAtAnyOneOfTheFourPositions_IsFalse(string candidate, string current)
    {
        Assert.False(VersionComparer.IsNewer(candidate, current));
    }

    /// <summary>
    /// Ordering is numeric, not lexicographic - "10" sorts before "9" as
    /// text, and this project's versioning scheme explicitly allows any
    /// number to climb arbitrarily high.
    /// </summary>
    [Fact]
    public void IsNewer_ComparesNumerically_NotAsText()
    {
        Assert.True(VersionComparer.IsNewer("ver-0.53.0.10", "ver-0.53.0.9"));
        Assert.False(VersionComparer.IsNewer("ver-0.53.0.9", "ver-0.53.0.10"));
    }

    /// <summary>
    /// The case this project actually runs in day to day: a <c>-dev</c> build
    /// already ahead of the last real release must read as "up to date", never
    /// as an available update.
    /// </summary>
    [Fact]
    public void IsNewer_OlderPublishedRelease_AgainstANewerDevBuild_IsFalse()
    {
        Assert.False(VersionComparer.IsNewer("ver-0.52.4.2", "ver-0.53.0.2-dev"));
    }

    /// <summary>
    /// And the mirror: a genuinely newer published release is still offered
    /// to a <c>-dev</c> build, so the suffix is ignored rather than acting as
    /// a blanket "never update".
    /// </summary>
    [Fact]
    public void IsNewer_NewerPublishedRelease_AgainstAnOlderDevBuild_IsTrue()
    {
        Assert.True(VersionComparer.IsNewer("ver-0.54.0.0", "ver-0.53.0.2-dev"));
    }

    /// <summary>
    /// Equal numbers with a suffix on one side only are equal, not newer -
    /// the suffix is ignored, so <c>ver-0.53.0.2</c> published against
    /// <c>ver-0.53.0.2-dev</c> running offers nothing.
    /// </summary>
    [Fact]
    public void IsNewer_SameNumbers_SuffixOnOneSideOnly_IsFalse()
    {
        Assert.False(VersionComparer.IsNewer("ver-0.53.0.2", "ver-0.53.0.2-dev"));
        Assert.False(VersionComparer.IsNewer("ver-0.53.0.2-dev", "ver-0.53.0.2"));
    }

    /// <summary>
    /// Malformed input degrades to "not newer" rather than throwing - on
    /// either side, and whichever way the malformed value would have sorted
    /// if it had parsed. Nothing installs on the strength of a string nobody
    /// could read.
    /// </summary>
    [Theory]
    [InlineData("", "ver-0.53.0.2")]
    [InlineData("   ", "ver-0.53.0.2")]
    [InlineData(null, "ver-0.53.0.2")]
    [InlineData("ver-0.53.0.2", null)]
    [InlineData("banana", "ver-0.53.0.2")]
    [InlineData("ver-0.53.0.2", "banana")]
    [InlineData("ver-1.2.3", "ver-0.53.0.2")]
    [InlineData("ver-1.2.3.4.5", "ver-0.53.0.2")]
    [InlineData("ver-a.b.c.d", "ver-0.53.0.2")]
    [InlineData("ver-1.2.3.-4", "ver-0.53.0.2")]
    [InlineData("ver-99999999999999999999.0.0.0", "ver-0.53.0.2")]
    [InlineData("v1.2.3.4", "ver-0.53.0.2")]
    public void IsNewer_MalformedEitherSide_IsFalse_AndNeverThrows(string? candidate, string? current)
    {
        Assert.False(VersionComparer.IsNewer(candidate, current));
    }

    /// <summary>
    /// The <c>ver-</c> prefix is optional on both sides. GitHub's tag and
    /// this project's own CHANGELOG header both carry it, but a release
    /// tagged without it should still compare rather than silently reading as
    /// malformed - a bare four-number string is unambiguous.
    /// </summary>
    [Fact]
    public void IsNewer_AcceptsABareFourNumberString_WithoutTheVerPrefix()
    {
        Assert.True(VersionComparer.IsNewer("0.54.0.0", "ver-0.53.0.2-dev"));
        Assert.True(VersionComparer.IsNewer("ver-0.54.0.0", "0.53.0.2"));
    }
}
