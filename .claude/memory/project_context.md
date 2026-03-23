---
name: RealTest MCP Project Context
description: Core facts about the realtest-mcp project — what it is, key decisions, and current status
type: project
---

RealTest MCP Server is a single-binary C# / .NET 10 console app that provides semantic search over RealTest backtesting engine documentation and example scripts, enabling Claude Code to generate syntactically correct RealScript code.

**Why:** Claude's training data for RealScript is incomplete and stale — it generates incorrect syntax without an authoritative knowledge source at query time.

**Key decisions:**
- Single binary (dual-mode: no args = MCP server, subcommands = ingestion CLI)
- SQLite + sqlite-vec (direct, no Semantic Kernel abstraction)
- SmartComponents.LocalEmbeddings for all-MiniLM-L6-v2 (bundled in NuGet, no download)
- No pre-built database distribution — users run ingestion locally
- Forum content deferred to v2; v1 = CHM docs + .rts example scripts only

**Current status (2026-03-23):** Doc ingestion & search improvements fully implemented, tested, and verified via live MCP tool tests. All success criteria confirmed.

**Key files:**
- Original design spec: `docs/superpowers/specs/2026-03-21-realtest-mcp-design.md`
- Doc ingestion spec: `docs/superpowers/specs/2026-03-22-doc-ingestion-design.md`

**Completed work:**
- `ChmParser` — classifies pages (Reference/Prose/NavIndex) using CSS classes, extracts Labels + Section breadcrumbs
- `DocChunker` — switches on PageType, emits `chunk_type="reference"` with alias splitting for "X or Y" titles
- `VectorStoreService.SearchByDescriptionAsync` — exact case-insensitive description match
- `GetFunctionReferenceTool` — 4-step cascade: exact description → keyword reference → keyword all → vector
