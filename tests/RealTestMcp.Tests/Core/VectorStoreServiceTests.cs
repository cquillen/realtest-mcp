using RealTestMcp.Core;
using RealTestMcp.Core.Models;

namespace RealTestMcp.Tests.Core;

public class VectorStoreServiceTests : IAsyncLifetime
{
    private VectorStoreService _store = null!;
    private string _dbPath = null!;

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");
        _store = new VectorStoreService(_dbPath);
        await _store.EnsureSchemaAsync();
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public async Task EnsureSchemaAsync_CreatesChunksTable()
    {
        var counts = await _store.GetChunkCountsAsync();
        Assert.NotNull(counts);
    }

    [Fact]
    public async Task UpsertAndSearch_ReturnsMatchingChunk()
    {
        var chunk = MakeChunk("chunk-1", "docs", "page");
        var embedding = MakeEmbedding(1.0f);

        await _store.UpsertChunkAsync(chunk, embedding);

        var results = await _store.VectorSearchAsync(embedding, sourceType: "docs", topK: 5);
        Assert.Single(results);
        Assert.Equal("chunk-1", results[0].Id);
    }

    [Fact]
    public async Task DeleteBySourceType_RemovesOnlyMatchingChunks()
    {
        await _store.UpsertChunkAsync(MakeChunk("a", "docs", "page"), MakeEmbedding(1.0f));
        await _store.UpsertChunkAsync(MakeChunk("b", "example", "script"), MakeEmbedding(2.0f));

        await _store.DeleteBySourceTypeAsync("docs");

        var remaining = await _store.VectorSearchAsync(MakeEmbedding(1.0f), sourceType: null, topK: 10);
        Assert.Single(remaining);
        Assert.Equal("b", remaining[0].Id);
    }

    [Fact]
    public async Task KeywordSearch_FindsChunkByContent()
    {
        var chunk = MakeChunk("fn-1", "docs", "function_entry", content: "ATR(periods) Average True Range");
        await _store.UpsertChunkAsync(chunk, MakeEmbedding(1.0f));

        var results = await _store.KeywordSearchAsync("ATR", chunkType: "function_entry", topK: 3);
        Assert.Single(results);
        Assert.Equal("fn-1", results[0].Id);
    }

    [Fact]
    public async Task SearchByDescription_ExactMatchCaseInsensitive()
    {
        var chunk = MakeChunk("ref-1", "docs", "reference", content: "ATR\nCategory: Indicator Functions\nSyntax: ATR(len)") with { Description = "ATR" };
        await _store.UpsertChunkAsync(chunk, MakeEmbedding(1.0f));

        // Exact match, case-insensitive
        var results = await _store.SearchByDescriptionAsync("atr");

        Assert.Single(results);
        Assert.Equal("ref-1", results[0].Id);
    }

    [Fact]
    public async Task SearchByDescription_NoMatchForPartialName()
    {
        var chunk = MakeChunk("ref-2", "docs", "reference") with { Description = "ATR" };
        await _store.UpsertChunkAsync(chunk, MakeEmbedding(1.0f));

        // "AT" is not an exact match for "ATR"
        var results = await _store.SearchByDescriptionAsync("AT");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchByDescription_DefaultsToReferenceChunkType()
    {
        var refChunk  = MakeChunk("ref-3", "docs", "reference") with { Description = "RSI" };
        var pageChunk = MakeChunk("pag-3", "docs", "page")      with { Description = "RSI" };
        await _store.UpsertChunkAsync(refChunk,  MakeEmbedding(1.0f));
        await _store.UpsertChunkAsync(pageChunk, MakeEmbedding(2.0f));

        var results = await _store.SearchByDescriptionAsync("RSI");

        Assert.Single(results);
        Assert.Equal("ref-3", results[0].Id);
    }

    // ── helpers ────────────────────────────────────────────────────

    private static Chunk MakeChunk(string id, string sourceType, string chunkType, string content = "test content")
        => new(id, sourceType, "/path/file", chunkType, null, null, null, content, 0, DateTime.UtcNow);

    private static float[] MakeEmbedding(float value)
        => Enumerable.Repeat(value, 384).ToArray();
}
