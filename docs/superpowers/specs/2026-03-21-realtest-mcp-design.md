# RealTest MCP Server — Design Spec
**Date:** 2026-03-21
**Status:** Approved

---

## Problem Statement

RealTest is a niche backtesting platform using a proprietary scripting language called RealScript. LLM training data for RealScript is incomplete and partially stale — Claude generates syntactically incorrect RealScript when working from memory alone. This MCP server provides authoritative, searchable access to RealTest documentation and example scripts at query time, correcting hallucinations at the source.

---

## Goals

- Enable Claude Code to generate syntactically correct RealScript code
- Serve as a source of truth for RealTest API function signatures
- Be fully self-contained: no API keys, no Docker, no external services
- Be easy for end users to set up and update when RealTest releases new versions
- Be distributable as open source (code only — no proprietary data in the repo)

## Non-Goals (v1)

- Forum content ingestion (v2)
- YouTube video transcript ingestion (v2)
- Pre-built database distribution

---

## Tech Stack

| Component | Technology |
|---|---|
| Language | C# / .NET 10 |
| MCP SDK | `ModelContextProtocol` NuGet package |
| CLI routing | `System.CommandLine` |
| Vector store | SQLite + `sqlite-vec` (direct, no abstraction layer) |
| Embeddings | `all-MiniLM-L6-v2` via `SmartComponents.LocalEmbeddings` (bundled in NuGet, no download) |
| HTML parsing | `HtmlAgilityPack` |
| Testing | xUnit |
| CI | GitHub Actions |

**Key decisions:**
- **No Semantic Kernel**: adds abstraction over a backend that will never be swapped. Raw `sqlite-vec` with a thin service class (~150-200 lines) is simpler and more maintainable.
- **`SmartComponents.LocalEmbeddings` over raw ONNX Runtime**: bundles `all-MiniLM-L6-v2` directly in the NuGet package — no model download, no file management, no checksums. Add one package reference and call `EmbedAsync()`. Microsoft-maintained.
- **Single binary**: server and ingestion CLI in one executable, routed by `System.CommandLine`. Simpler for users and for development.

---

## Architecture

### Single Binary, Two Modes

```
realtest-mcp                          → MCP server mode (stdio, managed by Claude Code)
realtest-mcp ingest docs              → Ingest CHM documentation
realtest-mcp ingest scripts           → Ingest .rts example and user scripts
realtest-mcp ingest forum <path>      → Ingest forum backup (v2)
realtest-mcp status                   → Show DB stats (chunk counts by source type, DB size)
```

### Internal Layers

```
┌─────────────────────────────────────────┐
│         Entry Point / CLI Router         │  System.CommandLine
├───────────────────┬─────────────────────┤
│   MCP Server      │  Ingestion Commands  │
│   (Tools)         │  (Parsers/Chunkers)  │
├───────────────────┴─────────────────────┤
│              Core Services               │
│   EmbeddingService │ VectorStoreService  │
├──────────────────────────────────────────┤
│           SQLite + sqlite-vec            │
└──────────────────────────────────────────┘
```

All layers live in a **single C# project**. Internal namespaces (`RealTestMcp.Tools`, `RealTestMcp.Ingestion`, `RealTestMcp.Core`) provide logical separation without multi-project overhead.

### Project Structure

```
RealTestMcp/
├── src/
│   └── RealTestMcp/
│       ├── RealTestMcp.csproj          # Project file lives with its source
│       ├── Program.cs                  # Entry point, CLI routing
│       ├── Tools/
│       │   ├── SearchDocsTool.cs
│       │   ├── GetFunctionReferenceTool.cs
│       │   ├── SearchExamplesTool.cs
│       │   └── SearchUserScriptsTool.cs
│       ├── Ingestion/
│       │   ├── Commands/
│       │   │   ├── IngestDocsCommand.cs
│       │   │   └── IngestScriptsCommand.cs
│       │   ├── Parsers/
│       │   │   ├── ChmParser.cs
│       │   │   └── RtsParser.cs
│       │   └── Chunkers/
│       │       ├── DocChunker.cs
│       │       └── ScriptChunker.cs
│       └── Core/
│           ├── EmbeddingService.cs
│           ├── VectorStoreService.cs
│           ├── Models/
│           │   ├── Chunk.cs
│           │   └── SearchResult.cs
│           └── Configuration/
│               └── AppSettings.cs
├── tests/
│   └── RealTestMcp.Tests/
│       ├── RealTestMcp.Tests.csproj
│       ├── Parsers/
│       ├── Chunkers/
│       ├── Integration/
│       └── data/                       # Sample files committed to repo
│           ├── docs/                   # Sample CHM HTML pages
│           └── scripts/                # Sample .rts files
├── skills/
│   ├── realscript-authoring/SKILL.md
│   ├── realscript-debugging/SKILL.md
│   └── strategy-design/SKILL.md
├── .github/
│   └── workflows/
│       └── ci.yml                      # Build + all tests on every push
├── .claude/
│   └── settings.json                   # Registers skills with Claude Code
├── appsettings.json                    # Default config (shipped with binary)
├── CLAUDE.md                           # References skills via @include
├── README.md
└── RealTestMcp.sln                     # Solution file at repo root
```

---

## Configuration

### Resolution Order

1. Built-in defaults (computed at runtime — see below)
2. `appsettings.json` next to the binary
3. Environment variable overrides

Because the MCP server is launched as a subprocess by Claude Code, its working directory is not predictable. `appsettings.json` is resolved relative to the **binary location** (`AppContext.BaseDirectory`), not the working directory.

### Default Path Computation

All user-specific defaults are computed via `Environment.GetFolderPath()`:

```csharp
// DB and model cache default to %LOCALAPPDATA%\RealTestMcp\
var appData = Environment.GetFolderPath(SpecialFolder.LocalApplicationData);
var defaultDbPath     = Path.Combine(appData, "RealTestMcp", "realtest.db");
var defaultModelCache = Path.Combine(appData, "RealTestMcp", "models");

// RealTest install defaults
var defaultInstallPath = @"C:\RealTest";
var defaultDocsPath    = @"C:\RealTest\Help";
var defaultScriptPaths = new[] { @"C:\RealTest\Scripts\Examples" };
```

### Environment Variable Expansion

All string path values read from `appsettings.json` must be passed through `Environment.ExpandEnvironmentVariables()` before use. The .NET JSON configuration provider does NOT expand `%VAR%` syntax automatically. This is applied centrally in `AppSettings` when paths are first accessed, not at each call site.

### Example `appsettings.json`

```json
{
  "Database": {
    "Path": "%LOCALAPPDATA%\\RealTestMcp\\realtest.db"
  },
  "RealTest": {
    "InstallPath": "C:\\RealTest",
    "DocsPath": "C:\\RealTest\\Help",
    "ScriptPaths": [
      "C:\\RealTest\\Scripts\\Examples",
      "C:\\Users\\craig\\Documents\\MyScripts"
    ]
  },
}
}
```

`ScriptPaths` is an array — users add their own script directories freely. All configured paths are processed on `ingest scripts`.

---

## Database Schema

### `chunks` table

Stores all text content with metadata.

```sql
CREATE TABLE IF NOT EXISTS chunks (
    id           TEXT    PRIMARY KEY,  -- SHA256(source_path + ':' + chunk_index)
    source_type  TEXT    NOT NULL,     -- 'docs' | 'example' | 'user_script' | 'forum' (v2)
    source_path  TEXT    NOT NULL,     -- Absolute path or URL of origin file
    chunk_type   TEXT    NOT NULL,     -- 'page' | 'function_entry' | 'script'
    section      TEXT,                 -- Doc section name (e.g. "Strategy", "Import")
    category     TEXT,                 -- Example category from CHM index (e.g. "Mean Reversion")
    description  TEXT,                 -- Example description from CHM index
    content      TEXT    NOT NULL,     -- Full text of the chunk
    chunk_index  INTEGER NOT NULL,     -- Position within source file (0-based)
    created_at   TEXT    NOT NULL      -- ISO 8601 UTC timestamp
);
```

### `chunk_embeddings` virtual table

`sqlite-vec` virtual table holding the embedding vectors, linked to `chunks` by `id`.

```sql
CREATE VIRTUAL TABLE IF NOT EXISTS chunk_embeddings USING vec0(
    chunk_id     TEXT PRIMARY KEY,     -- References chunks.id
    embedding    FLOAT[384]            -- all-MiniLM-L6-v2 output dimension
);
```

### Indexes

```sql
CREATE INDEX IF NOT EXISTS idx_chunks_source_type ON chunks(source_type);
CREATE INDEX IF NOT EXISTS idx_chunks_source_path ON chunks(source_path);
CREATE INDEX IF NOT EXISTS idx_chunks_chunk_type  ON chunks(chunk_type);
```

> **Note (Slice 2):** Validate that `sqlite-vec`'s `vec0` virtual table supports a `TEXT PRIMARY KEY` join column for ANN search. Some `vec0` configurations require an integer rowid for optimal index performance. If TEXT is unsupported, the schema will use an integer `rowid` in `chunk_embeddings` with a separate `chunk_id TEXT` column and index.

---

## Ingestion Pipeline

### Per-Source Flow

```
Source Files → Parser → Chunker → EmbeddingService → VectorStoreService (delete-scope → batch insert)
```

### Idempotent Replace Semantics

Each ingestion command is a **full destructive replace** within its scope:

- `ingest docs`: deletes ALL rows where `source_type = 'docs'`, then re-inserts. Run this after any RealTest version upgrade.
- `ingest scripts`: deletes ALL rows where `source_type IN ('example', 'user_script')`, then re-inserts from all currently configured `ScriptPaths`. This full-replace approach avoids orphan chunks when paths are added or removed from `ScriptPaths` — the current config is always the source of truth.

### Chunking Strategy

| Source | `chunk_type` | Strategy | Metadata |
|---|---|---|---|
| CHM docs | `page` | One chunk per HTML page | `source_type=docs`, `page_path`, `section` |
| CHM docs (function pages) | `function_entry` | Sub-page chunks if a page contains multiple distinct function entries (determined at Slice 4 by inspecting actual CHM structure) | `source_type=docs`, `page_path`, `section` |
| Example scripts | `script` | One chunk per `.rts` file | `source_type=example`, `source_path`, `category`, `description` |
| User scripts | `script` | One chunk per `.rts` file | `source_type=user_script`, `source_path` |

The CHM example index page is parsed first to extract script name → category/description mappings, attached as metadata when ingesting `.rts` files.

### Embedding Model

`all-MiniLM-L6-v2` via `SmartComponents.LocalEmbeddings` NuGet package. The model is bundled inside the package — no download, no file management, no cache directory needed. `EmbeddingService` is a thin wrapper over `LocalEmbedder` from that package. The `ModelCachePath` configuration key is removed as it is no longer needed.

---

## MCP Tools

### Tool Response Format

All tools return plain text formatted for readability in Claude's context. Chunk content is **truncated to 1500 characters** per result to keep context usage predictable across multiple tool calls in a single session. If content is truncated, a `[truncated]` marker is appended.

### `search_docs`
Semantic vector search over CHM documentation. Used for concept lookup, section behavior, and general "how does X work" queries.

| Parameter | Type | Description |
|---|---|---|
| `query` | string | What to search for |
| `section_filter` | string? | Case-insensitive match against `section` metadata (e.g. "Strategy", "Import") |
| `top_k` | int | Default: 5 |

### `get_function_reference`
Targeted lookup for a specific RealScript function. **Primary tool for syntax correctness.** Claude calls this before using any function in generated code.

| Parameter | Type | Description |
|---|---|---|
| `function_name` | string | Exact or partial name (e.g. "ATR", "Lowest") |

**Search strategy (in order):**
1. SQL keyword search: `WHERE chunk_type = 'function_entry' AND content LIKE '%<function_name>%'` (case-insensitive). Returns up to 3 matches.
2. If keyword search returns zero results: fall back to semantic vector search across **all `source_type=docs` chunks** (not just `function_entry`). This is intentional — some functions are documented only in narrative pages rather than dedicated function entries, and a broad semantic fallback ensures they are still findable. Returns top 3.

The keyword-first approach ensures exact function name matches take priority over semantic proximity.

### `search_examples`
Semantic search over built-in `.rts` example files.

| Parameter | Type | Description |
|---|---|---|
| `query` | string | What to search for |
| `category_filter` | string? | Case-insensitive match against `category` metadata. Valid values come from the CHM example index page (e.g. "Mean Reversion", "Futures", "Tutorial Scripts"). |
| `top_k` | int | Default: 3 |

### `search_user_scripts`
Semantic search over user-provided scripts from additional configured paths. Separate from built-in examples so Claude can distinguish authoritative examples from user code.

| Parameter | Type | Description |
|---|---|---|
| `query` | string | What to search for |
| `top_k` | int | Default: 3 |

---

## `status` Command Output

Plain text table, always printed to stdout:

```
RealTest MCP — Database Status
================================
DB path:        C:\Users\craig\AppData\Local\RealTestMcp\realtest.db
DB size:        4.2 MB

Chunk counts:
  docs           312
  example         47
  user_script     23
  ─────────────────
  total          382

Model:          all-MiniLM-L6-v2 (SmartComponents.LocalEmbeddings, bundled)

Last ingest:
  docs        2026-03-21 14:32 UTC
  scripts     2026-03-21 14:35 UTC
```

**Last ingest timestamps** are derived from `SELECT MAX(created_at) FROM chunks WHERE source_type = ?` — one query per source type. No separate metadata table is needed. This approach is accurate as long as ingestion always replaces the full scope (which it does), so `MAX(created_at)` always reflects the most recent full ingest for that type.

If the DB does not exist: print `DB not initialized — run: realtest-mcp ingest docs` and exit.

---

## Skills (SKILL.md Files)

Skills are Claude Code workflow files that enforce correct tool usage. Without them, Claude may still generate RealScript from stale training data. Skills make MCP tool consultation a hard gate before code generation.

### Activation

Skills are registered in `CLAUDE.md` using `@include` directives so they are automatically active in any Claude Code session within this project:

```markdown
@include skills/realscript-authoring/SKILL.md
@include skills/realscript-debugging/SKILL.md
@include skills/strategy-design/SKILL.md
```

### `realscript-authoring`
Workflow for generating RealScript code:
1. Call `get_function_reference` for every function planned
2. Call `search_examples` for similar strategy patterns
3. Compose code following retrieved patterns
4. Validate all function usages against retrieved signatures before presenting output

### `realscript-debugging`
Workflow for debugging broken RealScript:
1. Call `get_function_reference` for each function used in the script
2. Compare usage against canonical signatures
3. Flag discrepancies (wrong parameter order, deprecated syntax, etc.)
4. Call `search_docs` for any error messages encountered

### `strategy-design`
Workflow for translating a trading concept into RealScript:
1. Call `search_examples` for similar approaches
2. Call `search_docs` for required building blocks
3. Scaffold structure before filling in details

---

## Error Handling

- **Ingestion — file errors**: log the problematic file with a clear message and continue. One bad file never aborts the full run.
- **Embedding failures**: fatal. The `SmartComponents.LocalEmbeddings` model is bundled in the NuGet package and requires no download, so failures here indicate a corrupted install. Clear error message directing the user to reinstall.
- **MCP tool errors**: return a structured error string to Claude (e.g., "DB not found — run `realtest-mcp ingest docs` first"). Never throw unhandled exceptions into the stdio stream.
- **Missing DB at server startup**: server starts successfully. Tools return a helpful "DB not initialized" message rather than crashing.
- **Missing configuration**: sensible defaults everywhere. Only fail if a path is explicitly configured but doesn't exist.

---

## Testing

All tests run in CI. No locally-only carve-outs.

### Unit Tests
Pure logic, no file I/O:
- CHM parser: given raw HTML, produces correct chunks with correct metadata
- RTS parser: given `.rts` file string, produces correct chunks
- Chunking: page splitting, sub-page detection, metadata attachment

### Integration Tests
Use sample files committed to `tests/data/`:
- End-to-end ingest → search round trip on sample data
- `get_function_reference` keyword path returns correct signature for a known function
- `get_function_reference` semantic fallback returns a result when no exact match exists
- `search_examples` returns correct example for a known query
- `search_examples` with `category_filter` returns only matching category

Sample files:
- A few CHM HTML pages (covering single-function and multi-function layouts)
- A handful of `.rts` example scripts across at least two categories

### CI Pipeline
GitHub Actions: build + all tests on every push to `main`. Because `SmartComponents.LocalEmbeddings` bundles the model in the NuGet package, **no model download is needed in CI** — integration tests use the real `EmbeddingService` with no mocking required.

---

## Vertical Slice Build Order

Each slice leaves the system in a working, buildable state:

| Slice | Deliverable | What Works |
|---|---|---|
| 1 | Skeleton | Solution + project scaffolded. Single binary, CLI routing, `status` prints "DB not initialized". MCP server responds to `initialize` handshake. Claude Code can connect. CI pipeline green. |
| 2 | Storage | SQLite DB created, `sqlite-vec` extension loaded, schema applied. `status` shows real DB stats (all zeros). |
| 3 | Embeddings | Add `SmartComponents.LocalEmbeddings` NuGet package. `EmbeddingService` wrapper works. Can embed a string from the CLI. |
| 4 | Docs ingestion | CHM parser + chunker + `ingest docs` command. `status` shows doc chunk counts. |
| 5 | Docs search | `search_docs` and `get_function_reference` tools working. **First useful version.** |
| 6 | Scripts ingestion | RTS parser + `ingest scripts` with multi-path support. |
| 7 | Scripts search | `search_examples` and `search_user_scripts` tools working. **Complete v1.** |
| 8 | Skills | `realscript-authoring`, `realscript-debugging`, and `strategy-design` SKILL.md files written, registered in `CLAUDE.md`, and validated. |

---

## Open Items

1. **CHM file inspection** — actual CHM structure determines whether sub-page `function_entry` chunking is needed and how to detect function boundaries. Defer to Slice 4.
2. **Marsten's explicit go-ahead on public repo** — implicit from the project brief; worth confirming before publishing.
4. **Forum backup file** — needed for v2. Not required for v1.

---

## Future Work (v2)

- Forum content ingestion with PII sanitization pipeline and sanitization test suite as a CI gate
- YouTube video transcript ingestion with timestamped deep-links
