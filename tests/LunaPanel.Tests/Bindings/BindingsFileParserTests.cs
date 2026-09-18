using LunaPanel.Core.Bindings;

namespace LunaPanel.Tests.Bindings;

/// <summary>
/// Pins <see cref="BindingsFile.Parse"/> against the committed
/// <c>Fixtures/bindings/sample.binds</c> and <c>minimal.binds</c> fixtures,
/// plus hand-written malformed/truncated XML strings. Fixtures are read
/// from the test output directory via <see cref="AppContext.BaseDirectory"/>,
/// matching the pattern <c>FixtureIntegrityTests</c> and
/// <c>StatusJsonParserTests</c> already use.
/// </summary>
public class BindingsFileParserTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "bindings");

    private static string ReadFixture(string name) => File.ReadAllText(Path.Combine(FixturesRoot, name));

    private static BindingsFile ParseFixture(string name)
    {
        var result = BindingsFile.Parse(ReadFixture(name));
        Assert.True(result.Success, result.Error);
        return result.File!;
    }

    // ---------------------------------------------------------------
    // sample.binds
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_SampleBindsFile_Succeeds()
    {
        var result = BindingsFile.Parse(ReadFixture("sample.binds"));

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.File);
    }

    [Fact]
    public void Parse_SampleBindsFile_PresetNameAndVersion_AreReadFromRoot()
    {
        var file = ParseFixture("sample.binds");

        Assert.Equal("Custom", file.PresetName);
        Assert.Equal(4, file.MajorVersion);
        Assert.Equal(2, file.MinorVersion);
    }

    [Theory]
    [InlineData("FocusLeftPanel")]
    [InlineData("FocusCommsPanel")]
    [InlineData("FocusRightPanel")]
    [InlineData("CycleNextPanel")]
    [InlineData("CyclePreviousPanel")]
    [InlineData("UI_Up")]
    [InlineData("UI_Down")]
    [InlineData("UI_Left")]
    [InlineData("UI_Right")]
    [InlineData("UI_Select")]
    [InlineData("UI_Back")]
    [InlineData("LandingGearToggle")]
    [InlineData("ToggleCargoScoop")]
    [InlineData("ShipSpotLightToggle")]
    public void Parse_SampleBindsFile_ContainsEachRequiredAction(string actionName)
    {
        var file = ParseFixture("sample.binds");

        Assert.Contains(file.Elements, e => e.Name == actionName);
    }

    [Fact]
    public void Parse_SampleBindsFile_DoesNotTreatKeyboardLayoutAsABindingElement()
    {
        var file = ParseFixture("sample.binds");

        Assert.DoesNotContain(file.Elements, e => e.Name == "KeyboardLayout");
    }

    // ---------------------------------------------------------------
    // minimal.binds
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_MinimalBindsFile_Succeeds()
    {
        var result = BindingsFile.Parse(ReadFixture("minimal.binds"));

        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public void Parse_MinimalBindsFile_DoesNotTreatKeyboardLayoutAsABindingElement()
    {
        var file = ParseFixture("minimal.binds");

        Assert.DoesNotContain(file.Elements, e => e.Name == "KeyboardLayout");
    }

    [Fact]
    public void Parse_MinimalBindsFile_DuplicatedElement_KeepsOnlyFirstOccurrence()
    {
        var file = ParseFixture("minimal.binds");

        var matches = file.Elements.Where(e => e.Name == "DuplicatedElement").ToList();

        Assert.Single(matches);
        Assert.Equal("Key_D", matches[0].Primary?.Key);
    }

    [Fact]
    public void Parse_MinimalBindsFile_PrimaryMouseSecondaryKeyboard_SlotsParsedAsWritten()
    {
        var file = ParseFixture("minimal.binds");

        var element = file.Elements.Single(e => e.Name == "PrimaryMouseSecondaryKeyboard");

        Assert.Equal("Mouse", element.Primary?.Device);
        Assert.Equal("Mouse_1", element.Primary?.Key);
        Assert.Equal("Keyboard", element.Secondary?.Device);
        Assert.Equal("Key_C", element.Secondary?.Key);
    }

    [Fact]
    public void Parse_MinimalBindsFile_TwoModifiers_ParsedInFileOrder()
    {
        var file = ParseFixture("minimal.binds");

        var element = file.Elements.Single(e => e.Name == "TwoModifiers");

        Assert.Equal("Key_Space", element.Primary?.Key);
        Assert.Equal(2, element.Primary?.Modifiers.Count);
        Assert.Equal("Key_LeftControl", element.Primary?.Modifiers[0].Key);
        Assert.Equal("Key_LeftAlt", element.Primary?.Modifiers[1].Key);
    }

    // ---------------------------------------------------------------
    // Malformed / truncated XML - must never throw
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_EmptyString_ReturnsFailure_NotException()
    {
        var result = BindingsFile.Parse("");

        Assert.False(result.Success);
        Assert.Null(result.File);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_TruncatedXml_ReturnsFailure_NotException()
    {
        var result = BindingsFile.Parse("<Root PresetName=\"Custom\"><UI_Up><Primary Device=\"Keyboard\" Key=\"Key_W\"");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_NotXmlAtAll_ReturnsFailure_NotException()
    {
        var result = BindingsFile.Parse("this is not xml at all");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_WrongRootElementName_ReturnsFailure()
    {
        var result = BindingsFile.Parse("<NotRoot></NotRoot>");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }
}
