using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RealTestMcp.Tools;

[McpServerToolType]
public static class SearchDocsTool
{
    [McpServerTool, Description("Search RealTest documentation by concept or topic")]
    public static string SearchDocs(
        [Description("What to search for")] string query,
        [Description("Optional: limit to a doc section (e.g. 'Strategy', 'Import')")] string? sectionFilter = null,
        [Description("Number of results to return (default: 5)")] int topK = 5)
    {
        return "DB not initialized — run: realtest-mcp ingest docs";
    }
}
