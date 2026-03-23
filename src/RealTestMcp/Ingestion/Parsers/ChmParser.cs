using HtmlAgilityPack;

namespace RealTestMcp.Ingestion.Parsers;

public enum PageType { Reference, Prose, NavIndex }

public record HtmlPage(
    string FilePath,
    string Title,
    string Section,
    PageType PageType,
    Dictionary<string, string> Labels,
    string BodyText,
    string RawHtml);

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

        // Title: prefer ps2 element, fall back to <title> tag, then filename
        var ps2 = doc.DocumentNode.SelectSingleNode("//*[@class='ps2']");
        var titleTag = doc.DocumentNode.SelectSingleNode("//title");
        var title = ps2 is not null
            ? HtmlEntity.DeEntitize(ps2.InnerText).Trim()
            : titleTag is not null
                ? HtmlEntity.DeEntitize(titleTag.InnerText).Trim()
                : Path.GetFileNameWithoutExtension(filePath);

        // Section: join all hs1 breadcrumb anchor texts with " > "
        var hs1Nodes = doc.DocumentNode.SelectNodes("//a[@class='hs1']");
        var section = hs1Nodes is not null
            ? string.Join(" > ", hs1Nodes.Select(n => HtmlEntity.DeEntitize(n.InnerText).Trim()))
            : string.Empty;

        var ps4Nodes = doc.DocumentNode.SelectNodes("//*[@class='ps4']");
        var ps6Nodes = doc.DocumentNode.SelectNodes("//*[@class='ps6']");

        // Malformed: no ps2 AND no ps4 → skip (empty BodyText)
        if (ps2 is null && ps4Nodes is null)
            return new HtmlPage(filePath, title, section, PageType.NavIndex,
                new Dictionary<string, string>(), string.Empty, rawHtml);

        // Reference: at least one ps6 present (checked before navindex to take priority)
        if (ps6Nodes is { Count: > 0 })
        {
            var labels = ExtractLabels(doc);
            var bodyText = BuildReferenceBodyText(title, labels);
            return new HtmlPage(filePath, title, section, PageType.Reference, labels, bodyText, rawHtml);
        }

        // NavIndex: no ps6 AND ps4 text (after stripping links) < 20 chars
        // Still extract link texts as BodyText so the page can be indexed as a category listing.
        if (IsNavIndex(ps4Nodes))
        {
            var linkText = ExtractNavIndexLinks(ps4Nodes);
            return new HtmlPage(filePath, title, section, PageType.NavIndex,
                new Dictionary<string, string>(), linkText, rawHtml);
        }

        // Prose: everything else
        var proseText = BuildProseBodyText(title, doc);
        return new HtmlPage(filePath, title, section, PageType.Prose,
            new Dictionary<string, string>(), proseText, rawHtml);
    }

    private static string ExtractNavIndexLinks(HtmlNodeCollection? ps4Nodes)
    {
        if (ps4Nodes is null) return string.Empty;
        var items = new List<string>();
        foreach (var node in ps4Nodes)
        {
            var anchors = node.SelectNodes(".//a");
            if (anchors is null) continue;
            foreach (var a in anchors)
            {
                var text = HtmlEntity.DeEntitize(a.InnerText).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    items.Add(text);
            }
        }
        return string.Join("\n", items);
    }

    private static bool IsNavIndex(HtmlNodeCollection? ps4Nodes)
    {
        if (ps4Nodes is null) return true;
        var totalChars = 0;
        foreach (var node in ps4Nodes)
        {
            var clone = node.CloneNode(true);
            var anchors = clone.SelectNodes(".//a")?.ToList() ?? new List<HtmlNode>();
            foreach (var a in anchors) a.Remove();
            totalChars += HtmlEntity.DeEntitize(clone.InnerText).Trim().Length;
        }
        return totalChars < 20;
    }

    private static Dictionary<string, string> ExtractLabels(HtmlDocument doc)
    {
        var nodes = doc.DocumentNode.SelectNodes("//*[@class='ps4' or @class='ps6']");
        if (nodes is null) return new Dictionary<string, string>();

        var grouped = new Dictionary<string, List<string>>();
        string? currentKey = null;

        foreach (var node in nodes)
        {
            var cls = node.GetAttributeValue("class", "");
            var text = HtmlEntity.DeEntitize(node.InnerText).Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            if (cls == "ps4")
            {
                currentKey = text;
                if (!grouped.ContainsKey(currentKey))
                    grouped[currentKey] = new List<string>();
            }
            else if (cls == "ps6" && currentKey is not null)
            {
                grouped[currentKey].Add(text);
            }
        }

        return grouped.ToDictionary(
            kvp => kvp.Key,
            kvp => string.Join("\n  ", kvp.Value));
    }

    private static string BuildReferenceBodyText(string title, Dictionary<string, string> labels)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(title);
        foreach (var (k, v) in labels)
            sb.AppendLine($"{k}: {v}");
        return sb.ToString().TrimEnd();
    }

    private static string BuildProseBodyText(string title, HtmlDocument doc)
    {
        var nodes = doc.DocumentNode.SelectNodes("//*[@class='ps4' or @class='ps8']");
        var paragraphs = new List<string> { title };
        if (nodes is not null)
        {
            foreach (var node in nodes)
            {
                var text = HtmlEntity.DeEntitize(node.InnerText).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    paragraphs.Add(text);
            }
        }
        return string.Join("\n", paragraphs);
    }
}
