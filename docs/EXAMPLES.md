# Examples

One worked example per command. Setup lives in [SETUP.md](SETUP.md). Every example assumes `d365fo` is on `PATH` and the index is populated.

Output: `--output json` returns `{ "ok": true, "data": {…} }` or `{ "ok": false, "error": { "code": "…", "message": "…", "hint": "…" } }` on every command.

---

## Discover

### `search` — fuzzy-find AOT objects

```sh
d365fo search class Cust
```

Same pattern for `search table|edt|enum|form|query|view|entity|report|service|workflow|label`.

### `get` — full metadata for one object

```sh
d365fo get table CustTable
d365fo get table CustTable --resolve-labels    # rewrite @File:Id tokens to text
```

Supports `get class|edt|enum|form|menu-item|security|label|role|duty|privilege|query|view|entity|report|service|service-group`. Unknown names return a `*_NOT_FOUND` envelope with a `hint: "Did you mean: …"`.

### `find` — trace cross-references

```sh
d365fo find coc CustTable::validateWrite
```

Also available: `find relations|usages|extensions|handlers`. `find refs --xref` queries `DYNAMICSXREFDB` for path/line/column/kind.

#### `find form-patterns`

```sh
d365fo find form-patterns --output json               # full histogram
d365fo find form-patterns --pattern SimpleList         # all SimpleList forms
d365fo find form-patterns --table CustTable            # forms using CustTable
d365fo find form-patterns --similar-to CustGroup       # same pattern + datasource
```

Use before `generate form` to confirm the right pattern.

### `resolve label` — look up a label token

```sh
d365fo resolve label @SYS12345 --lang en-US,cs
```

### `read` — pull X++ source from AOT XML

```sh
d365fo read class CustTable_Extension --method validateWriteExt
```

`read table` and `read form` work the same way; add `--lines 10-40` or `--declaration` to scope the snippet.

### `models` — inspect indexed models

```sh
d365fo models list
d365fo models deps ApplicationSuite
d365fo models coupling --top 10
d365fo models coupling --only-cycles
```

`models list` enumerates every model with publisher, layer, and custom-flag. `models coupling` runs Tarjan SCC over `ModelDependencies` to surface dependency cycles and ranks every model by fan-in / fan-out / instability (`I = Ce / (Ca + Ce)`, where 0 = most stable, 1 = most volatile).

### `search any` — scope-agnostic quick jump

```sh
d365fo search any CustTable
```

UNIONs every indexed kind in one query and returns `byKind` counts for triage.

### `stats` — per-model + top-N aggregates

```sh
d365fo stats --top 10
```

Returns per-model object counts plus top tables (by field count), top classes (by method count), and top CoC extension targets. Handy for sizing a customisation and for agent prompts.

---

## Maintain the index

### `index refresh` — incremental re-extract

```sh
d365fo index refresh
d365fo index refresh --force          # re-scan every model
d365fo index extract --since 2026-04-01T00:00:00Z  # explicit threshold
d365fo index history --limit 20       # last 20 per-model extract runs
d365fo index history --model Contoso  # filter to one model
```

`index refresh` compares each model's content fingerprint against the DB row and re-extracts only changed models. `--force` ignores all thresholds.

### `lint` — in-process Best-Practice heuristics

```sh
d365fo lint
d365fo lint --category table-no-index,string-without-edt --all-models
d365fo lint --category today-usage,do-insert-update,doc-comment-missing
d365fo lint --format sarif > lint.sarif
```

Six categories shipped:

| Category | What it finds |
|---|---|
| `table-no-index` | Tables without cluster/alternate index |
| `ext-named-not-attributed` | `*_Extension` classes missing `[ExtensionOf]` |
| `string-without-edt` | String fields without an EDT |
| `today-usage` | `today()` calls (BP `BPUpgradeCodeToday`) |
| `do-insert-update` | `doInsert()` / `doUpdate()` / `doDelete()` calls |
| `doc-comment-missing` | Methods without `/// <summary>` |

Defaults to custom models only; `--all-models` includes ISV/MS content. `--format sarif` emits SARIF 2.1.0 for CI (GitHub code-scanning, Azure DevOps).

### `validate name` — naming-rule linter

```sh
d365fo validate name Table FmVehicle --prefix Fm
d365fo validate name Coc CustTable_Extension
```

Static naming-rule check (publisher prefix, PascalCase, suffix conventions). Returns structured `violations[]` with `code`, `severity`, `message`. Useful as a pre-commit hook.

### `init` — quickstart

```sh
d365fo init --run-extract
d365fo init --dry-run                  # show what would be done
d365fo init --persist-profile          # append env vars to $PROFILE / ~/.profile
```

Auto-detects the Windows `PackagesLocalDirectory`, prepares the SQLite schema, and (with `--run-extract`) drives the full extract pipeline. `--persist-profile` writes `D365FO_PACKAGES_PATH` / `D365FO_INDEX_DB` to the user's shell profile.

---

## Scaffold

`generate` writes atomically (`.tmp` + move) and keeps a `.bak` when `--overwrite` is used. Pass `--install-to <Model>` to drop the artefact straight into a model folder via the bridge (requires `D365FO_BRIDGE_ENABLED=1`, `D365FO_PACKAGES_PATH`, `D365FO_BIN_PATH`).

### Table

```sh
# Pattern-driven (P1) — emits canonical TableGroup, default fields, alt-key index
d365fo generate table FmCustomer \
  --pattern master \
  --label "@Fleet:Customer" \
  --install-to FleetManagement

# Header / lines pair (worksheet pattern)
d365fo generate table FmOrderHeader --pattern worksheet-header --install-to FleetManagement
d365fo generate table FmOrderLine   --pattern worksheet-line   --install-to FleetManagement

# Temp table — TempDB is a TableType, not a TableGroup
d365fo generate table FmTmpStaging \
  --pattern main --table-type TempDB \
  --install-to FleetManagement

# Hand-picked fields override pattern defaults
d365fo generate table FmVehicle \
  --label "@Fleet:Vehicle" \
  --field VIN:VinEdt:mandatory \
  --field Make:Name \
  --primary-key VIN \
  --out src/MyModel/AxTable/FmVehicle.xml
```

Patterns: `main`/`master`, `transaction`, `parameter`/`setup`/`config`, `group`, `worksheetheader`/`header`, `worksheetline`/`line`, `reference`/`lookup`, `framework`, `miscellaneous`. Temp tables: `--table-type TempDB --pattern main`.

### Class

```sh
d365fo generate class FmVehicleService --extends RunBase \
  --out src/MyModel/AxClass/FmVehicleService.xml
```

### Chain-of-Command extension

```sh
d365fo generate coc CustTable --method update --method insert \
  --out src/MyModel/AxClass/CustTable_Extension.xml
```

### Simple-list form

```sh
d365fo generate simple-list FmVehicleListPage --table FmVehicle \
  --out src/MyModel/AxForm/FmVehicleListPage.xml
```

> **Deprecated alias.** Use `generate form --pattern SimpleList` instead.

### Form (any of nine D365FO patterns)

```sh
d365fo generate form FmVehicleDetails \
  --pattern DetailsMaster \
  --table FmVehicle \
  --field VIN --field Make --field Model \
  --caption "@Fleet:Vehicles" \
  --out src/MyModel/AxForm/FmVehicleDetails.xml
```

| Pattern | Use case |
|---|---|
| `SimpleList` | Setup / config tables |
| `SimpleListDetails` | List + detail panel |
| `DetailsMaster` | Full master record (CustTable shape) |
| `DetailsTransaction` | Header + lines — add `--lines-table <TABLE>` |
| `Dialog` | Modal popup |
| `TableOfContents` | Tabbed settings page |
| `Lookup` | Dropdown lookup |
| `ListPage` | Navigation list |
| `Workspace` | KPI tiles + panorama |

`--field` (repeatable) — grid/detail columns. `--section Name:Caption` — tabs / panorama sections. `--lines-table <TABLE>` — secondary datasource for `DetailsTransaction`.

### Data entity (`AxDataEntityView`)

```sh
d365fo generate entity FmVehicleEntity --table FmVehicle \
  --public-entity-name FmVehicle --public-collection-name FmVehicles \
  --all-fields \
  --out src/MyModel/AxDataEntityView/FmVehicleEntity.xml
```

Emits a single-datasource `AxDataEntityView`. `--all-fields` auto-populates fields from the source table (mandatory flag carries over). Without it `<Fields />` is empty.

### Extension (table / form / EDT / enum)

```sh
d365fo generate extension Table CustTable Contoso \
  --out src/MyModel/AxTableExtension/CustTable.Contoso.xml
```

Name is always `<Target>.<Suffix>` to match the AOT convention. Kinds: `Table`, `Form`, `Edt`, `Enum`.

### Event handler

```sh
d365fo generate event-handler Contoso_CustTable_Handler \
  --source-kind Table --source-object CustTable --event inserted \
  --out src/MyModel/AxClass/Contoso_CustTable_Handler.xml
```

Picks the right attribute (`[DataEventHandler]`, `[FormEventHandler]`, `[FormDataSourceEventHandler]`, `[SubscribesTo]`) from `--source-kind`.

### Security privilege / duty

```sh
d365fo generate privilege FmVehicleReadPriv \
  --entry-point FmVehicleListPage --entry-kind MenuItemDisplay --entry-object FmVehicleListPage \
  --access Read --label "@Fleet:ReadVehicles" \
  --out src/MyModel/AxSecurityPrivilege/FmVehicleReadPriv.xml

d365fo generate duty FmVehicleMaintainDuty \
  --privilege FmVehicleReadPriv --privilege FmVehicleUpdatePriv \
  --out src/MyModel/AxSecurityDuty/FmVehicleMaintainDuty.xml

# Scaffold-and-wire in one pass: emit the duty/privilege XML AND merge its
# name into an existing role's <Duties> / <Privileges>.
d365fo generate duty FmReportingDuty --privilege FmReportsViewPriv \
  --out src/MyModel/AxSecurityDuty/FmReportingDuty.xml \
  --into-role src/MyModel/AxSecurityRole/FmVehicleAdminRole.xml
```

Both `generate privilege` and `generate duty` accept `--into-role <PATH>`; after the scaffold file is written, the role XML is updated idempotently with a `.bak` sibling (same merge path as `generate role --add-to`).

### Security role (new or merge)

```sh
# Scaffold a new role that references duties / privileges
d365fo generate role FmVehicleAdminRole \
  --duty FmVehicleMaintainDuty --privilege FmVehicleReadPriv \
  --label "@Fleet:VehicleAdminRole" --description "Full access to Fleet vehicles" \
  --out src/MyModel/AxSecurityRole/FmVehicleAdminRole.xml

# Merge new references into an existing role (idempotent; writes .bak)
d365fo generate role --add-to src/MyModel/AxSecurityRole/FmVehicleAdminRole.xml \
  --duty FmReportingDuty --privilege FmExportPriv
```

`--add-to` validates the root element (`AxSecurityRole`), dedupes by `Name` (case-insensitive), and returns `NoChange` when every reference already exists.

### Report (`AxReport` + DP class + data contract)

```sh
# Minimal — one dataset, no columns specified (tablix shell only)
d365fo generate report FmVehicleReport \
  --dp FmVehicleReportDP \
  --tmp FmVehicleReportTmp \
  --dataset FmVehicleDS \
  --caption "@Fleet:VehicleReport" \
  --out src/MyModel/AxReport/FmVehicleReport.xml

# With tablix column definitions — generates header row + data row in the tablix
d365fo generate report FmVehicleReport \
  --dp FmVehicleReportDP --tmp FmVehicleReportTmp \
  --field VIN --field Make --field Model --field Year \
  --caption "@Fleet:VehicleReport" \
  --out src/MyModel/AxReport/FmVehicleReport.xml

# With report parameters — auto-generates a DataContract class (FmVehicleReportDPContract.xml)
d365fo generate report FmVehicleReport \
  --dp FmVehicleReportDP \
  --field VIN --field Make --field Year \
  --parameter FromDate:DateTime --parameter ToDate:DateTime --parameter Customer \
  --out src/MyModel/AxReport/FmVehicleReport.xml

# Multi-dataset — primary dataset + additional via --extra-dataset
d365fo generate report FmFleetSummaryReport \
  --dp FmFleetSummaryReportDP \
  --field VehicleCount --field TotalMileage \
  --extra-dataset FmFleetCostsDS:FmFleetCostsDP \
  --extra-dataset FmFleetIncidentsDS:FmFleetIncidentsDP \
  --parameter PeriodYear:Integer \
  --out src/MyModel/AxReport/FmFleetSummaryReport.xml
```

The command atomically writes **two or three XML files**: `AxReport` (datasets + auto-design + tablix), DP class extending `SrsReportDataProviderBase`, and (when `--parameter` is used) a DataContract class extending `SrsReportDataContractBase`. Output overrides: `--out-dp`, `--out-contract`.

---

## Suggest / Analyze

### `suggest extension` — extensibility strategy advisor

```sh
# Auto-detect object kind and rank extension strategies
d365fo suggest extension CustTable --output json

# Hint the kind explicitly when needed
d365fo suggest extension SalesFormLetterService --kind Class --output json
d365fo suggest extension SalesTable --kind Table  --output json
d365fo suggest extension CustTable  --kind Form   --output json
```

Ranks extension strategies (`CoC`, `EventHandler`, `Extension`) by confidence based on what already exists in the codebase. Use before scaffolding.

### `analyze completeness` — cross-check project against index

```sh
# Analyse a full model folder (walks all *.xml recursively)
d365fo analyze completeness src/MyModel --output json

# Analyse a single AOT XML file
d365fo analyze completeness src/MyModel/AxSecurityRole/FmAdminRole.xml

# Skip slow label checks in CI (focus on structural refs only)
d365fo analyze completeness src/MyModel --skip-labels --output json

# Pipe into jq to count issues by code
d365fo analyze completeness src/MyModel --output json \
  | jq '.data.issues | group_by(.code) | map({code: .[0].code, count: length})'
```

Reports `MISSING_DUTY`, `MISSING_PRIVILEGE`, `MISSING_EDT`, `MISSING_LABEL`, `PARSE_ERROR`. Flags: `--skip-labels`, `--skip-edts`, `--skip-security`.

---

## Labels (read & write)

```sh
# Read
d365fo search label "Customer invoice"
d365fo search label "customer invoice" --fts        # rank-sorted FTS5
d365fo get label @SYS12345 --language en-us

# Write \u2014 atomic, preserves comments, BOM UTF-8
d365fo label create NewKey "New value" --file path/Foo.en-us.label.txt
d365fo label create NewKey "Updated"   --file path/Foo.en-us.label.txt --overwrite
d365fo label rename NewKey RenamedKey  --file path/Foo.en-us.label.txt
d365fo label delete RenamedKey         --file path/Foo.en-us.label.txt
```

`search label --fts` requires FTS5 (falls back to `LIKE` if unavailable). Write commands keep a `.bak` of the previous file.

---

## Review

```sh
d365fo review diff --base HEAD
```

Compare two revs with `--base main --head feature/my-branch`. Rules shipped today:

- `FIELD_WITHOUT_EDT` — table field without `<ExtendedDataType>`.
- `FIELD_WITHOUT_LABEL` — user-facing field without `<Label>`.
- `HARDCODED_STRING` — verbatim string literal in X++ source.
- `DYNAMIC_QUERY` — dynamic `Query` construction (flag for security review).

---

## Windows-only ops (D365FO VM)

These commands wrap the Microsoft tooling Visual Studio uses, so you can drive the IDE's workflow from a terminal, script, or CI pipeline.

```powershell
d365fo build --project C:\AosService\PackagesLocalDirectory\MyModel\MyModel.rnrproj
d365fo sync --full
d365fo test run --suite MyModel.Tests
d365fo bp check --model MyModel
```

Each parses the tool output and returns a structured JSON envelope (errors, warnings, elapsed time, tail of stdout). On non-Windows they return `UNSUPPORTED_PLATFORM`.

---

## Agent integration

### Emit the system prompt

```sh
d365fo agent-prompt --out .prompts/d365fo.md
```

`d365fo schema --full` emits a machine-readable catalog of every command.

### GitHub Copilot (VS Code / Visual Studio)

```sh
cp skills/copilot/* .github/instructions/
d365fo agent-prompt --out .github/copilot-instructions.md
```

Copilot picks up `.github/instructions/*.instructions.md` via `applyTo` globs and drives `d365fo` through its terminal tool.

### Claude Code / Claude Desktop

Drop `skills/anthropic/` into the project or `~/.claude/skills/`. Each `SKILL.md` triggers via its `applies_when` front-matter.

### Codex CLI / Gemini CLI

Paste the output of `d365fo agent-prompt` into the session system prompt, or reference it from `AGENTS.md`.

Use the compact manifest when the agent needs to discover the CLI surface:

```sh
d365fo schema
```

Use the complete manifest only when needed:

```sh
d365fo schema --full
```

CLI-first shortcuts replace common MCP multi-call workflows:

```sh
d365fo search batch CustTable SalesTable CustAccount --output json
d365fo get object table CustTable --output json
d365fo find related coc CustTable --method validateWrite --output json
```

### MCP server (`d365fo-mcp`)

Standalone JSON-RPC 2.0 server (protocol `2024-11-05`) that shares the CLI's index. Config sample for Claude Desktop:

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

After `dotnet publish src/D365FO.Mcp -c Release -r osx-arm64` you get a standalone `d365fo-mcp` binary you can drop on `$PATH`. The adapter exposes **55 tools** covering CLI parity (search / get / find / read / index_status), security & labels, heuristics, and aggregation — same index, same guardrails.

---

---

## Daemon (warm cache)

For latency-sensitive integrations, run the CLI as a daemon so the SQLite handle and read caches stay hot:

```sh
d365fo daemon start                                  # named pipe / Unix socket + file watcher
d365fo daemon start --packages J:\AosService\PackagesLocalDirectory
d365fo daemon start --no-watch                      # disable auto index refresh
d365fo daemon start --watch-debounce 5000           # wait 5 s after last change before refresh
d365fo daemon status
d365fo daemon stop
```

Transport: Windows named pipe `\\\\.\\ pipe\\d365fo-cli`; Unix socket at `$XDG_RUNTIME_DIR/d365fo-cli.sock` (fallback `$TMPDIR`). The frame format matches `d365fo-mcp`: one newline-terminated JSON-RPC request per connection, one response, close.

The daemon also starts a `FileSystemWatcher` over `D365FO_PACKAGES_PATH` (or `--packages`). When `*.xml` files change, it debounces (default 3 s) and automatically triggers an incremental `index refresh` for the affected model, emitting a JSON notification to stderr:

```json
{ "event": "index_refreshed", "model": "Contoso" }
```

Pass `--no-watch` to disable the watcher (e.g. read-only environments or network shares).

---

## CI / automation

Every command is scriptable: exit codes are reliable, output is JSON by default in non-TTY, no interactive prompts.

```yaml
- name: D365 review
  run: |
    d365fo index build
    d365fo review diff --base origin/main --head HEAD --output json \
      | jq -e '.data.violationCount == 0'
```
