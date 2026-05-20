using D365FO.Core;
using D365FO.Core.Index;
using Spectre.Console.Cli;

namespace D365FO.Cli.Commands.Analyze;

/// <summary>
/// Change-impact analysis: given an AOT object name, produces a ranked list of
/// all downstream consumers that would be affected by a modification.
/// Covers: CoC wrappers, event handlers, AOT extensions, form datasources,
/// data entities, and queries.
/// </summary>
public sealed class AnalyzeImpactCommand : Command<AnalyzeImpactCommand.Settings>
{
    public sealed class Settings : D365OutputSettings
    {
        [CommandArgument(0, "<OBJECT>")]
        [System.ComponentModel.Description("AOT object name to analyse (Table, Class, Form, etc.).")]
        public string ObjectName { get; init; } = "";
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);

        if (string.IsNullOrWhiteSpace(settings.ObjectName))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "Object name required."));

        var report = RepoFactory.Create().AnalyzeImpact(settings.ObjectName);

        return RenderHelpers.Render(kind, ToolResult<object>.Success(new
        {
            objectName   = report.ObjectName,
            directCount  = report.CocWrappers.Count + report.EventHandlers.Count + report.Extensions.Count,
            indirectCount = report.FormDataSources.Count + report.DataEntities.Count + report.Queries.Count,
            direct = new
            {
                cocWrappers   = report.CocWrappers,
                eventHandlers = report.EventHandlers,
                extensions    = report.Extensions,
            },
            indirect = new
            {
                formDataSources = report.FormDataSources,
                dataEntities    = report.DataEntities,
                queries         = report.Queries,
            },
        }));
    }
}
