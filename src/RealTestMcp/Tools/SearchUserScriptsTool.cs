using ModelContextProtocol.Server;
using RealTestMcp.Core;
using System.ComponentModel;
using System.Text;

namespace RealTestMcp.Tools;

[McpServerToolType]
public class SearchUserScriptsTool(VectorStoreService store, EmbeddingService embedder)
{
    [McpServerTool, Description("Search your own RealScript files for patterns or techniques")]
    public async Task<string> SearchUserScripts(
        [Description("What to search for")] string query,
        [Description("Number of results to return (default: 3)")] int topK = 3)
    {
        var queryEmbedding = await embedder.EmbedAsync(query);
        var results = await store.VectorSearchAsync(queryEmbedding, sourceType: "user_script", topK: topK);

        if (results.Count == 0)
            return "No user scripts found. Add script paths to appsettings.json and run 'realtest-mcp ingest scripts'.";

        var sb = new StringBuilder();
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"## User Script {i + 1}: {Path.GetFileName(r.SourcePath)}");
            sb.AppendLine();
            sb.AppendLine(r.Content);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
