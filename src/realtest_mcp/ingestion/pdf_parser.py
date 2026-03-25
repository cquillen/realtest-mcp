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
