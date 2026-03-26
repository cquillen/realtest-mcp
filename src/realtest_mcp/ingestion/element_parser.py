"""Parse element detail fields from raw PDF chunk text."""

import re
from dataclasses import dataclass

FIELD_LABELS = [
    "Category",
    "Description",
    "Input",
    "Syntax",
    "Parameters",
    "Choices",
    "Default",
    "Notes",
    "Example",
    "Return Value",
    "See also",
]


@dataclass
class ParsedElement:
    element_name: str
    element_name_display: str
    section_number: str
    category: str
    description: str
    fields: dict[str, str]

    def to_markdown(self) -> str:
        lines = [f"## {self.element_name_display}"]
        if self.category:
            lines.append(f"**Category:** {self.category}")
        if self.description:
            lines.append(f"**Description:** {self.description}")
        for label in FIELD_LABELS:
            if label in ("Category", "Description"):
                continue
            if label in self.fields:
                value = self.fields[label]
                if "\n" in value:
                    lines.append(f"**{label}:**")
                    lines.append(value)
                else:
                    lines.append(f"**{label}:** {value}")
        return "\n".join(lines)


class ElementParser:
    @staticmethod
    def parse(raw_text: str, title: str) -> ParsedElement:
        section_number = ElementParser._extract_section_number(title)
        name_display = ElementParser._extract_name(title)
        name = name_display.lower()
        fields = ElementParser._extract_fields(raw_text)
        category = fields.pop("Category", "")
        description = fields.pop("Description", "")
        return ParsedElement(
            element_name=name,
            element_name_display=name_display,
            section_number=section_number,
            category=category,
            description=description,
            fields=fields,
        )

    @staticmethod
    def split_aliases(title: str) -> list[tuple[str, str]]:
        name = ElementParser._extract_name(title)
        if " or " in name:
            parts = name.split(" or ")
            return [(p.strip().lower(), p.strip()) for p in parts]
        return [(name.lower(), name)]

    @staticmethod
    def _extract_fields(raw_text: str) -> dict[str, str]:
        fields = {}
        label_pattern = "|".join(re.escape(lbl) for lbl in FIELD_LABELS)
        pattern = re.compile(
            rf"^({label_pattern})\n(.*?)(?=^(?:{label_pattern})\n|\Z)",
            re.MULTILINE | re.DOTALL,
        )
        for match in pattern.finditer(raw_text):
            label = match.group(1)
            content = match.group(2).strip()
            fields[label] = content
        return fields

    @staticmethod
    def _extract_section_number(title: str) -> str:
        match = re.match(r"^(\d+(?:\.\d+)*)\.\s", title)
        return match.group(1) if match else ""

    @staticmethod
    def _extract_name(title: str) -> str:
        match = re.match(r"^[\d.]+\.\s+(.+)$", title)
        return match.group(1).strip() if match else title
