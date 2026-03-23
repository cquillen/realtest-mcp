# Doc Ingestion & Search Improvement Design

**Date:** 2026-03-22
**Status:** Approved
**Scope:** `ChmParser`, `DocChunker`, `VectorStoreService`, `GetFunctionReferenceTool`

---

## Problem

The RealTest MCP `get_function_reference` tool returns wrong results for exact function lookups (e.g., searching "ATR" returns pages about FuturesMargin and scaling positions instead of the ATR function page). Two root causes:

1. **Flat parsing** — `ChmParser` uses `body.InnerText`, discarding the CSS class structure that distinguishes section labels (`ps4`) from section values (`ps6`). Structured content like `"Syntax: ATR(len)"` becomes word soup.
2. **Unranked keyword search** — `LIKE '%ATR%'` with no ordering returns whatever 3 rows the table scan finds first, not the most relevant page.

---

## HTML Page Structure

All pages in `C:\RealTest\Help\` share a common layout:

| CSS class | Role |
|-----------|------|
| `ps1` | Navigation breadcrumb (section path) |
| `ps2` | Page title |
| `ps3` | Prev/Next nav arrows — ignore |
| `ps4` | Section label (reference pages) OR body paragraph (prose pages) |
| `ps6` | Section content value — only present on reference pages |
| `ps8` | Bullet list items |

**Two content shapes exist:**

- **Reference pages** — have `ps6` elements; `ps4` = label key, `ps6` = label value. Used for Syntax Element Details (functions, strategy elements, data elements, etc.).
- **Prose pages** — no `ps6`; `ps4` and `ps8` are flowing paragraphs. Used for Script Sections, UI docs, Backtest Engine Details, etc.
- **NavIndex pages** — title + link lists only, no substantive content. Skip during ingestion.

---

## Design

### 1. `ChmParser` — Page Classification & Structured Extraction

`ParseFile` returns a new `HtmlPage` record — the existing definition is replaced entirely:

```csharp
public record HtmlPage(
    string FilePath,
    string Title,
    string Section,         // extracted from ps1 breadcrumb links
    PageType PageType,      // Reference | Prose | NavIndex
    Dictionary<string, string> Labels,  // populated for Reference pages
    string BodyText,        // structured text for embedding
    string RawHtml);

public enum PageType { Reference, Prose, NavIndex }
```

**Classification rules (applied in order):**

0. **Skip (malformed)** — page has no `ps2` title element and no `ps4` elements. Produces no chunk.
1. **NavIndex** — `ps6` is absent AND the total character count of all `ps4` inner text *after stripping `<a>` tag inner text and whitespace* is less than 20 characters. These are link-list navigation pages with no prose. Skip during ingestion.
2. **Reference** — at least one `ps6` element is present. Extract `ps4` inner text as label keys; collect all consecutive `ps6` inner texts under that label. Multiple `ps6` elements under the same label are joined with `"\n  "` (newline + two spaces) so continuation lines are visually subordinate and cannot be confused with the opening title line.
3. **Prose** — everything else. Extract `ps4` and `ps8` elements as paragraphs.

**Section extraction:** Select all `<a>` elements with CSS class `hs1` (the breadcrumb links — e.g., `<a href="..." class="hs1">Realtest Script Language</a>`). Collect their inner text and join with ` > ` (e.g., `"Realtest Script Language > Syntax Element Details"`). The `ps1` class marks the containing paragraph; the `hs1` class marks the individual anchor links within it.

**BodyText for reference pages** — reconstructed as labeled content. For labels with multiple values (e.g., multiple parameters), continuation lines are indented with two spaces:
```
ATR
Category: Indicator Functions
Description: Wilder's Average True Range
Syntax: ATR(len)
Parameters: len - lookback period
Notes: Calculation uses the original Welles Wilder formula...
```
Multi-value example:
```
SomeFunc
Parameters: x - first value
  y - second value
  z - third value
```

**BodyText for prose pages** — title followed by paragraphs joined with `\n`.

### 2. `DocChunker` — Simplified Mapper

The `ChunkByFunctionEntry` path (multi-`<h2>` detection) is removed — it was a workaround for the old flat parser. The `function_entry` chunk type is retired; all reference docs now use `chunk_type = "reference"`. Existing `function_entry` rows are automatically removed by `DeleteBySourceTypeAsync("docs")` on re-ingestion. `DocChunker.Chunk()` now switches on `page.PageType` (pre-classified by `ChmParser`) rather than re-parsing `RawHtml`. All pages flow through two paths:

**Reference pages → `chunk_type: "reference"`**

Many RealTest help pages use `"X or Y"` alias titles (e.g. `"EMA or XAvg"`, `"Highest or HHV"`). A single such page produces **one chunk per alias**, all with identical content. This ensures `SearchByDescriptionAsync("EMA")` and `SearchByDescriptionAsync("XAvg")` both hit Step 1 exact match.

Split rule: if `page.Title` contains `" or "`, split on `" or "` to get aliases. Each alias becomes a separate chunk with `description = alias` and `chunk_index = 0, 1, 2, ...`. Pages with a single-token title (e.g. `"ATR"`) produce one chunk as before.

- `description`: one alias token from the title split (e.g. `"EMA"` or `"XAvg"`)
- `category`: first value of the `"Category"` label if present (multi-value Category is not expected but first-value is taken if it occurs)
- `section`: breadcrumb section string
- `content`: structured labeled `BodyText` (identical across all alias chunks for the same page)

**Prose pages → `chunk_type: "page"`**
- `description`: page title
- `section`: breadcrumb section string
- `content`: `BodyText` (title + paragraphs)

**NavIndex pages** → skipped, no chunk created.

### 3. `VectorStoreService` — New Exact-Match Method

Add `SearchByDescriptionAsync(string name, string? chunkType = "reference", int topK = 1)`:

```sql
SELECT ... FROM chunks
WHERE LOWER(description) = LOWER(@name)
  AND chunk_type = @chunk_type   -- defaults to "reference"
LIMIT @topk
```

Defaults to `chunk_type = "reference"` and `topK = 1` since an exact function name lookup should return at most one page. The `chunkType` parameter can be set to `null` to search all types.

### 4. `GetFunctionReferenceTool` — Priority Search Cascade

Replace the current 3-step lookup with a 4-step cascade, returning immediately when results are found:

| Step | Method | Notes |
|------|--------|-------|
| 1 | `SearchByDescriptionAsync(name)` | Exact `LOWER(description) = LOWER(name)`, `chunk_type = "reference"` |
| 2 | `KeywordSearchAsync(name, chunkType: "reference")` | `content LIKE '%name%'` filtered to reference chunks — catches cases where description is missing |
| 3 | `KeywordSearchAsync(name, chunkType: null)` | `content LIKE '%name%'` across all chunk types |
| 4 | `VectorSearchAsync(embedding, sourceType: "docs")` | Semantic embedding fallback |

**Note on Step 2:** `KeywordSearchAsync` uses `LIKE '%name%'` (not a prefix match). The prefix behaviour described during design is not needed since Step 1 already handles exact matches; Step 2 just provides a fallback for reference chunks where `description` wasn't populated.

---

## Data Flow

```
C:\RealTest\Help\*.htm
        │
        ▼
  ChmParser.ParseFile()
  ├── classify page type
  ├── extract breadcrumb → Section
  ├── extract ps4/ps6 → Labels (reference)
  ├── extract ps4/ps8 → paragraphs (prose)
  └── build structured BodyText
        │
        ▼
  DocChunker.Chunk()
  ├── NavIndex  → skip
  ├── Reference → chunk_type="reference", description=title, category=label
  └── Prose     → chunk_type="page", description=title
        │
        ▼
  EmbeddingService.EmbedAsync(content)
        │
        ▼
  VectorStoreService.UpsertChunkAsync()
```

---

## Out of Scope

- No schema migration — existing `chunks` table schema supports all new fields (`description`, `section`, `category` already exist)
- No changes to `ingest scripts`, `EmbeddingService`, or other tools
- Re-ingestion: user runs `realtest-mcp ingest docs` after deploying; `DeleteBySourceTypeAsync("docs")` handles cleanup automatically

---

## Success Criteria

- `get_function_reference("ATR")` returns the ATR function page as the top result
- `get_function_reference("EMA")` returns the EMA page (title `"EMA or XAvg"`) as the top result via exact description match
- `get_function_reference("XAvg")` returns the same EMA page via exact description match
- Reference pages store labeled content (Category, Syntax, Parameters, Notes visible in chunk content)
- `section` field is populated from breadcrumb for all chunks (not empty/directory name)
- NavIndex pages produce no chunks
- At least 95% of pages whose breadcrumb contains "Syntax Element Details" ingest as `chunk_type = "reference"` with `description` populated (the remaining ~5% are expected NavIndex or malformed pages)
