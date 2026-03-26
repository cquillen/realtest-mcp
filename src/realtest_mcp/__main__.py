"""CLI entry point: python -m realtest_mcp [serve|ingest|status]"""

import argparse


def main():
    parser = argparse.ArgumentParser(
        prog="realtest_mcp",
        description="RealTest MCP Server — RealScript documentation tools",
    )
    subparsers = parser.add_subparsers(dest="command", required=True)
    subparsers.add_parser("serve", help="Start MCP server (StdIO)")
    subparsers.add_parser("ingest", help="Extract PDF + index scripts into ChromaDB")
    subparsers.add_parser("status", help="Show database stats")

    args = parser.parse_args()

    if args.command == "serve":
        from realtest_mcp.server.mcp_server import run_server

        run_server()
    elif args.command == "ingest":
        from realtest_mcp.ingestion.ingest import run_ingest

        run_ingest()
    elif args.command == "status":
        from realtest_mcp.config import Config
        from realtest_mcp.store.vector_store import VectorStore

        config = Config.load()
        store = VectorStore(config.db_path)
        store.print_status()


if __name__ == "__main__":
    main()
