using LunaPanel.Server.Discovery;

namespace LunaPanel.Tests.Discovery;

/// <summary>
/// Pins the Valve KeyValues parse against a hand-written sample - format
/// confirmed against a real <c>libraryfolders.vdf</c> on the authoring
/// machine (single-library, so the multi-library and multi-drive shape
/// below is a format sample extrapolated from the confirmed grammar, not a
/// copy of anything real).
/// </summary>
public class SteamKeyValuesTests
{
    private const string MultiLibrarySample = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\Program Files (x86)\\Steam"
        		"label"		""
        		"apps"
        		{
        			"228980"		"413676006"
        		}
        	}
        	"1"
        	{
        		"path"		"D:\\SteamLibrary"
        		"label"		""
        		"apps"
        		{
        		}
        	}
        	"2"
        	{
        		"path"		"E:\\Games\\SteamLibrary"
        		"label"		"Games drive"
        		"apps"
        		{
        		}
        	}
        }
        """;

    [Fact]
    public void Parse_MultiLibrarySample_RootHasLibraryFoldersNode()
    {
        var root = SteamKeyValues.Parse(MultiLibrarySample);
        Assert.NotNull(root.GetNode("libraryfolders"));
    }

    [Fact]
    public void Parse_MultiLibrarySample_HasThreeNumberedChildren()
    {
        var libraryFolders = SteamKeyValues.Parse(MultiLibrarySample).GetNode("libraryfolders")!;
        Assert.Equal(new[] { "0", "1", "2" }, libraryFolders.Keys);
    }

    [Fact]
    public void Parse_MultiLibrarySample_UnescapesDoubleBackslashesInPaths()
    {
        var libraryFolders = SteamKeyValues.Parse(MultiLibrarySample).GetNode("libraryfolders")!;
        var firstPath = libraryFolders.GetNode("0")!.GetString("path");
        Assert.Equal(@"C:\Program Files (x86)\Steam", firstPath);
    }

    [Fact]
    public void Parse_MultiLibrarySample_ReadsPathsOnDifferentDrives()
    {
        var libraryFolders = SteamKeyValues.Parse(MultiLibrarySample).GetNode("libraryfolders")!;
        Assert.Equal(@"D:\SteamLibrary", libraryFolders.GetNode("1")!.GetString("path"));
        Assert.Equal(@"E:\Games\SteamLibrary", libraryFolders.GetNode("2")!.GetString("path"));
    }

    [Fact]
    public void Parse_MultiLibrarySample_EmptyLabelParsesAsEmptyString()
    {
        var libraryFolders = SteamKeyValues.Parse(MultiLibrarySample).GetNode("libraryfolders")!;
        Assert.Equal(string.Empty, libraryFolders.GetNode("0")!.GetString("label"));
    }

    [Fact]
    public void Parse_MultiLibrarySample_NestedEmptyAppsBlockParsesAsEmptyNode()
    {
        var libraryFolders = SteamKeyValues.Parse(MultiLibrarySample).GetNode("libraryfolders")!;
        var apps = libraryFolders.GetNode("1")!.GetNode("apps");
        Assert.NotNull(apps);
        Assert.Empty(apps!.Keys);
    }

    [Fact]
    public void Parse_MalformedText_DoesNotThrow()
    {
        // Degrade, don't crash - a truncated or corrupted file should not
        // take down the whole discovery pass.
        var root = SteamKeyValues.Parse("\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"");
        Assert.NotNull(root);
    }
}
