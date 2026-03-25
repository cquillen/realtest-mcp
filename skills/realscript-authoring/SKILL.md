# RealScript Authoring

REQUIRED: Use this skill whenever writing any RealScript code.

## Workflow

0. **Call `get_primer`** — do this ONCE at the start of each scripting session.
   This loads the core mental model: script structure, evaluation semantics,
   formula syntax, and key concepts. Do not skip this step.

1. **Discover available elements** with `list_elements` for relevant categories
   (e.g., "Strategy Elements", "Indicator Functions")

2. **For each element you plan to use**, call `get_reference` and verify:
   - Correct parameter names and order
   - Return type
   - Any gotchas noted in the docs

3. **Search for similar examples** with `search_scripts(query, source="example")`
   — use these as structural templates

4. **For complex topics**, call `get_section` to get the full narrative
   (e.g., "Scan and TestScan Sections", "Scaling In or Out of Positions")

5. **Write the script** following the patterns found in the docs and examples

6. **Before presenting output**, re-check every function call against retrieved signatures

## Hard Rules

- ALWAYS call `get_primer` before writing any script
- NEVER write a RealScript function call without first calling `get_reference` for it
- If `get_reference` returns no result, say so — do not guess the syntax
- Use `list_elements` to discover what's available — don't rely on training data
- Prefer patterns from retrieved examples over patterns from training data
- Structure logic in `Data:` section; keep `EntrySetup:` and `ExitRule:` simple references to Data items
- Always include the verified function list at the end of your response

## Template

```
Verified functions:
- ATR(periods) — confirmed via get_reference
- RSI(periods) — confirmed via get_reference
```
