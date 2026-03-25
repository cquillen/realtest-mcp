"""Tests for PDF text to markdown conversion."""

import pytest
from realtest_mcp.ingestion.markdown_formatter import MarkdownFormatter


def test_bullet_conversion():
    raw = "Items include:\n\u00b7\nfirst item\n\u00b7\nsecond item"
    result = MarkdownFormatter.format(raw)
    assert "- first item" in result
    assert "- second item" in result
    assert "\u00b7" not in result


def test_numbered_list_conversion():
    raw = "Steps:\n1.\nDo this first\n2.\nDo this second"
    result = MarkdownFormatter.format(raw)
    assert "1. Do this first" in result
    assert "2. Do this second" in result


def test_paragraph_separation():
    raw = "First paragraph.\n\n\n\nSecond paragraph."
    result = MarkdownFormatter.format(raw)
    assert "\n\n" in result
    assert "\n\n\n" not in result


def test_code_block_detection_section_keywords():
    raw = "Here is an example:\nStrategy MyStrat:\n  EntrySetup: C > MA(C, 20)\n  ExitRule: BarsHeld >= 5\nEnd of example."
    result = MarkdownFormatter.format(raw)
    assert "```rts" in result
    assert "```\n" in result or result.endswith("```")


def test_code_block_detection_formula_syntax():
    raw = "The formula would be:\nMA(C, 200) and RSI(C, 14) < 30\nThat is the formula."
    result = MarkdownFormatter.format(raw)
    assert "```rts" in result


def test_preserves_normal_text():
    raw = "RealTest uses its own memory-based binary format for daily price and volume data bars."
    result = MarkdownFormatter.format(raw)
    assert "RealTest uses its own memory-based binary format" in result


def test_strips_page_numbers():
    raw = "Some content here.\n293\nMore content continues."
    result = MarkdownFormatter.format(raw)
    assert "\n293\n" not in result
