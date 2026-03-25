"""MCP server setup using FastMCP."""

from mcp.server.fastmcp import FastMCP

from realtest_mcp.config import Config
from realtest_mcp.store.vector_store import VectorStore
from realtest_mcp.server.tools import register_tools

_mcp = FastMCP("RealTest MCP")


def run_server():
    """Start the MCP server over StdIO."""
    config = Config.load()
    store = VectorStore(config.db_path)
    register_tools(_mcp, store)
    _mcp.run(transport="stdio")
