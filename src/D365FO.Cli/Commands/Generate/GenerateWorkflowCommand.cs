using D365FO.Core;
using D365FO.Core.Scaffolding;
using Spectre.Console.Cli;

namespace D365FO.Cli.Commands.Generate;

/// <summary>
/// Scaffolds the D365FO workflow pattern: an <c>AxWorkflow</c> type definition,
/// a <c>WorkflowDocument</c> subclass, and optionally a CoC extension that adds
/// <c>canSubmitToWorkflow()</c> to the driving table.
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

        [CommandOption("--out-document <PATH>")]
        [System.ComponentModel.Description("Output path for the WorkflowDocument class. Defaults to sibling of --out.")]
        public string? OutDocument { get; init; }

        [CommandOption("--out-submit <PATH>")]
        [System.ComponentModel.Description("Output path for the canSubmitToWorkflow CoC extension. Defaults to sibling of --out when --table is supplied.")]
        public string? OutSubmit { get; init; }

        [CommandOption("--no-submit-stub")]
        [System.ComponentModel.Description("Skip generating the canSubmitToWorkflow CoC extension.")]
        public bool NoSubmitStub { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);

        if (string.IsNullOrWhiteSpace(settings.Name))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "Workflow name required."));
        if (string.IsNullOrWhiteSpace(settings.TableName))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "--table <TABLE> required."));

        var hasInstall = !string.IsNullOrWhiteSpace(settings.InstallTo);
        var hasOut     = !string.IsNullOrWhiteSpace(settings.Out);
        if (!hasInstall && !hasOut)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, "--out or --install-to is required."));

        var docClassName    = string.IsNullOrWhiteSpace(settings.DocumentClassName)
            ? settings.Name + "Document"
            : settings.DocumentClassName!;
        var submitExtName   = settings.TableName + "_WorkflowExtension";
        var generateSubmit  = !settings.NoSubmitStub;

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
                    settings.TableName!,
                    settings.ApprovalName,
                    settings.TaskName,
                    docClassName),
                workflowPath!, settings.Overwrite);

            var documentResult = ScaffoldFileWriter.Write(
                WorkflowScaffolder.WorkflowDocument(docClassName, settings.QueryName),
                documentPath!, settings.Overwrite);

            ScaffoldFileWriter.WriteResult? submitResult = null;
            if (generateSubmit && submitPath is not null)
            {
                submitResult = ScaffoldFileWriter.Write(
                    WorkflowScaffolder.CanSubmitExtension(settings.TableName!),
                    submitPath, settings.Overwrite);
            }

            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                kind            = "Workflow",
                name            = settings.Name,
                tableName       = settings.TableName,
                documentClassName = docClassName,
                approvalName    = settings.ApprovalName,
                taskName        = settings.TaskName,
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
}
