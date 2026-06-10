using D365FO.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365FO.Cli.Commands.Ops;

internal sealed record DoctorCheck(string Name, bool Ok, string? Detail);

public sealed class DoctorCommand : Command<DoctorCommand.Settings>
{
    public sealed class Settings : D365OutputSettings { }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);
        var cfg = D365FoSettings.FromEnvironment();
        var checks = new List<DoctorCheck>();

        void Add(string name, bool ok, string? detail = null) => checks.Add(new DoctorCheck(name, ok, detail));

        Add("config.databasePath resolvable", !string.IsNullOrEmpty(cfg.DatabasePath), cfg.DatabasePath);
        Add("config.packagesPath set", !string.IsNullOrEmpty(cfg.StandardPackagesPath),
            cfg.StandardPackagesPath ?? "Set D365FO_STANDARD_PACKAGES_PATH or use --packages.");
        Add("config.workspacePath set", !string.IsNullOrEmpty(cfg.WorkspacePath),
            cfg.WorkspacePath ?? "Set D365FO_WORKSPACE_PATH to enable scaffold output.");
        Add("index db exists", File.Exists(cfg.DatabasePath),
            File.Exists(cfg.DatabasePath) ? null : "Run 'd365fo index build'.");
        Add("runtime", true, $".NET {Environment.Version} on {Environment.OSVersion.Platform}");
        Add("platform.windows (build/sync/bp require it)",
            OperatingSystem.IsWindows(),
            OperatingSystem.IsWindows() ? null : "Non-Windows host: write-ops (build, sync, bp, test) are unavailable.");

        // Index freshness — the index is the single source of truth for
        // grounding, but only while it reflects the current workspace.
        if (File.Exists(cfg.DatabasePath) && !string.IsNullOrEmpty(cfg.StandardPackagesPath))
        {
            try
            {
                var repo = RepoFactory.Create();
                var roots = new List<string> { cfg.StandardPackagesPath! };
                roots.AddRange(cfg.CustomPackagesPaths);
                var staleness = D365FO.Core.Index.IndexStaleness.Check(repo, roots);
                Add("index freshness (stale-index)", !staleness.IsStale,
                    staleness.IsStale ? staleness.Detail : $"index extracted {staleness.LastExtractedUtc:O}");
            }
            catch (Exception ex)
            {
                Add("index freshness (stale-index)", true, $"check skipped: {ex.Message}");
            }
        }

        var allOk = checks.All(c => c.Ok);
        var payload = allOk
            ? ToolResult<object>.Success(new { allOk, checks })
            : ToolResult<object>.Fail("DOCTOR_FAILED", "One or more checks failed.",
                "Re-run with --output json and inspect 'error.hint' or 'data.checks'.");

        return RenderHelpers.Render(kind,
            allOk ? payload : ToolResult<object>.Success(new { allOk, checks }), _ =>
        {
            foreach (var c in checks)
            {
                var tick = c.Ok ? "[green]✓[/]" : "[red]✗[/]";
                var detail = c.Detail is null ? "" : $" [grey]— {RenderHelpers.Escape(c.Detail)}[/]";
                AnsiConsole.MarkupLine($"{tick} {RenderHelpers.Escape(c.Name)}{detail}");
            }
            AnsiConsole.MarkupLine(allOk ? "[green]All checks passed.[/]" : "[red]Some checks failed.[/]");
        });
    }
}

public sealed class VersionCommand : Command<VersionCommand.Settings>
{
    public sealed class Settings : D365OutputSettings { }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);
        var asm = typeof(VersionCommand).Assembly.GetName();
        var payload = ToolResult<object>.Success(new
        {
            name = "d365fo",
            version = asm.Version?.ToString() ?? "0.1.0-dev",
            runtime = $".NET {Environment.Version}",
            os = Environment.OSVersion.ToString(),
        });
        return RenderHelpers.Render(kind, payload);
    }
}
