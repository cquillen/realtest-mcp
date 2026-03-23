using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Tests.Parsers;

public class ChmParserTests
{
    private static string DataDir => Path.Combine(
        AppContext.BaseDirectory, "data", "docs");

    private static string ReferencePage => Path.Combine(DataDir, "single-function.html");
    private static string ProsePage     => Path.Combine(DataDir, "multi-function.html");
    private static string NavIndexPage  => Path.Combine(DataDir, "navindex-page.html");

    // ── ParseFile: reference page ───────────────────────────────────

    [Fact]
    public void ParseFile_ReferencePage_HasReferencePageType()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        Assert.Equal(PageType.Reference, page.PageType);
    }

    [Fact]
    public void ParseFile_ReferencePage_ExtractsTitleFromPs2()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        Assert.Equal("ATR", page.Title);
    }

    [Fact]
    public void ParseFile_ReferencePage_ExtractsBreadcrumbSection()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        Assert.Equal("Realtest Script Language > Syntax Element Details", page.Section);
    }

    [Fact]
    public void ParseFile_ReferencePage_ExtractsCategoryLabel()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        Assert.True(page.Labels.TryGetValue("Category", out var cat));
        Assert.Equal("Indicator Functions", cat);
    }

    [Fact]
    public void ParseFile_ReferencePage_ExtractsSyntaxLabel()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        Assert.True(page.Labels.TryGetValue("Syntax", out var syn));
        Assert.Equal("ATR(len)", syn);
    }

    [Fact]
    public void ParseFile_ReferencePage_BodyTextContainsTitle()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        Assert.StartsWith("ATR", page.BodyText);
    }

    [Fact]
    public void ParseFile_ReferencePage_BodyTextContainsLabeledContent()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        Assert.Contains("Category: Indicator Functions", page.BodyText);
        Assert.Contains("Syntax: ATR(len)", page.BodyText);
        Assert.Contains("Wilder's Average True Range", page.BodyText);
    }

    [Fact]
    public void ParseFile_ReferencePage_BodyTextHasNoHtmlTags()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        Assert.DoesNotContain("<", page.BodyText);
    }

    // ── ParseFile: prose page ───────────────────────────────────────

    [Fact]
    public void ParseFile_ProsePage_HasProsePageType()
    {
        var page = ChmParser.ParseFile(ProsePage);
        Assert.Equal(PageType.Prose, page.PageType);
    }

    [Fact]
    public void ParseFile_ProsePage_ExtractsTitleFromPs2()
    {
        var page = ChmParser.ParseFile(ProsePage);
        Assert.Equal("Script Sections", page.Title);
    }

    [Fact]
    public void ParseFile_ProsePage_ExtractsBreadcrumbSection()
    {
        var page = ChmParser.ParseFile(ProsePage);
        Assert.Equal("Realtest Script Language", page.Section);
    }

    [Fact]
    public void ParseFile_ProsePage_BodyTextContainsParagraphs()
    {
        var page = ChmParser.ParseFile(ProsePage);
        Assert.Contains("Script Sections", page.BodyText);
        Assert.Contains("named sections", page.BodyText);
        Assert.Contains("EntrySetup", page.BodyText);
    }

    // ── ParseFile: navindex page ────────────────────────────────────

    [Fact]
    public void ParseFile_NavIndexPage_HasNavIndexPageType()
    {
        var page = ChmParser.ParseFile(NavIndexPage);
        Assert.Equal(PageType.NavIndex, page.PageType);
    }

    [Fact]
    public void ParseFile_NavIndexPage_BodyTextContainsLinkNames()
    {
        var page = ChmParser.ParseFile(NavIndexPage);
        Assert.Contains("ATR", page.BodyText);
        Assert.Contains("RSI", page.BodyText);
        Assert.Contains("Highest", page.BodyText);
    }

    // ── ParseDirectory ──────────────────────────────────────────────

    [Fact]
    public void ParseDirectory_IncludesNavIndexPagesWithLinks()
    {
        var pages = ChmParser.ParseDirectory(DataDir);
        Assert.Contains(pages, p => p.PageType == PageType.NavIndex);
    }

    [Fact]
    public void ParseDirectory_IncludesReferenceAndProsePages()
    {
        var pages = ChmParser.ParseDirectory(DataDir);
        Assert.Contains(pages, p => p.PageType == PageType.Reference);
        Assert.Contains(pages, p => p.PageType == PageType.Prose);
    }
}
