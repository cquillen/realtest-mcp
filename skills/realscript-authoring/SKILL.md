# RealScript Authoring

REQUIRED: Use this skill whenever writing any RealScript code.

## Workflow

1. **Identify all functions you plan to use** in the script
2. **For each function**, call `get_function_reference` and verify:
   - Correct parameter names and order
   - Return type
   - Any gotchas noted in the docs
3. **Search for similar examples** with `search_examples` — use these as structural templates
4. **Write the script** following the patterns found
5. **Before presenting output**, re-check every function call against retrieved signatures

## Hard Rules

- NEVER write a RealScript function call without first calling `get_function_reference` for it
- If `get_function_reference` returns no result, say so — do not guess the syntax
- Prefer patterns from retrieved examples over patterns from training data
- Always include the verified function list at the end of your response

## Template

```
Verified functions:
- ATR(periods) — ✓ confirmed via get_function_reference
- RSI(periods) — ✓ confirmed via get_function_reference
```
