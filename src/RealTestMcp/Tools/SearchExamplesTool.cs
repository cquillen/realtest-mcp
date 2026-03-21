using ModelContextProtocol.Server;
using RealTestMcp.Core;
using System.ComponentModel;
using System.Text;

namespace RealTestMcp.Tools;

[McpServerToolType]
public class SearchExamplesTool(VectorStoreService store, EmbeddingService embedder)
{
    [McpServerTool, Description("Find example RealScript files demonstrating a concept or technique")]
    public async Task<string> SearchExamples(
        [Description("What to search for")] string query,
        [Description("Optional: filter by category (e.g. 'Mean Reversion', 'Futures', 'Tutorial Scripts')")] string? categoryFilter = null,
        [Description("Number of results to return (default: 3)")] int topK = 3)
    {
        var queryEmbedding = await embedder.EmbedAsync(query);
        var results = await store.VectorSearchAsync(queryEmbedding, sourceType: "example",
            categoryFilter: categoryFilter, topK: topK);

        if (results.Count == 0)
            return "No example scripts found. Run 'realtest-mcp ingest scripts' if the database is empty.";

        var sb = new StringBuilder();
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"## Example {i + 1}: {Path.GetFileName(r.SourcePath)}");
            if (r.Category is not null) sb.AppendLine($"Category: {r.Category}");
            if (r.Description is not null) sb.AppendLine($"Description: {r.Description}");
            sb.AppendLine();
            sb.AppendLine(r.Content);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
