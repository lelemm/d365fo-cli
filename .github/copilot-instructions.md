# D365 Finance & Operations X++ Development — `d365fo` CLI

<!--
  Deployed to your X++ project via Install-D365FoCopilotSkills.ps1.
  Primary target: GitHub Copilot in Visual Studio 2022 / 2026 (agent mode with built-in tools).
  Secondary target: VS Code with Copilot in agent mode (can run d365fo directly via terminal).
  References in the form `[learn:<page>]` link to Microsoft Learn pages
  (see "Authoritative X++ syntax source" at the bottom).
-->

This file gives **GitHub Copilot** the rules for assisting with D365 Finance & Operations X++ development. It is deployed to your X++ project's `.github/` folder by `Install-D365FoCopilotSkills.ps1` and is read automatically by Copilot in Visual Studio.

> **Primary environment — VS 2022 / VS 2026 agent mode:** GitHub Copilot runs `d365fo` commands via the built-in terminal tool (`run_command_in_terminal`). Skills in `.github/instructions/` load on demand and tell Copilot exactly which commands to run. No copy-paste, no MCP overhead.
>
> **Secondary environment — VS Code agent mode:** Same approach, different terminal tool name (`run_in_terminal`). Identical experience.
>
> **Fallback — VS Chat mode (no agent tools):** Copilot must ask the user to run `d365fo` commands manually and paste back JSON output. See the fallback workflow section below.

---

## Mandatory first steps

```sh
d365fo doctor --output json           # confirm index is healthy
d365fo models list --output json      # confirm target model — NEVER guess it
```

| Result | Action |
|---|---|
| `ok: false / NO_INDEX` | Run `d365fo index extract` first |
| `warnings: ["stale-index"]` | Run `d365fo index refresh` (incremental) |
| `⛔ CONFIGURATION PROBLEM` | Stop. Relay message to user. Wait. |
| Healthy + model confirmed | Note model name. Proceed. |

Models are ISV / customer policy boundaries — never infer from search results; always ask or read from `.rnrproj`.

### VS / VS Code operating modes

| Environment | How Copilot runs d365fo | Token cost |
|---|---|---|
| **VS 2022/2026 agent mode** | Built-in terminal tool → `d365fo` CLI | ~100 tokens |
| **VS Code agent mode** | `run_in_terminal` → `d365fo` CLI | ~100 tokens |
| **VS Chat mode** (no agent tools) | User runs manually, pastes JSON | collaborative |

In agent mode Copilot calls `d365fo` commands autonomously — it reads skills from `.github/instructions/`, decides which commands to run, executes them in the terminal, and interprets the JSON output. No copy-paste required.

### ⛔ Chat mode only (no agent tools) — fallback workflow

If Copilot is running in **chat mode without agent tools** (no tool list visible), it cannot call `d365fo` directly. The built-in code search / `@workspace` **always fails on AOT XML** — do not attempt it.

```
// ❌ WRONG — do not attempt this
Copilot: "Let me search for existing table examples in your codebase…"
         → "There was an error executing code search"
Copilot: "Since I cannot access the codebase, I'll provide generic guidance…"
         → hallucinated X++ templates
```

- ❌ **Never** attempt code search / `@workspace` on a D365FO project.
- ❌ **Never** say "Since I cannot access the codebase" and fall back to generic guidance.

**Instead:** ask the user to run the required `d365fo` commands in their Developer PowerShell and paste back the JSON output before you proceed with code generation.

---

## Core tool mapping

| Need | Command |
|---|---|
| Read class structure (methods, signatures) | `d365fo get class <Name> --output json` |
| Read X++ method body | `d365fo read class <Name> --method <M>` |
| Read table fields / indexes / relations | `d365fo get table <Name> --output json` |
| Read form controls / data sources | `d365fo get form <Name> --output json` |
| Search objects by name | `d365fo search class\|table\|form\|edt\|enum <query> --output json` |
| Multiple searches in one call | `d365fo search batch <q1> <q2> … --output json` |
| Multiple searches limited to one kind | `d365fo search batch <q1> <q2> … --kind class --output json` |
| Check existing CoC wrappers | `d365fo find coc <Class>::<method> --output json` |
| Find event handlers | `d365fo find handlers <Target> --output json` |
| Find label | `d365fo search label "<text>" --output json` |
| Resolve label token | `d365fo resolve label @SYS12345 --lang en-us,cs` |
| Trace security (Role → Duty → Privilege) | `d365fo get security <Role> --type Role --output json` |
| Find table relations | `d365fo find relations <Table> --output json` |
| Create new AOT object | `d365fo generate table\|class\|form\|coc\|entity\|edt\|enum … --install-to <Model>` |
| Edit method body (CDATA only) | `replace_string_in_file` — then `d365fo index refresh --model <Model>` |
| Structural change (add field, index, relation) | `d365fo generate … --overwrite` — NEVER `replace_string_in_file` on XML structure |
| Discover all CLI commands | `d365fo schema --output json` |
| Index health check | `d365fo doctor --output json` |

> **`--output json` is mandatory** in agent contexts. Write-path: `--install-to <Model>` for bridge-installed scaffolds; `--out <PATH>` for standalone. Generated files land at `PackagesLocalDirectory/<Model>/<Model>/Ax<Type>/<Name>.xml`.

---

## Key rules (condensed)

1. **Never create/edit AOT XML by hand or via scripts** (`Set-Content`, `Out-File`, `New-Item`, PS/Python scripts). If `d365fo` fails, stop and report — do not fall back to scripts.
2. **Never use code search / `@workspace` / `file_search` / `grep_search` on AOT XML.** Always `d365fo search`.
3. **Never auto-run `d365fo build`, `sync`, `bp check`, or `test run`** — slow, blocks the user. Only on explicit request.
4. **Never copy default parameter values into CoC wrapper signatures.** Causes compile errors.
5. **Never use `today()`** — use `DateTimeUtil::getToday(DateTimeUtil::getUserPreferredTimeZone())`.
6. **Never hardcode strings in `Info()` / `warning()` / `error()`** — use `@Model:Label`. Search first: `d365fo search label`.
7. **Never nest `while select` loops** — use joins, `exists join`, or pre-load to `Map` / temp table.
8. **Never use `replace_string_in_file` on `.xml` AOT files without running `d365fo index refresh` after** — stale index returns pre-edit data.

---

## Full X++ rules — loaded on demand from skills

Detailed rules are in `.github/instructions/` (lazy-loaded by Copilot when relevant):

| Skill file | Covers |
|---|---|
| `coc-extension-authoring` | CoC wrapper rules, `next` placement, signature matching, `[Hookable]`/`[Wrappable]` |
| `xpp-database-queries` | `select` grammar, `crossCompany`, `in` operator, joins, aggregates, SysDa, QueryRun |
| `x++-class-authoring` | Class structure, visibility, constructor patterns, extension methods, `var`, constants |
| `xpp-class-and-method-rules` | Method modifiers, override visibility, optional params, `this`, pass-by-value |
| `xpp-statement-and-type-rules` | `switch`, ternary, null handling, `using`, casting, `is`/`as` |
| `xpp-best-practice-rules` | BP rules: `today()`, labels, nested loops, alternate keys, `[SysObsolete]` |
| `form-pattern-scaffolding` | FormRun lifecycle, 9 form patterns, Display/Action/Output menu items |
| `table-scaffolding` | Table creation, EDT assignment, relations, indexes, number sequences, `TableGroup` vs `TableType` |
| `data-entity-scaffolding` | Data entity (`AxDataEntityView`) patterns, OData exposure |
| `event-handler-authoring` | `[DataEventHandler]`, `[SubscribesTo]`, pre/post handlers |
| `object-extension-authoring` | Table / Form / EDT / Enum extensions; new EDT and Enum creation |
| `security-hierarchy-trace` | Role → Duty → Privilege → Entry Point tracing; scaffold privilege/duty/role |
| `sysoperation-batch-patterns` | SysOperation batch jobs, RunBase/RunBaseBatch, data migration scripts |
| `business-events-authoring` | Custom business event contract + class scaffolding |
| `custom-service-authoring` | JSON/SOAP custom service scaffolding |
| `integration-patterns` | OData, DMF, business events, number sequences |
| `label-translation` | Label search, reuse, creation, multi-language |
| `model-dependency-and-coupling` | Model reference chains, ISV/customer boundary rules |
| `review-and-checkpoint-workflow` | Git checkpoint, `d365fo review diff`, accept/reject workflow |
