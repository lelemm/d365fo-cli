# Token Economics

> **TL;DR:** CLI + Skills saves **~80–92% of tool-plumbing tokens** compared to MCP over a typical 5–20 turn workflow.

---

## Why MCP is expensive

Every request loads all ~54 MCP tool schemas into context (~54 tokens each ≈ **~2,900 tokens/turn**). Over 20 turns: ~58,000 tokens burned before any useful work. MCP also encourages multi-step discovery flows (`find_type` → `get_metadata` → `call`) that compound with each round-trip.

## Why CLI + Skills is cheap

One shell tool (~100 tokens). Skills load only their YAML frontmatter (~30–60 tokens each) until triggered. Discovery is on demand via `d365fo schema`. Multi-step MCP flows collapse into one CLI call: `d365fo search batch`, `d365fo get object`, `d365fo find related`.

## Expected savings

Using the same per-turn accounting:

| Turns | MCP overhead | CLI + Skills overhead | Saving |
|---:|---:|---:|---:|
| 5  | ~14,500 | ~2,800 | ~81% |
| 10 | ~29,000 | ~3,500 | ~88% |
| 15 | ~44,000 | ~4,000 | ~91% |
| 20 | ~58,000 | ~4,500 | ~92% |

Real workflows save more, because MCP also pays for extra discovery round-trips
(often 5–15 kT per workflow) that the CLI eliminates via single calls such as
`d365fo get object table <Name>` or `d365fo search batch <A> <B> <C>`.

## When CLI does not save

| Situation | Recommended |
|---|---|
| AI host without a shell tool (Claude.ai web, ChatGPT web) | MCP — `D365FO.Mcp` adapter uses the same index |
| Single one-off lookup per session | Either (MCP warm connection has no startup cost) |
| Agent demands full generated XML back in context | Avoid — `d365fo generate` always writes to `--out`, returns JSON summary only |

## Sources

- Anthropic — [Equipping agents for the real world with Agent Skills](https://www.anthropic.com/engineering/equipping-agents-for-the-real-world-with-agent-skills), October 2025.
- Simon Willison — [Claude Skills are awesome, maybe a bigger deal than MCP](https://simonwillison.net/2025/Oct/16/claude-skills/), October 2025.
- seangalliher — [D365-erp-cli, "Why CLI over MCP?"](https://github.com/seangalliher/D365-erp-cli#why-cli-over-mcp).
---

## See also

- [EXAMPLES.md](EXAMPLES.md#agent-integration) — how to wire Skills and the CLI into each AI agent.
- [ARCHITECTURE.md](ARCHITECTURE.md) — the Metadata Bridge (case 4 above).
- [MIGRATION_FROM_MCP.md](MIGRATION_FROM_MCP.md) — decision tree for MCP users.
