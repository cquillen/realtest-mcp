# RealTest MCP Server Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a single-binary C# MCP server that ingests RealTest documentation and example scripts into a local SQLite vector database and exposes four semantic search tools to Claude Code.

**Architecture:** Single .NET 10 console app with dual-mode entry point — no CLI args runs the MCP server over stdio, named subcommands run ingestion. All vector storage is SQLite + sqlite-vec with a thin service wrapper. Embeddings use SmartComponents.LocalEmbeddings (model bundled in NuGet, zero download).

**Tech Stack:** .NET 10, ModelContextProtocol, System.CommandLine, Microsoft.Data.Sqlite, sqlite-vec (native extension), SmartComponents.LocalEmbeddings, HtmlAgilityPack, xUnit

---

## File Map

> Every file listed here will be created. This is the complete picture before a single line of code is written.

### Solution
- `RealTestMcp.sln` — solution file referencing both projects

### Main Project: `src/RealTestMcp/`
| File | Responsibility |
|---|---|
| `RealTestMcp.csproj` | Project config, NuGet references, native sqlite-vec content copy |
| `Program.cs` | Entry point; System.CommandLine routing; MCP server host setup |
| `Core/Models/Chunk.cs` | Immutable record representing a stored text chunk with all metadata |
| `Core/Models/SearchResult.cs` | Immutable record returned by all search tools |
| `Core/Configuration/AppSettings.cs` | Config binding with env-var expansion applied at property access |
| `Core/EmbeddingService.cs` | Thin wrapper over `LocalEmbedder` from SmartComponents.LocalEmbeddings |
| `Core/VectorStoreService.cs` | All SQLite + sqlite-vec interaction: schema creation, upsert, delete-by-scope, vector search, keyword search |
| `Ingestion/Parsers/ChmParser.cs` | Parses a directory of extracted CHM HTML files into raw `(path, html)` pairs |
| `Ingestion/Parsers/RtsParser.cs` | Reads a `.rts` file and returns its raw text with source metadata |
| `Ingestion/Chunkers/DocChunker.cs` | Converts HTML pages into `Chunk` records; detects multi-function pages and emits `function_entry` chunks |
| `Ingestion/Chunkers/ScriptChunker.cs` | Converts `.rts` file text into a single `Chunk` record with category/description metadata |
| `Ingestion/Commands/IngestDocsCommand.cs` | Orchestrates full docs ingestion: parse → chunk → embed → store |
| `Ingestion/Commands/IngestScriptsCommand.cs` | Orchestrates full scripts ingestion for all configured paths |
| `Tools/SearchDocsTool.cs` | MCP tool: semantic vector search over `source_type=docs` chunks |
| `Tools/GetFunctionReferenceTool.cs` | MCP tool: keyword-first then semantic search for a named RealScript function |
| `Tools/SearchExamplesTool.cs` | MCP tool: semantic search over `source_type=example` chunks |
| `Tools/SearchUserScriptsTool.cs` | MCP tool: semantic search over `source_type=user_script` chunks |

### Test Project: `tests/RealTestMcp.Tests/`
| File | Responsibility |
|---|---|
| `RealTestMcp.Tests.csproj` | xUnit test project referencing main project |
| `Parsers/ChmParserTests.cs` | Unit tests: given HTML string → correct page/function_entry chunks |
| `Parsers/RtsParserTests.cs` | Unit tests: given .rts string → correct script chunk |
| `Chunkers/DocChunkerTests.cs` | Unit tests: section extraction, multi-function page splitting, metadata |
| `Chunkers/ScriptChunkerTests.cs` | Unit tests: category/description attachment, chunk ID determinism |
| `Integration/IngestSearchTests.cs` | Integration tests: real embed+store+search round-trips using sample data |
| `data/docs/single-function.html` | Sample CHM page with one function (ATR) |
| `data/docs/multi-function.html` | Sample CHM page with two functions (Highest, Lowest) — triggers function_entry splitting |
| `data/scripts/mean-reversion.rts` | Sample script in "Mean Reversion" category |
| `data/scripts/futures-example.rts` | Sample script in "Futures" category |

### Config & CI
| File | Responsibility |
|---|---|
| `appsettings.json` | Shipped default config with %LOCALAPPDATA% paths |
| `CLAUDE.md` | `@include` directives for all three skills |
| `README.md` | Setup instructions |
| `.github/workflows/ci.yml` | Build + test on every push to main |
| `native/windows-x64/vec0.dll` | sqlite-vec native extension for Windows x64 (downloaded from sqlite-vec releases, committed to repo) |

### Skills
| File | Responsibility |
|---|---|
| `skills/realscript-authoring/SKILL.md` | Enforces MCP tool lookup before any RealScript code generation |
| `skills/realscript-debugging/SKILL.md` | Enforces function signature verification when debugging |
| `skills/strategy-design/SKILL.md` | Structures the workflow for translating trading ideas to RealScript |

---

## Pre-Flight: Get sqlite-vec Native Extension

Before starting Task 7, download `vec0.dll` for Windows x64 from the sqlite-vec GitHub releases:
- URL: `https://github.com/asg017/sqlite-vec/releases` → latest release → `sqlite-vec-v*.0-loadable-windows-x86_64.zip`
- Extract `vec0.dll`, place at `native/windows-x64/vec0.dll` in the repo root
- Verify the DLL can be loaded: `sqlite3 :memory: ".load ./vec0" "SELECT vec_version()"`

This file is committed to the repo so all developers and CI get it automatically.

---

## Task 1: Solution and Project Scaffold

**Files:**
- Create: `RealTestMcp.sln`
- Create: `src/RealTestMcp/RealTestMcp.csproj`
- Create: `tests/RealTestMcp.Tests/RealTestMcp.Tests.csproj`
- Create: `src/RealTestMcp/Program.cs` (stub)

- [ ] **Step 1: Create directory structure**

```bash
mkdir -p src/RealTestMcp tests/RealTestMcp.Tests native/windows-x64
```

- [ ] **Step 2: Create the main project**

```bash
cd src/RealTestMcp
dotnet new console -n RealTestMcp --framework net10.0 --force
```

- [ ] **Step 3: Create the test project**

```bash
cd tests/RealTestMcp.Tests
dotnet new xunit -n RealTestMcp.Tests --framework net10.0 --force
```

- [ ] **Step 4: Create the solution and add projects**

```bash
# From repo root
dotnet new sln -n RealTestMcp
dotnet sln add src/RealTestMcp/RealTestMcp.csproj
dotnet sln add tests/RealTestMcp.Tests/RealTestMcp.Tests.csproj
```

- [ ] **Step 5: Add project reference from tests to main**

```bash
dotnet add tests/RealTestMcp.Tests/RealTestMcp.Tests.csproj reference src/RealTestMcp/RealTestMcp.csproj
```

- [ ] **Step 6: Add NuGet packages to main project**

```bash
dotnet add src/RealTestMcp/RealTestMcp.csproj package ModelContextProtocol
dotnet add src/RealTestMcp/RealTestMcp.csproj package System.CommandLine --prerelease
dotnet add src/RealTestMcp/RealTestMcp.csproj package Microsoft.Data.Sqlite
dotnet add src/RealTestMcp/RealTestMcp.csproj package SmartComponents.LocalEmbeddings
dotnet add src/RealTestMcp/RealTestMcp.csproj package HtmlAgilityPack
dotnet add src/RealTestMcp/RealTestMcp.csproj package Microsoft.Extensions.Hosting
dotnet add src/RealTestMcp/RealTestMcp.csproj package Microsoft.Extensions.Configuration.Json
```

- [ ] **Step 7: Configure sqlite-vec native DLL copy in csproj**

Edit `src/RealTestMcp/RealTestMcp.csproj` to add after the existing `<PropertyGroup>`:

```xml
<ItemGroup>
  <Content Include="..\..\native\windows-x64\vec0.dll">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <Link>vec0.dll</Link>
  </Content>
</ItemGroup>
```

- [ ] **Step 8: Write stub Program.cs**

```csharp
// src/RealTestMcp/Program.cs
Console.WriteLine("RealTest MCP");
```

- [ ] **Step 9: Verify solution builds**

```bash
dotnet build RealTestMcp.sln
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 10: Verify tests run (empty suite)**

```bash
dotnet test RealTestMcp.sln
```

Expected: `Test run succeeded. Total: 0`.

- [ ] **Step 11: Commit**

```bash
git add .
git commit -m "chore: scaffold solution, projects, and NuGet references"
```

---

## Task 2: Core Models

**Files:**
- Create: `src/RealTestMcp/Core/Models/Chunk.cs`
- Create: `src/RealTestMcp/Core/Models/SearchResult.cs`

These are pure value objects. No test needed — they're data containers with no logic.

- [ ] **Step 1: Create Chunk record**

```csharp
// src/RealTestMcp/Core/Models/Chunk.cs
namespace RealTestMcp.Core.Models;

/// <summary>
/// A single indexed unit of text content with metadata.
/// source_type: 'docs' | 'example' | 'user_script'
/// chunk_type:  'page' | 'function_entry' | 'script'
/// </summary>
public record Chunk(
    string Id,           // SHA256(source_path + ':' + chunk_index)
    string SourceType,
    string SourcePath,
    string ChunkType,
    string? Section,
    string? Category,
    string? Description,
    string Content,
    int ChunkIndex,
    DateTime CreatedAt
);
```

- [ ] **Step 2: Create SearchResult record**

```csharp
// src/RealTestMcp/Core/Models/SearchResult.cs
namespace RealTestMcp.Core.Models;

public record SearchResult(
    string Id,
    string SourceType,
    string SourcePath,   // Absolute path to the source file
    string ChunkType,
    string? Section,
    string? Category,
    string? Description,
    string Content,   // Truncated to 1500 chars if over limit
    double Score
);
```

- [ ] **Step 3: Build to confirm**

```bash
dotnet build RealTestMcp.sln
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/RealTestMcp/Core/
git commit -m "feat: add Chunk and SearchResult core models"
```

---

## Task 3: AppSettings

**Files:**
- Create: `src/RealTestMcp/Core/Configuration/AppSettings.cs`
- Create: `appsettings.json`
- Test: `tests/RealTestMcp.Tests/Configuration/AppSettingsTests.cs`

- [ ] **Step 1: Write failing test for env-var expansion**

```csharp
// tests/RealTestMcp.Tests/Configuration/AppSettingsTests.cs
using RealTestMcp.Core.Configuration;

namespace RealTestMcp.Tests.Configuration;

public class AppSettingsTests
{
    [Fact]
    public void DbPath_ExpandsEnvironmentVariables()
    {
        var settings = new AppSettings();
        settings.Database.Path = "%LOCALAPPDATA%\\RealTestMcp\\realtest.db";

        Assert.DoesNotContain("%LOCALAPPDATA%", settings.Database.Path);
        Assert.Contains("RealTestMcp", settings.Database.Path);
    }

    [Fact]
    public void ScriptPaths_ExpandsAllEntries()
    {
        var settings = new AppSettings();
        settings.RealTest.ScriptPaths = ["%LOCALAPPDATA%\\Scripts", @"C:\RealTest\Examples"];

        Assert.All(settings.RealTest.ScriptPaths, p => Assert.DoesNotContain("%", p));
    }

    [Fact]
    public void DefaultDbPath_IsUnderLocalAppData()
    {
        var settings = new AppSettings();
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(expected, settings.Database.Path);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test RealTestMcp.sln --filter "FullyQualifiedName~AppSettingsTests"
```

Expected: FAIL — `AppSettings` type not found.

- [ ] **Step 3: Implement AppSettings**

```csharp
// src/RealTestMcp/Core/Configuration/AppSettings.cs
namespace RealTestMcp.Core.Configuration;

public class AppSettings
{
    public DatabaseSettings Database { get; set; } = new();
    public RealTestSettings RealTest { get; set; } = new();

    public class DatabaseSettings
    {
        private string _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RealTestMcp", "realtest.db");

        public string Path
        {
            get => _path;
            set => _path = Environment.ExpandEnvironmentVariables(value);
        }
    }

    public class RealTestSettings
    {
        public string InstallPath { get; set; } = @"C:\RealTest";

        private string _docsPath = @"C:\RealTest\Help";
        public string DocsPath
        {
            get => _docsPath;
            set => _docsPath = Environment.ExpandEnvironmentVariables(value);
        }

        private string[] _scriptPaths = [@"C:\RealTest\Scripts\Examples"];
        public string[] ScriptPaths
        {
            get => _scriptPaths;
            set => _scriptPaths = value
                .Select(Environment.ExpandEnvironmentVariables)
                .ToArray();
        }
    }

    /// <summary>
    /// Load from appsettings.json next to the binary, then apply env var overrides.
    /// Resolves config file relative to AppContext.BaseDirectory, not working directory.
    /// </summary>
    public static AppSettings Load()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables(prefix: "REALTEST_MCP_")
            .Build();

        var settings = new AppSettings();
        config.Bind(settings);
        return settings;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test RealTestMcp.sln --filter "FullyQualifiedName~AppSettingsTests"
```

Expected: All 3 tests pass.

- [ ] **Step 5: Create appsettings.json**

```json
{
  "Database": {
    "Path": "%LOCALAPPDATA%\\RealTestMcp\\realtest.db"
  },
  "RealTest": {
    "InstallPath": "C:\\RealTest",
    "DocsPath": "C:\\RealTest\\Help",
    "ScriptPaths": [
      "C:\\RealTest\\Scripts\\Examples"
    ]
  }
}
```

Add to `src/RealTestMcp/RealTestMcp.csproj`:
```xml
<ItemGroup>
  <Content Include="..\..\appsettings.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <Link>appsettings.json</Link>
  </Content>
</ItemGroup>
```

- [ ] **Step 6: Commit**

```bash
git add .
git commit -m "feat: add AppSettings with env-var expansion and config file loading"
```

---

## Task 4: CLI Routing and Status Stub

**Files:**
- Modify: `src/RealTestMcp/Program.cs`

- [ ] **Step 1: Implement CLI routing with stub handlers**

```csharp
// src/RealTestMcp/Program.cs
using System.CommandLine;
using RealTestMcp.Core.Configuration;

var settings = AppSettings.Load();

// ── ingest docs ──────────────────────────────────────────────────
var ingestDocsCommand = new Command("docs", "Ingest CHM documentation into the database");
ingestDocsCommand.SetHandler(() =>
{
    Console.Error.WriteLine("ingest docs: not yet implemented");
});

// ── ingest scripts ───────────────────────────────────────────────
var ingestScriptsCommand = new Command("scripts", "Ingest .rts scripts into the database");
ingestScriptsCommand.SetHandler(() =>
{
    Console.Error.WriteLine("ingest scripts: not yet implemented");
});

// ── ingest (parent) ──────────────────────────────────────────────
var ingestCommand = new Command("ingest", "Ingest data sources into the database");
ingestCommand.AddCommand(ingestDocsCommand);
ingestCommand.AddCommand(ingestScriptsCommand);

// ── status ───────────────────────────────────────────────────────
var statusCommand = new Command("status", "Show database statistics");
statusCommand.SetHandler(() =>
{
    var dbPath = settings.Database.Path;
    if (!File.Exists(dbPath))
    {
        Console.WriteLine("DB not initialized — run: realtest-mcp ingest docs");
        return;
    }
    Console.WriteLine("Status: DB exists (full status in Task 8)");
});

// ── root: MCP server mode ────────────────────────────────────────
var rootCommand = new RootCommand("RealTest MCP Server — semantic search for RealScript documentation");
rootCommand.AddCommand(ingestCommand);
rootCommand.AddCommand(statusCommand);
rootCommand.SetHandler(async () =>
{
    Console.Error.WriteLine("MCP server mode: not yet implemented");
    await Task.CompletedTask;
});

return await rootCommand.InvokeAsync(args);
```

- [ ] **Step 2: Verify CLI routing works**

```bash
dotnet run --project src/RealTestMcp/RealTestMcp.csproj -- --help
dotnet run --project src/RealTestMcp/RealTestMcp.csproj -- ingest --help
dotnet run --project src/RealTestMcp/RealTestMcp.csproj -- status
```

Expected:
- `--help` prints command list
- `ingest --help` shows `docs` and `scripts` subcommands
- `status` prints "DB not initialized — run: realtest-mcp ingest docs"

- [ ] **Step 3: Commit**

```bash
git add src/RealTestMcp/Program.cs
git commit -m "feat: add CLI routing with status stub and ingest command stubs"
```

---

## Task 5: MCP Server Skeleton

**Files:**
- Modify: `src/RealTestMcp/Program.cs`
- Create: `src/RealTestMcp/Tools/SearchDocsTool.cs` (stub)
- Create: `src/RealTestMcp/Tools/GetFunctionReferenceTool.cs` (stub)
- Create: `src/RealTestMcp/Tools/SearchExamplesTool.cs` (stub)
- Create: `src/RealTestMcp/Tools/SearchUserScriptsTool.cs` (stub)

> **Note:** Check the ModelContextProtocol NuGet package README for the exact API before implementing. The pattern below follows the standard C# MCP SDK. Verify tool attribute names (`[McpServerTool]`, `[Description]`) match what the installed version provides.

- [ ] **Step 1: Create stub tool files**

```csharp
// src/RealTestMcp/Tools/SearchDocsTool.cs
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RealTestMcp.Tools;

[McpServerToolType]
public static class SearchDocsTool
{
    [McpServerTool, Description("Search RealTest documentation by concept or topic")]
    public static string SearchDocs(
        [Description("What to search for")] string query,
        [Description("Optional: limit to a doc section (e.g. 'Strategy', 'Import')")] string? sectionFilter = null,
        [Description("Number of results to return (default: 5)")] int topK = 5)
    {
        return "DB not initialized — run: realtest-mcp ingest docs";
    }
}
```

Create identical stubs for `GetFunctionReferenceTool.cs`, `SearchExamplesTool.cs`, and `SearchUserScriptsTool.cs`:

```csharp
// src/RealTestMcp/Tools/GetFunctionReferenceTool.cs
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RealTestMcp.Tools;

[McpServerToolType]
public static class GetFunctionReferenceTool
{
    [McpServerTool, Description("Get the exact function signature and description for a RealScript function")]
    public static string GetFunctionReference(
        [Description("Function name to look up (e.g. 'ATR', 'Lowest')")] string functionName)
    {
        return "DB not initialized — run: realtest-mcp ingest docs";
    }
}
```

```csharp
// src/RealTestMcp/Tools/SearchExamplesTool.cs
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RealTestMcp.Tools;

[McpServerToolType]
public static class SearchExamplesTool
{
    [McpServerTool, Description("Find example RealScript files demonstrating a concept or technique")]
    public static string SearchExamples(
        [Description("What to search for")] string query,
        [Description("Optional: filter by category (e.g. 'Mean Reversion', 'Futures')")] string? categoryFilter = null,
        [Description("Number of results to return (default: 3)")] int topK = 3)
    {
        return "DB not initialized — run: realtest-mcp ingest scripts";
    }
}
```

```csharp
// src/RealTestMcp/Tools/SearchUserScriptsTool.cs
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RealTestMcp.Tools;

[McpServerToolType]
public static class SearchUserScriptsTool
{
    [McpServerTool, Description("Search your own RealScript files for patterns or techniques")]
    public static string SearchUserScripts(
        [Description("What to search for")] string query,
        [Description("Number of results to return (default: 3)")] int topK = 3)
    {
        return "DB not initialized — run: realtest-mcp ingest scripts";
    }
}
```

- [ ] **Step 2: Wire MCP server into Program.cs root handler**

Replace the root handler stub in `Program.cs`:

```csharp
rootCommand.SetHandler(async () =>
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services
        .AddSingleton(settings)
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly(typeof(SearchDocsTool).Assembly);

    await builder.Build().RunAsync();
});
```

Add required usings at top of `Program.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using RealTestMcp.Tools;
```

- [ ] **Step 3: Build to confirm**

```bash
dotnet build RealTestMcp.sln
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Test MCP server responds to initialize**

Run the server and send a minimal JSON-RPC initialize message. Claude Code will do this automatically — configure the MCP server in your Claude Code settings to verify:

```json
{
  "mcpServers": {
    "realtest": {
      "command": "dotnet",
      "args": ["run", "--project", "D:/Code/realtest-mcp/src/RealTestMcp/RealTestMcp.csproj"]
    }
  }
}
```

Start a new Claude Code session and verify `realtest` appears in available tools with the four stub tools listed.

- [ ] **Step 5: Commit**

```bash
git add src/RealTestMcp/
git commit -m "feat: add MCP server skeleton with four stub tools"
```

---

## Task 6: CI Pipeline

**Files:**
- Create: `.github/workflows/ci.yml`

- [ ] **Step 1: Create workflow file**

```yaml
# .github/workflows/ci.yml
name: CI

on:
  push:
    branches: [main, master]
  pull_request:
    branches: [main, master]

jobs:
  build-and-test:
    runs-on: windows-latest

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore RealTestMcp.sln

      - name: Build
        run: dotnet build RealTestMcp.sln --no-restore --configuration Release

      - name: Test
        run: dotnet test RealTestMcp.sln --no-build --configuration Release --verbosity normal
```

> **Note:** `windows-latest` is required because sqlite-vec ships a Windows x64 DLL. If Linux support is needed later, a separate runner with the Linux SO would be added.

- [ ] **Step 2: Commit and push to trigger CI**

```bash
git add .github/
git commit -m "ci: add GitHub Actions build and test workflow"
git push
```

- [ ] **Step 3: Verify CI passes on GitHub**

Check the Actions tab. Expected: green build.

---

## Task 7: VectorStoreService (SQLite + sqlite-vec)

**Files:**
- Create: `src/RealTestMcp/Core/VectorStoreService.cs`
- Test: `tests/RealTestMcp.Tests/Core/VectorStoreServiceTests.cs`

> **sqlite-vec extension loading:** The `vec0.dll` is copied to the build output directory. Load it with:
> ```csharp
> connection.EnableExtensions(true);
> connection.LoadExtension(Path.Combine(AppContext.BaseDirectory, "vec0"));
> ```
> `Microsoft.Data.Sqlite` enables extension loading via `EnableExtensions(true)` on an open connection. Verify this works with your installed `SQLitePCLRaw` bundle version — if `EnableExtensions` is not available, use the connection string option `"Mode=ReadWriteCreate;Foreign Keys=True"` and the `LoadExtension` method directly.
>
> **vec0 TEXT PRIMARY KEY validation:** Before implementing, test that `vec0` accepts a TEXT primary key:
> ```sql
> CREATE VIRTUAL TABLE t USING vec0(id TEXT PRIMARY KEY, v FLOAT[4]);
> INSERT INTO t VALUES ('test', '[1.0, 2.0, 3.0, 4.0]');
> SELECT * FROM t WHERE id = 'test';
> ```
> If this fails, use an integer `rowid` instead and join via a separate `chunk_id TEXT` column. Adjust the schema in `EnsureSchemaAsync` accordingly.

- [ ] **Step 1: Write failing tests**

```csharp
// tests/RealTestMcp.Tests/Core/VectorStoreServiceTests.cs
using RealTestMcp.Core;
using RealTestMcp.Core.Models;

namespace RealTestMcp.Tests.Core;

public class VectorStoreServiceTests : IAsyncLifetime
{
    private VectorStoreService _store = null!;
    private string _dbPath = null!;

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");
        _store = new VectorStoreService(_dbPath);
        await _store.EnsureSchemaAsync();
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public async Task EnsureSchemaAsync_CreatesChunksTable()
    {
        var counts = await _store.GetChunkCountsAsync();
        Assert.NotNull(counts);
    }

    [Fact]
    public async Task UpsertAndSearch_ReturnsMatchingChunk()
    {
        var chunk = MakeChunk("chunk-1", "docs", "page");
        var embedding = MakeEmbedding(1.0f);

        await _store.UpsertChunkAsync(chunk, embedding);

        var results = await _store.VectorSearchAsync(embedding, sourceType: "docs", topK: 5);
        Assert.Single(results);
        Assert.Equal("chunk-1", results[0].Id);
    }

    [Fact]
    public async Task DeleteBySourceType_RemovesOnlyMatchingChunks()
    {
        await _store.UpsertChunkAsync(MakeChunk("a", "docs", "page"), MakeEmbedding(1.0f));
        await _store.UpsertChunkAsync(MakeChunk("b", "example", "script"), MakeEmbedding(2.0f));

        await _store.DeleteBySourceTypeAsync("docs");

        var remaining = await _store.VectorSearchAsync(MakeEmbedding(1.0f), sourceType: null, topK: 10);
        Assert.Single(remaining);
        Assert.Equal("b", remaining[0].Id);
    }

    [Fact]
    public async Task KeywordSearch_FindsChunkByContent()
    {
        var chunk = MakeChunk("fn-1", "docs", "function_entry", content: "ATR(periods) Average True Range");
        await _store.UpsertChunkAsync(chunk, MakeEmbedding(1.0f));

        var results = await _store.KeywordSearchAsync("ATR", chunkType: "function_entry", topK: 3);
        Assert.Single(results);
        Assert.Equal("fn-1", results[0].Id);
    }

    // ── helpers ────────────────────────────────────────────────────

    private static Chunk MakeChunk(string id, string sourceType, string chunkType, string content = "test content")
        => new(id, sourceType, "/path/file", chunkType, null, null, null, content, 0, DateTime.UtcNow);

    private static float[] MakeEmbedding(float value)
        => Enumerable.Repeat(value, 384).ToArray();
}
```

- [ ] **Step 2: Run to verify tests fail**

```bash
dotnet test RealTestMcp.sln --filter "FullyQualifiedName~VectorStoreServiceTests"
```

Expected: FAIL — `VectorStoreService` not found.

- [ ] **Step 3: Implement VectorStoreService**

```csharp
// src/RealTestMcp/Core/VectorStoreService.cs
using Microsoft.Data.Sqlite;
using RealTestMcp.Core.Models;
using System.Security.Cryptography;
using System.Text;

namespace RealTestMcp.Core;

public class VectorStoreService : IAsyncDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _connection;

    public VectorStoreService(string dbPath)
    {
        _dbPath = dbPath;
    }

    public static string ComputeChunkId(string sourcePath, int chunkIndex)
    {
        var input = $"{sourcePath}:{chunkIndex}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<SqliteConnection> GetConnectionAsync()
    {
        if (_connection is null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
            _connection = new SqliteConnection($"Data Source={_dbPath}");
            await _connection.OpenAsync();

            // Load sqlite-vec extension
            _connection.EnableExtensions(true);
            _connection.LoadExtension(Path.Combine(AppContext.BaseDirectory, "vec0"));
        }
        return _connection;
    }

    public async Task EnsureSchemaAsync()
    {
        var conn = await GetConnectionAsync();

        // Each DDL statement must be executed separately — Microsoft.Data.Sqlite
        // uses sqlite3_prepare_v2 which stops at the first semicolon.
        await ExecuteNonQueryAsync(conn, """
            CREATE TABLE IF NOT EXISTS chunks (
                id           TEXT    PRIMARY KEY,
                source_type  TEXT    NOT NULL,
                source_path  TEXT    NOT NULL,
                chunk_type   TEXT    NOT NULL,
                section      TEXT,
                category     TEXT,
                description  TEXT,
                content      TEXT    NOT NULL,
                chunk_index  INTEGER NOT NULL,
                created_at   TEXT    NOT NULL
            )
            """);
        await ExecuteNonQueryAsync(conn, "CREATE INDEX IF NOT EXISTS idx_chunks_source_type ON chunks(source_type)");
        await ExecuteNonQueryAsync(conn, "CREATE INDEX IF NOT EXISTS idx_chunks_source_path ON chunks(source_path)");
        await ExecuteNonQueryAsync(conn, "CREATE INDEX IF NOT EXISTS idx_chunks_chunk_type  ON chunks(chunk_type)");

        // sqlite-vec virtual table for vector search
        await ExecuteNonQueryAsync(conn, """
            CREATE VIRTUAL TABLE IF NOT EXISTS chunk_embeddings USING vec0(
                chunk_id  TEXT PRIMARY KEY,
                embedding FLOAT[384]
            )
            """);
    }

    public async Task UpsertChunkAsync(Chunk chunk, float[] embedding)
    {
        var conn = await GetConnectionAsync();
        await ExecuteNonQueryAsync(conn, """
            INSERT OR REPLACE INTO chunks
                (id, source_type, source_path, chunk_type, section, category, description, content, chunk_index, created_at)
            VALUES
                (@id, @source_type, @source_path, @chunk_type, @section, @category, @description, @content, @chunk_index, @created_at)
            """,
            ("@id", chunk.Id),
            ("@source_type", chunk.SourceType),
            ("@source_path", chunk.SourcePath),
            ("@chunk_type", chunk.ChunkType),
            ("@section", (object?)chunk.Section ?? DBNull.Value),
            ("@category", (object?)chunk.Category ?? DBNull.Value),
            ("@description", (object?)chunk.Description ?? DBNull.Value),
            ("@content", chunk.Content),
            ("@chunk_index", chunk.ChunkIndex),
            ("@created_at", chunk.CreatedAt.ToString("O")));

        // Upsert embedding vector
        var vectorJson = "[" + string.Join(",", embedding) + "]";
        await ExecuteNonQueryAsync(conn,
            "INSERT OR REPLACE INTO chunk_embeddings (chunk_id, embedding) VALUES (@id, @vec)",
            ("@id", chunk.Id),
            ("@vec", vectorJson));
    }

    public async Task DeleteBySourceTypeAsync(string sourceType)
    {
        var conn = await GetConnectionAsync();
        // Delete embeddings first (no cascade on virtual table)
        await ExecuteNonQueryAsync(conn,
            "DELETE FROM chunk_embeddings WHERE chunk_id IN (SELECT id FROM chunks WHERE source_type = @st)",
            ("@st", sourceType));
        await ExecuteNonQueryAsync(conn,
            "DELETE FROM chunks WHERE source_type = @st",
            ("@st", sourceType));
    }

    public async Task DeleteBySourceTypesAsync(IEnumerable<string> sourceTypes)
    {
        foreach (var st in sourceTypes)
            await DeleteBySourceTypeAsync(st);
    }

    public async Task<List<SearchResult>> VectorSearchAsync(
        float[] queryEmbedding,
        string? sourceType,
        string? categoryFilter = null,
        string? sectionFilter = null,
        int topK = 5)
    {
        var conn = await GetConnectionAsync();
        var vectorJson = "[" + string.Join(",", queryEmbedding) + "]";

        // Use subquery pattern: run KNN on vec table first, then join and filter.
        // Direct JOIN + MATCH + WHERE on non-vec columns is not reliably supported by vec0.
        // Over-fetch by 3x to ensure enough results survive the metadata filters.
        var fetchK = topK * 3;

        var whereClause = sourceType is not null ? "WHERE c.source_type = @source_type" : "WHERE 1=1";
        if (categoryFilter is not null) whereClause += " AND LOWER(c.category) = LOWER(@category)";
        if (sectionFilter  is not null) whereClause += " AND LOWER(c.section)  LIKE LOWER(@section)";

        var sql = $"""
            SELECT c.id, c.source_type, c.source_path, c.chunk_type, c.section,
                   c.category, c.description, c.content, knn.distance
            FROM chunks c
            JOIN (
                SELECT chunk_id, distance
                FROM chunk_embeddings
                WHERE embedding MATCH @vec AND k = @fetchk
            ) knn ON c.id = knn.chunk_id
            {whereClause}
            ORDER BY knn.distance
            LIMIT @topk
            """;

        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@vec", vectorJson);
        cmd.Parameters.AddWithValue("@fetchk", fetchK);
        cmd.Parameters.AddWithValue("@topk", topK);
        if (sourceType    is not null) cmd.Parameters.AddWithValue("@source_type", sourceType);
        if (categoryFilter is not null) cmd.Parameters.AddWithValue("@category", categoryFilter);
        if (sectionFilter  is not null) cmd.Parameters.AddWithValue("@section", $"%{sectionFilter}%");

        return await ReadSearchResultsAsync(cmd);
    }

    public async Task<List<SearchResult>> KeywordSearchAsync(
        string keyword,
        string? chunkType = null,
        int topK = 3)
    {
        var conn = await GetConnectionAsync();
        var whereClause = chunkType is not null ? "AND chunk_type = @chunk_type" : "";
        var sql = $"""
            SELECT id, source_type, source_path, chunk_type, section, category, description, content, 0.0 AS distance
            FROM chunks
            WHERE LOWER(content) LIKE LOWER(@keyword)
              {whereClause}
            LIMIT @topk
            """;

        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@keyword", $"%{keyword}%");
        cmd.Parameters.AddWithValue("@topk", topK);
        if (chunkType is not null) cmd.Parameters.AddWithValue("@chunk_type", chunkType);

        return await ReadSearchResultsAsync(cmd);
    }

    public async Task<Dictionary<string, int>> GetChunkCountsAsync()
    {
        var conn = await GetConnectionAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT source_type, COUNT(*) FROM chunks GROUP BY source_type";

        var counts = new Dictionary<string, int>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            counts[reader.GetString(0)] = reader.GetInt32(1);
        return counts;
    }

    public async Task<string?> GetLastIngestTimeAsync(string sourceType)
    {
        var conn = await GetConnectionAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(created_at) FROM chunks WHERE source_type = @st";
        cmd.Parameters.AddWithValue("@st", sourceType);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull or null ? null : (string)result;
    }

    // ── helpers ────────────────────────────────────────────────────

    private static async Task<List<SearchResult>> ReadSearchResultsAsync(SqliteCommand cmd)
    {
        const int MaxContentLength = 1500;
        var results = new List<SearchResult>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            // Column order matches SELECT: id(0), source_type(1), source_path(2), chunk_type(3),
            //   section(4), category(5), description(6), content(7), distance(8)
            var content = reader.GetString(7);
            if (content.Length > MaxContentLength)
                content = content[..MaxContentLength] + " [truncated]";

            results.Add(new SearchResult(
                Id: reader.GetString(0),
                SourceType: reader.GetString(1),
                SourcePath: reader.GetString(2),
                ChunkType: reader.GetString(3),
                Section: reader.IsDBNull(4) ? null : reader.GetString(4),
                Category: reader.IsDBNull(5) ? null : reader.GetString(5),
                Description: reader.IsDBNull(6) ? null : reader.GetString(6),
                Content: content,
                Score: reader.GetDouble(8)));
        }
        return results;
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection conn,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test RealTestMcp.sln --filter "FullyQualifiedName~VectorStoreServiceTests"
```

Expected: All 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat: add VectorStoreService with SQLite+sqlite-vec schema, upsert, search"
```

---

## Task 8: EmbeddingService

**Files:**
- Create: `src/RealTestMcp/Core/EmbeddingService.cs`
- Test: `tests/RealTestMcp.Tests/Core/EmbeddingServiceTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/RealTestMcp.Tests/Core/EmbeddingServiceTests.cs
using RealTestMcp.Core;

namespace RealTestMcp.Tests.Core;

public class EmbeddingServiceTests
{
    private readonly EmbeddingService _service = new();

    [Fact]
    public async Task EmbedAsync_Returns384DimensionVector()
    {
        var embedding = await _service.EmbedAsync("ATR function for average true range");
        Assert.Equal(384, embedding.Length);
    }

    [Fact]
    public async Task EmbedAsync_SimilarTexts_HaveHigherCosineSimilarity()
    {
        var e1 = await _service.EmbedAsync("entry setup for mean reversion strategy");
        var e2 = await _service.EmbedAsync("setup conditions for a mean reversion trade");
        var e3 = await _service.EmbedAsync("futures contract expiry date calculation");

        var sim12 = CosineSimilarity(e1, e2);
        var sim13 = CosineSimilarity(e1, e3);

        Assert.True(sim12 > sim13, $"Expected similar texts ({sim12:F3}) > dissimilar ({sim13:F3})");
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
```

- [ ] **Step 2: Run to verify tests fail**

```bash
dotnet test RealTestMcp.sln --filter "FullyQualifiedName~EmbeddingServiceTests"
```

Expected: FAIL — `EmbeddingService` not found.

- [ ] **Step 3: Implement EmbeddingService**

> **Note:** Check the `SmartComponents.LocalEmbeddings` NuGet README for the exact class name and API. The class is typically `LocalEmbedder` and resides in the `SmartComponents.LocalEmbeddings` namespace. The example below follows the standard pattern — verify against the installed version.

```csharp
// src/RealTestMcp/Core/EmbeddingService.cs
using SmartComponents.LocalEmbeddings;

namespace RealTestMcp.Core;

public class EmbeddingService : IDisposable
{
    private readonly LocalEmbedder _embedder = new();

    public Task<float[]> EmbedAsync(string text)
    {
        var embedding = _embedder.Embed(text);
        return Task.FromResult(embedding.Values.ToArray());
    }

    public async Task<float[][]> EmbedBatchAsync(IEnumerable<string> texts)
    {
        var results = new List<float[]>();
        foreach (var text in texts)
            results.Add(await EmbedAsync(text));
        return [.. results];
    }

    public void Dispose() => _embedder.Dispose();
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test RealTestMcp.sln --filter "FullyQualifiedName~EmbeddingServiceTests"
```

Expected: Both tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/RealTestMcp/Core/EmbeddingService.cs tests/
git commit -m "feat: add EmbeddingService wrapping SmartComponents.LocalEmbeddings"
```

---

## Task 9: Status Command (Full Implementation)

**Files:**
- Modify: `src/RealTestMcp/Program.cs`

Replace the status stub with the full implementation now that `VectorStoreService` exists.

- [ ] **Step 1: Replace status handler in Program.cs**

Replace the `statusCommand.SetHandler` block:

```csharp
statusCommand.SetHandler(async () =>
{
    var dbPath = settings.Database.Path;
    if (!File.Exists(dbPath))
    {
        Console.WriteLine("DB not initialized — run: realtest-mcp ingest docs");
        return;
    }

    await using var store = new VectorStoreService(dbPath);
    await store.EnsureSchemaAsync();

    var counts = await store.GetChunkCountsAsync();
    var dbSize = new FileInfo(dbPath).Length / 1024.0 / 1024.0;

    var docsLast    = await store.GetLastIngestTimeAsync("docs");
    var exampleLast = await store.GetLastIngestTimeAsync("example");
    var userLast    = await store.GetLastIngestTimeAsync("user_script");

    Console.WriteLine("RealTest MCP — Database Status");
    Console.WriteLine("================================");
    Console.WriteLine($"DB path:        {dbPath}");
    Console.WriteLine($"DB size:        {dbSize:F1} MB");
    Console.WriteLine();
    Console.WriteLine("Chunk counts:");
    Console.WriteLine($"  docs           {counts.GetValueOrDefault("docs"),6}");
    Console.WriteLine($"  example        {counts.GetValueOrDefault("example"),6}");
    Console.WriteLine($"  user_script    {counts.GetValueOrDefault("user_script"),6}");
    Console.WriteLine($"  {"─────────────────"}");
    Console.WriteLine($"  total          {counts.Values.Sum(),6}");
    Console.WriteLine();
    Console.WriteLine("Model:          all-MiniLM-L6-v2 (SmartComponents.LocalEmbeddings, bundled)");
    Console.WriteLine();
    Console.WriteLine("Last ingest:");
    Console.WriteLine($"  docs        {docsLast ?? "never"}");
    Console.WriteLine($"  scripts     {exampleLast ?? userLast ?? "never"}");
});
```

Also add the `VectorStoreService` using at top of Program.cs:
```csharp
using RealTestMcp.Core;
```

- [ ] **Step 2: Verify status output**

```bash
dotnet run --project src/RealTestMcp/RealTestMcp.csproj -- status
```

Expected: Either "DB not initialized" (if no DB exists) or the full status table.

- [ ] **Step 3: Commit**

```bash
git add src/RealTestMcp/Program.cs
git commit -m "feat: implement full status command with chunk counts and ingest timestamps"
```

---

## Task 10: Sample Test Data Files

**Files:**
- Create: `tests/RealTestMcp.Tests/data/docs/single-function.html`
- Create: `tests/RealTestMcp.Tests/data/docs/multi-function.html`
- Create: `tests/RealTestMcp.Tests/data/scripts/mean-reversion.rts`
- Create: `tests/RealTestMcp.Tests/data/scripts/futures-example.rts`

These files are realistic samples that mirror the actual RealTest content format. They are used by all parser/chunker/integration tests.

- [ ] **Step 1: Create single-function HTML sample**

```html
<!-- tests/RealTestMcp.Tests/data/docs/single-function.html -->
<!DOCTYPE html>
<html>
<head><title>ATR</title></head>
<body>
<h1>ATR</h1>
<p><b>Average True Range.</b> Measures volatility as the average of true ranges over a given period.</p>
<h2>Syntax</h2>
<pre>ATR(periods)</pre>
<h2>Parameters</h2>
<p><b>periods</b>: Integer. Number of bars to average. Common values: 10, 14, 20.</p>
<h2>Returns</h2>
<p>Float. The average true range value for the current bar.</p>
<h2>Example</h2>
<pre>
Strategy MyStrategy:
  ExitStop: Entry - ATR(14) * 2
</pre>
<h2>Notes</h2>
<p>ATR is commonly used for stop placement and position sizing. It adapts to changing market volatility.</p>
</body>
</html>
```

- [ ] **Step 2: Create multi-function HTML sample**

```html
<!-- tests/RealTestMcp.Tests/data/docs/multi-function.html -->
<!DOCTYPE html>
<html>
<head><title>Highest and Lowest Functions</title></head>
<body>
<h1>Highest and Lowest</h1>
<p>These functions return the highest or lowest value of a series over a lookback period.</p>

<h2>Highest</h2>
<h3>Syntax</h3>
<pre>Highest(series, periods)</pre>
<h3>Parameters</h3>
<p><b>series</b>: The data series to evaluate (e.g. H for high prices).</p>
<p><b>periods</b>: Integer lookback period.</p>
<h3>Example</h3>
<pre>
// Donchian channel breakout
EntrySetup: C > Highest(H, 20)[1]
</pre>

<h2>Lowest</h2>
<h3>Syntax</h3>
<pre>Lowest(series, periods)</pre>
<h3>Parameters</h3>
<p><b>series</b>: The data series to evaluate (e.g. L for low prices).</p>
<p><b>periods</b>: Integer lookback period.</p>
<h3>Example</h3>
<pre>
// Long entry at lowest low
EntrySetup: C = Lowest(L, 10)
</pre>
</body>
</html>
```

- [ ] **Step 3: Create mean-reversion RTS sample**

```
// tests/RealTestMcp.Tests/data/scripts/mean-reversion.rts
// Simple Mean Reversion Strategy using RSI(2)
// Category: Mean Reversion

Settings:
  StartDate: 2010-01-01
  EndDate: 2023-12-31
  InitialCapital: 100000
  Commission: 0.005

Strategy MeanReversion:
  MaxPositions: 10
  PositionSize: 10%

  EntrySetup: RSI(2) < 10
  Entry: Open

  ExitRule: RSI(2) > 90
  ExitStop: Entry * 0.92

  SetupScore: -RSI(2)
```

- [ ] **Step 4: Create futures RTS sample**

```
// tests/RealTestMcp.Tests/data/scripts/futures-example.rts
// Trend Following on Futures using ATR-based stops
// Category: Futures

Settings:
  StartDate: 2015-01-01
  EndDate: 2023-12-31
  InitialCapital: 250000

Strategy TrendFollowing:
  MaxPositions: 5

  EntrySetup: C > Highest(H, 50)[1]
  Entry: Open

  ExitStop: Highest(H, 50)[1] - ATR(14) * 2
  ExitRule: C < Avg(C, 20)

  Quantity: 1
  QtyType: Contracts
```

- [ ] **Step 5: Configure test data files to copy to output**

Add to `tests/RealTestMcp.Tests/RealTestMcp.Tests.csproj`:

```xml
<ItemGroup>
  <Content Include="data\**\*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

- [ ] **Step 6: Commit**

```bash
git add tests/RealTestMcp.Tests/data/
git commit -m "test: add sample CHM HTML and RTS files for parser and integration tests"
```

---

## Task 11: ChmParser

**Files:**
- Create: `src/RealTestMcp/Ingestion/Parsers/ChmParser.cs`
- Test: `tests/RealTestMcp.Tests/Parsers/ChmParserTests.cs`

The CHM file must be extracted to a directory of HTML files before parsing. Users run `hh.exe -decompile <output_dir> <file.chm>` once after installing RealTest. `ChmParser` then operates on the extracted HTML directory.

- [ ] **Step 1: Write failing tests**

```csharp
// tests/RealTestMcp.Tests/Parsers/ChmParserTests.cs
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Tests.Parsers;

public class ChmParserTests
{
    private static string DataDir => Path.Combine(
        AppContext.BaseDirectory, "data", "docs");

    [Fact]
    public void ParseDirectory_FindsHtmlFiles()
    {
        var pages = ChmParser.ParseDirectory(DataDir);
        Assert.True(pages.Count >= 2);
    }

    [Fact]
    public void ParseDirectory_ExtractsTitle()
    {
        var pages = ChmParser.ParseDirectory(DataDir);
        var atr = pages.FirstOrDefault(p => p.Title.Contains("ATR", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(atr);
    }

    [Fact]
    public void ParseDirectory_ExtractsBodyText()
    {
        var pages = ChmParser.ParseDirectory(DataDir);
        var atr = pages.First(p => p.Title.Contains("ATR", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Average True Range", atr.BodyText);
        Assert.Contains("ATR(periods)", atr.BodyText);
    }

    [Fact]
    public void ParseDirectory_StripsTags()
    {
        var pages = ChmParser.ParseDirectory(DataDir);
        var atr = pages.First(p => p.Title.Contains("ATR", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("<", atr.BodyText);
    }
}
```

- [ ] **Step 2: Run to verify tests fail**

```bash
dotnet test RealTestMcp.sln --filter "FullyQualifiedName~ChmParserTests"
```

Expected: FAIL — type not found.

- [ ] **Step 3: Implement ChmParser**

```csharp
// src/RealTestMcp/Ingestion/Parsers/ChmParser.cs
using HtmlAgilityPack;

namespace RealTestMcp.Ingestion.Parsers;

public record HtmlPage(string FilePath, string Title, string BodyText, string RawHtml);

public static class ChmParser
{
    public static List<HtmlPage> ParseDirectory(string docsPath)
    {
        if (!Directory.Exists(docsPath))
            throw new DirectoryNotFoundException($"Docs path not found: {docsPath}");

        var pages = new List<HtmlPage>();
        foreach (var file in Directory.EnumerateFiles(docsPath, "*.htm", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(docsPath, "*.html", SearchOption.AllDirectories)))
        {
            try
            {
                var page = ParseFile(file);
                if (!string.IsNullOrWhiteSpace(page.BodyText))
                    pages.Add(page);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ChmParser] Skipping {file}: {ex.Message}");
            }
        }
        return pages;
    }

    public static HtmlPage ParseFile(string filePath)
    {
        var rawHtml = File.ReadAllText(filePath);   // read once; passed through to DocChunker
        var doc = new HtmlDocument();
        doc.LoadHtml(rawHtml);

        var title = doc.DocumentNode
            .SelectSingleNode("//title")?.InnerText.Trim()
            ?? Path.GetFileNameWithoutExtension(filePath);

        // Extract text from body, normalizing whitespace
        var body = doc.DocumentNode.SelectSingleNode("//body");
        var bodyText = body is null
            ? string.Empty
            : NormalizeWhitespace(HtmlEntity.DeEntitize(body.InnerText));

        return new HtmlPage(filePath, HtmlEntity.DeEntitize(title), bodyText, rawHtml);
    }

    private static string NormalizeWhitespace(string text)
    {
        // Collapse runs of whitespace/newlines into single spaces, then trim
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test RealTestMcp.sln --filter "FullyQualifiedName~ChmParserTests"
```

Expected: All 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/RealTestMcp/Ingestion/Parsers/ChmParser.cs tests/RealTestMcp.Tests/Parsers/ChmParserTests.cs
git commit -m "feat: add ChmParser for extracting text from HTML doc pages"
```

---

## Task 12: DocChunker

**Files:**
- Create: `src/RealTestMcp/Ingestion/Chunkers/DocChunker.cs`
- Test: `tests/RealTestMcp.Tests/Chunkers/DocChunkerTests.cs`

> **Open Item (from spec):** The `function_entry` detection strategy depends on the actual CHM structure. This task implements a heuristic: a page contains multiple function entries if its title suggests a grouping (e.g. "Highest and Lowest") AND it contains multiple `<h2>` headings each followed by a `Syntax` `<h3>`. This heuristic should be validated against the real CHM before finalizing. The test uses the `multi-function.html` sample which is known to trigger splitting.

- [ ] **Step 1: Write failing tests**

```csharp
// tests/RealTestMcp.Tests/Chunkers/DocChunkerTests.cs
using RealTestMcp.Ingestion.Chunkers;
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Tests.Chunkers;

public class DocChunkerTests
{
    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "data", "docs");

    [Fact]
    public void SingleFunctionPage_ProducesOnePageChunk()
    {
        var page = ChmParser.ParseFile(Path.Combine(DataDir, "single-function.html"));
        var chunks = DocChunker.Chunk(page);

        Assert.Single(chunks);
        Assert.Equal("page", chunks[0].ChunkType);
        Assert.Equal("docs", chunks[0].SourceType);
    }

    [Fact]
    public void SingleFunctionPage_ChunkContainsTitle()
    {
        var page = ChmParser.ParseFile(Path.Combine(DataDir, "single-function.html"));
        var chunks = DocChunker.Chunk(page);

        Assert.Contains("ATR", chunks[0].Content);
    }

    [Fact]
    public void MultiFunctionPage_ProducesFunctionEntryChunks()
    {
        var page = ChmParser.ParseFile(Path.Combine(DataDir, "multi-function.html"));
        var chunks = DocChunker.Chunk(page);

        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, c => Assert.Equal("function_entry", c.ChunkType));
    }

    [Fact]
    public void MultiFunctionPage_EachChunkContainsFunctionName()
    {
        var page = ChmParser.ParseFile(Path.Combine(DataDir, "multi-function.html"));
        var chunks = DocChunker.Chunk(page);

        Assert.Contains(chunks, c => c.Content.Contains("Highest"));
        Assert.Contains(chunks, c => c.Content.Contains("Lowest"));
    }

    [Fact]
    public void ChunkIds_AreDeterministic()
    {
        var page = ChmParser.ParseFile(Path.Combine(DataDir, "single-function.html"));
        var chunks1 = DocChunker.Chunk(page);
        var chunks2 = DocChunker.Chunk(page);

        Assert.Equal(chunks1[0].Id, chunks2[0].Id);
    }
}
```

- [ ] **Step 2: Run to verify tests fail**

```bash
dotnet test RealTestMcp.sln --filter "FullyQualifiedName~DocChunkerTests"
```

Expected: FAIL — type not found.

- [ ] **Step 3: Implement DocChunker**

```csharp
// src/RealTestMcp/Ingestion/Chunkers/DocChunker.cs
using HtmlAgilityPack;
using RealTestMcp.Core;
using RealTestMcp.Core.Models;
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Ingestion.Chunkers;

public static class DocChunker
{
    public static List<Chunk> Chunk(HtmlPage page)
    {
        // Parse from the already-loaded RawHtml — avoids re-reading the file from disk
        var doc = new HtmlDocument();
        doc.LoadHtml(page.RawHtml);

        // Detect multi-function page: has multiple <h2> elements each preceded by function-like content
        var h2Nodes = doc.DocumentNode.SelectNodes("//body//h2");
        if (h2Nodes is { Count: >= 2 } && HasMultipleSyntaxSections(doc))
            return ChunkByFunctionEntry(page, doc, h2Nodes);

        return [PageChunk(page)];
    }

    private static bool HasMultipleSyntaxSections(HtmlDocument doc)
    {
        // Look for multiple <h3> nodes containing "Syntax"
        var h3Nodes = doc.DocumentNode.SelectNodes("//body//h3");
        if (h3Nodes is null) return false;
        var syntaxCount = h3Nodes.Count(n =>
            n.InnerText.Contains("Syntax", StringComparison.OrdinalIgnoreCase));
        return syntaxCount >= 2;
    }

    private static List<Chunk> ChunkByFunctionEntry(HtmlPage page, HtmlDocument doc, HtmlNodeCollection h2Nodes)
    {
        var chunks = new List<Chunk>();
        for (int i = 0; i < h2Nodes.Count; i++)
        {
            var h2 = h2Nodes[i];
            var functionName = HtmlEntity.DeEntitize(h2.InnerText.Trim());

            // Collect all sibling nodes until the next h2
            var contentNodes = new List<HtmlNode>();
            var current = h2.NextSibling;
            while (current != null && current.Name.ToLower() != "h2")
            {
                contentNodes.Add(current);
                current = current.NextSibling;
            }

            var content = $"{functionName}\n" +
                NormalizeWhitespace(
                    HtmlEntity.DeEntitize(
                        string.Concat(contentNodes.Select(n => n.InnerText))));

            if (string.IsNullOrWhiteSpace(content)) continue;

            var id = VectorStoreService.ComputeChunkId(page.FilePath, i);
            chunks.Add(new Chunk(
                Id: id,
                SourceType: "docs",
                SourcePath: page.FilePath,
                ChunkType: "function_entry",
                Section: InferSection(page.FilePath),
                Category: null,
                Description: null,
                Content: content,
                ChunkIndex: i,
                CreatedAt: DateTime.UtcNow));
        }
        return chunks;
    }

    private static Chunk PageChunk(HtmlPage page)
    {
        var content = $"{page.Title}\n{page.BodyText}";
        var id = VectorStoreService.ComputeChunkId(page.FilePath, 0);
        return new Chunk(
            Id: id,
            SourceType: "docs",
            SourcePath: page.FilePath,
            ChunkType: "page",
            Section: InferSection(page.FilePath),
            Category: null,
            Description: null,
            Content: content,
            ChunkIndex: 0,
            CreatedAt: DateTime.UtcNow);
    }

    private static string? InferSection(string filePath)
    {
        // Best-effort: infer section from directory name
        var dir = Path.GetFileName(Path.GetDirectoryName(filePath));
        return string.IsNullOrWhiteSpace(dir) ? null : dir;
    }

    private static string NormalizeWhitespace(string text)
        => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test RealTestMcp.sln --filter "FullyQualifiedName~DocChunkerTests"
```

Expected: All 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/RealTestMcp/Ingestion/Chunkers/DocChunker.cs tests/RealTestMcp.Tests/Chunkers/DocChunkerTests.cs
git commit -m "feat: add DocChunker with page and function_entry splitting"
```

---

## Task 13: IngestDocsCommand

**Files:**
- Create: `src/RealTestMcp/Ingestion/Commands/IngestDocsCommand.cs`
- Modify: `src/RealTestMcp/Program.cs`

- [ ] **Step 1: Implement IngestDocsCommand**

```csharp
// src/RealTestMcp/Ingestion/Commands/IngestDocsCommand.cs
using RealTestMcp.Core;
using RealTestMcp.Core.Configuration;
using RealTestMcp.Core.Models;
using RealTestMcp.Ingestion.Chunkers;
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Ingestion.Commands;

public static class IngestDocsCommand
{
    public static async Task RunAsync(AppSettings settings)
    {
        var docsPath = settings.RealTest.DocsPath;
        if (!Directory.Exists(docsPath))
        {
            Console.Error.WriteLine($"Docs path not found: {docsPath}");
            Console.Error.WriteLine("Extract your CHM file first: hh.exe -decompile <output_dir> <file.chm>");
            return;
        }

        Console.WriteLine($"Parsing HTML files from: {docsPath}");
        var pages = ChmParser.ParseDirectory(docsPath);
        Console.WriteLine($"Found {pages.Count} pages");

        var allChunks = pages.SelectMany(DocChunker.Chunk).ToList();
        Console.WriteLine($"Produced {allChunks.Count} chunks");

        await using var store = new VectorStoreService(settings.Database.Path);
        await store.EnsureSchemaAsync();
        using var embedder = new EmbeddingService();

        Console.WriteLine("Clearing existing docs chunks...");
        await store.DeleteBySourceTypeAsync("docs");

        Console.WriteLine("Embedding and storing chunks...");
        int count = 0;
        foreach (var chunk in allChunks)
        {
            try
            {
                var embedding = await embedder.EmbedAsync(chunk.Content);
                // Stamp with current time for last-ingest tracking
                var stamped = chunk with { CreatedAt = DateTime.UtcNow };
                await store.UpsertChunkAsync(stamped, embedding);
                count++;
                if (count % 50 == 0) Console.Write($"\r  {count}/{allChunks.Count}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"\n[Warning] Failed to process {chunk.SourcePath}: {ex.Message}");
            }
        }

        Console.WriteLine($"\nDone. Ingested {count} docs chunks.");
    }
}
```

- [ ] **Step 2: Wire into Program.cs**

Replace the `ingestDocsCommand.SetHandler` stub:

```csharp
ingestDocsCommand.SetHandler(async () =>
    await IngestDocsCommand.RunAsync(settings));
```

Add using:
```csharp
using RealTestMcp.Ingestion.Commands;
```

- [ ] **Step 3: Test against real CHM docs (manual)**

If you have your RealTest install available:

```bash
# First extract the CHM (run once)
hh.exe -decompile C:\RealTest\Help C:\RealTest\RealTest.chm

# Then run ingestion
dotnet run --project src/RealTestMcp/RealTestMcp.csproj -- ingest docs

# Verify with status
dotnet run --project src/RealTestMcp/RealTestMcp.csproj -- status
```

Expected: `status` shows non-zero `docs` chunk count.

- [ ] **Step 4: Commit**

```bash
git add src/RealTestMcp/Ingestion/Commands/IngestDocsCommand.cs src/RealTestMcp/Program.cs
git commit -m "feat: implement ingest docs command with CHM parsing and embedding"
```

---

## Task 14: RtsParser and ScriptChunker

**Files:**
- Create: `src/RealTestMcp/Ingestion/Parsers/RtsParser.cs`
- Create: `src/RealTestMcp/Ingestion/Chunkers/ScriptChunker.cs`
- Test: `tests/RealTestMcp.Tests/Parsers/RtsParserTests.cs`
- Test: `tests/RealTestMcp.Tests/Chunkers/ScriptChunkerTests.cs`

- [ ] **Step 1: Write failing parser tests**

```csharp
// tests/RealTestMcp.Tests/Parsers/RtsParserTests.cs
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Tests.Parsers;

public class RtsParserTests
{
    private static string ScriptsDir => Path.Combine(AppContext.BaseDirectory, "data", "scripts");

    [Fact]
    public void ParseFile_ReturnsContent()
    {
        var result = RtsParser.ParseFile(Path.Combine(ScriptsDir, "mean-reversion.rts"));
        Assert.Contains("RSI", result.Content);
    }

    [Fact]
    public void ParseFile_FilePath_IsAbsolute()
    {
        var result = RtsParser.ParseFile(Path.Combine(ScriptsDir, "mean-reversion.rts"));
        Assert.True(Path.IsPathRooted(result.FilePath));
    }
}
```

- [ ] **Step 2: Write failing chunker tests**

```csharp
// tests/RealTestMcp.Tests/Chunkers/ScriptChunkerTests.cs
using RealTestMcp.Ingestion.Chunkers;
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Tests.Chunkers;

public class ScriptChunkerTests
{
    private static string ScriptsDir => Path.Combine(AppContext.BaseDirectory, "data", "scripts");

    [Fact]
    public void Chunk_ProducesOneChunkPerFile()
    {
        var script = RtsParser.ParseFile(Path.Combine(ScriptsDir, "mean-reversion.rts"));
        var chunks = ScriptChunker.Chunk(script, sourceType: "example");

        Assert.Single(chunks);
    }

    [Fact]
    public void Chunk_HasCorrectSourceType()
    {
        var script = RtsParser.ParseFile(Path.Combine(ScriptsDir, "mean-reversion.rts"));
        var example = ScriptChunker.Chunk(script, "example");
        var user = ScriptChunker.Chunk(script, "user_script");

        Assert.Equal("example", example[0].SourceType);
        Assert.Equal("user_script", user[0].SourceType);
    }

    [Fact]
    public void Chunk_AttachesCategory_WhenProvided()
    {
        var script = RtsParser.ParseFile(Path.Combine(ScriptsDir, "mean-reversion.rts"));
        var catalog = new Dictionary<string, (string Category, string Description)>
        {
            ["mean-reversion"] = ("Mean Reversion", "A simple RSI(2) mean reversion system")
        };

        var chunks = ScriptChunker.Chunk(script, "example", catalog);

        Assert.Equal("Mean Reversion", chunks[0].Category);
        Assert.Contains("RSI(2)", chunks[0].Description);
    }

    [Fact]
    public void ChunkIds_AreDeterministic()
    {
        var script = RtsParser.ParseFile(Path.Combine(ScriptsDir, "futures-example.rts"));
        var c1 = ScriptChunker.Chunk(script, "example");
        var c2 = ScriptChunker.Chunk(script, "example");

        Assert.Equal(c1[0].Id, c2[0].Id);
    }
}
```

- [ ] **Step 3: Run to verify tests fail**

```bash
dotnet test RealTestMcp.sln --filter "FullyQualifiedName~RtsParserTests|FullyQualifiedName~ScriptChunkerTests"
```

Expected: FAIL — types not found.

- [ ] **Step 4: Implement RtsParser**

```csharp
// src/RealTestMcp/Ingestion/Parsers/RtsParser.cs
namespace RealTestMcp.Ingestion.Parsers;

public record RtsFile(string FilePath, string Content);

public static class RtsParser
{
    public static RtsFile ParseFile(string filePath)
    {
        var content = File.ReadAllText(filePath);
        return new RtsFile(Path.GetFullPath(filePath), content);
    }

    public static IEnumerable<RtsFile> ParseDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            yield break;

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*.rts", SearchOption.AllDirectories))
        {
            RtsFile? result = null;
            try { result = ParseFile(file); }
            catch (Exception ex) { Console.Error.WriteLine($"[RtsParser] Skipping {file}: {ex.Message}"); }
            if (result is not null) yield return result;
        }
    }
}
```

- [ ] **Step 5: Implement ScriptChunker**

```csharp
// src/RealTestMcp/Ingestion/Chunkers/ScriptChunker.cs
using RealTestMcp.Core;
using RealTestMcp.Core.Models;
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Ingestion.Chunkers;

public static class ScriptChunker
{
    public static List<Chunk> Chunk(
        RtsFile script,
        string sourceType,
        Dictionary<string, (string Category, string Description)>? catalog = null)
    {
        var fileName = Path.GetFileNameWithoutExtension(script.FilePath);
        var id = VectorStoreService.ComputeChunkId(script.FilePath, 0);

        string? category = null;
        string? description = null;

        if (catalog is not null && catalog.TryGetValue(fileName, out var meta))
        {
            category = meta.Category;
            description = meta.Description;
        }

        return
        [
            new Chunk(
                Id: id,
                SourceType: sourceType,
                SourcePath: script.FilePath,
                ChunkType: "script",
                Section: null,
                Category: category,
                Description: description,
                Content: script.Content,
                ChunkIndex: 0,
                CreatedAt: DateTime.UtcNow)
        ];
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test RealTestMcp.sln --filter "FullyQualifiedName~RtsParserTests|FullyQualifiedName~ScriptChunkerTests"
```

Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/RealTestMcp/Ingestion/Parsers/RtsParser.cs src/RealTestMcp/Ingestion/Chunkers/ScriptChunker.cs tests/
git commit -m "feat: add RtsParser and ScriptChunker"
```

---

## Task 15: IngestScriptsCommand

**Files:**
- Create: `src/RealTestMcp/Ingestion/Commands/IngestScriptsCommand.cs`
- Modify: `src/RealTestMcp/Program.cs`

- [ ] **Step 1: Implement IngestScriptsCommand**

```csharp
// src/RealTestMcp/Ingestion/Commands/IngestScriptsCommand.cs
using RealTestMcp.Core;
using RealTestMcp.Core.Configuration;
using RealTestMcp.Ingestion.Chunkers;
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Ingestion.Commands;

public static class IngestScriptsCommand
{
    public static async Task RunAsync(AppSettings settings)
    {
        var scriptPaths = settings.RealTest.ScriptPaths;

        await using var store = new VectorStoreService(settings.Database.Path);
        await store.EnsureSchemaAsync();
        using var embedder = new EmbeddingService();

        Console.WriteLine("Clearing all existing script chunks...");
        await store.DeleteBySourceTypesAsync(["example", "user_script"]);

        // Convention: the FIRST entry in ScriptPaths is the official RealTest examples directory
        // (source_type=example). All subsequent entries are user scripts (source_type=user_script).
        // Users must keep the RealTest examples path as the first entry in appsettings.json.
        // This convention is documented in README.md under "ScriptPaths".
        int total = 0;
        for (int i = 0; i < scriptPaths.Length; i++)
        {
            var path = scriptPaths[i];
            var sourceType = i == 0 ? "example" : "user_script";

            if (!Directory.Exists(path))
            {
                Console.Error.WriteLine($"[Warning] Script path not found, skipping: {path}");
                continue;
            }

            Console.WriteLine($"Ingesting {sourceType} scripts from: {path}");
            var scripts = RtsParser.ParseDirectory(path).ToList();
            Console.WriteLine($"  Found {scripts.Count} .rts files");

            foreach (var script in scripts)
            {
                try
                {
                    var chunks = ScriptChunker.Chunk(script, sourceType);
                    foreach (var chunk in chunks)
                    {
                        var embedding = await embedder.EmbedAsync(chunk.Content);
                        var stamped = chunk with { CreatedAt = DateTime.UtcNow };
                        await store.UpsertChunkAsync(stamped, embedding);
                        total++;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Warning] Failed to process {script.FilePath}: {ex.Message}");
                }
            }
        }

        Console.WriteLine($"Done. Ingested {total} script chunks.");
    }
}
```

- [ ] **Step 2: Wire into Program.cs**

Replace `ingestScriptsCommand.SetHandler` stub:

```csharp
ingestScriptsCommand.SetHandler(async () =>
    await IngestScriptsCommand.RunAsync(settings));
```

- [ ] **Step 3: Manual test against real scripts**

```bash
dotnet run --project src/RealTestMcp/RealTestMcp.csproj -- ingest scripts
dotnet run --project src/RealTestMcp/RealTestMcp.csproj -- status
```

Expected: `status` shows non-zero `example` chunk count.

- [ ] **Step 4: Commit**

```bash
git add src/RealTestMcp/Ingestion/Commands/IngestScriptsCommand.cs src/RealTestMcp/Program.cs
git commit -m "feat: implement ingest scripts command with multi-path support"
```

---

## Task 16: Integration Tests (Ingest + Search Round-Trip)

**Files:**
- Create: `tests/RealTestMcp.Tests/Integration/IngestSearchTests.cs`

These tests use real embeddings (no mocks needed — SmartComponents bundles the model).

- [ ] **Step 1: Write integration tests**

```csharp
// tests/RealTestMcp.Tests/Integration/IngestSearchTests.cs
using RealTestMcp.Core;
using RealTestMcp.Ingestion.Chunkers;
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Tests.Integration;

public class IngestSearchTests : IAsyncLifetime
{
    private VectorStoreService _store = null!;
    private EmbeddingService _embedder = null!;
    private string _dbPath = null!;

    private static string DocsDir    => Path.Combine(AppContext.BaseDirectory, "data", "docs");
    private static string ScriptsDir => Path.Combine(AppContext.BaseDirectory, "data", "scripts");

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");
        _store = new VectorStoreService(_dbPath);
        await _store.EnsureSchemaAsync();
        _embedder = new EmbeddingService();
        await IngestSampleDataAsync();
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        _embedder.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    // ── search_docs ───────────────────────────────────────────────

    [Fact]
    public async Task SearchDocs_ReturnsRelevantDocChunk()
    {
        var queryEmbedding = await _embedder.EmbedAsync("volatility measurement average true range");
        var results = await _store.VectorSearchAsync(queryEmbedding, sourceType: "docs", topK: 5);

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Content.Contains("ATR", StringComparison.OrdinalIgnoreCase));
    }

    // ── get_function_reference (keyword path) ─────────────────────

    [Fact]
    public async Task GetFunctionReference_KeywordPath_FindsATR()
    {
        var results = await _store.KeywordSearchAsync("ATR", chunkType: "function_entry", topK: 3);

        // If no function_entry chunks exist (single-page CHM structure), fall back to page search
        if (results.Count == 0)
            results = await _store.KeywordSearchAsync("ATR", chunkType: null, topK: 3);

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Content.Contains("ATR"));
    }

    // ── get_function_reference (semantic fallback) ────────────────

    [Fact]
    public async Task GetFunctionReference_SemanticFallback_FindsFunction()
    {
        // Search for something that won't match keyword but should match semantically
        var keywordResults = await _store.KeywordSearchAsync("ZZZNOMATCH", chunkType: "function_entry", topK: 3);
        Assert.Empty(keywordResults); // confirm keyword misses

        var queryEmbedding = await _embedder.EmbedAsync("highest value over lookback period");
        var semanticResults = await _store.VectorSearchAsync(queryEmbedding, sourceType: "docs", topK: 3);
        Assert.NotEmpty(semanticResults);
    }

    // ── search_examples ───────────────────────────────────────────

    [Fact]
    public async Task SearchExamples_ReturnsScriptChunk()
    {
        var queryEmbedding = await _embedder.EmbedAsync("RSI mean reversion entry");
        var results = await _store.VectorSearchAsync(queryEmbedding, sourceType: "example", topK: 3);

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal("example", r.SourceType));
    }

    [Fact]
    public async Task SearchExamples_CategoryFilter_ReturnsOnlyMatchingCategory()
    {
        var queryEmbedding = await _embedder.EmbedAsync("futures trend following");
        var allResults = await _store.VectorSearchAsync(queryEmbedding, sourceType: "example", topK: 10);
        var filteredResults = await _store.VectorSearchAsync(queryEmbedding, sourceType: "example",
            categoryFilter: "Mean Reversion", topK: 10);

        // Filtered results must only contain Mean Reversion chunks
        Assert.All(filteredResults, r =>
            Assert.Equal("Mean Reversion", r.Category, StringComparer.OrdinalIgnoreCase));
    }

    // ── helpers ───────────────────────────────────────────────────

    private async Task IngestSampleDataAsync()
    {
        // Ingest docs
        foreach (var file in Directory.GetFiles(DocsDir, "*.html"))
        {
            var page = ChmParser.ParseFile(file);
            foreach (var chunk in DocChunker.Chunk(page))
            {
                var embedding = await _embedder.EmbedAsync(chunk.Content);
                await _store.UpsertChunkAsync(chunk, embedding);
            }
        }

        // Ingest scripts with category metadata
        var catalog = new Dictionary<string, (string, string)>
        {
            ["mean-reversion"] = ("Mean Reversion", "RSI(2) mean reversion system"),
            ["futures-example"] = ("Futures", "ATR-based trend following on futures"),
        };

        foreach (var file in Directory.GetFiles(ScriptsDir, "*.rts"))
        {
            var script = RtsParser.ParseFile(file);
            foreach (var chunk in ScriptChunker.Chunk(script, "example", catalog))
            {
                var embedding = await _embedder.EmbedAsync(chunk.Content);
                await _store.UpsertChunkAsync(chunk, embedding);
            }
        }
    }
}
```

- [ ] **Step 2: Run to verify tests fail**

```bash
dotnet test RealTestMcp.sln --filter "FullyQualifiedName~IngestSearchTests" --verbosity normal
```

Expected: Tests compile but may fail if `IngestSampleDataAsync` or `VectorSearchAsync` has a bug. Fix any failures before proceeding.

- [ ] **Step 3: Add command-level tests for IngestDocsCommand and IngestScriptsCommand**

Add to `IngestSearchTests.cs` after the existing test methods:

```csharp
    // ── IngestDocsCommand ─────────────────────────────────────────

    [Fact]
    public async Task IngestDocsCommand_PopulatesDocsChunks()
    {
        // Use a fresh store to verify the command itself
        var freshDbPath = Path.Combine(Path.GetTempPath(), $"cmd_{Guid.NewGuid()}.db");
        await using var freshStore = new VectorStoreService(freshDbPath);
        await freshStore.EnsureSchemaAsync();

        var settings = new AppSettings();
        settings.Database.Path = freshDbPath;
        settings.RealTest.DocsPath = DocsDir;

        await IngestDocsCommand.RunAsync(settings);

        var counts = await freshStore.GetChunkCountsAsync();
        Assert.True(counts.GetValueOrDefault("docs") > 0, "Expected docs chunks after IngestDocsCommand");

        if (File.Exists(freshDbPath)) File.Delete(freshDbPath);
    }

    [Fact]
    public async Task IngestScriptsCommand_PopulatesExampleChunks()
    {
        var freshDbPath = Path.Combine(Path.GetTempPath(), $"cmd_{Guid.NewGuid()}.db");
        await using var freshStore = new VectorStoreService(freshDbPath);
        await freshStore.EnsureSchemaAsync();

        var settings = new AppSettings();
        settings.Database.Path = freshDbPath;
        settings.RealTest.ScriptPaths = [ScriptsDir];

        await IngestScriptsCommand.RunAsync(settings);

        var counts = await freshStore.GetChunkCountsAsync();
        Assert.True(counts.GetValueOrDefault("example") > 0, "Expected example chunks after IngestScriptsCommand");

        if (File.Exists(freshDbPath)) File.Delete(freshDbPath);
    }
```

Also add the missing using at top of the test file:
```csharp
using RealTestMcp.Ingestion.Commands;
```

- [ ] **Step 4: Run all integration tests to verify they pass**

```bash
dotnet test RealTestMcp.sln --filter "FullyQualifiedName~IngestSearchTests" --verbosity normal
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add tests/RealTestMcp.Tests/Integration/
git commit -m "test: add ingest+search integration tests covering all search paths and commands"
```

---

## Task 17: MCP Tools — Full Implementation

**Files:**
- Modify: `src/RealTestMcp/Tools/SearchDocsTool.cs`
- Modify: `src/RealTestMcp/Tools/GetFunctionReferenceTool.cs`
- Modify: `src/RealTestMcp/Tools/SearchExamplesTool.cs`
- Modify: `src/RealTestMcp/Tools/SearchUserScriptsTool.cs`
- Modify: `src/RealTestMcp/Program.cs` (inject services into tools)

Tools need `VectorStoreService` and `EmbeddingService`. Inject via DI using the MCP SDK's service-based tool pattern.

- [ ] **Step 1: Update Program.cs to register services in DI**

In the MCP server host setup, add services:

```csharp
rootCommand.SetHandler(async () =>
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services
        .AddSingleton(settings)
        .AddSingleton(new VectorStoreService(settings.Database.Path))
        .AddSingleton<EmbeddingService>()
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly(typeof(SearchDocsTool).Assembly);

    var host = builder.Build();

    // Ensure schema exists before handling any requests
    var store = host.Services.GetRequiredService<VectorStoreService>();
    await store.EnsureSchemaAsync();

    await host.RunAsync();
});
```

- [ ] **Step 2: Implement SearchDocsTool**

```csharp
// src/RealTestMcp/Tools/SearchDocsTool.cs
using ModelContextProtocol.Server;
using RealTestMcp.Core;
using System.ComponentModel;
using System.Text;

namespace RealTestMcp.Tools;

[McpServerToolType]
public class SearchDocsTool(VectorStoreService store, EmbeddingService embedder)
{
    [McpServerTool, Description("Search RealTest documentation by concept or topic")]
    public async Task<string> SearchDocs(
        [Description("What to search for")] string query,
        [Description("Optional: limit to a doc section (e.g. 'Strategy', 'Import')")] string? sectionFilter = null,
        [Description("Number of results to return (default: 5)")] int topK = 5)
    {
        var queryEmbedding = await embedder.EmbedAsync(query);
        // sectionFilter is applied at the DB level inside VectorSearchAsync, not post-filtered.
        // Post-filtering on already-truncated topK results would incorrectly return empty
        // when matching chunks exist beyond the initial result set.
        var results = await store.VectorSearchAsync(queryEmbedding, sourceType: "docs",
            categoryFilter: null, sectionFilter: sectionFilter, topK: topK);

        if (results.Count == 0)
            return "No documentation found for that query. Try different search terms.";

        var sb = new StringBuilder();
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"## Result {i + 1}");
            if (r.Section is not null) sb.AppendLine($"Section: {r.Section}");
            sb.AppendLine();
            sb.AppendLine(r.Content);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 3: Implement GetFunctionReferenceTool**

```csharp
// src/RealTestMcp/Tools/GetFunctionReferenceTool.cs
using ModelContextProtocol.Server;
using RealTestMcp.Core;
using System.ComponentModel;
using System.Text;

namespace RealTestMcp.Tools;

[McpServerToolType]
public class GetFunctionReferenceTool(VectorStoreService store, EmbeddingService embedder)
{
    [McpServerTool, Description("Get the exact function signature and description for a RealScript function. Call this before using any function in generated code.")]
    public async Task<string> GetFunctionReference(
        [Description("Function name to look up (e.g. 'ATR', 'Lowest', 'RSI')")] string functionName)
    {
        // Step 1: keyword search against function_entry chunks
        var results = await store.KeywordSearchAsync(functionName, chunkType: "function_entry", topK: 3);

        // Step 2: semantic fallback across all docs if keyword found nothing
        if (results.Count == 0)
        {
            var queryEmbedding = await embedder.EmbedAsync(functionName);
            results = await store.VectorSearchAsync(queryEmbedding, sourceType: "docs", topK: 3);
        }

        if (results.Count == 0)
            return $"No reference found for '{functionName}'. Run 'realtest-mcp ingest docs' if the database is empty.";

        var sb = new StringBuilder();
        sb.AppendLine($"# Function Reference: {functionName}");
        sb.AppendLine();
        foreach (var r in results)
        {
            sb.AppendLine(r.Content);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Implement SearchExamplesTool**

```csharp
// src/RealTestMcp/Tools/SearchExamplesTool.cs
using ModelContextProtocol.Server;
using RealTestMcp.Core;
using System.ComponentModel;
using System.Text;

namespace RealTestMcp.Tools;

[McpServerToolType]
public class SearchExamplesTool(VectorStoreService store, EmbeddingService embedder)
{
    [McpServerTool, Description("Find example RealScript files demonstrating a concept or technique")]
    public async Task<string> SearchExamples(
        [Description("What to search for")] string query,
        [Description("Optional: filter by category (e.g. 'Mean Reversion', 'Futures', 'Tutorial Scripts')")] string? categoryFilter = null,
        [Description("Number of results to return (default: 3)")] int topK = 3)
    {
        var queryEmbedding = await embedder.EmbedAsync(query);
        var results = await store.VectorSearchAsync(queryEmbedding, sourceType: "example",
            categoryFilter: categoryFilter, topK: topK);

        if (results.Count == 0)
            return "No example scripts found. Run 'realtest-mcp ingest scripts' if the database is empty.";

        var sb = new StringBuilder();
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"## Example {i + 1}: {Path.GetFileName(r.SourcePath)}");
            if (r.Category is not null) sb.AppendLine($"Category: {r.Category}");
            if (r.Description is not null) sb.AppendLine($"Description: {r.Description}");
            sb.AppendLine();
            sb.AppendLine(r.Content);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 5: Implement SearchUserScriptsTool**

```csharp
// src/RealTestMcp/Tools/SearchUserScriptsTool.cs
using ModelContextProtocol.Server;
using RealTestMcp.Core;
using System.ComponentModel;
using System.Text;

namespace RealTestMcp.Tools;

[McpServerToolType]
public class SearchUserScriptsTool(VectorStoreService store, EmbeddingService embedder)
{
    [McpServerTool, Description("Search your own RealScript files for patterns or techniques")]
    public async Task<string> SearchUserScripts(
        [Description("What to search for")] string query,
        [Description("Number of results to return (default: 3)")] int topK = 3)
    {
        var queryEmbedding = await embedder.EmbedAsync(query);
        var results = await store.VectorSearchAsync(queryEmbedding, sourceType: "user_script", topK: topK);

        if (results.Count == 0)
            return "No user scripts found. Add script paths to appsettings.json and run 'realtest-mcp ingest scripts'.";

        var sb = new StringBuilder();
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"## User Script {i + 1}: {Path.GetFileName(r.SourcePath)}");
            sb.AppendLine();
            sb.AppendLine(r.Content);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 6: Build to confirm**

```bash
dotnet build RealTestMcp.sln
```

Expected: Build succeeded.

- [ ] **Step 7: End-to-end test with Claude Code**

With docs and scripts ingested, open a Claude Code session and test:
- Ask Claude to look up the `ATR` function — it should call `get_function_reference`
- Ask Claude to find an example of mean reversion — it should call `search_examples`
- Ask Claude how position sizing works — it should call `search_docs`

- [ ] **Step 8: Commit**

```bash
git add src/RealTestMcp/Tools/ src/RealTestMcp/Program.cs
git commit -m "feat: implement all four MCP tools with DI-injected services"
```

---

## Task 18: README

**Files:**
- Create: `README.md`

- [ ] **Step 1: Write README**

```markdown
# RealTest MCP Server

Semantic search over RealTest documentation and example scripts for Claude Code.
Fixes RealScript hallucinations by providing authoritative function references at query time.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- RealTest installed (for CHM docs and example scripts)

## Setup

**1. Clone and build**
\`\`\`bash
git clone <repo-url>
cd realtest-mcp
dotnet build
\`\`\`

**2. Extract RealTest docs** (one-time, after each RealTest upgrade)
\`\`\`bash
hh.exe -decompile C:\RealTest\Help C:\RealTest\RealTest.chm
\`\`\`

**3. Ingest docs and scripts**
\`\`\`bash
dotnet run --project src/RealTestMcp -- ingest docs
dotnet run --project src/RealTestMcp -- ingest scripts
dotnet run --project src/RealTestMcp -- status
\`\`\`

**4. Configure Claude Code** — add to your `claude_desktop_config.json`:
\`\`\`json
{
  "mcpServers": {
    "realtest": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/path/to/realtest-mcp/src/RealTestMcp"]
    }
  }
}
\`\`\`

**5. (Optional) Add your own scripts**

Edit `appsettings.json` and add paths to `ScriptPaths`, then re-run `ingest scripts`.

## Configuration

Edit `appsettings.json` (next to the built binary) to override defaults:

| Key | Default | Description |
|---|---|---|
| `Database.Path` | `%LOCALAPPDATA%\RealTestMcp\realtest.db` | SQLite database location |
| `RealTest.DocsPath` | `C:\RealTest\Help` | Extracted CHM HTML directory |
| `RealTest.ScriptPaths` | `["C:\RealTest\Scripts\Examples"]` | Script directories to index |

## Commands

| Command | Description |
|---|---|
| *(no args)* | Start MCP server (managed by Claude Code) |
| `ingest docs` | Ingest/refresh CHM documentation |
| `ingest scripts` | Ingest/refresh all configured script paths |
| `status` | Show database statistics |

## MCP Tools

| Tool | Description |
|---|---|
| `search_docs` | Semantic search over documentation |
| `get_function_reference` | Look up a RealScript function signature |
| `search_examples` | Find example scripts by concept |
| `search_user_scripts` | Search your own script files |
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: add README with setup instructions"
```

---

## Task 19: Skills (SKILL.md Files)

**Files:**
- Create: `skills/realscript-authoring/SKILL.md`
- Create: `skills/realscript-debugging/SKILL.md`
- Create: `skills/strategy-design/SKILL.md`
- Create: `CLAUDE.md`

- [ ] **Step 1: Write realscript-authoring skill**

```markdown
<!-- skills/realscript-authoring/SKILL.md -->
# RealScript Authoring

REQUIRED: Use this skill whenever writing any RealScript code.

## Workflow

1. **Identify all functions you plan to use** in the script
2. **For each function**, call `get_function_reference` and verify:
   - Correct parameter names and order
   - Return type
   - Any gotchas noted in the docs
3. **Search for similar examples** with `search_examples` — use these as structural templates
4. **Write the script** following the patterns found
5. **Before presenting output**, re-check every function call against retrieved signatures

## Hard Rules

- NEVER write a RealScript function call without first calling `get_function_reference` for it
- If `get_function_reference` returns no result, say so — do not guess the syntax
- Prefer patterns from retrieved examples over patterns from training data
- Always include the verified function list at the end of your response

## Template

\`\`\`
Verified functions:
- ATR(periods) — ✓ confirmed via get_function_reference
- RSI(periods) — ✓ confirmed via get_function_reference
\`\`\`
```

- [ ] **Step 2: Write realscript-debugging skill**

```markdown
<!-- skills/realscript-debugging/SKILL.md -->
# RealScript Debugging

REQUIRED: Use this skill when a RealScript file isn't working as expected.

## Workflow

1. **List all functions** used in the provided script
2. **For each function**, call `get_function_reference` and compare:
   - Are parameters in the correct order?
   - Are parameter names spelled correctly?
   - Is the return type being used correctly?
3. **Search for error messages** with `search_docs` if the user has provided one
4. **Flag every discrepancy** found — do not silently fix things
5. **Present a diff** of what needs to change and why, citing the retrieved docs

## Common Issues

- Wrong parameter order (e.g. `Highest(20, H)` instead of `Highest(H, 20)`)
- Using deprecated syntax from old versions
- Incorrect section names (e.g. `Entry:` vs `EntryRule:`)
- Missing required fields for a section type
```

- [ ] **Step 3: Write strategy-design skill**

```markdown
<!-- skills/strategy-design/SKILL.md -->
# Strategy Design

REQUIRED: Use this skill when translating a trading concept into a RealScript strategy.

## Workflow

1. **Search for similar examples** with `search_examples` using the trading concept as the query
2. **Identify the required building blocks** (entry conditions, exit rules, position sizing, etc.)
3. **Look up each building block** in docs with `search_docs`
4. **Scaffold the structure first** — sections only, no logic yet:
   \`\`\`
   Settings: ...
   Strategy <Name>:
     EntrySetup:
     Entry:
     ExitRule:
     ExitStop:
     Quantity:
   \`\`\`
5. **Fill in the logic** one section at a time, calling `get_function_reference` for each function

## Principle

Build the structure before the details. A correct skeleton with wrong parameters is easier to debug than a syntactically wrong script with the right intent.
```

- [ ] **Step 4: Create CLAUDE.md**

```markdown
# RealTest MCP — Claude Code Project

This project is the RealTest MCP server. When working in this codebase:

@include skills/realscript-authoring/SKILL.md
@include skills/realscript-debugging/SKILL.md
@include skills/strategy-design/SKILL.md
```

- [ ] **Step 5: Validate skills are active**

Start a new Claude Code session in this project directory. Verify that when you ask Claude to write a RealScript strategy, it invokes the `realscript-authoring` skill and calls `get_function_reference` before generating any code.

- [ ] **Step 6: Commit**

```bash
git add skills/ CLAUDE.md
git commit -m "feat: add realscript-authoring, debugging, and strategy-design skills"
```

---

## Task 20: Final CI Verification

- [ ] **Step 1: Add vec0.dll presence check to CI workflow**

Update `.github/workflows/ci.yml` to add a validation step before the build:

```yaml
      - name: Verify sqlite-vec native extension present
        shell: pwsh
        run: |
          if (-not (Test-Path "native/windows-x64/vec0.dll")) {
            Write-Error "native/windows-x64/vec0.dll is missing. Download from sqlite-vec GitHub releases."
            exit 1
          }
```

This surfaces a missing DLL as a clear error rather than a cryptic `DllNotFoundException` during tests.

- [ ] **Step 2: Add .gitattributes entry for binary DLL**

Create or update `.gitattributes`:
```
native/**/*.dll binary
```

This ensures Git treats the DLL as binary, preventing line-ending corruption on Windows.

```bash
git add .gitattributes
git commit -m "chore: mark native DLLs as binary in .gitattributes"
```

- [ ] **Step 3: Run full test suite locally**

```bash
dotnet test RealTestMcp.sln --verbosity normal
```

Expected: All tests pass, no skipped tests.

- [ ] **Step 4: Push and verify CI**

```bash
git push
```

Check GitHub Actions. Expected: green build on all jobs including the vec0.dll check.

- [ ] **Step 5: Verify end-to-end with real data**

With real CHM and scripts ingested:

```bash
dotnet run --project src/RealTestMcp -- ingest docs
dotnet run --project src/RealTestMcp -- ingest scripts
dotnet run --project src/RealTestMcp -- status
```

Then in Claude Code:
1. Ask: *"What are the parameters of the ATR function?"* → should call `get_function_reference`
2. Ask: *"Show me an example of a mean reversion strategy"* → should call `search_examples`
3. Ask: *"Write a breakout strategy using Highest"* → should invoke realscript-authoring skill, call `get_function_reference("Highest")`, then write code

- [ ] **Step 6: Tag v1.0.0**

```bash
git tag v1.0.0
git push origin v1.0.0
```
