using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RealTestMcp.Tools;

[McpServerToolType]
public static class SearchExamplesTool
{
    [McpServerTool, Description("Find example RealScript files demonstrating a concept or technique")]
    public static string SearchExamples(
        [Description("What to search for")] string query,
        [Description("Optional: filter by category (e.g. 'Mean Reversion', 'Futures')")] string? categoryFilter = null,
        [Description("Number of results to return (default: 3)")] int topK = 3)
    {
        return "DB not initialized — run: realtest-mcp ingest scripts";
    }
}
