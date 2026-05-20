# Plan: d365fo-cli Comprehensive Audit & Enhancement — v2

## TL;DR

Full audit of the d365fo-cli solution (post Phase 1 + Phase 2) covering AOT coverage
gaps, BP lint expansion (aligned to the official CAR rule set), integration-pattern
commands and skills, second scaffolding wave, developer-experience polish, MCP parity,
documentation refresh, and test coverage. Research grounded in Microsoft Learn
documentation, the D365FO Customization Analysis Report (CAR), and full code inventory.

---

## Status Legend

| Symbol | Meaning |
|--------|---------|
| ✅ | Implemented and merged |
| 🔲 | Planned, not started |
| 🚧 | In progress |

---

## Phase 1: Index & Performance Optimizations ✅

### 1.1 Parallel model extraction ✅
### 1.2 Index VACUUM / ANALYZE (`index optimize`) ✅
### 1.3 Incremental FTS5 updates ✅
### 1.4 Index export/import (`index export` / `index import`) ✅
### 1.5 Daemon warm-up (`daemon warmup`) ✅

---

## Phase 2: Missing Scaffolding Commands ✅

### 2.1 `generate sysoperation` ✅
### 2.2 `generate number-sequence` ✅
### 2.3 `generate workflow` ✅
### 2.4 `generate menu-item` ✅
### 2.5 `generate edt` ✅
### 2.6 `generate enum` ✅
### 2.7 `generate query` ✅

---

## Phase 3: AOT Coverage Gaps ✅

The index covers ~20 AOT object types. The full D365FO Application Object Tree has
additional types that are either not indexed or only partially surfaced.

### 3.1 Business Events (`AxBusinessEvent`) ✅

Business events are a first-class extensibility mechanism (subscribers via Power
Automate, Azure Service Bus, Event Grid, Logic Apps). Currently not in the index.

**What to extract:**
- Detect classes extending `BusinessEventsBase` in `AxClass` sources
- Extract `[BusinessEvents(classStr(X), classStr(Contract), "Category", "Description")]`
  attribute arguments for catalogue metadata
- New schema table `BusinessEvents(Id, Name, Category, ContractClass, ModelId, SourcePath)`

**New index commands:**
- `search business-event <query>` — search by name/category
- `get business-event <name>` — show contract class + attributes
- `find business-events --category <cat>` — filter by module category

**Files:**
- `src/D365FO.Core/Index/Schema.sql` — new `BusinessEvents` table (schema v11)
- `src/D365FO.Core/Extract/MetadataExtractor.cs` — detect in `AxClass` walk
- `src/D365FO.Core/Index/MetadataRepository.cs` — `SearchBusinessEvents`, `GetBusinessEvent`
- `src/D365FO.Cli/Commands/Search/SearchCommands.cs` — `SearchBusinessEventCommand`
- `src/D365FO.Cli/Commands/Get/GetCommands.cs` — `GetBusinessEventCommand`

**Reference:** https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/business-events/home-page

### 3.2 Security Policies / Extensible Data Security (XDS) ✅

XDS policies restrict what rows a user can read/write. Missing entirely.

**What to extract:**
- Walk `AxSecurityPolicy` directories
- Extract: `Name`, `ConstrainedTable`, `PolicyQuery`, `OperationType` (All/Select/Insert/Update/Delete), `ContextType` (ContextString/RoleName), `IsEnabled`, `IsMandatory`
- New schema table `SecurityPolicies(...)`

**New commands:**
- `search security-policy <query>`
- `get security-policy <name>`

**Files:**
- `src/D365FO.Core/Index/Schema.sql` — `SecurityPolicies` table
- `src/D365FO.Core/Extract/MetadataExtractor.cs` — `AxSecurityPolicy` walker
- `src/D365FO.Core/Index/MetadataRepository.cs` — `SearchSecurityPolicies`, `GetSecurityPolicy`
- `src/D365FO.Cli/Commands/Search/SearchCommands.cs`, `GetCommands.cs`

### 3.3 Configuration Keys (`AxConfigurationKey`) ✅

Configuration keys gate feature availability; important for completeness analysis.

**What to extract:**
- Walk `AxConfigurationKey` directories
- Extract: `Name`, `Label`, `IsEnabled`, `ParentKey`, `LicenseCode`, `ModelId`
- New schema table `ConfigurationKeys(...)`

**Files:**
- `src/D365FO.Core/Index/Schema.sql` — `ConfigurationKeys` table
- `src/D365FO.Core/Extract/MetadataExtractor.cs` — `AxConfigurationKey` walker
- `src/D365FO.Core/Index/MetadataRepository.cs` — `SearchConfigurationKeys`
- `src/D365FO.Cli/Commands/Search/SearchCommands.cs`

### 3.4 Tiles and Workspace Descriptors ✅

Tiles and workspaces are visible in the navigation experience. Low query frequency
but useful for impact analysis.

**What to extract:**
- Walk `AxTile` — `Name`, `MenuItemName`, `MenuItemType`, `Label`, `TileType`, `ModelId`
- Walk `AxWorkspace` (layout descriptor, not AxForm) — `Name`, `Label`

**Files:** Same pattern — schema tables + extractor walk + search commands.

### 3.5 Table AOT Properties Gap-fill ✅

Currently extracted table properties are missing several columns important for lint:

| Property | Schema Column | Needed For |
|----------|--------------|------------|
| `SaveDataPerCompany` | Missing | crossCompany lint, entity analysis |
| `CacheLookup` | Missing | `BPCheckTablePropertyMismatch` lint rule |
| `OccEnabled` | Missing | forUpdate lint |
| `ValidTimeStateFieldType` | Missing | validTimeState select hint |
| `SupportInheritance` / `Extends` (table) | Missing | inheritance chain lint |
| `AOSAuthorization` | Missing | security analysis |
| `FormRef` / `ListPageRef` | Missing | navigation completeness |
| `SystemTable` | Missing | filter platform objects |

**Files:**
- `src/D365FO.Core/Index/Schema.sql` — `ALTER TABLE Tables ADD COLUMN` for each
- `src/D365FO.Core/Extract/MetadataExtractor.cs` — read new XML elements
- `src/D365FO.Core/Index/Models.cs` — extend `TableDetails`
- `src/D365FO.Core/Index/MetadataRepository.cs` — extend `GetTableDetails` projection

### 3.6 EDT Properties Gap-fill ✅

| Property | Schema Column | Needed For |
|----------|--------------|------------|
| `ReferenceTable` | Missing | implicit FK detection |
| `FormHelp` | Missing | completeness check |
| `AnalysisUsage` | Missing | BI/aggregate dimension analysis |
| `EnumType` | Missing | EDT-backed enums |

**Files:** Same pattern as 3.5.

### 3.7 `search any` gap: Maps not included ✅

`SearchMaps` exists in `MetadataRepository` but `search any` and `search batch` do
not include maps in the union. Add `Maps` to the multi-kind union query.

**File:** `src/D365FO.Core/Index/MetadataRepository.cs` — extend `SearchAny` UNION.

---

## Phase 4: Lint & Static Analysis Expansion 🔲

Current lint has 6 rules. The official D365FO Customization Analysis Report (CAR)
defines ~25 certified rules. Closing the gap is the highest-value quality gate
improvement. See:
https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/dev-tools/customization-analysis-report

### Extraction-time flags (added to `Methods` / `TableMethods`)

Several rules require new boolean flags set during extraction (like `HasTodayCall`
already is). Add at extract time:

| Flag | Column | Detection |
|------|--------|-----------|
| `HasInsertInLoop` | `Methods.HasInsertInLoop` | `.insert()` call in method source inside a loop construct |
| `HasNestedSelect` | `Methods.HasNestedSelect` | `while select` inside another `while select` |
| `HasForceLiterals` | `Methods.HasForceLiterals` | `forceLiterals` keyword in select |
| `HasForUpdateWithoutUpdate` | `Methods.HasForUpdateWithoutUpdate` | `forUpdate` select with no `.update()`/`.delete()` call |
| `HasTryCatchInTts` | `Methods.HasTryCatchInTts` | `try` block inside `ttsbegin`/`ttscommit` without catching `UpdateConflict` |
| `HasEmptyTableMethodOverride` | `TableMethods.IsEmptyOverride` | Table method override with empty body |
| `HasEmptyLoop` | `Methods.HasEmptyLoop` | Loop body with no statements |
| `IsRunBaseBatch` | `Classes.IsRunBaseBatch` | `extends RunBaseBatch` in declaration |
| `HasCanGoBatch` | `Classes.HasCanGoBatch` | `canGoBatch` method returning `true` |

**File:** `src/D365FO.Core/Extract/MetadataExtractor.cs` — extend source scanner

### 4.1 `BPCheckNestedLoopInCode` 🔲

Flag methods where `while select` appears nested inside another loop. Already
identified as high-priority; the `HasNestedSelect` flag enables this at query time.

- `src/D365FO.Core/Index/MetadataRepository.cs` — new `FindNestedSelectMethods()`
- `src/D365FO.Cli/Commands/Lint/LintCommand.cs` — new rule category `nested-select`

### 4.2 `BPCheckInsertMethodInLoop` 🔲

Flag methods calling `.insert()` in a loop body. Should suggest `RecordInsertList`.

- New flag `HasInsertInLoop`; query in `FindInsertInLoopMethods()`

### 4.3 `BPCheckNoTTSTryBlock` 🔲

Flag methods with `try` blocks inside `ttsbegin/ttscommit` that do not catch
`UpdateConflict` or `UpdateConflictNotRecovered`.

- New flag `HasTryCatchInTts`; query in `FindTtsTryCatchMethods()`

### 4.4 `BPCheckEmptyTableMethod` 🔲

Table method overrides with empty bodies force the DB engine to fall back from
set-based to row-by-row operations — major performance issue.

- New flag `IsEmptyOverride` on `TableMethods`; query in `FindEmptyTableMethodOverrides()`

### 4.5 `BPCheckBatchJobsEnabled` 🔲

Classes extending `RunBaseBatch` must override `canGoBatch()` returning `true`.

- New class flags `IsRunBaseBatch`, `HasCanGoBatch`; query in `FindRunBaseBatchWithoutCanGoBatch()`

### 4.6 `BPCheckForceLiterals` (new; not in CAR but security-critical) 🔲

`forceLiterals` in a select statement disables parameter binding, creating SQL
injection risk in multi-tenant environments.

- New flag `HasForceLiterals`; rule severity: **error**

### 4.7 `BPCheckTablePropertyMismatch` — CacheLookup vs TableGroup 🔲

Metadata rule: certain TableGroup values require specific CacheLookup settings.
Valid pairs from Microsoft documentation:

| TableGroup | Required CacheLookup |
|-----------|---------------------|
| `Parameter` | `Found` or `FoundAndEmpty` |
| `Group` / `Reference` | `Found`, `FoundAndEmpty`, or `EntireTable` |
| `Main` | `Found`, `FoundAndEmpty`, or `None` |
| `Miscellaneous` (with &lt;500 rows) | `EntireTable` |

Requires Phase 3.5 (`CacheLookup` and `TableGroup` columns in `Tables`).

- `src/D365FO.Core/Index/MetadataRepository.cs` — `FindCacheLookupMismatches()`
- Rule category `cache-lookup-mismatch`, severity: **warning**

### 4.8 `BPCheckMissingDeleteActions` 🔲

Table relations without a configured `DeleteAction` or `OnDelete` property can lead
to orphaned records. Exclude: TempDB, InMemory, Reference, Staging, and Parameter
tables (they intentionally lack delete actions).

- Query: `Relations LEFT JOIN Tables` where `DeleteAction IS NULL` and table is not excluded
- Rule category `missing-delete-action`, severity: **warning**

### 4.9 `BPCheckAlternateKeyAbsent` (expand existing `table-no-index`) 🔲

The existing `table-no-index` rule flags tables with no index. A separate, more
specific rule should flag tables that have unique indexes but no index with
`AlternateKey = Yes` (required by BP for OData key resolution and DMF).

- Query: tables with at least one `AllowDuplicates = No` index but no `AlternateKey = Yes` index
- Rule category `no-alternate-key`, severity: **warning**

### 4.10 Label Reference Validation (`BPErrorUnknownLabel`) 🔲

Cross-check `@File:Key` references in method source code against the indexed
`Labels` table. References that do not resolve are BP errors at compile time.

- `src/D365FO.Core/Index/MetadataRepository.cs` — regex-scan method sources for `@\w+:\w+` patterns, cross-check against `Labels`
- Rule category `unknown-label-ref`, severity: **error**

### 4.11 `BPCheckAddressModel` + `BPCheckDimensionModel` 🔲

Flag fields using obsolete EDTs:
- Address model: `AddressZipCodeId`, `AddressStateId`, `AddressCountryRegionId` (old address model; new model uses postal address entities)
- Dimension model: `Dimension`, `LedgerAccount` (old financial dimension; new model uses `DimensionAttributeValueCombination`)

Requires knowledge of field EDT names, which are already in `TableFields.EdtName`.

- `src/D365FO.Core/Index/MetadataRepository.cs` — `FindObsoleteEdtUsages()`
- Rule categories `obsolete-address-edt`, `obsolete-dimension-edt`, severity: **warning**

### 4.12 Circular Model Dependency Detection 🔲

Extend `models coupling` to flag true directed cycles in the `ModelDependencies`
graph (Tarjan's SCC already implemented in `CouplingAnalyzer`; just surface as lint).

- `src/D365FO.Cli/Commands/Models/ModelsCommands.cs` — add `--detect-cycles` flag
- `src/D365FO.Core/Analysis/CouplingAnalyzer.cs` — `FindCycles()` already exists; expose result

### 4.13 Public Instance Field Detection 🔲

Flag classes with `public` instance member variables (X++ rule: class state must be
accessed via `parm*()` accessors or declared `protected`/`private`).

Requires class declaration source parsing to detect `public <type> <field>;` patterns.

- `src/D365FO.Core/Extract/MetadataExtractor.cs` — scan declaration block source
- `src/D365FO.Core/Index/Schema.sql` — `Classes.HasPublicInstanceFields` flag
- Rule category `public-instance-field`, severity: **warning**

---

## Phase 5: Integration Patterns — Commands & Skills 🔲

Based on the D365FO integration overview at:
https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/data-entities/integration-overview

### 5.1 `analyze integration` 🔲

Cross-check all indexed data entities for integration readiness:
- `EnablePublicAPI` present → OData-exposed; check: mandatory fields mapped, key fields defined, `PublicEntityName` unique
- `EnableDataManagementCapabilities` → DMF-capable; check: staging table present
- Missing `StagingTable` on DMF entities
- `IsReadOnly` fields that should be writeable
- Entities with 0 fields mapped
- Duplicate `PublicEntityName` values (BP error)

**File:** `src/D365FO.Cli/Commands/Analyze/AnalyzeIntegrationCommand.cs` (new)

### 5.2 `report integrations` 🔲

Summary report of all integration surface in a model/workspace:
- OData entities (count + list, mandatory-fields coverage %)
- Custom services (count + list, operations)
- Business events (count + list by category)
- Workflow types (count + list)
- Batch jobs (`RunBaseBatch` + `SysOperationServiceController` subclasses)

**File:** `src/D365FO.Cli/Commands/Analyze/ReportIntegrationsCommand.cs` (new)

### 5.3 `get entity --odata-metadata` 🔲

Emit the OData `$metadata` fragment for a data entity: entity type declaration with
fields, navigation properties, key specification. Useful for configuring external
integration systems (Logic Apps, Power Automate, etc.).

**File:** `src/D365FO.Cli/Commands/Get/GetCommands.cs` — extend `GetEntityCommand`
with `--odata-metadata` flag

### 5.4 New skill: `integration-patterns` 🔲

Covers the four D365FO integration patterns with CLI grounding:
- **OData**: entity query, $filter, $expand, custom actions via `generate entity`
- **Custom Services**: service class + group pattern via `generate custom-service` (Phase 6.2)
- **Data Management Framework**: DMF entity requirements, staging table, change tracking
- **Business Events**: custom event class + contract pattern via `generate business-event` (Phase 6.1)

**Files:**
- `skills/_source/integration-patterns.md` (new)
- `skills/copilot/integration-patterns.instructions.md` (auto-generated)

### 5.5 New skill: `business-events-authoring` 🔲

Complete guide to authoring custom business events:
- `BusinessEventsBase` subclass + `BusinessEventsContract` pattern
- `[BusinessEvents(...)]` attribute parameters
- Activation lifecycle (catalog → activate per company → endpoint subscription)
- Testing with Service Bus / Event Grid / Power Automate
- Grounded by `d365fo search business-event` (Phase 3.1)

**Files:**
- `skills/_source/business-events-authoring.md` (new)
- `skills/copilot/business-events-authoring.instructions.md`

**Reference:** https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/business-events/home-page

### 5.6 New skill: `custom-service-authoring` 🔲

Guide for building JSON/SOAP custom services:
- Service class (`[ServiceAttribute]`) + contract class (`[DataContractAttribute]`) + service group (`AxServiceGroup`)
- `[SysEntryPointAttribute(true)]` required on every exposed method
- REST endpoint format and authentication (AAD OAuth2)
- Service group registration in `AxServiceGroup`
- Consuming from Power Platform / Logic Apps

**Files:**
- `skills/_source/custom-service-authoring.md` (new)

**Reference:** https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/dev-ref/xpp-attribute-classes

---

## Phase 6: Scaffolding Wave 2 🔲

Second round of scaffold commands covering integration objects, legacy patterns,
and operational scenarios.

### 6.1 `generate business-event` 🔲

Scaffold a custom business event class extending `BusinessEventsBase` plus its
companion `BusinessEventsContract`.

**Options:** `--contract-name`, `--payload <name>:<type>`, `--category`, `--module`,
`--out-contract`

**Files:**
- `src/D365FO.Core/Scaffolding/BusinessEventScaffolder.cs` (new)
- `src/D365FO.Cli/Commands/Generate/GenerateBusinessEventCommand.cs` (new)

### 6.2 `generate custom-service` 🔲

Scaffold an `AxService`, its service class, the companion data contract, and an
`AxServiceGroup` XML.

**Options:** `--service-class`, `--group-name`, `--operation <name>:<returnType>`,
`--contract-param <name>:<type>`, `--out-class`, `--out-service`, `--out-group`

**Files:**
- `src/D365FO.Core/Scaffolding/CustomServiceScaffolder.cs` (new)
- `src/D365FO.Cli/Commands/Generate/GenerateCustomServiceCommand.cs` (new)

### 6.3 `generate migration-script` 🔲

Scaffold a data-migration `Runnable` class with the proper `doInsert`/`doUpdate`
pattern (the one documented exception where `do*` methods ARE appropriate), correct
transaction batching with a configurable batch size, and a progress log.

**Options:** `--source-table`, `--target-table`, `--batch-size <N>` (default 1000),
`--mode Insert|Update|Upsert`

**Files:**
- `src/D365FO.Core/Scaffolding/MigrationScriptScaffolder.cs` (new)
- `src/D365FO.Cli/Commands/Generate/GenerateMigrationScriptCommand.cs` (new)

### 6.4 `generate runbase` 🔲

Scaffold a legacy `RunBase`/`RunBaseBatch` class for teams maintaining older
codebases that cannot yet migrate to SysOperation. Includes `pack()`/`unpack()`,
`dialog()`, `getFromDialog()`, `canGoBatch() { return true; }`.

**Options:** `--batch` (include `canGoBatch()`), `--dialog-param <name>:<edt>`,
`--out`

**Files:**
- `src/D365FO.Core/Scaffolding/RunBaseScaffolder.cs` (new)
- `src/D365FO.Cli/Commands/Generate/GenerateRunBaseCommand.cs` (new)

### 6.5 `generate security-policy` 🔲

Scaffold an `AxSecurityPolicy` (XDS policy) with a policy query and constrained table.

**Options:** `--constrained-table`, `--policy-query`, `--operation All|Select`,
`--context-type RoleName|ContextString`, `--context-value`

**Files:**
- `src/D365FO.Core/Scaffolding/SecurityPolicyScaffolder.cs` (new)
- `src/D365FO.Cli/Commands/Generate/GenerateSecurityPolicyCommand.cs` (new)

---

## Phase 7: Developer Experience & Tooling 🔲

### 7.1 `d365fo impact <object>` 🔲

Change-impact analysis: given an AOT object name, produce a ranked list of all
downstream consumers that would be affected by a modification:
- CoC wrappers (`find coc`)
- Event handlers (`find handlers`)
- Object extensions (`find extensions`)
- Cross-references in method source (`find refs`)
- Forms that use the table as a datasource
- Data entities backed by the table
- Queries that join the table
- Reports that reference the object

Returns a single structured JSON report with severity tiers (Direct / Indirect).

**File:** `src/D365FO.Cli/Commands/Analyze/AnalyzeImpactCommand.cs` (new)

### 7.2 `d365fo compare <name> --base <gitref>` 🔲

Structured comparison of an AOT object between two git revisions. Unlike `git diff`,
this reports semantically: "added field X", "changed method signature Y", "removed
index Z". Builds on `ReviewDiffCommand` logic.

**File:** `src/D365FO.Cli/Commands/Review/ReviewDiffCommand.cs` — extend with
`--base <gitref>` + `--name <object>` targeted mode

### 7.3 Shell completion scripts 🔲

Generate tab-completion scripts for PowerShell, bash, and zsh via
`d365fo completion <shell>`. Spectre.Console.Cli has built-in completion support.

**File:** `src/D365FO.Cli/Program.cs` — enable `cfg.Settings.ExceptionHandler` +
`cfg.Settings.Registrar` completion wiring

### 7.4 Performance counters (telemetry-free) 🔲

Track per-command execution time in a `CommandTimings` SQLite table.
Surface via `d365fo stats --perf` for self-diagnosis without external telemetry.

- `src/D365FO.Core/Index/Schema.sql` — `CommandTimings(Id, Command, ElapsedMs, ExecutedUtc)` table
- `src/D365FO.Core/Index/MetadataRepository.cs` — `RecordCommandTiming()`, `GetCommandTimings()`
- `src/D365FO.Cli/Commands/Stats/StatsCommand.cs` — `--perf` flag

### 7.5 `d365fo search any --kind <K>` multi-kind filter 🔲

`search any` currently searches all kinds. Add `--kind table,class,edt` filter
(comma-separated) to narrow the union query without needing separate search commands.

**File:** `src/D365FO.Core/Index/MetadataRepository.cs` — `SearchAny(query, kinds[])`
`src/D365FO.Cli/Commands/Search/SearchCommands.cs` — `SearchAnyCommand` settings

### 7.6 `d365fo find batch-jobs` 🔲

Find all batch job classes: `RunBaseBatch` subclasses and `SysOperationServiceController`
subclasses. Useful for auditing before server upgrades. Requires Phase 4.5 flags.

**File:** `src/D365FO.Cli/Commands/Find/FindCommands.cs` — new `FindBatchJobsCommand`

---

## Phase 8: Documentation Refresh 🔲

### 8.1 Update `docs/ARCHITECTURE.md` 🔲

Current ARCHITECTURE.md describes schema v9/v10 with original features. Update to:
- Describe schema v11 (Phase 3–7 new tables)
- Document the Business Events and Security Policy indexing
- Add the integration analysis commands
- Update the AOT object type coverage table
- Document new lint rule categories
- Add shell completion section

### 8.2 Expand `docs/EXAMPLES.md` 🔲

Add worked examples for all Phase 2 commands:
- `generate sysoperation` — batch job with two parameters
- `generate number-sequence` — full three-file output
- `generate workflow` — PurchTable workflow with approval + canSubmitToWorkflow stub
- `generate menu-item` — display menu item for a form
- `generate edt` — custom string EDT with label
- `generate enum` — extensible status enum
- `generate query` — SalesTable + SalesLine inner join

And new Phase 5–7 commands when implemented.

### 8.3 Update `README.md` Commands at a Glance table 🔲

Add Phase 2 generate commands and any new Phase 5–7 commands to the table.
Mark the MCP adapter's tool count as updated (currently says "54 tools").

### 8.4 `docs/TROUBLESHOOTING.md` (new) 🔲

Consolidate the current README troubleshooting table into a full document:
- D365FO-specific package path patterns (cloud VMs, local docker, Azure Files share)
- Common extraction failures (unicode in paths, locked AOT files, .NET 4.8 bridge not found)
- SQLite WAL-mode locking
- Bridge child process startup issues
- Label language detection failures
- MCP tool count / token budget guidance

### 8.5 Expand `.github/copilot-instructions.md` 🔲

Add sections for:
- Phase 2 `generate` commands (sysoperation, workflow, edt, enum, query, menu-item, number-sequence)
- Integration pattern commands (Phase 5)
- Business event scaffolding (Phase 6.1)
- Updated generate command table

### 8.6 New skill: `er-extension-authoring` 🔲

Electronic Reporting custom functions and format extension points:
- `[ERLibraryExtension]` decorator on static class
- Custom ER functions: `[ERExtension("FunctionName", "Category")]`
- ER data model extension via IoC
- Grounded by searching for `ERLibraryExtension` classes via `d365fo search class`

**Files:**
- `skills/_source/er-extension-authoring.md` (new)

**Reference:** https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/analytics/er-overview-components

### 8.7 Update existing skills with Phase 2 commands 🔲

Several existing skills reference `d365fo generate` commands. Update:
- `x++-class-authoring.md` — add `generate sysoperation`, `generate runbase` (Phase 6.4)
- `object-extension-authoring.md` — add `generate edt`, `generate enum` usage
- `table-scaffolding.md` — add `generate query`, `generate number-sequence` for related objects

---

## Phase 9: MCP Parity 🔲

Every new Core feature should get a corresponding MCP tool. Track parity below.

| New Feature | CLI Command | MCP Tool | Status |
|-------------|-------------|----------|--------|
| EDT scaffold | `generate edt` | `generate_edt` | 🔲 |
| Enum scaffold | `generate enum` | `generate_enum` | 🔲 |
| Query scaffold | `generate query` | `generate_query` | 🔲 |
| SysOperation scaffold | `generate sysoperation` | `generate_sysoperation` | 🔲 |
| Workflow scaffold | `generate workflow` | `generate_workflow` | 🔲 |
| NumberSeq scaffold | `generate number-sequence` | `generate_number_sequence` | 🔲 |
| MenuItem scaffold | `generate menu-item` | `generate_menu_item` | 🔲 |
| Business event search | `search business-event` | `search_business_events` | 🔲 |
| Business event get | `get business-event` | `get_business_event` | 🔲 |
| Security policy search | `search security-policy` | `search_security_policies` | 🔲 |
| Integration analysis | `analyze integration` | `analyze_integration` | 🔲 |
| Integration report | `report integrations` | `report_integrations` | 🔲 |
| Impact analysis | `impact` | `analyze_impact` | 🔲 |
| Batch job finder | `find batch-jobs` | `find_batch_jobs` | 🔲 |
| New lint rules | `lint` (extended) | `lint` (updated) | 🔲 |

**Files:**
- `src/D365FO.Mcp/ToolCatalog.cs` — add entries for each new tool
- `src/D365FO.Mcp/ToolHandlers.cs` — add handler methods

---

## Phase 10: Test Coverage Expansion 🔲

### 10.1 Scaffolding snapshot tests for Phase 2 🔲

For each Phase 2 `generate` command, add a "golden file" snapshot test that parses
the output XML and asserts key element presence (prevents silent structural regressions).

```
generate sysoperation → assert <AxClass>, <Extends>SysOperationServiceController</Extends>,
                        classStr(...) in source
generate workflow     → assert <AxWorkflow>, <WorkflowElements>, <AxClass> document,
                        [ExtensionOf(tableStr(...))] in submit stub
generate edt          → assert <AxEdt>, <Extends>, <StringSize> when --size given
generate enum         → assert <AxEnum>, <IsExtensible>, correct <Value> ordering
generate query        → assert <AxQuery>, root <AxQuerySimpleRootDataSource>,
                        nested <AxQuerySimpleEmbeddedDataSource> for joins
```

**File:** `tests/D365FO.Cli.Tests/ScaffoldingSnapshotTests.cs` (new class)

### 10.2 Lint rule unit tests for Phase 4 🔲

Each new lint rule needs a test fixture with both a positive (rule fires) and
negative (rule does not fire) synthetic source string.

**File:** `tests/D365FO.Core.Tests/LintRuleTests.cs` (new class)

### 10.3 Bridge integration tests 🔲

The `D365FO.Bridge` project has no test project. Add mock-based tests:
- JSON-RPC dispatch routing
- Error response format
- Kernel-enum fallback when live bridge is unavailable

**New project:** `tests/D365FO.Bridge.Tests/`

### 10.4 CLI end-to-end integration tests 🔲

Invoke the compiled `d365fo` binary against the MiniAot sample fixture and assert:
- Exit code 0 on valid commands
- JSON envelope shape (`{ ok: true, data: ... }`)
- Exit code 1 on bad input
- Correct SARIF schema for `lint --format sarif`

**File:** `tests/D365FO.Cli.Tests/CliIntegrationTests.cs` (new class)

### 10.5 MCP parity regression test 🔲

Assert that every entry in `ToolCatalog` has a corresponding handler in
`ToolHandlers`. Fails as soon as a catalog entry is added without a handler.

**File:** `tests/D365FO.Core.Tests/McpServerHostTests.cs` — add parity assertion

---

## Relevant Files Summary

| Path | Role |
|------|------|
| `src/D365FO.Core/Index/Schema.sql` | SQLite schema; all Phase 3/7 new tables land here |
| `src/D365FO.Core/Index/MetadataRepository.cs` | All new queries and lint methods |
| `src/D365FO.Core/Extract/MetadataExtractor.cs` | New AOT walkers + extraction-time flags |
| `src/D365FO.Core/Index/Models.cs` | DTOs for new result types |
| `src/D365FO.Core/Scaffolding/` | New scaffolder classes (Phase 6) |
| `src/D365FO.Core/Analysis/CouplingAnalyzer.cs` | Extend with cycle detection surface |
| `src/D365FO.Cli/Program.cs` | Register all new commands |
| `src/D365FO.Cli/Commands/Generate/` | Phase 6 generate commands |
| `src/D365FO.Cli/Commands/Analyze/` | Phase 5 + 7 analyze commands |
| `src/D365FO.Cli/Commands/Lint/LintCommand.cs` | Phase 4 lint rule additions |
| `src/D365FO.Cli/Commands/Find/FindCommands.cs` | Phase 7.6 find batch-jobs |
| `src/D365FO.Mcp/ToolCatalog.cs` | Phase 9 MCP parity entries |
| `src/D365FO.Mcp/ToolHandlers.cs` | Phase 9 MCP handler methods |
| `skills/_source/` | Phase 5 + 8 new skill source files |
| `docs/ARCHITECTURE.md` | Phase 8.1 architecture update |
| `docs/EXAMPLES.md` | Phase 8.2 examples expansion |
| `.github/copilot-instructions.md` | Phase 8.5 instruction update |

---

## Schema Version Strategy

All Phase 3–7 schema additions are bundled into schema **v11**. The `EnsureSchema()`
method handles `IF NOT EXISTS` / `ALTER TABLE` for new columns and `CREATE TABLE` for
new tables. Pre-v11 databases upgrade transparently on first use after update.

New tables in v11:
- `BusinessEvents` (Phase 3.1)
- `SecurityPolicies` (Phase 3.2)
- `ConfigurationKeys` (Phase 3.3)
- `Tiles` (Phase 3.4)
- `CommandTimings` (Phase 7.4)

New columns in v11 (via `ALTER TABLE … ADD COLUMN IF NOT EXISTS`):
- `Tables`: `SaveDataPerCompany`, `CacheLookup`, `OccEnabled`, `ValidTimeStateFieldType`, `SupportInheritance`, `TableExtends`, `AOSAuthorization`, `FormRef`, `ListPageRef`, `SystemTable`
- `Edts`: `ReferenceTable`, `FormHelp`, `AnalysisUsage`, `EnumType`
- `Methods` / `TableMethods`: `HasInsertInLoop`, `HasNestedSelect`, `HasForceLiterals`, `HasForUpdateWithoutUpdate`, `HasTryCatchInTts`, `HasEmptyLoop`
- `TableMethods`: `IsEmptyOverride`
- `Classes`: `IsRunBaseBatch`, `HasCanGoBatch`, `HasPublicInstanceFields`

---

## Recommended Priority Order

```
Phase 3 (AOT coverage: Business Events, Security Policies, Table/EDT property gap-fill)
  ↓ unlocks Phase 4 metadata rules and Phase 5 integration analysis
Phase 4 (Lint expansion: ~15 new BP rules from CAR)
  ↓ each rule is independent; can be delivered incrementally
Phase 5 (Integration patterns: analyze integration, report integrations, OData metadata, skills)
  ↓ uses Phase 3 index + Phase 4 metadata quality
Phase 6 (Scaffolding wave 2: business-event, custom-service, migration-script, runbase)
  ↓ can overlap with Phase 5
Phase 8 (Documentation: ARCHITECTURE, EXAMPLES, copilot-instructions, new skills)
  ↓ continuously updated alongside each phase
Phase 9 (MCP parity: bulk update after Phase 3–6 stabilize)
Phase 7 (DX: impact, compare, completion, perf counters)
Phase 10 (Test coverage: snapshot tests, lint tests, bridge tests, CLI e2e)
  ↓ priority grows with each delivered phase
```

---

## Verification Criteria

1. **Unit tests pass** — `dotnet test` across all test projects after each phase
2. **MiniAot fixture validates** — end-to-end extract + query cycle still works
3. **Schema migration is backwards-compatible** — `EnsureSchema()` upgrades cleanly from v10
4. **New scaffolding emits valid AOT XML** — `XDocument.Parse()` + structural assertions
5. **Lint SARIF output validates** — feed output to VS Code SARIF Viewer extension
6. **New lint rules have no false positives** on the MiniAot sample
7. **MCP parity** — every new CLI command has a corresponding MCP tool
8. **Shell completion works** — test on PowerShell 7 + bash + zsh
9. **Integration analysis runs on MiniAot** — completeness command returns expected JSON shape
10. **All new skills render via `emit-skills.py`** without errors

---

## Key Decisions

| Decision | Rationale |
|----------|-----------|
| Business Events detected from `AxClass` sources, not a separate walk | No `AxBusinessEvent` XML folder; events are implemented as classes |
| Security Policies walk `AxSecurityPolicy` directory | Separate AOT folder, not embedded in other types |
| New lint flags set at extract time | Avoids repeated source scans at query time; consistent with `HasTodayCall` pattern |
| Schema v11 bundles all Phase 3–7 additions | One migration, not incremental bumps per-phase |
| MCP parity batched in Phase 9 | New CLI surface stabilizes first; no half-parity state shipped |
| `generate runbase` added despite legacy status | Many ISV/partner codebases are still on RunBase; migration-safe scaffolding has real value |
| Skills remain format-agnostic | Source in `_source/`, then `emit-skills.ps1`/`.py` produces both Copilot and Anthropic |
| No breaking changes to existing commands | JSON envelope shapes must remain stable |

---

## Further Considerations

1. **D365FO 10.0.x versioning** — Integration patterns change across PU releases. Skill
   metadata should note the Platform Update version tested against. Recommendation:
   test against the latest generally available PU and note it in skill frontmatter.

2. **Electronic Reporting (ER)** — ER configurations live in the database, not the AOT.
   The CLI cannot extract them via file system scanning. The `er-extension-authoring`
   skill (Phase 8.6) covers the X++ extension points (`[ERLibraryExtension]`) but
   configuration-level ER tooling is out of scope.

3. **Dual-write / Dataverse integration** — Covered conceptually in the
   `integration-patterns` skill (Phase 5.4) but no CLI commands are planned, as
   Dual-write is managed via the Power Platform portal, not the AOT.

4. **Composite / Aggregate Data Entities** — These use a different XML structure
   (`AxCompositeDataEntityView`, `AxAggregateMeasurement`). Phase 3 does not cover
   them; a Phase 3+ task can be added if demand warrants it.

5. **`generate table --valid-time-state`** — Tables with `ValidTimeStateFieldType != None`
   require specific field patterns (`ValidFrom`/`ValidTo` of type `Date` or `UtcDateTime`).
   Extend `generate table` to emit these when `--valid-time-state` flag is passed.

6. **Parallel lint rule execution** — Phase 4 adds many rules. To keep `d365fo lint`
   fast, run rule queries in parallel (`Task.WhenAll`) and merge results. The SQLite
   WAL-mode allows concurrent reads.

**Excluded from scope:** GUI/VS Code extension (separate repo), cloud-hosted build
pipeline, D365FO kernel debugging, Electronic Reporting configuration management,
Dual-write configuration.
