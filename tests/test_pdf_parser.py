"""Tests for PDF text extraction and TOC parsing."""

import pytest
from realtest_mcp.ingestion.pdf_parser import PdfParser

PDF_PATH = r"C:\RealTest\RealTest User Guide.pdf"


@pytest.fixture
def parser():
    return PdfParser(PDF_PATH)


def test_extract_full_text(parser):
    text = parser.extract_full_text()
    assert len(text) > 100_000
    assert "17.18.103. Commission" in text
    assert "EntrySetup" in text


def test_get_toc(parser):
    toc = parser.get_toc()
    assert len(toc) > 700
    first = toc[0]
    assert len(first) == 3
    assert isinstance(first[0], int)
    assert isinstance(first[1], str)
    assert isinstance(first[2], int)


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
    text = parser.extract_full_text()
    comm_pos = text.index("17.18.103. Commission")
    comp_pos = text.index("17.18.104. Compounded")
    assert comm_pos < comp_pos
