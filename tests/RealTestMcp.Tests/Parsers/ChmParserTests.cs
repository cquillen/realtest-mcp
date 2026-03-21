using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Tests.Parsers;

public class ChmParserTests
{
    private static string DataDir => Path.Combine(
        AppContext.BaseDirectory, "data", "docs");

    [Fact]
    public void ParseDirectory_FindsHtmlFiles()
    {
        var pages = ChmParser.ParseDirectory(DataDir);
        Assert.True(pages.Count >= 2);
    }

    [Fact]
    public void ParseDirectory_ExtractsTitle()
    {
        var pages = ChmParser.ParseDirectory(DataDir);
        var atr = pages.FirstOrDefault(p => p.Title.Contains("ATR", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(atr);
    }

    [Fact]
    public void ParseDirectory_ExtractsBodyText()
    {
        var pages = ChmParser.ParseDirectory(DataDir);
        var atr = pages.First(p => p.Title.Contains("ATR", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Average True Range", atr.BodyText);
        Assert.Contains("ATR(periods)", atr.BodyText);
    }

    [Fact]
    public void ParseDirectory_StripsTags()
    {
        var pages = ChmParser.ParseDirectory(DataDir);
        var atr = pages.First(p => p.Title.Contains("ATR", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("<", atr.BodyText);
    }
}
