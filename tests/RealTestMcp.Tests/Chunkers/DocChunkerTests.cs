using RealTestMcp.Ingestion.Chunkers;
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Tests.Chunkers;

public class DocChunkerTests
{
    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "data", "docs");

    [Fact]
    public void SingleFunctionPage_ProducesOnePageChunk()
    {
        var page = ChmParser.ParseFile(Path.Combine(DataDir, "single-function.html"));
        var chunks = DocChunker.Chunk(page);

        Assert.Single(chunks);
        Assert.Equal("page", chunks[0].ChunkType);
        Assert.Equal("docs", chunks[0].SourceType);
    }

    [Fact]
    public void SingleFunctionPage_ChunkContainsTitle()
    {
        var page = ChmParser.ParseFile(Path.Combine(DataDir, "single-function.html"));
        var chunks = DocChunker.Chunk(page);

        Assert.Contains("ATR", chunks[0].Content);
    }

    [Fact]
    public void MultiFunctionPage_ProducesFunctionEntryChunks()
    {
        var page = ChmParser.ParseFile(Path.Combine(DataDir, "multi-function.html"));
        var chunks = DocChunker.Chunk(page);

        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, c => Assert.Equal("function_entry", c.ChunkType));
    }

    [Fact]
    public void MultiFunctionPage_EachChunkContainsFunctionName()
    {
        var page = ChmParser.ParseFile(Path.Combine(DataDir, "multi-function.html"));
        var chunks = DocChunker.Chunk(page);

        Assert.Contains(chunks, c => c.Content.Contains("Highest"));
        Assert.Contains(chunks, c => c.Content.Contains("Lowest"));
    }

    [Fact]
    public void ChunkIds_AreDeterministic()
    {
        var page = ChmParser.ParseFile(Path.Combine(DataDir, "single-function.html"));
        var chunks1 = DocChunker.Chunk(page);
        var chunks2 = DocChunker.Chunk(page);

        Assert.Equal(chunks1[0].Id, chunks2[0].Id);
    }
}
