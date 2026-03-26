"""Parse 17.17 category summary lists into name:description mappings."""

import re


class CategoryParser:
    @staticmethod
    def parse(raw_text: str) -> dict[str, str]:
        """Parse bullet list of 'Name - description' into {lowered_name: description}."""
        result = {}
        cleaned = re.sub(r"\u00b7\s*", "", raw_text)
        for match in re.finditer(
            r"^([A-Za-z#?.]\S*(?:\s+or\s+\S+)?)\s+-\s+(.+)$",
            cleaned,
            re.MULTILINE,
        ):
            name_part = match.group(1).strip()
            description = match.group(2).strip()
            if " or " in name_part:
                for alias in name_part.split(" or "):
                    result[alias.strip().lower()] = description
            else:
                result[name_part.lower()] = description
        return result
