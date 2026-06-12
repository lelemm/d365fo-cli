using System.Xml.Linq;

namespace D365FO.Core.FormPatterns;

/// <summary>Normalized node of the form design tree.</summary>
public sealed class FormControlNode
{
    public required string Name { get; init; }

    /// <summary>
    /// Normalized control type: i:type minus AxForm/Control affixes
    /// (e.g. 'Grid', 'ActionPane', 'TabPage', 'String'), the &lt;Type&gt; element
    /// value, or the FormControlExtension name (e.g. 'QuickFilterControl').
    /// </summary>
    public required string Type { get; init; }

    /// <summary>Raw i:type attribute when present (e.g. 'AxFormGridControl').</summary>
    public string? AxType { get; init; }

    /// <summary>Sub-pattern declared on this container (e.g. 'CustomAndQuickFilters').</summary>
    public string? Pattern { get; set; }
    public string? PatternVersion { get; set; }

    public Dictionary<string, string> Properties { get; } = new();
    public List<FormControlNode> Children { get; } = new();
}

/// <summary>Normalized form Design info.</summary>
public sealed class FormDesignInfo
{
    /// <summary>Top-level form pattern declared on Design (e.g. 'SimpleList').</summary>
    public string? Pattern { get; set; }
    public string? PatternVersion { get; set; }
    public string? Style { get; set; }
    public Dictionary<string, string> Properties { get; } = new();
    public List<FormControlNode> Controls { get; } = new();
}

/// <summary>
/// Form Design tree walker shared by the form pattern validator and any future
/// pattern-mining pipeline. Port of the upstream MCP
/// <c>src/metadata/formPatternMiner.ts</c>, rewritten over XLinq.
///
/// Real AxForm XML nests controls as
/// <c>Design &gt; Controls &gt; AxFormControl[i:type] &gt; Controls &gt; …</c>;
/// container controls carry their own &lt;Pattern&gt;/&lt;PatternVersion&gt;
/// (sub-patterns); extension controls (QuickFilter etc.) are plain
/// &lt;AxFormControl&gt; without an i:type, identified by
/// &lt;FormControlExtension&gt;&lt;Name&gt;.
/// </summary>
public static class FormDesignWalker
{
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>Control/Design properties surfaced into Properties (when present as simple values).</summary>
    private static readonly string[] PropertyKeys =
    {
        "Caption", "Visible", "Enabled", "AutoDeclaration", "DataSource", "DataField",
        "DataMethod", "HelpText", "Label", "Width", "Height", "AllowEdit", "Mandatory",
        "Style", "TitleDataSource", "ArrangeMethod", "MultiSelect", "ShowRowLabels",
        "WidthMode", "HeightMode",
    };

    /// <summary>'AxFormGridControl' → 'Grid', 'AxFormActionPaneControl' → 'ActionPane', 'AxFormControl' → ''.</summary>
    public static string NormalizeControlType(string? axType)
    {
        if (string.IsNullOrEmpty(axType)) return "";
        var t = axType;
        if (t.StartsWith("AxForm", StringComparison.Ordinal)) t = t["AxForm".Length..];
        if (t.EndsWith("Control", StringComparison.Ordinal)) t = t[..^"Control".Length];
        return t;
    }

    /// <summary>
    /// Namespace-agnostic child lookup. AxForm XML mixes the
    /// <c>Microsoft.Dynamics.AX.Metadata.V6</c> default namespace with
    /// <c>xmlns=""</c> resets, so elements are matched by local name only.
    /// </summary>
    internal static XElement? Child(XElement parent, string localName)
        => parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    internal static IEnumerable<XElement> Children(XElement parent, string localName)
        => parent.Elements().Where(e => e.Name.LocalName == localName);

    private static string? ElementText(XElement parent, string name)
    {
        var el = Child(parent, name);
        if (el is null) return null;
        if ((string?)el.Attribute(Xsi + "nil") == "true") return null;
        var value = el.Value.Trim();
        return value.Length > 0 ? value : null;
    }

    /// <summary>Resolve the display type of a control node: i:type → &lt;Type&gt; element → extension name → 'Control'.</summary>
    private static (string Type, string? AxType) ResolveControlType(XElement node)
    {
        var axType = (string?)node.Attribute(Xsi + "type");
        var fromAxType = NormalizeControlType(axType);
        if (fromAxType.Length > 0) return (fromAxType, axType);

        var typeElement = ElementText(node, "Type");
        if (typeElement is not null) return (typeElement, axType);

        var extension = Child(node, "FormControlExtension");
        if (extension is not null && (string?)extension.Attribute(Xsi + "nil") != "true")
        {
            var extName = ElementText(extension, "Name");
            if (extName is not null) return (extName, axType);
        }
        return ("Control", axType);
    }

    private static FormControlNode ExtractControlNode(XElement node)
    {
        var (type, axType) = ResolveControlType(node);
        var control = new FormControlNode
        {
            Name = ElementText(node, "Name") ?? "Unknown",
            Type = type,
            AxType = axType,
        };

        control.Pattern = ElementText(node, "Pattern");
        control.PatternVersion = ElementText(node, "PatternVersion");

        foreach (var prop in PropertyKeys)
        {
            var value = ElementText(node, prop);
            if (value is not null) control.Properties[prop] = value;
        }

        var controlsNode = Child(node, "Controls");
        if (controlsNode is not null)
            foreach (var childNode in Children(controlsNode, "AxFormControl"))
                control.Children.Add(ExtractControlNode(childNode));

        return control;
    }

    /// <summary>Walk a &lt;Design&gt; element into a normalized tree.</summary>
    public static FormDesignInfo Walk(XElement? designNode)
    {
        var design = new FormDesignInfo();
        if (designNode is null) return design;

        design.Pattern = ElementText(designNode, "Pattern");
        design.PatternVersion = ElementText(designNode, "PatternVersion");
        design.Style = ElementText(designNode, "Style");

        foreach (var prop in PropertyKeys)
        {
            var value = ElementText(designNode, prop);
            if (value is not null) design.Properties[prop] = value;
        }

        var controlsNode = Child(designNode, "Controls");
        if (controlsNode is not null)
            foreach (var node in Children(controlsNode, "AxFormControl"))
                design.Controls.Add(ExtractControlNode(node));

        return design;
    }
}
