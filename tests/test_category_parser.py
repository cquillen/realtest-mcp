"""Tests for parsing 17.17 category summary lists."""

from realtest_mcp.ingestion.category_parser import CategoryParser


def test_parse_category_list():
    raw = """All the elements of a trading strategy definition.
\u00b7
Allocation - dollars allocated to this strategy
\u00b7
Commission - commission per entry or exit transaction in dollars
\u00b7
EntrySetup - true/false condition for whether a stock is a setup"""
    result = CategoryParser.parse(raw)
    assert len(result) == 3
    assert result["allocation"] == "dollars allocated to this strategy"
    assert result["commission"] == "commission per entry or exit transaction in dollars"
    assert result["entrysetup"] == "true/false condition for whether a stock is a setup"


def test_parse_with_alias():
    raw = """\u00b7
EMA or XAvg - exponential moving average
\u00b7
MA or Avg - simple moving average"""
    result = CategoryParser.parse(raw)
    assert result["ema"] == "exponential moving average"
    assert result["xavg"] == "exponential moving average"
    assert result["ma"] == "simple moving average"
    assert result["avg"] == "simple moving average"


def test_parse_empty_returns_empty():
    result = CategoryParser.parse("")
    assert result == {}
