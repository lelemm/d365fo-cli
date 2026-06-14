using D365FO.Core;
using D365FO.Core.Scaffolding;
using Spectre.Console.Cli;

namespace D365FO.Cli.Commands.Generate;

/// <summary>Scaffolds an <c>AxEnum</c> base enumeration.</summary>
public sealed class GenerateEnumCommand : Command<GenerateEnumCommand.Settings>
{
    public sealed class Settings : GenerateSettings
    {
        [CommandArgument(0, "<NAME>")]
        [System.ComponentModel.Description("Enum name.")]
        public string Name { get; init; } = "";

        [CommandOption("--value <SPEC>")]
        [System.ComponentModel.Description("Repeatable: <name>:<intValue>[:<label>]. Example: --value None:0 --value Active:1:'Active record'")]
        public string[] Values { get; init; } = Array.Empty<string>();

        [CommandOption("--non-extensible")]
        [System.ComponentModel.Description("Emit IsExtensible=false (default is true, the recommended D365FO setting).")]
        public bool NonExtensible { get; init; }

        [CommandOption("--label <TEXT>")]
        [System.ComponentModel.Description("Enum-level label.")]
        public string? Label { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);

        if (string.IsNullOrWhiteSpace(settings.Name))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "Enum name required."));

        var hasInstall = !string.IsNullOrWhiteSpace(settings.InstallTo);
        var hasOut     = !string.IsNullOrWhiteSpace(settings.Out);
        if (!hasInstall && !hasOut)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "--out or --install-to is required."));

        var outPath = settings.Out;
        if (hasInstall && !hasOut)
        {
            outPath = GenerateInstaller.ResolveInstallPath(kind, "AxEnum", settings.Name, settings.InstallTo!, out var fail);
            if (fail.HasValue) return fail.Value;
        }

        var values = settings.Values.Select(ParseValue).ToList();
        var doc = XppScaffolder.Enum(settings.Name, values, !settings.NonExtensible, settings.Label);

        try
        {
            var res = ScaffoldFileWriter.Write(doc, outPath!, settings.Overwrite);
            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                kind         = "AxEnum",
                name         = settings.Name,
                isExtensible = !settings.NonExtensible,
                label        = settings.Label,
                valueCount   = values.Count,
                values       = values.Select(v => new { v.Name, v.IntValue, v.Label }).ToList(),
                path         = res.Path,
                bytes        = res.Bytes,
                backup       = res.BackupPath,
                model        = settings.InstallTo,
            }));
        }
        catch (Exception ex)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.WriteFailed, ex.Message));
        }
    }

    private static EnumValueSpec ParseValue(string raw)
    {
        // Format: <name>:<intValue>[:<label>]
        var parts = raw.Split(':', 3, StringSplitOptions.TrimEntries);
        var name  = parts.Length > 0 ? parts[0] : raw;
        var intVal = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
        var label  = parts.Length > 2 ? parts[2].Trim('\'', '"') : null;
        return new EnumValueSpec(name, intVal, string.IsNullOrEmpty(label) ? null : label);
    }
}
