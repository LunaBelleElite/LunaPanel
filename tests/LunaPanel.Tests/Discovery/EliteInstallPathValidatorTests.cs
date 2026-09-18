using LunaPanel.Core.Discovery;

namespace LunaPanel.Tests.Discovery;

/// <summary>
/// Drives <see cref="EliteInstallPathValidator"/> against real temp
/// directories under the test output - shared by <c>AboutForm</c> and
/// <c>EliteSetupForm</c> (both <c>LunaPanel.Tray</c>), neither of which has
/// any test coverage of its own (no headless WinForms harness exists in
/// this project).
/// </summary>
public class EliteInstallPathValidatorTests
{
    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "elite-install-path-validator", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void IsValidEliteInstallRoot_FolderContainsProducts_ReturnsTrue()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(Path.Combine(dir, "Products"));

        Assert.True(EliteInstallPathValidator.IsValidEliteInstallRoot(dir));
    }

    [Fact]
    public void IsValidEliteInstallRoot_FolderHasNoProducts_ReturnsFalse()
    {
        var dir = NewTempDir();

        Assert.False(EliteInstallPathValidator.IsValidEliteInstallRoot(dir));
    }

    [Fact]
    public void IsValidEliteInstallRoot_GivenTheProductsFolderItself_ReturnsFalse()
    {
        var dir = NewTempDir();
        var productsFolder = Path.Combine(dir, "Products");
        Directory.CreateDirectory(productsFolder);

        // Picking "Products" itself (one level too deep) must not validate -
        // this is the exact mistake InvalidSelectionMessage warns against.
        Assert.False(EliteInstallPathValidator.IsValidEliteInstallRoot(productsFolder));
    }

    [Fact]
    public void IsValidEliteInstallRoot_NonexistentFolder_ReturnsFalse()
    {
        var dir = Path.Combine(NewTempDir(), "does-not-exist");

        Assert.False(EliteInstallPathValidator.IsValidEliteInstallRoot(dir));
    }

    [Fact]
    public void InstallRootFromProductPath_GivenARealProductPath_ReturnsTheGrandparent()
    {
        var root = @"C:\-Programs\Steam\steamapps\common\Elite Dangerous";
        var productPath = Path.Combine(root, "Products", "elite-dangerous-odyssey-64");

        Assert.Equal(root, EliteInstallPathValidator.InstallRootFromProductPath(productPath));
    }

    [Fact]
    public void InstallRootFromProductPath_APathWithNoParent_ReturnsNull()
    {
        Assert.Null(EliteInstallPathValidator.InstallRootFromProductPath(@"C:\"));
    }
}
