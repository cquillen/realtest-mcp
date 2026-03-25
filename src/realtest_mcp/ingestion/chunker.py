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
            title_end = self.text.index("\n", pos) + 1 if "\n" in self.text[pos:] else len(self.text)
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
        parent_map = {}
        stack = []
        for level, title, _page in self.toc:
            while stack and stack[-1][0] >= level:
                stack.pop()
            parent_map[title] = stack[-1][1] if stack else None
            stack.append((level, title))
        return parent_map

    @staticmethod
    def _extract_section_number(title: str) -> str:
        match = re.match(r"^(\d+(?:\.\d+)*)\.\s", title)
        return match.group(1) if match else ""
