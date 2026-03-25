"""Tests for .rts script file reading."""

import pytest
from realtest_mcp.ingestion.script_parser import ScriptParser


def test_parse_script_file(tmp_path):
    script = tmp_path / "test.rts"
    script.write_text("Strategy MyStrat:\n  EntrySetup: C > MA(C, 20)\n")
    result = ScriptParser.parse_file(str(script))
    assert result.filename == "test.rts"
    assert result.file_path == str(script)
    assert "EntrySetup" in result.content


def test_parse_directory(tmp_path):
    (tmp_path / "a.rts").write_text("Strategy A:\n  Side: Long\n")
    (tmp_path / "b.rts").write_text("Strategy B:\n  Side: Short\n")
    (tmp_path / "not_rts.txt").write_text("ignored")
    results = ScriptParser.parse_directory(str(tmp_path))
    assert len(results) == 2
    filenames = {r.filename for r in results}
    assert filenames == {"a.rts", "b.rts"}


def test_parse_wraps_in_code_block(tmp_path):
    script = tmp_path / "test.rts"
    script.write_text("Data:\n  sma: MA(C, 20)\n")
    result = ScriptParser.parse_file(str(script))
    assert result.content.startswith("```rts\n")
    assert result.content.endswith("\n```")


def test_parse_handles_encoding(tmp_path):
    script = tmp_path / "test.rts"
    script.write_bytes(b"Strategy:\n  // price \xa3100\n")
    result = ScriptParser.parse_file(str(script))
    assert "Strategy" in result.content
