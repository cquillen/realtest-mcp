# RealTest MCP — Python Rewrite Design Spec

**Date:** 2026-03-25
**Status:** Draft
**Replaces:** .NET implementation (src/RealTestMcp/)

## Goal

Reimplement the RealTest MCP server in Python, replacing the existing .NET implementation. Source all documentation from the RealTest User Guide PDF (the authoritative reference) instead of CHM/HTML help files. Improve retrieval quality with better embeddings, better content coverage (583 elements vs. partial), and a redesigned tool API.

## Motivation

A comprehensive gap analysis of 112+ RealScript files revealed the current MCP server is missing ~60% of the language surface. The root causes:

1. **Content source** — CHM/HTML files have poor semantic structure (table-based layout), making extraction unreliable
2. **Incomplete indexing** — many elements, sections, and concepts were never ingested
3. **Embedding quality** — all-MiniLM-L6-v2 (2021) is outperformed by newer models
4. **Tool design** — no discovery tool (`list_elements`), no section-level retrieval (`get_section`), the language guide dumps 604 lines of incomplete content in one call

The PDF extracts cleanly (proven via POC), contains all 583 syntax elements in a consistent structured format, and covers narrative documentation that explains how things work.

## Architecture

```
Build Time (run on docs update)          Runtime (MCP server)
================================         ====================
PDF ──> PyMuPDF ──> text stream          MCP SDK (StdIO)
  ──> TOC-based splitter                   ──> 6 tools
  ──> markdown formatter                   ──> ChromaDB queries
  ──> ChromaDB + BGE-base-en-v1.5          ──> formatted markdown results

.rts scripts ──> raw read
  ──> one chunk per file
  ──> ChromaDB
```

Single Python package. Two modes: `ingest` (build time) and `serve` (runtime). Shared ChromaDB persistent store on disk.

## Tech Stack

| Component | Technology | Why |
|-----------|-----------|-----|
| MCP server | `mcp` Python SDK, StdIO transport | Standard MCP implementation |
| Vector store | ChromaDB (embedded, persistent) | Handles embed + store + query; metadata filtering; no external server |
| Embeddings | BGE-base-en-v1.5 (768 dims) via `sentence-transformers` | Best retrieval quality at this scale; significant upgrade from MiniLM |
| PDF extraction | PyMuPDF (`fitz`) | Best-in-class PDF text extraction |
| Config | TOML (`tomllib` stdlib) | No extra dependency, supports comments, Python ecosystem standard |

## CLI Interface

```bash
python -m realtest_mcp serve           # Start MCP server (StdIO)
python -m realtest_mcp ingest          # Extract PDF + index scripts into ChromaDB
python -m realtest_mcp status          # Show DB stats (chunk counts, last ingest time)
```

If no data is indexed when tools are called, return a helpful error: "No documents indexed. Run `python -m realtest_mcp ingest` first."

## Configuration

`config.toml` next to the package:

```toml
[realtest]
pdf_path = "C:\\RealTest\\RealTest User Guide.pdf"

[scripts]
examples = "C:\\RealTest\\Scripts\\Examples"
user_scripts = [
    "C:\\Users\\craig\\Documents\\path\\to\\scripts",
]

[database]
path = "%LOCALAPPDATA%\\RealTestMcp\\chromadb"
```

Environment variable overrides using `REALTEST_MCP_` prefix with underscored path mapping:
- `REALTEST_MCP_PDF_PATH` → `realtest.pdf_path`
- `REALTEST_MCP_EXAMPLES` → `scripts.examples`
- `REALTEST_MCP_DB_PATH` → `database.path`

`%VAR%` expansion in all path values.

**Config file lookup order:**
1. Path specified by `REALTEST_MCP_CONFIG` environment variable
2. `config.toml` in current working directory
3. `config.toml` next to `__main__.py` (the package directory)

---

## Ingestion Pipeline

### PDF Parsing Strategy

The PDF is parsed as a **continuous text stream**, not page-by-page. Multiple elements share pages, so page boundaries do not correspond to content boundaries.

1. PyMuPDF extracts text from all pages into a single concatenated string
2. The TOC (749 bookmarked entries) provides the title patterns to split on
3. Splitting uses regex matches on TOC title strings found in the text stream:
   - Pattern: `^N.N.N. Title` anchored to line start (avoids false matches in "See also" references)
   - Each TOC title match marks the **start** of a new chunk, ending the previous chunk
   - Only **leaf-level** TOC entries produce chunks (parent entries that have children are not chunked separately — their content is captured by their children)
   - The text between two consecutive leaf-level title matches becomes the chunk content
4. Each split produces one content block with known boundaries

### Content Formatting

**All stored content is cleaned and formatted as markdown at ingest time. Raw PDF text is never stored directly.**

Conversion rules:
- `·` bullet characters become `- ` markdown lists
- Numbered sequences become `1.` ordered lists
- Section titles become `##` headers
- Code examples get fenced code blocks with `rts` language tag. Initial detection heuristics:
  - Lines containing RealScript section keywords (`EntrySetup:`, `ExitRule:`, `Data:`, `Strategy:`, etc.)
  - Lines containing formula syntax (`:=`, function calls like `MA(C,20)`, operators like `and`/`or` with identifiers)
  - Indented blocks following phrases like "for example:", "like this:", "as shown:", "as follows:"
  - These heuristics will require iterative tuning against actual PDF content during implementation
- Paragraphs get proper blank-line separation
- Bold field labels in element details use `**Label:**` format

### Content Types

#### 1. Element Details (section 17.18.*)

- **Count:** 583 TOC entries
- **Split on:** `17.18.N. Title` patterns in text stream
- **Field parsing:** Extract whatever fields are present from each element's text block. Known field labels: Category, Description, Syntax, Parameters, Input, Choices, Default, Notes, Example, Return Value, See also. Not all fields are present for every element — include only what exists.
- **Formatted as clean markdown:**

```markdown
## Commission
**Category:** Strategy Elements
**Description:** Commission amount, in instrument or account currency units, for each trade
**Input:** Formula specifying a commission amount
**Notes:**
If your broker charges no commissions, omit this formula or set it to 0.
Commission is calculated and charged separately for entry and exit transactions...
```

- **Metadata stored in ChromaDB:**
  - `chunk_type: "element_detail"`
  - `element_name`: parsed from title, **lowercased** for consistent matching (e.g., "commission")
  - `element_name_display`: original case from title (e.g., "Commission") — used for display
  - `category`: from the Category field (e.g., "Strategy Elements")
  - `summary`: concise one-line description from 17.17 category listing (joined by **lowercased element name match**; aliases get the summary from whichever form appears in 17.17; elements with no 17.17 match get `summary: null`)
  - `section_number`: "17.18.103"
  - `source: "pdf"`

- **Alias handling:** Elements with titles like "EMA or XAvg" or "Highest or HHV" produce multiple ChromaDB entries — same content, different `element_name` values. Split on ` or ` in the title. This pattern has been validated against the actual PDF TOC entries — the ` or ` convention is used consistently by the author for function/value aliases only, not as English prose in titles.

#### 2. Category Summaries (section 17.17.*)

- **Count:** 16 category listing sections
- **Purpose:** Provide concise `name - description` pairs for `list_elements` tool
- **Parsed into structured lists** of (element_name, short_description) pairs
- **Not stored as separate chunks** — the short descriptions are attached as `summary` metadata on the corresponding element detail chunks
- **Categories (from PDF, used as-is):**
  1. Script Sections
  2. Settings
  3. Import Specification
  4. Strategy Elements
  5. Bar Data Values
  6. Indicator Functions
  7. Multi-Bar Functions
  8. Cross-Sectional Functions
  9. General-Purpose Functions
  10. String Functions
  11. Stock/Contract Information
  12. Current Position Information
  13. Current Strategy Information
  14. Test Statistics Arrays
  15. Trade Record Values
  16. Trade Statistics Functions

#### 3. Narrative Sections

- **Source sections included:**
  - 11 — Using an Imported Trade List (trade list workflow)
  - 15 — Trading Your System (order generation, OrderClerk)
  - 16 — Backtest Engine Details (fill logic, compounding, scaling, futures, multi-currency)
  - 17.1-17.16 — Script language structure, syntax, formula evaluation, section definitions
- **Source sections excluded:**
  - 1-7 — Setup, UI, tutorials (not relevant to script generation)
  - 8-10 — Operational (import/run mechanics)
  - 12-14 — Command line, multiple instances, analyzing results
- **Granularity:** Leaf-level chunks. Each deepest TOC entry is its own chunk. Parent section title stored in metadata so `get_section` can reassemble children.
- **Metadata:**
  - `chunk_type: "narrative"`
  - `section_title`: e.g., "Scan and TestScan Sections"
  - `section_number`: "17.15.4"
  - `parent_section`: e.g., "Script Sections" (for child assembly)
  - `is_primer: true/false` — tagged for `get_primer` assembly. **The specific list of primer chunks is a planning deliverable** — during the planning phase, a dedicated pass over the full PDF TOC will identify which narrative sections teach the core mental model (script structure, evaluation semantics, formula syntax, key concepts). This is intentionally deferred from the spec to allow thorough curation rather than guesswork.
  - `primer_order`: integer for assembly ordering (only set when `is_primer: true`)
  - `source: "pdf"`

#### 4. Scripts (.rts files)

- **Source:** Example scripts directory + user script paths from config
- **One chunk per file**, entire content wrapped in a fenced code block
- **Metadata:**
  - `chunk_type: "script"`
  - `source_type: "example"` or `"user_script"`
  - `filename`: script name (e.g., "clenow_stocks_on_move.rts")
  - `file_path`: full path

---

## MCP Tool API

### 1. `get_primer()`

**Description:** Load the RealScript mental model — script structure, evaluation semantics, formula syntax, and key concepts. Call this once at the start of any scripting session.

**Parameters:** None

**Implementation:** Metadata query for all narrative chunks where `is_primer: true`. Assembled in a predefined logical order. Returns concatenated markdown.

**Returns:** ~2000-3000 tokens of core language knowledge.

### 2. `get_reference(name)`

**Description:** Get the exact reference documentation for a RealScript element. Call this before using any element in generated code.

**Parameters:**
- `name` (string, required) — Element name (e.g., "Commission", "ATR", "#Rank", "T.DateIn")

**Implementation:**
1. Exact match: ChromaDB `where` filter on `element_name` metadata (lowercased at ingest time, query input lowercased at search time — no case-sensitivity issue)
2. Fallback: Semantic search filtered to `chunk_type: "element_detail"`, return top result with a note that it wasn't an exact match

**Returns:** Formatted markdown with all available fields (Category, Description, Syntax, Parameters, Notes, Example, etc.)

### 3. `get_section(title)`

**Description:** Fetch a specific documentation section by title. Use for deep dives into topics like "Scan and TestScan Sections", "Operators", "Charts Section".

**Parameters:**
- `title` (string, required) — Section title or partial match

**Implementation:**
1. Metadata query matching on `section_title` (case-insensitive substring match) or `section_number` (dot-boundary prefix match — "17.15" matches "17.15", "17.15.1", "17.15.2.1" etc., but NOT "17.150" or "17.1". The match requires the prefix to end at a dot boundary or exact end of string.)
2. If the matched section has children (leaf chunks sharing the same `parent_section`), return all children concatenated in `section_number` sort order
3. Fallback: Semantic search filtered to `chunk_type: "narrative"`

**Returns:** Complete section content as markdown.

### 4. `list_elements(category?)`

**Description:** Discover what RealScript elements exist. Call with a category to see all elements in that category, or without to see all categories.

**Parameters:**
- `category` (string, optional) — Category name (e.g., "Strategy Elements", "Indicator Functions")

**Implementation:**
- If category provided: Metadata query for all element details with that category (case-insensitive exact match), return sorted list of `element_name_display: summary` pairs
- If no category: Return all 16 category names with element counts

**Returns:** Sorted list of element names with one-line descriptions, or category listing.

### 5. `search_docs(query, category?, top_k?)`

**Description:** Search RealTest documentation by concept or topic. Use when you don't know the exact element name or section title.

**Parameters:**
- `query` (string, required) — What to search for
- `category` (string, optional) — Filter to a specific element category
- `top_k` (int, optional, default 5) — Number of results

**Implementation:** Semantic search across doc chunks only (element details + narrative), excluding script chunks. Implicit filter: `source: "pdf"`. Optional category metadata filter.

**Returns:** Top results with chunk type, title/name, and content.

### 6. `search_scripts(query, source?, top_k?)`

**Description:** Find example RealScript files demonstrating a concept or technique.

**Parameters:**
- `query` (string, required) — What to search for
- `source` (string, optional) — `"example"`, `"user"`, or `"all"` (default: `"all"`). Note: `"user"` maps to `source_type: "user_script"` in metadata.
- `top_k` (int, optional, default 3) — Number of results

**Implementation:** Semantic search across script chunks. Optional source_type metadata filter. **Keyword fallback:** if semantic search returns zero results, split query into individual words and search for each as a keyword in chunk content (script code is semantically distant from natural language queries in the embedding model).

**Returns:** Matching scripts with filename and content. **Content truncation:** script content is truncated to 3000 characters in search results with a note indicating truncation. Full content is never suppressed — the truncation just keeps search result listings manageable. (Individual script retrieval via exact filename match could be added later if needed.)

---

## Project Structure

```
realtest_mcp/
├── pyproject.toml              # Package metadata, dependencies
├── config.toml                 # User configuration (paths)
├── README.md
│
├── src/realtest_mcp/
│   ├── __init__.py
│   ├── __main__.py             # CLI entry point (serve/ingest/status)
│   ├── config.py               # Load config.toml + env overrides
│   │
│   ├── ingestion/
│   │   ├── __init__.py
│   │   ├── pdf_parser.py       # PyMuPDF: PDF → continuous text stream + TOC
│   │   ├── chunker.py          # Split stream into element details + narratives
│   │   ├── script_parser.py    # Read .rts files
│   │   └── ingest.py           # Orchestrator: parse → chunk → format → store
│   │
│   ├── store/
│   │   ├── __init__.py
│   │   └── vector_store.py     # ChromaDB wrapper (collections, queries)
│   │
│   └── server/
│       ├── __init__.py
│       ├── mcp_server.py       # MCP server setup + tool registration
│       └── tools.py            # 6 tool implementations
│
└── tests/
    ├── conftest.py             # Shared fixtures
    ├── test_pdf_parser.py
    ├── test_chunker.py
    ├── test_script_parser.py
    ├── test_vector_store.py
    ├── test_tools.py
    └── test_integration.py
```

## Dependencies

```toml
[project]
dependencies = [
    "mcp",
    "chromadb",
    "sentence-transformers",
    "pymupdf",
]
```

No other runtime dependencies. `tomllib` is stdlib (Python 3.11+). Requires Python 3.11+.

## ChromaDB Collection Strategy

All chunk types are stored in a **single collection** called `"realtest_docs"`. This keeps the implementation simple and allows `search_docs` to search across element details and narrative sections in one query. Chunk type differentiation is handled via metadata filters.

The embedding function is configured on the collection using ChromaDB's `SentenceTransformerEmbeddingFunction` with model `"BAAI/bge-base-en-v1.5"`. ChromaDB handles embedding generation internally for both ingestion and queries.

## Ingestion Strategy

Ingestion is **wipe-and-rebuild**: each `ingest` run deletes all existing data in the collection and re-ingests from scratch. This ensures consistency — no stale chunks from previous ingestion runs with different parsing logic. At ~700 chunks with a local embedding model, full re-ingestion completes in a reasonable time.

A **last ingest timestamp** is stored as a metadata-only document in the collection with `chunk_type: "ingest_meta"` and `ingest_time` metadata field (ISO 8601 string). The `status` command reads this document. This document is excluded from all search queries via chunk_type filters.

## Error Handling

**Ingestion:** Continue past individual failures with warnings. If a single element detail fails to parse, or a single .rts file can't be read (e.g., encoding issues), log a warning and continue. The ingest command reports a summary at the end: `"Ingested 581/583 element details, 95/97 scripts. 4 warnings — see above."`

**Runtime (MCP tools):** If ChromaDB is not populated (no collection or zero chunks), tools return a clear error message: `"No documents indexed. Run 'python -m realtest_mcp ingest' first."` If a specific query returns no results, return an empty result set with a note, not an error.

**PDF not found:** The ingest command fails immediately with a clear message if the configured PDF path doesn't exist.

## Status Command

`python -m realtest_mcp status` outputs:

```
RealTest MCP Status
  Database: C:\Users\craig\AppData\Local\RealTestMcp\chromadb
  Collection: realtest_docs
  Element details: 583
  Narrative sections: 47
  Scripts (example): 97
  Scripts (user): 10
  Embedding model: BAAI/bge-base-en-v1.5
  Last ingest: 2026-03-25 14:30:00 UTC
```

## Skill Updates

The existing skill files in `skills/` reference old tool names (`get_language_guide`, `get_function_reference`, `search_examples`, `search_user_scripts`). These must be updated:

| Old tool | New tool |
|----------|----------|
| `get_language_guide()` | `get_primer()` |
| `get_function_reference(name)` | `get_reference(name)` |
| `search_examples(query)` | `search_scripts(query, source="example")` |
| `search_user_scripts(query)` | `search_scripts(query, source="user")` |

New tools `get_section`, `list_elements` should be added to skill workflows where appropriate.

---

## What Gets Retired

- `src/RealTestMcp/` — entire .NET implementation
- `native/vec0.dll` — SQLite vector extension
- `RealTestMcp.sln`, `.csproj` files
- `tests/RealTestMcp.Tests/` — .NET test project
- `docs/realscript-language-guide.md` — replaced by PDF-sourced `get_primer` tool

## What Gets Kept

- `skills/` — Claude Code skill definitions (updated to reference new tool names)
- `CLAUDE.md` — updated for new project structure
- `GAP_REPORT.md` — reference for validation
- `docs/superpowers/` — existing specs and plans (historical)

## Validation

After implementation, re-run the gap analysis (same approach as the original 112-script scan). The new MCP should resolve all items in the "Documented in Function Reference but Missing from Language Guide" category and most items in the "Completely Undocumented" category (those that exist in the PDF but were not in the CHM HTML).

Items that remain as gaps after validation represent content missing from the PDF itself — those would need to be reported upstream to the RealTest author.
