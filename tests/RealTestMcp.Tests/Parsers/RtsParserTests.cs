using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Tests.Parsers;

public class RtsParserTests
{
    private static string ScriptsDir => Path.Combine(AppContext.BaseDirectory, "data", "scripts");

    [Fact]
    public void ParseFile_ReturnsContent()
    {
        var result = RtsParser.ParseFile(Path.Combine(ScriptsDir, "mean-reversion.rts"));
        Assert.Contains("RSI", result.Content);
    }

    [Fact]
    public void ParseFile_FilePath_IsAbsolute()
    {
        var result = RtsParser.ParseFile(Path.Combine(ScriptsDir, "mean-reversion.rts"));
        Assert.True(Path.IsPathRooted(result.FilePath));
    }
}
