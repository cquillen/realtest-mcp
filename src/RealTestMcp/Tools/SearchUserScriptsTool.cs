using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RealTestMcp.Tools;

[McpServerToolType]
public static class SearchUserScriptsTool
{
    [McpServerTool, Description("Search your own RealScript files for patterns or techniques")]
    public static string SearchUserScripts(
        [Description("What to search for")] string query,
        [Description("Number of results to return (default: 3)")] int topK = 3)
    {
        return "DB not initialized — run: realtest-mcp ingest scripts";
    }
}
