# RealTest MCP — Roadmap

Items below are proposed enhancements to the RealTest MCP server.

---

## 1. YouTube Transcript Ingestion

**Status:** Proposed

RealTest has tutorial and demo videos that are not currently searchable through
the MCP. Ingesting YouTube transcripts with timestamped deep-links would make
video content discoverable alongside the PDF docs and example scripts.

**Approach:** Add a new ingestion source using `youtube-transcript-api` or
similar (no GPU required). Transcripts would be chunked and indexed into
ChromaDB as a new `chunk_type` (e.g., `video_transcript`), with metadata
carrying video ID, title, and timestamp offsets. The existing `search_docs`
tool would surface video results naturally.

---

## 2. CI/CD and Distribution

**Status:** Proposed

The ingestion pipeline currently runs locally. Automating builds and detecting
upstream doc changes would keep the database current without manual effort, and
publishing pre-built artifacts would let users skip the ingestion step entirely.

**Approach:** GitHub Actions workflows for:
- **Content drift detection** — hash the online docs against a stored manifest;
  trigger re-ingestion when content changes.
- **Automated DB builds** — run the ingestion pipeline in CI and validate the
  resulting ChromaDB.
- **Release artifact publishing** — attach pre-built ChromaDB databases to
  GitHub releases so users can download and use them directly.
- **Sanitization gate** — if forum or user-contributed content is ever added as
  a source, run a sanitization check before publishing.

---

## 3. Script Category Metadata

**Status:** Proposed

The RealTest documentation includes an index page that categorizes example
scripts (Mean Reversion, Trend Following, Futures, etc.). This metadata is not
currently captured during ingestion, so `search_scripts` cannot filter by
trading strategy category.

**Approach:** Parse the script category index page during ingestion and attach
category labels to script chunks in ChromaDB metadata. Expose an optional
`category` filter on `search_scripts`, similar to how `search_docs` already
supports category filtering.

---

## 4. Cross-Project MCP Availability

**Status:** Proposed

The MCP server is currently configured per-project via `.mcp.json`. This means
it is only available inside the RealTest project directory. Users writing
RealScript in other repositories must either copy the config or go without.

**Approach:** Provide a lightweight way to attach the MCP server to any Claude
Code session on demand, without polluting context when not needed. Options
include a `--mcp-config` flag pointing to a shared config file, shell aliases
that inject the server, or documenting the global MCP config path so users can
register it once.
