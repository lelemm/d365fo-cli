# MCP Server vs CLI

Both tools share an **identical purpose** — give GitHub Copilot access to real D365FO metadata so it generates X++ code that compiles on the first attempt. They differ in protocol and deployment, but share the same data layer.

| | `d365fo-mcp-server` | `d365fo` CLI |
|---|---|---|
| Protocol | MCP tools (JSON-RPC over stdio / HTTP) | Shell commands |
| Implementation | TypeScript + Node.js | C# / .NET 10 |
| Data layer | SQLite index + C# Bridge | **Shared** — same index, schema v5 is a superset |
| Deployment | Local or Azure App Service (shared team instance) | Local |
| Copilot integration | MCP tool calls | Shell tool |
| Token economy | Larger JSON envelopes | Smaller output — see [TOKEN_ECONOMICS.md](TOKEN_ECONOMICS.md) |
| CLI-only commands | — | `review diff`, `build/sync/test/bp`, `schema`, batch operations, `read`, `models` |

---

## Which solution to use?

| Situation | Recommendation |
|---|---|
| GitHub Copilot Chat (VS / VS Code) — primary target | **CLI** (shell tool) — or both side by side |
| Team shares one instance without a local install | MCP on Azure App Service |
| Agent without a shell tool (Claude.ai web, ChatGPT web) | MCP |
| Want both — Copilot picks the cheaper one per task | Side by side (Path A below) |

---

## Migration

### Path A — side-by-side operation (recommended)

The existing `.mcp.json` and `copilot-instructions.md` stay unchanged. The CLI is added alongside:

1. Build and deploy the CLI — see [SETUP.md](SETUP.md).
2. Copy `skills/copilot/*.instructions.md` to `.github/instructions/` in your X++ project.
3. Copilot automatically uses the shell tool for CLI commands and MCP for tool calls — both from the same index.

### Path B — CLI only

1. Steps 1–2 from Path A.
2. Remove the `d365fo-*` entries from `.mcp.json`.

### Path C — MCP only

Leave `d365fo-mcp-server` unchanged. The `D365FO.Mcp` adapter in this repository reads from the same SQLite index via JSON-RPC 2.0 stdio.

---

## Index compatibility

The CLI schema (`src/D365FO.Core/Index/Schema.sql`, `PRAGMA user_version = 5`) is a superset of the MCP server. Point an existing database at it:

```sh
export D365FO_INDEX_DB=/path/to/existing/d365fo.sqlite
d365fo index status
```

`EnsureSchema` migrates the schema forward automatically on first connection. Re-extract is idempotent per model.

---

## Command mapping

### MCP tool → CLI command

| MCP tool | CLI command |
|---|---|
| `search` / `search_any` | `d365fo search any <q>` |
| `batch_search` | `d365fo search batch <q1> <q2> …` |
| `get_*` family | `d365fo get <kind> <name>` or `d365fo get object <kind> <name>` |
| `find_*` relation family | `d365fo find <relation> <name>` or `d365fo find related <relation> <name>` |
| `get_table_details` | `d365fo get table <name>` *(+ indexes, methods, delete actions)* |
| `get_edt_details` | `d365fo get edt <name>` |
| `find_coc_extensions` | `d365fo find coc <Class>[::<method>]` |
| `get_security_coverage_for_object` | `d365fo get security <obj> --type <kind>` |
| `search_labels` | `d365fo search label <q> --lang en-us,cs` |
| `get_menu_item_details` | `d365fo get menu-item <name>` |
| `get_table_relations` | `d365fo find relations <table>` |
| `generate_smart_form` | `d365fo generate form <Name> --pattern <P>` |

### CLI-only commands (no MCP equivalent)

| Command | Purpose |
|---|---|
| `d365fo schema [--full]` | Agent manifest or full catalog of CLI commands |
| `d365fo search query\|view\|entity\|report\|service\|workflow` | Index queries, views, data entities, SSRS reports, services, workflows |
| `d365fo get form\|role\|duty\|privilege\|query\|view\|entity\|report\|service\|service-group` | Object details for the given type |
| `d365fo find extensions <Target>` | Table / Form / Edt / Enum extensions on the given object |
| `d365fo find handlers <Source>` | Event subscribers for a form / table / delegate |
| `d365fo resolve label @SYS12345 [--lang …]` | Resolve a `@File+Key` token across indexed languages |
| `d365fo read class\|table\|form <Name> [--method X]` | Read X++ source code from AOT XML |
| `d365fo models list` / `d365fo models deps <Name>` | List models or their dependency graph |
| `d365fo generate table\|class\|coc\|form\|entity\|extension\|event-handler\|privilege\|duty\|role` | Scaffold a new AOT XML object |
| `d365fo review diff` | AOT-semantic diff of git changes |
| `d365fo build` / `sync` / `test run` / `bp check` | MSBuild / SyncEngine / SysTestRunner / xppbp (Windows + VM) |

---

## See also

- [SETUP.md](SETUP.md) — CLI installation and configuration.
- [EXAMPLES.md](EXAMPLES.md) — one example per command.
- [TOKEN_ECONOMICS.md](TOKEN_ECONOMICS.md) — why the CLI saves tokens compared to MCP.
- [ARCHITECTURE.md](ARCHITECTURE.md) — relationship between the CLI, MCP adapter, and Core layer.
