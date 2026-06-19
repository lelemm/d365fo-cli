using D365FO.Core;
using D365FO.Core.Scaffolding;
using D365FO.Cli.Commands.Get;
using Spectre.Console.Cli;
using System.Text.Json.Nodes;

namespace D365FO.Cli.Commands.Generate;

/// <summary>Scaffolds an <c>AxDataEntityView</c>. ROADMAP §6.</summary>
public sealed class GenerateEntityCommand : Command<GenerateEntityCommand.Settings>
{
    public sealed class Settings : GenerateSettings
    {
        [CommandArgument(0, "<ENTITY>")]
        public string EntityName { get; init; } = "";

        [CommandOption("--table <TABLE>")]
        [System.ComponentModel.Description("Root data source table for the entity.")]
        public string? Table { get; init; }

        [CommandOption("--public-entity <NAME>")]
        public string? PublicEntity { get; init; }

        [CommandOption("--public-collection <NAME>")]
        public string? PublicCollection { get; init; }

        [CommandOption("--field <SPEC>")]
        [System.ComponentModel.Description("Repeatable: <name>[[:<dataField>[[:mandatory]]]].")]
        public string[] Fields { get; init; } = Array.Empty<string>();

        [CommandOption("--all-fields")]
        [System.ComponentModel.Description("Populate <Fields /> from the source table's columns. Requires the table to be indexed.")]
        public bool AllFields { get; init; }

        [CommandOption("--wizard-json <JSON_OR_PATH>")]
        [System.ComponentModel.Description("Wizard step JSON object or path. CLI options override matching step values.")]
        public string? WizardJson { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);
        if (string.IsNullOrWhiteSpace(settings.EntityName))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "Entity name required."));
        if (!GenerateBridgeScaffolding.TryLoadWizardSteps(settings.WizardJson, out var wizardSteps, out var wizardJsonError))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, wizardJsonError!));
        if (!GenerateBackendResolver.TryResolve(settings.Backend, out var backend, out var backendError))
            return GenerateBridgeScaffolding.RenderBackendError(kind, backendError!);
        var useBridge = GenerateBackendResolver.ShouldUseBridge(backend);

        var table = settings.Table
            ?? ReadStepString(wizardSteps, "table", "rootTable", "rootDataSource");
        if (string.IsNullOrWhiteSpace(table))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "--table <TABLE> required."));
        var hasInstall = !string.IsNullOrWhiteSpace(settings.InstallTo);
        var hasOut = !string.IsNullOrWhiteSpace(settings.Out);
        if (!hasInstall && !hasOut)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "--out or --install-to is required."));
        var outPath = settings.Out;
        if (hasInstall && !hasOut && !useBridge)
        {
            outPath = GenerateInstaller.ResolveInstallPath(kind, "AxDataEntityView", settings.EntityName, settings.InstallTo!, out var fail);
            if (fail.HasValue) return fail.Value;
        }

        var fields = settings.Fields.Select(ParseField).ToList();
        var autoFromTable = false;
        if (fields.Count == 0 && settings.AllFields)
        {
            var repo = RepoFactory.Create();
            var tableDetails = repo.GetTableDetails(table!);
            if (tableDetails is null)
                return RenderHelpers.Render(kind, ToolResult<object>.Fail(
                    D365FoErrorCodes.TableNotFound,
                    $"Table '{table}' not found in the index. Extract the model first or pass explicit --field <SPEC>."));
            foreach (var f in tableDetails.Fields)
                fields.Add(new EntityFieldSpec(f.Name, f.Name, f.Mandatory));
            autoFromTable = true;
        }
        if (useBridge)
        {
            var operation = hasInstall && !hasOut ? "create" : "render";
            var bridgeSteps = (JsonObject)wizardSteps.DeepClone();
            bridgeSteps["table"] = table;
            if (!string.IsNullOrWhiteSpace(settings.PublicEntity)) bridgeSteps["publicEntityName"] = settings.PublicEntity;
            if (!string.IsNullOrWhiteSpace(settings.PublicCollection)) bridgeSteps["publicCollectionName"] = settings.PublicCollection;
            if (fields.Count > 0 || settings.Fields.Length > 0 || settings.AllFields)
            {
                bridgeSteps["fields"] = ToFieldArray(fields, table!);
            }

            var bridgeArgs = new JsonObject
            {
                ["name"] = settings.EntityName,
                ["operation"] = operation,
                ["overwrite"] = settings.Overwrite,
                ["steps"] = bridgeSteps,
            };
            if (string.Equals(operation, "create", StringComparison.OrdinalIgnoreCase))
            {
                bridgeArgs["model"] = settings.InstallTo;
            }

            var (ok, error, result) = BridgeGate.TryRunWizard("runDataEntityWizard", bridgeArgs);
            if (!ok)
            {
                return RenderHelpers.Render(kind, ToolResult<object>.Fail(
                    "BRIDGE_WIZARD_FAILED",
                    error ?? "Data entity wizard failed.",
                    "Use --backend legacy only if you need the old XML scaffolder."));
            }

            var warnings = GenerateBridgeScaffolding.MergeWarnings(null, result);
            if (operation == "render")
            {
                var files = GenerateBridgeScaffolding.GetWizardFiles(result);
                var file = GenerateBridgeScaffolding.FindWizardFile(files, "dataEntityView", settings.EntityName);
                if (file is null)
                {
                    return RenderHelpers.Render(kind, ToolResult<object>.Fail(
                        "BRIDGE_WIZARD_FAILED",
                        "Bridge data entity wizard succeeded but did not return AxDataEntityView XML."));
                }

                try
                {
                    var res = ScaffoldFileWriter.Write(file.Xml, outPath!, settings.Overwrite);
                    return RenderHelpers.Render(kind, ToolResult<object>.Success(new
                    {
                        kind = "AxDataEntityView",
                        name = settings.EntityName,
                        table,
                        path = res.Path,
                        bytes = res.Bytes,
                        backup = res.BackupPath,
                        fieldCount = fields.Count > 0 ? fields.Count : ((JsonArray?)bridgeSteps["fields"])?.Count ?? 0,
                        fieldsFromTable = autoFromTable,
                        model = settings.InstallTo,
                        backend = "bridge",
                        source = (string?)result?["source"] ?? "vs-extension-wizard",
                    }, warnings));
                }
                catch (Exception ex)
                {
                    return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.WriteFailed, ex.Message));
                }
            }

            var createdFiles = GenerateBridgeScaffolding.GetWizardFiles(result);
            var createdEntity = GenerateBridgeScaffolding.FindWizardFile(createdFiles, "dataEntityView", settings.EntityName);
            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                kind = "AxDataEntityView",
                name = settings.EntityName,
                table,
                path = createdEntity?.Path,
                model = (string?)result?["model"] ?? settings.InstallTo,
                backend = "bridge",
                source = (string?)result?["source"] ?? "vs-extension-wizard",
                operation = (string?)result?["operation"] ?? operation,
                fieldCount = fields.Count > 0 ? fields.Count : ((JsonArray?)bridgeSteps["fields"])?.Count ?? 0,
                fieldsFromTable = autoFromTable,
            }, warnings));
        }

        var doc = XppScaffolder.DataEntity(
            settings.EntityName, table!, settings.PublicEntity, settings.PublicCollection, fields);
        try
        {
            var res = ScaffoldFileWriter.Write(doc, outPath!, settings.Overwrite);
            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                kind = "AxDataEntityView",
                name = settings.EntityName,
                table,
                path = res.Path,
                bytes = res.Bytes,
                backup = res.BackupPath,
                fieldCount = fields.Count,
                fieldsFromTable = autoFromTable,
                model = settings.InstallTo,
            }));
        }
        catch (Exception ex)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.WriteFailed, ex.Message));
        }
    }

    private static EntityFieldSpec ParseField(string raw)
    {
        var parts = raw.Split(':', StringSplitOptions.TrimEntries);
        var name = parts.Length > 0 ? parts[0] : "";
        var data = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
        var mandatory = parts.Length > 2 && string.Equals(parts[2], "mandatory", StringComparison.OrdinalIgnoreCase);
        return new EntityFieldSpec(name, data, mandatory);
    }

    private static string? ReadStepString(JsonObject steps, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = (string?)steps[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static JsonArray ToFieldArray(IEnumerable<EntityFieldSpec> fields, string table)
    {
        var array = new JsonArray();
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name))
            {
                continue;
            }

            array.Add(new JsonObject
            {
                ["name"] = field.Name,
                ["dataSource"] = table,
                ["dataField"] = string.IsNullOrWhiteSpace(field.DataField) ? field.Name : field.DataField,
                ["mandatory"] = field.IsMandatory,
            });
        }

        return array;
    }
}

/// <summary>Scaffolds <c>AxTableExtension</c> / <c>AxFormExtension</c> / <c>AxEdtExtension</c> / <c>AxEnumExtension</c>. ROADMAP §6.</summary>
public sealed class GenerateExtensionCommand : Command<GenerateExtensionCommand.Settings>
{
    public sealed class Settings : GenerateSettings
    {
        [CommandArgument(0, "<KIND>")]
        [System.ComponentModel.Description("Extension kind: Table, Form, Edt, Enum.")]
        public string Kind { get; init; } = "";

        [CommandArgument(1, "<TARGET>")]
        public string Target { get; init; } = "";

        [CommandOption("--suffix <SUFFIX>")]
        [System.ComponentModel.Description("Extension suffix. Defaults to the InstallTo model name or 'Extension'.")]
        public string? Suffix { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);
        if (string.IsNullOrWhiteSpace(settings.Kind))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "Extension kind required."));
        if (string.IsNullOrWhiteSpace(settings.Target))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "Target object required."));
        var hasInstall = !string.IsNullOrWhiteSpace(settings.InstallTo);
        var hasOut = !string.IsNullOrWhiteSpace(settings.Out);
        if (!hasInstall && !hasOut)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "--out or --install-to is required."));
        if (!GenerateBackendResolver.TryResolve(settings.Backend, out var backend, out var backendError))
            return GenerateBridgeScaffolding.RenderBackendError(kind, backendError!);
        var useBridge = GenerateBackendResolver.ShouldUseBridge(backend);

        var suffix = settings.Suffix
            ?? (hasInstall ? settings.InstallTo! : "Extension");
        var axFolder = settings.Kind switch
        {
            "Table" => "AxTableExtension",
            "Form" => "AxFormExtension",
            "Edt" => "AxEdtExtension",
            "Enum" => "AxEnumExtension",
            _ => null,
        };
        if (axFolder is null)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput,
                $"Unsupported extension kind: {settings.Kind}. Expected Table|Form|Edt|Enum."));

        var fullName = $"{settings.Target}.{suffix}";
        var outPath = settings.Out;
        if (hasInstall && !hasOut && !useBridge)
        {
            outPath = GenerateInstaller.ResolveInstallPath(kind, axFolder, fullName, settings.InstallTo!, out var fail);
            if (fail.HasValue) return fail.Value;
        }

        var doc = XppScaffolder.Extension(settings.Kind, settings.Target, suffix);

        // Grounding gate: the target object must exist in the index; fail
        // closed under D365FO_GROUNDING_ENFORCE=true.
        var gate = GroundingGate.Check(settings.GroundingToken, settings.Target, doc,
            requiredSymbols: new[] { settings.Target });
        if (gate.Failure is not null) return RenderHelpers.Render(kind, gate.Failure);

        var bridge = GenerateBridgeScaffolding.TryWrite(
            kind, backend, settings.InstallTo, settings.Overwrite, axFolder, fullName, doc.ToString(), outPath,
            gate.Warnings.Count > 0 ? gate.Warnings : null);
        if (bridge.Handled) return bridge.ExitCode;

        try
        {
            var res = ScaffoldFileWriter.Write(doc, outPath!, settings.Overwrite);
            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                kind = axFolder,
                name = fullName,
                target = settings.Target,
                suffix,
                path = res.Path,
                bytes = res.Bytes,
                backup = res.BackupPath,
                model = settings.InstallTo,
                grounding = gate.Grounding,
            }, warnings: gate.Warnings.Count > 0 ? gate.Warnings : null));
        }
        catch (Exception ex)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.WriteFailed, ex.Message));
        }
    }
}

/// <summary>Scaffolds an event-handler subscriber class. ROADMAP §6.</summary>
public sealed class GenerateEventHandlerCommand : Command<GenerateEventHandlerCommand.Settings>
{
    public sealed class Settings : GenerateSettings
    {
        [CommandArgument(0, "<CLASS_NAME>")]
        public string ClassName { get; init; } = "";

        [CommandOption("--source-kind <KIND>")]
        [System.ComponentModel.Description("Form | FormDataSource | Table | Class.")]
        public string SourceKind { get; init; } = "Form";

        [CommandOption("--source-object <NAME>")]
        public string? SourceObject { get; init; }

        [CommandOption("--event <NAME>")]
        [System.ComponentModel.Description("E.g. OnInitialized / OnValidatingWrite / PostActivate.")]
        public string Event { get; init; } = "OnInitialized";

        [CommandOption("--method <NAME>")]
        public string HandlerMethod { get; init; } = "OnEvent";
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);
        if (string.IsNullOrWhiteSpace(settings.ClassName))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "Class name required."));
        if (string.IsNullOrWhiteSpace(settings.SourceObject))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "--source-object required."));

        var doc = XppScaffolder.EventHandler(settings.ClassName, settings.SourceKind, settings.SourceObject!, settings.Event, settings.HandlerMethod);

        // Grounding gate: the event source object must exist in the index;
        // fail closed under D365FO_GROUNDING_ENFORCE=true.
        var gate = GroundingGate.Check(settings.GroundingToken, settings.SourceObject!, doc,
            requiredSymbols: new[] { settings.SourceObject! });
        if (gate.Failure is not null) return RenderHelpers.Render(kind, gate.Failure);

        return GenerateInstaller.Emit(
            kind, "class", "AxClass", settings.ClassName,
            settings.InstallTo, settings.Out, settings.Overwrite, doc,
            r => new
            {
                kind = "AxClass",
                role = "EventHandler",
                name = settings.ClassName,
                sourceKind = settings.SourceKind,
                sourceObject = settings.SourceObject,
                @event = settings.Event,
                method = settings.HandlerMethod,
                source = r.Source,
                path = r.Path,
                bytes = r.Bytes,
                backup = r.Backup,
                model = settings.InstallTo,
                grounding = gate.Grounding,
            },
            gate.Warnings.Count > 0 ? gate.Warnings.ToList() : null);
    }
}

/// <summary>Scaffolds a security privilege granting a single entry point. ROADMAP §6.</summary>
public sealed class GeneratePrivilegeCommand : Command<GeneratePrivilegeCommand.Settings>
{
    public sealed class Settings : GenerateSettings
    {
        [CommandArgument(0, "<NAME>")]
        public string Name { get; init; } = "";

        [CommandOption("--entry-point <NAME>")]
        public string? EntryPoint { get; init; }

        [CommandOption("--entry-kind <KIND>")]
        [System.ComponentModel.Description("MenuItemDisplay | MenuItemAction | MenuItemOutput | WebMenuItem.")]
        public string EntryKind { get; init; } = "MenuItemDisplay";

        [CommandOption("--entry-object <NAME>")]
        [System.ComponentModel.Description("Target object name when different from --entry-point.")]
        public string? EntryObject { get; init; }

        [CommandOption("--access <LEVEL>")]
        [System.ComponentModel.Description("NoAccess | Read | Update | Create | Correct | Delete.")]
        public string Access { get; init; } = "Read";

        [CommandOption("--label <TEXT>")]
        public string? Label { get; init; }

        [CommandOption("--into-role <PATH>")]
        [System.ComponentModel.Description("Path to an existing AxSecurityRole XML; after scaffolding, merge this privilege's Name into the role's <Privileges>.")]
        public string? IntoRole { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);
        if (string.IsNullOrWhiteSpace(settings.Name))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "Privilege name required."));
        if (string.IsNullOrWhiteSpace(settings.EntryPoint))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "--entry-point required."));
        if (!GenerateBackendResolver.TryResolve(settings.Backend, out var backend, out var backendError))
            return GenerateBridgeScaffolding.RenderBackendError(kind, backendError!);
        var useBridge = GenerateBackendResolver.ShouldUseBridge(backend);
        if (useBridge && backend == GenerateBackend.Bridge && !string.IsNullOrWhiteSpace(settings.IntoRole))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(
                D365FoErrorCodes.BadInput,
                "--backend bridge does not support --into-role; use --backend legacy for merge-style role edits."));
        useBridge = useBridge && string.IsNullOrWhiteSpace(settings.IntoRole);
        var hasInstall = !string.IsNullOrWhiteSpace(settings.InstallTo);
        var hasOut = !string.IsNullOrWhiteSpace(settings.Out);
        if (!hasInstall && !hasOut)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "--out or --install-to is required."));
        var outPath = settings.Out;
        if (hasInstall && !hasOut && !useBridge)
        {
            outPath = GenerateInstaller.ResolveInstallPath(kind, "AxSecurityPrivilege", settings.Name, settings.InstallTo!, out var fail);
            if (fail.HasValue) return fail.Value;
        }

        var doc = XppScaffolder.Privilege(settings.Name, settings.EntryPoint!, settings.EntryKind, settings.EntryObject, settings.Access, settings.Label);
        var bridge = GenerateBridgeScaffolding.TryWrite(
            kind, useBridge ? backend : GenerateBackend.Legacy, settings.InstallTo, settings.Overwrite, "AxSecurityPrivilege", settings.Name, doc.ToString(), outPath);
        if (bridge.Handled) return bridge.ExitCode;
        try
        {
            var res = ScaffoldFileWriter.Write(doc, outPath!, settings.Overwrite);
            object? intoRole = null;
            if (!string.IsNullOrWhiteSpace(settings.IntoRole))
            {
                if (SecurityRoleMerge.AddReferences(settings.IntoRole!, duties: null, privileges: new[] { settings.Name }, out var mergeResult, out var err))
                {
                    intoRole = mergeResult;
                }
                else
                {
                    return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.WriteFailed, err!));
                }
            }
            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                kind = "AxSecurityPrivilege",
                name = settings.Name,
                entryPoint = settings.EntryPoint,
                entryKind = settings.EntryKind,
                access = settings.Access,
                path = res.Path,
                bytes = res.Bytes,
                backup = res.BackupPath,
                model = settings.InstallTo,
                intoRole,
            }));
        }
        catch (Exception ex)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.WriteFailed, ex.Message));
        }
    }
}

/// <summary>Scaffolds a security duty grouping one or more privileges. ROADMAP §6.</summary>
public sealed class GenerateDutyCommand : Command<GenerateDutyCommand.Settings>
{
    public sealed class Settings : GenerateSettings
    {
        [CommandArgument(0, "<NAME>")]
        public string Name { get; init; } = "";

        [CommandOption("--privilege <NAME>")]
        [System.ComponentModel.Description("Repeatable. Privileges to aggregate under this duty.")]
        public string[] Privileges { get; init; } = Array.Empty<string>();

        [CommandOption("--label <TEXT>")]
        public string? Label { get; init; }

        [CommandOption("--into-role <PATH>")]
        [System.ComponentModel.Description("Path to an existing AxSecurityRole XML; after scaffolding, merge this duty's Name into the role's <Duties>.")]
        public string? IntoRole { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);
        if (string.IsNullOrWhiteSpace(settings.Name))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "Duty name required."));
        if (settings.Privileges.Length == 0)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "At least one --privilege required."));
        if (!GenerateBackendResolver.TryResolve(settings.Backend, out var backend, out var backendError))
            return GenerateBridgeScaffolding.RenderBackendError(kind, backendError!);
        var useBridge = GenerateBackendResolver.ShouldUseBridge(backend);
        if (useBridge && backend == GenerateBackend.Bridge && !string.IsNullOrWhiteSpace(settings.IntoRole))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(
                D365FoErrorCodes.BadInput,
                "--backend bridge does not support --into-role; use --backend legacy for merge-style role edits."));
        useBridge = useBridge && string.IsNullOrWhiteSpace(settings.IntoRole);
        var hasInstall = !string.IsNullOrWhiteSpace(settings.InstallTo);
        var hasOut = !string.IsNullOrWhiteSpace(settings.Out);
        if (!hasInstall && !hasOut)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "--out or --install-to is required."));
        var outPath = settings.Out;
        if (hasInstall && !hasOut && !useBridge)
        {
            outPath = GenerateInstaller.ResolveInstallPath(kind, "AxSecurityDuty", settings.Name, settings.InstallTo!, out var fail);
            if (fail.HasValue) return fail.Value;
        }

        var doc = XppScaffolder.Duty(settings.Name, settings.Privileges, settings.Label);
        var bridge = GenerateBridgeScaffolding.TryWrite(
            kind, useBridge ? backend : GenerateBackend.Legacy, settings.InstallTo, settings.Overwrite, "AxSecurityDuty", settings.Name, doc.ToString(), outPath);
        if (bridge.Handled) return bridge.ExitCode;
        try
        {
            var res = ScaffoldFileWriter.Write(doc, outPath!, settings.Overwrite);
            object? intoRole = null;
            if (!string.IsNullOrWhiteSpace(settings.IntoRole))
            {
                if (SecurityRoleMerge.AddReferences(settings.IntoRole!, duties: new[] { settings.Name }, privileges: null, out var mergeResult, out var err))
                {
                    intoRole = mergeResult;
                }
                else
                {
                    return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.WriteFailed, err!));
                }
            }
            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                kind = "AxSecurityDuty",
                name = settings.Name,
                privilegeCount = settings.Privileges.Length,
                privileges = settings.Privileges,
                path = res.Path,
                bytes = res.Bytes,
                backup = res.BackupPath,
                model = settings.InstallTo,
                intoRole,
            }));
        }
        catch (Exception ex)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.WriteFailed, ex.Message));
        }
    }
}

/// <summary>
/// Scaffolds a new <c>AxSecurityRole</c> or, with <c>--add-to</c>, appends duty /
/// privilege references to an existing role document. ROADMAP §6.
/// </summary>
public sealed class GenerateRoleCommand : Command<GenerateRoleCommand.Settings>
{
    public sealed class Settings : GenerateSettings
    {
        [CommandArgument(0, "<NAME>")]
        public string Name { get; init; } = "";

        [CommandOption("--duty <NAME>")]
        [System.ComponentModel.Description("Repeatable. Duties referenced by this role.")]
        public string[] Duties { get; init; } = Array.Empty<string>();

        [CommandOption("--privilege <NAME>")]
        [System.ComponentModel.Description("Repeatable. Privileges referenced directly by this role.")]
        public string[] Privileges { get; init; } = Array.Empty<string>();

        [CommandOption("--label <TEXT>")]
        public string? Label { get; init; }

        [CommandOption("--description <TEXT>")]
        public string? Description { get; init; }

        [CommandOption("--add-to <PATH>")]
        [System.ComponentModel.Description("Path to an existing AxSecurityRole XML file; duties/privileges are merged in-place instead of creating a new file.")]
        public string? AddTo { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);

        if (!string.IsNullOrWhiteSpace(settings.AddTo))
            return ExecuteAddTo(kind, settings);

        if (string.IsNullOrWhiteSpace(settings.Name))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "Role name required."));
        if (settings.Duties.Length == 0 && settings.Privileges.Length == 0)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(
                D365FoErrorCodes.BadInput,
                "At least one --duty or --privilege required."));
        if (!GenerateBackendResolver.TryResolve(settings.Backend, out var backend, out var backendError))
            return GenerateBridgeScaffolding.RenderBackendError(kind, backendError!);
        var useBridge = GenerateBackendResolver.ShouldUseBridge(backend);

        var hasInstall = !string.IsNullOrWhiteSpace(settings.InstallTo);
        var hasOut = !string.IsNullOrWhiteSpace(settings.Out);
        if (!hasInstall && !hasOut)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "--out or --install-to is required."));
        var outPath = settings.Out;
        if (hasInstall && !hasOut && !useBridge)
        {
            outPath = GenerateInstaller.ResolveInstallPath(kind, "AxSecurityRole", settings.Name, settings.InstallTo!, out var fail);
            if (fail.HasValue) return fail.Value;
        }

        var doc = XppScaffolder.Role(settings.Name, settings.Duties, settings.Privileges, settings.Label, settings.Description);
        var bridge = GenerateBridgeScaffolding.TryWrite(
            kind, backend, settings.InstallTo, settings.Overwrite, "AxSecurityRole", settings.Name, doc.ToString(), outPath);
        if (bridge.Handled) return bridge.ExitCode;
        try
        {
            var res = ScaffoldFileWriter.Write(doc, outPath!, settings.Overwrite);
            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                kind = "AxSecurityRole",
                name = settings.Name,
                dutyCount = settings.Duties.Length,
                privilegeCount = settings.Privileges.Length,
                duties = settings.Duties,
                privileges = settings.Privileges,
                path = res.Path,
                bytes = res.Bytes,
                backup = res.BackupPath,
                model = settings.InstallTo,
            }));
        }
        catch (Exception ex)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.WriteFailed, ex.Message));
        }
    }

    private static int ExecuteAddTo(OutputMode.Kind kind, Settings settings)
    {
        var path = settings.AddTo!;
        if (!System.IO.File.Exists(path))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, $"Role file not found: {path}"));
        if (settings.Duties.Length == 0 && settings.Privileges.Length == 0)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(
                D365FoErrorCodes.BadInput,
                "At least one --duty or --privilege required when using --add-to."));

        try
        {
            var doc = System.Xml.Linq.XDocument.Load(path);
            var changed = XppScaffolder.AddToRole(doc, settings.Duties, settings.Privileges);
            if (!changed)
            {
                return RenderHelpers.Render(kind, ToolResult<object>.Success(new
                {
                    kind = "AxSecurityRole",
                    path,
                    changed = false,
                    note = "All supplied duties / privileges were already referenced.",
                }));
            }

            var tmp = path + ".tmp";
            using (var fs = System.IO.File.Create(tmp))
                doc.Save(fs);
            var backup = path + ".bak";
            if (System.IO.File.Exists(backup)) System.IO.File.Delete(backup);
            System.IO.File.Move(path, backup);
            System.IO.File.Move(tmp, path);

            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                kind = "AxSecurityRole",
                path,
                changed = true,
                backup,
                addedDuties = settings.Duties,
                addedPrivileges = settings.Privileges,
            }));
        }
        catch (Exception ex)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.WriteFailed, ex.Message));
        }
    }
}

/// <summary>
/// Scaffolds an <c>AxReport</c> + matching <c>SrsReportDataProviderBase</c> AxClass.
/// Mirrors upstream MCP <c>generate_smart_report</c>. ROADMAP §P3.
/// </summary>
public sealed class GenerateReportCommand : Command<GenerateReportCommand.Settings>
{
    public sealed class Settings : GenerateSettings
    {
        [CommandArgument(0, "<NAME>")]
        [System.ComponentModel.Description("AOT report name, e.g. FleetVehicleReport.")]
        public string Name { get; init; } = "";

        [CommandOption("--dp <CLASS>")]
        [System.ComponentModel.Description("Primary data provider class name. Defaults to <Name>DP.")]
        public string? DpClass { get; init; }

        [CommandOption("--tmp <TABLE>")]
        [System.ComponentModel.Description("Temp table class name used by the primary DP. Defaults to <Name>Tmp.")]
        public string? TmpTable { get; init; }

        [CommandOption("--dataset <NAME>")]
        [System.ComponentModel.Description("Primary dataset name inside the report. Defaults to <Name>DS.")]
        public string? DatasetName { get; init; }

        [CommandOption("--caption <TEXT>")]
        public string? Caption { get; init; }

        [CommandOption("--field <FIELD>")]
        [System.ComponentModel.Description("Tablix column field name (repeatable). Generates header + data rows in the tablix.")]
        public string[]? Fields { get; init; }

        [CommandOption("--parameter <SPEC>")]
        [System.ComponentModel.Description("Report parameter (repeatable). Format: Name or Name:Type. Type: String (default), Integer, DateTime, Boolean, Decimal.")]
        public string[]? Parameters { get; init; }

        [CommandOption("--extra-dataset <SPEC>")]
        [System.ComponentModel.Description("Additional dataset (repeatable). Format: DatasetName:DPClassName. Each produces its own tablix in the design.")]
        public string[]? ExtraDatasets { get; init; }

        [CommandOption("--out-dp <PATH>")]
        [System.ComponentModel.Description("Output path for the primary DP class XML. Defaults to sibling of --out named <DpClass>.xml.")]
        public string? OutDp { get; init; }

        [CommandOption("--out-contract <PATH>")]
        [System.ComponentModel.Description("Output path for the DataContract class XML. Auto-derived when --parameter is used.")]
        public string? OutContract { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);
        if (string.IsNullOrWhiteSpace(settings.Name))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "Report name required."));

        var hasInstall = !string.IsNullOrWhiteSpace(settings.InstallTo);
        var hasOut     = !string.IsNullOrWhiteSpace(settings.Out);
        if (!hasInstall && !hasOut)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "--out or --install-to is required."));

        // --- Parse extra datasets ---
        List<ReportDatasetSpec>? extraDatasets = null;
        if (settings.ExtraDatasets is { Length: > 0 })
        {
            extraDatasets = [];
            foreach (var raw in settings.ExtraDatasets)
            {
                var parts = raw.Split(':', 2);
                if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                    return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput,
                        $"--extra-dataset '{raw}' must be in Name:DPClass format."));
                extraDatasets.Add(new ReportDatasetSpec(parts[0], parts[1]));
            }
        }

        // --- Parse parameters ---
        List<ReportParameterSpec>? paramSpecs = null;
        if (settings.Parameters is { Length: > 0 })
        {
            paramSpecs = settings.Parameters.Select(p =>
            {
                var parts = p.Split(':', 2);
                return new ReportParameterSpec(parts[0], parts.Length > 1 ? parts[1] : "String");
            }).ToList();
        }

        var spec = new ReportSpec(
            settings.Name,
            settings.DpClass,
            settings.TmpTable,
            settings.DatasetName,
            settings.Caption,
            extraDatasets is { Count: > 0 }
                ? [new ReportDatasetSpec(
                    string.IsNullOrWhiteSpace(settings.DatasetName) ? settings.Name + "DS" : settings.DatasetName,
                    string.IsNullOrWhiteSpace(settings.DpClass)     ? settings.Name + "DP" : settings.DpClass,
                    settings.Fields), .. extraDatasets]
                : null,
            settings.Fields,
            paramSpecs);

        var hasContract = spec.Parameters is { Count: > 0 };

        // --- Resolve output paths ---
        string? reportPath, dpPath, contractPath;
        if (hasInstall && !hasOut)
        {
            reportPath = GenerateInstaller.ResolveInstallPath(kind, "AxReport", settings.Name, settings.InstallTo!, out var f1);
            if (f1.HasValue) return f1.Value;

            if (!string.IsNullOrWhiteSpace(settings.OutDp))
            {
                dpPath = settings.OutDp;
            }
            else
            {
                dpPath = GenerateInstaller.ResolveInstallPath(kind, "AxClass", spec.EffectiveDpClass, settings.InstallTo!, out var f2);
                if (f2.HasValue) return f2.Value;
            }

            if (hasContract)
            {
                if (!string.IsNullOrWhiteSpace(settings.OutContract))
                {
                    contractPath = settings.OutContract;
                }
                else
                {
                    contractPath = GenerateInstaller.ResolveInstallPath(kind, "AxClass", spec.ContractClass, settings.InstallTo!, out var f3);
                    if (f3.HasValue) return f3.Value;
                }
            }
            else contractPath = null;
        }
        else
        {
            var dir = System.IO.Path.GetDirectoryName(settings.Out!)!;
            reportPath   = settings.Out!;
            dpPath       = settings.OutDp ?? System.IO.Path.Combine(dir, spec.EffectiveDpClass + ".xml");
            contractPath = hasContract
                ? (settings.OutContract ?? System.IO.Path.Combine(dir, spec.ContractClass + ".xml"))
                : null;
        }

        try
        {
            var reportResult   = ScaffoldFileWriter.Write(XppScaffolder.Report(spec),   reportPath!,   settings.Overwrite);
            var dpResult       = ScaffoldFileWriter.Write(XppScaffolder.ReportDp(spec), dpPath!,       settings.Overwrite);

            ScaffoldFileWriter.WriteResult? contractResult = null;
            if (hasContract && contractPath is not null)
            {
                var contractDoc = XppScaffolder.ReportContract(spec);
                if (contractDoc is not null)
                    contractResult = ScaffoldFileWriter.Write(contractDoc, contractPath, settings.Overwrite);
            }

            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                kind        = "AxReport",
                name        = spec.Name,
                dpClass     = spec.EffectiveDpClass,
                contractClass = spec.ContractClass,
                datasets    = spec.EffectiveDatasets.Select(ds => new { ds.Name, ds.DpClass }).ToList(),
                parameters  = spec.Parameters?.Select(p => new { p.Name, p.DataType }).ToList(),
                report      = new { path = reportResult.Path,   bytes = reportResult.Bytes,   backup = reportResult.BackupPath },
                dp          = new { path = dpResult.Path,       bytes = dpResult.Bytes,       backup = dpResult.BackupPath },
                contract    = contractResult is null ? null : new { path = contractResult.Path, bytes = contractResult.Bytes, backup = contractResult.BackupPath },
                model       = settings.InstallTo,
            }));
        }
        catch (Exception ex)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.WriteFailed, ex.Message));
        }
    }
}

/// <summary>
/// Idempotent &quot;add duty / privilege reference into an existing role&quot; merge,
/// used by <c>generate role --add-to</c> and by the <c>--into-role</c> flag on
/// <c>generate duty</c> / <c>generate privilege</c>. Loads the role XML,
/// merges via <see cref="XppScaffolder.AddToRole"/>, and writes atomically
/// with a <c>.bak</c> sibling.
/// </summary>
internal static class SecurityRoleMerge
{
    public static bool AddReferences(string path, string[]? duties, string[]? privileges, out object result, out string? error)
    {
        result = null!;
        if (!System.IO.File.Exists(path))
        {
            error = $"Role file not found: {path}";
            return false;
        }
        System.Xml.Linq.XDocument doc;
        try { doc = System.Xml.Linq.XDocument.Load(path); }
        catch (Exception ex) { error = $"Failed to parse role XML: {ex.Message}"; return false; }

        bool changed;
        try { changed = D365FO.Core.Scaffolding.XppScaffolder.AddToRole(doc, duties, privileges); }
        catch (InvalidOperationException ex) { error = ex.Message; return false; }

        if (!changed)
        {
            result = new { path, changed = false, note = "All supplied duties / privileges were already referenced." };
            error = null;
            return true;
        }

        try
        {
            var tmp = path + ".tmp";
            using (var fs = System.IO.File.Create(tmp)) doc.Save(fs);
            var backup = path + ".bak";
            if (System.IO.File.Exists(backup)) System.IO.File.Delete(backup);
            System.IO.File.Move(path, backup);
            System.IO.File.Move(tmp, path);
            result = new
            {
                path,
                changed = true,
                backup,
                addedDuties = duties ?? Array.Empty<string>(),
                addedPrivileges = privileges ?? Array.Empty<string>(),
            };
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
