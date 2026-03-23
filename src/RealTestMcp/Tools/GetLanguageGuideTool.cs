using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RealTestMcp.Tools;

[McpServerToolType]
public class GetLanguageGuideTool
{
    private static readonly string GuidePath = Path.Combine(
        AppContext.BaseDirectory, "realscript-language-guide.md");

    [McpServerTool, Description("Get the complete RealScript language guide — call this once at the start of any scripting session to load the full language model: script structure, evaluation model, all section types, strategy elements, functions, and idiomatic patterns.")]
    public string GetLanguageGuide()
    {
        if (File.Exists(GuidePath))
            return File.ReadAllText(GuidePath);

        // Fallback: look next to the exe and up two directories (dev mode)
        var devPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "realscript-language-guide.md");
        var resolved = Path.GetFullPath(devPath);
        if (File.Exists(resolved))
            return File.ReadAllText(resolved);

        return "Language guide not found. Expected at: " + GuidePath;
    }
}
