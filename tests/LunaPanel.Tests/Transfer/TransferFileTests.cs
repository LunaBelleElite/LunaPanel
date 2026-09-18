using LunaPanel.Core.Layouts;
using LunaPanel.Core.Macros;
using LunaPanel.Core.Transfer;

namespace LunaPanel.Tests.Transfer;

/// <summary>
/// The file format itself (<c>ref/docs/transfer.md</c>) - written and read
/// with no store, no HTTP and no device anywhere near it.
///
/// The claim that matters here is the round trip: what a commander exports is
/// what somebody else imports, down to the latch, the long-press and the
/// vessel context. Everything else in this file is about what happens when
/// the thing handed to LunaPanel is <em>not</em> that.
/// </summary>
public class TransferFileTests
{
    private static readonly DateTimeOffset Exported = new(2026, 9, 10, 21, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// Deliberately exercises every field a page and a slot can carry - a
    /// fixture that used one plain slot would round-trip perfectly through a
    /// writer that had silently dropped latches.
    /// </summary>
    private static Layout RichLayout() => new(
        LayoutMigrator.CurrentSchemaVersion,
        new[]
        {
            new LayoutPage(
                "SHIP",
                "t6",
                new[]
                {
                    new LayoutSlot(0, "LandingGearToggle", null, "Gear", null, Latch: false),
                    new LayoutSlot(1, null, "user-aaa111", "Dock", new LongPressAction(null, "user-bbb222"), Latch: false),
                    new LayoutSlot(2, "ShipSpotLightToggle", null, null, new LongPressAction("ToggleCargoScoop", null), Latch: true),
                },
                new[] { new LayoutSlot(9, "ToggleCargoScoop", null, "Scoop", null) },
                new[] { "Vessel:Srv" }),
            new LayoutPage("FOOT", "t9", Array.Empty<LayoutSlot>(), Array.Empty<LayoutSlot>()),
        });

    private static MacroDefinition Macro(string id, string name) =>
        MacroDefinition.Parse($$"""{ "id": "{{id}}", "name": "{{name}}", "steps": [ { "press": "UI_Down", "repeat": 2 }, { "wait": 120 } ] }""");

    /// <summary>
    /// The founding claim. Every page, every button, every name, every latch
    /// and every vessel context survives the trip out and back - compared as
    /// whole records, so a field added to <see cref="LayoutSlot"/> later is
    /// covered the day it exists rather than the day somebody remembers to
    /// assert it.
    /// </summary>
    [Fact]
    public void WriteProfile_ThenParse_RoundTripsEveryPageButtonNameLatchAndContext()
    {
        var layout = RichLayout();

        var parsed = TransferFile.Parse(TransferFile.WriteProfile(layout, Array.Empty<MacroDefinition>(), "Tablet", Exported));

        Assert.True(parsed.Success, parsed.Error);
        Assert.Equal(TransferKind.Profile, parsed.Kind);
        AssertSameLayout(layout, parsed.Layout!);
    }

    /// <summary>
    /// Field-by-field, never <c>Assert.Equal</c> over the whole
    /// <see cref="Layout"/> record: its <c>Pages</c> is an
    /// <c>IReadOnlyList</c>, so record equality compares the list by
    /// reference and two structurally identical layouts are never equal.
    ///
    /// Slots ARE compared as records, which is the point - a field added to
    /// <see cref="LayoutSlot"/> later is covered the day it exists, and a
    /// writer that dropped a latch reddens here rather than passing because
    /// both sides went through the same serializer.
    /// </summary>
    private static void AssertSameLayout(Layout expected, Layout actual)
    {
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.Pages.Count, actual.Pages.Count);

        for (var i = 0; i < expected.Pages.Count; i++)
        {
            var e = expected.Pages[i];
            var a = actual.Pages[i];

            Assert.Equal(e.Name, a.Name);
            Assert.Equal(e.TemplateId, a.TemplateId);
            Assert.Equal(e.ShowWhen, a.ShowWhen);
            Assert.Equal(e.Slots, a.Slots);
            Assert.Equal(e.Parked, a.Parked);
        }
    }

    /// <summary>
    /// The same claim, made one field at a time, because
    /// <see cref="Assert.Equal{T}(T, T)"/> over a record is a single opaque
    /// pass/fail and the report of what broke matters when it does.
    /// </summary>
    [Fact]
    public void WriteProfile_ThenParse_KeepsTheLatchTheLongPressAndTheParkedSlot()
    {
        var parsed = TransferFile.Parse(TransferFile.WriteProfile(RichLayout(), Array.Empty<MacroDefinition>(), "Tablet", Exported));

        var page = parsed.Layout!.Pages[0];
        Assert.Equal("SHIP", page.Name);
        Assert.Equal("t6", page.TemplateId);
        Assert.Equal(new[] { "Vessel:Srv" }, page.ShowWhen);
        Assert.Equal("Gear", page.Slots[0].Label);
        Assert.Equal("user-bbb222", page.Slots[1].LongPress!.Macro);
        Assert.True(page.Slots[2].Latch);
        Assert.Equal(9, Assert.Single(page.Parked).Index);
    }

    [Fact]
    public void WriteProfile_CarriesTheMacrosItWasGiven_AndParseReadsThemBack()
    {
        var macros = new[] { Macro("user-aaa111", "Dock"), Macro("user-bbb222", "Undock") };

        var parsed = TransferFile.Parse(TransferFile.WriteProfile(RichLayout(), macros, "Tablet", Exported));

        Assert.Equal(new[] { "user-aaa111", "user-bbb222" }, parsed.Macros.Select(m => m.Id));
        Assert.Equal("Undock", parsed.Macros[1].Name);
        Assert.Equal(2, parsed.Macros[0].Steps.Count);
    }

    /// <summary>
    /// A layout stores intent, never resolution
    /// (<c>ref/docs/layouts.md</c>), and that is exactly what lets a profile
    /// move to a machine bound differently. The needle is scoped to the file
    /// this writer produced, so it can only be satisfied by something this
    /// writer wrote.
    /// </summary>
    [Fact]
    public void WriteProfile_WritesActionNamesAndMacroIds_NeverAKeyOrAChord()
    {
        var json = TransferFile.WriteProfile(RichLayout(), new[] { Macro("user-aaa111", "Dock") }, "Tablet", Exported);

        Assert.Contains("LandingGearToggle", json, StringComparison.Ordinal);
        Assert.Contains("user-aaa111", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Key_", json, StringComparison.Ordinal);
        Assert.DoesNotContain("scanCode", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriteMacros_ThenParse_IsAMacrosFileWithNoLayoutAtAll()
    {
        var parsed = TransferFile.Parse(TransferFile.WriteMacros(new[] { Macro("user-aaa111", "Dock") }, "This PC", Exported));

        Assert.True(parsed.Success, parsed.Error);
        Assert.Equal(TransferKind.Macros, parsed.Kind);
        Assert.Null(parsed.Layout);
        Assert.Equal("user-aaa111", Assert.Single(parsed.Macros).Id);
    }

    /// <summary>
    /// The header a reader checks before it tries to use anything. Pinned as
    /// the literal properties, because the whole point of the envelope is
    /// that a later build can tell what a file is without parsing the rest
    /// of it.
    /// </summary>
    [Fact]
    public void WrittenFiles_CarryTheKindAndTheFormatVersion()
    {
        var profile = TransferFile.WriteProfile(RichLayout(), Array.Empty<MacroDefinition>(), "Tablet", Exported);
        var macros = TransferFile.WriteMacros(new[] { Macro("user-aaa111", "Dock") }, null, Exported);

        Assert.Contains("\"lunaPanel\": \"profile\"", profile, StringComparison.Ordinal);
        Assert.Contains("\"lunaPanel\": \"macros\"", macros, StringComparison.Ordinal);
        Assert.Contains("\"formatVersion\": 1", profile, StringComparison.Ordinal);
        Assert.Contains("\"formatVersion\": 1", macros, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"a string\"")]
    public void Parse_SomethingThatIsNotAnObjectOfOurs_IsRefused_WithNoException(string text)
    {
        var parsed = TransferFile.Parse(text);

        Assert.False(parsed.Success);
        Assert.False(string.IsNullOrWhiteSpace(parsed.Error));
    }

    [Fact]
    public void Parse_Null_IsRefused_WithNoException()
    {
        Assert.False(TransferFile.Parse(null).Success);
    }

    /// <summary>
    /// A JSON file that is simply something else - a package manifest, a
    /// game config - is told it is not a LunaPanel export, rather than
    /// failing later on a missing <c>pages</c> array.
    /// </summary>
    [Fact]
    public void Parse_ValidJsonThatIsNotOurs_SaysSo()
    {
        var parsed = TransferFile.Parse("""{ "name": "something-else", "version": "1.0.0" }""");

        Assert.False(parsed.Success);
        Assert.Contains("not a LunaPanel export", parsed.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_AnUnknownKind_IsRefused()
    {
        var parsed = TransferFile.Parse("""{ "lunaPanel": "theme", "formatVersion": 1 }""");

        Assert.False(parsed.Success);
        Assert.Contains("not a LunaPanel export", parsed.Error!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The deny direction for a file from the future: refused and named as
    /// such, never guessed at. Same rule <see cref="LayoutMigrator"/> takes
    /// for a too-new <c>schemaVersion</c>.
    /// </summary>
    [Fact]
    public void Parse_AFormatVersionFromTheFuture_IsRefused_AndSaysToUpdate()
    {
        var parsed = TransferFile.Parse($$"""{ "lunaPanel": "macros", "formatVersion": {{TransferFile.FormatVersion + 1}}, "macros": [] }""");

        Assert.False(parsed.Success);
        Assert.Contains("newer version of LunaPanel", parsed.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_AProfileWithNoLayout_IsRefused()
    {
        var parsed = TransferFile.Parse("""{ "lunaPanel": "profile", "formatVersion": 1, "macros": [] }""");

        Assert.False(parsed.Success);
        Assert.Contains("no buttons", parsed.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_AProfileWhoseLayoutIsRubbish_IsRefused()
    {
        var parsed = TransferFile.Parse("""{ "lunaPanel": "profile", "formatVersion": 1, "layout": { "nope": true }, "macros": [] }""");

        Assert.False(parsed.Success);
        Assert.Contains("could not be read", parsed.Error!, StringComparison.Ordinal);
    }

    /// <summary>
    /// One bad macro refuses the whole file. Nothing here is written
    /// anywhere, so this is only the reading half - but it is what makes the
    /// writing half safe, because the caller never gets a half-read result
    /// to act on.
    /// </summary>
    [Fact]
    public void Parse_OneUnreadableMacro_RefusesTheWholeFile()
    {
        var parsed = TransferFile.Parse(
            """
            {
              "lunaPanel": "macros",
              "formatVersion": 1,
              "macros": [
                { "id": "user-aaa111", "name": "Fine", "steps": [ { "wait": 5 } ] },
                { "id": "user-bbb222", "name": "Broken", "steps": [ { "whatIsThis": 3 } ] }
              ]
            }
            """);

        Assert.False(parsed.Success);
        Assert.Contains("macros in that file could not be read", parsed.Error!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A profile whose buttons name only shipped macros genuinely carries
    /// none. Refusing an absent array would refuse the commonest export
    /// there is.
    /// </summary>
    [Fact]
    public void Parse_NoMacrosArrayAtAll_IsEmpty_NotAnError()
    {
        var layout = TransferFile.WriteProfile(RichLayout(), Array.Empty<MacroDefinition>(), null, Exported);
        var withoutMacros = layout.Replace("\"macros\": []", "\"unrelated\": []", StringComparison.Ordinal);

        var parsed = TransferFile.Parse(withoutMacros);

        Assert.True(parsed.Success, parsed.Error);
        Assert.Empty(parsed.Macros);
    }

    /// <summary>
    /// The layout inside a profile carries its own <c>schemaVersion</c> and
    /// is migrated by the same <see cref="LayoutMigrator"/> a file on disk
    /// goes through - so a profile exported by an older build reads here
    /// exactly as an older layout file does. <c>pageName</c> is v0's
    /// spelling of what v1 calls <c>name</c>.
    /// </summary>
    [Fact]
    public void Parse_AProfileCarryingAnOlderLayoutSchema_IsMigrated_NotRefused()
    {
        var parsed = TransferFile.Parse(
            """
            {
              "lunaPanel": "profile",
              "formatVersion": 1,
              "layout": {
                "schemaVersion": 0,
                "pages": [ { "pageName": "SHIP", "templateId": "t6", "slots": [], "parked": [] } ]
              }
            }
            """);

        Assert.True(parsed.Success, parsed.Error);
        Assert.Equal(LayoutMigrator.CurrentSchemaVersion, parsed.Layout!.SchemaVersion);
        Assert.Equal("SHIP", Assert.Single(parsed.Layout.Pages).Name);
    }

    [Fact]
    public void Parse_AProfileCarryingALayoutSchemaFromTheFuture_IsRefused()
    {
        var parsed = TransferFile.Parse(
            $$"""
            {
              "lunaPanel": "profile",
              "formatVersion": 1,
              "layout": { "schemaVersion": {{LayoutMigrator.CurrentSchemaVersion + 1}}, "pages": [] }
            }
            """);

        Assert.False(parsed.Success);
        Assert.Contains("newer version of LunaPanel", parsed.Error!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The source name is display data from a file, so it goes through the
    /// same sanitiser a commander-typed device name does rather than a
    /// looser rule written here.
    /// </summary>
    [Fact]
    public void Parse_ASourceNameFullOfControlCharacters_IsSanitized_NotCarriedVerbatim()
    {
        var parsed = TransferFile.Parse(
            "{ \"lunaPanel\": \"macros\", \"formatVersion\": 1, \"sourceName\": \"  Luna\\u0007s\\ttablet  \", \"macros\": [] }");

        Assert.True(parsed.Success, parsed.Error);
        Assert.Equal("Luna s tablet", parsed.SourceName);
    }

    /// <summary>
    /// The cap is the device-name one, applied to a string that came out
    /// of a file. Forty characters is a device name's budget, and a source
    /// name is displayed in exactly the same place one is.
    /// </summary>
    [Fact]
    public void Parse_AnAbsurdlyLongSourceName_IsCapped()
    {
        var enormous = new string('x', 500);
        var parsed = TransferFile.Parse(
            $$"""{ "lunaPanel": "macros", "formatVersion": 1, "sourceName": "{{enormous}}", "macros": [] }""");

        Assert.True(parsed.Success, parsed.Error);
        Assert.Equal(LunaPanel.Core.Pairing.DeviceNaming.MaxNameLength, parsed.SourceName!.Length);
    }

    // -----------------------------------------------------------------
    // File names
    // -----------------------------------------------------------------

    [Fact]
    public void FileNameFor_ADeviceName_IsReducedToSomethingEveryFilesystemAccepts()
    {
        var name = TransferFile.FileNameFor(TransferKind.Profile, "Luna's phone / spare", Exported);

        Assert.Equal("lunapanel-profile-luna-s-phone-spare-20260910" + TransferFile.FileExtension, name);
    }

    [Fact]
    public void FileNameFor_ANameWithNothingUsableInIt_StillProducesAName()
    {
        Assert.Equal("lunapanel-macros-20260910" + TransferFile.FileExtension, TransferFile.FileNameFor(TransferKind.Macros, "///", Exported));
        Assert.Equal("lunapanel-macros-20260910" + TransferFile.FileExtension, TransferFile.FileNameFor(TransferKind.Macros, null, Exported));
    }

    /// <summary>
    /// Plain JSON to every editor and every mail client, and still
    /// recognisably ours in a downloads folder.
    /// </summary>
    [Fact]
    public void FileNameFor_AlwaysEndsInTheJsonExtension()
    {
        Assert.EndsWith(".json", TransferFile.FileNameFor(TransferKind.Profile, "Tablet", Exported), StringComparison.Ordinal);
        Assert.Equal(".lunapanel.json", TransferFile.FileExtension);
    }
}
