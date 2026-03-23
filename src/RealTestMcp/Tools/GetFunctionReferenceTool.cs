using ModelContextProtocol.Server;
using RealTestMcp.Core;
using System.ComponentModel;
using System.Text;

namespace RealTestMcp.Tools;

[McpServerToolType]
public class GetFunctionReferenceTool(VectorStoreService store, EmbeddingService embedder)
{
    [McpServerTool, Description("Get the exact function signature and description for a RealScript function. Call this before using any function in generated code.")]
    public async Task<string> GetFunctionReference(
        [Description("Function name to look up (e.g. 'ATR', 'Lowest', 'RSI')")] string functionName)
    {
        // Step 1: exact description match on reference chunks
        var results = await store.SearchByDescriptionAsync(functionName);

        // Step 2: keyword search within reference chunks
        if (results.Count == 0)
            results = await store.KeywordSearchAsync(functionName, chunkType: "reference", topK: 3);

        // Step 3: keyword search across all chunk types
        if (results.Count == 0)
            results = await store.KeywordSearchAsync(functionName, chunkType: null, topK: 3);

        // Step 4: semantic embedding fallback across all docs
        if (results.Count == 0)
        {
            var queryEmbedding = await embedder.EmbedAsync(functionName);
            results = await store.VectorSearchAsync(queryEmbedding, sourceType: "docs", topK: 3);
        }

        if (results.Count == 0)
            return $"No reference found for '{functionName}'. Run 'realtest-mcp ingest docs' if the database is empty.";

        var sb = new StringBuilder();
        sb.AppendLine($"# Function Reference: {functionName}");
        sb.AppendLine();
        foreach (var r in results)
        {
            sb.AppendLine(r.Content);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
