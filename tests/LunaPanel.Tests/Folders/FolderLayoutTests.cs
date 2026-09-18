using LunaPanel.Core.Bindings;
using LunaPanel.Core.Catalogue;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Macros;

namespace LunaPanel.Tests.Folders;

/// <summary>
/// Pins the CORE half of folders (<c>ref/docs/layouts.md</c>): the data model
/// (<see cref="LayoutSlot.FolderPage"/>, <see cref="LayoutPage.Id"/>,
/// <see cref="LayoutPage.IsFolderOwned"/>), every rule
/// <see cref="LayoutValidator"/> enforces over them, their JSON round trip,
/// and how <see cref="LayoutAnnotator"/> describes a folder button.
///
/// Kept in its own file rather than split across
/// <c>LayoutValidatorTests</c>/<c>LayoutJsonTests</c>/<c>LayoutAnnotatorTests</c>
/// for the same reason <c>Latching/LatchLayoutTests</c> is: folders are ONE
/// feature whose invariants only make sense read together - the id is only
/// safe because the validator forbids a shared target, and the validator's
/// one-level cap is only meaningful because the JSON layer can actually
/// carry <c>folderOwned</c> back off disk.
/// </summary>
public class FolderLayoutTests
{
    private const string CatalogueJson = """
        {
          "catalogueVersion": 1,
          "categories": [ { "id": "ship", "label": "SHIP" } ],
          "actions": {
            "LandingGearToggle": { "label": "GEAR", "category": "ship" }
          }
        }
        """;

    private const string BindingsXml = """
        <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
            <LandingGearToggle>
                <Primary Device="Keyboard" Key="Key_G" />
                <Secondary Device="{NoDevice}" Key="" />
            </LandingGearToggle>
        </Root>
        """;

    private static LayoutSlot FolderSlot(int index, string folderPageId, string? label = null) =>
        new(index, null, null, label, null, FolderPage: folderPageId);

    private static LayoutPage Interior(string name, string id, IReadOnlyList<LayoutSlot>? slots = null) =>
        new(name, "t6", slots ?? Array.Empty<LayoutSlot>(), Array.Empty<LayoutSlot>(), ShowWhen: null, Id: id, IsFolderOwned: true);

    private static LayoutPage TopLevel(string name, params LayoutSlot[] slots) =>
        new(name, "t6", slots, Array.Empty<LayoutSlot>());

    // ------------------------------------------------------------------
    // LayoutValidator
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_FolderSlotPointingAtItsOwnFolderOwnedPage_IsValid()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SHIP", FolderSlot(0, "aaa")),
            Interior("THRUST", "aaa"),
        });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Validate_SlotNamingBothAnActionAndAFolder_Fails()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SHIP", new LayoutSlot(0, "LandingGearToggle", null, null, null, FolderPage: "aaa")),
            Interior("THRUST", "aaa"),
        });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SHIP") && e.Contains("slot 0") && e.Contains("exactly one"));
    }

    [Fact]
    public void Validate_SlotNamingBothAMacroAndAFolder_Fails()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SHIP", new LayoutSlot(0, null, "request-docking", null, null, FolderPage: "aaa")),
            Interior("THRUST", "aaa"),
        });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("slot 0") && e.Contains("exactly one"));
    }

    /// <summary>
    /// The two-way check this replaced already rejected a slot naming
    /// nothing; widening it to three must not have lost that - the "neither"
    /// arm is the one a three-way rewrite most easily drops, because the
    /// obvious rewrite counts how many are set and only complains above one.
    /// </summary>
    [Fact]
    public void Validate_SlotNamingNothingAtAll_StillFails()
    {
        var layout = new Layout(1, new[] { TopLevel("SHIP", new LayoutSlot(0, null, null, null, null)) });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("slot 0") && e.Contains("exactly one"));
    }

    [Fact]
    public void Validate_LatchedFolderSlot_Fails()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SHIP", FolderSlot(0, "aaa") with { Latch = true }),
            Interior("THRUST", "aaa"),
        });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("slot 0") && e.Contains("latched"));
    }

    [Fact]
    public void Validate_HeldFolderSlot_Fails()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SHIP", FolderSlot(0, "aaa") with { Hold = true }),
            Interior("THRUST", "aaa"),
        });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("slot 0") && e.Contains("held"));
    }

    /// <summary>
    /// The one-level cap. A folder inside a folder is refused outright - the
    /// commander's own ruling, and the reason the client never has to render
    /// a breadcrumb deeper than one step back.
    /// </summary>
    [Fact]
    public void Validate_FolderSlotOnAFolderOwnedPage_Fails()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SHIP", FolderSlot(0, "aaa")),
            Interior("THRUST", "aaa", new[] { FolderSlot(0, "bbb") }),
            Interior("DEEPER", "bbb"),
        });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("THRUST") && e.Contains("slot 0") && e.Contains("one level"));
    }

    /// <summary>
    /// Parked, not active - the cap has to hold for a slot that fell out of
    /// its template's range too, or shrinking a folder's panel size would be
    /// a way to smuggle a second level past this rule.
    /// </summary>
    [Fact]
    public void Validate_ParkedFolderSlotOnAFolderOwnedPage_Fails()
    {
        var interior = new LayoutPage("THRUST", "t6", Array.Empty<LayoutSlot>(), new[] { FolderSlot(9, "bbb") }, null, "aaa", true);
        var layout = new Layout(1, new[]
        {
            TopLevel("SHIP", FolderSlot(0, "aaa")),
            interior,
            Interior("DEEPER", "bbb"),
        });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("THRUST") && e.Contains("one level"));
    }

    [Fact]
    public void Validate_FolderPageThatResolvesToNothing_Fails()
    {
        var layout = new Layout(1, new[] { TopLevel("SHIP", FolderSlot(0, "missing")) });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SHIP") && e.Contains("slot 0") && e.Contains("missing"));
    }

    /// <summary>
    /// A folder may only ever open a page that is MARKED as a folder's
    /// interior. Pointing one at an ordinary top-level page would put that
    /// page in the tab bar AND behind a button, with an "up one level" bar
    /// competing with its own tab.
    /// </summary>
    [Fact]
    public void Validate_FolderPagePointingAtAnOrdinaryPage_Fails()
    {
        var ordinary = new LayoutPage("SRV", "t6", Array.Empty<LayoutSlot>(), Array.Empty<LayoutSlot>(), null, "aaa", false);
        var layout = new Layout(1, new[] { TopLevel("SHIP", FolderSlot(0, "aaa")), ordinary });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        // Named specifically, not just "some error mentioning slot 0": before
        // the three-way exactly-one-of check landed, this fixture failed for
        // an entirely different reason (a folder slot has no action and no
        // macro, so the old two-way check rejected it), and a needle that
        // loose passed without the rule under test existing at all.
        Assert.Contains(result.Errors, e => e.Contains("slot 0") && e.Contains("SRV") && e.Contains("ordinary page"));
    }

    [Fact]
    public void Validate_TwoSlotsOnOnePageSharingOneFolderTarget_Fails()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SHIP", FolderSlot(0, "aaa"), FolderSlot(1, "aaa")),
            Interior("THRUST", "aaa"),
        });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("more than one"));
    }

    /// <summary>
    /// The cross-page half of the same rule, and the reason it cannot live in
    /// the per-page walk: two folder buttons on two DIFFERENT pages opening
    /// one interior is invisible to any check that only ever sees one page at
    /// a time, and it is the shape an import or a hand edit would actually
    /// produce.
    /// </summary>
    [Fact]
    public void Validate_TwoSlotsOnDifferentPagesSharingOneFolderTarget_Fails()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SHIP", FolderSlot(0, "aaa")),
            TopLevel("SRV", FolderSlot(0, "aaa")),
            Interior("THRUST", "aaa"),
        });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("more than one"));
    }

    /// <summary>
    /// An interior page nothing points at any more is an intact ORPHAN, not
    /// an error: clearing or reassigning a folder button must never destroy
    /// the page full of buttons behind it (the commander's own ruling, and
    /// the same philosophy as <see cref="LayoutPage.Parked"/>). So a
    /// folder-owned page with no referrer has to validate, or an ordinary
    /// Clear would leave the layout unsavable.
    /// </summary>
    [Fact]
    public void Validate_FolderOwnedPageNothingReferences_IsValid()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SHIP", new LayoutSlot(0, "LandingGearToggle", null, null, null)),
            Interior("THRUST", "aaa", new[] { new LayoutSlot(0, "LandingGearToggle", null, null, null) }),
        });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Validate_LayoutWithNoFoldersAtAll_IsUnaffected()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SHIP", new LayoutSlot(0, "LandingGearToggle", null, null, null)),
            TopLevel("SRV", new LayoutSlot(0, null, "request-docking", null, null)),
        });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    // ------------------------------------------------------------------
    // LayoutJson
    // ------------------------------------------------------------------

    [Fact]
    public void Json_FolderFields_SurviveASerializeParseRoundTrip()
    {
        var layout = new Layout(LayoutMigrator.CurrentSchemaVersion, new[]
        {
            TopLevel("SHIP", FolderSlot(0, "aaa", label: "THRUST")),
            Interior("THRUST", "aaa", new[] { new LayoutSlot(1, "LandingGearToggle", null, null, null) }),
        });

        var parsed = LayoutJson.Parse(LayoutJson.Serialize(layout));

        Assert.True(parsed.Success, parsed.Error);
        // Field by field, not Assert.Equal(layout, parsed.Layout): a record's
        // generated equality compares its IReadOnlyList members by REFERENCE,
        // so whole-layout equality here is always false and would be a test
        // that can never pass rather than one that pins anything.
        var round = parsed.Layout!;
        Assert.Equal(layout.SchemaVersion, round.SchemaVersion);
        Assert.Equal("aaa", round.Pages[0].Slots[0].FolderPage);
        Assert.Equal("THRUST", round.Pages[0].Slots[0].Label);
        Assert.Null(round.Pages[0].Id);
        Assert.False(round.Pages[0].IsFolderOwned);
        Assert.Equal("aaa", round.Pages[1].Id);
        Assert.True(round.Pages[1].IsFolderOwned);
        Assert.Equal("LandingGearToggle", round.Pages[1].Slots[0].Action);
        Assert.Null(round.Pages[1].Slots[0].FolderPage);
    }

    [Fact]
    public void Json_Serialize_OmitsEveryFolderFieldWhenAbsent()
    {
        var layout = new Layout(1, new[] { TopLevel("SHIP", new LayoutSlot(0, "LandingGearToggle", null, null, null)) });

        var json = LayoutJson.Serialize(layout);

        Assert.DoesNotContain("folderPage", json, StringComparison.Ordinal);
        Assert.DoesNotContain("folderOwned", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"id\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_Parse_AFileWrittenBeforeFoldersExisted_LeavesEveryFolderFieldNullOrFalse()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "pages": [
                { "name": "SHIP", "templateId": "t6",
                  "slots": [ { "index": 0, "action": "LandingGearToggle" } ],
                  "parked": [] }
              ]
            }
            """;

        var result = LayoutJson.Parse(json);

        Assert.True(result.Success, result.Error);
        var page = result.Layout!.Pages[0];
        Assert.Null(page.Id);
        Assert.False(page.IsFolderOwned);
        Assert.Null(page.Slots[0].FolderPage);
    }

    [Fact]
    public void Json_Parse_ReadsFolderFieldsBackOffDisk()
    {
        const string json = """
            {
              "schemaVersion": 2,
              "pages": [
                { "name": "SHIP", "templateId": "t6",
                  "slots": [ { "index": 0, "folderPage": "aaa" } ], "parked": [] },
                { "name": "THRUST", "templateId": "t6", "id": "aaa", "folderOwned": true,
                  "slots": [], "parked": [] }
              ]
            }
            """;

        var result = LayoutJson.Parse(json);

        Assert.True(result.Success, result.Error);
        Assert.Equal("aaa", result.Layout!.Pages[0].Slots[0].FolderPage);
        Assert.Equal("aaa", result.Layout.Pages[1].Id);
        Assert.True(result.Layout.Pages[1].IsFolderOwned);
    }

    [Fact]
    public void Json_Parse_NonStringFolderPage_IsReportedNotSilentlyDropped()
    {
        const string json = """
            { "schemaVersion": 2, "pages": [ { "name": "SHIP", "templateId": "t6",
              "slots": [ { "index": 0, "folderPage": 7 } ], "parked": [] } ] }
            """;

        var result = LayoutJson.Parse(json);

        Assert.False(result.Success);
        Assert.Contains("folderPage", result.Error);
    }

    [Fact]
    public void Json_Parse_NonBooleanFolderOwned_IsReportedNotSilentlyDropped()
    {
        const string json = """
            { "schemaVersion": 2, "pages": [ { "name": "SHIP", "templateId": "t6",
              "folderOwned": "yes", "slots": [], "parked": [] } ] }
            """;

        var result = LayoutJson.Parse(json);

        Assert.False(result.Success);
        Assert.Contains("folderOwned", result.Error);
    }

    /// <summary>
    /// A documentation-only bump (<c>ref/docs/layouts.md</c>): there is no
    /// <c>MigrateV1ToV2</c> body, because every folder field is additive and
    /// optional. What it buys is the refusal at the other end - an OLDER
    /// build meeting a folder-bearing file reports
    /// <see cref="LayoutMigrationOutcome.TooNew"/> and leaves it alone,
    /// instead of loading it with no folder-aware validation and re-saving it
    /// with every folder silently stripped.
    /// </summary>
    [Fact]
    public void Migrator_CurrentSchemaVersionIsTwo()
    {
        Assert.Equal(2, LayoutMigrator.CurrentSchemaVersion);
    }

    [Fact]
    public void Migrator_AV1File_StillLoads_AndComesUpToV2()
    {
        const string json = """
            { "schemaVersion": 1, "pages": [ { "name": "SHIP", "templateId": "t6", "slots": [], "parked": [] } ] }
            """;

        var migrated = LayoutMigrator.Migrate(json);

        Assert.NotEqual(LayoutMigrationOutcome.Malformed, migrated.Outcome);
        Assert.NotEqual(LayoutMigrationOutcome.TooNew, migrated.Outcome);
        var parsed = LayoutJson.Parse(migrated.MigratedJson!);
        Assert.True(parsed.Success, parsed.Error);
        Assert.Equal(2, parsed.Layout!.SchemaVersion);
    }

    // ------------------------------------------------------------------
    // LayoutAnnotator
    // ------------------------------------------------------------------

    private static (IReadOnlyList<CataloguePickerEntry> Merge, IReadOnlySet<string> Known) AnnotatorInputs()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(CatalogueJson);
        var bindings = BindingsFile.Parse(BindingsXml);
        Assert.True(bindings.Success, bindings.Error);
        return (CatalogueMerger.Merge(catalogue, bindings.File!), bindings.File!.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public void Annotate_FolderSlot_IsOk_LabelledWithTheTargetPagesName()
    {
        var (merge, known) = AnnotatorInputs();
        var layout = new Layout(1, new[] { TopLevel("SHIP", FolderSlot(0, "aaa")), Interior("THRUST", "aaa") });

        var annotations = LayoutAnnotator.Annotate(layout, merge, MacroKnowledge.Empty, known);

        var slot = annotations.First(a => a.PageName == "SHIP").Slot;
        Assert.Equal(SlotStatus.Ok, slot.Status);
        Assert.Equal("THRUST", slot.Label);
        Assert.Contains("Folder", slot.Reason, StringComparison.Ordinal);
        Assert.Contains("THRUST", slot.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A folder's own label override still wins, exactly as it does for an
    /// action or a macro slot - the target page's name is the DEFAULT, not a
    /// replacement for <c>ref/docs/button-naming.md</c>'s override chain.
    /// </summary>
    [Fact]
    public void Annotate_FolderSlotWithAnOverrideLabel_KeepsTheOverride()
    {
        var (merge, known) = AnnotatorInputs();
        var layout = new Layout(1, new[] { TopLevel("SHIP", FolderSlot(0, "aaa", label: "MORE")), Interior("THRUST", "aaa") });

        var annotations = LayoutAnnotator.Annotate(layout, merge, MacroKnowledge.Empty, known);

        Assert.Equal("MORE", annotations.First(a => a.PageName == "SHIP").Slot.Label);
    }

    /// <summary>
    /// A load never runs the validator (see <c>LayoutStore</c>), so a
    /// hand-edited or partially-restored file can genuinely reach here with a
    /// folder pointing at nothing. It must degrade, with a reason that says
    /// FOLDER rather than the generic "names neither an action nor a macro" -
    /// which is exactly what a commander would read as "this button is empty"
    /// and clear, destroying nothing but also explaining nothing.
    /// </summary>
    [Fact]
    public void Annotate_DanglingFolderReference_DegradesWithAFolderSpecificReason_RatherThanThrowing()
    {
        var (merge, known) = AnnotatorInputs();
        var layout = new Layout(1, new[] { TopLevel("SHIP", FolderSlot(0, "missing")) });

        var annotations = LayoutAnnotator.Annotate(layout, merge, MacroKnowledge.Empty, known);

        var slot = Assert.Single(annotations).Slot;
        Assert.Equal(SlotStatus.UnknownAction, slot.Status);
        Assert.Contains("folder", slot.Reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>PanelEndpoint.BuildResponse</c> hands the annotator a SINGLE-page
    /// synthetic layout on purpose (so a result can never be confused with
    /// another page of the same name) - which means the page a folder opens
    /// is, by construction, not in the layout being annotated. Without a
    /// separate source for the id -&gt; name lookup, every folder button on a
    /// real panel response would annotate as a dangling reference. This pins
    /// the override that makes the real endpoint work.
    /// </summary>
    [Fact]
    public void Annotate_FolderTargetOnAPageOutsideTheAnnotatedLayout_ResolvesThroughTheSuppliedPageSource()
    {
        var (merge, known) = AnnotatorInputs();
        var shipPage = TopLevel("SHIP", FolderSlot(0, "aaa"));
        var allPages = new[] { shipPage, Interior("THRUST", "aaa") };

        var annotations = LayoutAnnotator.Annotate(
            new Layout(1, new[] { shipPage }), merge, MacroKnowledge.Empty, known, allPages);

        var slot = Assert.Single(annotations).Slot;
        Assert.Equal(SlotStatus.Ok, slot.Status);
        Assert.Equal("THRUST", slot.Label);
    }
}
