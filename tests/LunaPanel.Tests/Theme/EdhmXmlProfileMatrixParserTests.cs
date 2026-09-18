using LunaPanel.Core.Theme;

namespace LunaPanel.Tests.Theme;

/// <summary>
/// Pins <see cref="EdhmXmlProfileMatrixParser.Parse"/> against the
/// committed <c>Fixtures/edhm/XML-Profile.sample.ini</c> fixture, whose own
/// comment explains it exists specifically to catch a row/column
/// transposition bug (every value is distinct).
/// </summary>
public class EdhmXmlProfileMatrixParserTests
{
    private static string ReadFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "edhm", "XML-Profile.sample.ini"));

    [Fact]
    public void Parse_SampleFixture_RowsMatchDocumentedLayout_NotTransposed()
    {
        var matrix = EdhmXmlProfileMatrixParser.Parse(ReadFixture());

        Assert.NotNull(matrix);
        Assert.Equal(new[] { 0.11, 0.22, 0.33 }, matrix!.Value.Red);
        Assert.Equal(new[] { 0.44, 0.55, 0.66 }, matrix.Value.Green);
        Assert.Equal(new[] { 0.77, 0.88, 0.99 }, matrix.Value.Blue);
    }

    [Fact]
    public void Parse_MissingOneOfNineKeys_ReturnsNull()
    {
        // Same fixture with z152 removed - one missing key out of nine
        // must fail the whole matrix, not silently default it to 0.
        const string ini = """
        [Constants]
        x150 = 0.11
        y150 = 0.22
        z150 = 0.33
        x151 = 0.44
        y151 = 0.55
        z151 = 0.66
        x152 = 0.77
        y152 = 0.88
        """;

        Assert.Null(EdhmXmlProfileMatrixParser.Parse(ini));
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsNull()
    {
        Assert.Null(EdhmXmlProfileMatrixParser.Parse(""));
    }

    [Fact]
    public void Parse_NonNumericValue_IsTreatedAsMissing_ReturnsNull()
    {
        const string ini = """
        [Constants]
        x150 = notanumber
        y150 = 0.22
        z150 = 0.33
        x151 = 0.44
        y151 = 0.55
        z151 = 0.66
        x152 = 0.77
        y152 = 0.88
        z152 = 0.99
        """;

        Assert.Null(EdhmXmlProfileMatrixParser.Parse(ini));
    }
}
