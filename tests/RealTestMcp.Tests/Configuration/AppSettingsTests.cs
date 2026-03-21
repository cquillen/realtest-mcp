using RealTestMcp.Core.Configuration;

namespace RealTestMcp.Tests.Configuration;

public class AppSettingsTests
{
    [Fact]
    public void DbPath_ExpandsEnvironmentVariables()
    {
        var settings = new AppSettings();
        settings.Database.Path = "%LOCALAPPDATA%\\RealTestMcp\\realtest.db";

        Assert.DoesNotContain("%LOCALAPPDATA%", settings.Database.Path);
        Assert.Contains("RealTestMcp", settings.Database.Path);
    }

    [Fact]
    public void ScriptPaths_ExpandsAllEntries()
    {
        var settings = new AppSettings();
        settings.RealTest.ScriptPaths = ["%LOCALAPPDATA%\\Scripts", @"C:\RealTest\Examples"];

        Assert.All(settings.RealTest.ScriptPaths, p => Assert.DoesNotContain("%", p));
    }

    [Fact]
    public void DefaultDbPath_IsUnderLocalAppData()
    {
        var settings = new AppSettings();
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(expected, settings.Database.Path);
    }
}
