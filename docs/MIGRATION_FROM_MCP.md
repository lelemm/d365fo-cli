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

Complete map of the MCP server's current tool surface (61 tools) onto CLI commands.

### Discovery & read

| MCP tool | CLI command |
|---|---|
| `search` | `d365fo search any <q>` |
| `batch_search` | `d365fo search batch <q1> <q2> …` |
| `batch_get_info` | `d365fo get batch table:CustTable class:X …` (max 10) |
| `search_extensions` | `d365fo find extensions <Target>` |
| `search_labels` | `d365fo search label <q> --lang en-us,cs` |
| `get_class_info` / `get_table_info` / `get_edt_info` / `get_enum_info` / `get_form_info` / `get_query_info` / `get_view_info` / `get_report_info` / `get_data_entity_info` / `get_menu_item_info` | `d365fo get <kind> <name>` or `d365fo get object <kind> <name>` |
| `get_table_extension_info` | `d365fo find extensions <Table> --kind Table` |
| `get_method_signature` | `d365fo get class <Name>` (signatures included) |
| `get_method_source` | `d365fo read class <Name> --method <M>` |
| `get_label_info` | `d365fo get label` / `d365fo resolve label @SYS12345` |
| `get_security_artifact_info` | `d365fo get role\|duty\|privilege <Name>` |
| `get_security_coverage_for_object` | `d365fo get security <obj> --type <kind>` |
| `find_coc_extensions` | `d365fo find coc <Class>[::<method>]` |
| `find_event_handlers` | `d365fo find handlers <Target>` |
| `find_references` / `resolve_references` | `d365fo find refs <Name>` / `d365fo validate references --file <F>` |
| `get_workspace_info` | `d365fo doctor` + `d365fo index status` (incl. stale-index detection) |

### Grounded generation & gates

| MCP tool | CLI command |
|---|---|
| `prepare_change` | `d365fo prepare change <Object> --method <M> --goal "…"` → grounding token |
| `prepare_create` | `d365fo prepare create <Name> --type <T> --goal "…"` → grounding token |
| `validate_xpp` | `d365fo validate xpp [FILE]` (offline BP rules, data-driven property stats) |
| `validate_object_naming` | `d365fo validate name <KIND> <NAME>` |
| `validate_form_pattern` | `d365fo validate form-pattern [FILE]` (FP001–FP010) |
| `get_form_patterns` | `d365fo find form-patterns [--pattern\|--table\|--similar-to]` |
| `get_form_pattern_spec` | `d365fo get form-pattern [NAME]` |
| `get_table_patterns` | `d365fo generate table --pattern <P>` (P1 patterns built in) |
| `recommend_extension_strategy` / `analyze_extension_points` | `d365fo suggest extension <Target>` |
| `suggest_edt` | `d365fo suggest edt <FieldName>` |
| `analyze_class_completeness` | `d365fo analyze completeness` |

### Writes

| MCP tool | CLI command |
|---|---|
| `create_d365fo_file` / `generate_d365fo_xml` / `generate_code` | `d365fo generate <kind> … --install-to <Model>` |
| `generate_smart_table` | `d365fo generate table <Name> --pattern <P> --field …` |
| `generate_smart_form` | `d365fo generate form <Name> --pattern <P>` (pattern-gated write) |
| `generate_smart_report` | `d365fo generate report <Name> --dataset …` |
| `modify_d365fo_file` | targeted editor edit of CDATA method bodies + `d365fo index refresh`; structural changes via `generate … --overwrite` |
| `undo_last_modification` | `.bak` backups written by every overwrite + `git checkout` |
| `create_label` / `rename_label` | `d365fo label create\|rename\|delete` (multi-language via `--lang`) |
| `review_workspace_changes` | `d365fo review diff` |
| `update_symbol_index` | `d365fo index refresh [--model <M>]` |

### Ops (Windows VM)

| MCP tool | CLI command |
|---|---|
| `build_d365fo_project` / `verify_d365fo_project` | `d365fo build` (structured `xppcDiagnostics`) |
| `trigger_db_sync` | `d365fo sync` |
| `run_bp_check` | `d365fo bp check` (offline subset: `d365fo validate xpp`) |
| `run_systest_class` | `d365fo test run --suite <S>` |

### Knowledge tools → Skills

| MCP tool | CLI equivalent |
|---|---|
| `get_xpp_knowledge` | 19 lazy-loaded Skills (`skills/anthropic/`, `skills/copilot/`) — X++ rule canon loaded only when relevant |
| `get_d365fo_error_help` | `xpp-best-practice-rules` skill + `d365fo validate xpp` diagnostics with `fix` hints |
| `get_api_usage_patterns` / `analyze_code_patterns` | `xpp-database-queries`, `x++-class-authoring`, … skills + `d365fo find refs` |
| `suggest_method_implementation` / `code_completion` | covered by the host agent grounded via `prepare change` + `read class` |

### CLI-only commands (no MCP equivalent)

| Command | Purpose |
|---|---|
| `d365fo schema [--full]` / `d365fo agent-prompt` | Agent manifest / system prompt for any harness |
| `d365fo analyze integration\|impact`, `report-integrations` | OData/DMF readiness, change-impact, integration surface report |
| `d365fo models coupling` | Fan-in/fan-out/instability/cycles over model dependencies |
| `d365fo index export\|import\|optimize\|history` | Index snapshots for CI caching, VACUUM, extraction telemetry |
| `d365fo daemon start\|status\|stop\|warmup` | Warm-cache IPC server with AOT file watcher |
| `d365fo lint`, `d365fo stats`, `d365fo find batch-jobs` | Index-wide BP gate, aggregate counters, batch-job inventory |
| `d365fo completion` | Shell tab-completion (bash, zsh, powershell) |

---

## See also

- [SETUP.md](SETUP.md) — CLI installation and configuration.
- [EXAMPLES.md](EXAMPLES.md) — one example per command.
- [TOKEN_ECONOMICS.md](TOKEN_ECONOMICS.md) — why the CLI saves tokens compared to MCP.
- [ARCHITECTURE.md](ARCHITECTURE.md) — relationship between the CLI, MCP adapter, and Core layer.
