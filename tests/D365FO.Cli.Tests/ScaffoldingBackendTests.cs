using D365FO.Cli.Commands.Generate;
using D365FO.Core;
using D365FO.Core.Scaffolding;
using System.Reflection;
using System.Text.Json.Nodes;

namespace D365FO.Cli.Tests;

public sealed class ScaffoldingBackendTests
{
    [Theory]
    [InlineData("auto", "Auto")]
    [InlineData("bridge", "Bridge")]
    [InlineData("legacy", "Legacy")]
    [InlineData("BRIDGE", "Bridge")]
    public void TryResolve_accepts_supported_backend_values(string raw, string expected)
    {
        var ok = GenerateBackendResolver.TryResolve(raw, out var backend, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(expected, backend.ToString());
    }

    [Fact]
    public void TryResolve_rejects_unknown_backend()
    {
        var ok = GenerateBackendResolver.TryResolve("wizard", out _, out var error);

        Assert.False(ok);
        Assert.Contains("auto, bridge, or legacy", error);
    }

    [Fact]
    public void TryLoadWizardSteps_accepts_direct_json_object()
    {
        var ok = GenerateBridgeScaffolding.TryLoadWizardSteps(
            """{"table":"CustTable","fields":[{"name":"AccountNum"}]}""",
            out var steps,
            out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("CustTable", (string?)steps["table"]);
        Assert.Single(steps["fields"]!.AsArray());
    }

    [Fact]
    public void TryLoadWizardSteps_accepts_wrapped_steps_json_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "wizard.json");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, """{"steps":{"table":"PurchTable","approvalName":"PurchApproval"}}""");

            var ok = GenerateBridgeScaffolding.TryLoadWizardSteps(path, out var steps, out var error);

            Assert.True(ok);
            Assert.Null(error);
            Assert.Equal("PurchTable", (string?)steps["table"]);
            Assert.Equal("PurchApproval", (string?)steps["approvalName"]);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Auto_uses_bridge_by_default_and_allows_explicit_opt_out()
    {
        var old = Environment.GetEnvironmentVariable("D365FO_BRIDGE_ENABLED");
        var configOverrideField = typeof(D365FoSettings).GetField(
            "ConfigPathOverrideForTests",
            BindingFlags.Static | BindingFlags.NonPublic);
        var clearCacheMethod = typeof(D365FoSettings).GetMethod(
            "ClearCacheForTests",
            BindingFlags.Static | BindingFlags.NonPublic);
        var oldConfigOverride = configOverrideField?.GetValue(null);
        var tempConfig = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            configOverrideField?.SetValue(null, tempConfig);
            clearCacheMethod?.Invoke(null, null);

            Environment.SetEnvironmentVariable("D365FO_BRIDGE_ENABLED", null);
            Assert.True(GenerateBackendResolver.ShouldUseBridge(GenerateBackend.Auto));

            Environment.SetEnvironmentVariable("D365FO_BRIDGE_ENABLED", "0");
            Assert.False(GenerateBackendResolver.ShouldUseBridge(GenerateBackend.Auto));

            Environment.SetEnvironmentVariable("D365FO_BRIDGE_ENABLED", "1");
            Assert.True(GenerateBackendResolver.ShouldUseBridge(GenerateBackend.Auto));

            Assert.True(GenerateBackendResolver.ShouldUseBridge(GenerateBackend.Bridge));
            Assert.False(GenerateBackendResolver.ShouldUseBridge(GenerateBackend.Legacy));
        }
        finally
        {
            Environment.SetEnvironmentVariable("D365FO_BRIDGE_ENABLED", old);
            configOverrideField?.SetValue(null, oldConfigOverride);
            clearCacheMethod?.Invoke(null, null);
        }
    }

    [Fact]
    public void Table_bridge_plan_translates_fields_and_primary_key_to_designer_actions()
    {
        var fields = new[]
        {
            new TableFieldSpec("AccountNum", "CustAccount", null, true),
            new TableFieldSpec("Description", "Description", null, false),
        };

        var actions = GenerateTableCommand.BuildTableDesignerActions(fields, new[] { "AccountNum" });

        Assert.Contains(actions, a => a.ActionId == "new-field" && (string?)a.Properties["name"] == "AccountNum");
        Assert.Contains(actions, a => a.ActionId == "new-field-group" && (string?)a.Properties["name"] == "AutoReport");
        Assert.Contains(actions, a => a.ActionId == "new-index" && (string?)a.Properties["name"] == "PrimaryIdx");
        Assert.Contains(actions, a =>
            a.ActionId == "new-index-field" &&
            a.Node == "Indexes[PrimaryIdx]/Fields" &&
            (string?)a.Properties["DataField"] == "AccountNum");
    }

    [Fact]
    public void Table_bridge_plan_uses_pattern_defaults_when_fields_are_absent()
    {
        var effectiveFields = GenerateTableCommand.EffectiveFields(Array.Empty<TableFieldSpec>(), TablePattern.Main).ToList();
        var pk = GenerateTableCommand.PrimaryKeyFields(effectiveFields, Array.Empty<string>()).ToList();

        Assert.Contains(effectiveFields, f => f.Name == "AccountNum" && f.Mandatory);
        Assert.Equal(new[] { "AccountNum" }, pk);
    }

    [Fact]
    public void Form_bridge_properties_set_pattern_version_style_and_caption_on_design()
    {
        var properties = GenerateFormImpl.BuildFormProperties(FormPattern.SimpleList, "@Fleet:Vehicles");
        var design = Assert.IsType<JsonObject>(properties["Design"]);

        Assert.Equal("SimpleList", (string?)design["Pattern"]);
        Assert.Equal("1.1", (string?)design["PatternVersion"]);
        Assert.Equal("SimpleList", (string?)design["Style"]);
        Assert.Equal("@Fleet:Vehicles", (string?)design["Caption"]);
    }

    [Fact]
    public void Form_bridge_plan_translates_datasource_and_fields_to_designer_actions()
    {
        var actions = GenerateFormImpl.BuildFormDesignerActions(
            "FmVehicleForm",
            "FmVehicle",
            FormPattern.SimpleList,
            new[] { "VehicleId", "Description" },
            Array.Empty<FormSectionSpec>(),
            null);

        Assert.Contains(actions, a => a.ActionId == "new-data-source" && (string?)a.Properties["table"] == "FmVehicle");
        Assert.Contains(actions, a => a.ActionId == "new-control" && (string?)a.Properties["name"] == "ActionPane");
        Assert.Contains(actions, a => a.ActionId == "new-control" && (string?)a.Properties["name"] == "Grid");
        Assert.Contains(actions, a =>
            a.ActionId == "new-control" &&
            a.Node == "Design/Controls[Grid]/Controls" &&
            (string?)a.Properties["DataField"] == "VehicleId");
    }
}
