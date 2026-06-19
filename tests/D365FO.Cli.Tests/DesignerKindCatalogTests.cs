using D365FO.Cli.Commands.Designer;
using D365FO.Shared.Designer;

namespace D365FO.Cli.Tests;

public sealed class DesignerKindCatalogTests
{
    [Fact]
    public void Help_tree_shows_parent_child_and_created_action_kind()
    {
        var help = DesignerHelpText.BranchDescription;

        Assert.Contains("Designer kind tree", help);
        Assert.Contains("security-privilege", help);
        Assert.Contains("EntryPoints -> security-entry-point-reference", help);
        Assert.Contains("new-entry-point", help);
        Assert.Contains("table", help);
        Assert.Contains("Fields -> table-field-string", help);
    }

    [Fact]
    public void Full_tree_shows_set_property_action()
    {
        var tree = DesignerKindCatalog.ToTree(full: true, parentKind: "table");

        Assert.Contains("set-property sets properties", tree);
    }

    [Fact]
    public void New_entry_point_declares_created_kind_and_next_catalog_kind()
    {
        var action = DesignerKindCatalog.FindAction("new-entry-point", "privilege", null);

        Assert.NotNull(action);
        Assert.Equal("security-privilege", action!.AppliesToKind);
        Assert.Equal("EntryPoints", action.AppliesToPath);
        Assert.Equal("security-entry-point-reference", action.CreatesKind);
        Assert.Equal("security-entry-point-reference", action.NextCatalogKind);
        Assert.Equal("EntryPoints[{name}]", action.ResultPathTemplate);
    }

    [Fact]
    public void Every_create_style_action_declares_output_kind_metadata()
    {
        foreach (var action in DesignerKindCatalog.Actions.Where(a => a.ActionId.StartsWith("new-", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.False(string.IsNullOrWhiteSpace(action.AppliesToKind));
            Assert.False(string.IsNullOrWhiteSpace(action.AppliesToPath));
            Assert.True(
                !string.IsNullOrWhiteSpace(action.CreatesKind) ||
                action.CreatesKindMap is { Count: > 0 },
                $"{action.ActionId} must declare createsKind or createsKindMap.");
            Assert.False(string.IsNullOrWhiteSpace(action.NextCatalogKind));
            Assert.False(string.IsNullOrWhiteSpace(action.ResultPathTemplate));
        }
    }

    [Fact]
    public void Set_property_action_is_available_without_created_kind_metadata()
    {
        var action = DesignerKindCatalog.FindAction("set-property", "table", null);

        Assert.NotNull(action);
        Assert.Equal("property", action!.ActionKind);
        Assert.Equal("table", action.AppliesToKind);
        Assert.True(string.IsNullOrWhiteSpace(action.CreatesKind));
        Assert.Contains(DesignerKindCatalog.ActionsFor("table"), a => a.ActionId == "set-property");
    }

    [Fact]
    public void Set_property_action_is_available_for_selected_child_node()
    {
        var action = DesignerKindCatalog.FindAction("set-property", "form", "Design/Controls[Grid]");

        Assert.NotNull(action);
        Assert.Equal("property", action!.ActionKind);
        Assert.Equal("Design/Controls[Grid]", action.AppliesToPath);
    }

    [Fact]
    public void Conditional_output_kind_resolves_from_selector()
    {
        var action = DesignerKindCatalog.FindAction("new-control", "form", null);

        Assert.NotNull(action);
        Assert.Equal("controlType", action!.CreatesKindSelector);
        Assert.Equal(
            "form-button-control",
            DesignerKindCatalog.ResolveCreatedKind(action, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["controlType"] = "button",
            }));
    }

    [Fact]
    public void Template_paths_match_concrete_designer_nodes()
    {
        Assert.True(DesignerKindCatalog.PathMatches("Indexes[{index}]/Fields", "Indexes[PrimaryIdx]/Fields"));

        var action = DesignerKindCatalog.FindAction("new-index-field", "table", "Indexes[PrimaryIdx]/Fields");

        Assert.NotNull(action);
        Assert.Equal("table-index-field", action!.CreatesKind);
    }

    [Fact]
    public void New_control_is_valid_for_nested_form_control_collections()
    {
        var action = DesignerKindCatalog.FindAction("new-control", "form", "Design/Controls[Grid]/Controls");

        Assert.NotNull(action);
        Assert.Equal("form-control", action!.CreatesKind);
    }

    [Fact]
    public void New_control_declares_common_form_control_subtypes()
    {
        var action = DesignerKindCatalog.FindAction("new-control", "form", null);

        Assert.NotNull(action);
        Assert.Equal("form-grid-control", DesignerKindCatalog.ResolveCreatedKind(action!, new Dictionary<string, string>
        {
            ["controlType"] = "grid",
        }));
        Assert.Equal("form-group-control", DesignerKindCatalog.ResolveCreatedKind(action!, new Dictionary<string, string>
        {
            ["controlType"] = "group",
        }));
        Assert.Equal("form-tab-page-control", DesignerKindCatalog.ResolveCreatedKind(action!, new Dictionary<string, string>
        {
            ["controlType"] = "tabPage",
        }));
    }
}
