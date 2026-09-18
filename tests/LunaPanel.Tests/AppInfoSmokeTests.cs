using LunaPanel.Core;

namespace LunaPanel.Tests;

/// <summary>
/// Proves the solution wiring is real: this test references a type that
/// lives only in LunaPanel.Core, so it can only compile and pass if the
/// Tests -> Core project reference actually resolves, and it pins the
/// value of <see cref="AppInfo.Name"/> against silent drift.
/// </summary>
public class AppInfoSmokeTests
{
    [Fact]
    public void AppInfo_Name_IsLunaPanel()
    {
        Assert.Equal("LunaPanel", AppInfo.Name);
    }
}
