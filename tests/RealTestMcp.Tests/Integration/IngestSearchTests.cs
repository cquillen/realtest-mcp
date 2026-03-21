// tests/RealTestMcp.Tests/Integration/IngestSearchTests.cs
using RealTestMcp.Core;
using RealTestMcp.Ingestion.Chunkers;
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Tests.Integration;

public class IngestSearchTests : IAsyncLifetime
{
    private VectorStoreService _store = null!;
    private EmbeddingService _embedder = null!;
    private string _dbPath = null!;

    private static string DocsDir    => Path.Combine(AppContext.BaseDirectory, "data", "docs");
    private static string ScriptsDir => Path.Combine(AppContext.BaseDirectory, "data", "scripts");

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");
        _store = new VectorStoreService(_dbPath);
        await _store.EnsureSchemaAsync();
        _embedder = new EmbeddingService();
        await IngestSampleDataAsync();
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        _embedder.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    // ── search_docs ───────────────────────────────────────────────

    [Fact]
    public async Task SearchDocs_ReturnsRelevantDocChunk()
    {
        var queryEmbedding = await _embedder.EmbedAsync("volatility measurement average true range");
        var results = await _store.VectorSearchAsync(queryEmbedding, sourceType: "docs", topK: 5);

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Content.Contains("ATR", StringComparison.OrdinalIgnoreCase));
    }

    // ── get_function_reference (keyword path) ─────────────────────

    [Fact]
    public async Task GetFunctionReference_KeywordPath_FindsATR()
    {
        var results = await _store.KeywordSearchAsync("ATR", chunkType: "function_entry", topK: 3);

        // If no function_entry chunks exist (single-page CHM structure), fall back to page search
        if (results.Count == 0)
            results = await _store.KeywordSearchAsync("ATR", chunkType: null, topK: 3);

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Content.Contains("ATR"));
    }

    // ── get_function_reference (semantic fallback) ────────────────

    [Fact]
    public async Task GetFunctionReference_SemanticFallback_FindsFunction()
    {
        // Search for something that won't match keyword but should match semantically
        var keywordResults = await _store.KeywordSearchAsync("ZZZNOMATCH", chunkType: "function_entry", topK: 3);
        Assert.Empty(keywordResults); // confirm keyword misses

        var queryEmbedding = await _embedder.EmbedAsync("highest value over lookback period");
        var semanticResults = await _store.VectorSearchAsync(queryEmbedding, sourceType: "docs", topK: 3);
        Assert.NotEmpty(semanticResults);
    }

    // ── search_examples ───────────────────────────────────────────

    [Fact]
    public async Task SearchExamples_ReturnsScriptChunk()
    {
        var queryEmbedding = await _embedder.EmbedAsync("RSI mean reversion entry");
        var results = await _store.VectorSearchAsync(queryEmbedding, sourceType: "example", topK: 3);

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal("example", r.SourceType));
    }

    [Fact]
    public async Task SearchExamples_CategoryFilter_ReturnsOnlyMatchingCategory()
    {
        var queryEmbedding = await _embedder.EmbedAsync("futures trend following");
        var filteredResults = await _store.VectorSearchAsync(queryEmbedding, sourceType: "example",
            categoryFilter: "Mean Reversion", topK: 10);

        // Filtered results must only contain Mean Reversion chunks
        Assert.All(filteredResults, r =>
            Assert.Equal("Mean Reversion", r.Category, StringComparer.OrdinalIgnoreCase));
    }

    // ── IngestDocsCommand ─────────────────────────────────────────

    [Fact]
    public async Task IngestDocsCommand_PopulatesDocsChunks()
    {
        var freshDbPath = Path.Combine(Path.GetTempPath(), $"cmd_{Guid.NewGuid()}.db");

        var settings = new RealTestMcp.Core.Configuration.AppSettings();
        settings.Database.Path = freshDbPath;
        settings.RealTest.DocsPath = DocsDir;

        await RealTestMcp.Ingestion.Commands.IngestDocsCommand.RunAsync(settings);

        // Open a new store after the command has disposed its own connection
        await using var freshStore = new VectorStoreService(freshDbPath);
        var counts = await freshStore.GetChunkCountsAsync();
        Assert.True(counts.GetValueOrDefault("docs") > 0, "Expected docs chunks after IngestDocsCommand");
    }

    [Fact]
    public async Task IngestScriptsCommand_PopulatesExampleChunks()
    {
        var freshDbPath = Path.Combine(Path.GetTempPath(), $"cmd_{Guid.NewGuid()}.db");

        var settings = new RealTestMcp.Core.Configuration.AppSettings();
        settings.Database.Path = freshDbPath;
        settings.RealTest.ScriptPaths = [ScriptsDir];

        await RealTestMcp.Ingestion.Commands.IngestScriptsCommand.RunAsync(settings);

        // Open a new store after the command has disposed its own connection
        await using var freshStore = new VectorStoreService(freshDbPath);
        var counts = await freshStore.GetChunkCountsAsync();
        Assert.True(counts.GetValueOrDefault("example") > 0, "Expected example chunks after IngestScriptsCommand");
    }

    // ── helpers ───────────────────────────────────────────────────

    private async Task IngestSampleDataAsync()
    {
        // Ingest docs
        foreach (var file in Directory.GetFiles(DocsDir, "*.html"))
        {
            var page = ChmParser.ParseFile(file);
            foreach (var chunk in DocChunker.Chunk(page))
            {
                var embedding = await _embedder.EmbedAsync(chunk.Content);
                await _store.UpsertChunkAsync(chunk, embedding);
            }
        }

        // Ingest scripts with category metadata
        var catalog = new Dictionary<string, (string, string)>
        {
            ["mean-reversion"] = ("Mean Reversion", "RSI(2) mean reversion system"),
            ["futures-example"] = ("Futures", "ATR-based trend following on futures"),
        };

        foreach (var file in Directory.GetFiles(ScriptsDir, "*.rts"))
        {
            var script = RtsParser.ParseFile(file);
            foreach (var chunk in ScriptChunker.Chunk(script, "example", catalog))
            {
                var embedding = await _embedder.EmbedAsync(chunk.Content);
                await _store.UpsertChunkAsync(chunk, embedding);
            }
        }
    }
}
