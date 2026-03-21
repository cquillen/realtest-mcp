using ModelContextProtocol.Server;
using RealTestMcp.Core;
using System.ComponentModel;
using System.Text;

namespace RealTestMcp.Tools;

[McpServerToolType]
public class SearchDocsTool(VectorStoreService store, EmbeddingService embedder)
{
    [McpServerTool, Description("Search RealTest documentation by concept or topic")]
    public async Task<string> SearchDocs(
        [Description("What to search for")] string query,
        [Description("Optional: limit to a doc section (e.g. 'Strategy', 'Import')")] string? sectionFilter = null,
        [Description("Number of results to return (default: 5)")] int topK = 5)
    {
        var queryEmbedding = await embedder.EmbedAsync(query);
        var results = await store.VectorSearchAsync(queryEmbedding, sourceType: "docs",
            categoryFilter: null, sectionFilter: sectionFilter, topK: topK);

        if (results.Count == 0)
            return "No documentation found for that query. Try different search terms.";

        var sb = new StringBuilder();
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"## Result {i + 1}");
            if (r.Section is not null) sb.AppendLine($"Section: {r.Section}");
            sb.AppendLine();
            sb.AppendLine(r.Content);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
