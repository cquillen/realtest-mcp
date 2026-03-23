# RealScript Authoring

REQUIRED: Use this skill whenever writing any RealScript code.

## Workflow

0. **Call `get_language_guide`** — do this ONCE at the start of each scripting session.
   This loads the full language model: script structure, evaluation model, all section types,
   strategy elements, functions, and idiomatic patterns. Do not skip this step.

1. **Identify all functions you plan to use** in the script

2. **For each function**, call `get_function_reference` and verify:
   - Correct parameter names and order
   - Return type
   - Any gotchas noted in the docs

3. **Search for similar examples** with `search_examples` — use these as structural templates

4. **Write the script** following the patterns found in the guide and examples

5. **Before presenting output**, re-check every function call against retrieved signatures

## Hard Rules

- ALWAYS call `get_language_guide` before writing any script
- NEVER write a RealScript function call without first calling `get_function_reference` for it
- If `get_function_reference` returns no result, say so — do not guess the syntax
- Prefer patterns from retrieved examples over patterns from training data
- Structure logic in `Data:` section; keep `EntrySetup:` and `ExitRule:` simple references to Data items
- Always include the verified function list at the end of your response

## Template

```
Verified functions:
- ATR(periods) — ✓ confirmed via get_function_reference
- RSI(periods) — ✓ confirmed via get_function_reference
```
