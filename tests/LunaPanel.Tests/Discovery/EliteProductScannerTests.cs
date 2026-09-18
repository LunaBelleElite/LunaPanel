using LunaPanel.Core.Diagnostics;
using LunaPanel.Server.Discovery;

namespace LunaPanel.Tests.Discovery;

public class EliteProductScannerTests
{
    [Fact]
    public void Scan_BothEditionsPresent_FindsBoth()
    {
        using var temp = TempDirectory.Create();
        temp.CreateFile("Products/elite-dangerous-odyssey-64/EliteDangerous64.exe");
        temp.CreateFile("Products/FORC-FDEV-D-1010/EliteDangerous32.exe");

        var log = new DiagnosticRingBuffer(200);
        var results = EliteProductScanner.Scan(temp.Path, EliteSource.Steam, log);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Edition == EliteEdition.Odyssey);
        Assert.Contains(results, r => r.Edition == EliteEdition.Horizons);
    }

    [Fact]
    public void Scan_OdysseyExeTakesPrecedenceOverHorizonsExe_WhenBothPresentInSameFolder()
    {
        using var temp = TempDirectory.Create();
        temp.CreateFile("Products/single/EliteDangerous64.exe");
        temp.CreateFile("Products/single/EliteDangerous32.exe");

        var log = new DiagnosticRingBuffer(200);
        var results = EliteProductScanner.Scan(temp.Path, EliteSource.Steam, log);

        Assert.Single(results);
        Assert.Equal(EliteEdition.Odyssey, results[0].Edition);
    }

    [Fact]
    public void Scan_ProductFolderWithNeitherExe_IsSkippedNotThrown()
    {
        using var temp = TempDirectory.Create();
        temp.CreateFile("Products/unrecognized/SomeOtherFile.txt");

        var log = new DiagnosticRingBuffer(200);
        var results = EliteProductScanner.Scan(temp.Path, EliteSource.Steam, log);

        Assert.Empty(results);
    }

    [Fact]
    public void Scan_NoProductsFolderAtAll_ReturnsEmptyCleanly()
    {
        using var temp = TempDirectory.Create();

        var log = new DiagnosticRingBuffer(200);
        var results = EliteProductScanner.Scan(temp.Path, EliteSource.Steam, log);

        Assert.Empty(results);
    }

    [Fact]
    public void Scan_RecordsSourceOnEachInstallation()
    {
        using var temp = TempDirectory.Create();
        temp.CreateFile("Products/x/EliteDangerous64.exe");

        var log = new DiagnosticRingBuffer(200);
        var results = EliteProductScanner.Scan(temp.Path, EliteSource.Epic, log);

        Assert.Equal(EliteSource.Epic, Assert.Single(results).Source);
    }
}
