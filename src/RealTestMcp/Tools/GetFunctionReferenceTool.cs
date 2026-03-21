using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RealTestMcp.Tools;

[McpServerToolType]
public static class GetFunctionReferenceTool
{
    [McpServerTool, Description("Get the exact function signature and description for a RealScript function")]
    public static string GetFunctionReference(
        [Description("Function name to look up (e.g. 'ATR', 'Lowest')")] string functionName)
    {
        return "DB not initialized — run: realtest-mcp ingest docs";
    }
}
