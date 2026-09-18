using LunaPanel.Core.Theme;

namespace LunaPanel.Tests.Theme;

/// <summary>
/// Pins <see cref="GraphicsConfigMatrixParser.ParseGuiColourDefault"/>
/// against the three committed <c>Fixtures/graphics/</c> fixtures.
/// </summary>
public class GraphicsConfigMatrixParserTests
{
    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "graphics", name));

    [Fact]
    public void Parse_IdentityFixture_ReturnsIdentityMatrix()
    {
        var matrix = GraphicsConfigMatrixParser.ParseGuiColourDefault(ReadFixture("guicolour-identity.xml"));

        Assert.NotNull(matrix);
        Assert.Equal(new[] { 1.0, 0.0, 0.0 }, matrix!.Value.Red);
        Assert.Equal(new[] { 0.0, 1.0, 0.0 }, matrix.Value.Green);
        Assert.Equal(new[] { 0.0, 0.0, 1.0 }, matrix.Value.Blue);
    }

    [Fact]
    public void Parse_CustomFixture_ReturnsExpectedNonIdentityRows()
    {
        var matrix = GraphicsConfigMatrixParser.ParseGuiColourDefault(ReadFixture("guicolour-custom.xml"));

        Assert.NotNull(matrix);
        Assert.Equal(new[] { 0.45, 0.35, 0.20 }, matrix!.Value.Red);
        Assert.Equal(new[] { 0.10, 0.70, 0.20 }, matrix.Value.Green);
        Assert.Equal(new[] { 0.05, 0.15, 0.80 }, matrix.Value.Blue);
    }

    [Fact]
    public void Parse_OverrideEmptyFixture_HasNoGuiColour_ReturnsNull()
    {
        // The common real-world case per the task brief: a player-set
        // override file that exists but is empty.
        Assert.Null(GraphicsConfigMatrixParser.ParseGuiColourDefault(ReadFixture("override-empty.xml")));
    }

    [Fact]
    public void Parse_MalformedXml_ReturnsNull_NotThrown()
    {
        Assert.Null(GraphicsConfigMatrixParser.ParseGuiColourDefault("<GraphicsConfig><Unclosed>"));
    }

    [Fact]
    public void Parse_MatrixRowWithWrongComponentCount_ReturnsNull()
    {
        const string xml = """
        <GraphicsConfig>
            <GUIColour>
                <Default>
                    <MatrixRed>1, 0</MatrixRed>
                    <MatrixGreen>0, 1, 0</MatrixGreen>
                    <MatrixBlue>0, 0, 1</MatrixBlue>
                </Default>
            </GUIColour>
        </GraphicsConfig>
        """;

        Assert.Null(GraphicsConfigMatrixParser.ParseGuiColourDefault(xml));
    }
}
