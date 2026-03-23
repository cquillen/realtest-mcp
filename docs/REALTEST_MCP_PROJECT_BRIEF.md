# RealTest MCP Server — Project Brief

## Project Overview

Build a Model Context Protocol (MCP) server that provides semantic search over RealTest backtesting engine documentation, example scripts, and community forum content. The server enables AI coding assistants (primarily Claude Code) to write accurate RealScript code by having access to authoritative, searchable documentation at query time.

RealTest is a niche backtesting platform created by Marsten Parker. It uses a proprietary scripting language called RealScript. Existing LLM training data for RealScript is incomplete and partially stale, making an MCP-based knowledge retrieval system essential for accurate code generation.

---

## Tech Stack

| Component | Technology | Rationale |
|---|---|---|
| Language | C# / .NET | Owner's primary language (25 years experience), maintainability |
| MCP SDK | `ModelContextProtocol` NuGet package | Official C# SDK, maintained with Microsoft |
| Vector Store | SQLite + sqlite-vec | Fully embedded, single-file DB, zero infrastructure, no Docker |
| Semantic Kernel | `Microsoft.SemanticKernel.Connectors.SqliteVec` | Microsoft-backed SQLite vector connector |
| Embeddings | `all-MiniLM-L6-v2` via `Microsoft.ML.OnnxRuntime` | Local, no API keys, no external dependencies |
| HTML Parsing | `HtmlAgilityPack` | Mature .NET HTML parser for CHM-extracted content |
| PII Detection | `Microsoft.Recognizers.Text` + regex | Pattern-based PII filtering for forum content |
| Transport | stdio | Claude Code auto-manages lifecycle, no server process management |
| Testing | xUnit or NUnit | Sanitization test suite, regression tests |

### Key Stack Decisions

- **SQLite over Qdrant**: Qdrant is technically superior but requires Docker. SQLite + sqlite-vec is fully embedded (in-process), ships as a single `.db` file, and has zero infrastructure requirements. For a corpus this small (thousands of chunks), brute-force KNN with SIMD acceleration returns exact results instantly. The tradeoff of no HNSW indexing is irrelevant at this scale.
- **C# over Python**: Owner's expertise. FastMCP (Python) was considered but C# provides better long-term maintainability for this developer. All required libraries have .NET equivalents.
- **Local embeddings over API**: Supports the self-contained distribution model. No OpenAI API key required. Quality difference is negligible for a small corpus.

---

## Data Sources

### 1. CHM Documentation (Primary Docs Source)
- **Location**: Local RealTest install directory, `.chm` file
- **Format**: Microsoft Compiled HTML Help — a compressed archive of HTML files
- **Extraction**: Decompile with `hh.exe -decompile` (Windows) or `extract_chmLib` (Linux) to get raw HTML
- **Content**: Complete RealTest user guide including scripting reference, UI guide, import configuration, order generation, etc.
- **A PDF version also exists** in the install directory as a fallback/cross-reference
- **Online mirror**: `https://mhptrading.com/docs/topics/idh-topic10.htm` (same content, used for drift detection only)
- **Note**: Ingest ALL content, not just scripting reference. The full user guide provides valuable context. Metadata tagging and search relevance handle prioritization.

### 2. Example Scripts (.rts files)
- **Location**: `C:\RealTest\Scripts\Examples\` in local install
- **Format**: Plain text files with `.rts` extension
- **Index page**: `https://mhptrading.com/docs/topics/idh-topic100.htm` (also in CHM) — contains categorized descriptions of every example script
- **Special handling required**: 
  - Parse the index page to extract script name → description mappings
  - Attach descriptions as metadata to corresponding `.rts` file chunks
  - Preserve category groupings (Import Examples, Scanning Examples, Mean Reversion, Futures, etc.) as metadata tags
  - Cross-link tutorial scripts to their tutorial doc pages (idh-topic90.htm, idh-topic92.htm, etc.)
- **Categories from index**: Tutorial Scripts, Import Examples, Scanning Examples, Indicators and Techniques, Single-Strategy Systems, Multi-Strategy Systems, Mean Reversion Theme and Variations, Futures Examples, Files Used by Examples Scripts

### 3. Forum Backup (Community Knowledge)
- **Format**: Single text file provided by Marsten Parker with pre-emptive scrubbing
- **Timestamp**: July 3, 2025 (covers several years of history)
- **Distribution rule**: Must stay private to the RealTest community — NEVER included in public repo or release artifacts
- **Refresh**: Periodic updates can be requested from Marsten

#### Forum File Format (Analyzed)

**Topic delimiter**: `=== Topic: <title> ===`

**Post delimiter**: `Post ID <numeric_id>:`

**Post body contents**:
- Plain text
- Quoted text blocks (indented, with usernames like `mhp:`, `David63:`)
- Image references: `image<dimensions> <size>` (e.g., `image364×518 12.4 KB`) — strip these, no value without actual images
- Attachment references with filenames and sizes
- Embedded RTS file content: `RTS File (/uploads/short-url/<hash>.rts):` followed by actual script content
- URLs (inline and standalone)
- `@username` mentions throughout

**No explicit author field per post** — authors identifiable from quote blocks and @mentions.

**Embedded `.rts` scripts in forum posts are high-value** — these are real-world working examples with surrounding discussion context (bugs found, corrections suggested). Extract as separate chunks linked back to parent post/thread.

#### Parser Design
1. Split on `=== Topic:` to get threads
2. Within each thread, split on `Post ID \d+:` to get individual posts
3. Extract embedded `.rts` content as separate indexed chunks with back-reference to parent post/thread
4. Detect and extract quoted blocks to understand reply chains
5. Strip image dimension/size references
6. Preserve URLs (especially YouTube links for Phase 2)

---

## Architecture

### Ingestion Pipeline

```
Data Sources → Sanitization → Chunking → Embedding → SQLite Vector Store
```

1. **Collection**: Parse CHM HTML, read `.rts` files, parse forum backup
2. **Sanitization** (forum content only): PII removal, username anonymization
3. **Chunking**: Source-type-aware chunking strategy
4. **Embedding**: `all-MiniLM-L6-v2` via ONNX Runtime
5. **Storage**: SQLite + sqlite-vec with metadata

### Chunking Strategy

| Source | Chunk Granularity | Metadata |
|---|---|---|
| Docs (CHM HTML) | One chunk per function/concept page | source_type=docs, section=<category>, url=<page_path> |
| Example scripts | Full file as one chunk + individual strategy blocks as separate chunks | source_type=example, category=<from_index>, description=<from_index> |
| Forum posts | Thread-level + individual post-level for long posts | source_type=forum, topic=<thread_title>, post_id=<id> |
| Forum embedded .rts | Extracted as standalone code chunks with thread back-reference | source_type=forum_code, topic=<thread_title>, post_id=<parent_id> |
| Index/catalog pages | Single navigation chunk | source_type=catalog, purpose=navigation |

### Deterministic Chunk IDs
Use hash of `source_url/path + chunk_index` for deterministic IDs. Enables incremental updates — delete stale chunks and upsert new ones without rebuilding the entire DB.

### MCP Server Tools

```csharp
[McpServerTool, Description("Search RealTest documentation by concept or topic")]
public static string SearchDocs(string query, string? sourceFilter = null)

[McpServerTool, Description("Get the exact function signature, parameters, and description for a RealScript function")]
public static string GetFunctionReference(string functionName)

[McpServerTool, Description("Find example scripts demonstrating a concept or technique")]
public static string SearchExamples(string query, string? category = null)

[McpServerTool, Description("Search community forum discussions for real-world usage patterns and solutions")]
public static string SearchForum(string query)
```

### Hybrid Search
Combine vector similarity with metadata filtering. The Semantic Kernel SQLite connector supports this. Example: vector search for "position sizing" filtered to `source_type=docs` returns documentation, while the same query filtered to `source_type=example` returns example scripts.

---

## Sanitization Pipeline (Forum Content)

### Layers (in order)

1. **Regex PII filters**: Email addresses, phone numbers, street addresses, SSNs, credit card numbers, IP addresses. High precision, catches ~80% of PII.
2. **Username/handle scrubbing**: Replace all `@username` mentions and quoted author attributions with generic identifiers (e.g., `User_A`, `User_B`). Maintain consistency within a thread so reply chains still make sense.
3. **Topic relevance filtering**: Skip posts with no RealScript-related terms or trading concepts (off-topic discussions, account issues, licensing complaints).
4. **NER-based entity scrubbing**: `Microsoft.Recognizers.Text` + optional ML.NET NER model for contextual PII that regex misses (broker account details, trading account sizes, real names in prose, folder paths with usernames).
5. **Respect redacted content**: Some posts explicitly redact content (e.g., "entry and exit logic I will have to leave out as I'm bound by an NDA"). Do not attempt to infer or reconstruct.
6. **Flagged content review output**: Dump borderline/flagged content to a review file for manual inspection before committing to DB.

### Items to Watch For (from sample analysis)
- `@username` mentions throughout
- Quoted author attributions (`mhp:`, `David63:`, etc.)
- Personal folder paths (`C:\RealTest\Output\Orders\OrderClerk2`)
- Potential account identifiers in code (`OrderNote: "DUM249302"`)
- NDA-redacted content markers

---

## Sanitization Test Suite

Automated tests that validate the sanitization pipeline and serve as a CI gate.

### Test Categories

1. **Query-based PII probing**: Automated queries designed to extract PII — "who posted about X", "what is [username]'s trading strategy", "list email addresses", "what broker does [person] use". Any result identifying a real person = test failure.
2. **Pattern scanning on DB contents**: Directly inspect stored chunks in SQLite for PII patterns (emails, phone numbers, names, handles) that sanitization should have caught.
3. **Adversarial prompt testing**: Indirect extraction attempts — "summarize the most personal posts", "who had the biggest trading loss", "what did the user from [city] say about".
4. **Regression suite**: Every discovered leak becomes a permanent regression test.
5. **CI gate**: If the DB rebuild introduces content that fails the test suite, the GitHub Actions release artifact does NOT publish.

---

## Skills (Claude Code)

### RealScript Authoring Skill
When asked to write a strategy:
1. Search function signatures for all functions likely needed
2. Search examples for similar strategy patterns
3. Compose the script following patterns found in docs and examples
4. Validate syntax against canonical function signatures before presenting output
5. Include checklist of verified functions

### RealScript Debugging Skill
When user says "this isn't working":
1. Search docs for each function used in the script
2. Compare usage against canonical signatures
3. Flag discrepancies (wrong parameter order, deprecated syntax, etc.)
4. Search forum for similar issues/error messages

### Strategy Design Skill
When given a trading concept in plain English:
1. Search examples for similar approaches
2. Search docs for required building blocks
3. Scaffold the strategy structure before filling in details
4. Reference relevant example scripts as templates

### Targets
- **Primary**: Claude Code (terminal-based agentic coding)
- **Secondary**: Cowork (desktop task automation)
- Skill format: SKILL.md files

---

## GitHub Actions

### Content Hash Drift Detection
- On schedule (weekly) or manual trigger
- Fetch online docs from `mhptrading.com/docs/topics/`
- Hash each page, compare against stored manifest (`content-manifest.json` committed to repo)
- If nothing changed: exit in <1 minute (no-op)
- If changes detected: re-ingest only changed/new content via incremental update

### Incremental DB Updates
- ChromaDB-style upsert by deterministic chunk ID
- Delete stale chunks, insert new ones, leave unchanged content untouched
- Rebuild only what changed

### Release Artifact Publishing
- Pre-built SQLite `.db` file published as GitHub release artifact
- Contains: docs + examples only (NO forum content)
- Tagged with date and content version
- CI gate: sanitization test suite must pass before publish

### Estimated Resource Usage
- Single run: ~5-10 minutes on standard Linux runner
- Weekly schedule: ~40 minutes/month (well under free tier limits)
- Most scheduled runs are no-ops due to drift detection (~30 seconds)

---

## Distribution Model

### Public Repository Contains
- MCP server source code
- Ingestion pipeline code
- Skills (SKILL.md files)
- Sanitization pipeline + test suite
- GitHub Actions workflows
- Content hash manifest
- README with setup instructions

### Public Release Artifact
- Pre-built SQLite `.db` file with docs + examples only
- Single file download

### Forum Content (Private/Local Only)
- NEVER in the public repo or release artifacts
- Users with the forum backup file run a local command to enrich their DB
- Example: `dotnet run ingest-forum <path-to-forum-backup.txt>`
- Satisfies Marsten's "keep it in the community" rule

### User Install Flow
1. Clone the repo (or install via NuGet tool package if .NET 10+)
2. `dotnet restore`
3. Download latest release artifact (pre-built `.db` file) — or run full indexer locally
4. Configure in Claude Code:
```json
{
  "mcpServers": {
    "realtest": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/RealTestMcp"],
      "env": {
        "REALTEST_DB_PATH": "/path/to/realtest.db"
      }
    }
  }
}
```
5. (Optional) If community member with forum backup: `dotnet run ingest-forum <path>`

### Requirements
- .NET SDK (version TBD based on ModelContextProtocol package requirements)
- No Docker
- No external API keys
- No server processes
- No network dependencies at runtime

---

## Phase 2 — YouTube Video Content Ingestion (Future)

### Context
A forum post titled "Compilation of links to RealTest videos and learning material" contains curated video URLs. Most/all are YouTube videos.

### Approach
- Pull transcripts via `youtube-transcript-api` or `yt-dlp` subtitle extraction — NO Whisper/GPU needed
- YouTube auto-generates transcripts; creator-corrected captions are higher quality when available
- Transcripts come pre-timestamped per segment

### Features
- Timestamped chunks with deep-link video URLs in search results (e.g., `https://youtube.com/watch?v=xyz&t=142`)
- Cleanup pass to normalize auto-generated caption errors on RealScript terminology
- MCP responses include direct video links at relevant timestamps

### Processing
- Lightweight enough for GitHub Actions (no GPU required)
- May need a small Python helper script for `youtube-transcript-api` (Python-only library) — or find .NET equivalent

---

## RealScript Language Reference (What We Know)

RealScript is declarative — you describe what constitutes a trade, not step-by-step instructions.

### Core Script Sections
- **Import** / **Data** — symbol universe, data sources, data section formulas
- **Settings** — backtest parameters (dates, capital, commission, etc.)
- **Parameters** — named parameters for optimization
- **Library** — reusable formula definitions
- **Template** — shared strategy settings
- **Strategy** — entry/exit rules, position sizing, ranking
- **Combined** — portfolio-level constraints across strategies
- **StatsGroup** — grouped statistics across strategy subsets
- **Scan** — screening/scanning definitions
- **OrderSettings** — order generation configuration
- **TestData** — per-bar test-level calculations
- **StratData** — per-bar strategy-level cross-sectional calculations (newer feature)

### Key Concepts
- `EntrySetup` / `Entry` — conditions qualifying a trade and triggering entry
- `ExitRule` / `ExitStop` — exit conditions
- `Quantity` / `QtyType` — position sizing
- `SetupScore` — ranking when more signals than capital
- `MaxPositions` — position limits
- Built-in functions: `C` (close), `H` (high), `L` (low), `V` (volume), `Avg()`, `ATR()`, `RSI()`, `CRSI()`, `ROC()`, `ADX()`, `Lowest()`, `Highest()`, etc.
- `Extern()` — reference stats from other strategies
- `Combined()` — reference combined portfolio stats
- `#Rank` — cross-sectional ranking operator
- `InList()`, `InRUI` — universe membership tests
- `S.Equity`, `S.M2M`, `S.Alloc` — strategy statistics
- Comments use `//`

### IMPORTANT
The above is from LLM training data and is known to be incomplete/partially inaccurate. The entire purpose of this MCP server is to provide authoritative, searchable access to the actual documentation. Always prefer MCP search results over this reference.

---

## Project Structure (Suggested)

```
RealTestMcp/
├── src/
│   ├── RealTestMcp.Server/          # MCP server (entry point)
│   │   ├── Program.cs
│   │   ├── Tools/
│   │   │   ├── SearchDocsTool.cs
│   │   │   ├── FunctionReferenceTool.cs
│   │   │   ├── SearchExamplesTool.cs
│   │   │   └── SearchForumTool.cs
│   │   └── Services/
│   │       ├── VectorSearchService.cs
│   │       └── EmbeddingService.cs
│   ├── RealTestMcp.Ingestion/       # Ingestion pipeline (CLI)
│   │   ├── Program.cs
│   │   ├── Parsers/
│   │   │   ├── ChmParser.cs
│   │   │   ├── RtsFileParser.cs
│   │   │   ├── ForumParser.cs
│   │   │   └── ExampleIndexParser.cs
│   │   ├── Sanitization/
│   │   │   ├── PiiFilter.cs
│   │   │   ├── UsernameAnonymizer.cs
│   │   │   ├── RelevanceFilter.cs
│   │   │   └── NerEntityScrubber.cs
│   │   ├── Chunking/
│   │   │   ├── DocChunker.cs
│   │   │   ├── ScriptChunker.cs
│   │   │   └── ForumChunker.cs
│   │   └── Embedding/
│   │       └── OnnxEmbeddingService.cs
│   └── RealTestMcp.Shared/          # Shared models/utilities
│       ├── Models/
│       └── Configuration/
├── tests/
│   ├── RealTestMcp.Sanitization.Tests/
│   │   ├── PiiProbeTests.cs
│   │   ├── PatternScanTests.cs
│   │   ├── AdversarialTests.cs
│   │   └── RegressionTests.cs
│   └── RealTestMcp.Server.Tests/
├── skills/
│   ├── realscript-authoring/SKILL.md
│   ├── realscript-debugging/SKILL.md
│   └── strategy-design/SKILL.md
├── .github/
│   └── workflows/
│       ├── drift-detection.yml
│       ├── build-db.yml
│       └── release.yml
├── data/
│   └── content-manifest.json
├── CLAUDE.md                         # → this document or a trimmed version
├── README.md
└── RealTestMcp.sln
```

---

## Open Items / Waiting On

1. ~~Help file format~~ — Confirmed: both CHM and PDF available locally
2. ~~Online docs URL~~ — Confirmed: `https://mhptrading.com/docs/topics/idh-topic10.htm`
3. ~~Forum backup format~~ — Analyzed, parser design complete
4. **Marsten's explicit go-ahead on distribution** — Implicit permission based on public forum backup posting with "keep it in the community" rule. Distribution model respects this.
5. **Full forum backup file** — needed for building forum parser and sanitization pipeline
6. **Local `.rts` example scripts** — from RealTest install directory
7. **CHM file** — from RealTest install directory

Items 5-7 are needed when building but not for scaffolding the project structure, MCP server skeleton, and test infrastructure.
