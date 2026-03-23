using RealTestMcp.Core;
using RealTestMcp.Core.Models;
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Ingestion.Chunkers;

public static class DocChunker
{
    public static List<Chunk> Chunk(HtmlPage page)
    {
        return page.PageType switch
        {
            PageType.Reference => ReferenceChunks(page),
            PageType.Prose     => [ProseChunk(page)],
            PageType.NavIndex  => string.IsNullOrWhiteSpace(page.BodyText) ? [] : [NavIndexChunk(page)],
            _                  => []
        };
    }

    private static List<Chunk> ReferenceChunks(HtmlPage page)
    {
        page.Labels.TryGetValue("Category", out var category);

        // "EMA or XAvg" → ["EMA", "XAvg"]; "ATR" → ["ATR"]
        var aliases = page.Title.Split(" or ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        return aliases.Select((alias, i) => new Chunk(
            Id: VectorStoreService.ComputeChunkId(page.FilePath, i),
            SourceType: "docs",
            SourcePath: page.FilePath,
            ChunkType: "reference",
            Section: NullIfEmpty(page.Section),
            Category: NullIfEmpty(category),
            Description: alias,
            Content: page.BodyText,
            ChunkIndex: i,
            CreatedAt: DateTime.UtcNow)).ToList();
    }

    private static Chunk ProseChunk(HtmlPage page)
    {
        var id = VectorStoreService.ComputeChunkId(page.FilePath, 0);
        return new Chunk(
            Id: id,
            SourceType: "docs",
            SourcePath: page.FilePath,
            ChunkType: "page",
            Section: NullIfEmpty(page.Section),
            Category: null,
            Description: page.Title,
            Content: page.BodyText,
            ChunkIndex: 0,
            CreatedAt: DateTime.UtcNow);
    }

    private static Chunk NavIndexChunk(HtmlPage page)
    {
        var id = VectorStoreService.ComputeChunkId(page.FilePath, 0);
        return new Chunk(
            Id: id,
            SourceType: "docs",
            SourcePath: page.FilePath,
            ChunkType: "index",
            Section: NullIfEmpty(page.Section),
            Category: null,
            Description: page.Title,
            Content: page.Title + "\n\n" + page.BodyText,
            ChunkIndex: 0,
            CreatedAt: DateTime.UtcNow);
    }

    private static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s;
}
