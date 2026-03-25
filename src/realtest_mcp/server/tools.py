"""MCP tool definitions for RealTest MCP."""

from pathlib import Path

from realtest_mcp.store.vector_store import VectorStore

_PRIMER_PATH = Path(__file__).parent.parent / "data" / "primer.md"

_store: VectorStore | None = None


def _check_populated() -> None:
    """Raise RuntimeError if the store is empty."""
    if _store is None or not _store.is_populated():
        raise RuntimeError(
            "Vector store is empty. Run the ingest pipeline first."
        )


def register_tools(mcp, store: VectorStore) -> None:
    """Register all MCP tools against the given FastMCP instance."""
    global _store
    _store = store

    @mcp.tool()
    def get_primer() -> str:
        """Load the RealScript mental model — script structure, evaluation semantics, formula syntax, and key concepts. Call this once at the start of any scripting session."""
        _check_populated()
        if _PRIMER_PATH.exists():
            return _PRIMER_PATH.read_text(encoding="utf-8")
        return "Primer file not found."

    # Path tokens that map to a narrative section rather than an element
    _PATH_TOKENS = {"scriptpath", "desktop", "documents", "appdata",
                    "realtest", "data", "output", "scripts", "scriptname",
                    "testname", "date", "time", "orderdate", "ocfolder"}

    # Operators and syntax tokens that map to narrative sections
    _OPERATOR_TOKENS = {
        "and", "or", "not", "mod",
        "+", "-", "*", "/", "%", "^",
        "=", "==", "<>", "!=", ">", "<", ">=", "<=",
        "bitand", "bitor", "bitxor", "bitnot",
    }
    _SYNTAX_SECTIONS = {
        "[]": "Bar Offsets",
        "bar offset": "Bar Offsets",
        "bar offsets": "Bar Offsets",
        "$": "Symbol References",
        "$$": "Symbol References",
        "@": "Symbol References",
        "//": "Formula Syntax",
        "/*": "Formula Syntax",
        "comment": "Formula Syntax",
        "comments": "Formula Syntax",
        # Scan sub-keywords
        "filter": "Scan and TestScan Sections",
        "filterlong": "Scan and TestScan Sections",
        "filtershort": "Scan and TestScan Sections",
        "sort": "Scan and TestScan Sections",
    }

    @mcp.tool()
    def get_reference(name: str) -> str:
        """Get the exact reference documentation for a RealScript element. Call this before using any element in generated code."""
        _check_populated()
        name_stripped = name.lower().strip("?").strip()
        # Path tokens → redirect to File Path Syntax section
        if name_stripped in _PATH_TOKENS:
            results = _store.get_section("File Path Syntax")
            if results:
                return results[0]["document"]
        # Operators → redirect to Operators section
        if name_stripped in _OPERATOR_TOKENS:
            results = _store.get_section("Operators")
            if results:
                return results[0]["document"]
        # Other syntax tokens → redirect to specific sections
        if name_stripped in _SYNTAX_SECTIONS:
            section_title = _SYNTAX_SECTIONS[name_stripped]
            results = _store.get_section(section_title)
            if results:
                return results[0]["document"]
        # Step 1: exact match (includes disambiguation and alias resolution)
        results = _store.get_by_element_name(name)
        if results:
            parts = []
            for r in results:
                parts.append(r["document"])
            return "\n\n".join(parts)
        # Step 2: semantic fallback
        results = _store.search(name, top_k=1, chunk_type="element_detail")
        if results:
            match_name = results[0]["metadata"].get("element_name_display", "unknown")
            note = f"_No exact match for '{name}'. Closest: **{match_name}**_\n\n"
            return note + results[0]["document"]
        return f"No reference found for '{name}'."

    @mcp.tool()
    def get_section(title: str) -> str:
        """Fetch a specific documentation section by title or section number. Use for deep dives into topics."""
        _check_populated()
        results = _store.get_section(title)
        if results:
            sections = []
            for r in results:
                sec_num = r["metadata"].get("section_number", "")
                sec_title = r["metadata"].get("section_title", "Untitled")
                content = r["document"]
                sections.append(f"## {sec_num}. {sec_title}\n\n{content}")
            return "\n\n---\n\n".join(sections)
        # Fallback to semantic search on narrative chunks
        results = _store.search(title, top_k=3, chunk_type="narrative")
        if results:
            sections = []
            for r in results:
                sec_num = r["metadata"].get("section_number", "")
                sec_title = r["metadata"].get("section_title", "Untitled")
                content = r["document"]
                sections.append(f"## {sec_num}. {sec_title}\n\n{content}")
            return "\n\n---\n\n".join(sections)
        return f"No section found for '{title}'."

    @mcp.tool()
    def list_elements(category: str = "") -> str:
        """Discover what RealScript elements exist. Call with a category to see elements, or without to see all categories."""
        _check_populated()
        if category:
            items = _store.list_elements(category)
            if not items:
                return f"No elements found in category '{category}'."
            lines = []
            for item in items:
                name = item["element_name_display"]
                summary = item["summary"]
                lines.append(f"- **{name}** — {summary}")
            return "\n".join(lines)
        else:
            cats = _store.list_categories()
            if not cats:
                return "No categories found."
            lines = []
            for cat in sorted(cats.keys()):
                lines.append(f"- **{cat}** ({cats[cat]} elements)")
            return "\n".join(lines)

    @mcp.tool()
    def search_docs(query: str, category: str = "", top_k: int = 5) -> str:
        """Search RealTest documentation by concept or topic."""
        _check_populated()
        results = _store.search_docs(query, top_k=top_k, category=category or None)
        if not results:
            return f"No documentation found for '{query}'."
        sections = []
        for r in results:
            name = r["metadata"].get("element_name_display", r["metadata"].get("section_title", "Untitled"))
            cat = r["metadata"].get("category", "")
            content = r["document"]
            sections.append(f"### {name} ({cat})\n\n{content}")
        return "\n\n---\n\n".join(sections)

    @mcp.tool()
    def search_scripts(query: str, source: str = "all", top_k: int = 3) -> str:
        """Find example RealScript files demonstrating a concept or technique."""
        _check_populated()
        source_type = None
        if source == "user":
            source_type = "user_script"
        elif source != "all":
            source_type = source
        results = _store.search_scripts(query, top_k=top_k, source_type=source_type)
        # Keyword fallback
        if not results:
            keywords = query.split()
            results = _store.keyword_search_scripts(keywords, top_k=top_k, source_type=source_type)
        if not results:
            return f"No scripts found for '{query}'."
        sections = []
        for r in results:
            name = r["metadata"].get("file_name", "unknown")
            content = r["document"]
            if len(content) > 3000:
                content = content[:3000] + "\n\n... (truncated, full script is longer)"
            sections.append(f"### {name}\n\n```realscript\n{content}\n```")
        return "\n\n---\n\n".join(sections)
