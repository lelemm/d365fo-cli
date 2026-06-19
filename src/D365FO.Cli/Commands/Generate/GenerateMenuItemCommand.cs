using D365FO.Core;
using D365FO.Core.Scaffolding;
using Spectre.Console.Cli;
using System.Text.Json.Nodes;

namespace D365FO.Cli.Commands.Generate;

/// <summary>
/// Scaffolds an <c>AxMenuItemDisplay</c>, <c>AxMenuItemAction</c>, or
/// <c>AxMenuItemOutput</c> AOT object.
/// </summary>
public sealed class GenerateMenuItemCommand : Command<GenerateMenuItemCommand.Settings>
{
    public sealed class Settings : GenerateSettings
    {
        [CommandArgument(0, "<NAME>")]
        [System.ComponentModel.Description("Menu item name.")]
        public string Name { get; init; } = "";

        [CommandOption("--kind <KIND>")]
        [System.ComponentModel.Description("Display (default) | Action | Output.")]
        public string Kind { get; init; } = "Display";

        [CommandOption("--object <NAME>")]
        [System.ComponentModel.Description("Target object name (form, class, or report).")]
        public string? ObjectName { get; init; }

        [CommandOption("--object-type <TYPE>")]
        [System.ComponentModel.Description("Form (default) | Class | Report | Query.")]
        public string ObjectType { get; init; } = "Form";

        [CommandOption("--label <TEXT>")]
        [System.ComponentModel.Description("Label text or @File:Key label reference.")]
        public string? Label { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);

        if (string.IsNullOrWhiteSpace(settings.Name))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "Menu item name required."));
        if (string.IsNullOrWhiteSpace(settings.ObjectName))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "--object <NAME> required."));

        if (!TryParseKind(settings.Kind, out var menuKind))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput,
                $"Unknown --kind '{settings.Kind}'. Expected Display | Action | Output."));

        if (!TryParseObjectType(settings.ObjectType, out var objType))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput,
                $"Unknown --object-type '{settings.ObjectType}'. Expected Form | Class | Report | Query."));
        if (!GenerateBackendResolver.TryResolve(settings.Backend, out var backend, out var backendError))
            return GenerateBridgeScaffolding.RenderBackendError(kind, backendError!);
        var useBridge = GenerateBackendResolver.ShouldUseBridge(backend);

        var hasInstall = !string.IsNullOrWhiteSpace(settings.InstallTo);
        var hasOut     = !string.IsNullOrWhiteSpace(settings.Out);
        if (!hasInstall && !hasOut)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "--out or --install-to is required."));

        var axSubfolder = MenuItemScaffolder.AxSubfolder(menuKind);
        var outPath = settings.Out;
        if (hasInstall && !hasOut && !useBridge)
        {
            outPath = GenerateInstaller.ResolveInstallPath(kind, axSubfolder, settings.Name, settings.InstallTo!, out var fail);
            if (fail.HasValue) return fail.Value;
        }

        var menuProperties = new JsonObject
        {
            ["Object"] = settings.ObjectName,
            ["ObjectType"] = objType.ToString(),
        };
        if (!string.IsNullOrWhiteSpace(settings.Label)) menuProperties["Label"] = settings.Label;

        var bridge = GenerateBridgeScaffolding.TryWrite(
            kind, backend, settings.InstallTo, settings.Overwrite, axSubfolder, settings.Name, null, outPath,
            properties: menuProperties);
        if (bridge.Handled) return bridge.ExitCode;

        var doc = MenuItemScaffolder.MenuItem(menuKind, settings.Name, settings.ObjectName!, objType, settings.Label);
        try
        {
            var res = ScaffoldFileWriter.Write(doc, outPath!, settings.Overwrite);
            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                kind        = axSubfolder,
                name        = settings.Name,
                menuKind    = menuKind.ToString(),
                objectName  = settings.ObjectName,
                objectType  = objType.ToString(),
                label       = settings.Label,
                path        = res.Path,
                bytes       = res.Bytes,
                backup      = res.BackupPath,
                model       = settings.InstallTo,
            }));
        }
        catch (Exception ex)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.WriteFailed, ex.Message));
        }
    }

    private static bool TryParseKind(string raw, out MenuItemKind menuKind)
    {
        menuKind = raw.ToLowerInvariant() switch
        {
            "action"  => MenuItemKind.Action,
            "output"  => MenuItemKind.Output,
            "display" or "" => MenuItemKind.Display,
            _ => (MenuItemKind)(-1),
        };
        return (int)menuKind >= 0;
    }

    private static bool TryParseObjectType(string raw, out MenuItemObjectType objType)
    {
        objType = raw.ToLowerInvariant() switch
        {
            "class"          => MenuItemObjectType.Class,
            "report" or "ssrsreport" => MenuItemObjectType.Report,
            "query"          => MenuItemObjectType.Query,
            "form" or ""     => MenuItemObjectType.Form,
            _ => (MenuItemObjectType)(-1),
        };
        return (int)objType >= 0;
    }
}
