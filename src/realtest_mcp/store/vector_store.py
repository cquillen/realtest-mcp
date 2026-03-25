"""ChromaDB vector store wrapper for RealTest MCP."""

import os
from datetime import datetime, timezone

import chromadb
from chromadb.utils.embedding_functions import SentenceTransformerEmbeddingFunction

COLLECTION_NAME = "realtest_docs"
EMBEDDING_MODEL = "BAAI/bge-base-en-v1.5"


class VectorStore:

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

    def add_documents(self, ids: list[str], documents: list[str], metadatas: list[dict]) -> None:
        self._collection.add(ids=ids, documents=documents, metadatas=metadatas)

    @classmethod
    def _resolve_alias(cls, name_lower: str) -> str | None:
        """Resolve known patterns to their canonical element name."""
        import re
        # InXXX pattern — e.g. InSPX, InNDX, InDJI → inxxx
        if re.match(r"^in[a-z]{2,}$", name_lower) and name_lower not in ("inxxx", "inlist", "intraday", "include", "import"):
            return "inxxx"
        # nan constant → isnan (which documents the nan constant)
        if name_lower == "nan":
            return "isnan"
        return None

    def _find_by_choice_value(self, name_lower: str) -> list[dict]:
        """Find elements whose Choices or Notes field lists the given value."""
        import re
        results = self._collection.get(
            where={"chunk_type": "element_detail"},
            include=["documents", "metadatas"],
        )
        matches = []
        for doc, meta, doc_id in zip(
            results.get("documents", []),
            results.get("metadatas", []),
            results.get("ids", []),
        ):
            found = False
            # Check formal Choices field: "Value - description" at line start
            if "**Choices:**" in doc:
                choices_text = doc[doc.index("**Choices:**"):]
                for line in choices_text.split("\n"):
                    stripped = line.strip().lower()
                    if stripped.startswith(name_lower + " -") or stripped == name_lower:
                        found = True
                        break
            # Check Notes for "Valid values are X, Y" or "Choices are X or Y" patterns
            if not found:
                pattern = rf"(?:valid values|choices) (?:are|include)\s+[^.]*\b{re.escape(name_lower)}\b"
                if re.search(pattern, doc, re.IGNORECASE):
                    found = True
            if found:
                matches.append({"id": doc_id, "document": doc, "metadata": meta})
        return matches

    def get_by_element_name(self, name: str) -> list[dict]:
        name_lower = name.lower()
        # Try exact match first
        results = self._collection.get(
            where={"element_name": name_lower},
            include=["documents", "metadatas"],
        )
        items = self._format_get_results(results)
        if items:
            return items
        # Fall back to matching disambiguated names like "combined (section)"
        all_elements = self._collection.get(
            where={"chunk_type": "element_detail"},
            include=["documents", "metadatas"],
        )
        for doc, meta, doc_id in zip(
            all_elements.get("documents", []),
            all_elements.get("metadatas", []),
            all_elements.get("ids", []),
        ):
            stored_name = meta.get("element_name", "")
            if stored_name.startswith(name_lower + " (") or stored_name.startswith(name_lower + "\n"):
                items.append({"id": doc_id, "document": doc, "metadata": meta})
        if items:
            return items
        # Resolve known aliases/patterns (InXXX, nan)
        canonical = self._resolve_alias(name_lower)
        if canonical:
            return self.get_by_element_name(canonical)
        # Search Choices fields for enum values (e.g. "Norgate" → DataSource)
        return self._find_by_choice_value(name_lower)

    def search(self, query: str, top_k: int = 5, category: str | None = None, chunk_type: str | None = None) -> list[dict]:
        where = self._build_where(category=category, chunk_type=chunk_type)
        results = self._collection.query(
            query_texts=[query],
            n_results=top_k,
            where=where if where else None,
            include=["documents", "metadatas", "distances"],
        )
        return self._format_query_results(results)

    def search_docs(self, query: str, top_k: int = 5, category: str | None = None) -> list[dict]:
        where_conditions = [{"source": "pdf"}]
        if category:
            where_conditions.append({"category": category})
        where = where_conditions[0] if len(where_conditions) == 1 else {"$and": where_conditions}
        results = self._collection.query(
            query_texts=[query], n_results=top_k, where=where,
            include=["documents", "metadatas", "distances"],
        )
        return self._format_query_results(results)

    def search_scripts(self, query: str, top_k: int = 3, source_type: str | None = None) -> list[dict]:
        where = {"chunk_type": "script"}
        if source_type:
            where = {"$and": [{"chunk_type": "script"}, {"source_type": source_type}]}
        results = self._collection.query(
            query_texts=[query], n_results=top_k, where=where,
            include=["documents", "metadatas", "distances"],
        )
        return self._format_query_results(results)

    def keyword_search_scripts(self, keywords: list[str], top_k: int = 3, source_type: str | None = None) -> list[dict]:
        where = {"chunk_type": "script"}
        if source_type:
            where = {"$and": [{"chunk_type": "script"}, {"source_type": source_type}]}
        results = self._collection.get(where=where, include=["documents", "metadatas"])
        matches = []
        for doc, meta, doc_id in zip(
            results.get("documents", []), results.get("metadatas", []), results.get("ids", []),
        ):
            if any(kw.lower() in doc.lower() for kw in keywords):
                matches.append({"id": doc_id, "document": doc, "metadata": meta})
        return matches[:top_k]

    def get_section(self, title: str) -> list[dict]:
        """Get narrative sections matching title (substring) or section_number (prefix).

        Note: Fetches all narrative chunks and filters in Python.
        Acceptable for ~100-200 chunks.
        """
        results = self._collection.get(where={"chunk_type": "narrative"}, include=["documents", "metadatas"])
        matches = []
        title_lower = title.lower()
        for doc, meta, doc_id in zip(
            results.get("documents", []), results.get("metadatas", []), results.get("ids", []),
        ):
            section_title = meta.get("section_title", "").lower()
            section_number = meta.get("section_number", "")
            parent_section = meta.get("parent_section", "").lower()
            if title_lower in section_title:
                matches.append({"id": doc_id, "document": doc, "metadata": meta})
            elif section_number == title or section_number.startswith(title + "."):
                matches.append({"id": doc_id, "document": doc, "metadata": meta})
            elif title_lower == parent_section:
                matches.append({"id": doc_id, "document": doc, "metadata": meta})
        matches.sort(key=lambda m: m["metadata"].get("section_number", ""))
        return matches

    def get_primer_chunks(self) -> list[dict]:
        results = self._collection.get(where={"is_primer": "true"}, include=["documents", "metadatas"])
        items = self._format_get_results(results)
        items.sort(key=lambda x: int(x["metadata"].get("primer_order", "999")))
        return items

    def list_elements(self, category: str) -> list[dict]:
        """List all element details in a category (case-insensitive)."""
        results = self._collection.get(where={"chunk_type": "element_detail"}, include=["metadatas"])
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
        results = self._collection.get(where={"chunk_type": "element_detail"}, include=["metadatas"])
        counts = {}
        for meta in results.get("metadatas", []):
            cat = meta.get("category", "Unknown")
            counts[cat] = counts.get(cat, 0) + 1
        return counts

    def set_ingest_timestamp(self) -> None:
        now = datetime.now(timezone.utc).isoformat()
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
        try:
            result = self._collection.get(ids=["__ingest_meta__"], include=["metadatas"])
            if result["metadatas"]:
                return result["metadatas"][0].get("ingest_time")
        except Exception:
            pass
        return None

    def wipe(self) -> None:
        self._client.delete_collection(COLLECTION_NAME)
        self._collection = self._client.get_or_create_collection(
            name=COLLECTION_NAME, embedding_function=self._embedding_fn,
        )

    def count(self) -> int:
        return self._collection.count()

    def is_populated(self) -> bool:
        return self.count() > 0

    def print_status(self) -> None:
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
                if meta.get("source_type") == "user_script":
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
    def _build_where(category=None, chunk_type=None):
        conditions = []
        if category: conditions.append({"category": category})
        if chunk_type: conditions.append({"chunk_type": chunk_type})
        if not conditions: return None
        return conditions[0] if len(conditions) == 1 else {"$and": conditions}

    @staticmethod
    def _format_get_results(results):
        items = []
        for doc, meta, doc_id in zip(
            results.get("documents", []), results.get("metadatas", []), results.get("ids", []),
        ):
            items.append({"id": doc_id, "document": doc, "metadata": meta})
        return items

    @staticmethod
    def _format_query_results(results):
        items = []
        if not results.get("ids") or not results["ids"][0]:
            return items
        for doc, meta, doc_id, dist in zip(
            results["documents"][0], results["metadatas"][0], results["ids"][0], results["distances"][0],
        ):
            items.append({"id": doc_id, "document": doc, "metadata": meta, "distance": dist})
        return items
