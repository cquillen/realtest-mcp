# RealTest MCP Server -- API Reference

This document describes the six MCP tools exposed by the RealTest MCP server.
All tools require the vector store to be populated (via the ingest pipeline)
before they can be called; otherwise they raise a `RuntimeError`.

---

## Tools

### 1. `get_primer`

Load the RealScript mental model -- script structure, evaluation semantics,
formula syntax, and key concepts.

**Parameters:** None

**Returns:** The full contents of the primer document as a string. Contains
the execution model, common mistakes, patterns, idioms, and recommended
workflows.

**Usage notes:**

- Call this once at the start of any scripting session. It is the foundation
  for writing correct RealScript code.
- Subsequent calls return the same content; there is no need to call it more
  than once per session.

**Example queries:**

- "Load the primer so I can start writing a script."
- Called implicitly as step 1 of the script-writing workflow.

---

### 2. `get_reference`

Get the exact reference documentation for a RealScript element.

**Parameters:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `name` | `str` | Yes | -- | The element name to look up (function, variable, item, operator, or syntax token). |

**Returns:** The full reference documentation for the named element. If
multiple matches exist (e.g., disambiguation across categories), all are
returned joined by double newlines. If no exact match is found, the closest
semantic match is returned with a notice. If nothing matches at all, a
"No reference found" message is returned.

**Resolution order:**

1. Path tokens (`scriptpath`, `desktop`, `documents`, `appdata`, etc.)
   redirect to the "File Path Syntax" section.
2. Operator tokens (`and`, `or`, `+`, `-`, `>=`, `mod`, `bitand`, etc.)
   redirect to the "Operators" section.
3. Syntax tokens (`[]`, `$`, `$$`, `@`, `//`, `filter`, `sort`, etc.)
   redirect to their mapped documentation sections (e.g., "Bar Offsets",
   "Symbol References", "Scan and TestScan Sections").
4. Exact element name match via the vector store (includes alias resolution).
5. Semantic fallback -- returns the closest element match with a notice.

**Usage notes:**

- Call this before using any element in generated code. This is a hard rule
  from the primer.
- The `name` parameter is case-insensitive and tolerates leading/trailing
  `?` characters.
- Works for functions (e.g., `ATR`), variables (e.g., `BarsHeld`), section
  items (e.g., `EntrySetup`), operators (e.g., `and`), and syntax tokens
  (e.g., `[]`).

**Example queries:**

- `get_reference("ATR")` -- get documentation for the ATR function.
- `get_reference("ExitStop")` -- get documentation for ExitStop semantics.
- `get_reference("BarsHeld")` -- get documentation for the BarsHeld variable.
- `get_reference("[]")` -- get documentation for bar offset syntax.
- `get_reference(">=")` -- get documentation for comparison operators.
- `get_reference("filter")` -- get documentation for scan filter keywords.

---

### 3. `get_section`

Fetch a specific documentation section by title or section number.

**Parameters:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `title` | `str` | Yes | -- | The section title or section number to retrieve. |

**Returns:** The formatted section content, including section number and
title as a level-2 heading. If multiple sections match, they are joined
by horizontal rules. If no exact title match is found, falls back to
semantic search across narrative chunks and returns the top 3 matches.

**Usage notes:**

- Use this for deep dives into topics like "Formula Syntax", "Bar Offsets",
  "Symbol References", or any named section in the RealTest documentation.
- Useful when debugging structural issues or when you need comprehensive
  coverage of a topic rather than a single element reference.

**Example queries:**

- `get_section("Formula Syntax")` -- full formula syntax documentation.
- `get_section("Bar Offsets")` -- how bar offset notation works.
- `get_section("Scan and TestScan Sections")` -- scan configuration details.

---

### 4. `list_elements`

Discover what RealScript elements exist.

**Parameters:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `category` | `str` | No | `""` (empty) | A category name to filter by. If omitted, lists all categories. |

**Returns:**

- **Without `category`:** A bullet list of all available categories with
  element counts, e.g., `- **Functions** (42 elements)`.
- **With `category`:** A bullet list of elements in that category, each
  showing the display name and a summary, e.g.,
  `- **ATR** -- Average True Range over N periods`.

**Usage notes:**

- Call without arguments first to see what categories exist.
- Then call with a specific category to browse available elements.
- Use this to discover building blocks before writing a script -- do not
  rely on training data for element names.

**Example queries:**

- `list_elements()` -- show all categories and counts.
- `list_elements("Functions")` -- list all available functions.
- `list_elements("Variables")` -- list all available variables.

---

### 5. `search_docs`

Search RealTest documentation by concept or topic.

**Parameters:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `query` | `str` | Yes | -- | A natural-language concept or topic to search for. |
| `category` | `str` | No | `""` (empty) | Restrict results to a specific category. If empty, searches all categories. |
| `top_k` | `int` | No | `5` | Maximum number of results to return. |

**Returns:** Formatted results as level-3 headings with element name and
category, followed by the document content. Multiple results are separated
by horizontal rules. Returns a "No documentation found" message if nothing
matches.

**Usage notes:**

- Use this for concept-based or topic-based searches when you do not know
  the exact element name.
- Useful for finding documentation related to error messages or unfamiliar
  concepts.
- The `category` filter narrows results when you know the domain.

**Example queries:**

- `search_docs("trailing stop")` -- find docs about trailing stops.
- `search_docs("position sizing", category="Variables")` -- find
  position-sizing variables.
- `search_docs("how to rank setups")` -- find setup ranking documentation.

---

### 6. `search_scripts`

Find example RealScript files demonstrating a concept or technique.

**Parameters:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `query` | `str` | Yes | -- | A concept, technique, or keyword to search for in example scripts. |
| `source` | `str` | No | `"all"` | Filter by script source. Use `"user"` for user scripts, `"all"` for everything, or a specific source type identifier. |
| `top_k` | `int` | No | `3` | Maximum number of scripts to return. |

**Returns:** Matching scripts formatted as level-3 headings with the
filename, followed by the script content in a `realscript` code block.
Scripts longer than 3000 characters are truncated with a notice. Multiple
results are separated by horizontal rules. If semantic search returns no
results, falls back to keyword-based search. Returns a "No scripts found"
message if nothing matches.

**Usage notes:**

- Use this to find structural templates and real-world examples before
  writing a new script.
- The keyword fallback ensures that even simple term matches will surface
  relevant scripts.
- Set `source` to `"user"` to search only user-provided scripts.

**Example queries:**

- `search_scripts("mean reversion")` -- find mean-reversion strategy examples.
- `search_scripts("trailing stop", source="user")` -- find user scripts
  with trailing stops.
- `search_scripts("rotational monthly rebalance", top_k=5)` -- find
  rotation strategy examples, returning up to 5.

---

## Recommended Workflows

These workflows define the order in which to use the tools for common tasks.
They are derived from the primer's WORKFLOWS section.

### Writing a New Script

1. `get_primer()` -- load the mental model (once per session).
2. `list_elements(category)` for each relevant category, then
   `get_reference(name)` for every element you plan to use.
3. `search_scripts(query)` for structural templates;
   `get_section(title)` for complex topics.
4. Write the script, then validate with `realtest -parse script.rts`
   (exit code 0 means valid syntax).
5. Re-check every function call against retrieved signatures before
   presenting output.

### Debugging an Existing Script

1. `get_reference(name)` for every function and element in the script --
   check parameter order, spelling, and types.
2. `search_docs(query)` for any error messages;
   `get_section(title)` if the issue is structural.
3. Flag every discrepancy -- do not silently fix. Present a diff citing
   the documentation.

### Designing a Strategy from a Trading Concept

1. `search_scripts(query)` for similar examples;
   `list_elements()` to discover building blocks.
2. Scaffold the structure first (sections only, no logic), then fill in
   one section at a time.
3. `get_reference(name)` for each element as you go.

---

## Command-Line Reference

The RealTest executable supports the following modes, which can be chained:

```
realtest -parse script.rts        # syntax check only
realtest -test script.rts         # run backtest
realtest -import script.rts       # run import
realtest -scan script.rts         # run scan
realtest -orders script.rts       # generate orders
realtest -optimize script.rts     # run optimization
```

Multiple scripts can follow a mode flag. Exit code 0 indicates success;
errors are written to stderr and to `errorlog.txt` in the RealTest install
folder.
