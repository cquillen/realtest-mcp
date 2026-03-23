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

`ParseFile` is extended to return an enriched `HtmlPage` record:

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

1. **NavIndex** — `ps6` is absent AND `ps4` contains only whitespace/links (no prose text). These are skipped during ingestion.
2. **Reference** — at least one `ps6` element is present. Extract `ps4` inner text as label keys; collect all consecutive `ps6` inner texts under that label, joining with `\n`.
3. **Prose** — everything else. Extract `ps4` and `ps8` elements as paragraphs.

**Section extraction:** Collect inner text of all `ps1`-class anchor (`hs1`) links, joined with ` > ` (e.g., `"Realtest Script Language > Syntax Element Details"`).

**BodyText for reference pages** — reconstructed as labeled content:
```
ATR
Category: Indicator Functions
Description: Wilder's Average True Range
Syntax: ATR(len)
Parameters: len - lookback period
Notes: Calculation uses the original Welles Wilder formula...
```

**BodyText for prose pages** — title followed by paragraphs joined with `\n`.

### 2. `DocChunker` — Simplified Mapper

The `ChunkByFunctionEntry` path (multi-`<h2>` detection) is removed — it was a workaround for the old flat parser. All pages now flow through two paths:

**Reference pages → `chunk_type: "reference"`**
- `description`: page title (enables exact-match lookup)
- `category`: value of the `"Category"` label if present
- `section`: breadcrumb section string
- `content`: structured labeled `BodyText`

**Prose pages → `chunk_type: "page"`**
- `description`: page title
- `section`: breadcrumb section string
- `content`: `BodyText` (title + paragraphs)

**NavIndex pages** → skipped, no chunk created.

### 3. `VectorStoreService` — New Exact-Match Method

Add `SearchByDescriptionAsync(string name, string? sourceType)`:

```sql
SELECT ... FROM chunks
WHERE LOWER(description) = LOWER(@name)
  AND source_type = @source_type   -- if provided
LIMIT @topk
```

### 4. `GetFunctionReferenceTool` — Priority Search Cascade

Replace the current 3-step lookup with a 4-step cascade, returning immediately when results are found:

| Step | Method | Query |
|------|--------|-------|
| 1 | `SearchByDescriptionAsync` | Exact `description = name` |
| 2 | `KeywordSearchAsync` | `content LIKE 'name\n%'` filtered to `chunk_type = "reference"` |
| 3 | `KeywordSearchAsync` | `content LIKE '%name%'` across all types |
| 4 | `VectorSearchAsync` | Semantic embedding fallback |

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
- Reference pages store labeled content (Category, Syntax, Parameters, Notes visible in chunk content)
- `section` field is populated from breadcrumb for all chunks (not empty/directory name)
- NavIndex pages produce no chunks
- All 583 Syntax Element Detail pages ingest as `chunk_type = "reference"` with `description` populated
