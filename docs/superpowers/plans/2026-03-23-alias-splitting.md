# Alias Splitting for "X or Y" Reference Pages Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix `get_function_reference("EMA")` by splitting "X or Y" page titles at ingestion so each alias gets its own chunk with `description = alias`, enabling exact-match lookup for all aliases.

**Architecture:** `DocChunker.ReferenceChunk` is replaced by `ReferenceChunks` which splits `page.Title` on `" or "` and emits one chunk per token. Single-token titles (e.g. `"ATR"`) produce one chunk — no behaviour change. Multi-token titles (e.g. `"EMA or XAvg"`) produce one chunk per alias, all with identical content. Chunk IDs are deterministic via `ComputeChunkId(filePath, chunkIndex)`.

**Tech Stack:** C# / .NET 10, xUnit

---

## File Map

| File | Change |
|------|--------|
| `src/RealTestMcp/Ingestion/Chunkers/DocChunker.cs` | Replace `ReferenceChunk` with `ReferenceChunks`, split on `" or "` |
| `tests/RealTestMcp.Tests/Chunkers/DocChunkerTests.cs` | Add alias-splitting tests |

No new files. No schema changes. No fixture changes — alias tests construct `HtmlPage` directly rather than parsing HTML.

---

## Task 1: Write Failing Alias Tests

**Files:**
- Modify: `tests/RealTestMcp.Tests/Chunkers/DocChunkerTests.cs`

- [ ] **Step 1: Add three alias-splitting tests to DocChunkerTests**

Add after the existing `ChunkIds_AreDeterministic` test (before the closing `}`):

```csharp
// ── "X or Y" alias splitting ──────────────────────────────────────

[Fact]
public void ReferencePage_WithOrAlias_ProducesOneChunkPerAlias()
{
    var labels = new Dictionary<string, string> { ["Category"] = "Multi-Bar Functions" };
    var bodyText = "EMA or XAvg\nCategory: Multi-Bar Functions\nSyntax: EMA(expr, count)";
    var page = new HtmlPage("/fake/ema.html", "EMA or XAvg",
        "Realtest Script Language > Syntax Element Details",
        PageType.Reference, labels, bodyText, "");

    var chunks = DocChunker.Chunk(page);

    Assert.Equal(2, chunks.Count);
    Assert.All(chunks, c => Assert.Equal("reference", c.ChunkType));
    Assert.Contains(chunks, c => c.Description == "EMA");
    Assert.Contains(chunks, c => c.Description == "XAvg");
}

[Fact]
public void ReferencePage_WithOrAlias_AllChunksHaveSameContent()
{
    var labels = new Dictionary<string, string>();
    var bodyText = "EMA or XAvg\nSyntax: EMA(expr, count)";
    var page = new HtmlPage("/fake/ema.html", "EMA or XAvg", "",
        PageType.Reference, labels, bodyText, "");

    var chunks = DocChunker.Chunk(page);

    Assert.Equal(chunks[0].Content, chunks[1].Content);
}

[Fact]
public void ReferencePage_WithOrAlias_ChunkIdsAreDeterministicAndDistinct()
{
    var labels = new Dictionary<string, string>();
    var page = new HtmlPage("/fake/ema.html", "EMA or XAvg", "",
        PageType.Reference, labels, "EMA or XAvg", "");

    var chunks1 = DocChunker.Chunk(page);
    var chunks2 = DocChunker.Chunk(page);

    Assert.Equal(chunks1[0].Id, chunks2[0].Id);   // deterministic
    Assert.Equal(chunks1[1].Id, chunks2[1].Id);   // deterministic
    Assert.NotEqual(chunks1[0].Id, chunks1[1].Id); // distinct per alias
}
```

> **Note:** `HtmlPage` is a record — construct it directly rather than parsing an HTML file. No new fixture needed.

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test tests/RealTestMcp.Tests/RealTestMcp.Tests.csproj --filter "FullyQualifiedName~DocChunkerTests" -v n
```

Expected: 3 failures — `ReferencePage_WithOrAlias_*` fail because `DocChunker` currently returns one chunk regardless of `" or "` in the title.

- [ ] **Step 3: Commit**

```bash
git add tests/RealTestMcp.Tests/Chunkers/DocChunkerTests.cs
git commit -m "test: add alias-splitting tests for 'X or Y' reference page titles"
```

---

## Task 2: Implement Alias Splitting in DocChunker

**Files:**
- Modify: `src/RealTestMcp/Ingestion/Chunkers/DocChunker.cs`

- [ ] **Step 1: Replace `ReferenceChunk` with `ReferenceChunks` and update `Chunk`**

Replace the entire file:

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
            PageType.Reference => ReferenceChunks(page),
            PageType.Prose     => [ProseChunk(page)],
            _                  => []   // NavIndex: skip
        };
    }

    private static List<Chunk> ReferenceChunks(HtmlPage page)
    {
        page.Labels.TryGetValue("Category", out var category);

        // "EMA or XAvg" → ["EMA", "XAvg"]; "ATR" → ["ATR"]
        var aliases = page.Title.Split(" or ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        return aliases.Select((alias, i) => new Chunk(
            Id: VectorStoreService.ComputeChunkId(page.FilePath, i),
            SourceType: "docs",
            SourcePath: page.FilePath,
            ChunkType: "reference",
            Section: NullIfEmpty(page.Section),
            Category: NullIfEmpty(category),
            Description: alias,
            Content: page.BodyText,
            ChunkIndex: i,
            CreatedAt: DateTime.UtcNow)).ToList();
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

Expected: All 14 tests pass (11 existing + 3 new alias tests). Existing tests are unaffected because `"ATR"` has no `" or "` — produces a single-element aliases array, same as before.

- [ ] **Step 3: Run full test suite**

```
dotnet test tests/RealTestMcp.Tests/RealTestMcp.Tests.csproj -v q
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/RealTestMcp/Ingestion/Chunkers/DocChunker.cs
git commit -m "feat: split 'X or Y' reference titles into per-alias chunks for exact-match lookup"
```
