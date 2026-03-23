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

**Current status (2026-03-21):** Design spec and implementation plan complete and reviewed. No code written yet. Ready to implement.

**Key files:**
- Spec: `docs/superpowers/specs/2026-03-21-realtest-mcp-design.md`
- Plan: `docs/superpowers/plans/2026-03-21-realtest-mcp.md`

**How to apply:** When the user asks to implement or start building, reference the plan. The plan follows vertical slices (20 tasks) with TDD throughout.
