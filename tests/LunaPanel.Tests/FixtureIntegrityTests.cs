using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LunaPanel.Tests;

/// <summary>
/// Pins the shape of the test fixture corpus under Fixtures/, so a later task
/// cannot silently ship a corrupted, truncated, or re-personalised copy of a
/// hand-authored fixture and have every parser test still pass against
/// garbage. These tests know nothing about how any real parser will be built
/// later - they only guard the raw fixture bytes on disk.
///
/// Every fixture under Fixtures/ is hand-authored for this project. None of
/// it is a copy of, or close paraphrase of, a real Elite Dangerous, EDHM-UI,
/// or other third-party file. See FixtureProvenance_NoFileContainsForbiddenThirdPartyMarkers
/// below, which is the test that is meant to catch a regression of that fact.
/// </summary>
public class FixtureIntegrityTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static string Path_(params string[] parts) =>
        Path.Combine(new[] { FixturesRoot }.Concat(parts).ToArray());

    // ---------------------------------------------------------------
    // bindings/sample.binds
    // ---------------------------------------------------------------

    [Fact]
    public void SampleBindsFile_RootElement_HasMajorVersion4()
    {
        var doc = XDocument.Load(Path_("bindings", "sample.binds"));
        var major = doc.Root!.Attribute("MajorVersion")!.Value;
        Assert.Equal("4", major);
    }

    [Fact]
    public void SampleBindsFile_Contains_FocusLeftPanelElement()
    {
        var text = File.ReadAllText(Path_("bindings", "sample.binds"));
        Assert.Contains("<FocusLeftPanel>", text);
    }

    [Fact]
    public void SampleBindsFile_Contains_AllRequiredActionElements()
    {
        var text = File.ReadAllText(Path_("bindings", "sample.binds"));
        string[] requiredActions =
        {
            "FocusLeftPanel", "FocusCommsPanel", "FocusRightPanel",
            "CycleNextPanel", "CyclePreviousPanel",
            "UI_Up", "UI_Down", "UI_Left", "UI_Right", "UI_Select", "UI_Back",
            "LandingGearToggle", "ToggleCargoScoop", "ShipSpotLightToggle",
        };

        foreach (var action in requiredActions)
        {
            Assert.Contains($"<{action}>", text);
        }
    }

    // ---------------------------------------------------------------
    // bindings/minimal.binds
    // ---------------------------------------------------------------

    [Fact]
    public void MinimalBindsFile_ParsesAsValidXml()
    {
        // Throws on malformed XML - the assertion is that this does not throw.
        var doc = XDocument.Load(Path_("bindings", "minimal.binds"));
        Assert.Equal("Root", doc.Root!.Name.LocalName);
    }

    [Fact]
    public void MinimalBindsFile_ContainsDuplicatedElement_Twice()
    {
        var text = File.ReadAllText(Path_("bindings", "minimal.binds"));
        var openTagCount = CountOccurrences(text, "<DuplicatedElement>");
        Assert.Equal(2, openTagCount);
    }

    // ---------------------------------------------------------------
    // bindings/versions/ - synthetic tree for discovery testing
    // ---------------------------------------------------------------

    private sealed record BindsVersion(int Major, int Minor, string Preset, string FileName);

    private static BindsVersion? FindLatestBindsVersion(string versionsDir)
    {
        // Deliberately exact: no trailing characters allowed, so a
        // "Custom.4.3.binds.<epoch>.backup" file cannot match this pattern.
        var regex = new Regex(@"^(?<preset>[^.]+)\.(?<major>\d+)\.(?<minor>\d+)\.binds$");
        BindsVersion? latest = null;

        foreach (var file in Directory.EnumerateFiles(versionsDir))
        {
            var name = Path.GetFileName(file);
            var match = regex.Match(name);
            if (!match.Success)
            {
                continue;
            }

            var major = int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture);
            var minor = int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture);

            if (latest is null || (major, minor).CompareTo((latest.Major, latest.Minor)) > 0)
            {
                latest = new BindsVersion(major, minor, match.Groups["preset"].Value, name);
            }
        }

        return latest;
    }

    [Fact]
    public void BindingsVersionsDirectory_Discovery_PicksHighestVersion_IgnoringBackup()
    {
        var latest = FindLatestBindsVersion(Path_("bindings", "versions"));

        Assert.NotNull(latest);
        Assert.Equal(4, latest!.Major);
        Assert.Equal(3, latest.Minor);
        Assert.Equal("Custom", latest.Preset);
        Assert.Equal("Custom.4.3.binds", latest.FileName);
    }

    [Fact]
    public void StartPresetFile_HasExactlyFourPresetNameLines()
    {
        var lines = File.ReadAllLines(Path_("bindings", "versions", "StartPreset.4.start"));
        Assert.Equal(4, lines.Length);
    }

    // ---------------------------------------------------------------
    // edhm/ThemeSettings.sample.json
    // ---------------------------------------------------------------

    [Fact]
    public void ThemeSettingsSampleJson_Parses_AndHasExactlyFourUiGroups()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path_("edhm", "ThemeSettings.sample.json")));
        var uiGroups = doc.RootElement.GetProperty("ui_groups");
        Assert.Equal(4, uiGroups.GetArrayLength());
    }

    [Fact]
    public void ThemeSettingsSampleJson_ExactlyOneUiGroup_HasNullElements()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path_("edhm", "ThemeSettings.sample.json")));
        var uiGroups = doc.RootElement.GetProperty("ui_groups");

        var nullElementsCount = uiGroups.EnumerateArray()
            .Count(g => g.GetProperty("Elements").ValueKind == JsonValueKind.Null);

        Assert.Equal(1, nullElementsCount);
    }

    // ---------------------------------------------------------------
    // edhm/Advanced.sample.ini + Startup-Profile.sample.ini - the sRGB
    // decode pin
    // ---------------------------------------------------------------

    private static Dictionary<string, double> ParseIniConstants(string path)
    {
        var result = new Dictionary<string, double>();

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('['))
            {
                continue;
            }

            var parts = trimmed.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var key = parts[0].Trim();
            if (double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// EDHM-UI stores linear-light float components; this is the standard
    /// sRGB (piecewise gamma) transfer curve used to encode a linear value
    /// into an 8-bit display byte.
    /// </summary>
    private static byte SrgbEncode(double linear)
    {
        var encoded = linear <= 0.0031308
            ? 12.92 * linear
            : (1.055 * Math.Pow(linear, 1.0 / 2.4)) - 0.055;

        return (byte)Math.Round(encoded * 255.0, MidpointRounding.AwayFromZero);
    }

    [Fact]
    public void SrgbEncode_ReferencePair_MatchesRealEdhmMeasurement()
    {
        // These two points were measured against a real EDHM-UI install
        // during the original (since-deleted) fixture pass: a linear value
        // of 0.3005 was observed to decode to display byte 0x95, and 0.9647
        // to 0xFB. They are not derived from, or copied out of, any fixture
        // in this repo - they are two isolated data points, kept here only
        // as proof that our sRGB decode formula matches EDHM's own intent.
        Assert.Equal((byte)0x95, SrgbEncode(0.3005));
        Assert.Equal((byte)0xFB, SrgbEncode(0.9647));
    }

    [Fact]
    public void SrgbEncode_AdvancedSampleIniValues_MatchIndependentlyComputedBytes()
    {
        var constants = ParseIniConstants(Path_("edhm", "Advanced.sample.ini"));

        // Expected bytes below were computed independently (via a one-off
        // Node.js calculation against the same documented formula), not by
        // calling SrgbEncode a second time against itself.
        Assert.Equal((byte)0xCC, SrgbEncode(constants["x77"])); // 0.6023
        Assert.Equal((byte)0x5F, SrgbEncode(constants["y77"])); // 0.1152
        Assert.Equal((byte)0xF1, SrgbEncode(constants["z77"])); // 0.8804
        Assert.Equal((byte)0xFF, SrgbEncode(constants["w77"])); // 1.0
        Assert.Equal((byte)0x07, SrgbEncode(constants["x78"])); // 0.002 - below the 0.0031308 threshold; exercises the linear branch
        Assert.Equal((byte)0x7C, SrgbEncode(constants["y78"])); // 0.2
        Assert.Equal((byte)0xCB, SrgbEncode(constants["z78"])); // 0.6
        Assert.Equal((byte)0xE7, SrgbEncode(constants["w78"])); // 0.8
    }

    // ---------------------------------------------------------------
    // edhm/Settings.sample.json
    // ---------------------------------------------------------------

    [Fact]
    public void EdhmSettingsSampleJson_ContainsAtLeastOneInstanceWithEmptyPath()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path_("edhm", "Settings.sample.json")));

        var hasEmptyPath = doc.RootElement.GetProperty("GameInstances")
            .EnumerateArray()
            .SelectMany(gi => gi.GetProperty("games").EnumerateArray())
            .Any(g => g.GetProperty("path").GetString() == "");

        Assert.True(hasEmptyPath, "Settings.sample.json must keep at least one game instance with an empty path - real files have those and a parser must skip them.");
    }

    // ---------------------------------------------------------------
    // graphics/
    // ---------------------------------------------------------------

    [Fact]
    public void GuiColourIdentityXml_DefaultMatrixRed_IsIdentityRow()
    {
        var doc = XDocument.Load(Path_("graphics", "guicolour-identity.xml"));
        var matrixRed = doc.Root!.Element("GUIColour")!.Element("Default")!.Element("MatrixRed")!.Value.Trim();
        Assert.Equal("1, 0, 0", matrixRed);
    }

    [Fact]
    public void GuiColourCustomXml_DefaultMatrixRed_IsNotIdentityRow()
    {
        var doc = XDocument.Load(Path_("graphics", "guicolour-custom.xml"));
        var matrixRed = doc.Root!.Element("GUIColour")!.Element("Default")!.Element("MatrixRed")!.Value.Trim();
        Assert.NotEqual("1, 0, 0", matrixRed);
    }

    [Fact]
    public void OverrideEmptyXml_RootElement_HasNoChildren()
    {
        var doc = XDocument.Load(Path_("graphics", "override-empty.xml"));
        Assert.Equal("GraphicsConfig", doc.Root!.Name.LocalName);
        Assert.Empty(doc.Root.Elements());
    }

    // ---------------------------------------------------------------
    // status/
    // ---------------------------------------------------------------

    [Fact]
    public void ClosedStatusJson_HasExactlyThreeProperties()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path_("status", "closed.json")));
        var propertyCount = doc.RootElement.EnumerateObject().Count();
        Assert.Equal(3, propertyCount);
    }

    // ---------------------------------------------------------------
    // journal/
    //
    // Synthesised, not captured: a real journal line carries the
    // commander's own name and the systems they have visited. What makes
    // this fixture worth having is three specific properties, each pinned
    // below, because JournalTailerTests' back-scan test stops proving
    // anything if any of them quietly changes.
    // ---------------------------------------------------------------

    private const string JournalFixtureName = "Journal.2026-01-01T120000.01.log";

    [Fact]
    public void JournalFixture_LoadGameLine_UsesTheCapitalLSpelling()
    {
        // "Lander01", not "lander01". This is the whole reason the back-scan
        // test can catch a case-sensitive comparison.
        var text = File.ReadAllText(Path_("journal", JournalFixtureName));

        Assert.Contains("\"event\":\"LoadGame\"", text, StringComparison.Ordinal);
        Assert.Contains("\"Ship\":\"Lander01\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void JournalFixture_ContainsALoadoutLineNamingADifferentShip()
    {
        // Loadout also carries a "Ship" property, naming the main ship. It is
        // in the fixture on purpose: a tailer that read the vessel type off
        // any "Ship" property instead of off LoadGame specifically lands on
        // "krait_mkii" here, which is a visible wrong answer rather than a
        // silent right one.
        var text = File.ReadAllText(Path_("journal", JournalFixtureName));

        Assert.Contains("\"event\":\"Loadout\"", text, StringComparison.Ordinal);
        Assert.Contains("\"Ship\":\"krait_mkii\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void JournalFixture_NamesNoVesselAnywhereExceptLoadGame()
    {
        // If a LaunchVessel or DockSRV line ever appears in this fixture, the
        // back-scan test stops proving that LoadGame is the anchor - it would
        // pass off the later event instead.
        var text = File.ReadAllText(Path_("journal", JournalFixtureName));

        Assert.DoesNotContain("LaunchVessel", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DockSRV", text, StringComparison.Ordinal);
    }

    [Fact]
    public void JournalFixture_EveryLineIsAJsonObjectCarryingAnEventName()
    {
        var lines = File.ReadAllLines(Path_("journal", JournalFixtureName))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
            Assert.True(doc.RootElement.TryGetProperty("event", out var name));
            Assert.False(string.IsNullOrEmpty(name.GetString()));
        }
    }

    // ---------------------------------------------------------------
    // Provenance - the point of this redo
    // ---------------------------------------------------------------

    [Fact]
    public void FixtureProvenance_NoFileContainsForbiddenThirdPartyMarkers()
    {
        string[] forbiddenMarkers =
        {
            "Owner", "RiCo", "Tsakari", "Strawberry_Milkshake", "steamapps", "AppData",
        };

        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(FixturesRoot, "*", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var relativePath = Path.GetRelativePath(FixturesRoot, file);

            foreach (var marker in forbiddenMarkers)
            {
                if (text.Contains(marker, StringComparison.Ordinal))
                {
                    violations.Add($"{relativePath}: contains forbidden marker \"{marker}\"");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Forbidden third-party/personal markers found in fixtures:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
