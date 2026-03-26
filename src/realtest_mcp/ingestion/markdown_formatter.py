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
        re.MULTILINE,
    )

    # Formula-like syntax: function calls, operators with identifiers
    _FORMULA_PATTERN = re.compile(
        r"(?:MA|EMA|RSI|ATR|HVOL|BBTop|BBBot|Highest|Lowest|CountTrue|"
        r"SinceTrue|Correl|Extern|Select|IF|Min|Max|Sum|Round|Abs)\s*\(",
        re.IGNORECASE,
    )

    # Standalone page numbers (a line that is just digits, typically 1-3 digits)
    _PAGE_NUMBER = re.compile(r"^\d{1,3}$", re.MULTILINE)

    @classmethod
    def format(cls, raw_text: str) -> str:
        """Convert raw PDF text to markdown."""
        text = raw_text

        # Strip standalone page numbers
        text = cls._PAGE_NUMBER.sub("", text)

        # Convert bullet points
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
        # Pattern: middle-dot on its own line followed by content on next line
        text = re.sub(r"\n\u00b7\n\s*", "\n- ", text)
        text = re.sub(r"\u00b7\s*", "- ", text)
        return text

    @classmethod
    def _convert_numbered_lists(cls, text: str) -> str:
        # Pattern: "N.\n" followed by content on next line
        text = re.sub(r"\n(\d+)\.\n\s*", r"\n\1. ", text)
        return text

    @classmethod
    def _fence_code_blocks(cls, text: str) -> str:
        lines = text.split("\n")
        result = []
        in_code = False
        code_buffer = []

        for _i, line in enumerate(lines):
            is_code_line = bool(
                cls._SECTION_KEYWORDS.match(line) or cls._FORMULA_PATTERN.search(line)
            )

            if is_code_line and not in_code:
                in_code = True
                result.append("```rts")
                code_buffer = [line]
            elif in_code and is_code_line:
                code_buffer.append(line)
            elif in_code and not is_code_line:
                result.extend(code_buffer)
                result.append("```")
                in_code = False
                code_buffer = []
                result.append(line)
            else:
                result.append(line)

        if in_code:
            result.extend(code_buffer)
            result.append("```")

        return "\n".join(result)

    @classmethod
    def _normalize_paragraphs(cls, text: str) -> str:
        text = re.sub(r"\n{3,}", "\n\n", text)
        text = "\n".join(line.rstrip() for line in text.split("\n"))
        return text
