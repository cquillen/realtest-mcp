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
            PageType.Reference => [ReferenceChunk(page)],
            PageType.Prose     => [ProseChunk(page)],
            _                  => []   // NavIndex: skip
        };
    }

    private static Chunk ReferenceChunk(HtmlPage page)
    {
        page.Labels.TryGetValue("Category", out var category);
        var id = VectorStoreService.ComputeChunkId(page.FilePath, 0);
        return new Chunk(
            Id: id,
            SourceType: "docs",
            SourcePath: page.FilePath,
            ChunkType: "reference",
            Section: NullIfEmpty(page.Section),
            Category: NullIfEmpty(category),
            Description: page.Title,
            Content: page.BodyText,
            ChunkIndex: 0,
            CreatedAt: DateTime.UtcNow);
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

    private static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s;
}
