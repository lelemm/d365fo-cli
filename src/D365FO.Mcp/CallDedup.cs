using System.Collections.Concurrent;

namespace D365FO.Mcp;

/// <summary>
/// Duplicate-call dedup cache (agentic-loop mitigation) — port of the upstream
/// MCP server's <c>callDedup</c> module.
///
/// A model stuck in a loop re-issues the same read call with identical
/// arguments. Read tools are served from a short-TTL cache on repeat — the
/// model gets the identical answer instantly (with a note) instead of
/// re-running DB queries. Write/stateful tools are excluded: repeated identical
/// calls are legitimate there (write retries after fixes, status polling).
/// </summary>
public static class CallDedup
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    public const int MaxEntries = 200;

    /// <summary>Tools whose repeated identical calls are legitimate — never dedup, never loop-hint.</summary>
    public static readonly HashSet<string> ExcludedTools = new(StringComparer.Ordinal)
    {
        // Write tools (`generate_object`, `labels`) are already excluded via
        // ToolCatalog.WriteTools; these are stateful reads whose answer can
        // change between identical calls (index freshness, workspace config).
        "index_status", "get_workspace_info", "index_history",
        // `prepare` issues a fresh provenance token on every call — never dedup.
        "prepare",
    };

    private sealed record Entry(string Body, bool IsError, DateTimeOffset At);

    private static readonly ConcurrentDictionary<string, Entry> Cache = new();

    public static string Key(string toolName, string argsJson) => toolName + "|" + argsJson;

    /// <summary>Returns the cached body (with a duplicate-call note) on a repeat read call.</summary>
    public static (string Body, bool IsError)? TryGet(string key)
    {
        if (!Cache.TryGetValue(key, out var entry)) return null;
        if (DateTimeOffset.UtcNow - entry.At > Ttl)
        {
            Cache.TryRemove(key, out _);
            return null;
        }
        return (entry.Body, entry.IsError);
    }

    public static void Store(string key, string body, bool isError)
    {
        if (Cache.Count >= MaxEntries)
        {
            // Drop the oldest ~25% — cheap pressure valve, exactness not needed.
            foreach (var stale in Cache.OrderBy(kv => kv.Value.At).Take(MaxEntries / 4).ToList())
                Cache.TryRemove(stale.Key, out _);
        }
        Cache[key] = new Entry(body, isError, DateTimeOffset.UtcNow);
    }

    /// <summary>Note appended to a deduped response so the agent notices the loop.</summary>
    public const string LoopHint =
        "\n\n[dedup] Identical call within 60 s — served from cache. " +
        "You already have this answer; use it instead of re-asking. " +
        "If you need different data, change the arguments.";

    /// <summary>Test hook.</summary>
    public static void Clear() => Cache.Clear();
}
