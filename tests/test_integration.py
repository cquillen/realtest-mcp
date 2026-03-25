"""Integration tests — run against real PDF and scripts directory.

These tests are slow (several minutes) because they process the full PDF
and embed ~700 chunks.  Mark: @pytest.mark.slow
"""

import os
import pytest
from pathlib import Path

from realtest_mcp.config import Config
from realtest_mcp.ingestion.ingest import run_ingest
from realtest_mcp.store.vector_store import VectorStore

PDF_PATH = r"C:\RealTest\RealTest User Guide.pdf"
EXAMPLES_PATH = r"C:\RealTest\Scripts\Examples"


@pytest.fixture(scope="module")
def ingested_store(tmp_path_factory):
    """Run full ingestion once and return the VectorStore."""
    if not Path(PDF_PATH).exists():
        pytest.skip(f"PDF not found: {PDF_PATH}")

    tmp_dir = tmp_path_factory.mktemp("integration")
    db_path = str(tmp_dir / "chromadb")

    config = Config(
        pdf_path=PDF_PATH,
        examples_path=EXAMPLES_PATH,
        user_script_paths=[],
        db_path=db_path,
    )
    run_ingest(config)
    return VectorStore(db_path)


@pytest.mark.slow
def test_element_count(ingested_store):
    """Should have >= 500 element detail entries."""
    results = ingested_store._collection.get(
        where={"chunk_type": "element_detail"},
        include=["metadatas"],
    )
    count = len(results["ids"])
    assert count >= 500, f"Expected >=500 element details, got {count}"


@pytest.mark.slow
def test_get_reference_commission(ingested_store):
    """Exact match on 'commission' returns Commission with Strategy Elements category."""
    results = ingested_store.get_by_element_name("commission")
    assert len(results) >= 1
    meta = results[0]["metadata"]
    assert meta["element_name_display"] == "Commission"
    assert meta["category"] == "Strategy Elements"


@pytest.mark.slow
def test_get_reference_bollinger_bands(ingested_store):
    """search_docs('Bollinger Bands') finds BBTop or Bollinger in results."""
    results = ingested_store.search_docs("Bollinger Bands", top_k=5)
    assert len(results) >= 1
    found = any(
        "bbtop" in r["metadata"].get("element_name", "").lower()
        or "bollinger" in r["document"].lower()
        for r in results
    )
    assert found, "Expected Bollinger-related result in search"


@pytest.mark.slow
def test_narrative_sections_exist(ingested_store):
    """Should have >= 40 narrative chunks."""
    results = ingested_store._collection.get(
        where={"chunk_type": "narrative"},
        include=["metadatas"],
    )
    count = len(results["ids"])
    assert count >= 40, f"Expected >=40 narrative chunks, got {count}"


@pytest.mark.slow
def test_primer_chunks(ingested_store):
    """Exactly 12 primer chunks; first should mention 'Script Language'."""
    primers = ingested_store.get_primer_chunks()
    assert len(primers) == 12, f"Expected 12 primer chunks, got {len(primers)}"
    first_doc = primers[0]["document"]
    assert "script language" in first_doc.lower() or "script" in first_doc.lower(), (
        f"First primer chunk should mention scripting, got: {first_doc[:200]}"
    )


@pytest.mark.slow
def test_scripts_indexed(ingested_store):
    """Should have >= 50 example scripts."""
    results = ingested_store._collection.get(
        where={"chunk_type": "script"},
        include=["metadatas"],
    )
    count = len(results["ids"])
    assert count >= 50, f"Expected >=50 scripts, got {count}"
