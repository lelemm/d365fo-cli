# d365fo — AI-native CLI for D365 Finance & Operations X++

> **Index your AOT. Search it in milliseconds. Scaffold objects that compile. Ground every AI answer in real metadata.**

[![MIT License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey.svg)]()
[![Successor to d365fo-mcp-server](https://img.shields.io/badge/successor%20to-d365fo--mcp--server-orange.svg)](https://github.com/dynamics365ninja/d365fo-mcp-server)

Built for D365FO developers and AI agents that need **real AOT metadata** — not training-data guesses.

---

## Why this exists

Every AI assistant hallucinates D365 field names, method signatures, and label IDs. The root cause is simple: your environment has hundreds of thousands of custom objects that no model has ever seen. The fix is equally simple: **give the agent a local index it can query before generating any code**.

| Without `d365fo` | With `d365fo` |
|---|---|
| Agent invents `CustTable.CustomerName` (doesn't exist) | `d365fo get table CustTable` returns the real field list in <100 ms |
| CoC wrapper copies default-param values → compile error | `d365fo find coc Class::method` returns exact signature — no guessing |
| Label IDs typed by hand → `BPErrorUnknownLabel` BP failures | `d365fo search label "customer account"` finds the right `@SYS…` token |
| ~70 MCP tool definitions burn ~3,500 tokens every turn | 1 shell command + lazy-loaded Skills ≈ 100 tokens per turn |
| Scaffolded XML missing ActionPane / QuickFilter / PatternVersion | `d365fo generate form` uses validated pattern templates |
| `today()` in generated X++ → `BPUpgradeCodeToday` failure | Agent receives the X++ rule canon from `.github/copilot-instructions.md` |

---

## Solution Architecture

![Solution Architecture](docs/img/solution-architecture-diagram.png)

The CLI sits between your AI agent and the D365FO metadata layer. It exposes a single `d365fo` binary that queries a local SQLite index (or a live metadata bridge on Windows VMs) and returns stable JSON envelopes your agent can parse in one tool call.

---

## Key Capabilities

- **Instant AOT lookup** — tables, classes, EDTs, enums, forms, queries, views, reports, services, workflows, security roles, and labels; results in milliseconds without touching a D365 VM
- **Scaffold X++ objects** — pattern-validated XML for tables, classes, CoC extensions, and AxForms (all 9 D365FO patterns: `SimpleList`, `SimpleListDetails`, `DetailsMaster`, `DetailsTransaction`, `Dialog`, `TableOfContents`, `Lookup`, `ListPage`, `Workspace`)
- **Understand the code** — trace CoC targets, inbound/outbound relations, event handlers, label translations, and cross-references across your workspace
- **AI-ready JSON** — every command returns a stable `{ ok, data, warnings }` envelope; agents parse once, never re-adapt
- **Agent-first command shortcuts** — `search batch`, `get object`, and `find related` collapse common MCP multi-tool workflows into one CLI process
- **15 lazy-loaded Skills** — full X++/CoC/BP rule canon (database queries, class rules, statement types, best practices) loaded only when relevant; ~90% fewer tokens per workflow than MCP
- **Daemon mode** — warm-cache named-pipe server with file-system watcher; auto-refreshes index on AOT XML changes (debounce configurable)
- **CI / pipeline ready** — runs in PowerShell, bash/zsh, GitHub Actions, Azure DevOps; no GUI required
- **Optional VM integration** — drives `MSBuild`, `SyncEngine`, `SysTestRunner`, and `xppbp` on Windows D365FO dev VMs
- **MCP adapter included** — `d365fo-mcp` speaks JSON-RPC 2.0 and shares the same index; wire into Claude Desktop, Cursor, Continue, or VS Code MCP

---

## Quick Start

### Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download) (pinned in `global.json`)
- Access to a D365 F&O `PackagesLocalDirectory` (local clone, Azure Files share, or Windows VM path)

### Install

```sh
git clone https://github.com/dynamics365ninja/d365fo-cli.git
cd d365fo-cli
dotnet build d365fo-cli.slnx -c Release
```

**PowerShell alias (fastest for dev):**

```powershell
function d365fo { dotnet run --project C:\path\to\d365fo-cli\src\D365FO.Cli -- @args }
```

**Self-contained binary (for distribution):**

```sh
dotnet publish src/D365FO.Cli -c Release -r win-x64 --self-contained
# also: linux-x64, osx-arm64
```

### First run

```sh
# Point at your packages folder
$env:D365FO_PACKAGES_PATH = "K:\AosService\PackagesLocalDirectory"

# Build + populate the index
d365fo index build
d365fo index extract

# Verify
d365fo doctor --output json
d365fo index status --output json

# Search
d365fo search table Cust --output json
d365fo get table CustTable --output json
d365fo find coc SalesTable::insert --output json
d365fo resolve label @SYS12345 --lang en-us,cs
```

### Scaffold your first object

```sh
# New table
d365fo generate table FmVehicle \
  --label "@Fleet:Vehicle" \
  --field VIN:VinEdt:mandatory \
  --field Make:Name \
  --field Year:YearEdt \
  --out src/MyModel/AxTable/FmVehicle.xml

# Chain-of-Command extension
d365fo generate coc SalesTable --method insert --out src/MyModel/AxClass/SalesTable_MyExt.xml

# Form (SimpleList pattern)
d365fo generate form FmVehicles \
  --pattern SimpleList \
  --table FmVehicle \
  --field VIN --field Make --field Year \
  --out src/MyModel/AxForm/FmVehicles.xml
```

---

## AI Agent Integration

### GitHub Copilot (VS Code / Visual Studio)

Copy `.github/copilot-instructions.md` into your consuming repo's `.github/` folder. It contains the full X++ rule canon with MS Learn citations.

Optionally copy the Skills:

```sh
# Emit GitHub Copilot instructions
python3 scripts/emit-skills.py

# Then copy to your repo
cp skills/copilot/*.instructions.md /your-repo/.github/instructions/
```

### Claude Code / Claude Desktop

```sh
# Emit Anthropic SKILL.md files
python3 scripts/emit-skills.py

# Reference in your project or ~/.claude/skills/
cp -r skills/anthropic/ /your-repo/.claude/skills/
```

### Codex CLI / Gemini CLI / Cursor

Reference the `SKILL.md` files from `skills/anthropic/` in your session prompt or `AGENTS.md`.

### MCP (Claude Desktop, Continue, VS Code MCP)

```json
{
  "mcpServers": {
    "d365fo": {
      "command": "d365fo-mcp",
      "args": [],
      "env": {
        "D365FO_PACKAGES_PATH": "K:\\AosService\\PackagesLocalDirectory"
      }
    }
  }
}
```

---

## When to use built-in tools vs. `d365fo`

> Quick reference for developers and AI agents working in VS 2022 / VS Code.

| Scenario | Built-in editor / terminal tools | `d365fo` CLI |
|---|---|---|
| Read class structure (methods, signatures) | ❌ `get_file` on XML — unreliable schema | ✅ `d365fo get class <Name> --output json` |
| Read a method body (X++ source) | ❌ | ✅ `d365fo read class <Name> --method <M>` |
| Inspect table fields / indexes / relations | ❌ `get_file` on AxTable XML — unreliable | ✅ `d365fo get table <Name> --output json` |
| Search for a class / table / method | ❌ `code_search` / `file_search` — can't parse AOT XML schema, returns misleading snippets | ✅ `d365fo search class <query> --output json` |
| Check for existing CoC wrappers | ❌ | ✅ `d365fo find coc <Class>::<method> --output json` |
| Create a new AOT object (class, table, form…) | ❌ `create_file` — wrong location, wrong XML schema | ✅ `d365fo generate class/table/form … --install-to <Model>` |
| Modify existing AOT XML — targeted method body edit (inside CDATA) | ⚠️ `replace_string_in_file` / `multi_replace_string_in_file` — allowed for method bodies only; run `d365fo index refresh` after | ✅ `d365fo generate … --overwrite` for full-file replace |
| Modify existing AOT XML — structural change (add field, index, relation…) | ❌ `replace_string_in_file` — corrupts XML structure | ✅ `d365fo generate extension … --overwrite` or VS AOT |
| Search for a label | ❌ | ✅ `d365fo search label "<text>" --output json` |
| Resolve a label key | ❌ | ✅ `d365fo resolve label @SYS12345 --lang en-us,cs` |
| Trace security (Role → Duty → Privilege) | ❌ | ✅ `d365fo get security <Role> --type Role --output json` |
| Run best-practice check | ❌ | ✅ `d365fo bp check --output json` (Windows VM only, on user request) |
| Inspect model dependencies | ❌ | ✅ `d365fo get model <Name> --output json` |
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

## Why CLI instead of MCP?

MCP servers inject every tool definition into the model's context on every single turn. For this project that used to be **54 tools ≈ 2,900 tokens every turn** — and now stands at ~70 tools as new commands have been added.

| | MCP server | CLI + Skills |
|---|---|---|
| Tool definitions per turn | ~70 tools (~3,500 tokens) | 1 shell tool (~100 tokens) |
| Discovery round-trips | 2–3 per task | often 1 (`d365fo get table X`) |
| Scriptable (shell, CI/CD) | No | Yes |
| Works in any AI harness | No — MCP hosts only | Yes — Copilot, Claude, Codex, Gemini, … |
| Token cost over 15-turn workflow | baseline | **~90% reduction** |

See [`docs/TOKEN_ECONOMICS.md`](docs/TOKEN_ECONOMICS.md) for the full analysis and the cases where MCP still wins.

---

## Commands at a Glance

| Group | Commands |
|---|---|
| **Prepare** | `prepare change`, `prepare create` — single-round context aggregators returning a grounding token |
| **Validate** | `validate name`, `validate xpp` (offline BP rules), `validate references` (anti-hallucination gate over the index) |
| **Index** | `index build`, `index extract`, `index refresh`, `index status` (incl. `stale-index` detection), `index export`, `index import`, `index optimize`, `index history` |
| **Discover** | `search any`, `search batch`, `search class\|table\|edt\|enum\|form\|query\|view\|entity\|report\|service\|workflow\|label\|business-event\|security-policy\|configuration-key\|tile\|workspace` |
| **Get** | `get object`, `get table\|class\|edt\|enum\|form\|menu-item\|security\|label\|role\|duty\|privilege\|query\|view\|entity\|report\|service\|business-event\|security-policy` |
| **Find** | `find related`, `find coc`, `find relations`, `find usages`, `find extensions`, `find handlers`, `find refs`, `find form-patterns` |
| **Read** | `read class`, `read table`, `read form` |
| **Resolve** | `resolve label` |
| **Generate** | `generate table\|class\|coc\|form\|entity\|extension\|event-handler\|privilege\|duty\|role\|report\|sysoperation\|number-sequence\|workflow\|menu-item\|edt\|enum\|query\|business-event\|custom-service\|migration-script\|runbase\|security-policy` |
| **Analyze** | `analyze completeness`, `lint`, `suggest extension` |
| **Review** | `review diff` |
| **Models** | `models list`, `models deps`, `models coupling` |
| **Agent** | `agent-prompt`, `schema` |
| **Daemon** | `daemon start\|status\|stop\|warmup` |
| **Ops (Windows VM)** | `build`, `sync`, `test run`, `bp check` |

See [`docs/EXAMPLES.md`](docs/EXAMPLES.md) for one worked example per command.

---

## Documentation

| Doc | What's inside |
|---|---|
| [docs/SETUP.md](docs/SETUP.md) | Install, configure, verify — dev alias vs. self-contained distribution |
| [docs/EXAMPLES.md](docs/EXAMPLES.md) | One worked example per command (discover, scaffold, review, ops, agents, daemon, CI) |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Index schema, AOT type coverage table (22 types), 16 lint rules, bridge, daemon file watcher |
| [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) | Common failures, SQLite locking, bridge issues, label detection, schema migration |
| [docs/TOKEN_ECONOMICS.md](docs/TOKEN_ECONOMICS.md) | Why CLI+Skills is cheaper per turn, with numbers |
| [docs/MIGRATION_FROM_MCP.md](docs/MIGRATION_FROM_MCP.md) | Coming from `d365fo-mcp-server`? Read this first |
| [docs/ROADMAP.md](docs/ROADMAP.md) | Planned and deferred items |

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `PACKAGES_PATH_NOT_FOUND` | Set `D365FO_PACKAGES_PATH` or pass `--packages <PATH>` |
| `UNSUPPORTED_PLATFORM` | `build` / `sync` / `test` / `bp` require Windows + a D365FO dev VM |
| `NO_INDEX` | Run `d365fo index build` then `d365fo index extract` |
| Index appears stale after editing XML | Run `d365fo index refresh --model <Model>` |
| Index file locked | Stop any running `d365fo daemon` or `d365fo-mcp` process; WAL sidecar files (`-wal`, `-shm`) are normal |

More in [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) and [docs/SETUP.md](docs/SETUP.md#troubleshooting).

---

## License

MIT. The sibling [`d365fo-mcp-server`](https://github.com/dynamics365ninja/d365fo-mcp-server) is also MIT.

---

## Disclaimer

This project is an independent research effort and is not affiliated with, endorsed by, or associated with Microsoft or any other organization. It is provided as-is for educational and development purposes.
