using LunaPanel.Core.GameState;

namespace LunaPanel.Tests.GameState;

/// <summary>
/// Pins the journal event table. The headline pin here is the spelling one:
/// O10 and <c>panel-tab-tracking.md</c> both warn that
/// <c>DockingCancelled</c> has a double L and that a misspelled event name
/// fails silently forever. This is the table that turns that from a thing to
/// remember into a thing the build checks.
/// </summary>
public class JournalVocabularyTests
{
    [Fact]
    public void DockingCancelled_IsSpelledWithADoubleL()
    {
        Assert.True(JournalVocabulary.IsKnown("DockingCancelled"));
    }

    [Fact]
    public void DockingCanceled_SingleL_IsNotKnown()
    {
        // The whole point: the single-L American spelling must NOT resolve.
        // If it did, a bind written with it would parse cleanly and then
        // never fire, with nothing to debug.
        Assert.False(JournalVocabulary.IsKnown("DockingCanceled"));
    }

    [Theory]
    [InlineData("LoadGame")]
    [InlineData("LaunchVessel")]
    [InlineData("DockSRV")]
    [InlineData("FSDJump")]
    [InlineData("DockingRequested")]
    public void TheEventsTheDesignDocsNameAConsumerFor_AreAllPresent(string name)
    {
        Assert.True(JournalVocabulary.IsKnown(name), $"'{name}' is named by a design doc as a signal LunaPanel consumes.");
    }

    [Fact]
    public void TryGet_IsCaseInsensitive_ButReturnsFrontiersOwnSpelling()
    {
        Assert.True(JournalVocabulary.TryGet("fsdjump", out var entry));
        Assert.Equal("FSDJump", entry.Name);
    }

    [Fact]
    public void EveryEntry_IsKeyedByItsOwnName()
    {
        // A row whose key and Name disagree would resolve under one spelling
        // and report the other, which is worse than being absent.
        foreach (var pair in JournalVocabulary.Events)
        {
            Assert.Equal(pair.Key, pair.Value.Name);
        }
    }

    [Fact]
    public void EveryEntry_CitesWhereItsAttestationLives()
    {
        var missing = JournalVocabulary.Events.Values
            .Where(e => string.IsNullOrWhiteSpace(e.Source))
            .Select(e => e.Name)
            .ToList();

        Assert.True(missing.Count == 0, "Entries with no Source: " + string.Join(", ", missing));
    }

    [Fact]
    public void DockingCancelled_IsMarkedCited_NotObserved()
    {
        // Honest provenance: this project has never captured the string from
        // a real file - it is only named in ref/docs and the test plan. That
        // is exactly the row whose spelling nothing here has verified
        // against the game, which is why it is the one carrying the warning.
        Assert.True(JournalVocabulary.TryGet("DockingCancelled", out var entry));
        Assert.Equal(JournalEventProvenance.Cited, entry.Provenance);
    }

    [Theory]
    [InlineData("DockSRV")]
    [InlineData("LaunchVessel")]
    [InlineData("SupercruiseEntry")]
    [InlineData("FSDJump")]
    [InlineData("RefuelAll")]
    public void EventsThisProjectHasActuallyCaptured_AreMarkedObserved(string name)
    {
        Assert.True(JournalVocabulary.TryGet(name, out var entry));
        Assert.Equal(JournalEventProvenance.Observed, entry.Provenance);
    }

    [Fact]
    public void TheTableIsSmall_AndDeliberatelySo()
    {
        // Not a coverage target: a row nobody reads is a row whose spelling
        // nobody has checked. If this fails because the table grew, confirm
        // each new row has a real consumer, then update the number.
        // [2026-09-07, superseded: was 12. +2 for DockingGranted/DockingDenied,
        // both real consumers of request-docking's waitForEdge step
        // (ref/docs/panel-tab-tracking.md's acknowledgement).]
        // [2026-09-10, superseded: was 14. +1 for LaunchSRV, which is how the
        // Scarab and the Scorpion announce a launch - LaunchVessel is the
        // Nomad's spelling and only the Nomad's. Its consumer is
        // JournalStateStore.CurrentVesselType; without the row, Record drops
        // the event before the vessel type is ever read off it.]
        // [2026-09-12, superseded: was 15. +1 for Continued (O24), so the
        // multi-part journal seam can be recognised and made visible in
        // diagnostics rather than falling through to the generic
        // no-LoadGame warning - see JournalTailer.RecordLine/ReportBackScanOutcome.]
        // [2026-09-12, superseded: was 16. +39 for a vocabulary-expansion
        // pass covering docking/departure, station services, travel,
        // exploration, combat, mining/trade, crew/multicrew and on-foot
        // events - all Cited except RefuelAll, which this project's own
        // probe caught (tests/notes/probe-2026-09-07-E.txt:122). The brief
        // that requested this pass estimated 31 before full enumeration;
        // the real count once every event was listed is 39, and every one
        // of them was added rather than trimmed to match the earlier
        // estimate.]
        Assert.Equal(55, JournalVocabulary.Events.Count);
    }
}
