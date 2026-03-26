# RealTest MCP Server

An MCP server that gives LLM agents structured access to RealTest backtesting
documentation and example scripts. Fixes RealScript hallucinations by providing
authoritative function references, searchable docs, and verified example scripts
at query time.

## Requirements

- Python 3.11+
- RealTest installed (for the User Guide PDF and example scripts)

## Setup

**1. Clone and install**

```bash
git clone <repo-url>
cd realtest-mcp
pip install -e .
```

**2. Configure paths**

Edit `config.toml` to match your RealTest installation:

```toml
[realtest]
pdf_path = "C:\\RealTest\\RealTest User Guide.pdf"

[scripts]
examples = "C:\\RealTest\\Scripts\\Examples"
user_scripts = []

[database]
path = "%LOCALAPPDATA%\\RealTestMcp\\chromadb"
```

All paths support `%VAR%` environment variable expansion. Individual fields can
also be overridden via environment variables: `REALTEST_MCP_PDF_PATH`,
`REALTEST_MCP_EXAMPLES`, `REALTEST_MCP_DB_PATH`.

**3. Ingest docs and scripts**

```bash
python -m realtest_mcp ingest
python -m realtest_mcp status
```

**4. Configure your MCP client**

Add to `.mcp.json` in your project (or pass via `--mcp-config`):

```json
{
  "mcpServers": {
    "realtest": {
      "command": "python",
      "args": ["-m", "realtest_mcp", "serve"]
    }
  }
}
```

**5. (Optional) Add your own scripts**

Add directories to `user_scripts` in `config.toml`, then re-run `ingest`.

## Commands

| Command | Description |
|---|---|
| `serve` | Start MCP server (StdIO transport, managed by client) |
| `ingest` | Extract PDF docs, index scripts into ChromaDB |
| `status` | Show database statistics (chunk counts, timestamps) |

## MCP Tools

Six tools are exposed to MCP clients:

| Tool | Description |
|---|---|
| `get_primer` | Load the RealScript mental model (call once per session) |
| `get_reference` | Look up exact element docs with alias resolution |
| `get_section` | Fetch narrative documentation sections by title |
| `list_elements` | Browse element categories and their contents |
| `search_docs` | Semantic search over documentation |
| `search_scripts` | Find example scripts by concept or technique |

See [API Reference](docs/api-reference.md) for full parameter details and
usage examples.

## Documentation

- [Architecture Overview](docs/architecture.md) -- system design, components, data flow
- [API Reference](docs/api-reference.md) -- tool parameters, resolution logic, workflows
- [Roadmap](docs/roadmap.md) -- proposed enhancements
