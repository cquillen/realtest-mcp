using HtmlAgilityPack;
using RealTestMcp.Core;
using RealTestMcp.Core.Models;
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Ingestion.Chunkers;

public static class DocChunker
{
    public static List<Chunk> Chunk(HtmlPage page)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(page.RawHtml);

        // Detect multi-function page: has multiple <h2> elements + multiple Syntax <h3> sections
        var h2Nodes = doc.DocumentNode.SelectNodes("//body//h2");
        if (h2Nodes is { Count: >= 2 } && HasMultipleSyntaxSections(doc))
            return ChunkByFunctionEntry(page, doc, h2Nodes);

        return [PageChunk(page)];
    }

    private static bool HasMultipleSyntaxSections(HtmlDocument doc)
    {
        var h3Nodes = doc.DocumentNode.SelectNodes("//body//h3");
        if (h3Nodes is null) return false;
        var syntaxCount = h3Nodes.Count(n =>
            n.InnerText.Contains("Syntax", StringComparison.OrdinalIgnoreCase));
        return syntaxCount >= 2;
    }

    private static List<Chunk> ChunkByFunctionEntry(HtmlPage page, HtmlDocument doc, HtmlNodeCollection h2Nodes)
    {
        var chunks = new List<Chunk>();
        for (int i = 0; i < h2Nodes.Count; i++)
        {
            var h2 = h2Nodes[i];
            var functionName = HtmlEntity.DeEntitize(h2.InnerText.Trim());

            // Collect all sibling nodes until the next h2
            var contentNodes = new List<HtmlNode>();
            var current = h2.NextSibling;
            while (current != null && current.Name.ToLower() != "h2")
            {
                contentNodes.Add(current);
                current = current.NextSibling;
            }

            var content = $"{functionName}\n" +
                NormalizeWhitespace(
                    HtmlEntity.DeEntitize(
                        string.Concat(contentNodes.Select(n => n.InnerText))));

            if (string.IsNullOrWhiteSpace(content)) continue;

            var id = VectorStoreService.ComputeChunkId(page.FilePath, i);
            chunks.Add(new Chunk(
                Id: id,
                SourceType: "docs",
                SourcePath: page.FilePath,
                ChunkType: "function_entry",
                Section: InferSection(page.FilePath),
                Category: null,
                Description: null,
                Content: content,
                ChunkIndex: i,
                CreatedAt: DateTime.UtcNow));
        }
        return chunks;
    }

    private static Chunk PageChunk(HtmlPage page)
    {
        var content = $"{page.Title}\n{page.BodyText}";
        var id = VectorStoreService.ComputeChunkId(page.FilePath, 0);
        return new Chunk(
            Id: id,
            SourceType: "docs",
            SourcePath: page.FilePath,
            ChunkType: "page",
            Section: InferSection(page.FilePath),
            Category: null,
            Description: null,
            Content: content,
            ChunkIndex: 0,
            CreatedAt: DateTime.UtcNow);
    }

    private static string? InferSection(string filePath)
    {
        var dir = Path.GetFileName(Path.GetDirectoryName(filePath));
        return string.IsNullOrWhiteSpace(dir) ? null : dir;
    }

    private static string NormalizeWhitespace(string text)
        => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
}
