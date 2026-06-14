using D365FO.Core;
using D365FO.Core.FormPatterns;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365FO.Cli.Commands.Get;

/// <summary>
/// Form pattern spec catalog — the unified MCP <c>form_pattern</c> tool's
/// <c>action=spec</c>. With no argument, lists every known
/// top-level pattern and container sub-pattern; with a name, returns the full
/// machine-readable spec (structure tree, versions, when-to-use, reference
/// forms, lifecycle guidance). Pure catalog data — needs no index.
/// </summary>
public sealed class GetFormPatternCommand : Command<GetFormPatternCommand.Settings>
{
    public sealed class Settings : D365OutputSettings
    {
        [CommandArgument(0, "[NAME]")]
        [System.ComponentModel.Description("Pattern id/xmlName (e.g. SimpleList, DetailsMaster) or sub-pattern (e.g. FieldsFieldGroups). Omit to list all.")]
        public string? Name { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);

        if (string.IsNullOrWhiteSpace(settings.Name))
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                patterns = FormPatternCatalog.Patterns.Select(p => new
                {
                    id = p.Id,
                    xmlName = p.XmlName,
                    displayName = p.DisplayName,
                    variantOf = p.VariantOf,
                    purpose = p.Purpose,
                    referenceForms = p.ReferenceForms,
                }),
                subPatterns = FormPatternCatalog.SubPatterns.Select(sp => new
                {
                    id = sp.Id,
                    xmlName = sp.XmlName,
                    displayName = sp.DisplayName,
                    appliesTo = sp.AppliesToControlTypes,
                    purpose = sp.Purpose,
                }),
                hint = "Run `d365fo get form-pattern <NAME>` for the full structural spec.",
            }), _ =>
            {
                var table = new Table().AddColumns("Pattern", "Display name", "Reference forms");
                foreach (var p in FormPatternCatalog.Patterns)
                    table.AddRow(p.XmlName, p.DisplayName, string.Join(", ", p.ReferenceForms));
                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"[grey]{FormPatternCatalog.SubPatterns.Count} container sub-patterns — `d365fo get form-pattern <NAME>` for details.[/]");
            });
        }

        var spec = FormPatternCatalog.Resolve(settings.Name);
        if (spec is not null)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                id = spec.Id,
                xmlName = spec.XmlName,
                xmlAliases = spec.XmlAliases,
                variantOf = spec.VariantOf,
                displayName = spec.DisplayName,
                versions = spec.Versions,
                purpose = spec.Purpose,
                whenToUse = spec.WhenToUse,
                whenNotToUse = spec.WhenNotToUse,
                referenceForms = spec.ReferenceForms,
                designProperties = spec.DesignProperties,
                requiresDataSource = spec.RequiresDataSource,
                structure = spec.Root.Select(Project),
                extraRootChildren = ProjectExtra(spec.ExtraRoot),
                lifecycleGuidance = spec.LifecycleGuidance,
                notes = spec.Notes,
                applicableSubPatterns = FormPatternCatalog.SubPatterns
                    .Where(sp => sp.ParentPatterns is null || sp.ParentPatterns.Contains(spec.Id))
                    .Select(sp => sp.XmlName),
            }));
        }

        var sub = FormPatternCatalog.ResolveSubPattern(settings.Name);
        if (sub is not null)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                id = sub.Id,
                xmlName = sub.XmlName,
                xmlAliases = sub.XmlAliases,
                displayName = sub.DisplayName,
                kind = "subPattern",
                versions = sub.Versions,
                appliesToControlTypes = sub.AppliesToControlTypes,
                parentPatterns = sub.ParentPatterns,
                purpose = sub.Purpose,
                referenceForms = sub.ReferenceForms,
                structure = sub.Root.Select(Project),
                extraRootChildren = ProjectExtra(sub.ExtraRoot),
                notes = sub.Notes,
            }));
        }

        return RenderHelpers.Render(kind, ToolResult<object>.Fail(
            "PATTERN_NOT_FOUND",
            $"Unknown form pattern or sub-pattern '{settings.Name}'.",
            $"Known patterns: {string.Join(", ", FormPatternCatalog.KnownPatternNames())}. Run `d365fo get form-pattern` to list everything."));
    }

    private static object Project(NodeSpec n) => new
    {
        id = n.Id,
        controlTypes = n.ControlTypes,
        occurrence = Camel(n.Occurrence.ToString()),
        nameHint = n.NameHint,
        properties = n.Properties,
        requiresSubPattern = n.RequiresSubPattern ? true : (bool?)null,
        allowedSubPatterns = n.AllowedSubPatterns,
        childrenOrdered = n.Children is { Count: > 0 } ? n.ChildrenOrdered : (bool?)null,
        children = n.Children?.Select(Project),
        extraChildren = ProjectExtra(n.Extra),
    };

    private static object ProjectExtra(ExtraChildren extra)
        => extra.IsAny ? "any" : extra.IsNone ? "none" : extra.Types!;

    private static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];
}
