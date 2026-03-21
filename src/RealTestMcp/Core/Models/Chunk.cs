namespace RealTestMcp.Core.Models;

/// <summary>
/// A single indexed unit of text content with metadata.
/// source_type: 'docs' | 'example' | 'user_script'
/// chunk_type:  'page' | 'function_entry' | 'script'
/// </summary>
public record Chunk(
    string Id,           // SHA256(source_path + ':' + chunk_index)
    string SourceType,
    string SourcePath,
    string ChunkType,
    string? Section,
    string? Category,
    string? Description,
    string Content,
    int ChunkIndex,
    DateTime CreatedAt
);
