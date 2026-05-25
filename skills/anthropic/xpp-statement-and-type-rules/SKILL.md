---
name: xpp-statement-and-type-rules
description: X++ statement-level and type-system rules â€” switch / break, ternary, no-DB-null sentinels, casting (`as` / `is`), `using` blocks, embedded function declarations. Invoke when writing any non-trivial X++ control flow or type conversion.
applies_when: User intent involves X++ control flow, switch statements, casting, null/empty checks on date/utcDateTime, or IDisposable resource handling.
---
> â›” **NEVER write X++ AOT XML files directly** via PowerShell, terminal file commands (`Set-Content`, `Out-File`, `New-Item`), editor write tools, or any raw text approach. The XML schema (`<AxClass>`, `<AxTable>`, `<AxForm>`, `<Methods>`, `<SourceCode>`) is proprietary â€” LLMs have not been trained on it reliably. **ALWAYS use `d365fo generate â€¦` commands** to produce correct AOT XML. If `d365fo` is unavailable in PATH, stop and ask the user to install it.

# X++ statement & type rules

> **Sources of truth:** [learn:xpp-conditional](https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/dev-ref/xpp-conditional) and [learn:xpp-variables-data-types](https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/dev-ref/xpp-variables-data-types).

## `switch` / `break`

- **`break` is required** at the end of every `case`. Implicit fall-through compiles but is misleading.
- To match multiple values to a single branch use the **comma-list** form â€” never empty fall-through:

```xpp
// âœ… CORRECT
switch (mod(year, 4))
{
    case 13, 17, 21:
        result = "leap-adjacent";
        break;
    default:
        result = "ordinary";
        break;
}
```

## Ternary

`cond ? a : b` â€” both branches must have the same type. No implicit widening of `int` â†” `real`.

## â— X++ has NO database null

Each primitive has a "null-equivalent" sentinel:

| Type | Null-equivalent value |
|---|---|
| `int` / `int64` | `0` |
| `real` | `0.0` |
| `str` | `""` |
| `date` | `1900-01-01` (`dateNull()`) |
| `utcDateTime` | date-part `1900-01-01` (`utcDateTimeNull()`) |
| `enum` | element with value `0` |
| `boolean` | `false` |
| `RecId` | `0` |

In SQL `where` clauses these compare as **false** (rows with sentinel values are NOT returned by `where field`). In plain expressions they are ordinary values.

```xpp
// âŒ WRONG â€” there is no null
if (myDate == null) { â€¦ }

// âœ… CORRECT
if (!myDate)              { â€¦ }   // boolean test on sentinel
if (myDate == dateNull()) { â€¦ }   // explicit
```

Same for `utcDateTime` â€” compare against `utcDateTimeNull()` or use `if (!myUtc)`.

## Casting

- Prefer **`as`** (returns `null` on type mismatch) and **`is`** (boolean test) over hard down-casts.
- Hard down-casts (`(SubClass)objectExpr`) on object-typed expressions throw `InvalidCastException` on mismatch.
- Late binding exists for `Object` and `FormRun` only â€” accept the runtime cost if you use it.

```xpp
common = ledgerJournalTrans;
LedgerJournalTrans trans = common as LedgerJournalTrans;
if (trans) { trans.update(); }
```

## `using` blocks for IDisposable

Equivalent to `try` + `finally { x.Dispose(); }` but shorter and exception-safe.

```xpp
using (var reader = new StreamReader(path))
{
    line = reader.ReadLine();
}
```

## Embedded function declarations

Local functions inside a method **can read** variables declared earlier in the enclosing method but **cannot leak** their own variables out. Prefer them over a private helper method only when the helper truly does not belong to the class API.

## Hard "never" list

- **Never** test `myDate == null` â€” there is no null in X++.
- **Never** rely on switch fall-through â€” always `break` (or use the comma-list form).
- **Never** down-cast an `Object` without an `is` guard (or an `as` + null check).
