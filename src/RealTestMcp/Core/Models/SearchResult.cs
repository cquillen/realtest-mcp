namespace RealTestMcp.Core.Models;

public record SearchResult(
    string Id,
    string SourceType,
    string SourcePath,   // Absolute path to the source file
    string ChunkType,
    string? Section,
    string? Category,
    string? Description,
    string Content,   // Truncated to 1500 chars if over limit
    double Score
);
