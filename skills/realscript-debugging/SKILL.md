# RealScript Debugging

REQUIRED: Use this skill when a RealScript file isn't working as expected.

## Workflow

1. **List all functions** used in the provided script
2. **For each function**, call `get_function_reference` and compare:
   - Are parameters in the correct order?
   - Are parameter names spelled correctly?
   - Is the return type being used correctly?
3. **Search for error messages** with `search_docs` if the user has provided one
4. **Flag every discrepancy** found — do not silently fix things
5. **Present a diff** of what needs to change and why, citing the retrieved docs

## Common Issues

- Wrong parameter order (e.g. `Highest(20, H)` instead of `Highest(H, 20)`)
- Using deprecated syntax from old versions
- Incorrect section names (e.g. `Entry:` vs `EntryRule:`)
- Missing required fields for a section type
