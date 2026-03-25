# Strategy Design

REQUIRED: Use this skill when translating a trading concept into a RealScript strategy.

## Workflow

1. **Search for similar examples** with `search_scripts` using the trading concept as the query
2. **Discover available elements** with `list_elements("Strategy Elements")` and other relevant categories
3. **Look up each building block** with `get_reference` for exact syntax
4. **Read relevant narrative** with `get_section` for complex topics
   (e.g., "Capacity Constraints", "Dynamic Sizing", "Scaling In or Out")
5. **Scaffold the structure first** — sections only, no logic yet:
   ```
   Settings: ...
   Strategy <Name>:
     EntrySetup:
     EntryLimit:
     ExitRule:
     ExitStop:
     Quantity:
   ```
6. **Fill in the logic** one section at a time, calling `get_reference` for each element

## Principle

Build the structure before the details. A correct skeleton with wrong parameters is easier to debug than a syntactically wrong script with the right intent.
