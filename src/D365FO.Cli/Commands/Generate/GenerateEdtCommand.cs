using D365FO.Core;
using D365FO.Core.Scaffolding;
using Spectre.Console.Cli;

namespace D365FO.Cli.Commands.Generate;

/// <summary>Scaffolds an <c>AxEdt</c> Extended Data Type.</summary>
public sealed class GenerateEdtCommand : Command<GenerateEdtCommand.Settings>
{
    public sealed class Settings : GenerateSettings
    {
        [CommandArgument(0, "<NAME>")]
        [System.ComponentModel.Description("EDT name.")]
        public string Name { get; init; } = "";

        [CommandOption("--extends <BASE>")]
        [System.ComponentModel.Description("Parent EDT to extend (e.g. Name, AccountNum). Takes precedence over --base-type.")]
        public string? Extends { get; init; }

        [CommandOption("--base-type <TYPE>")]
        [System.ComponentModel.Description("Primitive type when no --extends is given. String (default) | Int | Int64 | Real | Date | UtcDateTime | Boolean. Infers a sensible standard parent EDT.")]
        public string? BaseType { get; init; }

        [CommandOption("--size <N>")]
        [System.ComponentModel.Description("StringSize override for string-based EDTs.")]
        public int? Size { get; init; }

        [CommandOption("--label <TEXT>")]
        [System.ComponentModel.Description("Label text or @File:Key label reference.")]
        public string? Label { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);

        if (string.IsNullOrWhiteSpace(settings.Name))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "EDT name required."));

        var hasInstall = !string.IsNullOrWhiteSpace(settings.InstallTo);
        var hasOut     = !string.IsNullOrWhiteSpace(settings.Out);
        if (!hasInstall && !hasOut)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "--out or --install-to is required."));

        var outPath = settings.Out;
        if (hasInstall && !hasOut)
        {
            outPath = GenerateInstaller.ResolveInstallPath(kind, "AxEdt", settings.Name, settings.InstallTo!, out var fail);
            if (fail.HasValue) return fail.Value;
        }

        var doc = XppScaffolder.Edt(settings.Name, settings.Extends, settings.BaseType, settings.Size, settings.Label);
        try
        {
            var res = ScaffoldFileWriter.Write(doc, outPath!, settings.Overwrite);
            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                kind       = "AxEdt",
                name       = settings.Name,
                extends    = settings.Extends,
                baseType   = settings.BaseType,
                stringSize = settings.Size,
                label      = settings.Label,
                path       = res.Path,
                bytes      = res.Bytes,
                backup     = res.BackupPath,
                model      = settings.InstallTo,
            }));
        }
        catch (Exception ex)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.WriteFailed, ex.Message));
        }
    }
}
