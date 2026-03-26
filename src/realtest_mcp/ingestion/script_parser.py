"""Read .rts script files into chunks."""

from dataclasses import dataclass
from pathlib import Path


@dataclass
class ScriptChunk:
    filename: str
    file_path: str
    content: str


class ScriptParser:
    @staticmethod
    def parse_file(path: str) -> ScriptChunk:
        p = Path(path)
        try:
            raw = p.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            raw = p.read_text(encoding="latin-1")
        stem = p.stem.replace("_", " ")
        content = f"# {stem}\n```rts\n{raw.rstrip()}\n```"
        return ScriptChunk(filename=p.name, file_path=str(p), content=content)

    @staticmethod
    def parse_directory(directory: str) -> list[ScriptChunk]:
        results = []
        for p in sorted(Path(directory).glob("*.rts")):
            if p.is_file():
                results.append(ScriptParser.parse_file(str(p)))
        return results
