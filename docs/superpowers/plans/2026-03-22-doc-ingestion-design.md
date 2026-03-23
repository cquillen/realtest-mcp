# Doc Ingestion & Search Improvement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix `get_function_reference` returning wrong results by rewriting `ChmParser` to use CSS class structure, simplifying `DocChunker` to use the new page type system, adding exact-match lookup to `VectorStoreService`, and updating `GetFunctionReferenceTool` to a 4-step search cascade.

**Architecture:** `ChmParser` now classifies pages (Reference/Prose/NavIndex) and extracts structured `Labels` + breadcrumb `Section` instead of dumping raw body text. `DocChunker` switches on `PageType` — Reference pages emit `chunk_type="reference"` with `description=title` for exact-match lookup; Prose pages emit `chunk_type="page"`. `VectorStoreService` gains `SearchByDescriptionAsync` for `LOWER(description) = LOWER(@name)` lookup. `GetFunctionReferenceTool` uses a 4-step cascade: exact description → keyword reference → keyword all → vector.

**Tech Stack:** C# / .NET 10, HtmlAgilityPack (already in use), SQLite, xUnit

---

## File Map

| File | Change |
|------|--------|
| `tests/RealTestMcp.Tests/data/docs/single-function.html` | Replace: ATR reference page with ps1/ps2/ps4/ps6 structure |
| `tests/RealTestMcp.Tests/data/docs/multi-function.html` | Replace: Script Sections prose page with ps1/ps2/ps4/ps8 structure |
| `tests/RealTestMcp.Tests/data/docs/navindex-page.html` | Create: NavIndex page with ps2 + ps4 (links only, no prose text) |
| `tests/RealTestMcp.Tests/Parsers/ChmParserTests.cs` | Rewrite: tests for PageType, Section, Labels, BodyText structure |
| `src/RealTestMcp/Ingestion/Parsers/ChmParser.cs` | Rewrite: new HtmlPage record, PageType enum, structured extraction |
| `tests/RealTestMcp.Tests/Chunkers/DocChunkerTests.cs` | Rewrite: tests for reference/prose/navindex chunk outcomes |
| `src/RealTestMcp/Ingestion/Chunkers/DocChunker.cs` | Rewrite: remove function_entry path, switch on PageType |
| `tests/RealTestMcp.Tests/Core/VectorStoreServiceTests.cs` | Add: SearchByDescriptionAsync test |
| `src/RealTestMcp/Core/VectorStoreService.cs` | Add: SearchByDescriptionAsync method |
| `src/RealTestMcp/Tools/GetFunctionReferenceTool.cs` | Update: 4-step search cascade |
| `tests/RealTestMcp.Tests/Integration/IngestSearchTests.cs` | Update: remove function_entry reference, add description-match test |

---

## Task 1: Update HTML Test Fixtures

**Files:**
- Modify: `tests/RealTestMcp.Tests/data/docs/single-function.html`
- Modify: `tests/RealTestMcp.Tests/data/docs/multi-function.html`
- Create: `tests/RealTestMcp.Tests/data/docs/navindex-page.html`

- [ ] **Step 1: Replace single-function.html with ATR reference page**

Overwrite `tests/RealTestMcp.Tests/data/docs/single-function.html` with:

```html
<!DOCTYPE html>
<html>
<head><title>ATR</title></head>
<body>
<p class="ps1"><a class="hs1" href="rts.htm">Realtest Script Language</a> &gt; <a class="hs1" href="syntax.htm">Syntax Element Details</a></p>
<p class="ps2">ATR</p>
<p class="ps4">Category</p>
<p class="ps6">Indicator Functions</p>
<p class="ps4">Description</p>
<p class="ps6">Wilder's Average True Range</p>
<p class="ps4">Syntax</p>
<p class="ps6">ATR(len)</p>
<p class="ps4">Parameters</p>
<p class="ps6">len - lookback period</p>
<p class="ps4">Notes</p>
<p class="ps6">Calculation uses the original Welles Wilder formula</p>
</body>
</html>
```

- [ ] **Step 2: Replace multi-function.html with a prose page**

Overwrite `tests/RealTestMcp.Tests/data/docs/multi-function.html` with:

```html
<!DOCTYPE html>
<html>
<head><title>Script Sections</title></head>
<body>
<p class="ps1"><a class="hs1" href="rts.htm">Realtest Script Language</a></p>
<p class="ps2">Script Sections</p>
<p class="ps4">A RealScript strategy is composed of named sections that define entry and exit logic.</p>
<p class="ps4">Each section is evaluated once per bar during the backtest.</p>
<p class="ps8">EntrySetup: conditions that must be true before an entry is triggered</p>
<p class="ps8">ExitStop: price level at which to exit the trade with a stop order</p>
</body>
</html>
```

- [ ] **Step 3: Create navindex-page.html**

Create `tests/RealTestMcp.Tests/data/docs/navindex-page.html`:

```html
<!DOCTYPE html>
<html>
<head><title>Syntax Element Details</title></head>
<body>
<p class="ps1"><a class="hs1" href="rts.htm">Realtest Script Language</a></p>
<p class="ps2">Syntax Element Details</p>
<p class="ps4"><a href="atr.htm">ATR</a></p>
<p class="ps4"><a href="rsi.htm">RSI</a></p>
<p class="ps4"><a href="highest.htm">Highest</a></p>
</body>
</html>
```

> **Why navindex-page.html is NavIndex:** The three ps4 elements contain only `<a>` tags. After stripping anchor text, the ps4 inner text is empty (0 chars < 20 threshold), and there are no ps6 elements.

---

## Task 2: Write Failing ChmParser Tests

**Files:**
- Modify: `tests/RealTestMcp.Tests/Parsers/ChmParserTests.cs`

- [ ] **Step 1: Replace ChmParserTests.cs with new tests**

```csharp
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Tests.Parsers;

public class ChmParserTests
{
    private static string DataDir => Path.Combine(
        AppContext.BaseDirectory, "data", "docs");

    private static string ReferencePage => Path.Combine(DataDir, "single-function.html");
    private static string ProsePage     => Path.Combine(DataDir, "multi-function.html");
    private static string NavIndexPage  => Path.Combine(DataDir, "navindex-page.html");

    // ── ParseFile: reference page ───────────────────────────────────

    [Fact]
    public void ParseFile_ReferencePage_HasReferencePageType()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        Assert.Equal(PageType.Reference, page.PageType);
    }

    [Fact]
    public void ParseFile_ReferencePage_ExtractsTitleFromPs2()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        Assert.Equal("ATR", page.Title);
    }

    [Fact]
    public void ParseFile_ReferencePage_ExtractsBreadcrumbSection()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        Assert.Equal("Realtest Script Language > Syntax Element Details", page.Section);
    }

    [Fact]
    public void ParseFile_ReferencePage_ExtractsCategoryLabel()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        Assert.True(page.Labels.TryGetValue("Category", out var cat));
        Assert.Equal("Indicator Functions", cat);
    }

    [Fact]
    public void ParseFile_ReferencePage_ExtractsSyntaxLabel()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        Assert.True(page.Labels.TryGetValue("Syntax", out var syn));
        Assert.Equal("ATR(len)", syn);
    }

    [Fact]
    public void ParseFile_ReferencePage_BodyTextContainsTitle()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        Assert.StartsWith("ATR", page.BodyText);
    }

    [Fact]
    public void ParseFile_ReferencePage_BodyTextContainsLabeledContent()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        Assert.Contains("Category: Indicator Functions", page.BodyText);
        Assert.Contains("Syntax: ATR(len)", page.BodyText);
        Assert.Contains("Wilder's Average True Range", page.BodyText);
    }

    [Fact]
    public void ParseFile_ReferencePage_BodyTextHasNoHtmlTags()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        Assert.DoesNotContain("<", page.BodyText);
    }

    // ── ParseFile: prose page ───────────────────────────────────────

    [Fact]
    public void ParseFile_ProsePage_HasProsePageType()
    {
        var page = ChmParser.ParseFile(ProsePage);
        Assert.Equal(PageType.Prose, page.PageType);
    }

    [Fact]
    public void ParseFile_ProsePage_ExtractsTitleFromPs2()
    {
        var page = ChmParser.ParseFile(ProsePage);
        Assert.Equal("Script Sections", page.Title);
    }

    [Fact]
    public void ParseFile_ProsePage_ExtractsBreadcrumbSection()
    {
        var page = ChmParser.ParseFile(ProsePage);
        Assert.Equal("Realtest Script Language", page.Section);
    }

    [Fact]
    public void ParseFile_ProsePage_BodyTextContainsParagraphs()
    {
        var page = ChmParser.ParseFile(ProsePage);
        Assert.Contains("Script Sections", page.BodyText);
        Assert.Contains("named sections", page.BodyText);
        Assert.Contains("EntrySetup", page.BodyText);
    }

    // ── ParseFile: navindex page ────────────────────────────────────

    [Fact]
    public void ParseFile_NavIndexPage_HasNavIndexPageType()
    {
        var page = ChmParser.ParseFile(NavIndexPage);
        Assert.Equal(PageType.NavIndex, page.PageType);
    }

    [Fact]
    public void ParseFile_NavIndexPage_HasEmptyBodyText()
    {
        var page = ChmParser.ParseFile(NavIndexPage);
        Assert.True(string.IsNullOrEmpty(page.BodyText));
    }

    // ── ParseDirectory ──────────────────────────────────────────────

    [Fact]
    public void ParseDirectory_ExcludesNavIndexPages()
    {
        var pages = ChmParser.ParseDirectory(DataDir);
        Assert.DoesNotContain(pages, p => p.PageType == PageType.NavIndex);
    }

    [Fact]
    public void ParseDirectory_IncludesReferenceAndProsePages()
    {
        var pages = ChmParser.ParseDirectory(DataDir);
        Assert.Contains(pages, p => p.PageType == PageType.Reference);
        Assert.Contains(pages, p => p.PageType == PageType.Prose);
    }
}
```

- [ ] **Step 2: Run tests to confirm they all fail**

```
dotnet test tests/RealTestMcp.Tests/RealTestMcp.Tests.csproj --filter "FullyQualifiedName~ChmParserTests" -v n
```

Expected: Build errors or test failures because `PageType` and new `HtmlPage` fields don't exist yet.

---

## Task 3: Implement ChmParser Rewrite

**Files:**
- Modify: `src/RealTestMcp/Ingestion/Parsers/ChmParser.cs`

- [ ] **Step 1: Replace ChmParser.cs**

```csharp
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

        // Rule 0: malformed — no ps2 AND no ps4 → skip (empty BodyText)
        if (ps2 is null && ps4Nodes is null)
            return new HtmlPage(filePath, title, section, PageType.NavIndex,
                new Dictionary<string, string>(), string.Empty, rawHtml);

        // Rule 2: reference — at least one ps6 present
        if (ps6Nodes is { Count: > 0 })
        {
            var labels = ExtractLabels(doc);
            var bodyText = BuildReferenceBodyText(title, labels);
            return new HtmlPage(filePath, title, section, PageType.Reference, labels, bodyText, rawHtml);
        }

        // Rule 1: navindex — no ps6 AND ps4 text (after stripping links) < 20 chars
        if (IsNavIndex(ps4Nodes))
            return new HtmlPage(filePath, title, section, PageType.NavIndex,
                new Dictionary<string, string>(), string.Empty, rawHtml);

        // Rule 3: prose — everything else
        var proseText = BuildProseBodyText(title, doc);
        return new HtmlPage(filePath, title, section, PageType.Prose,
            new Dictionary<string, string>(), proseText, rawHtml);
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
```

- [ ] **Step 2: Run ChmParser tests**

```
dotnet test tests/RealTestMcp.Tests/RealTestMcp.Tests.csproj --filter "FullyQualifiedName~ChmParserTests" -v n
```

Expected: All ChmParser tests pass. Some DocChunker/Integration tests may now fail due to signature changes — that's expected and will be fixed in the next tasks.

- [ ] **Step 3: Commit**

```bash
git add src/RealTestMcp/Ingestion/Parsers/ChmParser.cs \
        tests/RealTestMcp.Tests/Parsers/ChmParserTests.cs \
        tests/RealTestMcp.Tests/data/docs/single-function.html \
        tests/RealTestMcp.Tests/data/docs/multi-function.html \
        tests/RealTestMcp.Tests/data/docs/navindex-page.html
git commit -m "feat: rewrite ChmParser with structured page classification and label extraction"
```

---

## Task 4: Write Failing DocChunker Tests

**Files:**
- Modify: `tests/RealTestMcp.Tests/Chunkers/DocChunkerTests.cs`

- [ ] **Step 1: Replace DocChunkerTests.cs**

```csharp
using RealTestMcp.Ingestion.Chunkers;
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Tests.Chunkers;

public class DocChunkerTests
{
    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "data", "docs");

    private static string ReferencePage => Path.Combine(DataDir, "single-function.html");
    private static string ProsePage     => Path.Combine(DataDir, "multi-function.html");
    private static string NavIndexPage  => Path.Combine(DataDir, "navindex-page.html");

    // ── reference page ──────────────────────────────────────────────

    [Fact]
    public void ReferencePage_ProducesOneReferenceChunk()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        var chunks = DocChunker.Chunk(page);

        Assert.Single(chunks);
        Assert.Equal("reference", chunks[0].ChunkType);
    }

    [Fact]
    public void ReferencePage_ChunkHasDescriptionEqualToTitle()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        var chunks = DocChunker.Chunk(page);

        Assert.Equal("ATR", chunks[0].Description);
    }

    [Fact]
    public void ReferencePage_ChunkHasCategoryFromLabel()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        var chunks = DocChunker.Chunk(page);

        Assert.Equal("Indicator Functions", chunks[0].Category);
    }

    [Fact]
    public void ReferencePage_ChunkHasSectionFromBreadcrumb()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        var chunks = DocChunker.Chunk(page);

        Assert.Equal("Realtest Script Language > Syntax Element Details", chunks[0].Section);
    }

    [Fact]
    public void ReferencePage_ChunkContentContainsLabeledText()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        var chunks = DocChunker.Chunk(page);

        Assert.Contains("ATR", chunks[0].Content);
        Assert.Contains("Indicator Functions", chunks[0].Content);
        Assert.Contains("ATR(len)", chunks[0].Content);
    }

    [Fact]
    public void ReferencePage_ChunkSourceTypeIsDocs()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        var chunks = DocChunker.Chunk(page);

        Assert.Equal("docs", chunks[0].SourceType);
    }

    // ── prose page ───────────────────────────────────────────────────

    [Fact]
    public void ProsePage_ProducesOnePageChunk()
    {
        var page = ChmParser.ParseFile(ProsePage);
        var chunks = DocChunker.Chunk(page);

        Assert.Single(chunks);
        Assert.Equal("page", chunks[0].ChunkType);
    }

    [Fact]
    public void ProsePage_ChunkHasDescriptionEqualToTitle()
    {
        var page = ChmParser.ParseFile(ProsePage);
        var chunks = DocChunker.Chunk(page);

        Assert.Equal("Script Sections", chunks[0].Description);
    }

    [Fact]
    public void ProsePage_ChunkSectionFromBreadcrumb()
    {
        var page = ChmParser.ParseFile(ProsePage);
        var chunks = DocChunker.Chunk(page);

        Assert.Equal("Realtest Script Language", chunks[0].Section);
    }

    // ── navindex page ─────────────────────────────────────────────────

    [Fact]
    public void NavIndexPage_ProducesNoChunks()
    {
        var page = ChmParser.ParseFile(NavIndexPage);
        var chunks = DocChunker.Chunk(page);

        Assert.Empty(chunks);
    }

    // ── determinism ───────────────────────────────────────────────────

    [Fact]
    public void ChunkIds_AreDeterministic()
    {
        var page = ChmParser.ParseFile(ReferencePage);
        var chunks1 = DocChunker.Chunk(page);
        var chunks2 = DocChunker.Chunk(page);

        Assert.Equal(chunks1[0].Id, chunks2[0].Id);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test tests/RealTestMcp.Tests/RealTestMcp.Tests.csproj --filter "FullyQualifiedName~DocChunkerTests" -v n
```

Expected: Compile errors because `DocChunker` still has old signature / `HtmlPage` record changed.

---

## Task 5: Implement DocChunker Simplification

**Files:**
- Modify: `src/RealTestMcp/Ingestion/Chunkers/DocChunker.cs`

- [ ] **Step 1: Replace DocChunker.cs**

```csharp
using RealTestMcp.Core;
using RealTestMcp.Core.Models;
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Ingestion.Chunkers;

public static class DocChunker
{
    public static List<Chunk> Chunk(HtmlPage page)
    {
        return page.PageType switch
        {
            PageType.Reference => [ReferenceChunk(page)],
            PageType.Prose     => [ProseChunk(page)],
            _                  => []   // NavIndex: skip
        };
    }

    private static Chunk ReferenceChunk(HtmlPage page)
    {
        page.Labels.TryGetValue("Category", out var category);
        var id = VectorStoreService.ComputeChunkId(page.FilePath, 0);
        return new Chunk(
            Id: id,
            SourceType: "docs",
            SourcePath: page.FilePath,
            ChunkType: "reference",
            Section: NullIfEmpty(page.Section),
            Category: NullIfEmpty(category),
            Description: page.Title,
            Content: page.BodyText,
            ChunkIndex: 0,
            CreatedAt: DateTime.UtcNow);
    }

    private static Chunk ProseChunk(HtmlPage page)
    {
        var id = VectorStoreService.ComputeChunkId(page.FilePath, 0);
        return new Chunk(
            Id: id,
            SourceType: "docs",
            SourcePath: page.FilePath,
            ChunkType: "page",
            Section: NullIfEmpty(page.Section),
            Category: null,
            Description: page.Title,
            Content: page.BodyText,
            ChunkIndex: 0,
            CreatedAt: DateTime.UtcNow);
    }

    private static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s;
}
```

- [ ] **Step 2: Run DocChunker tests**

```
dotnet test tests/RealTestMcp.Tests/RealTestMcp.Tests.csproj --filter "FullyQualifiedName~DocChunkerTests" -v n
```

Expected: All DocChunker tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/RealTestMcp/Ingestion/Chunkers/DocChunker.cs \
        tests/RealTestMcp.Tests/Chunkers/DocChunkerTests.cs
git commit -m "feat: simplify DocChunker to use PageType, retire function_entry chunk type"
```

---

## Task 6: Write Failing SearchByDescriptionAsync Test

**Files:**
- Modify: `tests/RealTestMcp.Tests/Core/VectorStoreServiceTests.cs`

- [ ] **Step 1: Add test to VectorStoreServiceTests**

Add this test to the existing `VectorStoreServiceTests` class (after the `KeywordSearch_FindsChunkByContent` test):

```csharp
[Fact]
public async Task SearchByDescription_ExactMatchCaseInsensitive()
{
    var chunk = MakeChunk("ref-1", "docs", "reference", content: "ATR\nCategory: Indicator Functions\nSyntax: ATR(len)");
    // Set description by using the overload that accepts all fields
    var chunkWithDesc = chunk with { Description = "ATR" };
    await _store.UpsertChunkAsync(chunkWithDesc, MakeEmbedding(1.0f));

    // Exact match, case-insensitive
    var results = await _store.SearchByDescriptionAsync("atr");

    Assert.Single(results);
    Assert.Equal("ref-1", results[0].Id);
}

[Fact]
public async Task SearchByDescription_NoMatchForPartialName()
{
    var chunk = MakeChunk("ref-2", "docs", "reference") with { Description = "ATR" };
    await _store.UpsertChunkAsync(chunk, MakeEmbedding(1.0f));

    // "AT" is not an exact match for "ATR"
    var results = await _store.SearchByDescriptionAsync("AT");

    Assert.Empty(results);
}

[Fact]
public async Task SearchByDescription_DefaultsToReferenceChunkType()
{
    var refChunk  = MakeChunk("ref-3", "docs", "reference") with { Description = "RSI" };
    var pageChunk = MakeChunk("pag-3", "docs", "page")      with { Description = "RSI" };
    await _store.UpsertChunkAsync(refChunk,  MakeEmbedding(1.0f));
    await _store.UpsertChunkAsync(pageChunk, MakeEmbedding(2.0f));

    var results = await _store.SearchByDescriptionAsync("RSI");

    Assert.Single(results);
    Assert.Equal("ref-3", results[0].Id);
}
```

Also update the `MakeChunk` helper to enable `with` expressions — the current helper returns `Chunk` which is already a record, so `with` works. No helper change needed.

- [ ] **Step 2: Run tests to confirm the new tests fail**

```
dotnet test tests/RealTestMcp.Tests/RealTestMcp.Tests.csproj --filter "FullyQualifiedName~VectorStoreServiceTests" -v n
```

Expected: Compile error — `SearchByDescriptionAsync` does not exist yet.

---

## Task 7: Implement SearchByDescriptionAsync

**Files:**
- Modify: `src/RealTestMcp/Core/VectorStoreService.cs`

- [ ] **Step 1: Add method to VectorStoreService**

Add after the `KeywordSearchAsync` method (around line 221):

```csharp
public async Task<List<SearchResult>> SearchByDescriptionAsync(
    string name,
    string? chunkType = "reference",
    int topK = 1)
{
    var conn = await GetConnectionAsync();
    var whereChunkType = chunkType is not null ? "AND chunk_type = @chunk_type" : "";
    var sql = $"""
        SELECT id, source_type, source_path, chunk_type, section, category, description, content, 0.0 AS distance
        FROM chunks
        WHERE LOWER(description) = LOWER(@name)
          {whereChunkType}
        LIMIT @topk
        """;

    var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@name", name);
    cmd.Parameters.AddWithValue("@topk", topK);
    if (chunkType is not null) cmd.Parameters.AddWithValue("@chunk_type", chunkType);

    return await ReadSearchResultsAsync(cmd);
}
```

- [ ] **Step 2: Run VectorStoreService tests**

```
dotnet test tests/RealTestMcp.Tests/RealTestMcp.Tests.csproj --filter "FullyQualifiedName~VectorStoreServiceTests" -v n
```

Expected: All VectorStoreService tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/RealTestMcp/Core/VectorStoreService.cs \
        tests/RealTestMcp.Tests/Core/VectorStoreServiceTests.cs
git commit -m "feat: add SearchByDescriptionAsync for exact function name lookup"
```

---

## Task 8: Update GetFunctionReferenceTool and Integration Tests

**Files:**
- Modify: `src/RealTestMcp/Tools/GetFunctionReferenceTool.cs`
- Modify: `tests/RealTestMcp.Tests/Integration/IngestSearchTests.cs`

- [ ] **Step 1: Replace GetFunctionReferenceTool.cs**

```csharp
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
```

- [ ] **Step 2: Update IngestSearchTests — remove function_entry references, add description-match test**

In `IngestSearchTests.cs`, replace `GetFunctionReference_KeywordPath_FindsATR` with:

```csharp
[Fact]
public async Task GetFunctionReference_DescriptionMatch_FindsATR()
{
    // Step 1 of cascade: exact description match
    var results = await _store.SearchByDescriptionAsync("ATR");

    Assert.NotEmpty(results);
    Assert.All(results, r => Assert.Equal("reference", r.ChunkType));
    Assert.Contains(results, r =>
        r.Description?.Equals("ATR", StringComparison.OrdinalIgnoreCase) == true);
}

[Fact]
public async Task GetFunctionReference_KeywordFallback_FindsATR()
{
    // Step 2 of cascade: keyword search in reference chunks
    var results = await _store.KeywordSearchAsync("ATR", chunkType: "reference", topK: 3);

    Assert.NotEmpty(results);
    Assert.Contains(results, r => r.Content.Contains("ATR"));
}
```

Also update `GetFunctionReference_SemanticFallback_FindsFunction` — change the keyword-miss assertion to use `chunkType: "reference"` instead of the retired `"function_entry"`:

```csharp
[Fact]
public async Task GetFunctionReference_SemanticFallback_FindsFunction()
{
    // Search for something that won't match keyword but should match semantically
    var keywordResults = await _store.KeywordSearchAsync("ZZZNOMATCH", chunkType: "reference", topK: 3);
    Assert.Empty(keywordResults); // confirm keyword misses

    var queryEmbedding = await _embedder.EmbedAsync("highest value over lookback period");
    var semanticResults = await _store.VectorSearchAsync(queryEmbedding, sourceType: "docs", topK: 3);
    Assert.NotEmpty(semanticResults);
}
```

- [ ] **Step 3: Run the integration tests (requires vec0.dll)**

```
dotnet test tests/RealTestMcp.Tests/RealTestMcp.Tests.csproj --filter "FullyQualifiedName~IngestSearchTests" -v n
```

Expected: All integration tests pass. The description-match test finds ATR via exact lookup; keyword fallback finds it via content search.

- [ ] **Step 4: Commit**

```bash
git add src/RealTestMcp/Tools/GetFunctionReferenceTool.cs \
        tests/RealTestMcp.Tests/Integration/IngestSearchTests.cs
git commit -m "feat: update GetFunctionReferenceTool to 4-step search cascade with exact description match"
```

---

## Task 9: Full Test Suite

- [ ] **Step 1: Run all tests**

```
dotnet test tests/RealTestMcp.Tests/RealTestMcp.Tests.csproj -v n
```

Expected: All tests pass. If any test fails, diagnose using the test name and error message before proceeding.

- [ ] **Step 2: Verify success criteria**

Manually verify against spec requirements:
- `ParseFile` on ATR page → `PageType.Reference`, `Description = "ATR"`, `Category = "Indicator Functions"`, `Section` contains "Syntax Element Details"
- `SearchByDescriptionAsync("ATR")` returns exactly 1 result
- NavIndex pages produce 0 chunks
- `DocChunker.Chunk` on prose page → `chunk_type = "page"`, `Description` = title, `Section` from breadcrumb

- [ ] **Step 3: Final commit (if any fixups were needed)**

```bash
git add -u
git commit -m "chore: final fixups from full test run"
```
