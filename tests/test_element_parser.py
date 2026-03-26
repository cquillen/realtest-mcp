"""Tests for parsing element detail fields from raw chunk text."""

from realtest_mcp.ingestion.element_parser import ElementParser


def test_parse_full_element():
    raw = """Category
Strategy Elements
Description
Commission amount, in instrument or account currency units, for each trade
Input
Formula specifying a commission amount
Notes
If your broker charges no commissions, omit this formula or set it to 0.
Commission is calculated separately for entry and exit."""
    result = ElementParser.parse(raw, "17.18.103. Commission")
    assert result.element_name == "commission"
    assert result.element_name_display == "Commission"
    assert result.category == "Strategy Elements"
    assert result.section_number == "17.18.103"
    assert "Commission amount" in result.description
    assert "Formula specifying" in result.fields.get("Input", "")
    assert "charges no commissions" in result.fields.get("Notes", "")


def test_parse_element_missing_fields():
    raw = """Category
Bar Data Values
Description
Dividend amount ($/share) earned on an ex-dividend date"""
    result = ElementParser.parse(raw, "17.18.147. Dividend")
    assert result.category == "Bar Data Values"
    assert "Syntax" not in result.fields
    assert "Parameters" not in result.fields


def test_parse_element_with_syntax():
    raw = """Category
Indicator Functions
Description
Wilder's Average True Range
Syntax
ATR(len)
Parameters
len - lookback period
Notes
Uses original Welles Wilder calculation."""
    result = ElementParser.parse(raw, "17.18.67. ATR")
    assert result.fields["Syntax"] == "ATR(len)"
    assert "len - lookback period" in result.fields["Parameters"]


def test_alias_splitting():
    title = "17.18.150. EMA or XAvg"
    names = ElementParser.split_aliases(title)
    assert names == [("ema", "EMA"), ("xavg", "XAvg")]


def test_alias_no_split():
    title = "17.18.103. Commission"
    names = ElementParser.split_aliases(title)
    assert names == [("commission", "Commission")]


def test_format_as_markdown():
    raw = """Category
Indicator Functions
Description
Wilder's Average True Range
Syntax
ATR(len)
Parameters
len - lookback period"""
    result = ElementParser.parse(raw, "17.18.67. ATR")
    md = result.to_markdown()
    assert md.startswith("## ATR")
    assert "**Category:** Indicator Functions" in md
    assert "**Syntax:** ATR(len)" in md
