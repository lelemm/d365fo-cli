// <copyright file="DesignerKindCatalog.cs" company="d365fo-cli contributors">
// MIT
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

#nullable enable

namespace D365FO.Shared.Designer;

public sealed class DesignerKindGroup
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public DesignerKindNode[] Kinds { get; set; } = Array.Empty<DesignerKindNode>();
}

public sealed class DesignerKindNode
{
    public string Kind { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string AxType { get; set; } = string.Empty;
    public string[] Aliases { get; set; } = Array.Empty<string>();
    public DesignerCollectionSpec[] Collections { get; set; } = Array.Empty<DesignerCollectionSpec>();
}

public sealed class DesignerCollectionSpec
{
    public string Path { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string ChildKind { get; set; } = string.Empty;
    public string[] Actions { get; set; } = Array.Empty<string>();
}

public sealed class DesignerInputSpec
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    public string? Description { get; set; }
}

public sealed class DesignerActionSpec
{
    public string ActionId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string ActionKind { get; set; } = "create";
    public string AppliesToKind { get; set; } = string.Empty;
    public string AppliesToPath { get; set; } = string.Empty;
    public string CreatesKind { get; set; } = string.Empty;
    public string CreatesCollection { get; set; } = string.Empty;
    public string ResultPathTemplate { get; set; } = string.Empty;
    public string NextCatalogKind { get; set; } = string.Empty;
    public string? CreatesKindSelector { get; set; }
    public Dictionary<string, string>? CreatesKindMap { get; set; }
    public DesignerInputSpec[] Inputs { get; set; } = Array.Empty<DesignerInputSpec>();
}

public static class DesignerKindCatalog
{
    private static readonly DesignerKindGroup[] _groups =
    {
        new()
        {
            Id = "security",
            Label = "Security",
            Kinds = new[]
            {
                Kind("security-privilege", "Security privilege", "AxSecurityPrivilege",
                    new[] { "privilege", "securityPrivilege", "AxSecurityPrivilege" },
                    Collection("EntryPoints", "Entry points", "security-entry-point-reference", "new-entry-point")),
                Kind("security-entry-point-reference", "Security entry point reference", "AxSecurityEntryPointReference",
                    new[] { "entry-point", "entrypoint", "AxSecurityEntryPointReference" }),
                Kind("security-duty", "Security duty", "AxSecurityDuty",
                    new[] { "duty", "securityDuty", "AxSecurityDuty" },
                    Collection("Privileges", "Privilege references", "security-privilege-reference", "new-privilege-reference")),
                Kind("security-privilege-reference", "Security privilege reference", "AxSecurityPrivilegeReference",
                    new[] { "privilege-reference", "AxSecurityPrivilegeReference" }),
                Kind("security-role", "Security role", "AxSecurityRole",
                    new[] { "role", "securityRole", "AxSecurityRole" },
                    Collection("Duties", "Duty references", "security-duty-reference", "new-duty-reference"),
                    Collection("Privileges", "Privilege references", "security-privilege-reference", "new-privilege-reference")),
                Kind("security-duty-reference", "Security duty reference", "AxSecurityDutyReference",
                    new[] { "duty-reference", "AxSecurityDutyReference" }),
            },
        },
        new()
        {
            Id = "data-model",
            Label = "Data model",
            Kinds = new[]
            {
                Kind("table", "Table", "AxTable",
                    new[] { "AxTable" },
                    Collection("Fields", "Fields", "table-field-string", "new-field"),
                    Collection("Indexes", "Indexes", "table-index", "new-index"),
                    Collection("Relations", "Relations", "table-relation", "new-relation"),
                    Collection("FieldGroups", "Field groups", "table-field-group", "new-field-group")),
                Kind("table-field-string", "Table string field", "AxTableFieldString",
                    new[] { "table-field", "field", "AxTableFieldString" }),
                Kind("table-field-int", "Table integer field", "AxTableFieldInt",
                    new[] { "AxTableFieldInt" }),
                Kind("table-field-enum", "Table enum field", "AxTableFieldEnum",
                    new[] { "AxTableFieldEnum" }),
                Kind("table-index", "Table index", "AxTableIndex",
                    new[] { "index", "AxTableIndex" },
                    Collection("Fields", "Index fields", "table-index-field", "new-index-field")),
                Kind("table-index-field", "Table index field", "AxTableIndexField",
                    new[] { "index-field", "AxTableIndexField" }),
                Kind("table-relation", "Table relation", "AxTableRelation",
                    new[] { "relation", "AxTableRelation" }),
                Kind("table-field-group", "Table field group", "AxTableFieldGroup",
                    new[] { "field-group", "AxTableFieldGroup" }),
                Kind("query", "Query", "AxQuery",
                    new[] { "AxQuery" },
                    Collection("DataSources", "Data sources", "query-data-source", "new-data-source")),
                Kind("query-data-source", "Query data source", "AxQuerySimpleRootDataSource",
                    new[] { "query-ds", "AxQuerySimpleRootDataSource" }),
                Kind("data-entity", "Data entity", "AxDataEntityView",
                    new[] { "entity", "dataEntityView", "AxDataEntityView" },
                    Collection("DataSources", "Data sources", "data-entity-data-source", "new-data-source"),
                    Collection("Fields", "Fields", "data-entity-field", "new-field")),
                Kind("data-entity-data-source", "Data entity data source", "AxDataEntityViewRootDataSource",
                    new[] { "entity-ds", "AxDataEntityViewRootDataSource" }),
                Kind("data-entity-field", "Data entity field", "AxDataEntityViewField",
                    new[] { "entity-field", "AxDataEntityViewField" }),
            },
        },
        new()
        {
            Id = "ui",
            Label = "UI",
            Kinds = new[]
            {
                Kind("form", "Form", "AxForm",
                    new[] { "AxForm" },
                    Collection("DataSources", "Data sources", "form-data-source-root", "new-data-source"),
                    Collection("Design/Controls", "Design controls", "form-control", "new-control")),
                Kind("form-data-source-root", "Form data source", "AxFormDataSourceRoot",
                    new[] { "form-ds", "AxFormDataSourceRoot" }),
                Kind("form-control", "Form control", "AxFormControl",
                    new[] { "control", "AxFormControl" }),
                Kind("form-action-pane-control", "Form action pane control", "AxFormActionPaneControl",
                    new[] { "actionpane-control", "action-pane-control", "AxFormActionPaneControl" }),
                Kind("form-action-pane-tab-control", "Form action pane tab control", "AxFormActionPaneTabControl",
                    new[] { "actionpanetab-control", "action-pane-tab-control", "AxFormActionPaneTabControl" }),
                Kind("form-button-control", "Form button control", "AxFormButtonControl",
                    new[] { "button-control", "AxFormButtonControl" }),
                Kind("form-button-group-control", "Form button group control", "AxFormButtonGroupControl",
                    new[] { "buttongroup-control", "button-group-control", "AxFormButtonGroupControl" }),
                Kind("form-command-button-control", "Form command button control", "AxFormCommandButtonControl",
                    new[] { "commandbutton-control", "command-button-control", "AxFormCommandButtonControl" }),
                Kind("form-grid-control", "Form grid control", "AxFormGridControl",
                    new[] { "grid-control", "AxFormGridControl" }),
                Kind("form-group-control", "Form group control", "AxFormGroupControl",
                    new[] { "group-control", "AxFormGroupControl" }),
                Kind("form-menu-item-button-control", "Form menu item button control", "AxFormMenuItemButtonControl",
                    new[] { "menuitembutton-control", "menu-item-button-control", "AxFormMenuItemButtonControl" }),
                Kind("form-string-control", "Form string control", "AxFormStringControl",
                    new[] { "string-control", "AxFormStringControl" }),
                Kind("form-tab-control", "Form tab control", "AxFormTabControl",
                    new[] { "tab-control", "AxFormTabControl" }),
                Kind("form-tab-page-control", "Form tab page control", "AxFormTabPageControl",
                    new[] { "tabpage-control", "tab-page-control", "AxFormTabPageControl" }),
                Kind("menu-item-display", "Display menu item", "AxMenuItemDisplay",
                    new[] { "menuItemDisplay", "AxMenuItemDisplay" }),
                Kind("menu-item-action", "Action menu item", "AxMenuItemAction",
                    new[] { "menuItemAction", "AxMenuItemAction" }),
                Kind("menu-item-output", "Output menu item", "AxMenuItemOutput",
                    new[] { "menuItemOutput", "AxMenuItemOutput" }),
            },
        },
        new()
        {
            Id = "service-workflow",
            Label = "Service/workflow",
            Kinds = new[]
            {
                Kind("service", "Service", "AxService", new[] { "AxService" }),
                Kind("service-group", "Service group", "AxServiceGroup",
                    new[] { "serviceGroup", "AxServiceGroup" }),
                Kind("workflow-template", "Workflow template", "AxWorkflowTemplate",
                    new[] { "workflowTemplate", "AxWorkflowTemplate" }),
                Kind("workflow-approval", "Workflow approval", "AxWorkflowApproval",
                    new[] { "workflowApproval", "AxWorkflowApproval" }),
                Kind("workflow-task", "Workflow task", "AxWorkflowTask",
                    new[] { "workflowTask", "AxWorkflowTask" }),
            },
        },
    };

    private static readonly DesignerActionSpec[] _actions =
    {
        Action("new-entry-point", "New Entry Point", "security-privilege", "EntryPoints",
            "security-entry-point-reference", "EntryPoints", "EntryPoints[{name}]", "security-entry-point-reference",
            Inputs(
                Required("name", "Entry point reference name."),
                Optional("objectName", "Referenced menu item or securable object."),
                Optional("objectType", "Referenced object type."))),
        Action("new-privilege-reference", "New Privilege Reference", "security-duty", "Privileges",
            "security-privilege-reference", "Privileges", "Privileges[{name}]", "security-privilege-reference",
            Inputs(Required("name", "Referenced privilege name."))),
        Action("new-privilege-reference", "New Privilege Reference", "security-role", "Privileges",
            "security-privilege-reference", "Privileges", "Privileges[{name}]", "security-privilege-reference",
            Inputs(Required("name", "Referenced privilege name."))),
        Action("new-duty-reference", "New Duty Reference", "security-role", "Duties",
            "security-duty-reference", "Duties", "Duties[{name}]", "security-duty-reference",
            Inputs(Required("name", "Referenced duty name."))),
        Action("new-field", "New Field", "table", "Fields",
            "table-field-string", "Fields", "Fields[{name}]", "table-field-string",
            Inputs(Required("name", "Field name."), Optional("extendedDataType", "EDT name."), Optional("type", "Field type selector.")),
            "type",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "string", "table-field-string" },
                { "int", "table-field-int" },
                { "integer", "table-field-int" },
                { "enum", "table-field-enum" },
            }),
        Action("new-index", "New Index", "table", "Indexes",
            "table-index", "Indexes", "Indexes[{name}]", "table-index",
            Inputs(Required("name", "Index name."), Optional("alternateKey", "Whether the index is an alternate key."), Optional("allowDuplicates", "Whether duplicates are allowed."))),
        Action("new-index-field", "New Index Field", "table", "Indexes[{index}]/Fields",
            "table-index-field", "Fields", "Indexes[{index}]/Fields[{name}]", "table-index-field",
            Inputs(Required("name", "Index field name."), Optional("dataField", "Referenced table field."))),
        Action("new-relation", "New Relation", "table", "Relations",
            "table-relation", "Relations", "Relations[{name}]", "table-relation",
            Inputs(Required("name", "Relation name."), Optional("relatedTable", "Related table name."))),
        Action("new-field-group", "New Field Group", "table", "FieldGroups",
            "table-field-group", "FieldGroups", "FieldGroups[{name}]", "table-field-group",
            Inputs(Required("name", "Field group name."))),
        Action("new-data-source", "New Data Source", "query", "DataSources",
            "query-data-source", "DataSources", "DataSources[{name}]", "query-data-source",
            Inputs(Required("name", "Data source name."), Optional("table", "Source table."))),
        Action("new-data-source", "New Data Source", "data-entity", "DataSources",
            "data-entity-data-source", "DataSources", "DataSources[{name}]", "data-entity-data-source",
            Inputs(Required("name", "Data source name."), Optional("table", "Source table."))),
        Action("new-field", "New Field", "data-entity", "Fields",
            "data-entity-field", "Fields", "Fields[{name}]", "data-entity-field",
            Inputs(Required("name", "Field name."), Optional("dataSource", "Backing data source."), Optional("dataField", "Backing field."))),
        Action("new-data-source", "New Data Source", "form", "DataSources",
            "form-data-source-root", "DataSources", "DataSources[{name}]", "form-data-source-root",
            Inputs(Required("name", "Data source name."), Optional("table", "Source table."))),
        Action("new-control", "New Control", "form", "Design/Controls",
            "form-control", "Design/Controls", "Design/Controls[{name}]", "form-control",
            Inputs(Required("name", "Control name."), Required("controlType", "Control type selector.")),
            "controlType",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "actionpane", "form-action-pane-control" },
                { "action-pane", "form-action-pane-control" },
                { "actionpanetab", "form-action-pane-tab-control" },
                { "action-pane-tab", "form-action-pane-tab-control" },
                { "button", "form-button-control" },
                { "buttongroup", "form-button-group-control" },
                { "button-group", "form-button-group-control" },
                { "commandbutton", "form-command-button-control" },
                { "command-button", "form-command-button-control" },
                { "control", "form-control" },
                { "grid", "form-grid-control" },
                { "group", "form-group-control" },
                { "menuitembutton", "form-menu-item-button-control" },
                { "menu-item-button", "form-menu-item-button-control" },
                { "quickfilter", "form-control" },
                { "quick-filter", "form-control" },
                { "quickfiltercontrol", "form-control" },
                { "string", "form-string-control" },
                { "tab", "form-tab-control" },
                { "tabpage", "form-tab-page-control" },
                { "tab-page", "form-tab-page-control" },
            }),
    };

    public static IReadOnlyList<DesignerKindGroup> Groups => _groups;
    public static IReadOnlyList<DesignerActionSpec> Actions => _actions;

    public static IEnumerable<DesignerKindNode> Kinds => _groups.SelectMany(g => g.Kinds);

    public static string NormalizeKind(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return string.Empty;
        }

        var trimmed = kind.Trim();
        foreach (var node in Kinds)
        {
            if (string.Equals(node.Kind, trimmed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.AxType, trimmed, StringComparison.OrdinalIgnoreCase) ||
                node.Aliases.Any(a => string.Equals(a, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                return node.Kind;
            }
        }

        return trimmed;
    }

    public static DesignerKindNode? FindKind(string kind)
    {
        var canonical = NormalizeKind(kind);
        return Kinds.FirstOrDefault(k => string.Equals(k.Kind, canonical, StringComparison.OrdinalIgnoreCase));
    }

    public static DesignerActionSpec? FindAction(string actionId, string parentKind, string? path)
    {
        var canonicalKind = NormalizeKind(parentKind);
        if (string.Equals(actionId, "set-property", StringComparison.OrdinalIgnoreCase) &&
            FindKind(canonicalKind) is not null)
        {
            return PropertyAction(canonicalKind, path);
        }

        var action = _actions.FirstOrDefault(a =>
            string.Equals(a.ActionId, actionId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.AppliesToKind, canonicalKind, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(path) ||
             PathMatches(a.AppliesToPath, path!)));
        if (action is not null)
        {
            return action;
        }

        if (string.Equals(actionId, "new-control", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(canonicalKind, "form", StringComparison.OrdinalIgnoreCase) &&
            IsFormControlCollectionPath(path))
        {
            return _actions.FirstOrDefault(a =>
                string.Equals(a.ActionId, "new-control", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.AppliesToKind, "form", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.AppliesToPath, "Design/Controls", StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    public static DesignerActionSpec[] ActionsFor(string parentKind, string? path = null)
    {
        var canonicalKind = NormalizeKind(parentKind);
        var actions = _actions
            .Where(a => string.Equals(a.AppliesToKind, canonicalKind, StringComparison.OrdinalIgnoreCase))
            .Where(a => string.IsNullOrWhiteSpace(path) || PathMatches(a.AppliesToPath, path!))
            .ToList();
        if (actions.Count == 0 &&
            string.Equals(canonicalKind, "form", StringComparison.OrdinalIgnoreCase) &&
            IsFormControlCollectionPath(path))
        {
            actions = _actions
                .Where(a => string.Equals(a.AppliesToKind, "form", StringComparison.OrdinalIgnoreCase))
                .Where(a => string.Equals(a.ActionId, "new-control", StringComparison.OrdinalIgnoreCase))
                .Where(a => string.Equals(a.AppliesToPath, "Design/Controls", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (FindKind(canonicalKind) is not null)
        {
            actions.Add(PropertyAction(canonicalKind, path));
        }

        return actions.ToArray();
    }

    public static bool PathMatches(string template, string path)
    {
        if (string.IsNullOrWhiteSpace(template) || string.IsNullOrWhiteSpace(path))
        {
            return string.Equals(template, path, StringComparison.OrdinalIgnoreCase);
        }

        var templateSegments = template.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var pathSegments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (templateSegments.Length != pathSegments.Length)
        {
            return false;
        }

        for (var i = 0; i < templateSegments.Length; i++)
        {
            if (!SegmentMatches(templateSegments[i], pathSegments[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SegmentMatches(string template, string path)
    {
        var templateName = SegmentName(template);
        var pathName = SegmentName(path);
        if (!string.Equals(templateName, pathName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var templateKey = SegmentKey(template);
        var pathKey = SegmentKey(path);
        if (string.IsNullOrWhiteSpace(templateKey) && string.IsNullOrWhiteSpace(pathKey))
        {
            return true;
        }

        if (templateKey is { Length: > 2 } &&
            templateKey[0] == '{' &&
            templateKey[templateKey.Length - 1] == '}')
        {
            return !string.IsNullOrWhiteSpace(pathKey);
        }

        return string.Equals(templateKey, pathKey, StringComparison.OrdinalIgnoreCase);
    }

    private static string SegmentName(string segment)
    {
        var idx = segment.IndexOf('[');
        return idx < 0 ? segment : segment.Substring(0, idx);
    }

    private static string? SegmentKey(string segment)
    {
        var start = segment.IndexOf('[');
        var end = segment.LastIndexOf(']');
        return start < 0 || end <= start
            ? null
            : segment.Substring(start + 1, end - start - 1);
    }

    private static bool IsFormControlCollectionPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var segments = path!.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 ||
            !string.Equals(segments[0], "Design", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(segments[segments.Length - 1], "Controls", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (var i = 1; i < segments.Length - 1; i++)
        {
            if (!string.Equals(SegmentName(segments[i]), "Controls", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(SegmentKey(segments[i])))
            {
                return false;
            }
        }

        return true;
    }

    public static string ResolveCreatedKind(DesignerActionSpec action, IReadOnlyDictionary<string, string> properties)
    {
        if (action.CreatesKindMap == null || string.IsNullOrWhiteSpace(action.CreatesKindSelector))
        {
            return action.CreatesKind;
        }

        if (properties.TryGetValue(action.CreatesKindSelector!, out var selectorValue) &&
            action.CreatesKindMap.TryGetValue(selectorValue, out var mappedKind))
        {
            return mappedKind;
        }

        return action.CreatesKind;
    }

    public static string ToTree(bool full, string? parentKind = null)
    {
        var sb = new StringBuilder();
        var filter = string.IsNullOrWhiteSpace(parentKind) ? null : NormalizeKind(parentKind!);
        foreach (var group in _groups)
        {
            var kinds = group.Kinds
                .Where(k => filter == null || string.Equals(k.Kind, filter, StringComparison.OrdinalIgnoreCase))
                .Where(k => full || k.Collections.Length > 0)
                .ToArray();
            if (kinds.Length == 0)
            {
                continue;
            }

            sb.AppendLine(group.Label);
            foreach (var kind in kinds)
            {
                sb.Append("  ");
                sb.Append(kind.Kind);
                if (!string.IsNullOrWhiteSpace(kind.AxType))
                {
                    sb.Append(" / ");
                    sb.Append(kind.AxType);
                }
                sb.AppendLine();

                foreach (var collection in kind.Collections)
                {
                    sb.Append("    ");
                    sb.Append(collection.Path);
                    sb.Append(" -> ");
                    sb.Append(collection.ChildKind);
                    var actionIds = collection.Actions.Length == 0
                        ? string.Empty
                        : " via " + string.Join(", ", collection.Actions);
                    sb.AppendLine(actionIds);
                }

                if (full)
                {
                    foreach (var action in ActionsFor(kind.Kind))
                    {
                        sb.Append("      ");
                        sb.Append(action.ActionId);
                        if (string.Equals(action.ActionKind, "property", StringComparison.OrdinalIgnoreCase))
                        {
                            sb.Append(" sets properties");
                        }
                        else
                        {
                            sb.Append(" creates ");
                            sb.Append(action.CreatesKind);
                            if (action.CreatesKindMap != null && action.CreatesKindMap.Count > 0)
                            {
                                sb.Append(" (conditional: ");
                                sb.Append(action.CreatesKindSelector);
                                sb.Append(")");
                            }
                        }
                        sb.AppendLine();
                    }
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    public static string HelpSummary()
        => "Designer kind tree:\n" + ToTree(full: false) + "\n\nNode path examples: EntryPoints, Fields, Design/Controls, EntryPoints[[CustTableListPage]].";

    private static DesignerKindNode Kind(string kind, string label, string axType, string[] aliases, params DesignerCollectionSpec[] collections)
        => new()
        {
            Kind = kind,
            Label = label,
            AxType = axType,
            Aliases = aliases,
            Collections = collections,
        };

    private static DesignerCollectionSpec Collection(string path, string label, string childKind, params string[] actions)
        => new()
        {
            Path = path,
            Label = label,
            ChildKind = childKind,
            Actions = actions,
        };

    private static DesignerActionSpec Action(
        string actionId,
        string label,
        string appliesToKind,
        string appliesToPath,
        string createsKind,
        string createsCollection,
        string resultPathTemplate,
        string nextCatalogKind,
        DesignerInputSpec[] inputs,
        string? createsKindSelector = null,
        Dictionary<string, string>? createsKindMap = null)
        => new()
        {
            ActionId = actionId,
            Label = label,
            ActionKind = "create",
            AppliesToKind = appliesToKind,
            AppliesToPath = appliesToPath,
            CreatesKind = createsKind,
            CreatesCollection = createsCollection,
            ResultPathTemplate = resultPathTemplate,
            NextCatalogKind = nextCatalogKind,
            Inputs = inputs,
            CreatesKindSelector = createsKindSelector,
            CreatesKindMap = createsKindMap,
        };

    private static DesignerActionSpec PropertyAction(string appliesToKind, string? appliesToPath)
        => new()
        {
            ActionId = "set-property",
            Label = "Set Property",
            ActionKind = "property",
            AppliesToKind = NormalizeKind(appliesToKind),
            AppliesToPath = appliesToPath ?? string.Empty,
            Inputs = Inputs(Optional("properties", "Property bag to apply to the selected node.")),
        };

    private static DesignerInputSpec[] Inputs(params DesignerInputSpec[] inputs) => inputs;

    private static DesignerInputSpec Required(string name, string description)
        => new() { Name = name, Required = true, Description = description };

    private static DesignerInputSpec Optional(string name, string description)
        => new() { Name = name, Required = false, Description = description };
}
