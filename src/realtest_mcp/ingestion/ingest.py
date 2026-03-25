"""Ingestion orchestrator — PDF extraction + script indexing pipeline."""

import hashlib
import warnings
from pathlib import Path

from ..config import Config
from ..store.vector_store import VectorStore
from .pdf_parser import PdfParser
from .chunker import Chunker
from .markdown_formatter import MarkdownFormatter
from .element_parser import ElementParser
from .category_parser import CategoryParser
from .script_parser import ScriptParser

PRIMER_SECTIONS = {
    "17": 1, "17.1": 2, "17.15": 3, "17.15.2": 4, "17.15.6": 5,
    "17.15.7": 6, "17.16": 7, "17.16.1": 8, "17.16.2": 9,
    "16.11": 10, "16.6": 11, "16.1": 12,
}


def _chunk_id(source: str, name: str) -> str:
    return hashlib.sha256(f"{source}:{name}".encode()).hexdigest()[:16]


def run_ingest(config: Config | None = None) -> dict:
    """Run the full ingestion pipeline. Returns summary dict."""
    if config is None:
        config = Config.load()

    # --- Validate ---
    pdf_path = config.pdf_path
    if not Path(pdf_path).exists():
        raise FileNotFoundError(f"PDF not found: {pdf_path}")

    print(f"[ingest] PDF: {pdf_path}")

    # --- Init store & wipe ---
    store = VectorStore(config.db_path)
    store.wipe()
    print("[ingest] Wiped ChromaDB collection")

    # --- Extract PDF ---
    print("[ingest] Extracting PDF text and TOC ...")
    parser = PdfParser(pdf_path)
    text = parser.extract_full_text()
    toc = parser.get_toc()
    print(f"[ingest] TOC entries: {len(toc)}")

    # --- Chunk ---
    print("[ingest] Chunking text stream ...")
    chunker = Chunker(toc, text)
    chunks = chunker.split()
    print(f"[ingest] Raw chunks: {len(chunks)}")

    warn_count = 0

    # --- Phase 1: Category summaries (17.17.*) ---
    print("[ingest] Parsing category summaries (17.17.*) ...")
    category_summaries: dict[str, str] = {}
    for chunk in chunks:
        if chunk.section_number.startswith("17.17") and chunk.section_number != "17.17":
            try:
                parsed = CategoryParser.parse(chunk.text)
                category_summaries.update(parsed)
            except Exception as exc:
                warnings.warn(f"Category parse error for {chunk.title}: {exc}")
                warn_count += 1
    print(f"[ingest] Category summaries: {len(category_summaries)} element names mapped")

    # --- Phase 2: Element details (17.18.*) ---
    print("[ingest] Processing element details (17.18.*) ...")
    elem_ids, elem_docs, elem_metas = [], [], []
    for chunk in chunks:
        sn = chunk.section_number
        if not sn.startswith("17.18."):
            continue
        # Skip the parent section "17.18" itself
        if sn == "17.18":
            continue
        try:
            parsed = ElementParser.parse(chunk.text, chunk.title)
            md = MarkdownFormatter.format(parsed.to_markdown())
            aliases = ElementParser.split_aliases(chunk.title)
            for alias_lower, alias_display in aliases:
                summary = category_summaries.get(alias_lower, "")
                doc_id = _chunk_id("pdf", alias_lower)
                elem_ids.append(doc_id)
                elem_docs.append(md)
                elem_metas.append({
                    "chunk_type": "element_detail",
                    "source": "pdf",
                    "element_name": alias_lower,
                    "element_name_display": alias_display,
                    "section_number": sn,
                    "category": parsed.category,
                    "summary": summary,
                })
        except Exception as exc:
            warnings.warn(f"Element parse error for {chunk.title}: {exc}")
            warn_count += 1

    if elem_ids:
        store.add_documents(ids=elem_ids, documents=elem_docs, metadatas=elem_metas)
    print(f"[ingest] Element details indexed: {len(elem_ids)}")

    # --- Phase 3: Narrative sections ---
    print("[ingest] Processing narrative sections ...")
    narr_ids, narr_docs, narr_metas = [], [], []
    for chunk in chunks:
        sn = chunk.section_number
        if sn.startswith("17.17") or sn.startswith("17.18"):
            continue
        if len(chunk.text) < 20:
            continue
        try:
            md = MarkdownFormatter.format(chunk.text)
            # Strip section number prefix from title for section_title
            title_parts = chunk.title.split(". ", 1)
            section_title = title_parts[1] if len(title_parts) > 1 else chunk.title

            is_primer = sn in PRIMER_SECTIONS
            primer_order = PRIMER_SECTIONS.get(sn, 0)

            parent_section = ""
            if chunk.parent_title:
                parent_parts = chunk.parent_title.split(". ", 1)
                parent_section = parent_parts[1] if len(parent_parts) > 1 else chunk.parent_title

            doc_id = _chunk_id("pdf", chunk.title)
            narr_ids.append(doc_id)
            narr_docs.append(md)
            narr_metas.append({
                "chunk_type": "narrative",
                "source": "pdf",
                "section_title": section_title,
                "section_number": sn,
                "parent_section": parent_section,
                "is_primer": "true" if is_primer else "false",
                "primer_order": str(primer_order) if is_primer else "0",
            })
        except Exception as exc:
            warnings.warn(f"Narrative error for {chunk.title}: {exc}")
            warn_count += 1

    if narr_ids:
        store.add_documents(ids=narr_ids, documents=narr_docs, metadatas=narr_metas)
    print(f"[ingest] Narrative sections indexed: {len(narr_ids)}")

    # --- Phase 4: Scripts ---
    print("[ingest] Indexing scripts ...")
    script_ids, script_docs, script_metas = [], [], []

    script_dirs: list[tuple[str, str]] = []
    if config.examples_path:
        script_dirs.append((config.examples_path, "example"))
    for p in config.user_script_paths:
        script_dirs.append((p, "user_script"))

    for dir_path, source_type in script_dirs:
        if not Path(dir_path).is_dir():
            warnings.warn(f"Script directory not found, skipping: {dir_path}")
            warn_count += 1
            continue
        try:
            scripts = ScriptParser.parse_directory(dir_path)
            for sc in scripts:
                try:
                    doc_id = _chunk_id("script", sc.filename)
                    script_ids.append(doc_id)
                    script_docs.append(sc.content)
                    script_metas.append({
                        "chunk_type": "script",
                        "source": "script",
                        "source_type": source_type,
                        "filename": sc.filename,
                        "file_path": sc.file_path,
                    })
                except Exception as exc:
                    warnings.warn(f"Script error for {sc.filename}: {exc}")
                    warn_count += 1
        except Exception as exc:
            warnings.warn(f"Script directory error for {dir_path}: {exc}")
            warn_count += 1

    if script_ids:
        store.add_documents(ids=script_ids, documents=script_docs, metadatas=script_metas)
    print(f"[ingest] Scripts indexed: {len(script_ids)}")

    # --- Timestamp ---
    store.set_ingest_timestamp()

    # --- Summary ---
    summary = {
        "elements": len(elem_ids),
        "narratives": len(narr_ids),
        "scripts": len(script_ids),
        "warnings": warn_count,
    }
    total = summary["elements"] + summary["narratives"] + summary["scripts"]
    print(f"[ingest] Done. Total chunks: {total} | Warnings: {warn_count}")
    print(f"  Elements: {summary['elements']}")
    print(f"  Narratives: {summary['narratives']}")
    print(f"  Scripts: {summary['scripts']}")
    return summary
