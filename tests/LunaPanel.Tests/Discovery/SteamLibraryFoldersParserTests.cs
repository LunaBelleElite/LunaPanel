using LunaPanel.Server.Discovery;

namespace LunaPanel.Tests.Discovery;

public class SteamLibraryFoldersParserTests
{
    private const string ThreeLibrariesOnDifferentDrives = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\Program Files (x86)\\Steam"
        	}
        	"1"
        	{
        		"path"		"D:\\SteamLibrary"
        	}
        	"2"
        	{
        		"path"		"E:\\Games\\SteamLibrary"
        	}
        }
        """;

    [Fact]
    public void ExtractLibraryPaths_ThreeLibraries_ReturnsAllThreeInOrder()
    {
        var paths = SteamLibraryFoldersParser.ExtractLibraryPaths(ThreeLibrariesOnDifferentDrives);

        Assert.Equal(
            new[]
            {
                @"C:\Program Files (x86)\Steam",
                @"D:\SteamLibrary",
                @"E:\Games\SteamLibrary",
            },
            paths);
    }

    [Fact]
    public void ExtractLibraryPaths_NoLibraryFoldersNode_ReturnsEmpty()
    {
        var paths = SteamLibraryFoldersParser.ExtractLibraryPaths("\"somethingElse\" { }");
        Assert.Empty(paths);
    }

    [Fact]
    public void ExtractLibraryPaths_EntryWithNoPath_IsSkipped()
    {
        const string vdf = """
            "libraryfolders"
            {
            	"0"
            	{
            		"label"		"no path here"
            	}
            	"1"
            	{
            		"path"		"C:\\Games"
            	}
            }
            """;

        var paths = SteamLibraryFoldersParser.ExtractLibraryPaths(vdf);
        Assert.Equal(new[] { @"C:\Games" }, paths);
    }
}
