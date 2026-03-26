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
        metadatas=[
            {
                "chunk_type": "element_detail",
                "element_name": "atr",
                "element_name_display": "ATR",
                "category": "Indicator Functions",
            }
        ],
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
            {
                "chunk_type": "element_detail",
                "element_name": "atr",
                "element_name_display": "ATR",
                "category": "Indicator Functions",
                "summary": "average true range",
            },
            {
                "chunk_type": "element_detail",
                "element_name": "rsi",
                "element_name_display": "RSI",
                "category": "Indicator Functions",
                "summary": "relative strength index",
            },
            {
                "chunk_type": "element_detail",
                "element_name": "commission",
                "element_name_display": "Commission",
                "category": "Strategy Elements",
                "summary": "trade commission",
            },
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
    store.add_documents(
        ids=["e1"], documents=["test"], metadatas=[{"chunk_type": "element_detail"}]
    )
    assert store.count() > 0
    store.wipe()
    assert store.count() == 0


def test_is_populated(store):
    assert not store.is_populated()
    store.add_documents(
        ids=["e1"], documents=["test"], metadatas=[{"chunk_type": "element_detail"}]
    )
    assert store.is_populated()
