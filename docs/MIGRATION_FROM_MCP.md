# MCP Server vs CLI

Oba nástroje mají **identický záměr** — dát GitHub Copilot přístup k reálným D365FO metadatům tak, aby generoval X++ kód, který se kompiluje na první pokus. Liší se protokolem a nasazením, sdílejí stejnou datovou vrstvu.

| | `d365fo-mcp-server` | `d365fo` CLI |
|---|---|---|
| Protokol | MCP tools (JSON-RPC přes stdio / HTTP) | Shell příkazy |
| Implementace | TypeScript + Node.js | C# / .NET 10 |
| Data vrstva | SQLite index + C# Bridge | **Sdílená** — stejný index, schema v5 je superset |
| Nasazení | Lokální nebo Azure App Service (sdílená instance pro tým) | Lokální |
| Integrace s Copilotem | MCP tool calls | Shell tool |
| Token ekonomika | Větší JSON envelopy | Menší výstup — viz [TOKEN_ECONOMICS.md](TOKEN_ECONOMICS.md) |
| CLI-only příkazy | — | `review diff`, `build/sync/test/bp`, `schema`, batch operace, `read`, `models` |

---

## Které řešení použít?

| Situace | Doporučení |
|---|---|
| GitHub Copilot Chat (VS / VS Code) — primární cíl | **CLI** (shell tool) — nebo obojí souběžně |
| Tým sdílí jednu instanci bez lokální instalace | MCP na Azure App Service |
| Agent bez shell toolu (Claude.ai web, ChatGPT web) | MCP |
| Chceš obojí — Copilot si vybere levnější per-task | Souběžně (Path A níže) |

---

## Migrace

### Path A — souběžné provozování (doporučeno)

Existující `.mcp.json` a `copilot-instructions.md` zůstávají beze změny. CLI se přidá vedle:

1. Sestav a nasaď CLI — viz [SETUP.md](SETUP.md).
2. Zkopíruj `skills/copilot/*.instructions.md` do `.github/instructions/` tvého X++ projektu.
3. Copilot automaticky použije shell tool pro CLI příkazy a MCP pro tool calls — obojí ze stejného indexu.

### Path B — pouze CLI

1. Kroky 1–2 z Path A.
2. Odstraň `d365fo-*` záznamy z `.mcp.json`.

### Path C — pouze MCP

Ponechej `d365fo-mcp-server` beze změny. `D365FO.Mcp` adaptér v tomto repozitáři čte ze stejného SQLite indexu přes JSON-RPC 2.0 stdio.

---

## Kompatibilita indexu

CLI schema (`src/D365FO.Core/Index/Schema.sql`, `PRAGMA user_version = 5`) je superset MCP serveru. Existující databázi stačí nasměrovat:

```sh
export D365FO_INDEX_DB=/path/to/existing/d365fo.sqlite
d365fo index status
```

`EnsureSchema` migruje schéma forward automaticky při prvním připojení. Re-extract je idempotentní per model.

---

## Mapování příkazů

### MCP tool → CLI příkaz

| MCP tool | CLI příkaz |
|---|---|
| `search` / `search_any` | `d365fo search any <q>` |
| `batch_search` | `d365fo search batch <q1> <q2> …` |
| `get_*` family | `d365fo get <kind> <name>` nebo `d365fo get object <kind> <name>` |
| `find_*` relation family | `d365fo find <relation> <name>` nebo `d365fo find related <relation> <name>` |
| `get_table_details` | `d365fo get table <name>` *(+ indexy, metody, delete actions)* |
| `get_edt_details` | `d365fo get edt <name>` |
| `find_coc_extensions` | `d365fo find coc <Class>[::<method>]` |
| `get_security_coverage_for_object` | `d365fo get security <obj> --type <kind>` |
| `search_labels` | `d365fo search label <q> --lang en-us,cs` |
| `get_menu_item_details` | `d365fo get menu-item <name>` |
| `get_table_relations` | `d365fo find relations <table>` |
| `generate_smart_form` | `d365fo generate form <Name> --pattern <P>` |

### CLI-only příkazy (bez MCP ekvivalentu)

| Příkaz | Účel |
|---|---|
| `d365fo schema [--full]` | Agent manifest nebo kompletní katalog CLI příkazů |
| `d365fo search query\|view\|entity\|report\|service\|workflow` | Index dotazů, views, datových entit, SSRS reportů, služeb, workflow |
| `d365fo get form\|role\|duty\|privilege\|query\|view\|entity\|report\|service\|service-group` | Detaily objektu daného typu |
| `d365fo find extensions <Target>` | Table / Form / Edt / Enum extensions na daný objekt |
| `d365fo find handlers <Source>` | Event subscribers pro form / table / delegate |
| `d365fo resolve label @SYS12345 [--lang …]` | Přeložení `@File+Key` tokenu přes indexované jazyky |
| `d365fo read class\|table\|form <Name> [--method X]` | Čtení X++ zdrojového kódu z AOT XML |
| `d365fo models list` / `d365fo models deps <Name>` | Seznam modelů nebo jejich závislostní graf |
| `d365fo generate table\|class\|coc\|form\|entity\|extension\|event-handler\|privilege\|duty\|role` | Scaffold nového AOT XML |
| `d365fo review diff` | AOT-sémantický diff git změn |
| `d365fo build` / `sync` / `test run` / `bp check` | MSBuild / SyncEngine / SysTestRunner / xppbp (Windows + VM) |

---

## Viz také

- [SETUP.md](SETUP.md) — instalace a konfigurace CLI.
- [EXAMPLES.md](EXAMPLES.md) — jeden příklad na každý příkaz.
- [TOKEN_ECONOMICS.md](TOKEN_ECONOMICS.md) — proč CLI šetří tokeny oproti MCP.
- [ARCHITECTURE.md](ARCHITECTURE.md) — vztah CLI, MCP adaptéru a Core vrstvy.
