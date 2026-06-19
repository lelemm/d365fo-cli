using D365FO.Core;
using D365FO.Core.Validation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365FO.Cli.Commands.Validate;

/// <summary>
/// Offline X++ / XML Best Practice validator (&lt;50 ms, all-platform). Port of
/// the upstream MCP <c>validate_xpp</c> tool: checks generated code against the
/// D365FO rule canon without xppbp.exe or a Windows VM. Property rules
/// (XML002–XML005) are data-driven when PropertyStats have been mined during
/// <c>index extract</c>.
/// Exit codes: 0 = clean (or warnings only), 1 = command failure, 2 = errors found.
/// </summary>
public sealed class ValidateXppCommand : Command<ValidateXppCommand.Settings>
{
    public sealed class Settings : D365OutputSettings
    {
        [CommandArgument(0, "[FILE]")]
        [System.ComponentModel.Description("Path to the X++ / XML file to validate. Omit to read from stdin.")]
        public string? File { get; init; }

        [CommandOption("--code-type <TYPE>")]
        [System.ComponentModel.Description("xpp (default) for X++ source, xml-table for AxTable XML, xml-any for other XML. Auto-detected from file extension/content when omitted.")]
        public string? CodeType { get; init; }

        [CommandOption("--context <NAME>")]
        [System.ComponentModel.Description("Owning class/table name, used in diagnostic messages.")]
        public string? Context { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);
        return XppValidationRunner.Run(kind, settings.File, settings.CodeType, settings.Context);
    }
}

internal static class XppValidationRunner
{
    public static int Run(
        OutputMode.Kind kind,
        string? file,
        string? codeTypeRaw = null,
        string? context = null,
        string source = "offline-validator",
        IReadOnlyList<string>? fallbackWarnings = null)
    {
        var (code, error) = ValidateInput.ReadCode(file);
        if (error is not null)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("INPUT_NOT_FOUND", error,
                "Pass a file path or pipe code via stdin: `cat MyClass.xpp | d365fo validate xpp`."));

        var codeType = XppValidator.NormalizeCodeType(codeTypeRaw ?? DetectCodeType(file, code!));

        // Property rules read mined statistics when an index exists; validation
        // itself never requires one (static defaults apply otherwise).
        IPropertyStatsProvider? stats = null;
        try
        {
            var repo = RepoFactory.Create();
            if (repo.HasPropertyStats()) stats = repo;
        }
        catch { /* no index — static defaults */ }

        var violations = XppValidator.Validate(code!, codeType, stats);
        var errors = violations.Count(v => v.Severity == "error");
        var warningCount = violations.Count(v => v.Severity == "warning");

        var result = ToolResult<object>.Success(new
        {
            source,
            context,
            codeType,
            propertyRulesDataDriven = stats is not null,
            errors,
            warnings = warningCount,
            fallbackWarnings = fallbackWarnings ?? Array.Empty<string>(),
            violations = violations.Select(v => new
            {
                rule = v.Rule,
                severity = v.Severity,
                line = v.Line,
                excerpt = v.Excerpt,
                fix = v.Fix,
            }),
            verdict = errors > 0
                ? "Fix all errors before writing the file."
                : warningCount > 0
                    ? "Address warnings where practical, then proceed."
                    : "No violations found.",
        });

        var rc = RenderHelpers.Render(kind, result, _ =>
        {
            foreach (var v in violations)
            {
                var colour = v.Severity == "error" ? "red" : "yellow";
                var line = v.Line is { } l ? $" (line {l})" : "";
                AnsiConsole.MarkupLine($"[{colour}]{v.Rule}[/]{line} {RenderHelpers.Escape(v.Excerpt)}");
                AnsiConsole.MarkupLine($"  [grey]{RenderHelpers.Escape(v.Fix)}[/]");
            }
            AnsiConsole.MarkupLine(errors > 0
                ? $"[red]{errors} error(s)[/], [yellow]{warningCount} warning(s)[/]"
                : $"[green]clean[/] ({warningCount} warning(s))");
        });
        return rc != 0 ? rc : errors > 0 ? 2 : 0;
    }

    internal static string DetectCodeType(string? file, string code)
    {
        if (code.Contains("<AxTable", StringComparison.OrdinalIgnoreCase)) return XppValidator.CodeTypeXmlTable;
        var trimmed = code.TrimStart();
        if (trimmed.StartsWith("<?xml") || trimmed.StartsWith('<')) return XppValidator.CodeTypeXmlAny;
        if (file is not null && file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return XppValidator.CodeTypeXmlAny;
        return XppValidator.CodeTypeXpp;
    }
}

/// <summary>Shared file/stdin input reader for the validate commands.</summary>
internal static class ValidateInput
{
    public static (string? Code, string? Error) ReadCode(string? file)
    {
        if (!string.IsNullOrEmpty(file))
        {
            if (!File.Exists(file)) return (null, $"File not found: {file}");
            return (File.ReadAllText(file), null);
        }
        if (Console.IsInputRedirected)
        {
            var code = Console.In.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(code)) return (code, null);
        }
        return (null, "No input: pass a FILE argument or pipe code via stdin.");
    }
}
