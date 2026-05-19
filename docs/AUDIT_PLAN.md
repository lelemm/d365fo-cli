# Plan: d365fo-cli Comprehensive Audit & Enhancement

## TL;DR

Full audit of the d365fo-cli solution identifying incomplete functionality, missing scaffolding, index optimization opportunities, and new integration-focused features. The solution is mature (~55+ commands, v10 schema, 9 form patterns, Bridge + MCP coexistence) but has gaps in scaffolding coverage, lint rules, integration patterns, and index performance. This plan proposes phased improvements grouped by priority and feasibility.

---

## Phase 1: Index & Performance Optimizations ✅

Extraction and index operations are the heaviest workloads. These improvements reduce wall-clock time and resource consumption.

### 1.1 Parallel model extraction ✅

Currently models are extracted sequentially (per-file parallelism exists inside each model). Add `Parallel.ForEach` over models with configurable `--parallelism N` (default: `Environment.ProcessorCount / 2`). Guard the SQLite write path with a per-model commit lock.

- File: `src/D365FO.Core/Extract/MetadataExtractor.cs`
- File: `src/D365FO.Cli/Commands/Index/IndexCommands.cs` (ExtractCore method)

### 1.2 Index VACUUM / ANALYZE command ✅

After large extractions, SQLite benefits from `VACUUM` (reclaims space) and `ANALYZE` (updates query planner stats). Add `d365fo index optimize` command.

- File: `src/D365FO.Core/Index/MetadataRepository.cs` — new `Optimize()` method
- File: `src/D365FO.Cli/Commands/Index/IndexCommands.cs` — new `IndexOptimizeCommand`

### 1.3 Incremental FTS5 updates ✅

Current FTS5 rebuild is full (`INSERT INTO LabelFts(LabelFts) VALUES('rebuild')`). Switch to incremental content-sync triggers or per-model FTS row delete + re-insert during `ApplyExtract`.

- File: `src/D365FO.Core/Index/Schema.sql`
- File: `src/D365FO.Core/Index/MetadataRepository.cs` (ApplyExtract label section)

### 1.4 Index export/import for team sharing ✅

Add `d365fo index export --out index.tar.gz` and `d365fo index import --from index.tar.gz` to share pre-built indexes across team (CI artifact).

- New command files under `src/D365FO.Cli/Commands/Index/`

### 1.5 Daemon warm-up command ✅

`d365fo daemon warmup` pre-loads frequently-queried tables/classes into SQLite page cache (single `SELECT count(*) FROM ...` per major table).

- File: `src/D365FO.Cli/Commands/Daemon/DaemonCommands.cs`

---

## Phase 2: Missing Scaffolding Commands

The `generate` branch covers 12 artifact types. Key D365FO patterns are missing.

### 2.1 `generate sysoperation`

Scaffold the DataContract + Service + Controller triplet (the standard pattern for batch jobs).

**Options:** `--contract-name`, `--service-name`, `--controller-name`, `--param <name>:<type>`, `--execution-mode Batch|Sync`.

- New: `src/D365FO.Core/Scaffolding/SysOperationScaffolder.cs`
- New: `src/D365FO.Cli/Commands/Generate/GenerateSysOperationCommand.cs`
- Reference: `skills/_source/x++-class-authoring.md` (SysOperation section)

### 2.2 `generate number-sequence`

Scaffold NumberSeqApplicationModule CoC extension + EDT + form handler wiring.

**Options:** `--module-name`, `--edt <name>`, `--scope Company|Shared`.

- New: `src/D365FO.Core/Scaffolding/NumberSequenceScaffolder.cs`

### 2.3 `generate workflow`

Scaffold WorkflowDocument subclass + WorkflowType XML + `canSubmitToWorkflow()` table method stub.

**Options:** `--table`, `--approval-name`, `--task-name`.

- New: `src/D365FO.Core/Scaffolding/WorkflowScaffolder.cs`

### 2.4 `generate menu-item`

Scaffold AxMenuItemDisplay/Action/Output.

**Options:** `--kind Display|Action|Output`, `--object <name>`, `--object-type Form|Class|Report`, `--label`.

- New: `src/D365FO.Core/Scaffolding/MenuItemScaffolder.cs`

### 2.5 `generate edt`

Scaffold AxEdt with proper `extends`, `stringSize`, `label`.

**Options:** `--extends <base>`, `--base-type String|Int|Real|Date|...`, `--size N`, `--label`.

- Extend: `src/D365FO.Core/Scaffolding/XppScaffolder.cs`

### 2.6 `generate enum`

Scaffold AxEnum with values.

**Options:** `--value <name>:<intValue>[:label]`, `--is-extensible Yes|No`.

- Extend: `src/D365FO.Core/Scaffolding/XppScaffolder.cs`

### 2.7 `generate query`

Scaffold AxQuery with datasources and joins.

**Options:** `--ds <table>`, `--join <table>:<joinKind>:<parentDs>`.

- New: `src/D365FO.Core/Scaffolding/QueryScaffolder.cs`

---

## Phase 3: Lint & Static Analysis Expansion

Current lint has 6 rules. D365FO BP has ~80+ rules. Expand in-process lint to cover the highest-value gaps without requiring a Windows VM.

### 3.1 Nested select detection (`BPCheckNestedLoopinCode`)

Scan method source for `while select … { … while select` patterns in indexed X++ source.

- File: `src/D365FO.Core/Index/MetadataRepository.cs` (new lint query)
- File: `src/D365FO.Cli/Commands/Lint/LintCommand.cs`

### 3.2 Label reference validation (`BPErrorUnknownLabel`)

Cross-check `@File:Key` references in code/XML against indexed Labels table.

- File: `src/D365FO.Core/Index/MetadataRepository.cs`

### 3.3 Missing alternate key detection (`BPCheckAlternateKeyAbsent`)

Tables with no `AlternateKey=Yes` index. Already partially covered by `table-no-index` but not specifically checking the AlternateKey flag.

### 3.4 Circular model dependency detection

Extend `models coupling` to flag true cycles in the ModelDependencies graph (not just instability metrics).

- File: `src/D365FO.Cli/Commands/Models/ModelsCommands.cs`

### 3.5 Duplicate method signature detection (`BPDuplicateMethod`)

Find methods with identical names across class inheritance chains within the same model.

- New lint category in `MetadataRepository.cs`

### 3.6 Public instance field detection

Flag classes with public instance members (rule #9 from copilot-instructions).

- Requires class declaration parsing enhancement in extractor.

---

## Phase 4: Integration Patterns — New Skills & Commands

Based on Microsoft Learn documentation for D365FO integrations, these are missing from both the CLI surface and the skills system.

### 4.1 New skill: `integration-patterns`

Covers OData, Custom Services, Data Management Framework, Business Events, Dual-write, Electronic Reporting integration patterns with grounding through `d365fo` commands.

- New: `skills/_source/integration-patterns.md`
- New: `skills/copilot/integration-patterns.instructions.md`
- Reference: <https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/data-entities/integration-overview>

### 4.2 New skill: `business-events-authoring`

How to define custom business events, activate them, and subscribe (Azure Service Bus, Event Grid, Logic Apps, Power Automate).

- Reference: <https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/business-events/home-page>

### 4.3 `search business-event`

Index AxBusinessEvent objects and expose them via search/get.

- Requires new extraction path in `MetadataExtractor` + new schema table.

### 4.4 `get entity --odata-metadata`

Emit the OData `$metadata` fragment for a data entity (fields, navigation properties, key) to help integration developers configure external systems.

- Extend: `src/D365FO.Cli/Commands/Get/GetCommands.cs` (GetEntityCommand)

### 4.5 `analyze integration`

Cross-check data entities: are all mandatory fields mapped? Is `IsPublic=Yes`? Is there a staging table? Are key fields unique?

- New: `src/D365FO.Cli/Commands/Analyze/AnalyzeIntegrationCommand.cs`

### 4.6 New skill: `custom-service-authoring`

Patterns for authoring JSON/SOAP custom services: Service class + Contract + ServiceGroup + deployment.

- Reference: <https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/dev-ref/xpp-attribute-classes>

### 4.7 `generate business-event`

Scaffold a custom business event class (extends `BusinessEventsBase`) + contract.

- New: `src/D365FO.Core/Scaffolding/BusinessEventScaffolder.cs`

---

## Phase 5: Developer Experience & Tooling

### 5.1 `d365fo impact <object>`

Change-impact analysis: given an object name, show all downstream consumers (CoC wrappers, event handlers, extensions, form references, menu items) that would be affected by a modification.

- Combines: `find coc` + `find handlers` + `find extensions` + `find refs` into a single report.
- New: `src/D365FO.Cli/Commands/Analyze/AnalyzeImpactCommand.cs`

### 5.2 `d365fo compare <name> --base <gitref>`

Structured comparison of an AOT object between two Git revisions (not raw diff — shows "added field X", "changed method signature Y").

- Extends: `src/D365FO.Cli/Commands/Review/ReviewDiffCommand.cs`

### 5.3 `d365fo generate migration-script`

Scaffold a data-migration runnable class with the proper `doInsert`/`doUpdate` pattern (the one exception where those methods ARE appropriate) + transaction batching.

- New scaffold type

### 5.4 Tab-completion / shell integration

Generate completion scripts for PowerShell, bash, zsh via `d365fo completion <shell>`. Spectre.Console supports this natively.

- File: `src/D365FO.Cli/Program.cs`

### 5.5 Telemetry-free performance counters

Track per-command execution time in the SQLite DB (a `CommandTimings` table). Surface via `d365fo stats --perf` for self-diagnosis without external telemetry.

- File: `src/D365FO.Core/Index/MetadataRepository.cs`

---

## Phase 6: Test Coverage Expansion

### 6.1 Integration tests for Bridge

Currently no test project for D365FO.Bridge. Add mock-based tests validating JSON-RPC dispatch, error handling, kernel-enum fallback.

- New: `tests/D365FO.Bridge.Tests/`

### 6.2 Scaffolding snapshot tests

For each `generate` command, add a "golden file" snapshot test that detects unintentional XML output regressions.

- Extend: `tests/D365FO.Cli.Tests/`

### 6.3 CLI integration tests

Invoke the compiled `d365fo` binary against the MiniAot sample and verify exit codes + JSON output shape.

- New: `tests/D365FO.Cli.Tests/CliIntegrationTests.cs`

### 6.4 Lint rule unit tests

Each new lint category from Phase 3 gets dedicated test fixtures.

- Extend: `tests/D365FO.Core.Tests/`

---

## Relevant Files

| Path | Role |
|------|------|
| `src/D365FO.Core/Index/MetadataRepository.cs` | Central repository; all new queries + lint rules land here |
| `src/D365FO.Core/Index/Schema.sql` | SQLite schema; new tables for business events, command timings |
| `src/D365FO.Core/Extract/MetadataExtractor.cs` | Extraction pipeline; parallelism improvements |
| `src/D365FO.Core/Scaffolding/XppScaffolder.cs` | Scaffold entry point; new generators extend here |
| `src/D365FO.Cli/Program.cs` | Command registration; every new command registers here |
| `src/D365FO.Cli/Commands/Generate/` | Generate command implementations |
| `src/D365FO.Cli/Commands/Lint/LintCommand.cs` | Lint rule orchestration |
| `src/D365FO.Mcp/ToolHandlers.cs` | MCP parity; new Core features get MCP wrappers here |
| `skills/_source/` | Source markdown for all skills |
| `skills/copilot/` | Copilot-format skill output |

---

## Verification Criteria

1. **Unit tests pass** — `dotnet test` across all test projects after each phase
2. **MiniAot fixture validates** — end-to-end extract + query cycle still works
3. **Schema migration is backwards-compatible** — `EnsureSchema()` upgrades cleanly from v10
4. **New scaffolding emits valid AOT XML** — `XDocument.Parse()` + pattern assertions
5. **Lint SARIF output validates** — feed output to VS Code SARIF Viewer extension
6. **Shell completion scripts install correctly** — test on PowerShell 7 + bash
7. **Index export/import roundtrip** — export from one machine, import on another, queries produce identical results
8. **No regression in extraction speed** — benchmark before/after parallelization on the MiniAot sample

---

## Key Decisions

| Decision | Rationale |
|----------|-----------|
| SysOperation scaffold emits 3 separate .xml files | Consistent with how D365FO organizes AxClass objects |
| Business events require new schema table at v11 | Cannot fit into existing schema cleanly |
| Parallel extraction defaults to half CPU cores | Avoid starving other processes on dev VMs |
| Skills remain format-agnostic | Source in `_source/`, then `emit-skills.ps1`/`.py` produces both Copilot and Anthropic |
| No breaking changes to existing commands | JSON envelope shapes must remain stable |
| Schema changes bundled into v11 | One migration, not incremental bumps per-phase |

**Excluded from scope:** GUI/VS Code extension (separate repo), cloud-hosted build pipeline, D365FO kernel debugging.

---

## Recommended Priority

```
Phase 2 (scaffolding, items 2.1–2.7)     ← immediate ROI, fills functional gaps
  ↓
Phase 1 (performance, items 1.1–1.3)     ← faster developer experience
  ↓
Phase 4 (integration, items 4.1–4.7)     ← unlocks new use cases
  ↓
Phase 3 (lint, items 3.1–3.6)            ← quality gate improvements
  ↓
Phase 5 (DX, items 5.1–5.5)             ← polish
  ↓
Phase 6 (tests, items 6.1–6.4)          ← ongoing with each phase
```

---

## Further Considerations

1. **Schema version strategy** — Multiple phases add DB schema. Bundle all into v11, or bump per-phase? **Recommendation:** one v11 bump with all new tables; migration code handles pre-v11 gracefully.

2. **Learn.microsoft.com reference sync** — Integration patterns change across D365FO releases (10.0.x). Should the CLI embed versioned references or always link to "latest"? **Recommendation:** link to latest + note the PU version tested against in skill metadata.

3. **MCP parity** — Every new `D365FO.Core` feature should get a corresponding MCP tool wrapper in `ToolHandlers.cs` + entry in `ToolCatalog`. Track parity in a checklist per phase.
