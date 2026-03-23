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
    public void NavIndexPage_ProducesNoChunks()
    {
        var page = ChmParser.ParseFile(NavIndexPage);
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
}
