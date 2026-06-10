using System.Text.RegularExpressions;

namespace D365FO.Core.Validation;

/// <summary>One structured X++ compiler finding parsed from an xppc log line.</summary>
public sealed record XppcDiagnostic(
    string Severity,
    string? Kind,
    string? Model,
    string? Object,
    string? Member,
    int? Line,
    int? Column,
    string Message)
{
    /// <summary>Known-fix hint for the message, when the pattern is recognised.</summary>
    public string? Hint => XppcDiagnostics.FixHint(Message);
}

/// <summary>
/// Parser for xppc.exe compiler output — port of the upstream MCP server's
/// structured-diagnostics feature in <c>build_d365fo_project</c>.
///
/// xppc log lines have the form (standalone/UDE mode):
///   Compile Error: Class Method dynamics://MyModel/MyClass/myMethod: [(28,27),(28,28)]: ';' expected.
/// i.e. &lt;severity&gt;: &lt;element kind&gt; dynamics://&lt;model&gt;/&lt;object&gt;[/&lt;member&gt;]: [(line,col)[,(line,col)]]: &lt;message&gt;
/// </summary>
public static class XppcDiagnostics
{
    private static readonly Regex DiagLine = new(
        @"^(Compile Fatal Error|Compile Error|Compile Warning|Generation Warning|Best Practice Warning):\s*(?:(.*?)\s+)?dynamics://([^/\s:]+)/([^/\s:]+)(?:/([^\s:]+))?\s*:?\s*\[\((\d+),(\d+)\)(?:,\(\d+,\d+\))?\]\s*:\s*(.*)$",
        RegexOptions.Compiled);

    private static readonly Regex SimpleLine = new(
        @"^(Compile Fatal Error|Compile Error|Compile Warning|Generation Warning):\s*(.+)$",
        RegexOptions.Compiled);

    /// <summary>Stale incremental-build symbols — a full build is needed.</summary>
    public static bool IndicatesStaleSymbols(string logContent) =>
        Regex.IsMatch(logContent, "has not been successfully compiled since it was last changed|Do a Full Build",
            RegexOptions.IgnoreCase);

    public static IReadOnlyList<XppcDiagnostic> Parse(string logContent)
    {
        var diagnostics = new List<XppcDiagnostic>();
        foreach (var rawLine in logContent.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            var m = DiagLine.Match(line);
            if (m.Success)
            {
                diagnostics.Add(new XppcDiagnostic(
                    Severity: m.Groups[1].Value.Contains("Error") ? "error" : "warning",
                    Kind: m.Groups[2].Success && m.Groups[2].Value.Length > 0 ? m.Groups[2].Value : null,
                    Model: m.Groups[3].Value,
                    Object: m.Groups[4].Value,
                    Member: m.Groups[5].Success && m.Groups[5].Value.Length > 0 ? m.Groups[5].Value : null,
                    Line: int.Parse(m.Groups[6].Value),
                    Column: int.Parse(m.Groups[7].Value),
                    Message: m.Groups[8].Value.Trim()));
                continue;
            }
            var simple = SimpleLine.Match(line);
            if (simple.Success)
            {
                diagnostics.Add(new XppcDiagnostic(
                    Severity: simple.Groups[1].Value.Contains("Error") ? "error" : "warning",
                    Kind: null, Model: null, Object: null, Member: null, Line: null, Column: null,
                    Message: simple.Groups[2].Value.Trim()));
            }
        }
        return diagnostics;
    }

    /// <summary>
    /// Compact known-fix table for the most common xppc messages, so the agent
    /// can correct everything in one round instead of re-asking.
    /// </summary>
    public static string? FixHint(string message)
    {
        var m = message.ToLowerInvariant();
        if (m.Contains("';' expected"))
            return "Missing semicolon — check the statement at the reported line/column.";
        if (m.Contains("unknown type") || m.Contains("could not be found") || m.Contains("does not exist"))
            return "The identifier does not exist in metadata. Verify it with `d365fo search any <name>` / `d365fo validate references` — never guess names.";
        if (m.Contains("is not a valid method") || m.Contains("method not found"))
            return "Method missing on the type. Check the real method list with `d365fo get class <name>` or `d365fo get table <name>`.";
        if (m.Contains("does not denote a class"))
            return "[ExtensionOf] intrinsic mismatch — use tableStr() for tables, classStr() for classes, formStr() for forms.";
        if (m.Contains("label") && (m.Contains("not exist") || m.Contains("unknown")))
            return "Label id missing. Find it with `d365fo search label \"<text>\"` or create it via `d365fo label create`.";
        if (m.Contains("final") && m.Contains("extend"))
            return "The base class is final — CoC needs [Wrappable(true)] on the method or use an event handler instead.";
        if (m.Contains("expected") && m.Contains("but found"))
            return "Syntax error — re-check X++ syntax at the reported position (common after editing CDATA method bodies).";
        if (m.Contains("number of arguments"))
            return "Argument count mismatch — `d365fo validate references` reports the indexed signature arity before the compiler does.";
        return null;
    }
}
