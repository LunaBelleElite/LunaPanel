using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Theme;
using LunaPanel.Server.Http;
using LunaPanel.Server.Layouts;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Drives <see cref="LayoutResetEndpoint"/> against a real
/// <see cref="LayoutStore"/> (and, for the "untouched" claims, a real
/// <see cref="ThemeOverrideStore"/>) over a temp directory - same discipline
/// as <see cref="LayoutImportEndpointTests"/>: the <c>.bak</c> generation
/// asserted on is the one <see cref="LayoutStore"/> itself writes, never one
/// this test made.
/// </summary>
public class LayoutResetEndpointTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private sealed record Harness(LayoutStore Store, CapturingDiagnosticLog Log, string Directory);

    private static Harness NewHarness([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "layout-reset", testName, Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        var log = new CapturingDiagnosticLog();
        return new Harness(new LayoutStore(dir, log), log, dir);
    }

    /// <summary>
    /// A two-page custom arrangement, deliberately unlike the one-page
    /// starter - the only way a "replaces every page" claim can be
    /// distinguished from "replaces the first page and leaves the rest".
    /// </summary>
    private static Layout TwoPageCustomLayout() => new(1, new[]
    {
        new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, "LandingGearToggle", null, "MINE", null) }, Array.Empty<LayoutSlot>()),
        new LayoutPage("EXTRA", "t6", new[] { new LayoutSlot(0, "ToggleCargoScoop", null, "EXTRAPAGE", null) }, Array.Empty<LayoutSlot>()),
    });

    /// <summary>
    /// Every action name either starter variant uses (phone and tablet - see
    /// <see cref="StarterLayout.LoadForDeviceClass"/>), so either can be
    /// saved.
    /// </summary>
    private static IReadOnlySet<string> StarterKnownActions()
    {
        var names = new HashSet<string>(StringComparer.Ordinal) { "LandingGearToggle", "ToggleCargoScoop" };
        foreach (var layout in new[] { StarterLayout.Load(), StarterLayout.LoadForDeviceClass("tablet") })
        {
            foreach (var page in layout.Pages)
            {
                foreach (var slot in page.Slots.Concat(page.Parked))
                {
                    if (slot.Action is not null)
                    {
                        names.Add(slot.Action);
                    }

                    if (slot.LongPress?.Action is not null)
                    {
                        names.Add(slot.LongPress.Action);
                    }
                }
            }
        }

        return names;
    }

    [Fact]
    public void Reset_ReplacesEveryPage_NotJustTheVisibleOne()
    {
        var harness = NewHarness();
        harness.Store.Save("ME", TwoPageCustomLayout(), StarterKnownActions());

        var outcome = LayoutResetEndpoint.Reset(harness.Store, "ME", "phone", StarterKnownActions(), harness.Log);

        Assert.Equal(LayoutResetEndpoint.ResetOutcome.Reset, outcome);
        var result = harness.Store.Load("ME");
        Assert.Equal(LayoutLoadOutcome.Loaded, result.Outcome);

        // The custom second page is gone entirely - not merged with, not
        // left standing alongside the starter's own page.
        Assert.Equal(StarterLayout.Load().Pages.Count, result.Layout!.Pages.Count);
        Assert.DoesNotContain(result.Layout.Pages, p => p.Name == "EXTRA");
        Assert.DoesNotContain(
            result.Layout.Pages.SelectMany(p => p.Slots).Select(s => s.Label ?? string.Empty),
            label => label is "MINE" or "EXTRAPAGE");
    }

    /// <summary>
    /// The result is not merely "not the custom arrangement" - it is
    /// genuinely what a freshly-paired device of this class receives, page
    /// names and slot actions alike, compared against <see cref="StarterLayout"/>
    /// itself rather than a re-typed fixture that could quietly drift from it.
    /// </summary>
    [Fact]
    public void Reset_ResultEquals_WhatAFreshlyPairedDeviceReceives()
    {
        var harness = NewHarness();
        harness.Store.Save("ME", TwoPageCustomLayout(), StarterKnownActions());

        LayoutResetEndpoint.Reset(harness.Store, "ME", "phone", StarterKnownActions(), harness.Log);

        var result = harness.Store.Load("ME");
        var starter = StarterLayout.Load();

        Assert.Equal(starter.Pages.Select(p => p.Name), result.Layout!.Pages.Select(p => p.Name));
        Assert.Equal(starter.Pages.Select(p => p.TemplateId), result.Layout.Pages.Select(p => p.TemplateId));
        Assert.Equal(
            starter.Pages.SelectMany(p => p.Slots).Select(s => (s.Index, s.Action, s.Macro)),
            result.Layout.Pages.SelectMany(p => p.Slots).Select(s => (s.Index, s.Action, s.Macro)));
    }

    [Fact]
    public void Reset_LeavesTheOldArrangementInBak_AndUndoRestoresIt()
    {
        var harness = NewHarness();
        harness.Store.Save("ME", TwoPageCustomLayout(), StarterKnownActions());

        LayoutResetEndpoint.Reset(harness.Store, "ME", "phone", StarterKnownActions(), harness.Log);

        var bakPath = Path.Combine(harness.Directory, "layout-ME.json.bak");
        Assert.True(File.Exists(bakPath));
        Assert.Contains("EXTRAPAGE", File.ReadAllText(bakPath), StringComparison.Ordinal);

        Assert.True(LayoutImportEndpoint.Undo(harness.Store, "ME"));

        var restored = harness.Store.Load("ME");
        Assert.Equal(LayoutLoadOutcome.Loaded, restored.Outcome);
        Assert.Contains(restored.Layout!.Pages, p => p.Name == "EXTRA");
    }

    [Fact]
    public void Reset_AnotherDevicesLayout_IsUntouched()
    {
        var harness = NewHarness();
        harness.Store.Save("ME", TwoPageCustomLayout(), StarterKnownActions());
        harness.Store.Save("OTHER", TwoPageCustomLayout(), StarterKnownActions());

        LayoutResetEndpoint.Reset(harness.Store, "ME", "phone", StarterKnownActions(), harness.Log);

        var other = harness.Store.Load("OTHER");
        Assert.Equal(LayoutLoadOutcome.Loaded, other.Outcome);
        Assert.Contains(other.Layout!.Pages, p => p.Name == "EXTRA");
        Assert.False(File.Exists(Path.Combine(harness.Directory, "layout-OTHER.json.bak")));
    }

    /// <summary>
    /// Reset touches only <see cref="LayoutStore"/> - a manual colour
    /// override for the same device, stored in a completely separate file
    /// beside it, must survive byte-for-byte. A "reset" that quietly cleared
    /// more than button placement would be a factory reset wearing this
    /// feature's name (<c>ref/docs/reset-to-default.md</c>'s "What this is
    /// not").
    /// </summary>
    [Fact]
    public void Reset_TheThemeOverride_IsUntouched()
    {
        var harness = NewHarness();
        var themeLog = new CapturingDiagnosticLog();
        var themeStore = new ThemeOverrideStore(harness.Directory, themeLog);
        var overrideValue = new ThemeOverride(
            new HudColor(10, 20, 30), new HudColor(40, 50, 60), new HudColor(70, 80, 90));
        themeStore.Save("ME", overrideValue);
        harness.Store.Save("ME", TwoPageCustomLayout(), StarterKnownActions());

        LayoutResetEndpoint.Reset(harness.Store, "ME", "phone", StarterKnownActions(), harness.Log);

        var stillThere = themeStore.Load("ME");
        Assert.Equal(ThemeOverrideLoadOutcome.Loaded, stillThere.Outcome);
        Assert.Equal(overrideValue, stillThere.Override);
    }

    /// <summary>
    /// A save the validator refuses (here, forced by supplying no known
    /// action names at all, which the starter's own actions cannot satisfy)
    /// must leave the device's existing arrangement exactly as it was -
    /// the same "refused before it ever reaches disk" contract
    /// <see cref="LayoutStore.Save"/> already gives every other caller.
    /// </summary>
    [Fact]
    public void Reset_ARefusedSave_ReturnsCouldNotSave_AndLeavesTheOldLayoutInPlace()
    {
        var harness = NewHarness();
        harness.Store.Save("ME", TwoPageCustomLayout(), StarterKnownActions());

        var outcome = LayoutResetEndpoint.Reset(harness.Store, "ME", "phone", new HashSet<string>(StringComparer.Ordinal), harness.Log);

        Assert.Equal(LayoutResetEndpoint.ResetOutcome.CouldNotSave, outcome);
        var stillMine = harness.Store.Load("ME");
        Assert.Contains(stillMine.Layout!.Pages, p => p.Name == "EXTRA");
        Assert.False(File.Exists(Path.Combine(harness.Directory, "layout-ME.json.bak")));
    }

    // -------------------------------------------------------------------
    // deviceClass-aware reset (2026-09-16) - a tablet that resets gets its
    // own 64-slot SHIP page back, not the phone's 30-slot one.
    // -------------------------------------------------------------------

    [Fact]
    public void Reset_DeviceClassTablet_RestoresTheTabletVariant()
    {
        var harness = NewHarness();
        harness.Store.Save("ME", TwoPageCustomLayout(), StarterKnownActions());

        var outcome = LayoutResetEndpoint.Reset(harness.Store, "ME", "tablet", StarterKnownActions(), harness.Log);

        Assert.Equal(LayoutResetEndpoint.ResetOutcome.Reset, outcome);
        var result = harness.Store.Load("ME");
        var tabletStarter = StarterLayout.LoadForDeviceClass("tablet");

        Assert.Equal("t64", result.Layout!.Pages[0].TemplateId);
        Assert.Equal(tabletStarter.Pages.Select(p => p.Name), result.Layout.Pages.Select(p => p.Name));
        Assert.Equal(tabletStarter.Pages.Select(p => p.TemplateId), result.Layout.Pages.Select(p => p.TemplateId));
    }

    [Theory]
    [InlineData("phone")]
    [InlineData("unknown")]
    public void Reset_DeviceClassPhoneOrUnknown_RestoresThePhoneVariant(string deviceClass)
    {
        var harness = NewHarness();
        harness.Store.Save("ME", TwoPageCustomLayout(), StarterKnownActions());

        var outcome = LayoutResetEndpoint.Reset(harness.Store, "ME", deviceClass, StarterKnownActions(), harness.Log);

        Assert.Equal(LayoutResetEndpoint.ResetOutcome.Reset, outcome);
        var result = harness.Store.Load("ME");

        Assert.Equal("t30", result.Layout!.Pages[0].TemplateId);
        Assert.Equal(StarterLayout.Load().Pages.Select(p => p.Name), result.Layout.Pages.Select(p => p.Name));
    }
}
