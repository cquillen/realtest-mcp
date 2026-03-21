# Strategy Design

REQUIRED: Use this skill when translating a trading concept into a RealScript strategy.

## Workflow

1. **Search for similar examples** with `search_examples` using the trading concept as the query
2. **Identify the required building blocks** (entry conditions, exit rules, position sizing, etc.)
3. **Look up each building block** in docs with `search_docs`
4. **Scaffold the structure first** — sections only, no logic yet:
   ```
   Settings: ...
   Strategy <Name>:
     EntrySetup:
     Entry:
     ExitRule:
     ExitStop:
     Quantity:
   ```
5. **Fill in the logic** one section at a time, calling `get_function_reference` for each function

## Principle

Build the structure before the details. A correct skeleton with wrong parameters is easier to debug than a syntactically wrong script with the right intent.
