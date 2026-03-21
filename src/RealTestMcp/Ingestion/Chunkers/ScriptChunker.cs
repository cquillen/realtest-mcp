using RealTestMcp.Core;
using RealTestMcp.Core.Models;
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Ingestion.Chunkers;

public static class ScriptChunker
{
    public static List<Chunk> Chunk(
        RtsFile script,
        string sourceType,
        Dictionary<string, (string Category, string Description)>? catalog = null)
    {
        var fileName = Path.GetFileNameWithoutExtension(script.FilePath);
        var id = VectorStoreService.ComputeChunkId(script.FilePath, 0);

        string? category = null;
        string? description = null;

        if (catalog is not null && catalog.TryGetValue(fileName, out var meta))
        {
            category = meta.Category;
            description = meta.Description;
        }

        return
        [
            new Chunk(
                Id: id,
                SourceType: sourceType,
                SourcePath: script.FilePath,
                ChunkType: "script",
                Section: null,
                Category: category,
                Description: description,
                Content: script.Content,
                ChunkIndex: 0,
                CreatedAt: DateTime.UtcNow)
        ];
    }
}
