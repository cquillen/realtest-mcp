using RealTestMcp.Ingestion.Chunkers;
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Tests.Chunkers;

public class DocChunkerTests
{
    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "data", "docs");

    private static string ReferencePage => Path.Combine(DataDir, "single-function.html");
    private static string ProsePage     => Path.Combine(DataDir, "multi-function.html");
    private static string NavIndexPage  => Path.Combine(DataDir, "navindex-page.html");

    // ── reference page ──────────────────────────────────────────────

    [Fact]
    public void ReferencePage_ProducesOneReferenceChunk()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        var chunks = DocChunker.Chunk(page);

        Assert.Single(chunks);
        Assert.Equal("reference", chunks[0].ChunkType);
    }

    [Fact]
    public void ReferencePage_ChunkHasDescriptionEqualToTitle()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        var chunks = DocChunker.Chunk(page);

        Assert.Equal("ATR", chunks[0].Description);
    }

    [Fact]
    public void ReferencePage_ChunkHasCategoryFromLabel()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        var chunks = DocChunker.Chunk(page);

        Assert.Equal("Indicator Functions", chunks[0].Category);
    }

    [Fact]
    public void ReferencePage_ChunkHasSectionFromBreadcrumb()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        var chunks = DocChunker.Chunk(page);

        Assert.Equal("Realtest Script Language > Syntax Element Details", chunks[0].Section);
    }

    [Fact]
    public void ReferencePage_ChunkContentContainsLabeledText()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        var chunks = DocChunker.Chunk(page);

        Assert.Contains("ATR", chunks[0].Content);
        Assert.Contains("Indicator Functions", chunks[0].Content);
        Assert.Contains("ATR(len)", chunks[0].Content);
    }

    [Fact]
    public void ReferencePage_ChunkSourceTypeIsDocs()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        var chunks = DocChunker.Chunk(page);

        Assert.Equal("docs", chunks[0].SourceType);
    }

    // ── prose page ───────────────────────────────────────────────────

    [Fact]
    public void ProsePage_ProducesOnePageChunk()
    {
        var page = ChmParser.ParseFile(ProsePage);
        var chunks = DocChunker.Chunk(page);

        Assert.Single(chunks);
        Assert.Equal("page", chunks[0].ChunkType);
    }

    [Fact]
    public void ProsePage_ChunkHasDescriptionEqualToTitle()
    {
        var page = ChmParser.ParseFile(ProsePage);
        var chunks = DocChunker.Chunk(page);

        Assert.Equal("Script Sections", chunks[0].Description);
    }

    [Fact]
    public void ProsePage_ChunkSectionFromBreadcrumb()
    {
        var page = ChmParser.ParseFile(ProsePage);
        var chunks = DocChunker.Chunk(page);

        Assert.Equal("Realtest Script Language", chunks[0].Section);
    }

    // ── navindex page ─────────────────────────────────────────────────

    [Fact]
    public void NavIndexPage_WithLinks_ProducesOneIndexChunk()
    {
        var page = ChmParser.ParseFile(NavIndexPage);
        var chunks = DocChunker.Chunk(page);

        Assert.Single(chunks);
        Assert.Equal("index", chunks[0].ChunkType);
    }

    [Fact]
    public void NavIndexPage_WithLinks_ContentContainsLinkedNames()
    {
        var page = ChmParser.ParseFile(NavIndexPage);
        var chunks = DocChunker.Chunk(page);

        Assert.Contains("ATR", chunks[0].Content);
        Assert.Contains("RSI", chunks[0].Content);
        Assert.Contains("Highest", chunks[0].Content);
    }

    [Fact]
    public void NavIndexPage_WithNoLinks_ProducesNoChunks()
    {
        var page = new HtmlPage("/fake/empty-nav.html", "Empty Nav", "",
            PageType.NavIndex, new Dictionary<string, string>(), "", "");
        var chunks = DocChunker.Chunk(page);

        Assert.Empty(chunks);
    }

    // ── determinism ───────────────────────────────────────────────────

    [Fact]
    public void ChunkIds_AreDeterministic()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        var chunks1 = DocChunker.Chunk(page);
        var chunks2 = DocChunker.Chunk(page);

        Assert.Equal(chunks1[0].Id, chunks2[0].Id);
    }

    // ── "X or Y" alias splitting ──────────────────────────────────────

    [Fact]
    public void ReferencePage_WithOrAlias_ProducesOneChunkPerAlias()
    {
        var labels = new Dictionary<string, string> { ["Category"] = "Multi-Bar Functions" };
        var bodyText = "EMA or XAvg\nCategory: Multi-Bar Functions\nSyntax: EMA(expr, count)";
        var page = new HtmlPage("/fake/ema.html", "EMA or XAvg",
            "Realtest Script Language > Syntax Element Details",
            PageType.Reference, labels, bodyText, "");

        var chunks = DocChunker.Chunk(page);

        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, c => Assert.Equal("reference", c.ChunkType));
        Assert.Contains(chunks, c => c.Description == "EMA");
        Assert.Contains(chunks, c => c.Description == "XAvg");
    }

    [Fact]
    public void ReferencePage_WithOrAlias_AllChunksHaveSameContent()
    {
        var labels = new Dictionary<string, string>();
        var bodyText = "EMA or XAvg\nSyntax: EMA(expr, count)";
        var page = new HtmlPage("/fake/ema.html", "EMA or XAvg", "",
            PageType.Reference, labels, bodyText, "");

        var chunks = DocChunker.Chunk(page);

        Assert.Equal(chunks[0].Content, chunks[1].Content);
    }

    [Fact]
    public void ReferencePage_WithOrAlias_ChunkIdsAreDeterministicAndDistinct()
    {
        var labels = new Dictionary<string, string>();
        var page = new HtmlPage("/fake/ema.html", "EMA or XAvg", "",
            PageType.Reference, labels, "EMA or XAvg", "");

        var chunks1 = DocChunker.Chunk(page);
        var chunks2 = DocChunker.Chunk(page);

        Assert.Equal(chunks1[0].Id, chunks2[0].Id);    // deterministic
        Assert.Equal(chunks1[1].Id, chunks2[1].Id);    // deterministic
        Assert.NotEqual(chunks1[0].Id, chunks1[1].Id); // distinct per alias
    }
}
