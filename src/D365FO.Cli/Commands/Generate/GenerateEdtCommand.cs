using D365FO.Core;
using D365FO.Core.Scaffolding;
using Spectre.Console.Cli;
using System.Text.Json.Nodes;

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
        [System.ComponentModel.Description("Primitive type when no --extends is given. String (default) | Int | Int64 | Real | Date | UtcDateTime | Boolean | Time | Guid | Container | Enum. Infers a sensible standard parent EDT.")]
        public string? BaseType { get; init; }

        [CommandOption("--size <N>")]
        [System.ComponentModel.Description("StringSize override for string-based EDTs.")]
        public int? Size { get; init; }

        [CommandOption("--label <TEXT>")]
        [System.ComponentModel.Description("Label text or @File:Key label reference.")]
        public string? Label { get; init; }

        [CommandOption("--enum-type <XPPENUM>")]
        [System.ComponentModel.Description("Backing X++ enum name for Enum-type EDTs (e.g. NoYes). If omitted, inferred from --extends; otherwise not emitted.")]
        public string? EnumType { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);

        if (string.IsNullOrWhiteSpace(settings.Name))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "EDT name required."));
        if (!GenerateBackendResolver.TryResolve(settings.Backend, out var backend, out var backendError))
            return GenerateBridgeScaffolding.RenderBackendError(kind, backendError!);
        var useBridge = GenerateBackendResolver.ShouldUseBridge(backend);

        var hasInstall = !string.IsNullOrWhiteSpace(settings.InstallTo);
        var hasOut     = !string.IsNullOrWhiteSpace(settings.Out);
        if (!hasInstall && !hasOut)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "--out or --install-to is required."));

        var outPath = settings.Out;
        if (hasInstall && !hasOut && !useBridge)
        {
            outPath = GenerateInstaller.ResolveInstallPath(kind, "AxEdt", settings.Name, settings.InstallTo!, out var fail);
            if (fail.HasValue) return fail.Value;
        }

        var effectiveEnumType = settings.EnumType;
        if (string.IsNullOrWhiteSpace(effectiveEnumType) &&
            string.Equals(settings.BaseType, "Enum", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(settings.Extends))
        {
            // EnumType derivation from --extends is not always identity (e.g. AccessToSensitiveData -> NoYes).
            effectiveEnumType = ResolveEnumTypeFromExtendsChain(settings.Extends!);
        }

        var edtProperties = new JsonObject
        {
            ["type"] = "AxEdt" + ResolveConcreteTypeSuffix(settings.BaseType, settings.Extends),
        };
        if (!string.IsNullOrWhiteSpace(settings.Extends)) edtProperties["Extends"] = settings.Extends;
        if (!string.IsNullOrWhiteSpace(settings.Label)) edtProperties["Label"] = settings.Label;
        if (settings.Size.HasValue) edtProperties["StringSize"] = settings.Size.Value;
        if (!string.IsNullOrWhiteSpace(effectiveEnumType)) edtProperties["EnumType"] = effectiveEnumType;

        var bridge = GenerateBridgeScaffolding.TryWrite(
            kind, backend, settings.InstallTo, settings.Overwrite, "AxEdt", settings.Name, null, outPath,
            properties: edtProperties);
        if (bridge.Handled) return bridge.ExitCode;

        var doc = XppScaffolder.Edt(settings.Name, settings.Extends, settings.BaseType, settings.Size, settings.Label, effectiveEnumType);
        try
        {
            var res = ScaffoldFileWriter.Write(doc, outPath!, settings.Overwrite);
            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                kind       = "AxEdt",
                name       = settings.Name,
                extends    = settings.Extends,
                baseType   = settings.BaseType,
                enumType   = effectiveEnumType,
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

    private static string? ResolveEnumTypeFromExtendsChain(string extendsName)
    {
        if (string.IsNullOrWhiteSpace(extendsName))
            return null;

        try
        {
            var repo = RepoFactory.Create();
            var current = extendsName;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Follow extends links defensively; break on cycles or unreasonable depth.
            for (var depth = 0; depth < 16 && !string.IsNullOrWhiteSpace(current); depth++)
            {
                if (!visited.Add(current)) break;

                var edt = repo.GetEdt(current);
                if (edt is null)
                {
                    // Defer to scaffolder inference (e.g. NoYesId -> NoYes) when chain lookup is unavailable.
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(edt.EnumType))
                    return edt.EnumType!;

                current = edt.Extends;
            }
        }
        catch
        {
            // Best-effort inference; fallback handled below.
        }

        return null;
    }

    private static string ResolveConcreteTypeSuffix(string? baseType, string? extends) =>
        !string.IsNullOrEmpty(baseType)
            ? baseType.ToLowerInvariant() switch
            {
                "int" or "integer" => "Int",
                "int64" => "Int64",
                "real" => "Real",
                "date" => "Date",
                "utcdatetime" or "datetime" => "UtcDateTime",
                "boolean" or "bool" => "Enum",
                "time" => "Time",
                "guid" => "Guid",
                "container" => "Container",
                "enum" => "Enum",
                _ => "String",
            }
            : InferConcreteTypeSuffixFromExtends(extends);

    private static string InferConcreteTypeSuffixFromExtends(string? extends) =>
        extends?.ToLowerInvariant() switch
        {
            "integer" or "int" => "Int",
            "int64" or "recid" => "Int64",
            "amount" or "amountmst" or "qty" or "weight" or "real" => "Real",
            "date" or "transdate" => "Date",
            "utcdatetime" or "transdatetime" => "UtcDateTime",
            "noyes" or "noyesid" or "boolean" => "Enum",
            "timeofday" or "time" => "Time",
            "guid" => "Guid",
            "container" => "Container",
            _ => "String",
        } ?? "String";
}
