# Roadmap — Grounded Reliability

> Goal: every answer and every generated object is provably derived from the
> indexed codebase (the single source of truth), compiles under the real X++
> compiler, and passes Best Practice checks — while keeping the number of
> agentic rounds per task at the minimum.
>
> The sibling [`d365fo-mcp-server`](https://github.com/dynamics365ninja/d365fo-mcp-server)
> has landed a "grounded reliability" wave (PR #514–#530). This roadmap ports
> those proven mechanisms to the CLI and adapts them to its multi-process,
> token-efficient model. Source references below point at the MCP repo
> (`src/tools/*.ts`, `src/utils/*.ts` on `origin/main`).

## Phase 1 — Offline validation gate (`validate xpp`, `validate references`)

The CLI's `lint` audits the *index*; nothing today validates *generated code
before it is written*. Port the two MCP gates that close exactly this hole:

1. **`d365fo validate xpp [--file F | stdin] [--code-type xpp|xml-table|xml-any]`**
   — port of `validateXpp.ts` (687 lines). Offline, <50 ms, no VM. Rules:
   - SEL001 `today()` deprecated, SEL002 `forceLiterals`, SEL003 `crossCompany`
     on joined buffer, SEL004 nested `while select`, SEL005 function call in
     `where`;
   - COC001 default-param value copied into CoC wrapper, COC002 `[ExtensionOf]`
     not `final`, COC003 name not ending `_Extension`;
   - BP001 hardcoded string in `info/warning/error/checkFailed`, BP002
     `doInsert/doUpdate/doDelete` without migration justification, BP003
     generic doc-comment;
   - XML001 missing `AlternateKey`, XML002–XML005 data-driven property rules
     (see Phase 2).
   Output: `{rule, severity, line, excerpt, fix}[]` in the standard envelope.
   Implementation: `D365FO.Core/Validation/XppValidator.cs`; share regex canon
   with the existing index-side lint where rules overlap.

2. **`d365fo validate references [--file F | stdin] [--context Owner]`**
   — port of `resolveReferences.ts` (879 lines), the semantic
   anti-hallucination gate. Extracts every external identifier from an X++
   snippet and proves it against the index:
   - intrinsics (`tableStr`, `fieldStr`, `classStr`, `enumStr`,
     `extendedTypeStr`, `menuItem*Str`, …) — must exist, else **error**;
   - `Type::member` static access incl. **arity check** from the indexed
     signature;
   - declared variable types; bound buffer field/method access
     (`buffer.Field`) when the buffer's table type is known;
   - label tokens `@File:Id` and legacy `@SYS12345`.
   Conservative severity model (false blocks are worse than misses): kernel
   classes and not-yet-created label files downgrade to warnings.
   Implementation: `D365FO.Core/Validation/ReferenceResolver.cs` over
   `MetadataRepository`.

Both gates are what makes "the generated code compiles" checkable on any
platform in milliseconds, before `build` ever runs.

## Phase 2 — `property_stats` mining: data-driven BP property rules

Port the `property_stats` work (MCP PR #525 + perf follow-up #530):

- During `index extract`, mine property distributions **from standard
  (non-custom) models** into a `PropertyStats` table: `Label`, `TableGroup`,
  `ClusteredIndex`, `CacheLookup`, field-EDT coverage, form-pattern defaults.
- `validate xpp` XML002–XML005 use mined thresholds instead of static
  opinions ("97 % of standard main tables set TableGroup → flag its absence,
  and suggest the most common standard values").
- **Scaffolders consult the same stats**: `TablePattern`,
  `FormPatternTemplates` and the other generators set default properties to
  what the standard models actually use, so generated objects match BP checks
  out of the box.
- Port the extract-side perf tricks: buffered `property_stats` writes,
  `sortModelsBySize` for better parallel scheduling.

## Phase 3 — `prepare` aggregators: minimum agentic rounds

One call must replace the 4–6 discovery rounds an agent does today
(search → validate name → suggest edt → search labels → patterns):

1. **`d365fo prepare change <Object> [--method M] [--proposed-name N] --goal "…"`**
   — port of `prepareChange.ts`: exact method signature, existing CoC wrappers
   (bridge-first, index fallback), CoC/event-handler eligibility, recommended
   extension strategy, naming validation — gathered by parallel queries,
   returned in one envelope.
2. **`d365fo prepare create <Name> --type <T> --goal "…" [--field-hint …]`**
   — port of `prepareCreate.ts`: collision check (exact + prefixed), naming
   validation incl. the prefix the generator will actually apply, similar
   existing objects to copy patterns from, EDT suggestions per planned field,
   reusable existing labels, mined property defaults from `PropertyStats`.

Both return a **grounding token** (Phase 4) proving the model looked at the
real codebase before writing.

## Phase 4 — Grounding tokens + write-side enforcement

Adapt `provenanceStore.ts` to the CLI's multi-process reality:

- File-backed provenance store under `~/.d365fo/provenance/` (SHA-256 token,
  30-min TTL, object-bound), since each CLI invocation is a fresh process.
- `generate …` commands accept `--grounding-token`. With
  `D365FO_GROUNDING_ENFORCE=true`:
  - extension-shaped generators (coc, extension, event-handler) **require** a
    valid token bound to the target object;
  - every generator runs `validate references` + `validate xpp` internally on
    its own output and fails closed with structured violations instead of
    writing a broken file;
  - the envelope gains a `grounding` section (token used, checks run,
    verifiedCount).
- Mirror the MCP hybrid-guard lesson (PR #529): enforcement must be bypassable
  where the token issuer is not reachable, logged once — never dead-loop the
  agent.

## Phase 5 — Index freshness + label parity

- **Staleness detector** — port `indexStaleness.ts` (mtime scan, 5 000-file
  cap, 60 s tolerance): `index status` and `doctor` compare the newest
  `.xml`/`.label.txt` mtime against the index bookkeeping timestamp and emit
  the `stale-index` warning. *Note: `agent-prompt` already documents this
  warning, but nothing emits it today — this closes a documented-but-missing
  behaviour.*
- **Label parity** with MCP fixes:
  - `label create --languages <list>` scoping writes to selected locales, with
    on-disk casing resolution to avoid duplicate locale folders (PR #519/#520);
  - case-insensitive **ordinal** collation when sorting label IDs (PR #524);
  - label-index reconciliation on `index refresh` so deleted/renamed entries
    don't linger (PR #517);
  - verify special-character search parity (`_`, FTS5 vs LIKE fallback,
    PR #514 vs CLI #51).

## Phase 6 — Compiler loop closure (Windows VM)

- **Structured xppc diagnostics in `d365fo build`** — port the parser from
  `buildProject.ts` (PR #528): compiler output becomes
  `{file, line, code, message, hint}[]`, error codes cross-linked to
  `D365FoErrorCodes` help. The agent fixes compile errors from one structured
  result instead of re-reading raw MSBuild logs.
- Normalize `bp check` findings into the same violation shape as
  `lint`/`validate xpp` so all three gates speak one schema.

## Phase 7 — Instructions + token economy

- **Slim `agent-prompt`** — mirror the MCP "slim system prompt" +
  copilot-instructions restructuring (PR #526): a short rule canon plus a
  need→command mapping table; worked examples live only in Skills. Teach the
  new loop explicitly: `prepare → generate → (auto)validate → build`.
- **Skills update** — each authoring skill leads with the single `prepare`
  call instead of the multi-round discovery sequence it documents today.
- **`d365fo-mcp` adapter** — port MCP tool annotations
  (title + readOnly/destructive/idempotent hints, PR #529) and the
  duplicate-call dedup cache + agentic-loop detection (PR #528) into
  `D365FO.Mcp`/daemon, where repeated identical read calls are served from a
  short-TTL cache with a loop hint.

## Phase 8 — Golden quality-gate suite

Port the concept of `tests/golden/quality-gate.test.ts` (MCP PR #528):

- a fixture mini-index checked into `tests/`;
- every scaffolder's output must pass `validate references` +
  `validate xpp` **clean**;
- an injected hallucinated symbol (fake field, wrong arity, missing label)
  must be flagged as error;
- staleness must trigger on a touched fixture file;
- runs in CI as a blocking gate, so the offline grounding chain can never
  silently regress.

## Status

All eight phases are implemented (June 2026):

| Phase | Status | Where |
|---|---|---|
| 1 — validate gates | ✅ | `d365fo validate xpp` / `validate references`; `D365FO.Core/Validation/XppValidator.cs`, `ReferenceResolver.cs` (+ schema v14: `ExtensionFields` so extension-added fields resolve) |
| 2 — property_stats | ✅ | mined into `PropertyStats` during `index extract` (standard models only); XML002–005 data-driven; scaffolder emits `ClusteredIndex`; largest-models-first extract scheduling |
| 3 — prepare | ✅ | `d365fo prepare change` / `prepare create` — one call returns context + grounding token |
| 4 — enforcement | ✅ | file-backed `ProvenanceStore` (`~/.d365fo/provenance`, 30-min TTL, object-bound); `--grounding-token` on generate; `D365FO_GROUNDING_ENFORCE=true` fails closed on missing token / unresolved references / BP errors; CoC scaffolder now picks `tableStr`/`classStr`/`formStr` by target kind |
| 5 — freshness/labels | ✅ | `stale-index` warning in `index status` + `doctor` (`IndexStaleness`); `label create --lang a,b,c` multi-locale with on-disk casing reuse; ordinal-collation sorted label inserts |
| 6 — xppc diagnostics | ✅ | `d365fo build` parses `dynamics://` compiler lines into `{object, member, line, column, message, hint}` (`XppcDiagnostics`), keeps the payload on failure, detects stale-symbol full-build need |
| 7 — instructions/MCP | ✅ | agent-prompt teaches prepare→generate→validate→build; skills lead with `prepare`; `d365fo-mcp` ships tool annotations + 60-s dedup cache with loop hint |
| 8 — golden suite | ✅ | `tests/D365FO.Core.Tests/GoldenQualityGateTests.cs` — every scaffolder output passes both gates clean; injected hallucinations (fake table/field/label, wrong arity, copied CoC default) must be flagged |
