"""Shared test fixtures."""

import pytest
from pathlib import Path


@pytest.fixture
def tmp_config(tmp_path):
    """Create a temporary config.toml for testing."""
    config_content = f"""
[realtest]
pdf_path = "C:\\\\RealTest\\\\RealTest User Guide.pdf"

[scripts]
examples = "C:\\\\RealTest\\\\Scripts\\\\Examples"
user_scripts = []

[database]
path = "{(tmp_path / 'chromadb').as_posix()}"
"""
    config_file = tmp_path / "config.toml"
    config_file.write_text(config_content)
    return config_file
