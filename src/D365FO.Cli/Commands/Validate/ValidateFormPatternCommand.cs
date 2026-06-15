using D365FO.Core;
using D365FO.Core.FormPatterns;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365FO.Cli.Commands.Validate;

/// <summary>
/// Structural form-pattern validator over AxForm XML — the unified MCP
/// <c>object_patterns</c> tool's <c>domain=form, action=validate</c>. Checks the Design tree against the
/// curated pattern catalog (FP001-FP010); requires no index and no VM.
/// Exit codes: 0 = clean (or warnings only), 1 = command failure, 2 = errors found.
/// </summary>
public sealed class ValidateFormPatternCommand : Command<ValidateFormPatternCommand.Settings>
{
    public sealed class Settings : D365OutputSettings
    {
        [CommandArgument(0, "[FILE]")]
        [System.ComponentModel.Description("Path to the AxForm XML file to validate. Omit to read from stdin.")]
        public string? File { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);

        var (xml, error) = ValidateInput.ReadCode(settings.File);
        if (error is not null)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("INPUT_NOT_FOUND", error,
                "Pass a file path or pipe XML via stdin: `cat MyForm.xml | d365fo validate form-pattern`."));

        var report = FormPatternValidator.ValidateXml(xml!);

        var result = ToolResult<object>.Success(new
        {
            formName = report.FormName,
            pattern = report.Pattern,
            patternVersion = report.PatternVersion,
            enforced = D365FO.Cli.Commands.Generate.FormPatternGate.EnforcementEnabled,
            errors = report.ErrorCount,
            warnings = report.WarningCount,
            coverage = new
            {
                containersTotal = report.ContainersTotal,
                containersPatterned = report.ContainersPatterned,
            },
            violations = report.Violations.Select(v => new
            {
                rule = v.Rule,
                severity = v.Severity,
                path = v.Path,
                excerpt = v.Excerpt,
                fix = v.Fix,
            }),
            verdict = report.HasErrors
                ? "Structural pattern violations — fix before writing the form (these block `generate form` while D365FO_FORM_PATTERN_ENFORCE=true)."
                : report.WarningCount > 0
                    ? "No structural violations; address the recommendations where practical."
                    : "Form matches its declared pattern.",
        });

        var rc = RenderHelpers.Render(kind, result, _ =>
        {
            AnsiConsole.MarkupLine($"[bold]{RenderHelpers.Escape(report.FormName ?? "(form)")}[/] — pattern [blue]{RenderHelpers.Escape(report.Pattern ?? "(none)")}[/] {RenderHelpers.Escape(report.PatternVersion ?? "")}");
            foreach (var v in report.Violations)
            {
                var colour = v.Severity == "error" ? "red" : "yellow";
                AnsiConsole.MarkupLine($"[{colour}]{v.Rule}[/] {RenderHelpers.Escape(v.Path)}: {RenderHelpers.Escape(v.Excerpt)}");
                AnsiConsole.MarkupLine($"  [grey]{RenderHelpers.Escape(v.Fix)}[/]");
            }
            AnsiConsole.MarkupLine(report.HasErrors
                ? $"[red]{report.ErrorCount} error(s)[/], [yellow]{report.WarningCount} warning(s)[/]"
                : $"[green]clean[/] ({report.WarningCount} warning(s))");
        });
        return rc != 0 ? rc : report.HasErrors ? 2 : 0;
    }
}
