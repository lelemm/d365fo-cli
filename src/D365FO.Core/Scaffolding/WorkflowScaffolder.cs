using System.Xml.Linq;

namespace D365FO.Core.Scaffolding;

/// <summary>
/// Scaffolds the standard D365FO workflow pattern:
/// <c>WorkflowDocument</c> subclass, <c>AxWorkflow</c> type XML, and a CoC
/// <c>canSubmitToWorkflow()</c> stub on the driving table.
/// </summary>
public static class WorkflowScaffolder
{
    /// <summary>
    /// Scaffolds an <c>AxClass</c> extending <c>WorkflowDocument</c>.
    /// The generated <c>getQueryName()</c> returns the companion query name.
    /// </summary>
    public static XDocument WorkflowDocument(string documentClassName, string? queryName = null)
    {
        var effectiveQuery = queryName ?? documentClassName.Replace("Document", "") + "Query";

        var declaration =
            $"class {documentClassName} extends WorkflowDocument\n" +
            "{\n" +
            "}\n";

        var getQuerySrc =
            "public QueryName getQueryName()\n" +
            "{\n" +
            $"    return queryStr({effectiveQuery});\n" +
            "}\n";

        return new XDocument(
            new XElement("AxClass",
                new XElement("Name", documentClassName),
                new XElement("Extends", "WorkflowDocument"),
                new XElement("SourceCode",
                    new XElement("Declaration", declaration),
                    new XElement("Methods",
                        new XElement("Method",
                            new XElement("Name", "getQueryName"),
                            new XElement("Source", getQuerySrc))))));
    }

    /// <summary>
    /// Scaffolds an <c>AxWorkflow</c> type definition that ties the workflow
    /// to a table. Optionally includes approval and/or task sub-type elements.
    /// </summary>
    public static XDocument WorkflowType(
        string workflowTypeName,
        string tableName,
        string? approvalName = null,
        string? taskName = null,
        string? documentClassName = null)
    {
        var docClass      = documentClassName ?? workflowTypeName + "Document";
        var submitMenuItem = workflowTypeName + "Submit";
        var docMenuItem    = workflowTypeName + "MenuItem";

        var root = new XElement("AxWorkflow",
            new XElement("Name", workflowTypeName),
            new XElement("DocumentTableName", tableName),
            new XElement("DocumentMenuItemName", docMenuItem),
            new XElement("DocumentMenuItemType", "Display"),
            new XElement("SubmitToWorkflowMenuItem", submitMenuItem),
            new XElement("SubmitToWorkflowMenuItemType", "Action"),
            new XElement("WorkflowDocumentClass", docClass));

        var elements = new List<XElement>();
        if (!string.IsNullOrWhiteSpace(approvalName))
        {
            elements.Add(new XElement("AxWorkflowElement",
                new XElement("Name", approvalName),
                new XElement("WorkflowElementType", "Approval"),
                new XElement("OutcomeType", "TwoOutcome")));
        }
        if (!string.IsNullOrWhiteSpace(taskName))
        {
            elements.Add(new XElement("AxWorkflowElement",
                new XElement("Name", taskName),
                new XElement("WorkflowElementType", "Task"),
                new XElement("OutcomeType", "SingleOutcome")));
        }
        if (elements.Count > 0)
            root.Add(new XElement("WorkflowElements", elements));

        return new XDocument(root);
    }

    /// <summary>
    /// Scaffolds a CoC extension on <paramref name="tableName"/> that adds a
    /// <c>canSubmitToWorkflow()</c> override — the entry point that controls
    /// whether the Submit button on the form is enabled.
    /// </summary>
    public static XDocument CanSubmitExtension(string tableName)
    {
        var extensionName = tableName + "_WorkflowExtension";

        var declaration =
            $"[ExtensionOf(tableStr({tableName}))]\n" +
            $"final class {extensionName}\n" +
            "{\n" +
            "}\n";

        var canSubmitSrc =
            "public boolean canSubmitToWorkflow(str _workflowType = '')\n" +
            "{\n" +
            "    boolean canSubmit = next canSubmitToWorkflow(_workflowType);\n" +
            "\n" +
            "    // Add conditions under which this record can be submitted:\n" +
            "    // canSubmit = canSubmit && this.Status == MyStatus::Draft;\n" +
            "\n" +
            "    return canSubmit;\n" +
            "}\n";

        return new XDocument(
            new XElement("AxClass",
                new XElement("Name", extensionName),
                new XElement("SourceCode",
                    new XElement("Declaration", declaration),
                    new XElement("Methods",
                        new XElement("Method",
                            new XElement("Name", "canSubmitToWorkflow"),
                            new XElement("Source", canSubmitSrc))))));
    }
}
