using LunaPanel.Core.Layouts;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// Drives <see cref="LayoutImport"/> - the two pure decisions layout recovery
/// rests on (<c>ref/docs/layout-import.md</c>), with no filesystem and no
/// HTTP anywhere near them.
/// </summary>
public class LayoutImportTests
{
    private static readonly DateTimeOffset Base = new(2026, 9, 7, 20, 14, 0, TimeSpan.Zero);

    private static OrphanMarker Marker(string deviceId, string name, string deviceClass, DateTimeOffset lastSeen) =>
        new(deviceId, name, deviceClass, lastSeen);

    private static IReadOnlySet<string> Live(params string[] ids) => new HashSet<string>(ids, StringComparer.Ordinal);

    // ---------------------------------------------------------------
    // BuildCandidates
    // ---------------------------------------------------------------

    [Fact]
    public void BuildCandidates_EveryLayoutOwnedByALiveDevice_IsEmpty()
    {
        var candidates = LayoutImport.BuildCandidates(
            new[] { "AAAA", "BBBB" },
            Live("AAAA", "BBBB"),
            Array.Empty<OrphanMarker>());

        Assert.Empty(candidates);
    }

    [Fact]
    public void BuildCandidates_ALayoutNoLiveDeviceOwns_IsOffered_DescribedFromItsMarker()
    {
        var candidates = LayoutImport.BuildCandidates(
            new[] { "AAAA", "BBBB" },
            Live("AAAA"),
            new[] { Marker("BBBB", "Phone 2", "phone", Base) });

        var candidate = Assert.Single(candidates);
        Assert.Equal("BBBB", candidate.DeviceId);
        Assert.Equal("Phone 2", candidate.Name);
        Assert.Equal("phone", candidate.DeviceClass);
        Assert.Equal(Base, candidate.LastSeenAt);
    }

    /// <summary>
    /// A layout left by a device forgotten before markers existed still has
    /// to be offered - it is the commander's own arrangement, and refusing to
    /// list it because its description is thin would lose exactly the layouts
    /// that predate this feature.
    /// </summary>
    [Fact]
    public void BuildCandidates_OrphanWithNoMarker_IsStillOffered_WithNoLastSeen()
    {
        var candidate = Assert.Single(LayoutImport.BuildCandidates(
            new[] { "BBBB" },
            Live(),
            Array.Empty<OrphanMarker>()));

        Assert.Equal("BBBB", candidate.DeviceId);
        Assert.Null(candidate.LastSeenAt);
        Assert.Equal("unknown", candidate.DeviceClass);
        Assert.Contains("BBBB", candidate.Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// A marker whose layout file is gone describes nothing that could be
    /// imported, so it must not appear - offering a row that cannot be acted
    /// on is worse than a shorter list.
    /// </summary>
    [Fact]
    public void BuildCandidates_MarkerWithNoLayoutFile_IsNotOffered()
    {
        var candidates = LayoutImport.BuildCandidates(
            Array.Empty<string>(),
            Live(),
            new[] { Marker("BBBB", "Phone", "phone", Base) });

        Assert.Empty(candidates);
    }

    /// <summary>
    /// A device that was forgotten and has since re-paired leaves a marker
    /// behind, but its layout id is live again - the live check wins, or a
    /// commander would be offered their own current layout as if it were
    /// abandoned.
    /// </summary>
    [Fact]
    public void BuildCandidates_MarkerForAnIdThatIsLiveAgain_IsNotOffered()
    {
        var candidates = LayoutImport.BuildCandidates(
            new[] { "AAAA" },
            Live("AAAA"),
            new[] { Marker("AAAA", "Phone", "phone", Base) });

        Assert.Empty(candidates);
    }

    [Fact]
    public void BuildCandidates_OrdersMostRecentlyUsedFirst()
    {
        var candidates = LayoutImport.BuildCandidates(
            new[] { "OLD", "NEW", "MID" },
            Live(),
            new[]
            {
                Marker("OLD", "Phone", "phone", Base.AddDays(-9)),
                Marker("NEW", "Tablet", "tablet", Base),
                Marker("MID", "Phone 2", "phone", Base.AddDays(-3)),
            });

        Assert.Equal(new[] { "NEW", "MID", "OLD" }, candidates.Select(c => c.DeviceId));
    }

    /// <summary>
    /// An undescribed orphan sorts last rather than first: "no timestamp"
    /// is not "just now", and putting it at the top would push the row the
    /// commander is most likely to want off the top of the list.
    /// </summary>
    [Fact]
    public void BuildCandidates_UndescribedOrphans_SortAfterDescribedOnes()
    {
        var candidates = LayoutImport.BuildCandidates(
            new[] { "NOMARKER", "OLD" },
            Live(),
            new[] { Marker("OLD", "Phone", "phone", Base.AddYears(-2)) });

        Assert.Equal(new[] { "OLD", "NOMARKER" }, candidates.Select(c => c.DeviceId));
    }

    // ---------------------------------------------------------------
    // ChooseAutoAdopt
    // ---------------------------------------------------------------

    [Fact]
    public void ChooseAutoAdopt_NoCandidates_IsNull() =>
        Assert.Null(LayoutImport.ChooseAutoAdopt(Array.Empty<LayoutImportCandidate>(), "Phone"));

    [Fact]
    public void ChooseAutoAdopt_ExactlyOneCandidateMatchingTheName_IsThatCandidate()
    {
        var candidates = LayoutImport.BuildCandidates(
            new[] { "AAAA", "BBBB" },
            Live(),
            new[]
            {
                Marker("AAAA", "Phone", "phone", Base),
                Marker("BBBB", "Tablet", "tablet", Base),
            });

        var chosen = LayoutImport.ChooseAutoAdopt(candidates, "Phone");

        Assert.NotNull(chosen);
        Assert.Equal("AAAA", chosen!.DeviceId);
    }

    /// <summary>
    /// The "ambiguous match" arm. Two orphans called "Phone" is exactly the
    /// case where guessing is worse than asking - so nothing is adopted, and
    /// the caller falls through to presenting the list.
    /// </summary>
    [Fact]
    public void ChooseAutoAdopt_TwoCandidatesSharingTheName_IsNull()
    {
        var candidates = LayoutImport.BuildCandidates(
            new[] { "AAAA", "BBBB" },
            Live(),
            new[]
            {
                Marker("AAAA", "Phone", "phone", Base),
                Marker("BBBB", "Phone", "phone", Base.AddDays(-1)),
            });

        Assert.Null(LayoutImport.ChooseAutoAdopt(candidates, "Phone"));
    }

    /// <summary>
    /// Several orphans, exactly one of which matches - the coordinator's
    /// reading of the spec (2026-09-08): "exactly one orphan WHOSE NAME
    /// MATCHES", not "exactly one orphan, which also matches". The other two
    /// still appear on the list; they simply do not stop the match.
    /// </summary>
    [Fact]
    public void ChooseAutoAdopt_SeveralCandidatesButOnlyOneMatchingName_IsThatCandidate()
    {
        var candidates = LayoutImport.BuildCandidates(
            new[] { "AAAA", "BBBB", "CCCC" },
            Live(),
            new[]
            {
                Marker("AAAA", "Tablet", "tablet", Base),
                Marker("BBBB", "Phone 2", "phone", Base),
                Marker("CCCC", "Phone", "phone", Base),
            });

        Assert.Equal("CCCC", LayoutImport.ChooseAutoAdopt(candidates, "Phone")!.DeviceId);
    }

    [Fact]
    public void ChooseAutoAdopt_NoNameMatches_IsNull()
    {
        var candidates = LayoutImport.BuildCandidates(
            new[] { "AAAA" },
            Live(),
            new[] { Marker("AAAA", "Tablet", "tablet", Base) });

        Assert.Null(LayoutImport.ChooseAutoAdopt(candidates, "Phone"));
    }

    /// <summary>
    /// "Phone 2" must not match "Phone". A prefix or contains comparison
    /// would adopt the wrong device's layout the moment a household has more
    /// than one phone, which is the exact case this feature exists for.
    /// </summary>
    [Fact]
    public void ChooseAutoAdopt_MatchesTheWholeName_NotAPrefix()
    {
        var candidates = LayoutImport.BuildCandidates(
            new[] { "BBBB" },
            Live(),
            new[] { Marker("BBBB", "Phone 2", "phone", Base) });

        Assert.Null(LayoutImport.ChooseAutoAdopt(candidates, "Phone"));
    }

    [Fact]
    public void ChooseAutoAdopt_MatchesCaseInsensitively_AndIgnoresSurroundingSpace()
    {
        var candidates = LayoutImport.BuildCandidates(
            new[] { "BBBB" },
            Live(),
            new[] { Marker("BBBB", "kitchen tablet", "tablet", Base) });

        Assert.Equal("BBBB", LayoutImport.ChooseAutoAdopt(candidates, "  Kitchen Tablet ")!.DeviceId);
    }

    /// <summary>
    /// An undescribed orphan carries a synthesised name, which must never be
    /// eligible for an automatic adoption - the whole justification for
    /// adopting without asking is that a human typed (or accepted) that name.
    /// </summary>
    [Fact]
    public void ChooseAutoAdopt_AnUndescribedOrphan_IsNeverAdoptedAutomatically()
    {
        var candidates = LayoutImport.BuildCandidates(new[] { "BBBB" }, Live(), Array.Empty<OrphanMarker>());
        var synthesisedName = candidates[0].Name;

        Assert.Null(LayoutImport.ChooseAutoAdopt(candidates, synthesisedName));
    }
}
