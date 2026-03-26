"""Tests for config loading."""

import pytest

from realtest_mcp.config import Config


def test_load_config_from_env_var(tmp_config, monkeypatch):
    monkeypatch.setenv("REALTEST_MCP_CONFIG", str(tmp_config))
    config = Config.load()
    assert config.pdf_path == r"C:\RealTest\RealTest User Guide.pdf"
    assert config.examples_path == r"C:\RealTest\Scripts\Examples"


def test_env_var_override(tmp_config, monkeypatch):
    monkeypatch.setenv("REALTEST_MCP_CONFIG", str(tmp_config))
    monkeypatch.setenv("REALTEST_MCP_PDF_PATH", "/custom/path.pdf")
    config = Config.load()
    assert config.pdf_path == "/custom/path.pdf"


def test_expand_percent_vars(tmp_config, monkeypatch):
    monkeypatch.setenv("REALTEST_MCP_CONFIG", str(tmp_config))
    monkeypatch.setenv("REALTEST_MCP_DB_PATH", "%TEMP%/test_db")
    config = Config.load()
    assert "%TEMP%" not in config.db_path


def test_missing_config_raises(tmp_path, monkeypatch):
    monkeypatch.delenv("REALTEST_MCP_CONFIG", raising=False)
    monkeypatch.chdir(tmp_path)  # CWD with no config.toml
    # Patch _find_config_file to avoid finding repo's config.toml via package path
    monkeypatch.setattr(Config, "_find_config_file", staticmethod(lambda: None))
    with pytest.raises(FileNotFoundError):
        Config.load()
