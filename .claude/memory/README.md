# Memory

Persistent memory files for this project. Claude reads and writes these across conversations.

## Types

| Type      | Purpose |
|-----------|---------|
| user      | User's role, preferences, and knowledge level |
| feedback  | Guidance on how Claude should approach work here |
| project   | Ongoing goals, decisions, constraints, deadlines |
| reference | Pointers to external resources (dashboards, trackers, docs) |

## File Format

```markdown
---
name: descriptive-name
description: One-line description used to judge relevance in future sessions
type: user | feedback | project | reference
---

Memory content here.
For feedback/project types, include:
**Why:** reason this rule/fact exists
**How to apply:** when this should influence behavior
```

## Index

All memory files are listed in `MEMORY.md` in this directory.
