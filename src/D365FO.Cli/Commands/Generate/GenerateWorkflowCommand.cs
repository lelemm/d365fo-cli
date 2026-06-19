using D365FO.Core;
using D365FO.Core.Scaffolding;
using D365FO.Cli.Commands.Get;
using Spectre.Console.Cli;
using System.Text.Json.Nodes;

namespace D365FO.Cli.Commands.Generate;

/// <summary>
/// Scaffolds the D365FO workflow pattern: an <c>AxWorkflowTemplate</c>,
/// a <c>WorkflowDocument</c> subclass, submit action metadata, and optional
/// approval/task elements.
/// </summary>
public sealed class GenerateWorkflowCommand : Command<GenerateWorkflowCommand.Settings>
{
    public sealed class Settings : GenerateSettings
    {
        [CommandArgument(0, "<NAME>")]
        [System.ComponentModel.Description("Workflow type name (e.g. PurchOrderWorkflow).")]
        public string Name { get; init; } = "";

        [CommandOption("--table <TABLE>")]
        [System.ComponentModel.Description("Table that drives the workflow (e.g. PurchTable).")]
        public string? TableName { get; init; }

        [CommandOption("--approval-name <NAME>")]
        [System.ComponentModel.Description("Name of the approval element to include.")]
        public string? ApprovalName { get; init; }

        [CommandOption("--task-name <NAME>")]
        [System.ComponentModel.Description("Name of the task element to include.")]
        public string? TaskName { get; init; }

        [CommandOption("--document-class <NAME>")]
        [System.ComponentModel.Description("WorkflowDocument class name. Defaults to <NAME>Document.")]
        public string? DocumentClassName { get; init; }

        [CommandOption("--query <NAME>")]
        [System.ComponentModel.Description("Query name used by the WorkflowDocument. Defaults to <DocumentClass>Query.")]
        public string? QueryName { get; init; }

        [CommandOption("--submit-class <NAME>")]
        [System.ComponentModel.Description("Submit-to-workflow class name. Defaults to <NAME>SubmitToWorkflow.")]
        public string? SubmitClassName { get; init; }

        [CommandOption("--submit-menu-item <NAME>")]
        [System.ComponentModel.Description("Action menu item that launches submit-to-workflow. Defaults to <NAME>Submit.")]
        public string? SubmitMenuItemName { get; init; }

        [CommandOption("--document-menu-item <NAME>")]
        [System.ComponentModel.Description("Display menu item used as the workflow document entry point. Defaults to <NAME>MenuItem.")]
        public string? DocumentMenuItemName { get; init; }

        [CommandOption("--category <NAME>")]
        [System.ComponentModel.Description("Workflow category.")]
        public string? Category { get; init; }

        [CommandOption("--label <TEXT>")]
        [System.ComponentModel.Description("Workflow label.")]
        public string? Label { get; init; }

        [CommandOption("--help-text <TEXT>")]
        [System.ComponentModel.Description("Workflow help text.")]
        public string? HelpText { get; init; }

        [CommandOption("--out-document <PATH>")]
        [System.ComponentModel.Description("Output path for the WorkflowDocument class. Defaults to sibling of --out.")]
        public string? OutDocument { get; init; }

        [CommandOption("--out-submit <PATH>")]
        [System.ComponentModel.Description("Output path for the submit-to-workflow class. Defaults to sibling of --out.")]
        public string? OutSubmit { get; init; }

        [CommandOption("--out-submit-menu-item <PATH>")]
        [System.ComponentModel.Description("Output path for the submit-to-workflow action menu item. Defaults to sibling of --out.")]
        public string? OutSubmitMenuItem { get; init; }

        [CommandOption("--out-approval <PATH>")]
        [System.ComponentModel.Description("Output path for the workflow approval element. Defaults to sibling of --out when --approval-name is supplied.")]
        public string? OutApproval { get; init; }

        [CommandOption("--out-task <PATH>")]
        [System.ComponentModel.Description("Output path for the workflow task element. Defaults to sibling of --out when --task-name is supplied.")]
        public string? OutTask { get; init; }

        [CommandOption("--no-submit-stub")]
        [System.ComponentModel.Description("Skip generating the submit-to-workflow class and action menu item.")]
        public bool NoSubmitStub { get; init; }

        [CommandOption("--wizard-json <JSON_OR_PATH>")]
        [System.ComponentModel.Description("Wizard step JSON object or path. CLI options override matching step values.")]
        public string? WizardJson { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);

        if (string.IsNullOrWhiteSpace(settings.Name))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "Workflow name required."));
        if (!GenerateBridgeScaffolding.TryLoadWizardSteps(settings.WizardJson, out var wizardSteps, out var wizardJsonError))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, wizardJsonError!));
        if (!GenerateBackendResolver.TryResolve(settings.Backend, out var backend, out var backendError))
            return GenerateBridgeScaffolding.RenderBackendError(kind, backendError!);
        var useBridge = GenerateBackendResolver.ShouldUseBridge(backend);

        var tableName = settings.TableName ?? ReadStepString(wizardSteps, "table", "tableName");
        if (string.IsNullOrWhiteSpace(tableName))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "--table <TABLE> required."));

        var hasInstall = !string.IsNullOrWhiteSpace(settings.InstallTo);
        var hasOut     = !string.IsNullOrWhiteSpace(settings.Out);
        if (!hasInstall && !hasOut)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "--out or --install-to is required."));

        var docClassName = settings.DocumentClassName
            ?? ReadStepString(wizardSteps, "documentClassName", "documentClass")
            ?? settings.Name + "Document";
        var queryName = settings.QueryName
            ?? ReadStepString(wizardSteps, "queryName", "query")
            ?? docClassName.Replace("Document", "") + "Query";
        var submitClassName = settings.SubmitClassName
            ?? ReadStepString(wizardSteps, "submitClassName", "submitClass")
            ?? settings.Name + "SubmitToWorkflow";
        var submitMenuItemName = settings.SubmitMenuItemName
            ?? ReadStepString(wizardSteps, "submitMenuItemName", "submitMenuItem")
            ?? settings.Name + "Submit";
        var documentMenuItemName = settings.DocumentMenuItemName
            ?? ReadStepString(wizardSteps, "documentMenuItemName", "documentMenuItem")
            ?? settings.Name + "MenuItem";
        var approvalName = settings.ApprovalName ?? ReadStepString(wizardSteps, "approvalName", "approval");
        var taskName = settings.TaskName ?? ReadStepString(wizardSteps, "taskName", "task");
        var generateSubmit = !settings.NoSubmitStub && !ReadStepBool(wizardSteps, "noSubmitStub", false) && !ReadStepBool(wizardSteps, "skipSubmitStub", false);

        if (useBridge)
        {
            var operation = hasInstall && !hasOut ? "create" : "render";
            var bridgeSteps = (JsonObject)wizardSteps.DeepClone();
            bridgeSteps["table"] = tableName;
            bridgeSteps["documentClassName"] = docClassName;
            bridgeSteps["queryName"] = queryName;
            bridgeSteps["submitClassName"] = submitClassName;
            bridgeSteps["submitMenuItemName"] = submitMenuItemName;
            bridgeSteps["documentMenuItemName"] = documentMenuItemName;
            bridgeSteps["noSubmitStub"] = !generateSubmit;
            SetStepIfNotEmpty(bridgeSteps, "approvalName", approvalName);
            SetStepIfNotEmpty(bridgeSteps, "taskName", taskName);
            SetStepIfNotEmpty(bridgeSteps, "category", settings.Category);
            SetStepIfNotEmpty(bridgeSteps, "label", settings.Label);
            SetStepIfNotEmpty(bridgeSteps, "helpText", settings.HelpText);

            var bridgeArgs = new JsonObject
            {
                ["name"] = settings.Name,
                ["operation"] = operation,
                ["overwrite"] = settings.Overwrite,
                ["steps"] = bridgeSteps,
            };
            if (string.Equals(operation, "create", StringComparison.OrdinalIgnoreCase))
            {
                bridgeArgs["model"] = settings.InstallTo;
            }

            var (ok, error, result) = BridgeGate.TryRunWizard("runWorkflowWizard", bridgeArgs);
            if (!ok)
            {
                return RenderHelpers.Render(kind, ToolResult<object>.Fail(
                    "BRIDGE_WIZARD_FAILED",
                    error ?? "Workflow wizard failed.",
                    "Use --backend legacy only if you need the old XML scaffolder."));
            }

            var warnings = GenerateBridgeScaffolding.MergeWarnings(null, result);
            var files = GenerateBridgeScaffolding.GetWizardFiles(result);

            if (operation == "render")
            {
                var workflowFile = GenerateBridgeScaffolding.FindWizardFile(files, "workflowTemplate", settings.Name);
                var documentFile = GenerateBridgeScaffolding.FindWizardFile(files, "class", docClassName);
                var submitFile = generateSubmit ? GenerateBridgeScaffolding.FindWizardFile(files, "class", submitClassName) : null;
                var submitMenuFile = generateSubmit ? GenerateBridgeScaffolding.FindWizardFile(files, "menuItemAction", submitMenuItemName) : null;
                var approvalFile = !string.IsNullOrWhiteSpace(approvalName) ? GenerateBridgeScaffolding.FindWizardFile(files, "workflowApproval", approvalName) : null;
                var taskFile = !string.IsNullOrWhiteSpace(taskName) ? GenerateBridgeScaffolding.FindWizardFile(files, "workflowTask", taskName) : null;
                if (workflowFile is null || documentFile is null || (generateSubmit && (submitFile is null || submitMenuFile is null)) ||
                    (!string.IsNullOrWhiteSpace(approvalName) && approvalFile is null) ||
                    (!string.IsNullOrWhiteSpace(taskName) && taskFile is null))
                {
                    return RenderHelpers.Render(kind, ToolResult<object>.Fail(
                        "BRIDGE_WIZARD_FAILED",
                        "Bridge workflow wizard succeeded but did not return the expected metadata files."));
                }

                var bridgeWorkflowPath = settings.Out!;
                var bridgeDocumentPath = settings.OutDocument ?? SiblingPath(bridgeWorkflowPath, docClassName);
                var bridgeSubmitPath = generateSubmit ? settings.OutSubmit ?? SiblingPath(bridgeWorkflowPath, submitClassName) : null;
                var bridgeSubmitMenuPath = generateSubmit ? settings.OutSubmitMenuItem ?? SiblingPath(bridgeWorkflowPath, submitMenuItemName) : null;
                var bridgeApprovalPath = !string.IsNullOrWhiteSpace(approvalName) ? settings.OutApproval ?? SiblingPath(bridgeWorkflowPath, approvalName!) : null;
                var bridgeTaskPath = !string.IsNullOrWhiteSpace(taskName) ? settings.OutTask ?? SiblingPath(bridgeWorkflowPath, taskName!) : null;

                try
                {
                    var workflowResult = ScaffoldFileWriter.Write(workflowFile.Xml, bridgeWorkflowPath, settings.Overwrite);
                    var documentResult = ScaffoldFileWriter.Write(documentFile.Xml, bridgeDocumentPath, settings.Overwrite);
                    ScaffoldFileWriter.WriteResult? submitResult = null;
                    ScaffoldFileWriter.WriteResult? submitMenuResult = null;
                    ScaffoldFileWriter.WriteResult? approvalResult = null;
                    ScaffoldFileWriter.WriteResult? taskResult = null;
                    if (generateSubmit)
                    {
                        submitResult = ScaffoldFileWriter.Write(submitFile!.Xml, bridgeSubmitPath!, settings.Overwrite);
                        submitMenuResult = ScaffoldFileWriter.Write(submitMenuFile!.Xml, bridgeSubmitMenuPath!, settings.Overwrite);
                    }
                    if (approvalFile is not null)
                    {
                        approvalResult = ScaffoldFileWriter.Write(approvalFile.Xml, bridgeApprovalPath!, settings.Overwrite);
                    }
                    if (taskFile is not null)
                    {
                        taskResult = ScaffoldFileWriter.Write(taskFile.Xml, bridgeTaskPath!, settings.Overwrite);
                    }

                    return RenderHelpers.Render(kind, ToolResult<object>.Success(new
                    {
                        kind = "AxWorkflowTemplate",
                        name = settings.Name,
                        tableName,
                        documentClassName = docClassName,
                        queryName,
                        submitClassName = generateSubmit ? submitClassName : null,
                        submitMenuItemName = generateSubmit ? submitMenuItemName : null,
                        documentMenuItemName,
                        approvalName,
                        taskName,
                        workflow = ToResult(workflowResult),
                        document = ToResult(documentResult),
                        submitClass = ToResult(submitResult),
                        submitMenuItem = ToResult(submitMenuResult),
                        approval = ToResult(approvalResult),
                        task = ToResult(taskResult),
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

            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                kind = "AxWorkflowTemplate",
                name = settings.Name,
                tableName,
                documentClassName = docClassName,
                queryName,
                submitClassName = generateSubmit ? submitClassName : null,
                submitMenuItemName = generateSubmit ? submitMenuItemName : null,
                documentMenuItemName,
                approvalName,
                taskName,
                model = (string?)result?["model"] ?? settings.InstallTo,
                backend = "bridge",
                source = (string?)result?["source"] ?? "vs-extension-wizard",
                operation = (string?)result?["operation"] ?? operation,
                files = files.Select(file => new { file.Kind, file.Name, file.Path }).ToList(),
            }, warnings));
        }

        var submitExtName = tableName + "_WorkflowExtension";

        // Resolve output paths.
        string? workflowPath, documentPath, submitPath;
        if (hasInstall && !hasOut)
        {
            workflowPath  = GenerateInstaller.ResolveInstallPath(kind, "AxWorkflow", settings.Name, settings.InstallTo!, out var f1);
            if (f1.HasValue) return f1.Value;
            documentPath  = string.IsNullOrWhiteSpace(settings.OutDocument)
                ? GenerateInstaller.ResolveInstallPath(kind, "AxClass", docClassName, settings.InstallTo!, out _)
                : settings.OutDocument;
            submitPath    = !generateSubmit ? null
                : string.IsNullOrWhiteSpace(settings.OutSubmit)
                    ? GenerateInstaller.ResolveInstallPath(kind, "AxClass", submitExtName, settings.InstallTo!, out _)
                    : settings.OutSubmit;
        }
        else
        {
            var dir = System.IO.Path.GetDirectoryName(settings.Out!)!;
            workflowPath  = settings.Out!;
            documentPath  = settings.OutDocument ?? System.IO.Path.Combine(dir, docClassName + ".xml");
            submitPath    = !generateSubmit ? null
                : settings.OutSubmit ?? System.IO.Path.Combine(dir, submitExtName + ".xml");
        }

        try
        {
            var workflowResult = ScaffoldFileWriter.Write(
                WorkflowScaffolder.WorkflowType(
                    settings.Name,
                    tableName!,
                    approvalName,
                    taskName,
                    docClassName),
                workflowPath!, settings.Overwrite);

            var documentResult = ScaffoldFileWriter.Write(
                WorkflowScaffolder.WorkflowDocument(docClassName, queryName),
                documentPath!, settings.Overwrite);

            ScaffoldFileWriter.WriteResult? submitResult = null;
            if (generateSubmit && submitPath is not null)
            {
                submitResult = ScaffoldFileWriter.Write(
                    WorkflowScaffolder.CanSubmitExtension(tableName!),
                    submitPath, settings.Overwrite);
            }

            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                kind            = "Workflow",
                name            = settings.Name,
                tableName,
                documentClassName = docClassName,
                approvalName,
                taskName,
                workflow        = new { path = workflowResult.Path,  bytes = workflowResult.Bytes,  backup = workflowResult.BackupPath },
                document        = new { path = documentResult.Path,  bytes = documentResult.Bytes,  backup = documentResult.BackupPath },
                submitStub      = submitResult is null ? null : new { path = submitResult.Path, bytes = submitResult.Bytes, backup = submitResult.BackupPath },
                model           = settings.InstallTo,
            }));
        }
        catch (Exception ex)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.WriteFailed, ex.Message));
        }
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

    private static bool ReadStepBool(JsonObject steps, string key, bool defaultValue)
    {
        var node = steps[key];
        if (node is null)
        {
            return defaultValue;
        }

        if (node is JsonValue value && value.TryGetValue<bool>(out var boolean))
        {
            return boolean;
        }

        return bool.TryParse(node.ToString().Trim('"'), out var parsed) ? parsed : defaultValue;
    }

    private static void SetStepIfNotEmpty(JsonObject steps, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            steps[key] = value;
        }
    }

    private static string SiblingPath(string rootPath, string name)
    {
        var dir = System.IO.Path.GetDirectoryName(rootPath);
        var fileName = name + ".xml";
        return string.IsNullOrWhiteSpace(dir) ? fileName : System.IO.Path.Combine(dir, fileName);
    }

    private static object? ToResult(ScaffoldFileWriter.WriteResult? result) =>
        result is null
            ? null
            : new { path = result.Path, bytes = result.Bytes, backup = result.BackupPath };
}
