# RealTest MCP Server

Semantic search over RealTest documentation and example scripts for Claude Code.
Fixes RealScript hallucinations by providing authoritative function references at query time.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- RealTest installed (for CHM docs and example scripts)

## Setup

**1. Clone and build**
```bash
git clone <repo-url>
cd realtest-mcp
dotnet build
```

**2. Extract RealTest docs** (one-time, after each RealTest upgrade)
```bash
hh.exe -decompile C:\RealTest\Help C:\RealTest\RealTest.chm
```

**3. Ingest docs and scripts**
```bash
dotnet run --project src/RealTestMcp -- ingest docs
dotnet run --project src/RealTestMcp -- ingest scripts
dotnet run --project src/RealTestMcp -- status
```

**4. Configure Claude Code** — add to your `claude_desktop_config.json`:
```json
{
  "mcpServers": {
    "realtest": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/path/to/realtest-mcp/src/RealTestMcp"]
    }
  }
}
```

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
