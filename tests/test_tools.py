"""Tests for MCP tools — exercises VectorStore methods directly."""

import pytest

from realtest_mcp.store.vector_store import VectorStore


@pytest.fixture
def populated_store(tmp_path):
    """Create a VectorStore with sample data for tool testing."""
    store = VectorStore(str(tmp_path / "chromadb"))

    ids = [
        "elem_rsi", "elem_sma", "elem_cross",
        "narr_primer_1", "narr_primer_2",
        "script_example_1",
    ]
    documents = [
        "RSI calculates the Relative Strength Index oscillator.",
        "SMA computes a Simple Moving Average over a period.",
        "Cross detects when one series crosses another.",
        "RealScript uses a row-by-row evaluation model.",
        "Formulas reference built-in elements by name.",
        "// Example: RSI strategy\nSetBars = 100\nC1 = RSI(14) < 30\nBuy = C1",
    ]
    metadatas = [
        {
            "chunk_type": "element_detail",
            "element_name": "rsi",
            "element_name_display": "RSI",
            "category": "Oscillators",
            "summary": "Relative Strength Index",
            "source": "pdf",
        },
        {
            "chunk_type": "element_detail",
            "element_name": "sma",
            "element_name_display": "SMA",
            "category": "Moving Averages",
            "summary": "Simple Moving Average",
            "source": "pdf",
        },
        {
            "chunk_type": "element_detail",
            "element_name": "cross",
            "element_name_display": "Cross",
            "category": "Logical",
            "summary": "Detects crossover events",
            "source": "pdf",
        },
        {
            "chunk_type": "narrative",
            "section_title": "Evaluation Model",
            "section_number": "2.1",
            "is_primer": "true",
            "primer_order": "1",
            "source": "pdf",
        },
        {
            "chunk_type": "narrative",
            "section_title": "Formula Syntax",
            "section_number": "2.2",
            "is_primer": "true",
            "primer_order": "2",
            "source": "pdf",
        },
        {
            "chunk_type": "script",
            "file_name": "RSI_Strategy.txt",
            "source_type": "example",
            "source": "script",
        },
    ]

    store.add_documents(ids=ids, documents=documents, metadatas=metadatas)
    return store


def test_get_reference_exact_match(populated_store):
    """get_by_element_name returns exact match."""
    results = populated_store.get_by_element_name("rsi")
    assert len(results) == 1
    assert results[0]["metadata"]["element_name_display"] == "RSI"
    assert "Relative Strength Index" in results[0]["document"]


def test_get_reference_case_insensitive(populated_store):
    """get_by_element_name is case-insensitive (stored lowercased)."""
    results_lower = populated_store.get_by_element_name("sma")
    results_upper = populated_store.get_by_element_name("SMA")
    assert len(results_lower) == 1
    assert len(results_upper) == 1
    assert results_lower[0]["id"] == results_upper[0]["id"]


def test_list_elements_with_category(populated_store):
    """list_elements returns correct elements for a category."""
    items = populated_store.list_elements("Oscillators")
    assert len(items) == 1
    assert items[0]["element_name_display"] == "RSI"
    assert items[0]["summary"] == "Relative Strength Index"


def test_list_elements_case_insensitive(populated_store):
    """list_elements category matching is case-insensitive."""
    items = populated_store.list_elements("oscillators")
    assert len(items) == 1
    assert items[0]["element_name_display"] == "RSI"


def test_primer_chunks_in_order(populated_store):
    """get_primer_chunks returns chunks sorted by primer_order."""
    chunks = populated_store.get_primer_chunks()
    assert len(chunks) == 2
    assert chunks[0]["metadata"]["section_title"] == "Evaluation Model"
    assert chunks[1]["metadata"]["section_title"] == "Formula Syntax"
    assert int(chunks[0]["metadata"]["primer_order"]) < int(chunks[1]["metadata"]["primer_order"])


def test_search_docs_excludes_scripts(populated_store):
    """search_docs only returns pdf-sourced documents, not scripts."""
    results = populated_store.search_docs("RSI strategy", top_k=10)
    for r in results:
        assert r["metadata"].get("source") == "pdf", (
            f"search_docs returned non-pdf source: {r['metadata']}"
        )
