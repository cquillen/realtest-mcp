# RealTest MCP -- Architecture Overview

## System Overview

RealTest MCP is a Python MCP (Model Context Protocol) server that gives LLM
agents structured access to RealTest backtesting documentation and example
scripts. It works in two phases:

1. **Ingest** -- an offline pipeline extracts the RealTest User Guide PDF and
   `.rts` script files, parses them into typed chunks, embeds them, and stores
   everything in a local ChromaDB vector database.
2. **Serve** -- a FastMCP server exposes six tools over StdIO that let an LLM
   look up elements, search documentation, and find example scripts.

```
 +-----------+      +------------------+      +----------+
 |  RealTest |      |   Ingestion      |      | ChromaDB |
 |  User     | ---> |   Pipeline       | ---> | (local   |
 |  Guide    |      |  (offline)       |      |  persist)|
 |  (PDF)    |      +------------------+      +----+-----+
 +-----------+                                     |
                                                   |  read
 +-----------+      +------------------+           |
 |  .rts     | ---> |   Script Parser  | ----------+
 |  Scripts  |      +------------------+           |
 +-----------+                                     v
                    +------------------+      +----------+
                    |  MCP Client      | <--> | FastMCP  |
                    |  (Claude, etc.)  |      | Server   |
                    +------------------+      +----------+
                          StdIO
```

## Components

### CLI Entry Point

**`src/realtest_mcp/__main__.py`**

Three subcommands:

| Command   | Action                                      |
|-----------|---------------------------------------------|
| `serve`   | Start the MCP server over StdIO              |
| `ingest`  | Run the full ingestion pipeline               |
| `status`  | Print database stats (chunk counts, timestamps)|

Invoked as `python -m realtest_mcp <command>`.

### Configuration

**`src/realtest_mcp/config.py`**

A `Config` dataclass loaded from `config.toml`. Fields:

- `pdf_path` -- path to the RealTest User Guide PDF
- `examples_path` -- directory of bundled `.rts` example scripts
- `user_script_paths` -- list of additional user script directories
- `db_path` -- ChromaDB persistence directory

Config file resolution order:
1. `REALTEST_MCP_CONFIG` environment variable
2. `./config.toml` (current working directory)
3. `config.toml` next to the installed package

All path values support `%VAR%`-style environment variable expansion. Individual
fields can be overridden via `REALTEST_MCP_PDF_PATH`, `REALTEST_MCP_EXAMPLES`,
and `REALTEST_MCP_DB_PATH`.

### MCP Server

**`src/realtest_mcp/server/mcp_server.py`**

Thin bootstrap: loads config, creates a `VectorStore`, registers tools, and
starts `FastMCP` on the StdIO transport. Configured in `.mcp.json` for
MCP-aware clients.

**`src/realtest_mcp/server/tools.py`**

Six registered tools:

| Tool              | Purpose                                                    |
|-------------------|------------------------------------------------------------|
| `get_primer()`    | Return the static primer document (`data/primer.md`)       |
| `get_reference()` | Exact element lookup with alias resolution and fallbacks   |
| `get_section()`   | Fetch narrative sections by title or section number        |
| `list_elements()` | List element categories, or elements within a category     |
| `search_docs()`   | Semantic search over PDF-sourced documentation chunks      |
| `search_scripts()`| Semantic + keyword search over indexed `.rts` scripts      |

`get_reference()` implements a multi-step resolution chain:
1. Path tokens (e.g. `ScriptPath`) redirect to the "File Path Syntax" section
2. Operators redirect to the "Operators" section
3. Syntax tokens (e.g. `[]`, `$`) redirect to specific narrative sections
4. Exact match by `element_name` metadata, including disambiguated names
5. Alias resolution (e.g. `InSPX` -> `InXXX`, `nan` -> `IsNaN`, `Opt*` -> `OptimizeSettings`)
6. Enum/choice value search (scans Choices fields in element docs)
7. Semantic fallback (vector similarity on element_detail chunks)

### Vector Store

**`src/realtest_mcp/store/vector_store.py`**

Wraps ChromaDB with a single collection (`realtest_docs`) and the
`BAAI/bge-base-en-v1.5` embedding model via `sentence-transformers`.

Key capabilities:
- **Add/wipe** -- bulk insert and full collection reset for re-ingestion
- **Exact lookup** -- `get_by_element_name()` with alias and enum resolution
- **Semantic search** -- `search()`, `search_docs()`, `search_scripts()` with
  optional category and chunk-type filters
- **Keyword fallback** -- `keyword_search_scripts()` scans script content for
  literal keyword matches when semantic search returns nothing
- **Section retrieval** -- `get_section()` filters narrative chunks by title
  substring or section number prefix
- **Status** -- chunk counts by type, ingest timestamp

### Ingestion Pipeline

**`src/realtest_mcp/ingestion/ingest.py`** -- orchestrator

The pipeline runs four sequential phases, each producing chunks that are
inserted into ChromaDB:

```
Phase 1: Category Summaries (sections 17.17.*)
  pdf_parser -> chunker -> category_parser
  Output: {element_name: summary} lookup table (not stored directly)

Phase 2: Element Details (sections 17.18.*)
  pdf_parser -> chunker -> element_parser -> markdown_formatter -> store
  Output: element_detail chunks with category, syntax, description, etc.

Phase 3: Narrative Sections (all other sections)
  pdf_parser -> chunker -> markdown_formatter -> store
  Output: narrative chunks with section titles, parent sections, primer flags

Phase 4: Scripts
  script_parser -> store
  Output: script chunks from example and user directories
```

The pipeline wipes the database on each run (full rebuild). A SHA-256 hash of
`source:name` generates deterministic chunk IDs.

Specific narrative sections are flagged as "primer" content via a hardcoded map
(`PRIMER_SECTIONS`) to support the `get_primer_chunks()` store method.

#### Ingestion Submodules

| Module                | Role                                                       |
|-----------------------|------------------------------------------------------------|
| `pdf_parser.py`       | PyMuPDF-based text and TOC extraction from the PDF         |
| `chunker.py`          | Splits continuous text at TOC title boundaries into `RawChunk` objects; tracks parent-child section hierarchy |
| `element_parser.py`   | Parses structured fields (Category, Syntax, Choices, etc.) from element detail text; handles `Name or Alias` splitting |
| `category_parser.py`  | Parses `Name - description` bullet lists from 17.17.* sections into a summary lookup |
| `markdown_formatter.py`| Cleans PDF artifacts (page numbers, bullets, numbered lists) and auto-fences RealScript code blocks |
| `script_parser.py`    | Reads `.rts` files from disk, wraps content with filename heading and code fence |

### Static Data

**`src/realtest_mcp/data/primer.md`**

A curated markdown document covering the RealScript mental model, served
directly by the `get_primer()` tool. This is a static file, not generated from
the vector store.

## Chunk Types in the Database

All chunks live in a single ChromaDB collection. The `chunk_type` metadata field
discriminates them:

| chunk_type       | Source    | Content                                    |
|------------------|-----------|--------------------------------------------|
| `element_detail` | PDF       | Structured element reference (one per alias)|
| `narrative`      | PDF       | Prose documentation sections               |
| `script`         | .rts files| Full script content with filename heading  |
| `ingest_meta`    | system    | Ingest timestamp (single sentinel record)  |

## Tech Stack

| Layer         | Technology                         |
|---------------|------------------------------------|
| Language      | Python >= 3.11                     |
| MCP framework | `mcp` (FastMCP, StdIO transport)   |
| Vector DB     | ChromaDB (persistent local client) |
| Embeddings    | sentence-transformers (`BAAI/bge-base-en-v1.5`) |
| PDF parsing   | PyMuPDF (`fitz`)                   |
| Build         | setuptools                         |
| Testing       | pytest, pytest-asyncio             |

## File Layout

```
realtest-mcp/
  .mcp.json                          # MCP client configuration
  config.toml                        # Runtime paths (PDF, scripts, DB)
  pyproject.toml                     # Package metadata and dependencies
  src/realtest_mcp/
    __main__.py                      # CLI: serve | ingest | status
    config.py                        # Config loading with env var support
    data/
      primer.md                      # Static RealScript primer document
    ingestion/
      ingest.py                      # Pipeline orchestrator (4 phases)
      pdf_parser.py                  # PDF text + TOC extraction
      chunker.py                     # TOC-based text splitting
      element_parser.py              # Structured element field parsing
      category_parser.py             # Category summary list parsing
      markdown_formatter.py          # PDF text cleanup and code fencing
      script_parser.py               # .rts file reader
    server/
      mcp_server.py                  # FastMCP bootstrap
      tools.py                       # Tool definitions (6 tools)
    store/
      vector_store.py                # ChromaDB wrapper
```

## Data Sources

The system ingests two kinds of source material:

1. **RealTest User Guide PDF** -- the canonical documentation. The PDF's
   embedded table of contents drives section splitting. Sections under 17.17
   provide category summaries; sections under 17.18 provide per-element
   reference details; all other sections become narrative chunks.

2. **RealScript files (`.rts`)** -- example scripts shipped with RealTest and
   optionally user-authored scripts from configured directories. Each file
   becomes a single chunk.
