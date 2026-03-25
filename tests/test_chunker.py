"""Tests for TOC-based text stream splitting."""

import pytest
from realtest_mcp.ingestion.chunker import Chunker


@pytest.fixture
def sample_toc():
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
    assert len(chunks) == len(sample_toc)


def test_chunk_content_boundaries(sample_toc, sample_text):
    chunker = Chunker(sample_toc, sample_text)
    chunks = chunker.split()
    lang_chunk = chunks[0]
    assert "Writing scripts does not require programming" in lang_chunk.text
    assert "When a bar field is referenced" not in lang_chunk.text


def test_parent_chunk_has_intro_content(sample_toc, sample_text):
    chunker = Chunker(sample_toc, sample_text)
    chunks = chunker.split()
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
