using System.Text.Json.Nodes;
using D365FO.Cli.Commands.Get;
using D365FO.Core;
using D365FO.Core.Scaffolding;
using D365FO.Shared.Designer;
using Spectre.Console;
using Spectre.Console.Cli;
using static D365FO.Cli.Commands.Designer.DesignerCommandCommon;

namespace D365FO.Cli.Commands.Designer;

internal static class DesignerHelpText
{
    internal static string BranchDescription =>
        "Run bridge-backed D365FO metadata designer actions.\n\n" +
        DesignerKindCatalog.HelpSummary();

    internal static string CatalogDescription =>
        "List designer actions for a parent kind/node.\n\n" +
        DesignerKindCatalog.HelpSummary();

    internal static string RunDescription =>
        "Run one designer action. Create-style actions return the created kind and next catalog kind.\n\n" +
        "Examples:\n" +
        "  d365fo designer run new-entry-point --parent-kind privilege --parent MyPrivilege --model MyModel --set name=CustTableListPage\n" +
        "  d365fo designer run set-property --parent-kind table --parent MyTable --model MyModel --set Label=@My:Label\n" +
        "  d365fo designer run new-field --parent-kind table --parent MyTable --model MyModel --set name=Description --set type=string\n\n" +
        DesignerKindCatalog.HelpSummary();
}

public sealed class DesignerKindsCommand : Command<DesignerKindsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--full")]
        [System.ComponentModel.Description("Show actions under each kind, not just the compact parent/child tree.")]
        public bool Full { get; init; }

        [CommandOption("--parent-kind <KIND>")]
        [System.ComponentModel.Description("Filter the tree to one parent kind, e.g. privilege, table, form.")]
        public string? ParentKind { get; init; }

        [CommandOption("-o|--output <FORMAT>")]
        [System.ComponentModel.Description("Output format: json, table, or raw tree text.")]
        public string? Output { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var output = OutputMode.Resolve(settings.Output);
        var tree = DesignerKindCatalog.ToTree(settings.Full, settings.ParentKind);
        if (output == OutputMode.Kind.Raw)
        {
            Console.Out.WriteLine(tree);
            return 0;
        }

        var payload = new
        {
            source = "static",
            parentKind = string.IsNullOrWhiteSpace(settings.ParentKind)
                ? null
                : DesignerKindCatalog.NormalizeKind(settings.ParentKind),
            full = settings.Full,
            tree,
            groups = DesignerKindCatalog.Groups,
            actions = DesignerKindCatalog.Actions,
        };

        return RenderHelpers.Render(output, ToolResult<object>.Success(payload), _ =>
        {
            AnsiConsole.WriteLine(tree);
        });
    }
}

public sealed class DesignerCatalogCommand : Command<DesignerCatalogCommand.Settings>
{
    public sealed class Settings : D365OutputSettings
    {
        [CommandOption("--parent-kind <KIND>")]
        [System.ComponentModel.Description("Parent kind to inspect, e.g. privilege, table, form.")]
        public string? ParentKind { get; init; }

        [CommandOption("--node <PATH>")]
        [System.ComponentModel.Description("Optional child node path, e.g. EntryPoints, Fields, Design/Controls.")]
        public string? Node { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var output = OutputMode.Resolve(settings.Output);
        if (string.IsNullOrWhiteSpace(settings.ParentKind))
        {
            return RenderHelpers.Render(output, ToolResult<object>.Fail("BAD_INPUT", "--parent-kind is required."));
        }

        var args = new JsonObject
        {
            ["parentKind"] = settings.ParentKind,
        };
        if (!string.IsNullOrWhiteSpace(settings.Node)) args["node"] = settings.Node;

        var (ok, error, result) = BridgeGate.TryDesignerCatalog(args);
        if (!ok)
        {
            return RenderHelpers.Render(output, ToolResult<object>.Fail(
                "BRIDGE_DESIGNER_CATALOG_FAILED",
                error ?? "Bridge designer catalog failed.",
                "Use `d365fo designer kinds --full` for offline static discovery."));
        }

        return RenderHelpers.Render(output, ToolResult<object>.Success((object?)result ?? new JsonObject()), RenderCatalogTable);
    }

    private static void RenderCatalogTable(object data)
    {
        if (data is not JsonObject obj)
        {
            Console.Out.WriteLine(D365Json.Serialize(data, indented: true));
            return;
        }

        var tree = (string?)obj["tree"];
        if (!string.IsNullOrWhiteSpace(tree))
        {
            AnsiConsole.WriteLine(tree);
        }

        if (obj["actions"] is JsonArray actions)
        {
            var table = new Table().AddColumn("Action").AddColumn("Path").AddColumn("Creates").AddColumn("Next");
            foreach (var action in actions.OfType<JsonObject>())
            {
                var creates = (string?)action["createsKind"];
                if (string.IsNullOrWhiteSpace(creates) &&
                    string.Equals((string?)action["actionKind"], "property", StringComparison.OrdinalIgnoreCase))
                {
                    creates = "(sets properties)";
                }

                table.AddRow(
                    RenderHelpers.Escape((string?)action["actionId"]),
                    RenderHelpers.Escape((string?)action["appliesToPath"]),
                    RenderHelpers.Escape(creates),
                    RenderHelpers.Escape((string?)action["nextCatalogKind"]));
            }
            AnsiConsole.Write(table);
        }
    }
}

public sealed class DesignerActionsCommand : Command<DesignerActionsCommand.Settings>
{
    public sealed class Settings : D365OutputSettings
    {
        [CommandOption("--parent-kind <KIND>")]
        [System.ComponentModel.Description("Parent object kind, e.g. privilege, table, form.")]
        public string? ParentKind { get; init; }

        [CommandOption("--parent <NAME>")]
        [System.ComponentModel.Description("Parent object name.")]
        public string? Parent { get; init; }

        [CommandOption("--model <MODEL>")]
        [System.ComponentModel.Description("Model containing the parent object.")]
        public string? Model { get; init; }

        [CommandOption("--file <PATH>")]
        [System.ComponentModel.Description("Read parent object XML from a file instead of the metadata provider.")]
        public string? File { get; init; }

        [CommandOption("--node <PATH>")]
        [System.ComponentModel.Description("Optional child node path, e.g. EntryPoints, Fields, Design/Controls.")]
        public string? Node { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var output = OutputMode.Resolve(settings.Output);
        var validation = ValidateParentArgs(settings.ParentKind, settings.Parent, settings.Model, settings.File);
        if (validation is not null)
        {
            return RenderHelpers.Render(output, ToolResult<object>.Fail("BAD_INPUT", validation));
        }

        var args = ParentArgs(settings.ParentKind!, settings.Parent!, settings.Model, settings.File, settings.Node);
        var (ok, error, result) = BridgeGate.TryDesignerActions(args);
        if (!ok)
        {
            return RenderHelpers.Render(output, ToolResult<object>.Fail(
                "BRIDGE_DESIGNER_ACTIONS_FAILED",
                error ?? "Bridge designer actions failed."));
        }

        return RenderHelpers.Render(output, ToolResult<object>.Success((object?)result ?? new JsonObject()));
    }
}

public sealed class DesignerRunCommand : Command<DesignerRunCommand.Settings>
{
    public sealed class Settings : D365OutputSettings
    {
        [CommandArgument(0, "<ACTION_ID>")]
        [System.ComponentModel.Description("Designer action ID, e.g. new-entry-point, new-field, new-control.")]
        public string ActionId { get; init; } = string.Empty;

        [CommandOption("--parent-kind <KIND>")]
        [System.ComponentModel.Description("Parent object kind, e.g. privilege, table, form.")]
        public string? ParentKind { get; init; }

        [CommandOption("--parent <NAME>")]
        [System.ComponentModel.Description("Parent object name.")]
        public string? Parent { get; init; }

        [CommandOption("--model <MODEL>")]
        [System.ComponentModel.Description("Model containing the parent object. Without --out/--dry-run, the bridge updates this object.")]
        public string? Model { get; init; }

        [CommandOption("--file <PATH>")]
        [System.ComponentModel.Description("Read parent object XML from a file instead of the metadata provider.")]
        public string? File { get; init; }

        [CommandOption("--node <PATH>")]
        [System.ComponentModel.Description("Optional child node path. Inferred when the action is unique for the parent kind.")]
        public string? Node { get; init; }

        [CommandOption("--properties <JSON_OR_PATH>")]
        [System.ComponentModel.Description("JSON object or path to a JSON file with action inputs.")]
        public string? Properties { get; init; }

        [CommandOption("--set <KEY=VALUE>")]
        [System.ComponentModel.Description("Set one action property. Can be repeated; overrides --properties values.")]
        public string[] Set { get; init; } = Array.Empty<string>();

        [CommandOption("--dry-run")]
        [System.ComponentModel.Description("Return updated XML without saving through the metadata provider.")]
        public bool DryRun { get; init; }

        [CommandOption("--out <PATH>")]
        [System.ComponentModel.Description("Write returned XML to a file. Implies render mode rather than provider update.")]
        public string? Out { get; init; }

        [CommandOption("--overwrite")]
        [System.ComponentModel.Description("Allow --out to replace an existing file, keeping a .bak backup.")]
        public bool Overwrite { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var output = OutputMode.Resolve(settings.Output);
        if (string.IsNullOrWhiteSpace(settings.ActionId))
        {
            return RenderHelpers.Render(output, ToolResult<object>.Fail("BAD_INPUT", "Action ID is required."));
        }

        var validation = ValidateParentArgs(settings.ParentKind, settings.Parent, settings.Model, settings.File);
        if (validation is not null)
        {
            return RenderHelpers.Render(output, ToolResult<object>.Fail("BAD_INPUT", validation));
        }

        if (!TryLoadProperties(settings.Properties, settings.Set, out var properties, out var propertyError))
        {
            return RenderHelpers.Render(output, ToolResult<object>.Fail("BAD_INPUT", propertyError ?? "Could not load properties."));
        }

        var operation = !string.IsNullOrWhiteSpace(settings.Model) &&
                        string.IsNullOrWhiteSpace(settings.Out) &&
                        !settings.DryRun
            ? "update"
            : "render";

        var args = ParentArgs(settings.ParentKind!, settings.Parent!, settings.Model, settings.File, settings.Node);
        args["actionId"] = settings.ActionId;
        args["operation"] = operation;
        args["dryRun"] = settings.DryRun;
        args["properties"] = properties;

        var (ok, error, result) = BridgeGate.TryDesignerRun(args);
        if (!ok)
        {
            return RenderHelpers.Render(output, ToolResult<object>.Fail(
                "BRIDGE_DESIGNER_RUN_FAILED",
                error ?? "Bridge designer action failed."));
        }

        if (!string.IsNullOrWhiteSpace(settings.Out))
        {
            var xml = (string?)result?["xml"];
            if (string.IsNullOrWhiteSpace(xml))
            {
                return RenderHelpers.Render(output, ToolResult<object>.Fail(
                    "BRIDGE_DESIGNER_RUN_FAILED",
                    "Bridge action succeeded but did not return XML for --out."));
            }

            try
            {
                var write = ScaffoldFileWriter.Write(xml!, settings.Out!, settings.Overwrite);
                if (result is not null)
                {
                    result["outPath"] = write.Path;
                    result["bytes"] = write.Bytes;
                    if (!string.IsNullOrWhiteSpace(write.BackupPath)) result["backup"] = write.BackupPath;
                    result.Remove("xml");
                }
            }
            catch (Exception ex)
            {
                return RenderHelpers.Render(output, ToolResult<object>.Fail(D365FoErrorCodes.WriteFailed, ex.Message));
            }
        }

        return RenderHelpers.Render(output, ToolResult<object>.Success((object?)result ?? new JsonObject()));
    }

    private static bool TryLoadProperties(string? raw, string[] setValues, out JsonObject properties, out string? error)
    {
        properties = new JsonObject();
        error = null;

        if (!string.IsNullOrWhiteSpace(raw))
        {
            string json;
            try
            {
                json = System.IO.File.Exists(raw) ? System.IO.File.ReadAllText(raw) : raw;
            }
            catch (Exception ex)
            {
                error = "Could not read properties JSON: " + ex.Message;
                return false;
            }

            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(json);
            }
            catch (Exception ex)
            {
                error = "Could not parse properties JSON: " + ex.Message;
                return false;
            }

            if (parsed is not JsonObject obj)
            {
                error = "Properties JSON must be an object.";
                return false;
            }

            properties = (JsonObject)obj.DeepClone();
        }

        foreach (var set in setValues)
        {
            var idx = set.IndexOf('=');
            if (idx <= 0)
            {
                error = "--set values must use KEY=VALUE.";
                return false;
            }

            var key = set[..idx].Trim();
            var value = set[(idx + 1)..].Trim();
            if (!DesignerCommandCommon.TrySetPropertyValue(properties, key, value, out error))
            {
                return false;
            }
        }

        return true;
    }
}

public sealed class DesignerPropertiesCommand : Command<DesignerPropertiesCommand.Settings>
{
    public sealed class Settings : D365OutputSettings
    {
        [CommandOption("--parent-kind <KIND>")]
        [System.ComponentModel.Description("Parent object kind, e.g. table, form, privilege.")]
        public string? ParentKind { get; init; }

        [CommandOption("--parent <NAME>")]
        [System.ComponentModel.Description("Parent object name.")]
        public string? Parent { get; init; }

        [CommandOption("--model <MODEL>")]
        [System.ComponentModel.Description("Model containing the parent object.")]
        public string? Model { get; init; }

        [CommandOption("--file <PATH>")]
        [System.ComponentModel.Description("Read parent object XML from a file instead of the metadata provider.")]
        public string? File { get; init; }

        [CommandOption("--node <PATH>")]
        [System.ComponentModel.Description("Optional selected node path, e.g. Fields[[AccountNum]], Design/Controls[[Grid]].")]
        public string? Node { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var output = OutputMode.Resolve(settings.Output);
        var validation = ValidateParentArgs(settings.ParentKind, settings.Parent, settings.Model, settings.File);
        if (validation is not null)
        {
            return RenderHelpers.Render(output, ToolResult<object>.Fail("BAD_INPUT", validation));
        }

        var args = ParentArgs(settings.ParentKind!, settings.Parent!, settings.Model, settings.File, settings.Node);
        var (ok, error, result) = BridgeGate.TryDesignerProperties(args);
        if (!ok)
        {
            return RenderHelpers.Render(output, ToolResult<object>.Fail(
                "BRIDGE_DESIGNER_PROPERTIES_FAILED",
                error ?? "Bridge designer properties failed."));
        }

        return RenderHelpers.Render(output, ToolResult<object>.Success((object?)result ?? new JsonObject()), RenderPropertiesTable);
    }

    private static void RenderPropertiesTable(object data)
    {
        if (data is not JsonObject obj || obj["properties"] is not JsonArray properties)
        {
            Console.Out.WriteLine(D365Json.Serialize(data, indented: true));
            return;
        }

        var table = new Table()
            .AddColumn("Property")
            .AddColumn("Type")
            .AddColumn("Writable")
            .AddColumn("Value")
            .AddColumn("Options");
        foreach (var prop in properties.OfType<JsonObject>())
        {
            var options = prop["options"] is JsonArray opts && opts.Count > 0
                ? string.Join(", ", opts.OfType<JsonObject>().Select(o => (string?)o["name"]).Where(s => !string.IsNullOrWhiteSpace(s)).Take(8))
                : string.Empty;
            table.AddRow(
                RenderHelpers.Escape((string?)prop["name"]),
                RenderHelpers.Escape((string?)prop["type"]),
                ((bool?)prop["writable"] ?? false) ? "yes" : "no",
                RenderHelpers.Escape(prop["value"]?.ToString() ?? string.Empty),
                RenderHelpers.Escape(options));
        }

        AnsiConsole.Write(table);
    }
}

public sealed class DesignerPropertyOptionsCommand : Command<DesignerPropertyOptionsCommand.Settings>
{
    public sealed class Settings : D365OutputSettings
    {
        [CommandOption("--parent-kind <KIND>")]
        public string? ParentKind { get; init; }

        [CommandOption("--parent <NAME>")]
        public string? Parent { get; init; }

        [CommandOption("--model <MODEL>")]
        public string? Model { get; init; }

        [CommandOption("--file <PATH>")]
        public string? File { get; init; }

        [CommandOption("--node <PATH>")]
        public string? Node { get; init; }

        [CommandOption("--property <NAME>")]
        [System.ComponentModel.Description("Property name to inspect for dropdown-style options.")]
        public string? Property { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var output = OutputMode.Resolve(settings.Output);
        var validation = ValidateParentArgs(settings.ParentKind, settings.Parent, settings.Model, settings.File);
        if (validation is not null)
        {
            return RenderHelpers.Render(output, ToolResult<object>.Fail("BAD_INPUT", validation));
        }
        if (string.IsNullOrWhiteSpace(settings.Property))
        {
            return RenderHelpers.Render(output, ToolResult<object>.Fail("BAD_INPUT", "--property is required."));
        }

        var args = ParentArgs(settings.ParentKind!, settings.Parent!, settings.Model, settings.File, settings.Node);
        args["property"] = settings.Property;
        var (ok, error, result) = BridgeGate.TryDesignerPropertyOptions(args);
        if (!ok)
        {
            return RenderHelpers.Render(output, ToolResult<object>.Fail(
                "BRIDGE_DESIGNER_PROPERTY_OPTIONS_FAILED",
                error ?? "Bridge designer property options failed."));
        }

        return RenderHelpers.Render(output, ToolResult<object>.Success((object?)result ?? new JsonObject()));
    }
}

public sealed class DesignerSetPropertyCommand : Command<DesignerSetPropertyCommand.Settings>
{
    public sealed class Settings : DesignerSetSettings
    {
        [CommandOption("--property <NAME>")]
        [System.ComponentModel.Description("Property name to set.")]
        public string? Property { get; init; }

        [CommandOption("--value <VALUE>")]
        [System.ComponentModel.Description("Property value.")]
        public string? Value { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var output = OutputMode.Resolve(settings.Output);
        var validation = ValidateSetArgs(settings, requireProperties: false);
        if (validation is not null)
        {
            return RenderHelpers.Render(output, ToolResult<object>.Fail("BAD_INPUT", validation));
        }

        var properties = new JsonObject();
        if (!DesignerCommandCommon.TrySetPropertyValue(properties, settings.Property!, settings.Value ?? string.Empty, out var propertyError))
        {
            return RenderHelpers.Render(output, ToolResult<object>.Fail("BAD_INPUT", propertyError ?? "Invalid property value."));
        }

        return RunSetProperty(output, settings, properties);
    }
}

public sealed class DesignerSetPropertiesCommand : Command<DesignerSetPropertiesCommand.Settings>
{
    public sealed class Settings : DesignerSetSettings
    {
        [CommandOption("--properties <JSON_OR_PATH>")]
        [System.ComponentModel.Description("JSON object or path to a JSON file with property values.")]
        public string? Properties { get; init; }

        [CommandOption("--set <KEY=VALUE>")]
        [System.ComponentModel.Description("Set one property. Can be repeated; overrides --properties values.")]
        public string[] Set { get; init; } = Array.Empty<string>();
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var output = OutputMode.Resolve(settings.Output);
        var validation = ValidateSetArgs(settings, requireProperties: true);
        if (validation is not null)
        {
            return RenderHelpers.Render(output, ToolResult<object>.Fail("BAD_INPUT", validation));
        }

        if (!DesignerCommandCommon.TryLoadProperties(settings.Properties, settings.Set, out var properties, out var propertyError))
        {
            return RenderHelpers.Render(output, ToolResult<object>.Fail("BAD_INPUT", propertyError ?? "Could not load properties."));
        }

        return RunSetProperty(output, settings, properties);
    }
}

public abstract class DesignerSetSettings : D365OutputSettings
{
    [CommandOption("--parent-kind <KIND>")]
    [System.ComponentModel.Description("Parent object kind, e.g. table, form, privilege.")]
    public string? ParentKind { get; init; }

    [CommandOption("--parent <NAME>")]
    [System.ComponentModel.Description("Parent object name.")]
    public string? Parent { get; init; }

    [CommandOption("--model <MODEL>")]
    [System.ComponentModel.Description("Model containing the parent object. Without --out/--dry-run, the bridge updates this object.")]
    public string? Model { get; init; }

    [CommandOption("--file <PATH>")]
    [System.ComponentModel.Description("Read parent object XML from a file instead of the metadata provider.")]
    public string? File { get; init; }

    [CommandOption("--node <PATH>")]
    [System.ComponentModel.Description("Optional selected node path, e.g. Fields[[AccountNum]], Design/Controls[[Grid]].")]
    public string? Node { get; init; }

    [CommandOption("--dry-run")]
    [System.ComponentModel.Description("Return updated XML without saving through the metadata provider.")]
    public bool DryRun { get; init; }

    [CommandOption("--out <PATH>")]
    [System.ComponentModel.Description("Write returned XML to a file. Implies render mode rather than provider update.")]
    public string? Out { get; init; }

    [CommandOption("--overwrite")]
    [System.ComponentModel.Description("Allow --out to replace an existing file, keeping a .bak backup.")]
    public bool Overwrite { get; init; }
}

internal static class DesignerCommandCommon
{
    internal static JsonObject ParentArgs(string parentKind, string parent, string? model, string? file, string? node)
    {
        var args = new JsonObject
        {
            ["parentKind"] = parentKind,
            ["parent"] = parent,
        };
        if (!string.IsNullOrWhiteSpace(model)) args["model"] = model;
        if (!string.IsNullOrWhiteSpace(file)) args["file"] = file;
        if (!string.IsNullOrWhiteSpace(node)) args["node"] = node;
        return args;
    }

    internal static string? ValidateParentArgs(string? parentKind, string? parent, string? model, string? file)
    {
        if (string.IsNullOrWhiteSpace(parentKind)) return "--parent-kind is required.";
        if (string.IsNullOrWhiteSpace(parent)) return "--parent is required.";
        if (string.IsNullOrWhiteSpace(model) == string.IsNullOrWhiteSpace(file))
        {
            return "Pass exactly one of --model or --file.";
        }
        return null;
    }

    internal static bool TryLoadProperties(string? raw, string[] setValues, out JsonObject properties, out string? error)
    {
        properties = new JsonObject();
        error = null;

        if (!string.IsNullOrWhiteSpace(raw))
        {
            string json;
            try
            {
                json = System.IO.File.Exists(raw) ? System.IO.File.ReadAllText(raw) : raw;
            }
            catch (Exception ex)
            {
                error = "Could not read properties JSON: " + ex.Message;
                return false;
            }

            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(json);
            }
            catch (Exception ex)
            {
                error = "Could not parse properties JSON: " + ex.Message;
                return false;
            }

            if (parsed is not JsonObject obj)
            {
                error = "Properties JSON must be an object.";
                return false;
            }

            properties = (JsonObject)obj.DeepClone();
        }

        foreach (var set in setValues)
        {
            var idx = set.IndexOf('=');
            if (idx <= 0)
            {
                error = "--set values must use KEY=VALUE.";
                return false;
            }

            if (!TrySetPropertyValue(properties, set[..idx].Trim(), set[(idx + 1)..].Trim(), out error))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool TrySetPropertyValue(JsonObject properties, string key, string value, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(key))
        {
            error = "Property key cannot be empty.";
            return false;
        }

        properties[key] = ParseScalar(value);
        return true;
    }

    internal static JsonNode? ParseScalar(string value)
    {
        if (bool.TryParse(value, out var b)) return JsonValue.Create(b);
        if (int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var i))
            return JsonValue.Create(i);
        if (long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var l))
            return JsonValue.Create(l);
        return JsonValue.Create(value);
    }

    internal static string? ValidateSetArgs(DesignerSetSettings settings, bool requireProperties)
    {
        var validation = ValidateParentArgs(settings.ParentKind, settings.Parent, settings.Model, settings.File);
        if (validation is not null) return validation;
        if (requireProperties && settings is DesignerSetPropertiesCommand.Settings multi &&
            string.IsNullOrWhiteSpace(multi.Properties) &&
            (multi.Set == null || multi.Set.Length == 0))
        {
            return "Pass --properties or at least one --set key=value.";
        }
        if (!requireProperties && settings is DesignerSetPropertyCommand.Settings single &&
            string.IsNullOrWhiteSpace(single.Property))
        {
            return "--property is required.";
        }
        return null;
    }

    internal static int RunSetProperty(OutputMode.Kind output, DesignerSetSettings settings, JsonObject properties)
    {
        var operation = !string.IsNullOrWhiteSpace(settings.Model) &&
                        string.IsNullOrWhiteSpace(settings.Out) &&
                        !settings.DryRun
            ? "update"
            : "render";

        var args = ParentArgs(settings.ParentKind!, settings.Parent!, settings.Model, settings.File, settings.Node);
        args["actionId"] = "set-property";
        args["operation"] = operation;
        args["dryRun"] = settings.DryRun;
        args["properties"] = properties;

        var (ok, error, result) = BridgeGate.TryDesignerRun(args);
        if (!ok)
        {
            return RenderHelpers.Render(output, ToolResult<object>.Fail(
                "BRIDGE_DESIGNER_SET_PROPERTY_FAILED",
                error ?? "Bridge designer set-property failed."));
        }

        if (!string.IsNullOrWhiteSpace(settings.Out))
        {
            var xml = (string?)result?["xml"];
            if (string.IsNullOrWhiteSpace(xml))
            {
                return RenderHelpers.Render(output, ToolResult<object>.Fail(
                    "BRIDGE_DESIGNER_SET_PROPERTY_FAILED",
                    "Bridge action succeeded but did not return XML for --out."));
            }

            try
            {
                var write = ScaffoldFileWriter.Write(xml!, settings.Out!, settings.Overwrite);
                if (result is not null)
                {
                    result["outPath"] = write.Path;
                    result["bytes"] = write.Bytes;
                    if (!string.IsNullOrWhiteSpace(write.BackupPath)) result["backup"] = write.BackupPath;
                    result.Remove("xml");
                }
            }
            catch (Exception ex)
            {
                return RenderHelpers.Render(output, ToolResult<object>.Fail(D365FoErrorCodes.WriteFailed, ex.Message));
            }
        }

        return RenderHelpers.Render(output, ToolResult<object>.Success((object?)result ?? new JsonObject()));
    }
}
