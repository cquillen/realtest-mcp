# RealTest MCP Python Rewrite — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reimplement the RealTest MCP server in Python with PDF-sourced documentation, ChromaDB vector store, BGE-base-en-v1.5 embeddings, and a 6-tool API.

**Architecture:** Single Python package with CLI entry point. Ingestion pipeline extracts the RealTest User Guide PDF as a continuous text stream, splits on TOC titles, formats as markdown, and stores in ChromaDB. MCP server exposes 6 tools over StdIO using the FastMCP decorator API.

**Tech Stack:** Python 3.11+, `mcp` (FastMCP), `chromadb`, `sentence-transformers` (BGE-base-en-v1.5), `pymupdf` (fitz), `pytest`

**Spec:** `docs/superpowers/specs/2026-03-25-python-mcp-rewrite-design.md`

---

## File Structure

```
D:\Code\realtest-mcp\
├── pyproject.toml                      # Package metadata, dependencies, pytest config
├── config.toml                         # User configuration (paths to PDF, scripts, DB)
├── README.md                           # Updated for Python implementation
├── CLAUDE.md                           # Updated for new tool names
│
├── src/realtest_mcp/
│   ├── __init__.py                     # Package version
│   ├── __main__.py                     # CLI: serve | ingest | status
│   ├── config.py                       # Load config.toml + env var overrides
│   │
│   ├── ingestion/
│   │   ├── __init__.py
│   │   ├── pdf_parser.py              # PyMuPDF: PDF → full text stream + TOC list
│   │   ├── chunker.py                 # Split text stream on TOC titles → raw chunks
│   │   ├── markdown_formatter.py      # Convert raw PDF text to clean markdown
│   │   ├── element_parser.py          # Parse element detail fields (Category, Syntax, etc.)
│   │   ├── category_parser.py         # Parse 17.17 category summaries → name:description pairs
│   │   ├── script_parser.py           # Read .rts files → chunks
│   │   └── ingest.py                  # Orchestrator: parse → chunk → format → store
│   │
│   ├── store/
│   │   ├── __init__.py
│   │   └── vector_store.py            # ChromaDB wrapper (add, query, metadata filters)
│   │
│   └── server/
│       ├── __init__.py
│       ├── mcp_server.py              # FastMCP server setup
│       └── tools.py                   # 6 tool implementations
│
├── tests/
│   ├── conftest.py                    # Shared fixtures (tmp ChromaDB, sample PDF text)
│   ├── test_config.py
│   ├── test_pdf_parser.py
│   ├── test_chunker.py
│   ├── test_markdown_formatter.py
│   ├── test_element_parser.py
│   ├── test_category_parser.py
│   ├── test_script_parser.py
│   ├── test_vector_store.py
│   ├── test_tools.py
│   └── test_integration.py
│
├── skills/                            # Updated skill definitions
│   ├── realscript-authoring/SKILL.md
│   ├── realscript-debugging/SKILL.md
│   └── strategy-design/SKILL.md
│
├── scripts/
│   └── pdf_extract_poc.py             # POC (kept for reference)
│
├── docs/
│   └── superpowers/                   # Specs and plans (historical)
│
└── GAP_REPORT.md                      # Validation reference
```

**Design note:** `chunker.py` and `markdown_formatter.py` are split because chunking (text stream splitting) and formatting (markdown conversion) are distinct responsibilities that should be testable independently. Similarly, `element_parser.py` and `category_parser.py` handle specific parsing patterns for their respective PDF section structures.

---

## Primer Chunks Curation

The following narrative sections are tagged `is_primer: true` with the specified order. These teach the core mental model needed for writing RealScript.

| `primer_order` | Section | Why |
|---|---|---|
| 1 | 17. Realtest Script Language | Script structure overview: sections, items, Item:content syntax |
| 2 | 17.1. Bar Offsets | Fundamental: `C[1]` means prior bar, `C[-1]` means next bar |
| 3 | 17.15. Script Sections | Overview of all section types and their roles |
| 4 | 17.15.2. Data Section | Pre-computed arrays, critical for correct script design |
| 5 | 17.15.6. Library Section | Difference from Data, arg1-arg9 calling, when to use |
| 6 | 17.15.7. Strategy Section | Strategy elements, entry/exit/sizing structure |
| 7 | 17.16. Formula Syntax | Where formulas are allowed, recursion, nesting |
| 8 | 17.16.1. Operators | Operator list and precedence |
| 9 | 17.16.2. Formula Evaluation | Short-circuit optimization, nan behavior |
| 10 | 16.11. The Current Bar in Formula Evaluation | Critical: avoids look-ahead bias |
| 11 | 16.6. Intraday Fills With Daily Bars | How fill simulation works |
| 12 | 16.1. Asset Allocation and Position Sizing | The setup selection loop |

---

## Task 1: Project Scaffolding

**Files:**
- Create: `pyproject.toml`
- Create: `config.toml`
- Create: `src/realtest_mcp/__init__.py`
- Create: `src/realtest_mcp/__main__.py`
- Create: `src/realtest_mcp/config.py`
- Create: `tests/conftest.py`
- Create: `tests/test_config.py`

- [ ] **Step 1: Create `pyproject.toml`**

```toml
[build-system]
requires = ["setuptools>=68.0"]
build-backend = "setuptools.backends._legacy:_Backend"

[project]
name = "realtest-mcp"
version = "0.1.0"
requires-python = ">=3.11"
dependencies = [
    "mcp",
    "chromadb",
    "sentence-transformers",
    "pymupdf",
]

[project.optional-dependencies]
dev = ["pytest>=7.0", "pytest-asyncio"]

[tool.pytest.ini_options]
testpaths = ["tests"]
asyncio_mode = "auto"

[tool.setuptools.packages.find]
where = ["src"]
```

- [ ] **Step 2: Create `config.toml`**

```toml
[realtest]
pdf_path = "C:\\RealTest\\RealTest User Guide.pdf"

[scripts]
examples = "C:\\RealTest\\Scripts\\Examples"
user_scripts = []

[database]
path = "%LOCALAPPDATA%\\RealTestMcp\\chromadb"
```

- [ ] **Step 3: Create `src/realtest_mcp/__init__.py`**

```python
"""RealTest MCP Server — RealScript documentation and script search tools."""

__version__ = "0.1.0"
```

- [ ] **Step 4: Create `src/realtest_mcp/config.py`**

```python
"""Load config.toml with environment variable overrides."""

import os
import re
import tomllib
from dataclasses import dataclass, field
from pathlib import Path


@dataclass
class Config:
    pdf_path: str = ""
    examples_path: str = ""
    user_script_paths: list[str] = field(default_factory=list)
    db_path: str = ""

    @staticmethod
    def _expand_vars(value: str) -> str:
        """Expand %VAR% style environment variables in a string."""
        def replacer(match):
            var_name = match.group(1)
            return os.environ.get(var_name, match.group(0))
        return re.sub(r"%([^%]+)%", replacer, value)

    @classmethod
    def load(cls) -> "Config":
        """Load config from TOML file with env var overrides."""
        config_path = cls._find_config_file()
        if config_path is None:
            raise FileNotFoundError(
                "No config.toml found. Searched:\n"
                "  1. REALTEST_MCP_CONFIG env var\n"
                "  2. ./config.toml\n"
                "  3. <package_dir>/config.toml"
            )

        with open(config_path, "rb") as f:
            data = tomllib.load(f)

        config = cls(
            pdf_path=data.get("realtest", {}).get("pdf_path", ""),
            examples_path=data.get("scripts", {}).get("examples", ""),
            user_script_paths=data.get("scripts", {}).get("user_scripts", []),
            db_path=data.get("database", {}).get("path", ""),
        )

        # Environment variable overrides
        env_map = {
            "REALTEST_MCP_PDF_PATH": "pdf_path",
            "REALTEST_MCP_EXAMPLES": "examples_path",
            "REALTEST_MCP_DB_PATH": "db_path",
        }
        for env_var, attr in env_map.items():
            val = os.environ.get(env_var)
            if val:
                setattr(config, attr, val)

        # Expand %VAR% in all path fields
        config.pdf_path = cls._expand_vars(config.pdf_path)
        config.examples_path = cls._expand_vars(config.examples_path)
        config.user_script_paths = [cls._expand_vars(p) for p in config.user_script_paths]
        config.db_path = cls._expand_vars(config.db_path)

        return config

    @staticmethod
    def _find_config_file() -> Path | None:
        # 1. Env var
        env_path = os.environ.get("REALTEST_MCP_CONFIG")
        if env_path and Path(env_path).exists():
            return Path(env_path)
        # 2. Current working directory
        cwd_path = Path.cwd() / "config.toml"
        if cwd_path.exists():
            return cwd_path
        # 3. Next to package
        pkg_path = Path(__file__).parent.parent.parent / "config.toml"
        if pkg_path.exists():
            return pkg_path
        return None
```

- [ ] **Step 5: Create `src/realtest_mcp/__main__.py`**

```python
"""CLI entry point: python -m realtest_mcp [serve|ingest|status]"""

import argparse
import sys


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
        from realtest_mcp.store.vector_store import VectorStore
        from realtest_mcp.config import Config
        config = Config.load()
        store = VectorStore(config.db_path)
        store.print_status()


if __name__ == "__main__":
    main()
```

- [ ] **Step 6: Create stub modules for imports to work**

Create empty `__init__.py` files:
- `src/realtest_mcp/ingestion/__init__.py`
- `src/realtest_mcp/store/__init__.py`
- `src/realtest_mcp/server/__init__.py`

- [ ] **Step 7: Write config tests**

Create `tests/conftest.py`:
```python
"""Shared test fixtures."""

import pytest
from pathlib import Path


@pytest.fixture
def tmp_config(tmp_path):
    """Create a temporary config.toml for testing."""
    config_content = f"""
[realtest]
pdf_path = "C:\\\\RealTest\\\\RealTest User Guide.pdf"

[scripts]
examples = "C:\\\\RealTest\\\\Scripts\\\\Examples"
user_scripts = []

[database]
path = "{tmp_path / 'chromadb'}"
"""
    config_file = tmp_path / "config.toml"
    config_file.write_text(config_content)
    return config_file
```

Create `tests/test_config.py`:
```python
"""Tests for config loading."""

import os
import pytest
from realtest_mcp.config import Config


def test_load_config_from_env_var(tmp_config, monkeypatch):
    monkeypatch.setenv("REALTEST_MCP_CONFIG", str(tmp_config))
    config = Config.load()
    assert config.pdf_path == r"C:\RealTest\RealTest User Guide.pdf"
    assert config.examples_path == r"C:\RealTest\Scripts\Examples"


def test_env_var_override(tmp_config, monkeypatch):
    monkeypatch.setenv("REALTEST_MCP_CONFIG", str(tmp_config))
    monkeypatch.setenv("REALTEST_MCP_PDF_PATH", "/custom/path.pdf")
    config = Config.load()
    assert config.pdf_path == "/custom/path.pdf"


def test_expand_percent_vars(tmp_config, monkeypatch):
    monkeypatch.setenv("REALTEST_MCP_CONFIG", str(tmp_config))
    monkeypatch.setenv("REALTEST_MCP_DB_PATH", "%TEMP%/test_db")
    config = Config.load()
    assert "%TEMP%" not in config.db_path


def test_missing_config_raises(tmp_path, monkeypatch):
    # Ensure no config file is findable
    monkeypatch.delenv("REALTEST_MCP_CONFIG", raising=False)
    monkeypatch.chdir(tmp_path)  # CWD with no config.toml
    with pytest.raises(FileNotFoundError):
        Config.load()
```

- [ ] **Step 8: Install dependencies and run tests**

```bash
cd D:\Code\realtest-mcp
pip install -e ".[dev]"
pytest tests/test_config.py -v
```

Expected: All 4 tests pass.

- [ ] **Step 9: Commit**

```bash
git add pyproject.toml config.toml src/ tests/
git commit -m "feat: project scaffolding with config loading and CLI skeleton"
```

---

## Task 2: PDF Parser

**Files:**
- Create: `src/realtest_mcp/ingestion/pdf_parser.py`
- Create: `tests/test_pdf_parser.py`

- [ ] **Step 1: Write failing tests**

Create `tests/test_pdf_parser.py`:
```python
"""Tests for PDF text extraction and TOC parsing."""

import pytest
from realtest_mcp.ingestion.pdf_parser import PdfParser

PDF_PATH = r"C:\RealTest\RealTest User Guide.pdf"


@pytest.fixture
def parser():
    return PdfParser(PDF_PATH)


def test_extract_full_text(parser):
    text = parser.extract_full_text()
    assert len(text) > 100_000  # PDF has ~500 pages of content
    assert "17.18.103. Commission" in text
    assert "EntrySetup" in text


def test_get_toc(parser):
    toc = parser.get_toc()
    assert len(toc) > 700  # 749 entries expected
    # Each entry has (level, title, page)
    first = toc[0]
    assert len(first) == 3
    assert isinstance(first[0], int)  # level
    assert isinstance(first[1], str)  # title
    assert isinstance(first[2], int)  # page


def test_toc_contains_element_details(parser):
    toc = parser.get_toc()
    titles = [t[1] for t in toc]
    assert any("Commission" in t for t in titles)
    assert any("Extern" in t for t in titles)
    assert any("#Rank" in t for t in titles)


def test_toc_contains_narrative_sections(parser):
    toc = parser.get_toc()
    titles = [t[1] for t in toc]
    assert any("Formula Syntax" in t for t in titles)
    assert any("Script Sections" in t for t in titles)
    assert any("Backtest Engine" in t for t in titles)


def test_full_text_is_continuous_stream(parser):
    """Verify text is one continuous string, not page-separated."""
    text = parser.extract_full_text()
    # Commission and Compounded share page 294 in the PDF.
    # In a continuous stream, Commission content should appear before Compounded.
    comm_pos = text.index("17.18.103. Commission")
    comp_pos = text.index("17.18.104. Compounded")
    assert comm_pos < comp_pos
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
pytest tests/test_pdf_parser.py -v
```

Expected: FAIL with `ModuleNotFoundError: No module named 'realtest_mcp.ingestion.pdf_parser'`

- [ ] **Step 3: Implement PDF parser**

Create `src/realtest_mcp/ingestion/pdf_parser.py`:
```python
"""Extract text and TOC from the RealTest User Guide PDF."""

import fitz  # PyMuPDF


class PdfParser:
    """Extracts a continuous text stream and TOC from a PDF file."""

    def __init__(self, pdf_path: str):
        self.pdf_path = pdf_path

    def extract_full_text(self) -> str:
        """Extract all pages as a single continuous text string."""
        doc = fitz.open(self.pdf_path)
        try:
            pages = []
            for page in doc:
                pages.append(page.get_text())
            return "\n".join(pages)
        finally:
            doc.close()

    def get_toc(self) -> list[tuple[int, str, int]]:
        """Return the PDF table of contents as [(level, title, page), ...]."""
        doc = fitz.open(self.pdf_path)
        try:
            return [(level, title, page) for level, title, page in doc.get_toc()]
        finally:
            doc.close()
```

- [ ] **Step 4: Run tests**

```bash
pytest tests/test_pdf_parser.py -v
```

Expected: All 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/realtest_mcp/ingestion/pdf_parser.py tests/test_pdf_parser.py
git commit -m "feat: PDF parser — extract continuous text stream and TOC"
```

---

## Task 3: Text Stream Chunker

**Files:**
- Create: `src/realtest_mcp/ingestion/chunker.py`
- Create: `tests/test_chunker.py`

- [ ] **Step 1: Write failing tests**

Create `tests/test_chunker.py`:
```python
"""Tests for TOC-based text stream splitting."""

import pytest
from realtest_mcp.ingestion.chunker import Chunker


@pytest.fixture
def sample_toc():
    """Simplified TOC for testing."""
    return [
        (1, "17. Script Language", 1),
        (2, "17.1. Bar Offsets", 2),
        (2, "17.2. Date Constants", 3),
        (2, "17.15. Script Sections", 4),
        (3, "17.15.1. Import Section", 5),
        (3, "17.15.2. Data Section", 6),
        (2, "17.18. Syntax Element Details", 7),
        (3, "17.18.1. #Avg", 8),
        (3, "17.18.2. #ByCII", 9),
    ]


@pytest.fixture
def sample_text():
    """Simulated continuous text stream matching sample_toc."""
    return """Some preamble text here.
17. Script Language
Writing scripts does not require programming.
Scripts are organized into sections.
17.1. Bar Offsets
When a bar field is referenced, there is a current bar context.
Use C[1] for prior bar close.
17.2. Date Constants
Date constants use YYYYMMDD format.
17.15. Script Sections
The outer sections represent different categories.
The following topics describe each section.
17.15.1. Import Section
The Import section specifies data import settings.
17.15.2. Data Section
The Data section contains pre-computed formulas.
17.18. Syntax Element Details
All elements are listed alphabetically.
17.18.1. #Avg
Category
Cross-Sectional Functions
Description
Calculates the average value across all symbols.
17.18.2. #ByCII
Category
Cross-Sectional Functions
Description
Groups calculations by CII index.
Trailing text."""


def test_chunk_splits_on_toc_titles(sample_toc, sample_text):
    chunker = Chunker(sample_toc, sample_text)
    chunks = chunker.split()
    # Should produce one chunk per TOC entry
    assert len(chunks) == len(sample_toc)


def test_chunk_content_boundaries(sample_toc, sample_text):
    chunker = Chunker(sample_toc, sample_text)
    chunks = chunker.split()
    # First chunk (17. Script Language) should contain its intro text
    lang_chunk = chunks[0]
    assert "Writing scripts does not require programming" in lang_chunk.text
    # But NOT the next section's content
    assert "When a bar field is referenced" not in lang_chunk.text


def test_parent_chunk_has_intro_content(sample_toc, sample_text):
    chunker = Chunker(sample_toc, sample_text)
    chunks = chunker.split()
    # 17.15 parent should have its intro but not its children's content
    sections_chunk = [c for c in chunks if c.title == "17.15. Script Sections"][0]
    assert "outer sections represent different categories" in sections_chunk.text
    assert "Import section specifies" not in sections_chunk.text


def test_chunk_metadata(sample_toc, sample_text):
    chunker = Chunker(sample_toc, sample_text)
    chunks = chunker.split()
    first = chunks[0]
    assert first.title == "17. Script Language"
    assert first.section_number == "17"
    assert first.level == 1


def test_chunk_parent_section(sample_toc, sample_text):
    chunker = Chunker(sample_toc, sample_text)
    chunks = chunker.split()
    import_chunk = [c for c in chunks if "Import Section" in c.title][0]
    assert import_chunk.parent_title == "17.15. Script Sections"


def test_no_empty_chunks_for_entries_with_content(sample_toc, sample_text):
    chunker = Chunker(sample_toc, sample_text)
    chunks = chunker.split()
    for chunk in chunks:
        assert len(chunk.text.strip()) > 0, f"Empty chunk: {chunk.title}"
```

- [ ] **Step 2: Run tests to verify failure**

```bash
pytest tests/test_chunker.py -v
```

Expected: FAIL with `ModuleNotFoundError`

- [ ] **Step 3: Implement chunker**

Create `src/realtest_mcp/ingestion/chunker.py`:
```python
"""Split a continuous text stream into chunks based on TOC title markers."""

import re
from dataclasses import dataclass


@dataclass
class RawChunk:
    """A raw text chunk extracted from the PDF text stream."""
    title: str
    section_number: str
    level: int
    text: str
    parent_title: str | None


class Chunker:
    """Splits a text stream on TOC title patterns."""

    def __init__(self, toc: list[tuple[int, str, int]], text: str):
        self.toc = toc
        self.text = text

    def split(self) -> list[RawChunk]:
        """Split text stream into chunks using TOC titles as boundaries."""
        # Build regex patterns for each TOC entry, anchored to line start
        entries = []
        for level, title, _page in self.toc:
            section_number = self._extract_section_number(title)
            escaped_title = re.escape(title)
            pattern = re.compile(r"^" + escaped_title + r"\s*$", re.MULTILINE)
            entries.append((level, title, section_number, pattern))

        # Find all title positions in the text stream
        positions = []
        for i, (level, title, section_number, pattern) in enumerate(entries):
            match = pattern.search(self.text)
            if match:
                positions.append((match.start(), i, level, title, section_number))

        # Sort by position in text
        positions.sort(key=lambda x: x[0])

        # Build parent lookup
        parent_map = self._build_parent_map()

        # Extract text between consecutive positions
        chunks = []
        for idx, (pos, entry_idx, level, title, section_number) in enumerate(positions):
            # Text starts after the title line
            title_end = self.text.index("\n", pos) + 1 if "\n" in self.text[pos:] else len(self.text)
            # Text ends at the next title position, or end of stream
            if idx + 1 < len(positions):
                text_end = positions[idx + 1][0]
            else:
                text_end = len(self.text)

            chunk_text = self.text[title_end:text_end].strip()
            parent_title = parent_map.get(title)

            chunks.append(RawChunk(
                title=title,
                section_number=section_number,
                level=level,
                text=chunk_text,
                parent_title=parent_title,
            ))

        return chunks

    def _build_parent_map(self) -> dict[str, str | None]:
        """Map each TOC title to its parent title."""
        parent_map = {}
        stack = []  # (level, title)
        for level, title, _page in self.toc:
            while stack and stack[-1][0] >= level:
                stack.pop()
            parent_map[title] = stack[-1][1] if stack else None
            stack.append((level, title))
        return parent_map

    @staticmethod
    def _extract_section_number(title: str) -> str:
        """Extract section number from title like '17.18.103. Commission' → '17.18.103'."""
        match = re.match(r"^(\d+(?:\.\d+)*)\.\s", title)
        return match.group(1) if match else ""
```

- [ ] **Step 4: Run tests**

```bash
pytest tests/test_chunker.py -v
```

Expected: All 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/realtest_mcp/ingestion/chunker.py tests/test_chunker.py
git commit -m "feat: TOC-based text stream chunker"
```

---

## Task 4: Markdown Formatter

**Files:**
- Create: `src/realtest_mcp/ingestion/markdown_formatter.py`
- Create: `tests/test_markdown_formatter.py`

- [ ] **Step 1: Write failing tests**

Create `tests/test_markdown_formatter.py`:
```python
"""Tests for PDF text to markdown conversion."""

import pytest
from realtest_mcp.ingestion.markdown_formatter import MarkdownFormatter


def test_bullet_conversion():
    raw = "Items include:\n\u00b7\nfirst item\n\u00b7\nsecond item"
    result = MarkdownFormatter.format(raw)
    assert "- first item" in result
    assert "- second item" in result
    assert "\u00b7" not in result


def test_numbered_list_conversion():
    raw = "Steps:\n1.\nDo this first\n2.\nDo this second"
    result = MarkdownFormatter.format(raw)
    assert "1. Do this first" in result
    assert "2. Do this second" in result


def test_paragraph_separation():
    raw = "First paragraph about something.\nSecond paragraph about another thing."
    result = MarkdownFormatter.format(raw)
    # Paragraphs should have blank line between them
    assert "\n\n" in result


def test_code_block_detection_section_keywords():
    raw = "Here is an example:\nStrategy MyStrat:\n  EntrySetup: C > MA(C, 20)\n  ExitRule: BarsHeld >= 5"
    result = MarkdownFormatter.format(raw)
    assert "```rts" in result
    assert "```\n" in result or result.endswith("```")


def test_code_block_detection_formula_syntax():
    raw = "The formula would be:\nMA(C, 200) and RSI(C, 14) < 30"
    result = MarkdownFormatter.format(raw)
    assert "```rts" in result


def test_preserves_normal_text():
    raw = "RealTest uses its own memory-based binary format for daily price and volume data bars."
    result = MarkdownFormatter.format(raw)
    assert "RealTest uses its own memory-based binary format" in result


def test_strips_page_numbers():
    raw = "Some content here.\n293\nMore content continues."
    result = MarkdownFormatter.format(raw)
    assert "\n293\n" not in result
```

- [ ] **Step 2: Run tests to verify failure**

```bash
pytest tests/test_markdown_formatter.py -v
```

- [ ] **Step 3: Implement markdown formatter**

Create `src/realtest_mcp/ingestion/markdown_formatter.py`:
```python
"""Convert raw PDF-extracted text to clean markdown."""

import re


class MarkdownFormatter:
    """Converts raw PDF text to well-formed markdown."""

    # RealScript section keywords that indicate code
    _SECTION_KEYWORDS = re.compile(
        r"^\s*(Strategy|Data|Library|Import|Settings|Scan|TestScan|Charts|"
        r"Benchmark|Combined|Template|Parameters|Include|Notes|"
        r"EntrySetup|ExitRule|ExitStop|ExitLimit|Quantity|SetupScore|"
        r"EntryLimit|EntryStop|Side|Commission|Slippage)\s*[:>]",
        re.MULTILINE
    )

    # Formula-like syntax: function calls, operators with identifiers
    _FORMULA_PATTERN = re.compile(
        r"(?:MA|EMA|RSI|ATR|HVOL|BBTop|BBBot|Highest|Lowest|CountTrue|"
        r"SinceTrue|Correl|Extern|Select|IF|Min|Max|Sum|Round|Abs)\s*\(",
        re.IGNORECASE
    )

    # Standalone page numbers (a line that is just digits, typically 1-3 digits)
    _PAGE_NUMBER = re.compile(r"^\d{1,3}$", re.MULTILINE)

    @classmethod
    def format(cls, raw_text: str) -> str:
        """Convert raw PDF text to markdown."""
        text = raw_text

        # Strip standalone page numbers
        text = cls._PAGE_NUMBER.sub("", text)

        # Convert bullet points (· character)
        text = cls._convert_bullets(text)

        # Convert numbered lists
        text = cls._convert_numbered_lists(text)

        # Detect and fence code blocks
        text = cls._fence_code_blocks(text)

        # Clean up paragraph spacing
        text = cls._normalize_paragraphs(text)

        return text.strip()

    @classmethod
    def _convert_bullets(cls, text: str) -> str:
        """Convert · bullet markers to markdown - lists."""
        # Pattern: · on its own line followed by content on next line
        text = re.sub(r"\n\u00b7\n\s*", "\n- ", text)
        # Also handle inline bullets
        text = re.sub(r"\u00b7\s*", "- ", text)
        return text

    @classmethod
    def _convert_numbered_lists(cls, text: str) -> str:
        """Convert PDF numbered lists to markdown."""
        # Pattern: "N.\n" followed by content on next line
        text = re.sub(r"\n(\d+)\.\n\s*", r"\n\1. ", text)
        return text

    @classmethod
    def _fence_code_blocks(cls, text: str) -> str:
        """Detect code examples and wrap in fenced code blocks."""
        lines = text.split("\n")
        result = []
        in_code = False
        code_buffer = []

        for i, line in enumerate(lines):
            is_code_line = bool(
                cls._SECTION_KEYWORDS.match(line)
                or cls._FORMULA_PATTERN.search(line)
            )

            # Check if previous line is a trigger phrase
            trigger = False
            if i > 0 and is_code_line:
                prev = lines[i - 1].lower().rstrip()
                if any(prev.endswith(p) for p in [
                    ":", "example:", "as follows:", "like this:",
                    "as shown:", "for example:", "would be:"
                ]):
                    trigger = True

            if is_code_line and not in_code:
                in_code = True
                # Insert opening fence before the trigger line if present
                if trigger and result and not result[-1].startswith("```"):
                    result.append("```rts")
                elif not trigger:
                    result.append("```rts")
                code_buffer = [line]
            elif in_code and is_code_line:
                code_buffer.append(line)
            elif in_code and not is_code_line:
                # End code block
                result.extend(code_buffer)
                result.append("```")
                in_code = False
                code_buffer = []
                result.append(line)
            else:
                result.append(line)

        # Close any open code block
        if in_code:
            result.extend(code_buffer)
            result.append("```")

        return "\n".join(result)

    @classmethod
    def _normalize_paragraphs(cls, text: str) -> str:
        """Ensure proper paragraph spacing."""
        # Collapse multiple blank lines to two
        text = re.sub(r"\n{3,}", "\n\n", text)
        # Remove trailing whitespace per line
        text = "\n".join(line.rstrip() for line in text.split("\n"))
        return text
```

- [ ] **Step 4: Run tests**

```bash
pytest tests/test_markdown_formatter.py -v
```

Expected: All 7 tests pass. Some code detection heuristics may need tuning — adjust the regex patterns and re-run until tests pass. This is expected iterative work.

- [ ] **Step 5: Commit**

```bash
git add src/realtest_mcp/ingestion/markdown_formatter.py tests/test_markdown_formatter.py
git commit -m "feat: markdown formatter for PDF text conversion"
```

---

## Task 5: Element Detail Parser

**Files:**
- Create: `src/realtest_mcp/ingestion/element_parser.py`
- Create: `tests/test_element_parser.py`

- [ ] **Step 1: Write failing tests**

Create `tests/test_element_parser.py`:
```python
"""Tests for parsing element detail fields from raw chunk text."""

import pytest
from realtest_mcp.ingestion.element_parser import ElementParser


def test_parse_full_element():
    raw = """Category
Strategy Elements
Description
Commission amount, in instrument or account currency units, for each trade
Input
Formula specifying a commission amount
Notes
If your broker charges no commissions, omit this formula or set it to 0.
Commission is calculated separately for entry and exit."""
    result = ElementParser.parse(raw, "17.18.103. Commission")
    assert result.element_name == "commission"
    assert result.element_name_display == "Commission"
    assert result.category == "Strategy Elements"
    assert result.section_number == "17.18.103"
    assert "Commission amount" in result.description
    assert "Formula specifying" in result.fields.get("Input", "")
    assert "charges no commissions" in result.fields.get("Notes", "")


def test_parse_element_missing_fields():
    raw = """Category
Bar Data Values
Description
Dividend amount ($/share) earned on an ex-dividend date"""
    result = ElementParser.parse(raw, "17.18.147. Dividend")
    assert result.category == "Bar Data Values"
    assert "Syntax" not in result.fields
    assert "Parameters" not in result.fields


def test_parse_element_with_syntax():
    raw = """Category
Indicator Functions
Description
Wilder's Average True Range
Syntax
ATR(len)
Parameters
len - lookback period
Notes
Uses original Welles Wilder calculation."""
    result = ElementParser.parse(raw, "17.18.67. ATR")
    assert result.fields["Syntax"] == "ATR(len)"
    assert "len - lookback period" in result.fields["Parameters"]


def test_alias_splitting():
    title = "17.18.150. EMA or XAvg"
    names = ElementParser.split_aliases(title)
    assert names == [("ema", "EMA"), ("xavg", "XAvg")]


def test_alias_no_split():
    title = "17.18.103. Commission"
    names = ElementParser.split_aliases(title)
    assert names == [("commission", "Commission")]


def test_format_as_markdown():
    raw = """Category
Indicator Functions
Description
Wilder's Average True Range
Syntax
ATR(len)
Parameters
len - lookback period"""
    result = ElementParser.parse(raw, "17.18.67. ATR")
    md = result.to_markdown()
    assert md.startswith("## ATR")
    assert "**Category:** Indicator Functions" in md
    assert "**Syntax:** ATR(len)" in md
```

- [ ] **Step 2: Run tests to verify failure**

```bash
pytest tests/test_element_parser.py -v
```

- [ ] **Step 3: Implement element parser**

Create `src/realtest_mcp/ingestion/element_parser.py`:
```python
"""Parse element detail fields from raw PDF chunk text."""

import re
from dataclasses import dataclass, field as dataclass_field


# Field labels that appear in element detail blocks
FIELD_LABELS = [
    "Category", "Description", "Input", "Syntax", "Parameters",
    "Choices", "Default", "Notes", "Example", "Return Value", "See also",
]


@dataclass
class ParsedElement:
    """A parsed element detail with structured fields."""
    element_name: str          # lowercased for matching
    element_name_display: str  # original case for display
    section_number: str
    category: str
    description: str
    fields: dict[str, str]     # all parsed fields except Category/Description

    def to_markdown(self) -> str:
        """Format as clean markdown."""
        lines = [f"## {self.element_name_display}"]

        if self.category:
            lines.append(f"**Category:** {self.category}")
        if self.description:
            lines.append(f"**Description:** {self.description}")

        for label in FIELD_LABELS:
            if label in ("Category", "Description"):
                continue
            if label in self.fields:
                value = self.fields[label]
                if "\n" in value:
                    lines.append(f"**{label}:**")
                    lines.append(value)
                else:
                    lines.append(f"**{label}:** {value}")

        return "\n".join(lines)


class ElementParser:
    """Parse element detail blocks into structured data."""

    @staticmethod
    def parse(raw_text: str, title: str) -> ParsedElement:
        """Parse raw element text into a ParsedElement."""
        section_number = ElementParser._extract_section_number(title)
        name_display = ElementParser._extract_name(title)
        name = name_display.lower()

        fields = ElementParser._extract_fields(raw_text)
        category = fields.pop("Category", "")
        description = fields.pop("Description", "")

        return ParsedElement(
            element_name=name,
            element_name_display=name_display,
            section_number=section_number,
            category=category,
            description=description,
            fields=fields,
        )

    @staticmethod
    def split_aliases(title: str) -> list[tuple[str, str]]:
        """Split 'EMA or XAvg' into [('ema','EMA'), ('xavg','XAvg')]."""
        name = ElementParser._extract_name(title)
        if " or " in name:
            parts = name.split(" or ")
            return [(p.strip().lower(), p.strip()) for p in parts]
        return [(name.lower(), name)]

    @staticmethod
    def _extract_fields(raw_text: str) -> dict[str, str]:
        """Extract known field labels and their content from raw text."""
        fields = {}
        # Build regex that matches any known label at line start
        label_pattern = "|".join(re.escape(l) for l in FIELD_LABELS)
        pattern = re.compile(
            rf"^({label_pattern})\n(.*?)(?=^(?:{label_pattern})\n|\Z)",
            re.MULTILINE | re.DOTALL,
        )
        for match in pattern.finditer(raw_text):
            label = match.group(1)
            content = match.group(2).strip()
            fields[label] = content
        return fields

    @staticmethod
    def _extract_section_number(title: str) -> str:
        match = re.match(r"^([\d.]+)\.\s", title)
        return match.group(1) if match else ""

    @staticmethod
    def _extract_name(title: str) -> str:
        match = re.match(r"^[\d.]+\.\s+(.+)$", title)
        return match.group(1).strip() if match else title
```

- [ ] **Step 4: Run tests**

```bash
pytest tests/test_element_parser.py -v
```

Expected: All 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/realtest_mcp/ingestion/element_parser.py tests/test_element_parser.py
git commit -m "feat: element detail parser with field extraction and alias splitting"
```

---

## Task 6: Category Summary Parser

**Files:**
- Create: `src/realtest_mcp/ingestion/category_parser.py`
- Create: `tests/test_category_parser.py`

- [ ] **Step 1: Write failing tests**

Create `tests/test_category_parser.py`:
```python
"""Tests for parsing 17.17 category summary lists."""

import pytest
from realtest_mcp.ingestion.category_parser import CategoryParser


def test_parse_category_list():
    raw = """All the elements of a trading strategy definition.
\u00b7
Allocation - dollars allocated to this strategy
\u00b7
Commission - commission per entry or exit transaction in dollars
\u00b7
EntrySetup - true/false condition for whether a stock is a setup"""
    result = CategoryParser.parse(raw)
    assert len(result) == 3
    assert result["allocation"] == "dollars allocated to this strategy"
    assert result["commission"] == "commission per entry or exit transaction in dollars"
    assert result["entrysetup"] == "true/false condition for whether a stock is a setup"


def test_parse_with_alias():
    raw = """\u00b7
EMA or XAvg - exponential moving average
\u00b7
MA or Avg - simple moving average"""
    result = CategoryParser.parse(raw)
    # Both alias forms should be present
    assert result["ema"] == "exponential moving average"
    assert result["xavg"] == "exponential moving average"
    assert result["ma"] == "simple moving average"
    assert result["avg"] == "simple moving average"


def test_parse_empty_returns_empty():
    result = CategoryParser.parse("")
    assert result == {}
```

- [ ] **Step 2: Run tests to verify failure**

```bash
pytest tests/test_category_parser.py -v
```

- [ ] **Step 3: Implement category parser**

Create `src/realtest_mcp/ingestion/category_parser.py`:
```python
"""Parse 17.17 category summary lists into name:description mappings."""

import re


class CategoryParser:
    """Parses category listing text into element name → short description pairs."""

    @staticmethod
    def parse(raw_text: str) -> dict[str, str]:
        """Parse bullet list of 'Name - description' into {lowered_name: description}.

        Handles aliases like 'EMA or XAvg - description' by creating entries
        for both 'ema' and 'xavg'.
        """
        result = {}

        # Match lines like "Name - description" (after bullet cleanup)
        # The bullet char might be on a separate line or inline
        cleaned = re.sub(r"\u00b7\s*", "", raw_text)

        for match in re.finditer(
            r"^([A-Za-z#?.]\S*(?:\s+or\s+\S+)?)\s+-\s+(.+)$",
            cleaned,
            re.MULTILINE,
        ):
            name_part = match.group(1).strip()
            description = match.group(2).strip()

            if " or " in name_part:
                for alias in name_part.split(" or "):
                    result[alias.strip().lower()] = description
            else:
                result[name_part.lower()] = description

        return result
```

- [ ] **Step 4: Run tests**

```bash
pytest tests/test_category_parser.py -v
```

Expected: All 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/realtest_mcp/ingestion/category_parser.py tests/test_category_parser.py
git commit -m "feat: category summary parser for 17.17 name:description pairs"
```

---

## Task 7: Script Parser

**Files:**
- Create: `src/realtest_mcp/ingestion/script_parser.py`
- Create: `tests/test_script_parser.py`

- [ ] **Step 1: Write failing tests**

Create `tests/test_script_parser.py`:
```python
"""Tests for .rts script file reading."""

import pytest
from realtest_mcp.ingestion.script_parser import ScriptParser


def test_parse_script_file(tmp_path):
    script = tmp_path / "test.rts"
    script.write_text("Strategy MyStrat:\n  EntrySetup: C > MA(C, 20)\n")
    result = ScriptParser.parse_file(str(script))
    assert result.filename == "test.rts"
    assert result.file_path == str(script)
    assert "EntrySetup" in result.content


def test_parse_directory(tmp_path):
    (tmp_path / "a.rts").write_text("Strategy A:\n  Side: Long\n")
    (tmp_path / "b.rts").write_text("Strategy B:\n  Side: Short\n")
    (tmp_path / "not_rts.txt").write_text("ignored")
    results = list(ScriptParser.parse_directory(str(tmp_path)))
    assert len(results) == 2
    filenames = {r.filename for r in results}
    assert filenames == {"a.rts", "b.rts"}


def test_parse_wraps_in_code_block(tmp_path):
    script = tmp_path / "test.rts"
    script.write_text("Data:\n  sma: MA(C, 20)\n")
    result = ScriptParser.parse_file(str(script))
    assert result.content.startswith("```rts\n")
    assert result.content.endswith("\n```")


def test_parse_handles_encoding(tmp_path):
    """Scripts should be readable even with non-UTF8 chars."""
    script = tmp_path / "test.rts"
    script.write_bytes(b"Strategy:\n  // price \xa3100\n")  # Latin-1 pound sign
    result = ScriptParser.parse_file(str(script))
    assert "Strategy" in result.content
```

- [ ] **Step 2: Run tests to verify failure**

```bash
pytest tests/test_script_parser.py -v
```

- [ ] **Step 3: Implement script parser**

Create `src/realtest_mcp/ingestion/script_parser.py`:
```python
"""Read .rts script files into chunks."""

from dataclasses import dataclass
from pathlib import Path


@dataclass
class ScriptChunk:
    """A parsed .rts script file."""
    filename: str
    file_path: str
    content: str  # Wrapped in fenced code block


class ScriptParser:
    """Reads .rts files from disk."""

    @staticmethod
    def parse_file(path: str) -> ScriptChunk:
        """Read a single .rts file and wrap content in a code block."""
        p = Path(path)
        try:
            raw = p.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            raw = p.read_text(encoding="latin-1")

        content = f"```rts\n{raw.rstrip()}\n```"
        return ScriptChunk(
            filename=p.name,
            file_path=str(p),
            content=content,
        )

    @staticmethod
    def parse_directory(directory: str) -> list[ScriptChunk]:
        """Read all .rts files from a directory (non-recursive)."""
        results = []
        for p in sorted(Path(directory).glob("*.rts")):
            if p.is_file():
                results.append(ScriptParser.parse_file(str(p)))
        return results
```

- [ ] **Step 4: Run tests**

```bash
pytest tests/test_script_parser.py -v
```

Expected: All 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/realtest_mcp/ingestion/script_parser.py tests/test_script_parser.py
git commit -m "feat: script parser for .rts files"
```

---

## Task 8: Vector Store Wrapper

**Files:**
- Create: `src/realtest_mcp/store/vector_store.py`
- Create: `tests/test_vector_store.py`

- [ ] **Step 1: Write failing tests**

Create `tests/test_vector_store.py`:
```python
"""Tests for ChromaDB vector store wrapper."""

import pytest
from realtest_mcp.store.vector_store import VectorStore


@pytest.fixture
def store(tmp_path):
    return VectorStore(str(tmp_path / "test_chromadb"))


def test_add_and_get_by_metadata(store):
    store.add_documents(
        ids=["elem_1"],
        documents=["## ATR\n**Category:** Indicator Functions"],
        metadatas=[{
            "chunk_type": "element_detail",
            "element_name": "atr",
            "element_name_display": "ATR",
            "category": "Indicator Functions",
        }],
    )
    results = store.get_by_element_name("atr")
    assert len(results) == 1
    assert "ATR" in results[0]["document"]


def test_get_by_element_name_case_insensitive(store):
    store.add_documents(
        ids=["elem_1"],
        documents=["## Commission"],
        metadatas=[{"chunk_type": "element_detail", "element_name": "commission"}],
    )
    # Query with different case — name is lowercased at ingest, query lowercases too
    results = store.get_by_element_name("Commission")
    assert len(results) == 1


def test_semantic_search(store):
    store.add_documents(
        ids=["elem_1", "elem_2"],
        documents=[
            "## ATR\nWilder's Average True Range indicator for volatility",
            "## Commission\nCommission amount per trade in dollars",
        ],
        metadatas=[
            {"chunk_type": "element_detail", "category": "Indicator Functions"},
            {"chunk_type": "element_detail", "category": "Strategy Elements"},
        ],
    )
    results = store.search("volatility measurement", top_k=1)
    assert len(results) == 1
    assert "ATR" in results[0]["document"]


def test_search_with_category_filter(store):
    store.add_documents(
        ids=["elem_1", "elem_2"],
        documents=[
            "## ATR\nAverage True Range for volatility",
            "## Commission\nCommission for trades",
        ],
        metadatas=[
            {"chunk_type": "element_detail", "category": "Indicator Functions"},
            {"chunk_type": "element_detail", "category": "Strategy Elements"},
        ],
    )
    results = store.search("trading costs", category="Strategy Elements", top_k=5)
    # Should only return Commission, not ATR
    assert all("Strategy Elements" in r["metadata"].get("category", "") for r in results)


def test_search_excludes_scripts_from_docs(store):
    store.add_documents(
        ids=["doc_1", "script_1"],
        documents=[
            "## ATR\nAverage True Range indicator",
            "```rts\nData:\n  atr20: ATR(20)\n```",
        ],
        metadatas=[
            {"chunk_type": "element_detail", "source": "pdf"},
            {"chunk_type": "script", "source_type": "example"},
        ],
    )
    results = store.search_docs("ATR indicator", top_k=5)
    assert all(r["metadata"].get("chunk_type") != "script" for r in results)


def test_list_elements_by_category(store):
    store.add_documents(
        ids=["e1", "e2", "e3"],
        documents=["doc1", "doc2", "doc3"],
        metadatas=[
            {"chunk_type": "element_detail", "element_name": "atr", "element_name_display": "ATR", "category": "Indicator Functions", "summary": "average true range"},
            {"chunk_type": "element_detail", "element_name": "rsi", "element_name_display": "RSI", "category": "Indicator Functions", "summary": "relative strength index"},
            {"chunk_type": "element_detail", "element_name": "commission", "element_name_display": "Commission", "category": "Strategy Elements", "summary": "trade commission"},
        ],
    )
    elements = store.list_elements("Indicator Functions")
    assert len(elements) == 2
    names = [e["element_name_display"] for e in elements]
    assert "ATR" in names
    assert "RSI" in names


def test_list_categories(store):
    store.add_documents(
        ids=["e1", "e2"],
        documents=["doc1", "doc2"],
        metadatas=[
            {"chunk_type": "element_detail", "category": "Indicator Functions"},
            {"chunk_type": "element_detail", "category": "Strategy Elements"},
        ],
    )
    categories = store.list_categories()
    assert "Indicator Functions" in categories
    assert "Strategy Elements" in categories


def test_get_primer_chunks(store):
    store.add_documents(
        ids=["n1", "n2", "n3"],
        documents=["primer content 1", "non-primer", "primer content 2"],
        metadatas=[
            {"chunk_type": "narrative", "is_primer": "true", "primer_order": "1"},
            {"chunk_type": "narrative", "is_primer": "false"},
            {"chunk_type": "narrative", "is_primer": "true", "primer_order": "2"},
        ],
    )
    primers = store.get_primer_chunks()
    assert len(primers) == 2
    assert primers[0]["document"] == "primer content 1"
    assert primers[1]["document"] == "primer content 2"


def test_wipe_collection(store):
    store.add_documents(ids=["e1"], documents=["test"], metadatas=[{"chunk_type": "element_detail"}])
    assert store.count() > 0
    store.wipe()
    assert store.count() == 0


def test_is_populated(store):
    assert not store.is_populated()
    store.add_documents(ids=["e1"], documents=["test"], metadatas=[{"chunk_type": "element_detail"}])
    assert store.is_populated()
```

- [ ] **Step 2: Run tests to verify failure**

```bash
pytest tests/test_vector_store.py -v
```

- [ ] **Step 3: Implement vector store**

Create `src/realtest_mcp/store/vector_store.py`:
```python
"""ChromaDB vector store wrapper for RealTest MCP."""

import os
import sys
from datetime import datetime, timezone

import chromadb
from chromadb.utils.embedding_functions import SentenceTransformerEmbeddingFunction

COLLECTION_NAME = "realtest_docs"
EMBEDDING_MODEL = "BAAI/bge-base-en-v1.5"


class VectorStore:
    """Wrapper around ChromaDB for document storage and retrieval."""

    def __init__(self, db_path: str):
        os.makedirs(db_path, exist_ok=True)
        self._client = chromadb.PersistentClient(path=db_path)
        self._embedding_fn = SentenceTransformerEmbeddingFunction(
            model_name=EMBEDDING_MODEL,
        )
        self._collection = self._client.get_or_create_collection(
            name=COLLECTION_NAME,
            embedding_function=self._embedding_fn,
        )
        self._db_path = db_path

    def add_documents(
        self,
        ids: list[str],
        documents: list[str],
        metadatas: list[dict],
    ) -> None:
        """Add documents with metadata to the collection."""
        self._collection.add(ids=ids, documents=documents, metadatas=metadatas)

    def get_by_element_name(self, name: str) -> list[dict]:
        """Exact match lookup by lowercased element name."""
        results = self._collection.get(
            where={"element_name": name.lower()},
            include=["documents", "metadatas"],
        )
        return self._format_get_results(results)

    def search(
        self,
        query: str,
        top_k: int = 5,
        category: str | None = None,
        chunk_type: str | None = None,
    ) -> list[dict]:
        """Semantic search with optional metadata filters."""
        where = self._build_where(category=category, chunk_type=chunk_type)
        results = self._collection.query(
            query_texts=[query],
            n_results=top_k,
            where=where if where else None,
            include=["documents", "metadatas", "distances"],
        )
        return self._format_query_results(results)

    def search_docs(self, query: str, top_k: int = 5, category: str | None = None) -> list[dict]:
        """Search documentation only (exclude scripts)."""
        where_conditions = [{"source": "pdf"}]
        if category:
            where_conditions.append({"category": category})
        if len(where_conditions) == 1:
            where = where_conditions[0]
        else:
            where = {"$and": where_conditions}
        results = self._collection.query(
            query_texts=[query],
            n_results=top_k,
            where=where,
            include=["documents", "metadatas", "distances"],
        )
        return self._format_query_results(results)

    def search_scripts(
        self,
        query: str,
        top_k: int = 3,
        source_type: str | None = None,
    ) -> list[dict]:
        """Search script chunks with optional source filter."""
        where = {"chunk_type": "script"}
        if source_type:
            where = {"$and": [{"chunk_type": "script"}, {"source_type": source_type}]}
        results = self._collection.query(
            query_texts=[query],
            n_results=top_k,
            where=where,
            include=["documents", "metadatas", "distances"],
        )
        return self._format_query_results(results)

    def keyword_search_scripts(self, keywords: list[str], top_k: int = 3, source_type: str | None = None) -> list[dict]:
        """Keyword fallback search for scripts."""
        where = {"chunk_type": "script"}
        if source_type:
            where = {"$and": [{"chunk_type": "script"}, {"source_type": source_type}]}
        results = self._collection.get(
            where=where,
            include=["documents", "metadatas"],
        )
        # Manual keyword matching
        matches = []
        for doc, meta, doc_id in zip(
            results.get("documents", []),
            results.get("metadatas", []),
            results.get("ids", []),
        ):
            doc_lower = doc.lower()
            if any(kw.lower() in doc_lower for kw in keywords):
                matches.append({"id": doc_id, "document": doc, "metadata": meta})
        return matches[:top_k]

    def get_section(self, title: str) -> list[dict]:
        """Get narrative sections matching title (substring) or section_number (prefix).

        Note: This fetches all narrative chunks and filters in Python.
        Acceptable for ~100-200 chunks. If corpus grows significantly,
        consider ChromaDB where-filter optimizations.
        """
        results = self._collection.get(
            where={"chunk_type": "narrative"},
            include=["documents", "metadatas"],
        )
        matches = []
        title_lower = title.lower()
        for doc, meta, doc_id in zip(
            results.get("documents", []),
            results.get("metadatas", []),
            results.get("ids", []),
        ):
            section_title = meta.get("section_title", "").lower()
            section_number = meta.get("section_number", "")
            parent_section = meta.get("parent_section", "").lower()

            # Match by title substring
            if title_lower in section_title:
                matches.append({"id": doc_id, "document": doc, "metadata": meta})
            # Match by section number prefix (dot-boundary)
            elif section_number == title or section_number.startswith(title + "."):
                matches.append({"id": doc_id, "document": doc, "metadata": meta})
            # Match children by parent (exact match, not substring)
            elif title_lower == parent_section:
                matches.append({"id": doc_id, "document": doc, "metadata": meta})

        # Sort by section_number
        matches.sort(key=lambda m: m["metadata"].get("section_number", ""))
        return matches

    def get_primer_chunks(self) -> list[dict]:
        """Get all primer-tagged narrative chunks in order."""
        results = self._collection.get(
            where={"is_primer": "true"},
            include=["documents", "metadatas"],
        )
        items = self._format_get_results(results)
        items.sort(key=lambda x: int(x["metadata"].get("primer_order", "999")))
        return items

    def list_elements(self, category: str) -> list[dict]:
        """List all element details in a category (case-insensitive)."""
        # ChromaDB where filters are case-sensitive, so we fetch all elements
        # and filter in Python. Acceptable for ~583 elements.
        results = self._collection.get(
            where={"chunk_type": "element_detail"},
            include=["metadatas"],
        )
        category_lower = category.lower()
        items = []
        for meta in results.get("metadatas", []):
            if meta.get("category", "").lower() == category_lower:
                items.append({
                    "element_name_display": meta.get("element_name_display", ""),
                    "summary": meta.get("summary", ""),
                })
        items.sort(key=lambda x: x["element_name_display"].lower())
        return items

    def list_categories(self) -> dict[str, int]:
        """Return all categories with element counts."""
        results = self._collection.get(
            where={"chunk_type": "element_detail"},
            include=["metadatas"],
        )
        counts = {}
        for meta in results.get("metadatas", []):
            cat = meta.get("category", "Unknown")
            counts[cat] = counts.get(cat, 0) + 1
        return counts

    def set_ingest_timestamp(self) -> None:
        """Store ingest timestamp as a metadata document."""
        now = datetime.now(timezone.utc).isoformat()
        # Remove existing meta doc if present
        try:
            self._collection.delete(ids=["__ingest_meta__"])
        except Exception:
            pass
        self._collection.add(
            ids=["__ingest_meta__"],
            documents=["Ingest metadata"],
            metadatas=[{"chunk_type": "ingest_meta", "ingest_time": now}],
        )

    def get_ingest_time(self) -> str | None:
        """Get the last ingest timestamp."""
        try:
            result = self._collection.get(ids=["__ingest_meta__"], include=["metadatas"])
            if result["metadatas"]:
                return result["metadatas"][0].get("ingest_time")
        except Exception:
            pass
        return None

    def wipe(self) -> None:
        """Delete all documents in the collection."""
        self._client.delete_collection(COLLECTION_NAME)
        self._collection = self._client.get_or_create_collection(
            name=COLLECTION_NAME,
            embedding_function=self._embedding_fn,
        )

    def count(self) -> int:
        """Return total document count."""
        return self._collection.count()

    def is_populated(self) -> bool:
        """Check if any documents exist."""
        return self.count() > 0

    def print_status(self) -> None:
        """Print database status to stdout."""
        results = self._collection.get(include=["metadatas"])
        metadatas = results.get("metadatas", [])

        counts = {"element_detail": 0, "narrative": 0, "script_example": 0, "script_user": 0}
        for meta in metadatas:
            ct = meta.get("chunk_type", "")
            if ct == "element_detail":
                counts["element_detail"] += 1
            elif ct == "narrative":
                counts["narrative"] += 1
            elif ct == "script":
                st = meta.get("source_type", "")
                if st == "user_script":
                    counts["script_user"] += 1
                else:
                    counts["script_example"] += 1

        ingest_time = self.get_ingest_time() or "Never"

        print(f"RealTest MCP Status")
        print(f"  Database: {self._db_path}")
        print(f"  Collection: {COLLECTION_NAME}")
        print(f"  Element details: {counts['element_detail']}")
        print(f"  Narrative sections: {counts['narrative']}")
        print(f"  Scripts (example): {counts['script_example']}")
        print(f"  Scripts (user): {counts['script_user']}")
        print(f"  Embedding model: {EMBEDDING_MODEL}")
        print(f"  Last ingest: {ingest_time}")

    @staticmethod
    def _build_where(category: str | None = None, chunk_type: str | None = None) -> dict | None:
        conditions = []
        if category:
            conditions.append({"category": category})
        if chunk_type:
            conditions.append({"chunk_type": chunk_type})
        if not conditions:
            return None
        if len(conditions) == 1:
            return conditions[0]
        return {"$and": conditions}

    @staticmethod
    def _format_get_results(results: dict) -> list[dict]:
        items = []
        for doc, meta, doc_id in zip(
            results.get("documents", []),
            results.get("metadatas", []),
            results.get("ids", []),
        ):
            items.append({"id": doc_id, "document": doc, "metadata": meta})
        return items

    @staticmethod
    def _format_query_results(results: dict) -> list[dict]:
        items = []
        if not results.get("ids") or not results["ids"][0]:
            return items
        for doc, meta, doc_id, dist in zip(
            results["documents"][0],
            results["metadatas"][0],
            results["ids"][0],
            results["distances"][0],
        ):
            items.append({"id": doc_id, "document": doc, "metadata": meta, "distance": dist})
        return items
```

- [ ] **Step 4: Run tests**

```bash
pytest tests/test_vector_store.py -v
```

Expected: All 10 tests pass. Note: first run will download the BGE-base-en-v1.5 model (~440MB) — this is a one-time download.

- [ ] **Step 5: Commit**

```bash
git add src/realtest_mcp/store/vector_store.py tests/test_vector_store.py
git commit -m "feat: ChromaDB vector store with metadata filtering and search"
```

---

## Task 9: Ingestion Orchestrator

**Files:**
- Create: `src/realtest_mcp/ingestion/ingest.py`
- Create: `tests/test_integration.py`

- [ ] **Step 1: Implement ingestion orchestrator**

Create `src/realtest_mcp/ingestion/ingest.py`:
```python
"""Orchestrate PDF extraction and script indexing into ChromaDB."""

import hashlib
import sys
from pathlib import Path

from realtest_mcp.config import Config
from realtest_mcp.ingestion.pdf_parser import PdfParser
from realtest_mcp.ingestion.chunker import Chunker
from realtest_mcp.ingestion.markdown_formatter import MarkdownFormatter
from realtest_mcp.ingestion.element_parser import ElementParser
from realtest_mcp.ingestion.category_parser import CategoryParser
from realtest_mcp.ingestion.script_parser import ScriptParser
from realtest_mcp.store.vector_store import VectorStore

# Primer chunks: section_number → primer_order
PRIMER_SECTIONS = {
    "17": 1,
    "17.1": 2,
    "17.15": 3,
    "17.15.2": 4,
    "17.15.6": 5,
    "17.15.7": 6,
    "17.16": 7,
    "17.16.1": 8,
    "17.16.2": 9,
    "16.11": 10,
    "16.6": 11,
    "16.1": 12,
}


def _chunk_id(source: str, name: str) -> str:
    """Generate a deterministic chunk ID."""
    return hashlib.sha256(f"{source}:{name}".encode()).hexdigest()[:16]


def run_ingest():
    """Main ingestion entry point."""
    config = Config.load()

    # Validate PDF exists
    if not Path(config.pdf_path).exists():
        print(f"Error: PDF not found at {config.pdf_path}", file=sys.stderr)
        sys.exit(1)

    store = VectorStore(config.db_path)
    store.wipe()

    warnings = []
    element_count = 0
    narrative_count = 0
    script_count = 0

    # --- Phase 1: Parse PDF ---
    print(f"Parsing PDF: {config.pdf_path}")
    parser = PdfParser(config.pdf_path)
    full_text = parser.extract_full_text()
    toc = parser.get_toc()

    # --- Phase 2: Chunk the text stream ---
    print("Splitting text stream on TOC titles...")
    chunker = Chunker(toc, full_text)
    raw_chunks = chunker.split()
    print(f"  {len(raw_chunks)} raw chunks extracted")

    # --- Phase 3: Parse 17.17 category summaries ---
    print("Parsing category summaries...")
    summaries = {}  # lowered_name → short_description
    for chunk in raw_chunks:
        if chunk.section_number.startswith("17.17."):
            parsed = CategoryParser.parse(chunk.text)
            summaries.update(parsed)
    print(f"  {len(summaries)} element summaries found")

    # --- Phase 4: Process element details (17.18.*) ---
    print("Processing element details...")
    ids, docs, metas = [], [], []
    for chunk in raw_chunks:
        if not chunk.section_number.startswith("17.18.") or chunk.title.startswith("17.18. "):
            continue
        try:
            parsed = ElementParser.parse(chunk.text, chunk.title)
            markdown = MarkdownFormatter.format(parsed.to_markdown())
            aliases = ElementParser.split_aliases(chunk.title)

            for name_lower, name_display in aliases:
                chunk_id = _chunk_id("element", name_lower)
                summary = summaries.get(name_lower)
                meta = {
                    "chunk_type": "element_detail",
                    "element_name": name_lower,
                    "element_name_display": name_display,
                    "category": parsed.category,
                    "section_number": parsed.section_number,
                    "source": "pdf",
                }
                if summary:
                    meta["summary"] = summary

                ids.append(chunk_id)
                docs.append(markdown)
                metas.append(meta)
                element_count += 1

        except Exception as e:
            warnings.append(f"  Warning: Failed to parse element '{chunk.title}': {e}")

    if ids:
        store.add_documents(ids=ids, documents=docs, metadatas=metas)
    print(f"  {element_count} element entries indexed")

    # --- Phase 5: Process narrative sections ---
    print("Processing narrative sections...")
    ids, docs, metas = [], [], []
    for chunk in raw_chunks:
        # Skip 17.17 (category listings) and 17.18 (element details)
        if chunk.section_number.startswith("17.17") or chunk.section_number.startswith("17.18"):
            continue
        # Skip if no meaningful content
        if len(chunk.text.strip()) < 20:
            continue
        try:
            markdown = MarkdownFormatter.format(chunk.text)
            chunk_id = _chunk_id("narrative", chunk.section_number)
            is_primer = chunk.section_number in PRIMER_SECTIONS
            meta = {
                "chunk_type": "narrative",
                "section_title": chunk.title.split(". ", 1)[-1] if ". " in chunk.title else chunk.title,
                "section_number": chunk.section_number,
                "parent_section": chunk.parent_title or "",
                "is_primer": "true" if is_primer else "false",
                "source": "pdf",
            }
            if is_primer:
                meta["primer_order"] = str(PRIMER_SECTIONS[chunk.section_number])

            ids.append(chunk_id)
            docs.append(markdown)
            metas.append(meta)
            narrative_count += 1

        except Exception as e:
            warnings.append(f"  Warning: Failed to process narrative '{chunk.title}': {e}")

    if ids:
        store.add_documents(ids=ids, documents=docs, metadatas=metas)
    print(f"  {narrative_count} narrative sections indexed")

    # --- Phase 6: Index scripts ---
    print("Indexing scripts...")
    script_paths = [(config.examples_path, "example")]
    for path in config.user_script_paths:
        script_paths.append((path, "user_script"))

    ids, docs, metas = [], [], []
    for script_dir, source_type in script_paths:
        if not Path(script_dir).exists():
            warnings.append(f"  Warning: Script directory not found: {script_dir}")
            continue
        for script in ScriptParser.parse_directory(script_dir):
            try:
                chunk_id = _chunk_id("script", script.file_path)
                ids.append(chunk_id)
                docs.append(script.content)
                metas.append({
                    "chunk_type": "script",
                    "source_type": source_type,
                    "filename": script.filename,
                    "file_path": script.file_path,
                })
                script_count += 1
            except Exception as e:
                warnings.append(f"  Warning: Failed to index script '{script.filename}': {e}")

    if ids:
        store.add_documents(ids=ids, documents=docs, metadatas=metas)
    print(f"  {script_count} scripts indexed")

    # --- Phase 7: Store ingest timestamp ---
    store.set_ingest_timestamp()

    # --- Summary ---
    print()
    if warnings:
        for w in warnings:
            print(w, file=sys.stderr)
        print()

    total = element_count + narrative_count + script_count
    print(f"Ingestion complete: {total} chunks indexed "
          f"({element_count} elements, {narrative_count} narrative, {script_count} scripts). "
          f"{len(warnings)} warnings.")
```

- [ ] **Step 2: Write integration test**

Create `tests/test_integration.py`:
```python
"""Integration tests: full ingest + search pipeline."""

import os
import pytest
from realtest_mcp.ingestion.ingest import run_ingest
from realtest_mcp.store.vector_store import VectorStore
from realtest_mcp.config import Config

PDF_PATH = r"C:\RealTest\RealTest User Guide.pdf"


@pytest.fixture
def ingested_store(tmp_path, monkeypatch):
    """Run full ingestion against the real PDF and return the store."""
    config_content = f"""
[realtest]
pdf_path = "{PDF_PATH.replace(chr(92), chr(92)*2)}"

[scripts]
examples = "C:\\\\RealTest\\\\Scripts\\\\Examples"
user_scripts = []

[database]
path = "{str(tmp_path / 'chromadb').replace(chr(92), chr(92)*2)}"
"""
    config_file = tmp_path / "config.toml"
    config_file.write_text(config_content)
    monkeypatch.setenv("REALTEST_MCP_CONFIG", str(config_file))

    run_ingest()
    return VectorStore(str(tmp_path / "chromadb"))


@pytest.mark.slow
def test_element_count(ingested_store):
    """Should have ~583 element detail entries (with aliases, could be more)."""
    results = ingested_store._collection.get(
        where={"chunk_type": "element_detail"},
        include=["metadatas"],
    )
    assert len(results["ids"]) >= 500  # At least 500 elements


@pytest.mark.slow
def test_get_reference_commission(ingested_store):
    results = ingested_store.get_by_element_name("commission")
    assert len(results) >= 1
    assert "Commission" in results[0]["document"]
    assert "Strategy Elements" in results[0]["metadata"].get("category", "")


@pytest.mark.slow
def test_get_reference_bollinger_bands(ingested_store):
    """BBTop should be findable via semantic search for 'Bollinger Bands'."""
    results = ingested_store.search_docs("Bollinger Bands", top_k=3)
    docs = " ".join(r["document"] for r in results)
    assert "BBTop" in docs or "Bollinger" in docs


@pytest.mark.slow
def test_narrative_sections_exist(ingested_store):
    results = ingested_store._collection.get(
        where={"chunk_type": "narrative"},
        include=["metadatas"],
    )
    assert len(results["ids"]) >= 40


@pytest.mark.slow
def test_primer_chunks(ingested_store):
    primers = ingested_store.get_primer_chunks()
    assert len(primers) == 12  # 12 primer sections defined
    # First should be section 17 intro
    assert "Script Language" in primers[0]["metadata"].get("section_title", "")


@pytest.mark.slow
def test_scripts_indexed(ingested_store):
    results = ingested_store._collection.get(
        where={"chunk_type": "script"},
        include=["metadatas"],
    )
    assert len(results["ids"]) >= 50  # At least 50 example scripts
```

- [ ] **Step 3: Run unit tests (fast)**

```bash
pytest tests/ -v -k "not slow"
```

Expected: All non-slow tests pass.

- [ ] **Step 4: Run integration tests (slow, downloads model, reads real PDF)**

```bash
pytest tests/test_integration.py -v -m slow
```

Expected: All 6 integration tests pass. First run will be slow due to model download + full PDF processing.

- [ ] **Step 5: Commit**

```bash
git add src/realtest_mcp/ingestion/ingest.py tests/test_integration.py
git commit -m "feat: ingestion orchestrator — PDF extraction + script indexing pipeline"
```

---

## Task 10: MCP Server and Tools

**Files:**
- Create: `src/realtest_mcp/server/mcp_server.py`
- Create: `src/realtest_mcp/server/tools.py`
- Create: `tests/test_tools.py`

- [ ] **Step 1: Implement MCP server**

Create `src/realtest_mcp/server/mcp_server.py`:
```python
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
```

- [ ] **Step 2: Implement tools**

Create `src/realtest_mcp/server/tools.py`:
```python
"""MCP tool implementations for RealTest documentation search."""

from mcp.server.fastmcp import FastMCP

from realtest_mcp.store.vector_store import VectorStore

# Module-level store reference, set by register_tools
_store: VectorStore | None = None


def register_tools(mcp: FastMCP, store: VectorStore) -> None:
    """Register all tools with the MCP server."""
    global _store
    _store = store

    @mcp.tool()
    def get_primer() -> str:
        """Load the RealScript mental model — script structure, evaluation semantics, formula syntax, and key concepts. Call this once at the start of any scripting session."""
        _check_populated()
        primers = _store.get_primer_chunks()
        if not primers:
            return "No primer content found. Run 'python -m realtest_mcp ingest' first."
        sections = []
        for p in primers:
            title = p["metadata"].get("section_title", "")
            sections.append(f"## {title}\n\n{p['document']}")
        return "\n\n---\n\n".join(sections)

    @mcp.tool()
    def get_reference(name: str) -> str:
        """Get the exact reference documentation for a RealScript element. Call this before using any element in generated code.

        Args:
            name: Element name (e.g., 'Commission', 'ATR', '#Rank', 'T.DateIn')
        """
        _check_populated()
        # Step 1: Exact match
        results = _store.get_by_element_name(name)
        if results:
            return results[0]["document"]

        # Step 2: Semantic fallback
        results = _store.search(
            query=name,
            top_k=1,
            chunk_type="element_detail",
        )
        if results:
            display = results[0]["metadata"].get("element_name_display", "")
            note = f"_Note: No exact match for '{name}'. Closest match: {display}_\n\n"
            return note + results[0]["document"]

        return f"No reference found for '{name}'."

    @mcp.tool()
    def get_section(title: str) -> str:
        """Fetch a specific documentation section by title or section number. Use for deep dives into topics.

        Args:
            title: Section title (partial match) or section number (e.g., '17.15')
        """
        _check_populated()
        results = _store.get_section(title)
        if not results:
            # Semantic fallback
            results = _store.search(
                query=title,
                top_k=3,
                chunk_type="narrative",
            )
        if not results:
            return f"No section found matching '{title}'."

        sections = []
        for r in results:
            sec_title = r["metadata"].get("section_title", "")
            sec_num = r["metadata"].get("section_number", "")
            sections.append(f"## {sec_num}. {sec_title}\n\n{r['document']}")
        return "\n\n---\n\n".join(sections)

    @mcp.tool()
    def list_elements(category: str = "") -> str:
        """Discover what RealScript elements exist. Call with a category to see elements, or without to see all categories.

        Args:
            category: Optional category name (e.g., 'Strategy Elements', 'Indicator Functions')
        """
        _check_populated()
        if category:
            elements = _store.list_elements(category)
            if not elements:
                return f"No elements found in category '{category}'."
            lines = [f"## {category} ({len(elements)} elements)\n"]
            for e in elements:
                summary = e.get("summary", "")
                display = e["element_name_display"]
                if summary:
                    lines.append(f"- **{display}** — {summary}")
                else:
                    lines.append(f"- **{display}**")
            return "\n".join(lines)
        else:
            categories = _store.list_categories()
            if not categories:
                return "No elements indexed."
            lines = ["## Element Categories\n"]
            for cat, count in sorted(categories.items()):
                lines.append(f"- **{cat}** ({count} elements)")
            return "\n".join(lines)

    @mcp.tool()
    def search_docs(query: str, category: str = "", top_k: int = 5) -> str:
        """Search RealTest documentation by concept or topic. Use when you don't know the exact element name or section title.

        Args:
            query: What to search for
            category: Optional category filter (e.g., 'Strategy Elements')
            top_k: Number of results (default 5)
        """
        _check_populated()
        results = _store.search_docs(
            query=query,
            top_k=top_k,
            category=category or None,
        )
        if not results:
            return f"No documentation found for '{query}'."
        sections = []
        for r in results:
            ct = r["metadata"].get("chunk_type", "")
            name = r["metadata"].get("element_name_display", "") or r["metadata"].get("section_title", "")
            cat = r["metadata"].get("category", "")
            header = f"### {name}"
            if cat:
                header += f" ({cat})"
            sections.append(f"{header}\n\n{r['document']}")
        return "\n\n---\n\n".join(sections)

    @mcp.tool()
    def search_scripts(query: str, source: str = "all", top_k: int = 3) -> str:
        """Find example RealScript files demonstrating a concept or technique.

        Args:
            query: What to search for
            source: 'example', 'user', or 'all' (default 'all')
            top_k: Number of results (default 3)
        """
        _check_populated()
        source_type = None
        if source == "example":
            source_type = "example"
        elif source == "user":
            source_type = "user_script"

        results = _store.search_scripts(
            query=query,
            top_k=top_k,
            source_type=source_type,
        )

        # Keyword fallback if no semantic results
        if not results:
            keywords = query.split()
            results = _store.keyword_search_scripts(
                keywords=keywords,
                top_k=top_k,
                source_type=source_type,
            )

        if not results:
            return f"No scripts found for '{query}'."

        sections = []
        for r in results:
            filename = r["metadata"].get("filename", "unknown")
            content = r["document"]
            # Truncate at 3000 chars
            if len(content) > 3000:
                content = content[:3000] + f"\n\n_... truncated ({len(r['document'])} chars total)_"
            sections.append(f"### {filename}\n\n{content}")
        return "\n\n---\n\n".join(sections)


def _check_populated():
    """Raise a helpful error if the store is empty."""
    if not _store or not _store.is_populated():
        raise RuntimeError(
            "No documents indexed. Run 'python -m realtest_mcp ingest' first."
        )
```

- [ ] **Step 3: Write tool tests**

Create `tests/test_tools.py`:
```python
"""Tests for MCP tool implementations."""

import pytest
from mcp.server.fastmcp import FastMCP

from realtest_mcp.store.vector_store import VectorStore
from realtest_mcp.server.tools import register_tools


@pytest.fixture
def tools_store(tmp_path):
    """Set up a store with sample data and register tools."""
    store = VectorStore(str(tmp_path / "test_chromadb"))

    # Add sample element details
    store.add_documents(
        ids=["e_atr", "e_commission", "e_bbtop"],
        documents=[
            "## ATR\n**Category:** Indicator Functions\n**Description:** Wilder's Average True Range\n**Syntax:** ATR(len)\n**Parameters:** len - lookback period",
            "## Commission\n**Category:** Strategy Elements\n**Description:** Commission amount per trade\n**Input:** Formula",
            "## BBTop\n**Category:** Indicator Functions\n**Description:** Bollinger Band upper line\n**Syntax:** BBTop(len, stdev)",
        ],
        metadatas=[
            {"chunk_type": "element_detail", "element_name": "atr", "element_name_display": "ATR", "category": "Indicator Functions", "summary": "average true range", "source": "pdf"},
            {"chunk_type": "element_detail", "element_name": "commission", "element_name_display": "Commission", "category": "Strategy Elements", "summary": "trade commission", "source": "pdf"},
            {"chunk_type": "element_detail", "element_name": "bbtop", "element_name_display": "BBTop", "category": "Indicator Functions", "summary": "Bollinger Band upper", "source": "pdf"},
        ],
    )

    # Add sample narrative
    store.add_documents(
        ids=["n_17", "n_17_1"],
        documents=[
            "Writing RealTest scripts does not require programming.",
            "When a bar field is referenced, there is a current bar context. Use C[1] for prior bar.",
        ],
        metadatas=[
            {"chunk_type": "narrative", "section_title": "Realtest Script Language", "section_number": "17", "parent_section": "", "is_primer": "true", "primer_order": "1", "source": "pdf"},
            {"chunk_type": "narrative", "section_title": "Bar Offsets", "section_number": "17.1", "parent_section": "17. Realtest Script Language", "is_primer": "true", "primer_order": "2", "source": "pdf"},
        ],
    )

    # Add sample script
    store.add_documents(
        ids=["s_test"],
        documents=["```rts\nStrategy Test:\n  EntrySetup: RSI(C, 14) < 30\n```"],
        metadatas=[{"chunk_type": "script", "source_type": "example", "filename": "test.rts", "file_path": "C:\\test.rts"}],
    )

    mcp = FastMCP("test")
    register_tools(mcp, store)
    return store


def test_get_reference_exact(tools_store):
    from realtest_mcp.server.tools import _store
    results = _store.get_by_element_name("atr")
    assert len(results) == 1
    assert "ATR" in results[0]["document"]


def test_get_reference_case_insensitive(tools_store):
    from realtest_mcp.server.tools import _store
    results = _store.get_by_element_name("Commission")
    assert len(results) == 1


def test_list_elements_with_category(tools_store):
    from realtest_mcp.server.tools import _store
    elements = _store.list_elements("Indicator Functions")
    assert len(elements) == 2
    names = [e["element_name_display"] for e in elements]
    assert "ATR" in names
    assert "BBTop" in names


def test_primer_returns_ordered(tools_store):
    from realtest_mcp.server.tools import _store
    primers = _store.get_primer_chunks()
    assert len(primers) == 2
    assert "Script Language" in primers[0]["metadata"]["section_title"]
    assert "Bar Offsets" in primers[1]["metadata"]["section_title"]


def test_search_docs_excludes_scripts(tools_store):
    from realtest_mcp.server.tools import _store
    results = _store.search_docs("RSI entry setup", top_k=5)
    for r in results:
        assert r["metadata"].get("chunk_type") != "script"
```

- [ ] **Step 4: Run tests**

```bash
pytest tests/test_tools.py -v
```

Expected: All 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/realtest_mcp/server/ tests/test_tools.py
git commit -m "feat: MCP server with 6 tools — get_primer, get_reference, get_section, list_elements, search_docs, search_scripts"
```

---

## Task 11: Update Skills and CLAUDE.md

**Files:**
- Modify: `skills/realscript-authoring/SKILL.md`
- Modify: `skills/realscript-debugging/SKILL.md`
- Modify: `skills/strategy-design/SKILL.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update realscript-authoring skill**

Replace contents of `skills/realscript-authoring/SKILL.md`:
```markdown
# RealScript Authoring

REQUIRED: Use this skill whenever writing any RealScript code.

## Workflow

0. **Call `get_primer`** — do this ONCE at the start of each scripting session.
   This loads the core mental model: script structure, evaluation semantics,
   formula syntax, and key concepts. Do not skip this step.

1. **Discover available elements** with `list_elements` for relevant categories
   (e.g., "Strategy Elements", "Indicator Functions")

2. **For each element you plan to use**, call `get_reference` and verify:
   - Correct parameter names and order
   - Return type
   - Any gotchas noted in the docs

3. **Search for similar examples** with `search_scripts(query, source="example")`
   — use these as structural templates

4. **For complex topics**, call `get_section` to get the full narrative
   (e.g., "Scan and TestScan Sections", "Scaling In or Out of Positions")

5. **Write the script** following the patterns found in the docs and examples

6. **Before presenting output**, re-check every function call against retrieved signatures

## Hard Rules

- ALWAYS call `get_primer` before writing any script
- NEVER write a RealScript function call without first calling `get_reference` for it
- If `get_reference` returns no result, say so — do not guess the syntax
- Use `list_elements` to discover what's available — don't rely on training data
- Prefer patterns from retrieved examples over patterns from training data
- Structure logic in `Data:` section; keep `EntrySetup:` and `ExitRule:` simple references to Data items
- Always include the verified function list at the end of your response

## Template

```
Verified functions:
- ATR(periods) — confirmed via get_reference
- RSI(periods) — confirmed via get_reference
```
```

- [ ] **Step 2: Update realscript-debugging skill**

Replace contents of `skills/realscript-debugging/SKILL.md`:
```markdown
# RealScript Debugging

REQUIRED: Use this skill when a RealScript file isn't working as expected.

## Workflow

1. **List all functions** used in the provided script
2. **For each function**, call `get_reference` and compare:
   - Are parameters in the correct order?
   - Are parameter names spelled correctly?
   - Is the return type being used correctly?
3. **Search for error messages** with `search_docs` if the user has provided one
4. **Check section structure** with `get_section` if the issue might be structural
5. **Flag every discrepancy** found — do not silently fix things
6. **Present a diff** of what needs to change and why, citing the retrieved docs

## Common Issues

- Wrong parameter order (e.g. `Highest(20, H)` instead of `Highest(H, 20)`)
- Using deprecated syntax from old versions
- Incorrect section names (e.g. `Entry:` vs `EntrySetup:`)
- Missing required fields for a section type
```

- [ ] **Step 3: Update strategy-design skill**

Replace contents of `skills/strategy-design/SKILL.md`:
```markdown
# Strategy Design

REQUIRED: Use this skill when translating a trading concept into a RealScript strategy.

## Workflow

1. **Search for similar examples** with `search_scripts` using the trading concept as the query
2. **Discover available elements** with `list_elements("Strategy Elements")` and other relevant categories
3. **Look up each building block** with `get_reference` for exact syntax
4. **Read relevant narrative** with `get_section` for complex topics
   (e.g., "Capacity Constraints", "Dynamic Sizing", "Scaling In or Out")
5. **Scaffold the structure first** — sections only, no logic yet:
   ```
   Settings: ...
   Strategy <Name>:
     EntrySetup:
     EntryLimit:
     ExitRule:
     ExitStop:
     Quantity:
   ```
6. **Fill in the logic** one section at a time, calling `get_reference` for each element

## Principle

Build the structure before the details. A correct skeleton with wrong parameters is easier to debug than a syntactically wrong script with the right intent.
```

- [ ] **Step 4: Update CLAUDE.md**

Replace contents of `CLAUDE.md`:
```markdown
# RealTest MCP — Claude Code Project

This project is the RealTest MCP server (Python). When working in this codebase:

@include skills/realscript-authoring/SKILL.md
@include skills/realscript-debugging/SKILL.md
@include skills/strategy-design/SKILL.md
```

- [ ] **Step 5: Commit**

```bash
git add skills/ CLAUDE.md
git commit -m "docs: update skills and CLAUDE.md for Python MCP tool names"
```

---

## Task 12: MCP Registration and End-to-End Test

**Files:**
- Create: `.mcp.json`
- Modify: `README.md`

- [ ] **Step 1: Create `.mcp.json`**

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

- [ ] **Step 2: Run ingestion against the real PDF**

```bash
cd D:\Code\realtest-mcp
python -m realtest_mcp ingest
```

Expected output:
```
Parsing PDF: C:\RealTest\RealTest User Guide.pdf
Splitting text stream on TOC titles...
  ~749 raw chunks extracted
Parsing category summaries...
  ~300+ element summaries found
Processing element details...
  ~583+ element entries indexed
Processing narrative sections...
  ~100+ narrative sections indexed
Indexing scripts...
  ~97+ scripts indexed

Ingestion complete: ~780+ chunks indexed (...). 0 warnings.
```

- [ ] **Step 3: Verify status**

```bash
python -m realtest_mcp status
```

Expected: Shows counts matching ingestion output.

- [ ] **Step 4: Update README.md**

Replace `README.md` with updated setup instructions for the Python implementation, covering:
- Prerequisites (Python 3.11+)
- Installation (`pip install -e ".[dev]"`)
- Configuration (`config.toml`)
- First run (`python -m realtest_mcp ingest`)
- MCP registration (`.mcp.json`)
- Available tools (6 tools with descriptions)
- Running tests (`pytest`)

- [ ] **Step 5: Commit**

```bash
git add .mcp.json README.md
git commit -m "feat: MCP registration and updated README"
```

- [ ] **Step 6: End-to-end manual test**

Restart Claude Code in the repo directory. Verify:
1. The MCP server starts (check no errors in Claude Code MCP status)
2. `get_primer()` returns the 12-section mental model briefing
3. `get_reference("Commission")` returns the formatted element detail
4. `get_reference("Bollinger Bands")` finds BBTop via semantic fallback
5. `list_elements("Strategy Elements")` returns all strategy elements with summaries
6. `get_section("Formula Syntax")` returns section 17.16 plus children
7. `search_docs("position sizing")` returns relevant results
8. `search_scripts("mean reversion")` returns example scripts

---

## Task Summary

| Task | Component | Dependencies |
|------|-----------|-------------|
| 1 | Project scaffolding + config | None |
| 2 | PDF parser | None |
| 3 | Text stream chunker | Task 2 |
| 4 | Markdown formatter | None |
| 5 | Element detail parser | None |
| 6 | Category summary parser | None |
| 7 | Script parser | None |
| 8 | Vector store wrapper | None |
| 9 | Ingestion orchestrator | Tasks 2-8 |
| 10 | MCP server + tools | Tasks 8-9 |
| 11 | Skill updates | Task 10 |
| 12 | Registration + E2E test | Tasks 9-11 |

Tasks 2, 4, 5, 6, 7, 8 are independent and can be parallelized.
