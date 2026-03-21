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
| Embeddings | `all-MiniLM-L6-v2` via `Microsoft.ML.OnnxRuntime` (local, cached) |
| HTML parsing | `HtmlAgilityPack` |
| Testing | xUnit |
| CI | GitHub Actions |

**Key decisions:**
- **No Semantic Kernel**: adds abstraction over a backend that will never be swapped. Raw `sqlite-vec` with a thin service class (~150-200 lines) is simpler and more maintainable.
- **Local ONNX embeddings**: no paid services, no external dependencies at runtime. Model downloaded on first run, cached in user data directory.
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
│       ├── Parsers/
│       ├── Chunkers/
│       ├── Integration/
│       └── data/                       # Sample files for tests
│           ├── docs/                   # Sample CHM HTML pages
│           └── scripts/                # Sample .rts files
├── skills/
│   ├── realscript-authoring/SKILL.md
│   ├── realscript-debugging/SKILL.md
│   └── strategy-design/SKILL.md
├── .github/
│   └── workflows/
│       └── ci.yml                      # Build + test on every push
├── appsettings.json
├── CLAUDE.md
├── README.md
└── RealTestMcp.csproj
```

---

## Configuration

`appsettings.json` next to the binary, with environment variable overrides:

```json
{
  "Database": {
    "Path": "C:\\Users\\{user}\\.realtest-mcp\\realtest.db"
  },
  "RealTest": {
    "InstallPath": "C:\\RealTest",
    "DocsPath": "C:\\RealTest\\Help",
    "ScriptPaths": [
      "C:\\RealTest\\Scripts\\Examples",
      "C:\\Users\\{user}\\Documents\\MyScripts"
    ]
  },
  "Embeddings": {
    "ModelCachePath": "C:\\Users\\{user}\\.realtest-mcp\\models"
  }
}
```

- All paths have sensible defaults
- `ScriptPaths` is an array — users add their own script directories freely
- DB and model cache default to a well-known user data directory so they persist across reinstalls

---

## Ingestion Pipeline

### Per-Source Flow

```
Source Files → Parser → Chunker → EmbeddingService → VectorStoreService (delete-scope → upsert)
```

### Idempotent Replace Semantics

Each ingestion command is a **destructive replace** within its scope:

- `ingest docs` deletes all `source_type=docs` chunks, then re-inserts
- `ingest scripts` deletes all chunks whose `source_path` falls under a configured script directory, then re-inserts

Users upgrading RealTest re-run `ingest docs` and `ingest scripts` — old content is replaced, nothing else is affected.

Chunk IDs are deterministic: `hash(source_path + chunk_index)`. This keeps upsert logic simple and avoids orphans.

### Chunking Strategy

| Source | Strategy | Metadata |
|---|---|---|
| CHM docs | Page-level by default; sub-page if a page contains multiple distinct function entries (determined by inspecting actual CHM structure) | `source_type=docs`, `page_path`, `section` |
| Example scripts | One chunk per `.rts` file | `source_type=example`, `source_path`, `category`, `description` |
| User scripts | One chunk per `.rts` file | `source_type=user_script`, `source_path` |

The CHM example index page is parsed first to extract script name → category/description mappings, attached as metadata when ingesting `.rts` files.

### Embedding Model

`all-MiniLM-L6-v2` (ONNX format). Downloaded from HuggingFace on first run, cached in user data directory. Not bundled in the repo (too large). Subsequent runs load from cache.

---

## MCP Tools

### `search_docs`
Semantic search over CHM documentation. Used for concept lookup, section behavior, and general "how does X work" queries.

| Parameter | Type | Description |
|---|---|---|
| `query` | string | What to search for |
| `section_filter` | string? | Limit to a doc section (e.g. "Strategy", "Import") |
| `top_k` | int | Default: 5 |

### `get_function_reference`
Targeted lookup for a specific RealScript function. **Primary tool for syntax correctness.** Claude calls this before using any function in generated code.

| Parameter | Type | Description |
|---|---|---|
| `function_name` | string | Exact or partial name (e.g. "ATR", "Lowest") |

Uses keyword matching against function-level chunks first, falls back to semantic search. Returns canonical signature, parameters, and description from the docs.

### `search_examples`
Semantic search over built-in `.rts` example files.

| Parameter | Type | Description |
|---|---|---|
| `query` | string | What to search for |
| `category_filter` | string? | e.g. "Mean Reversion", "Futures" |
| `top_k` | int | Default: 3 |

### `search_user_scripts`
Semantic search over user-provided scripts from additional configured paths. Separate from built-in examples so Claude can distinguish authoritative examples from user code.

| Parameter | Type | Description |
|---|---|---|
| `query` | string | What to search for |
| `top_k` | int | Default: 3 |

---

## Skills (SKILL.md Files)

Skills are Claude Code workflow files that enforce correct tool usage. Without them, Claude may still generate RealScript from stale training data. Skills make MCP tool consultation a hard gate before code generation.

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
- **Embedding failures**: fatal. Clear error message explaining how to resolve (re-download model, check path).
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
- `get_function_reference` returns correct signature for a known function in sample docs
- `search_examples` returns correct example for a known query

Sample files:
- A few CHM HTML pages (covering single-function and multi-function layouts)
- A handful of `.rts` example scripts

### CI Pipeline
GitHub Actions: build + all tests on every push to `main`.

---

## Vertical Slice Build Order

Each slice leaves the system in a working, buildable state:

| Slice | Deliverable | What Works |
|---|---|---|
| 1 | Skeleton | Single binary, CLI routing, `status` command prints "DB not initialized". MCP server responds to `initialize` handshake. Claude Code can connect. |
| 2 | Storage | SQLite DB created, `sqlite-vec` loaded, schema defined. `status` shows real DB stats (all zeros). |
| 3 | Embeddings | ONNX model download + cache. `EmbeddingService` works. Can embed a string from the CLI. |
| 4 | Docs ingestion | CHM parser + chunker + `ingest docs` command. `status` shows doc chunk counts. |
| 5 | Docs search | `search_docs` and `get_function_reference` tools working. **First useful version.** |
| 6 | Scripts ingestion | RTS parser + `ingest scripts` with multi-path support. |
| 7 | Scripts search | `search_examples` and `search_user_scripts` tools working. **Complete v1.** |
| 8 | Skills | `realscript-authoring`, `realscript-debugging`, and `strategy-design` SKILL.md files written and validated. |

---

## Open Items

1. **CHM file inspection** — actual CHM structure determines final chunking strategy for docs (page-level vs. sub-page). Defer to Slice 4.
2. **`all-MiniLM-L6-v2` ONNX download approach** — confirm NuGet package or manual download for model acquisition.
3. **Marsten's explicit go-ahead on public repo** — implicit from the project brief; worth confirming before publishing.
4. **Forum backup file** — needed for v2. Not required for v1.

---

## Future Work (v2)

- Forum content ingestion with PII sanitization pipeline and sanitization test suite
- YouTube video transcript ingestion with timestamped deep-links
