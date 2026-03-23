# RealScript Language Guide

**Purpose:** A complete reference for the RealScript `.rts` file format, extracted from official RealTest documentation. Intended to give Claude a deep, holistic understanding of the language — not just function signatures, but the evaluation model, section interactions, and idiomatic patterns.

---

## 1. Script File Anatomy

A `.rts` file is divided into named top-level **sections**. Each section begins with its name followed by a colon. Lines within a section are indented. Comments use `//`.

```
// Optional file-level comment

Import:           // Data import specification
  ...

Settings:         // Runtime settings (DataFile, dates, account size)
  ...

Parameters:       // Named optimization variables
  ...

Data:             // Pre-computed named formula arrays
  ...

Library:          // Reusable sub-formulas
  ...

Template: name    // Reusable strategy element blocks
  ...

Strategy: "Name"  // Trading strategy definition
  ...

Combined:         // Portfolio-level constraints across all strategies
  ...
```

Only `Strategy:` is required for a runnable test. In practice, nearly every script has at minimum `Settings:`, `Data:`, and one or more `Strategy:` sections.

---

## 2. The Evaluation Model (Critical)

Understanding *when* each formula is evaluated is the most important concept in RealScript. Getting this wrong produces look-ahead bias.

### 2.1 Data Section — Pre-computed before the test

All `Data:` items are computed as **full-length arrays** before the backtest begins. For every symbol, every bar is computed in one pass. Think of Data items as columns in a spreadsheet: when the backtest starts running, all columns are already filled in.

**Consequence:** Data items can reference future bars with negative offsets (e.g. `Dividend[-Bars]`), but indicator functions with variable lookbacks may not be bufferable and require `#SlowCalc`.

### 2.2 EntrySetup — Evaluated on the signal day (prior to entry)

For the default entry mode (`EntryTime: NextOpen`, or any strategy using `EntryLimit`/`EntryStop`), `EntrySetup` is evaluated on **day T**, and the entry order is placed for **day T+1**.

- The most recent bar visible to the formula is **day T** (the prior day relative to the entry fill).
- You cannot see the entry day's bar at setup time.
- `FillPrice` in `EntrySetup` returns `OrderPrice` (the estimated entry price), not the actual fill.

**Exception:** `EntryTime: ThisClose` evaluates setup and fills on the same bar. Useful for live trading with end-of-day signals, but RealTest cannot generate Tomorrow's Orders for this mode.

### 2.3 Exit Formulas — Also evaluated on the prior day

`ExitRule`, `ExitStop`, and `ExitLimit` are always evaluated using the **prior day** as the most recent bar (for `NextOpen` and `NextClose` exits). The formula on day T generates an order for day T+1.

- `ExitStop` and `ExitLimit` re-evaluate every day a position is held, so they can track (trail) as conditions change.
- `FillPrice` in exit formulas correctly returns the actual entry price (via the OrderPrice substitution mechanism — see §6.3).

**Exception:** `ExitTime: ThisClose` evaluates and fills on the same bar.

### 2.4 Timeline Summary

```
Day T   → EntrySetup evaluates (current bar = T)
           ExitRule evaluates for open positions (current bar = T)
           ExitStop / ExitLimit evaluated for open positions (current bar = T)

Day T+1 → Orders fill at open (NextOpen) or intraday (Intraday) or close (NextClose/ThisClose)
           BarsHeld increments by 1
```

---

## 3. Section Reference

### 3.1 Import

Specifies how to pull data from an external provider into a local `.rtd` data file.

```
Import:
  DataSource:   Norgate
  IncludeList:  .S&P 500 Current & Past
  IncludeList:  SPY
  StartDate:    1995-01-01
  EndDate:      Latest
  SaveAs:       spx.rtd
```

Common `DataSource` values: `Norgate`, `Yahoo`, `Tiingo`, `CSV`.
`IncludeList` starting with `.` references a Norgate dynamic watchlist.
`SaveAs` names the local `.rtd` file written to disk.

### 3.2 Settings

Runtime configuration for scans and tests.

```
Settings:
  DataFile:     spx.rtd          // which .rtd to load
  AccountSize:  100000           // starting equity ($)
  StartDate:    2000-01-01       // backtest start
  EndDate:      Latest           // backtest end
  BarSize:      Daily            // Daily (default), Weekly, Monthly
```

`ScanSettings:`, `TestSettings:`, and `OrderSettings:` can override specific settings for each run mode while `Settings:` holds the common defaults.

### 3.3 Parameters

Named variables for optimization. A parameter is just a name with a default value; during optimization, RealTest sweeps across specified ranges.

```
Parameters:
  rsiLen:   14
  stopMult: 2.0
  lookback: 90
```

Parameters are referenced by name in `Data:` and `Strategy:` formulas. This is the canonical way to make a script configurable.

### 3.4 Data

Named formula arrays computed once across all bars before any test runs. This section exists for two reasons:
1. **Performance** — computing an indicator once in `Data:` is far faster than re-computing it in every strategy formula on every bar.
2. **Readability** — complex entry logic can be broken into named steps, with `EntrySetup:` referencing a final `IsSetup` variable.

```
Data:
  rsi14:    RSI(14)
  ma200:    MA(C, 200)
  atr14:    ATR(14)
  IsSetup:  rsi14 < 30 and C > ma200
```

**Special tags** that prefix a Data formula:

| Tag | Meaning |
|-----|---------|
| `#SlowCalc` | Formula cannot be buffered (e.g., uses `Log(C)` of unadjusted prices). Forces full re-calculation. |
| `#PercentRank` | Cross-sectional percentile rank — ranks the formula across all symbols on each date. |
| `#OnePerDate` | Evaluate once per date (not per symbol). Use for market-wide conditions like `Extern($SPY, C > MA(C,200))`. |

**One-pass calculation:** Most built-in multi-bar functions (`MA`, `EMA`, `ATR`, `RSI`, `Highest`, `Lowest`, `StdDev`, etc.) support one-pass calculation in `Data:` when given a non-variable count. This is the most performant path. Functions like `Slope(Log(C), n)` require `#SlowCalc` because they cannot be buffered.

### 3.5 Library

Defines sub-formulas that can be referenced in strategy elements. Similar to functions. Used in examples to track sub-position quantities.

```
Library:
  posshares: shares
```

### 3.6 Template

A named block of strategy elements that can be mixed into one or more strategies via `Using:`. Used to share common logic without duplication.

```
Template: base
  MaxPositions: 10
  Quantity:     10
  QtyType:      Percent

Strategy: "LongMR"
  Using:       base
  Side:        Long
  EntrySetup:  IsSetup
  ...
```

### 3.7 Strategy

The core section. Contains all entry, exit, sizing, and constraint elements. See §4 for full element reference.

```
Strategy: "StrategyName"
  Side:        Long           // Long, Short, or omit for both
  EntrySetup:  IsSetup
  SetupScore:  -RSI(14)       // higher score = selected first; negate for "lowest RSI first"
  ExitRule:    RSI(14) > 50 or BarsHeld >= 20
  ExitStop:    FillPrice - 2 * ATR(14)
  ExitLimit:   FillPrice + 2 * ATR(14)
  Quantity:    10
  QtyType:     Percent
  MaxPositions: 10
```

### 3.8 Combined

Portfolio-level constraints that apply across all strategies collectively.

```
Combined:
  MaxExposure:  100
  MaxPositions: 20
```

---

## 4. Strategy Elements Reference

### 4.1 Entry Elements

| Element | Description | Notes |
|---------|-------------|-------|
| `EntrySetup: formula` | True/false entry eligibility | Required to produce any trades. Evaluated on prior day. |
| `EntryLimit: price` | Limit order entry price | Low must touch price (long) or High must touch (short) |
| `EntryStop: price` | Stop order entry price | High must touch (long) or Low must touch (short) |
| `EntryTime: choice` | When the entry order fills | `ThisClose`, `Intraday`, `NextOpen` (default for market), `NextClose` |
| `SetupScore: formula` | Rank setups when capacity is limited | Higher score = selected first |
| `EntryScore: formula` | Rank entries specifically | Use only when more entries than capacity; use `SetupScore` for general ranking |
| `Side: Long\|Short` | Force a single direction | Omit if strategy can go both ways |

### 4.2 Exit Elements

| Element | Description | Notes |
|---------|-------------|-------|
| `ExitRule: formula` | Conditional exit trigger | Re-evaluated every day. Can return a string as exit reason. |
| `ExitStop: price` | Stop loss price | Re-evaluated daily (supports trailing stops) |
| `ExitLimit: price` | Profit target price | Re-evaluated daily (supports moving targets) |
| `ExitTime: choice` | When ExitRule fills | `ThisClose`, `NextOpen` (default), `NextClose` |
| `ExitStopTime: choice` | When ExitStop fills | `Intraday` (default = live STP DAY), `NextOpen`, `NextClose`, `ThisClose` |
| `ExitLimitTime: choice` | When ExitLimit fills | `Intraday` (default = live LMT DAY), `NextOpen`, `NextClose`, `ThisClose` |

All three exit types (`ExitRule`, `ExitStop`, `ExitLimit`) operate as a **one-cancels-all bracket** — whichever triggers first wins.

**Multi-reason ExitRule** using `Select`:
```
ExitRule: Select(C > C[1], "up day",
                 BarsHeld = 10, "time stop")
```
Each branch returns a string reason displayed in the Trade List.

### 4.3 Sizing Elements

| Element | Description |
|---------|-------------|
| `Quantity: formula` | Position size. Interpretation depends on QtyType. |
| `QtyType: Shares\|Value\|Percent` | `Shares` = share count; `Value` = dollar amount; `Percent` = % of S.Alloc |
| `QtyPrice: OrderPrice\|FillPrice` | Price used for Value/Percent sizing calculations (default: OrderPrice) |
| `DynamicSizing: True` | Special mode: Quantity becomes a daily target-position-size. Disables EntrySetup/ExitRule/ExitStop/ExitLimit. |

**Position sizing example:**
```
// Invest 10% of allocation per position
Quantity: 10
QtyType:  Percent

// ATR-based volatility sizing
Quantity: 0.01 * S.Alloc / ATR(14)
QtyType:  Shares
```

### 4.4 Capacity Constraints

All constraints participate in the **setup selection process** — setups are evaluated top-down and skipped when a constraint would be exceeded. With `Reduce: True`, position size is reduced to fit rather than skipping entirely.

| Element | Description |
|---------|-------------|
| `MaxPositions: n` | Max number of open positions |
| `MaxExposure: pct` | Max total exposure as % of S.Alloc |
| `MaxInvested: dollars` | Max total dollar investment |
| `MaxLongExp: pct` | Max long exposure % |
| `MaxLongInv: dollars` | Max long dollar investment |
| `MaxShortExp: pct` | Max short exposure % |
| `MaxShortInv: dollars` | Max short dollar investment |
| `Reduce: True\|False` | Reduce size to available capacity instead of skipping |

---

## 5. Bar Data Values

All values reference the **current bar** unless a numeric offset is appended.

| Name | Alias | Description |
|------|-------|-------------|
| `Open` | `O` | Bar open price |
| `High` | `H` | Bar high price |
| `Low` | `L` | Bar low price |
| `Close` | `C` | Bar close price |
| `Volume` | `V` | Bar volume |
| `DayOfWeek` | — | 1=Mon, 2=Tue, 3=Wed, 4=Thu, 5=Fri |
| `InSPX`, `InNDX`, etc. | — | Norgate index constituency (0 or 1) |

### 5.1 Bar Offset Syntax

Append `[n]` to look back (positive n) or forward (negative n):

```
C[1]          // prior day close
C[5]          // 5 bars ago close
DayOfWeek[-1] // tomorrow's day of week (valid for pre-computing order timing)
H[0]          // same as H (current bar)
```

Forward offsets (`[-n]`) are valid in `Data:` sections since all bars are pre-computed. Use with caution — they access future data.

---

## 6. Current Position Information

These values are only meaningful in formulas evaluated while a position is open (exit formulas, quantity formulas, etc.).

| Name | Alias | Description |
|------|-------|-------------|
| `FillPrice` | — | Entry fill price (see §6.3) |
| `OrderPrice` | — | Entry order price (the price used when placing the order) |
| `BarsHeld` | — | Bars since entry, excluding entry day ("nights held") |
| `EntryDate` | — | Entry date as YYYYMMDD integer |
| `Shares` | `Contracts` | Current position size (always positive) |
| `S.Alloc` | — | Current strategy allocation ($) |
| `S.Equity` | — | Current strategy equity ($, updated after each exit) |

### 6.1 BarsHeld Semantics

`BarsHeld` counts **nights** since entry, not including entry day:
- Entry on Monday: `BarsHeld = 0` on Monday, `= 1` on Tuesday, `= 4` on Friday
- To exit on the 4th trading day after entry: `ExitRule: BarsHeld = 4`

### 6.2 S.Alloc vs S.Equity

Use `S.Alloc` (not `S.Equity`) in position sizing formulas. `S.Alloc` is computed once per date before any trades, ensuring position sizes match pre-market order calculations. `S.Equity` updates after each exit, so using it in entry formulas would create a dependency on the order exits are processed.

### 6.3 FillPrice Substitution

When formulas are evaluated at EntrySetup time (day T, before the entry fill on T+1), `FillPrice` automatically returns `OrderPrice`. After the position is entered, `FillPrice` returns the actual fill price. This allows you to write `ExitStop: FillPrice - 2 * ATR(14)` and have it work correctly on both the entry day and all subsequent days without special-casing.

---

## 7. Function Reference

### 7.1 Multi-Bar Functions (Indicators)

All support one-pass calculation in `Data:` with non-variable count unless noted.

| Function | Aliases | Syntax | Description |
|----------|---------|--------|-------------|
| `MA` | `Avg` | `MA(expr, count)` | Simple moving average |
| `EMA` | `XAvg` | `EMA(expr, count)` | Exponential moving average (factor = 2/(count+1)) |
| `RSI` | — | `RSI(len)` | Wilder's RSI (uses Wilder smoothing = EMA factor 1/len) |
| `ATR` | — | `ATR(len)` | Wilder's Average True Range |
| `MACD` | — | `MACD(len1, len2)` | EMA(C,len1) − EMA(C,len2) |
| `STOC` | — | `STOC(len, avg)` | Stochastic; equivalent to 100*Avg((C-Lowest(L,len))/(Highest(H,len)-Lowest(L,len)), avg) |
| `StdDev` | — | `StdDev(expr, count)` | Population standard deviation (same as Excel STDEV.P) |
| `Highest` | `HHV` | `Highest(expr, count)` | Highest value over count bars |
| `Lowest` | `LLV` | `Lowest(expr, count)` | Lowest value over count bars |
| `ROC` | `PctChg` | `ROC(expr, count)` | % rate of change: 100*(expr/expr[count]-1) |
| `CountTrue` | — | `CountTrue(cond, count)` | Count of bars where condition is true |
| `Slope` | — | `Slope(expr, count)` | Linear regression slope over count bars |
| `Correl` | — | `Correl(expr1, expr2, count)` | Pearson correlation of two series |

**RSI note:** `RSI(len)` uses Wilder's exponential smoothing, equivalent to an EMA with factor `1/len` (not the standard `2/(len+1)` EMA). Wilder smoothing is equivalent to `EMA(expr, 2*len-1)`. The consequence: `RSI(len)` crossing 50 is identical to `C` crossing `EMA(C, 2*len-1)`.

### 7.2 General-Purpose Functions

| Function | Aliases | Syntax | Description |
|----------|---------|--------|-------------|
| `IF` | `IIF` | `IF(cond, if_true, if_false)` | Conditional — returns if_true or if_false |
| `Select` | — | `Select(cond, val, cond, val, ...)` | Multi-branch conditional; returns NaN if no branch matches (use odd arg count for default) |
| `Abs` | — | `Abs(value)` | Absolute value |
| `Log` | — | `Log(value)` | Natural logarithm |
| `Exp` | — | `Exp(value)` | e raised to the power |
| `Extern` | — | `Extern($SYM, expr)` | Evaluate expr for a different symbol (`$SYM`), strategy (`@Name`), or bar size (`~Weekly`) |

### 7.3 Cross-Sectional Functions

Used to reference other symbols or aggregate across strategies/positions:

```
// Reference SPY to create a market regime filter
Regime: Extern($SPY, C > MA(C, 200))

// Correlation of this stock to SPY over 100 days
CorrToSPY: Correl(ROC(C,1), Extern($SPY, ROC(C,1)), 100)
```

### 7.4 Useful Patterns

```
// Trailing stop: 5% below highest high since entry
ExitStop: 0.95 * Highest(H, BarsHeld)

// Williams %R
WilliamsR: -1 * (100 - STOC(14, 1))

// AdjSlope (Clenow annualized exponential slope)
ExponentialSlope: #SlowCalc Slope(Log(C), 90)
AnnualizedSlope:  100 * ((Exp(ExponentialSlope)) ^ 250 - 1)
R2:               #SlowCalc Correl(Log(C), FunBar, 90) ^ 2
AdjSlope:         AnnualizedSlope * R2
```

---

## 8. Idiomatic Patterns

### 8.1 The Data → EntrySetup Pattern

The most important structural pattern in RealScript. All indicator logic goes in `Data:`, and `EntrySetup:` references a final boolean:

```
Data:
  rsi3:     RSI(3)
  ma200:    MA(C, 200)
  atr14:    ATR(14)
  dv20:     MA(C * V, 20)          // average dollar volume
  IsSetup:  rsi3 < 20
            and C > ma200          // trend filter
            and dv20 > 1000000     // liquidity filter

Strategy: "MeanRevLong"
  EntrySetup: IsSetup
```

Multi-line formulas: a formula can be continued on subsequent indented lines. The `and`/`or` at the start of each continuation makes the logic readable.

### 8.2 Entry Limit vs Market Entry

```
// Buy at limit 4% below today's close (mean reversion entry)
EntrySetup: IsSetup
EntryLimit: C * 0.96

// Buy at stop breakout above today's high (momentum entry)
EntrySetup: IsSetup
EntryStop: H * 1.001
```

### 8.3 Position Sizing Approaches

```
// Equal weight — 10% of allocation per position
Quantity: 10
QtyType:  Percent

// Fixed dollar per position
Quantity: 10000
QtyType:  Value

// Volatility sizing — risk 0.5% of allocation per ATR
Quantity: 0.005 * S.Alloc / ATR(14)
QtyType:  Shares
```

### 8.4 Exit Bracket (Stop + Target + Rule)

All three can coexist. The first to trigger wins:

```
ExitRule:  RSI(14) > 50 or BarsHeld >= 20
ExitStop:  FillPrice - 2 * ATR(14)    // hard stop
ExitLimit: FillPrice + 3 * ATR(14)    // profit target
```

### 8.5 Market Regime Filter

A common pattern using `Extern` to check a benchmark:

```
Data:
  Regime: Extern($SPY, C > MA(C, 200))
  IsSetup: rsi14 < 30 and C > ma200 and Regime
```

### 8.6 Ranking Setups

When more setups exist than can be entered (due to `MaxPositions`), `SetupScore` selects which ones:

```
// Enter the most oversold (lowest RSI) setups first
SetupScore: -RSI(3)

// Enter highest-ranked by custom composite score
SetupScore: AdjSlope * VolatilityFilter
```

### 8.7 Template Reuse

```
Template: base
  Quantity:     10
  QtyType:      Percent
  MaxPositions: 10

Strategy: "LongMR"
  Using:      base
  Side:       Long
  EntrySetup: rsi3 < 20 and C > ma200
  ExitRule:   C > C[1] or BarsHeld = 5

Strategy: "ShortMR"
  Using:      base
  Side:       Short
  EntrySetup: rsi3 > 80 and C < ma50
  ExitRule:   C < C[1] or BarsHeld = 5
```

---

## 9. Common Gotchas

1. **Look-ahead bias in Data:** You can write `C[-1]` (tomorrow's close) in `Data:` and it will compile. You won't get an error — it's on you to avoid this in entry/exit logic.

2. **RSI warmup:** `RSI(14)` needs 28+ bars before it stabilizes (Wilder smoothing). On the first ~28 bars, values are unreliable. Use `StartDate` at least a year after `Import StartDate`.

3. **FillPrice on day 0:** If you use `FillPrice` in a formula that is evaluated alongside `EntrySetup` (e.g., in `Quantity`), it returns `OrderPrice`, not the actual fill. This is usually correct behavior. Override with `QtyPrice: FillPrice` if you need actual fill-based sizing.

4. **BarsHeld = 0 on first day:** The entry day itself is not counted. A time stop of 1 bar means `BarsHeld = 1`, which is the day after entry.

5. **Multiple exits:** If both `ExitStop` and `ExitLimit` are specified with default `Intraday` timing, both orders are placed as a bracket. If neither triggers intraday, the position remains open. The bracket is re-evaluated each day.

6. **Quantity = 0 skips the trade:** Returning 0 from `Quantity` does not cause an error — the setup is silently skipped with reason "zero quantity."

7. **Case insensitivity:** RealScript is case-insensitive for built-in names (`rsi` = `RSI`), but user-defined Data names are conventionally camelCase or lowercase.

8. **#SlowCalc is required for Log(C) in Slope:** Log of unadjusted prices cannot be buffered across splits. Always prefix `Slope(Log(C), n)` and `Correl(Log(C), ...)` with `#SlowCalc`.

---

## 10. Minimal Complete Example

```
// RSI Mean Reversion — minimal working script

Settings:
  DataFile:    mydata.rtd
  AccountSize: 100000
  StartDate:   2005-01-01
  EndDate:     Latest

Parameters:
  rsiLen:  14
  stopMlt: 2

Data:
  rsi:     RSI(rsiLen)
  ma200:   MA(C, 200)
  atr14:   ATR(14)
  Regime:  Extern($SPY, C > MA(C, 200))
  IsSetup: rsi < 30 and C > ma200 and Regime

Strategy: "RSI_MR"
  Side:        Long
  EntrySetup:  IsSetup
  SetupScore:  -rsi              // most oversold first
  ExitRule:    rsi > 50 or BarsHeld >= 20
  ExitStop:    FillPrice - stopMlt * atr14
  ExitLimit:   FillPrice + stopMlt * 1.5 * atr14
  Quantity:    10
  QtyType:     Percent
  MaxPositions: 10
  MaxExposure:  100
```

---

## 11. Function Name Aliases (Quick Reference)

RealScript has many functions with alternate names. Either form is valid:

| Primary | Alias | Notes |
|---------|-------|-------|
| `MA` | `Avg` | Simple moving average |
| `EMA` | `XAvg` | Exponential moving average |
| `Highest` | `HHV` | Highest value in range |
| `Lowest` | `LLV` | Lowest value in range |
| `ROC` | `PctChg` | % rate of change |
| `STOC` | — | Stochastic (NOT "Stochastic" — the full word doesn't work) |
| `IF` | `IIF` | Conditional |
| `Open` | `O` | Bar open |
| `High` | `H` | Bar high |
| `Low` | `L` | Bar low |
| `Close` | `C` | Bar close |
| `Volume` | `V` | Bar volume |
| `Shares` | `Contracts` | Position size |
