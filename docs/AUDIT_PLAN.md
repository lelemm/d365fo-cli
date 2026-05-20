# d365fo-cli — Capabilities & Feature Overview

Full reference of what the tool provides. For setup see [SETUP.md](SETUP.md), for worked examples see [EXAMPLES.md](EXAMPLES.md), for internals see [ARCHITECTURE.md](ARCHITECTURE.md).

---

## Index

The SQLite index (`$D365FO_INDEX_DB`) stores AOT metadata extracted from `PackagesLocalDirectory`. It covers 22 AOT object types and is populated by `d365fo index extract`.

### Index maintenance commands

| Command | What it does |
|---------|-------------|
| `index build` | Create or migrate the schema in-place |
| `index extract` | Full extraction of all packages |
| `index extract --model <M>` | Scoped extraction of one model (seconds) |
| `index refresh` | Re-extract only models whose content fingerprint changed |
| `index refresh --force` | Re-extract all models regardless of fingerprint |
| `index status` | Show per-model row counts and last-extracted timestamps |
| `index history` | Show per-model extraction run history |
| `index export` | Dump the index to a portable archive |
| `index import` | Restore from an archive |
| `index optimize` | Run `VACUUM` + `ANALYZE` to compact and re-plan |
| `doctor` | End-to-end health check: paths, schema version, object counts |

---

## Search & Discovery

### `search`

Fuzzy substring search across every indexed type.

```
search table|class|edt|enum|form|menu-item|query|view|entity|report
search service|service-group|workflow|map|label|role|duty|privilege
search business-event|security-policy|configuration-key|tile|workspace
search batch-jobs
search any <query>   — UNIONs all types, returns byKind counts
```

### `get`

Full metadata for one named object. Returns a structured JSON envelope.

```
get table|class|edt|enum|form|menu-item|query|view|entity|report
get service|service-group|map|role|duty|privilege|label
get business-event|security-policy
```

`get table <T> --odata-metadata` emits an OData `<EntityType>` + `<EntitySet>` XML fragment for the table.

### `find`

Cross-reference queries.

```
find coc <Class> [--method <M>]       — Chain-of-Command wrappers
find usages <needle> [--kind k,k,…]  — All references by kind
find extensions <target>              — All object extensions
find handlers <object>                — Event subscribers
find relations <table>                — FK relations
find refs --xref                      — Path/line/kind via DYNAMICSXREFDB
find form-patterns [--pattern P]      — Form pattern histogram / filter
find batch-jobs [--model M]           — RunBaseBatch subclasses
```

### `models`

```
models list                   — All indexed models with publisher/layer/custom flag
models deps <Model>           — Dependency tree
models coupling [--top N]     — Fan-in / fan-out / instability (Martin metric)
models coupling --only-cycles — Tarjan SCC: circular dependencies only
```

### `stats`

Per-model object counts + top-N tables by field count and classes by method count.

```
stats [--top N]
stats --perf     — Command timing percentiles from local telemetry
```

### `read`

Pull X++ source snippets directly from AOT XML — no compiler or VM needed.

```
read class <C> --method <M>
read table <T> [--declaration]
read form <F> [--lines 10-40]
```

---

## Lint

`d365fo lint` runs 16 in-process heuristics against the index without touching the VM.

```sh
d365fo lint                                         # all rules, custom models only
d365fo lint --all-models                            # include MS/ISV content
d365fo lint --category insert-in-loop,force-literals  # specific rules
d365fo lint --format sarif > lint.sarif             # SARIF 2.1.0 for CI
```

### Rule catalogue

| Rule | Finds | Severity |
|------|-------|---------|
| `table-no-index` | Tables without cluster or alternate-key index | warning |
| `ext-named-not-attributed` | `*_Extension` classes missing `[ExtensionOf]` | warning |
| `string-without-edt` | String fields without an EDT | warning |
| `today-usage` | `today()` calls (`BPUpgradeCodeToday`) | warning |
| `do-insert-update` | `doInsert()` / `doUpdate()` / `doDelete()` in non-migration code | warning |
| `doc-comment-missing` | Public/protected methods without `/// <summary>` | warning |
| `nested-select` | `while select` nested inside another loop | warning |
| `insert-in-loop` | `.insert()` inside a loop body — suggest `RecordInsertList` | warning |
| `tts-try-catch` | `try` inside `ttsbegin`/`ttscommit` without catching `UpdateConflict` | warning |
| `empty-table-method` | Table method override with empty body | warning |
| `runbase-no-can-go-batch` | `RunBaseBatch` subclass without `canGoBatch() { return true; }` | warning |
| `force-literals` | `forceLiterals` in a select — SQL injection risk | error |
| `cache-lookup-mismatch` | `CacheLookup` inconsistent with `TableGroup` | warning |
| `missing-delete-action` | Table relation without `DeleteAction` or `OnDelete` | warning |
| `no-alternate-key` | Tables with unique indexes but no `AlternateKey` | warning |
| `unknown-label-ref` | `@File:Key` label references that don't resolve in the index | error |

---

## Scaffolding (`generate`)

All scaffolders write atomically (`.tmp` + move, `.bak` on overwrite). Pass `--install-to <Model>` to drop straight into a model folder via the Bridge.

### AOT object types

| Command | Emits |
|---------|-------|
| `generate table` | `AxTable` XML with fields, indexes, relations (9 patterns) |
| `generate class` | `AxClass` skeleton |
| `generate coc` | Chain-of-Command extension class |
| `generate form` | `AxForm` XML (9 patterns: SimpleList, DetailsMaster, Workspace …) |
| `generate entity` | `AxDataEntityView` |
| `generate extension` | `AxTableExtension` / `AxFormExtension` / `AxEdtExtension` / `AxEnumExtension` |
| `generate event-handler` | X++ event subscriber class with correct attribute |
| `generate privilege` | `AxSecurityPrivilege` |
| `generate duty` | `AxSecurityDuty` |
| `generate role` | `AxSecurityRole` (new or merge into existing) |
| `generate menu-item` | `AxMenuItemDisplay` / `AxMenuItemAction` / `AxMenuItemOutput` |
| `generate report` | `AxReport` + DP class + optional DataContract class |
| `generate edt` | `AxEdt` |
| `generate enum` | `AxEnum` |
| `generate query` | `AxQuery` with root datasource and optional nested joins |
| `generate sysoperation` | Contract + Service + Controller class triple |
| `generate number-sequence` | Module extension + EDT + form handler class |
| `generate workflow` | `AxWorkflow` + document class + submit stub |
| `generate business-event` | Event class + contract class |
| `generate custom-service` | `AxService` + service class + `AxServiceGroup` |
| `generate runbase` | `RunBase` / `RunBaseBatch` skeleton with dialog parameters |
| `generate security-policy` | `AxSecurityPolicy` (XDS row-level security) |
| `generate migration-script` | Data-fix `Runnable` class with `ttsbegin`/`ttscommit` batching |
| `generate simple-list` | Alias for `generate form --pattern SimpleList` |

### Scaffolding validation helpers

```sh
d365fo find form-patterns --similar-to CustGroup   # pick the right form pattern
d365fo suggest extension CustTable                  # rank extension strategies
d365fo validate name Table FmVehicle --prefix Fm    # naming-rule check
```

---

## Analysis

### `analyze completeness`

Cross-checks a model folder (or single XML file) against the index. Reports missing duties, privileges, EDTs, labels, and parse errors.

```sh
d365fo analyze completeness src/MyModel --output json
d365fo analyze completeness src/MyModel --skip-labels
```

### `analyze integration`

Runs integration-health checks against the index for a model: OData entities without staging tables, services without security entry points, business events without contracts, and batch jobs without `canGoBatch`.

```sh
d365fo analyze integration [--model M] --output json
```

### `analyze impact`

Change-impact graph for a named object: CoC wrappers, event handlers, object extensions, form datasources, data entities, and queries that reference it.

```sh
d365fo analyze impact CustTable --output json
```

### `report-integrations`

Aggregated integration surface for a model: OData entities, custom services, business events, workflow types, and batch jobs — all in one call.

```sh
d365fo report-integrations [--model M] --output json
```

---

## Labels

```sh
# Read
d365fo search label "Customer invoice"
d365fo search label "..." --fts                        # FTS5 ranked search
d365fo get label @SYS12345 --language en-us
d365fo resolve label @SYS12345 --lang en-US,cs

# Write (atomic, preserves BOM + comments)
d365fo label create Key "Value" --file path/Foo.en-us.label.txt
d365fo label create Key "New"   --file path/Foo.en-us.label.txt --overwrite
d365fo label rename OldKey NewKey --file path/Foo.en-us.label.txt
d365fo label delete Key           --file path/Foo.en-us.label.txt
```

---

## Review

```sh
d365fo review diff --base HEAD
d365fo review diff --base main --head feature/my-branch
```

Rules: `FIELD_WITHOUT_EDT`, `FIELD_WITHOUT_LABEL`, `HARDCODED_STRING`, `DYNAMIC_QUERY`.

---

## Developer Experience

### Shell completion

```sh
d365fo completion bash       # bash tab-completion script
d365fo completion zsh        # zsh tab-completion script
d365fo completion powershell # PowerShell tab-completion script
```

Source the output in your shell profile to get `<Tab>` completion for all subcommands and flags.

### Daemon

Keeps the SQLite handle and read caches hot. Starts a `FileSystemWatcher` that auto-triggers incremental refresh on `*.xml` changes (debounce 3 s).

```sh
d365fo daemon start [--no-watch] [--watch-debounce 5000]
d365fo daemon status
d365fo daemon stop
```

---

## Windows-only (D365FO VM)

Wraps the Microsoft tools Visual Studio uses.

```powershell
d365fo build --project path/to/MyModel.rnrproj
d365fo sync --full
d365fo test run --suite MyModel.Tests
d365fo bp check --model MyModel
```

Returns `UNSUPPORTED_PLATFORM` on non-Windows.

---

## MCP server

Exposes the same index and scaffolding surface as the CLI over the `ModelContextProtocol` C# SDK via stdio. ~55 tools covering search, get, find, scaffolding, lint, analysis, and aggregation.

```jsonc
{
  "mcpServers": {
    "d365fo": {
      "command": "dotnet",
      "args": ["run", "--project", "/abs/path/to/src/D365FO.Mcp", "--no-build"],
      "env": { "D365FO_INDEX_DB": "/abs/path/d365fo-index.sqlite" }
    }
  }
}
```

---

## Copilot Skills

15 instruction files in `skills/copilot/` cover the full X++ authoring and review canon. Deploy to an X++ project with the `Install-D365FoCopilotSkills.ps1` script (see [SETUP.md](SETUP.md)).

| Skill | Covers |
|-------|--------|
| `coc-extension-authoring` | Chain-of-Command patterns, wrapping rules, extension naming |
| `data-entity-scaffolding` | OData entities, staging tables, field mapping |
| `event-handler-authoring` | Pre/post event subscribers, delegate pattern |
| `form-pattern-scaffolding` | 9 form patterns, datasource setup, controls |
| `label-translation` | Label file format, BOM, multi-language workflow |
| `model-dependency-and-coupling` | Layering, reference scanning, circular deps |
| `object-extension-authoring` | Table/form/EDT/enum extension conventions |
| `review-and-checkpoint-workflow` | PR review rules, BP check integration |
| `security-hierarchy-trace` | Role → duty → privilege → entry-point chain |
| `table-scaffolding` | TableGroup, indexes, relation, delete actions |
| `x++-class-authoring` | Class hierarchy, CoC, access modifiers |
| `xpp-best-practice-rules` | CAR rule set cross-reference |
| `xpp-class-and-method-rules` | Method-level BP rules |
| `xpp-database-queries` | `select`/`while select`, `forUpdate`, TTS scope |
| `xpp-statement-and-type-rules` | Type system, container, enum usage |
| `business-events-authoring` | `BusinessEventsBase`, contract class, payload |
| `integration-patterns` | OData, custom services, Dual-write surface |
| `custom-service-authoring` | JSON/SOAP services, `ServiceAttribute`, `SysEntryPointAttribute` |

---

## Relevant source files

| Path | Purpose |
|------|---------|
| `src/D365FO.Core/Index/Schema.sql` | SQLite schema definition |
| `src/D365FO.Core/Index/MetadataRepository.cs` | All query and lint methods |
| `src/D365FO.Core/Extract/MetadataExtractor.cs` | AOT walkers + extraction-time flags |
| `src/D365FO.Core/Index/Models.cs` | DTOs for query results |
| `src/D365FO.Core/Scaffolding/` | Scaffolder classes |
| `src/D365FO.Cli/Program.cs` | Command registration |
| `src/D365FO.Cli/Commands/` | All CLI command implementations |
| `src/D365FO.Mcp/ToolCatalog.cs` | MCP tool descriptors |
| `src/D365FO.Mcp/ToolHandlers.cs` | MCP handler methods |
| `skills/_source/` | Skill source files (emitted to `skills/copilot/`) |
