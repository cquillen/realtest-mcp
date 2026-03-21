using HtmlAgilityPack;

namespace RealTestMcp.Ingestion.Parsers;

public record HtmlPage(string FilePath, string Title, string BodyText, string RawHtml);

public static class ChmParser
{
    public static List<HtmlPage> ParseDirectory(string docsPath)
    {
        if (!Directory.Exists(docsPath))
            throw new DirectoryNotFoundException($"Docs path not found: {docsPath}");

        var pages = new List<HtmlPage>();
        foreach (var file in Directory.EnumerateFiles(docsPath, "*.htm", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(docsPath, "*.html", SearchOption.AllDirectories)))
        {
            try
            {
                var page = ParseFile(file);
                if (!string.IsNullOrWhiteSpace(page.BodyText))
                    pages.Add(page);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ChmParser] Skipping {file}: {ex.Message}");
            }
        }
        return pages;
    }

    public static HtmlPage ParseFile(string filePath)
    {
        var rawHtml = File.ReadAllText(filePath);
        var doc = new HtmlDocument();
        doc.LoadHtml(rawHtml);

        var title = doc.DocumentNode
            .SelectSingleNode("//title")?.InnerText.Trim()
            ?? Path.GetFileNameWithoutExtension(filePath);

        var body = doc.DocumentNode.SelectSingleNode("//body");
        var bodyText = body is null
            ? string.Empty
            : NormalizeWhitespace(HtmlEntity.DeEntitize(body.InnerText));

        return new HtmlPage(filePath, HtmlEntity.DeEntitize(title), bodyText, rawHtml);
    }

    private static string NormalizeWhitespace(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
    }
}
