# d365fo-cli — Capabilities & Feature Overview

Full reference of what the tool provides. For setup see [SETUP.md](SETUP.md), for worked examples see [EXAMPLES.md](EXAMPLES.md), for internals see [ARCHITECTURE.md](ARCHITECTURE.md).

---

## Index

The SQLite index (`$D365FO_INDEX_DB`) stores AOT metadata extracted from `PackagesLocalDirectory`. It covers 22 AOT object types and is populated by `d365fo index extract`.

By default the index stores object/method *metadata* only — method bodies (the X++ source) are parsed for lint flags and then discarded; the canonical source stays in the AOT XML on disk. Pass `--index-source` to additionally full-text index method bodies into `MethodSourceFts` (see below).

### Index maintenance commands

| Command | What it does |
|---------|-------------|
| `index build` | Create or migrate the schema in-place |
| `index extract` | Full extraction of all packages |
| `index extract --model <M>` | Scoped extraction of one model (seconds) |
| `index extract --index-source` | Also full-text index X++ method bodies (opt-in; enlarges the DB, accelerates `find refs`). Also available on `index refresh`. |
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

### `security`

Security hierarchy, mirroring the MCP `security_info` tool. (`get role|duty|privilege` and `get security` remain as aliases.)

```
security role <Name>           — Role: duties + privileges
security duty <Name>           — Duty: privileges
security privilege <Name>      — Privilege: entry points
security coverage <Object>     — Role → Duty → Privilege routes that reach an object
```

### `form-pattern`

Form-pattern advisor, spec catalog, and structural validator (mirrors the MCP `object_patterns` tool, `domain=form`).

```
form-pattern analyze [--pattern P|--table T|--similar-to Form]  — pattern histogram / advisor
form-pattern spec [Name]        — required structure tree, versions, reference forms
form-pattern validate [File]    — FP001–FP010 structural validation of AxForm XML
```

### `find`

Cross-reference queries.

```
find coc <Class> [--method <M>]       — Chain-of-Command wrappers
find usages <needle> [--kind k,k,…]  — All references by kind
find extensions <target>              — All object extensions
find handlers <object>                — Event subscribers
find relations <table>                — FK relations
find refs <needle>                    — References inside method bodies (FTS5 when `--index-source` was used, else on-demand source scan)
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

All label operations live under the unified `labels` branch (mirrors the MCP
`labels` tool). The older `search label` / `resolve label` / `label *` forms
still work as aliases.

```sh
# Read
d365fo labels search "Customer invoice"
d365fo labels search "..." --fts                        # FTS5 ranked search
d365fo labels info @SYS12345 --language en-us
d365fo labels resolve @SYS12345 --lang en-US,cs

# Write (atomic, preserves BOM + comments)
d365fo labels create Key "Value" --file path/Foo.en-us.label.txt
d365fo labels create Key "New"   --file path/Foo.en-us.label.txt --overwrite
d365fo labels rename OldKey NewKey --file path/Foo.en-us.label.txt
d365fo labels delete Key           --file path/Foo.en-us.label.txt
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

Exposes the same index and scaffolding surface as the CLI over the `ModelContextProtocol` C# SDK via stdio. The tool surface is **consolidated** into **20 discriminator-based tools** (`search`, `get_object_info`, `get_method`, `labels`, `security_info`, `extension_info`, `object_patterns`, `generate_object`, `analyze`, `models`, …) instead of one tool per object type — mirroring the upstream `d365fo-mcp-server` (which sits at 26). A single tool dispatches on a `type` / `objectType` / `mode` / `action` / `domain` / `include` field. See [MIGRATION_FROM_MCP.md](MIGRATION_FROM_MCP.md) for the full old→new mapping.

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

19 instruction files in `skills/copilot/` cover the full X++ authoring and review canon. Deploy to an X++ project with the `Install-D365FoCopilotSkills.ps1` script (see [SETUP.md](SETUP.md)).

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
| `custom-service-authoring` | JSON/SOAP services |

---

## When to use built-in editor tools vs. `d365fo`

> Quick reference for developers and AI agents working in VS 2022 / VS Code.

| Scenario | Built-in editor / terminal tools | `d365fo` CLI |
|---|---|---|
| Read class structure (methods, signatures) | ❌ `get_file` on XML — unreliable schema | ✅ `d365fo get class <Name> --output json` |
| Read a method body (X++ source) | ❌ | ✅ `d365fo read class <Name> --method <M>` |
| Inspect table fields / indexes / relations | ❌ `get_file` on AxTable XML — unreliable | ✅ `d365fo get table <Name> --output json` |
| Inspect several objects at once | ❌ | ✅ `d365fo get batch table:CustTable class:CustTableType --output json` |
| Search for a class / table / method | ❌ `code_search` / `file_search` — can't parse AOT XML schema, returns misleading snippets | ✅ `d365fo search class <query> --output json` |
| Check for existing CoC wrappers | ❌ | ✅ `d365fo find coc <Class>::<method> --output json` |
| Form pattern structure / requirements | ❌ | ✅ `d365fo form-pattern spec <Pattern> --output json` |
| Validate a form against its pattern | ❌ | ✅ `d365fo form-pattern validate <file> --output json` |
| Create a new AOT object (class, table, form…) | ❌ `create_file` — wrong location, wrong XML schema | ✅ `d365fo generate class/table/form … --install-to <Model>` |
| Modify existing AOT XML — targeted method body edit (inside CDATA) | ⚠️ `replace_string_in_file` / `multi_replace_string_in_file` — allowed for method bodies only; run `d365fo index refresh` after | ✅ `d365fo generate … --overwrite` for full-file replace |
| Modify existing AOT XML — structural change (add field, index, relation…) | ❌ `replace_string_in_file` — corrupts XML structure | ✅ `d365fo generate extension … --overwrite` or VS AOT |
| Search for a label | ❌ | ✅ `d365fo labels search "<text>" --output json` |
| Resolve a label key | ❌ | ✅ `d365fo labels resolve @SYS12345 --lang en-us,cs` |
| Trace security (Role → Duty → Privilege) | ❌ | ✅ `d365fo security coverage <Role> --type Role --output json` |
| Run best-practice check | ❌ | ✅ `d365fo validate xpp <file>` (offline) or `d365fo bp check` (Windows VM, on user request) |
| Inspect model dependencies | ❌ | ✅ `d365fo models deps <Name> --output json` |
| Build / compile — check errors across workspace | ⚠️ `run_build` — on explicit user request only | ✅ `d365fo build` — **on explicit user request only** |
| Get compilation errors for a specific file (fast) | ✅ `get_errors` — per-file, no full build needed | ➖ not available |
| Navigate workspace structure (projects, file lists) | ✅ `get_projects_in_solution`, `get_files_in_project` | ➖ not needed |
| Read / edit non-AOT files (PS scripts, docs, JSON config) | ✅ `get_file`, `replace_string_in_file`, `multi_replace_string_in_file` | ➖ not needed |
| Git operations (commit, diff, branch) | ✅ `run_command_in_terminal` — `git …` | ➖ not needed |
| Refresh index after editing XML | ❌ | ✅ `d365fo index refresh --model <Model>` |
| Verify index health | ❌ | ✅ `d365fo doctor --output json` + `d365fo index status --output json` |

**One-line rule:** if the file ends in `.xml` and is an AOT object → always `d365fo`. Everything else (config, scripts, docs) → standard editor tools.

> ⛔ **When `d365fo` returns `ok: false`** — report the error to the user and stop. Metadata read from open XML files does **not** substitute for the CLI. Never fall back to PowerShell / Python scripts to write AOT XML: spawned processes hang forever in VS 2022 (no interactive terminal).

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
