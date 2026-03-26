"""Load config.toml with environment variable overrides."""

import os
import re
import tomllib
from dataclasses import dataclass, field
from pathlib import Path


@dataclass
class Config:
    pdf_path: str = ""
    examples_path: str = ""
    user_script_paths: list[str] = field(default_factory=list)
    db_path: str = ""

    @staticmethod
    def _expand_vars(value: str) -> str:
        """Expand %VAR% style environment variables in a string."""

        def replacer(match):
            var_name = match.group(1)
            return os.environ.get(var_name, match.group(0))

        return re.sub(r"%([^%]+)%", replacer, value)

    @classmethod
    def load(cls) -> "Config":
        """Load config from TOML file with env var overrides."""
        config_path = cls._find_config_file()
        if config_path is None:
            raise FileNotFoundError(
                "No config.toml found. Searched:\n"
                "  1. REALTEST_MCP_CONFIG env var\n"
                "  2. ./config.toml\n"
                "  3. <package_dir>/config.toml"
            )

        with open(config_path, "rb") as f:
            data = tomllib.load(f)

        config = cls(
            pdf_path=data.get("realtest", {}).get("pdf_path", ""),
            examples_path=data.get("scripts", {}).get("examples", ""),
            user_script_paths=data.get("scripts", {}).get("user_scripts", []),
            db_path=data.get("database", {}).get("path", ""),
        )

        # Environment variable overrides
        env_map = {
            "REALTEST_MCP_PDF_PATH": "pdf_path",
            "REALTEST_MCP_EXAMPLES": "examples_path",
            "REALTEST_MCP_DB_PATH": "db_path",
        }
        for env_var, attr in env_map.items():
            val = os.environ.get(env_var)
            if val:
                setattr(config, attr, val)

        # Expand %VAR% in all path fields
        config.pdf_path = cls._expand_vars(config.pdf_path)
        config.examples_path = cls._expand_vars(config.examples_path)
        config.user_script_paths = [cls._expand_vars(p) for p in config.user_script_paths]
        config.db_path = cls._expand_vars(config.db_path)

        return config

    @staticmethod
    def _find_config_file() -> Path | None:
        # 1. Env var
        env_path = os.environ.get("REALTEST_MCP_CONFIG")
        if env_path and Path(env_path).exists():
            return Path(env_path)
        # 2. Current working directory
        cwd_path = Path.cwd() / "config.toml"
        if cwd_path.exists():
            return cwd_path
        # 3. Next to package
        pkg_path = Path(__file__).parent.parent.parent / "config.toml"
        if pkg_path.exists():
            return pkg_path
        return None
