using D365FO.Core;
using D365FO.Core.FormPatterns;
using D365FO.Core.Scaffolding;
using Spectre.Console.Cli;
using D365FO.Cli.Commands.Get;
using System.Text.Json.Nodes;

namespace D365FO.Cli.Commands.Generate;

public abstract class GenerateSettings : D365OutputSettings
{
    [CommandOption("--out <PATH>")]
    [System.ComponentModel.Description("Output file path. Required unless --install-to is used.")]
    public string? Out { get; init; }

    [CommandOption("--overwrite")]
    public bool Overwrite { get; init; }

    [CommandOption("--install-to <MODEL>")]
    [System.ComponentModel.Description("Install the generated artefact directly into <MODEL>. Bridge backend saves through IMetadataProvider; legacy resolves the model path and writes XML.")]
    public string? InstallTo { get; init; }

    [CommandOption("--backend <BACKEND>")]
    [System.ComponentModel.Description("Scaffolding backend: auto (default), bridge, or legacy. auto uses bridge unless D365FO_BRIDGE_ENABLED=0.")]
    public string? Backend { get; init; }

    [CommandOption("--grounding-token <TOKEN>")]
    [System.ComponentModel.Description("Grounding token from `d365fo prepare change`/`prepare create` proving the index was consulted. Required for extension-shaped objects when D365FO_GROUNDING_ENFORCE=true.")]
    public string? GroundingToken { get; init; }
}

internal static class GenerateInstaller
{
    /// <summary>
    /// Resolve the on-disk install path for a scaffolded artefact. When
    /// <c>--install-to &lt;MODEL&gt;</c> is supplied we ask the bridge where
    /// the model lives on disk and compose
    /// <c>&lt;modelFolder&gt;/Ax&lt;Kind&gt;/&lt;Name&gt;.xml</c> — the
    /// canonical location Visual Studio and the D365FO build tools expect.
    /// The caller then invokes the regular <see cref="ScaffoldFileWriter"/>
    /// against this path. Returns null on failure and renders an error into
    /// <paramref name="failure"/>.
    /// </summary>
    internal static string? ResolveInstallPath(OutputMode.Kind kind, string axSubfolder, string name, string model, out int? failure)
    {
        failure = null;
        var folder = BridgeGate.TryGetModelFolder(model);
        if (string.IsNullOrEmpty(folder))
        {
            failure = RenderHelpers.Render(kind, ToolResult<object>.Fail(
                "INSTALL_FAILED",
                $"Could not resolve folder for model '{model}'. Ensure the bridge can see the model: point D365FO_PACKAGES_PATH (and D365FO_CUSTOM_PACKAGES_PATH for custom-model roots) at the directories that contain the model, on a D365FO VM. Set D365FO_BRIDGE_ENABLED=0 only if you want to disable bridge-backed installs."));
            return null;
        }
        return System.IO.Path.Combine(folder!, axSubfolder, name + ".xml");
    }
}

public sealed class GenerateTableCommand : Command<GenerateTableCommand.Settings>
{
    public sealed class Settings : GenerateSettings
    {
        [CommandArgument(0, "<NAME>")]
        public string Name { get; init; } = "";

        [CommandOption("--label <KEY>")]
        public string? Label { get; init; }

        [CommandOption("--field <SPEC>")]
        [System.ComponentModel.Description("Repeatable: <name>:<edt>[[:mandatory]]. Example: --field AccountNum:CustAccount:mandatory")]
        public string[] Fields { get; init; } = Array.Empty<string>();

        [CommandOption("--pattern <PATTERN>")]
        [System.ComponentModel.Description("Business-role preset: main|transaction|parameter|group|worksheetheader|worksheetline|reference|framework|miscellaneous. Sets <TableGroup> and provides default fields when --field is empty. Aliases: master, setup, config, transactional, lookup …")]
        public string? Pattern { get; init; }

        [CommandOption("--table-type <TYPE>")]
        [System.ComponentModel.Description("Storage kind: RegularTable|TempDB|InMemory (default RegularTable). NEVER pass TempDB to --pattern — it is a TableType, not a TableGroup.")]
        public string? TableType { get; init; }

        [CommandOption("--primary-key <FIELD>")]
        [System.ComponentModel.Description("Repeatable: field name(s) to compose the alternate-key index. Defaults to all mandatory fields, or the first field if none mandatory.")]
        public string[] PrimaryKey { get; init; } = Array.Empty<string>();
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);
        if (string.IsNullOrWhiteSpace(settings.Name))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("BAD_INPUT", "Table name required."));
        if (!TablePatternNormalizer.TryNormalize(settings.Pattern, out var pattern, out var perr))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("BAD_INPUT", perr!));
        if (!TablePatternNormalizer.TryNormalizeStorage(settings.TableType, out var storage, out var serr))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("BAD_INPUT", serr!));
        if (!GenerateBackendResolver.TryResolve(settings.Backend, out var backend, out var backendError))
            return GenerateBridgeScaffolding.RenderBackendError(kind, backendError!);
        var useBridge = GenerateBackendResolver.ShouldUseBridge(backend);

        var hasInstall = !string.IsNullOrWhiteSpace(settings.InstallTo);
        var hasOut     = !string.IsNullOrWhiteSpace(settings.Out);
        if (!hasInstall && !hasOut)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("BAD_INPUT", "--out or --install-to is required."));
        var outPath = settings.Out;
        if (hasInstall && !hasOut && !useBridge)
        {
            outPath = GenerateInstaller.ResolveInstallPath(kind, "AxTable", settings.Name, settings.InstallTo!, out var fail);
            if (fail.HasValue) return fail.Value;
        }

        var fields2 = settings.Fields.Select(ParseField).ToList();
        var effectiveFields = EffectiveFields(fields2, pattern).ToList();
        var pkNames = PrimaryKeyFields(effectiveFields, settings.PrimaryKey).ToList();
        var tableProperties = new JsonObject();
        if (!string.IsNullOrWhiteSpace(settings.Label)) tableProperties["Label"] = settings.Label;
        if (pattern != TablePattern.None) tableProperties["TableGroup"] = pattern.ToString();
        if (storage != TableStorage.RegularTable) tableProperties["TableType"] = storage.ToString();
        if (pkNames.Count > 0) tableProperties["ClusteredIndex"] = "PrimaryIdx";
        var bridgeActions = BuildTableDesignerActions(effectiveFields, pkNames);
        var bridge = GenerateBridgeScaffolding.TryWrite(
            kind, backend, settings.InstallTo, settings.Overwrite, "AxTable", settings.Name, null, outPath,
            warnings: null,
            properties: tableProperties,
            designerActions: bridgeActions,
            successFactory: summary => new
            {
                kind = "AxTable",
                name = settings.Name,
                path = summary.Path,
                bytes = summary.Bytes,
                backup = summary.BackupPath,
                fieldCount = effectiveFields.Count,
                pattern = pattern == TablePattern.None ? null : pattern.ToString(),
                tableType = storage == TableStorage.RegularTable ? null : storage.ToString(),
                usedPatternDefaults = fields2.Count == 0 && pattern != TablePattern.None,
                model = summary.Model,
                backend = "bridge",
                source = summary.Source,
                operation = summary.Operation,
                designerActions = summary.DesignerActions,
            });
        if (bridge.Handled) return bridge.ExitCode;

        var doc = XppScaffolder.Table(settings.Name, settings.Label, fields2, pattern, storage, settings.PrimaryKey);
        try
        {
            var res = ScaffoldFileWriter.Write(doc, outPath!, settings.Overwrite);
            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                kind = "AxTable",
                name = settings.Name,
                path = res.Path,
                bytes = res.Bytes,
                backup = res.BackupPath,
                fieldCount = fields2.Count > 0 ? fields2.Count : TablePatternPresets.DefaultFieldsFor(pattern).Count,
                pattern = pattern == TablePattern.None ? null : pattern.ToString(),
                tableType = storage == TableStorage.RegularTable ? null : storage.ToString(),
                usedPatternDefaults = fields2.Count == 0 && pattern != TablePattern.None,
                model = settings.InstallTo,
            }));
        }
        catch (Exception ex)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("WRITE_FAILED", ex.Message));
        }
    }

    private static TableFieldSpec ParseField(string raw)
    {
        var parts = raw.Split(':', StringSplitOptions.TrimEntries);
        var name = parts.Length > 0 ? parts[0] : "";
        var edt = parts.Length > 1 ? parts[1] : null;
        var mandatory = parts.Length > 2 && string.Equals(parts[2], "mandatory", StringComparison.OrdinalIgnoreCase);
        return new TableFieldSpec(name, string.IsNullOrEmpty(edt) ? null : edt, null, mandatory);
    }

    internal static IEnumerable<TableFieldSpec> EffectiveFields(IReadOnlyList<TableFieldSpec> supplied, TablePattern pattern) =>
        supplied.Count > 0 ? supplied : TablePatternPresets.DefaultFieldsFor(pattern);

    internal static IEnumerable<string> PrimaryKeyFields(IReadOnlyList<TableFieldSpec> effectiveFields, IEnumerable<string> requested)
    {
        var pkNames = requested
            .Where(n => !string.IsNullOrWhiteSpace(n) &&
                        effectiveFields.Any(f => string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (pkNames.Count == 0)
        {
            pkNames = effectiveFields.Where(f => f.Mandatory).Select(f => f.Name).ToList();
        }
        if (pkNames.Count == 0 && effectiveFields.Count > 0)
        {
            pkNames.Add(effectiveFields[0].Name);
        }

        return pkNames;
    }

    internal static IReadOnlyList<BridgeDesignerAction> BuildTableDesignerActions(
        IReadOnlyList<TableFieldSpec> fields,
        IReadOnlyList<string> primaryKeyFields)
    {
        var actions = new List<BridgeDesignerAction>();
        foreach (var field in fields)
        {
            var props = new JsonObject
            {
                ["name"] = field.Name,
                ["type"] = "string",
                ["extendedDataType"] = field.Edt ?? "Name",
            };
            if (!string.IsNullOrWhiteSpace(field.Label)) props["Label"] = field.Label;
            if (field.Mandatory) props["Mandatory"] = "Yes";
            actions.Add(new BridgeDesignerAction("new-field", "table", null, props));
        }

        if (fields.Count > 0)
        {
            actions.Add(new BridgeDesignerAction(
                "new-field-group",
                "table",
                null,
                new JsonObject { ["name"] = "AutoReport" }));
        }

        if (primaryKeyFields.Count > 0)
        {
            actions.Add(new BridgeDesignerAction(
                "new-index",
                "table",
                null,
                new JsonObject
                {
                    ["name"] = "PrimaryIdx",
                    ["AlternateKey"] = "Yes",
                    ["AllowDuplicates"] = "No",
                }));

            foreach (var fieldName in primaryKeyFields)
            {
                actions.Add(new BridgeDesignerAction(
                    "new-index-field",
                    "table",
                    "Indexes[PrimaryIdx]/Fields",
                    new JsonObject
                    {
                        ["name"] = fieldName,
                        ["DataField"] = fieldName,
                    }));
            }
        }

        return actions;
    }
}

public sealed class GenerateClassCommand : Command<GenerateClassCommand.Settings>
{
    public sealed class Settings : GenerateSettings
    {
        [CommandArgument(0, "<NAME>")]
        public string Name { get; init; } = "";

        [CommandOption("--extends <BASE>")]
        public string? Extends { get; init; }

        [CommandOption("--non-final")]
        public bool NonFinal { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);
        if (string.IsNullOrWhiteSpace(settings.Name))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("BAD_INPUT", "Class name required."));
        if (!GenerateBackendResolver.TryResolve(settings.Backend, out var backend, out var backendError))
            return GenerateBridgeScaffolding.RenderBackendError(kind, backendError!);
        var useBridge = GenerateBackendResolver.ShouldUseBridge(backend);
        var hasInstall = !string.IsNullOrWhiteSpace(settings.InstallTo);
        var hasOut     = !string.IsNullOrWhiteSpace(settings.Out);
        if (!hasInstall && !hasOut)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("BAD_INPUT", "--out or --install-to is required."));
        var outPath = settings.Out;
        if (hasInstall && !hasOut && !useBridge)
        {
            outPath = GenerateInstaller.ResolveInstallPath(kind, "AxClass", settings.Name, settings.InstallTo!, out var fail);
            if (fail.HasValue) return fail.Value;
        }

        var bridgeWarnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.Extends) || settings.NonFinal)
        {
            bridgeWarnings.Add("Bridge backend uses the VS Add New Item class template; --extends and --non-final are legacy-only for now.");
        }
        var bridge = GenerateBridgeScaffolding.TryWrite(
            kind, backend, settings.InstallTo, settings.Overwrite, "AxClass", settings.Name, null, outPath,
            bridgeWarnings.Count > 0 ? bridgeWarnings : null,
            new JsonObject());
        if (bridge.Handled) return bridge.ExitCode;

        var doc = XppScaffolder.Class(settings.Name, settings.Extends, !settings.NonFinal);
        try
        {
            var res = ScaffoldFileWriter.Write(doc, outPath!, settings.Overwrite);
            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                kind = "AxClass", name = settings.Name, path = res.Path, bytes = res.Bytes, backup = res.BackupPath, model = settings.InstallTo,
            }));
        }
        catch (Exception ex)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("WRITE_FAILED", ex.Message));
        }
    }
}

public sealed class GenerateCocCommand : Command<GenerateCocCommand.Settings>
{
    public sealed class Settings : GenerateSettings
    {
        [CommandArgument(0, "<TARGET>")]
        [System.ComponentModel.Description("Target class name. Extension will be named <TARGET>_Extension.")]
        public string Target { get; init; } = "";

        [CommandOption("--method <NAME>")]
        [System.ComponentModel.Description("Repeatable. Each method gets a `next` wrapper.")]
        public string[] Methods { get; init; } = Array.Empty<string>();
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);
        if (string.IsNullOrWhiteSpace(settings.Target))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("BAD_INPUT", "Target class required."));
        if (settings.Methods.Length == 0)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("BAD_INPUT", "At least one --method required."));
        var hasInstall = !string.IsNullOrWhiteSpace(settings.InstallTo);
        var hasOut     = !string.IsNullOrWhiteSpace(settings.Out);
        if (!hasInstall && !hasOut)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("BAD_INPUT", "--out or --install-to is required."));
        var outPath = settings.Out;
        if (hasInstall && !hasOut)
        {
            outPath = GenerateInstaller.ResolveInstallPath(kind, "AxClass", settings.Target + "_Extension", settings.InstallTo!, out var fail);
            if (fail.HasValue) return fail.Value;
        }

        // Guardrail: warn if the target already has CoC wrappers, and resolve
        // the target's AOT kind so [ExtensionOf] uses the right intrinsic
        // (tableStr for tables, classStr for classes, …).
        var warnings = new List<string>();
        var targetKind = "class";
        try
        {
            var repo = RepoFactory.Create();
            var existing = repo.FindCocExtensions(settings.Target);
            if (existing.Count > 0)
                warnings.Add($"There are already {existing.Count} CoC extension(s) of {settings.Target}. Consider extending an existing one instead of stacking a new wrapper.");
            var kinds = repo.SymbolKinds(settings.Target);
            targetKind = kinds.FirstOrDefault(k => k is "class" or "table" or "form" or "data-entity" or "map" or "view") ?? "class";
        }
        catch { /* index may be empty; not fatal */ }

        var doc = XppScaffolder.CocExtension(settings.Target, targetKind, settings.Methods);

        // Grounding gate: prove the target and every wrapped method against the
        // index; fail closed under D365FO_GROUNDING_ENFORCE=true.
        var gate = GroundingGate.Check(
            settings.GroundingToken,
            settings.Target,
            doc,
            settings.Methods.Select(m => (settings.Target, m)));
        if (gate.Failure is not null) return RenderHelpers.Render(kind, gate.Failure);
        warnings.AddRange(gate.Warnings);

        try
        {
            var res = ScaffoldFileWriter.Write(doc, outPath!, settings.Overwrite);
            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                kind = "AxClass",
                name = settings.Target + "_Extension",
                path = res.Path,
                bytes = res.Bytes,
                backup = res.BackupPath,
                methodCount = settings.Methods.Length,
                model = settings.InstallTo,
                grounding = gate.Grounding,
            }, warnings: warnings));
        }
        catch (Exception ex)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("WRITE_FAILED", ex.Message));
        }
    }
}

public sealed class GenerateSimpleListCommand : Command<GenerateSimpleListCommand.Settings>
{
    public sealed class Settings : GenerateSettings
    {
        [CommandArgument(0, "<FORM_NAME>")]
        public string FormName { get; init; } = "";

        [CommandOption("--table <TABLE>")]
        public string? Table { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        return GenerateFormImpl.Run(
            output:       settings.Output,
            formName:     settings.FormName,
            table:        settings.Table,
            patternRaw:   "SimpleList",
            caption:      null,
            fields:       Array.Empty<string>(),
            sections:     Array.Empty<string>(),
            linesTable:   null,
            outPath:      settings.Out,
            installTo:    settings.InstallTo,
            overwrite:    settings.Overwrite,
            backendRaw:   settings.Backend);
    }
}

/// <summary>
/// Pattern-aware form scaffolder. Mirrors <c>generate_smart_form</c> from
/// <c>d365fo-mcp-server</c>: nine D365FO patterns, optional grid fields,
/// optional sections (TabPages for TOC / Dialog / Workspace), optional lines
/// datasource for <c>DetailsTransaction</c>.
/// </summary>
public sealed class GenerateFormCommand : Command<GenerateFormCommand.Settings>
{
    public sealed class Settings : GenerateSettings
    {
        [CommandArgument(0, "<FORM_NAME>")]
        public string FormName { get; init; } = "";

        [CommandOption("--pattern <PATTERN>")]
        [System.ComponentModel.Description("Form pattern: SimpleList | SimpleListDetails | DetailsMaster | DetailsTransaction | Dialog | TableOfContents | Lookup | ListPage | Workspace. Aliases (master, transaction, toc, panorama, …) are accepted.")]
        public string? Pattern { get; init; }

        [CommandOption("--table <TABLE>")]
        [System.ComponentModel.Description("Primary datasource table.")]
        public string? Table { get; init; }

        [CommandOption("--caption <TEXT>")]
        [System.ComponentModel.Description("Caption / title (literal text or @File:Label).")]
        public string? Caption { get; init; }

        [CommandOption("--field <NAME>")]
        [System.ComponentModel.Description("Field name to render as a grid / detail column (repeatable).")]
        public string[] Fields { get; init; } = Array.Empty<string>();

        [CommandOption("--section <SECTION>")]
        [System.ComponentModel.Description("TabPage / section (repeatable). Format: <Name>:<Caption>. Used by TableOfContents, Dialog, Workspace.")]
        public string[] Sections { get; init; } = Array.Empty<string>();

        [CommandOption("--lines-table <TABLE>")]
        [System.ComponentModel.Description("Lines datasource table for DetailsTransaction.")]
        public string? LinesTable { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        return GenerateFormImpl.Run(
            output:       settings.Output,
            formName:     settings.FormName,
            table:        settings.Table,
            patternRaw:   settings.Pattern,
            caption:      settings.Caption,
            fields:       settings.Fields,
            sections:     settings.Sections,
            linesTable:   settings.LinesTable,
            outPath:      settings.Out,
            installTo:    settings.InstallTo,
            overwrite:    settings.Overwrite,
            backendRaw:   settings.Backend);
    }
}

internal static class GenerateFormImpl
{
    public static int Run(
        string? output,
        string formName,
        string? table,
        string? patternRaw,
        string? caption,
        IReadOnlyList<string> fields,
        IReadOnlyList<string> sections,
        string? linesTable,
        string? outPath,
        string? installTo,
        bool overwrite,
        string? backendRaw)
    {
        var kind = OutputMode.Resolve(output);
        if (string.IsNullOrWhiteSpace(formName))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("BAD_INPUT", "Form name required."));
        if (!GenerateBackendResolver.TryResolve(backendRaw, out var backend, out var backendError))
            return GenerateBridgeScaffolding.RenderBackendError(kind, backendError!);
        var useBridge = GenerateBackendResolver.ShouldUseBridge(backend);

        var pattern = FormPatternNormalizer.Normalize(patternRaw);

        // Patterns that need a datasource: everything except Dialog / TableOfContents (where it is optional).
        var dsRequired = pattern is not (FormPattern.Dialog or FormPattern.TableOfContents);
        if (dsRequired && string.IsNullOrWhiteSpace(table))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("BAD_INPUT", $"--table <TABLE> required for pattern {pattern}."));

        var hasInstall = !string.IsNullOrWhiteSpace(installTo);
        var hasOut     = !string.IsNullOrWhiteSpace(outPath);
        if (!hasInstall && !hasOut)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("BAD_INPUT", "--out or --install-to is required."));

        if (hasInstall && !hasOut && !useBridge)
        {
            outPath = GenerateInstaller.ResolveInstallPath(kind, "AxForm", formName, installTo!, out var fail);
            if (fail.HasValue) return fail.Value;
        }

        var sectionSpecs = ParseSections(sections);
        var formProperties = BuildFormProperties(pattern, caption);
        var bridgeActions = BuildFormDesignerActions(formName, table, pattern, fields, sectionSpecs, linesTable);
        var bridge = GenerateBridgeScaffolding.TryWrite(
            kind, backend, installTo, overwrite, "AxForm", formName, null, outPath,
            warnings: null,
            properties: formProperties,
            designerActions: bridgeActions,
            successFactory: summary => new
            {
                kind = "AxForm",
                name = formName,
                pattern = pattern.ToString(),
                path = summary.Path,
                bytes = summary.Bytes,
                backup = summary.BackupPath,
                model = summary.Model,
                fieldCount = fields.Count,
                sectionCount = sectionSpecs.Count,
                backend = "bridge",
                source = summary.Source,
                operation = summary.Operation,
                designerActions = summary.DesignerActions,
            });
        if (bridge.Handled) return bridge.ExitCode;

        string xml;
        try
        {
            xml = XppScaffolder.Form(
                formName:        formName,
                dataSourceTable: table,
                pattern:         pattern,
                caption:         caption,
                gridFields:      fields,
                sections:        sectionSpecs,
                linesTable:      linesTable);
        }
        catch (Exception ex)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("RENDER_FAILED", ex.Message));
        }

        // Pre-write pattern self-test: structural violations (FP001-FP005, FP007)
        // block the write while D365FO_FORM_PATTERN_ENFORCE=true (the default),
        // mirroring the upstream MCP form-pattern write gate.
        var patternReport = D365FO.Core.FormPatterns.FormPatternValidator.ValidateXml(xml);
        var patternWarnings = patternReport.Violations
            .Select(v => $"form-pattern {v.Rule} [{v.Severity}] {v.Path}: {v.Excerpt}")
            .ToList();
        if (patternReport.HasErrors && FormPatternGate.EnforcementEnabled)
        {
            var errors = patternReport.Violations.Where(v => v.Severity == "error")
                .Select(v => $"{v.Rule} {v.Path}: {v.Excerpt} → {v.Fix}");
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(
                "FORM_PATTERN_VIOLATION",
                $"Generated form violates pattern {patternReport.Pattern} (D365FO_FORM_PATTERN_ENFORCE=true):\n" +
                string.Join("\n", errors),
                "Fix the structure (see `d365fo get form-pattern " + (patternReport.Pattern ?? "<pattern>") + "`), " +
                "or set D365FO_FORM_PATTERN_ENFORCE=false to bypass the gate."));
        }

        try
        {
            var res = ScaffoldFileWriter.Write(xml, outPath!, overwrite);
            return RenderHelpers.Render(kind, ToolResult<object>.Success(new
            {
                kind         = "AxForm",
                name         = formName,
                pattern      = pattern.ToString(),
                path         = res.Path,
                bytes        = res.Bytes,
                backup       = res.BackupPath,
                model        = installTo,
                fieldCount   = fields.Count,
                sectionCount = sectionSpecs.Count,
                patternCheck = new
                {
                    enforced = FormPatternGate.EnforcementEnabled,
                    errors   = patternReport.ErrorCount,
                    warnings = patternReport.WarningCount,
                },
            }, patternWarnings.Count > 0 ? patternWarnings : null));
        }
        catch (Exception ex)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("WRITE_FAILED", ex.Message));
        }
    }

    internal static JsonObject BuildFormProperties(FormPattern pattern, string? caption)
    {
        var design = new JsonObject
        {
            ["Pattern"] = pattern.ToString(),
            ["PatternVersion"] = FormPatternCatalog.ResolveExact(pattern.ToString())?.Versions.FirstOrDefault() ?? "1.0",
        };

        var spec = FormPatternCatalog.ResolveExact(pattern.ToString());
        if (spec?.DesignProperties is not null)
        {
            foreach (var property in spec.DesignProperties)
            {
                design[property.Key] = property.Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(caption))
        {
            design["Caption"] = caption;
        }

        return new JsonObject
        {
            ["Design"] = design,
        };
    }

    internal static IReadOnlyList<BridgeDesignerAction> BuildFormDesignerActions(
        string formName,
        string? table,
        FormPattern pattern,
        IReadOnlyList<string> fields,
        IReadOnlyList<FormSectionSpec> sections,
        string? linesTable)
    {
        var actions = new List<BridgeDesignerAction>();
        var dsName = string.IsNullOrWhiteSpace(table) ? null : table;
        if (!string.IsNullOrWhiteSpace(table))
        {
            actions.Add(new BridgeDesignerAction(
                "new-data-source",
                "form",
                null,
                new JsonObject
                {
                    ["name"] = dsName,
                    ["table"] = table,
                }));
        }

        switch (pattern)
        {
            case FormPattern.SimpleList:
            case FormPattern.ListPage:
                AddActionPane(actions);
                AddFilterGroup(actions);
                AddGrid(actions, "Grid", dsName);
                AddFieldControls(actions, ControlChildrenPath("Grid"), "Grid", fields, dsName);
                break;

            case FormPattern.Lookup:
                AddFilterGroup(actions);
                AddGrid(actions, "Grid", dsName);
                AddFieldControls(actions, ControlChildrenPath("Grid"), "Grid", fields, dsName);
                break;

            case FormPattern.SimpleListDetails:
                AddActionPane(actions);
                AddGroup(actions, null, "GridContainer", "SidePanel", pattern: "SidePanel", patternVersion: "1.0", style: "SidePanel");
                AddGrid(actions, "NavigationList", dsName, ControlChildrenPath("GridContainer"), style: "List");
                AddFieldControls(actions, ControlChildrenPath("GridContainer", "NavigationList"), "List", fields.Take(3).ToArray(), dsName);
                AddGroup(actions, null, "DetailsGroup", "Group", pattern: "FieldsFieldGroups", patternVersion: "1.1");
                AddFieldControls(actions, ControlChildrenPath("DetailsGroup"), "Overview", fields, dsName);
                break;

            case FormPattern.DetailsMaster:
                AddActionPane(actions);
                AddFilterGroup(actions);
                AddTab(actions, "Tab", style: "FastTabs");
                AddTabPage(actions, ControlChildrenPath("Tab"), "Overview", "Overview", pattern: "FieldsFieldGroups", patternVersion: "1.1");
                AddFieldControls(actions, ControlChildrenPath("Tab", "Overview"), "Overview", fields, dsName);
                break;

            case FormPattern.DetailsTransaction:
                AddActionPane(actions);
                AddFilterGroup(actions);
                var linesDs = string.IsNullOrWhiteSpace(linesTable) ? (string.IsNullOrWhiteSpace(dsName) ? formName + "Lines" : dsName + "Lines") : linesTable;
                if (!string.IsNullOrWhiteSpace(linesDs))
                {
                    actions.Add(new BridgeDesignerAction(
                        "new-data-source",
                        "form",
                        null,
                        new JsonObject
                        {
                            ["name"] = linesDs,
                            ["table"] = linesDs,
                            ["JoinSource"] = dsName,
                        }));
                }

                AddTab(actions, "Tab", style: "FastTabs");
                AddTabPage(actions, ControlChildrenPath("Tab"), "Header", "Header", pattern: "FieldsFieldGroups", patternVersion: "1.1");
                AddFieldControls(actions, ControlChildrenPath("Tab", "Header"), "Header", fields, dsName);
                AddTabPage(actions, ControlChildrenPath("Tab"), "Lines", "Lines");
                AddGrid(actions, "LinesGrid", linesDs, ControlChildrenPath("Tab", "Lines"));
                break;

            case FormPattern.Dialog:
                AddGroup(actions, null, "DialogBody", "Group", pattern: "FieldsFieldGroups", patternVersion: "1.1", style: "DialogContent");
                AddFieldControls(actions, ControlChildrenPath("DialogBody"), "Dialog", fields, dsName);
                AddButtonGroup(actions, null, "ButtonGroup", style: "DialogCommitContainer");
                AddCommandButton(actions, ControlChildrenPath("ButtonGroup"), "OK");
                break;

            case FormPattern.TableOfContents:
                AddActionPane(actions);
                AddTab(actions, "TOCTabs", style: "TOCList");
                var tocSections = sections.Count > 0
                    ? sections
                    : new[] { new FormSectionSpec("TabPageGeneral", "General"), new FormSectionSpec("TabPageSetup", "Setup") };
                foreach (var section in tocSections)
                {
                    AddTabPage(actions, ControlChildrenPath("TOCTabs"), section.Name, section.Caption, pattern: "FieldsFieldGroups", patternVersion: "1.1");
                }
                break;

            case FormPattern.Workspace:
                AddActionPane(actions);
                AddTab(actions, "PanoramaBody", style: "Panorama");
                var workspaceSections = sections.Count > 0
                    ? sections
                    : new[] { new FormSectionSpec("Overview", "Overview") };
                foreach (var section in workspaceSections)
                {
                    AddTabPage(actions, ControlChildrenPath("PanoramaBody"), section.Name + "Section", section.Caption);
                    AddGrid(actions, section.Name + "Grid", dsName, ControlChildrenPath("PanoramaBody", section.Name + "Section"));
                }
                break;
        }

        return actions;
    }

    private static void AddActionPane(List<BridgeDesignerAction> actions) =>
        AddControl(actions, null, "ActionPane", "actionPane", "ActionPane");

    private static void AddFilterGroup(List<BridgeDesignerAction> actions)
    {
        AddGroup(actions, null, "CustomFilterGroup", "Group", pattern: "CustomAndQuickFilters", patternVersion: "1.1", style: "CustomFilter");
        AddExtensionControl(actions, ControlChildrenPath("CustomFilterGroup"), "QuickFilterControl", "QuickFilterControl");
    }

    private static void AddGrid(List<BridgeDesignerAction> actions, string name, string? dataSource, string? node = null, string? style = "Tabular")
    {
        var extra = new Dictionary<string, string?>
        {
            ["DataSource"] = dataSource,
            ["ShowRowLabels"] = "No",
            ["Style"] = style,
        };
        AddControl(actions, node, name, "grid", "Grid", extra);
    }

    private static void AddGroup(
        List<BridgeDesignerAction> actions,
        string? node,
        string name,
        string type,
        string? pattern = null,
        string? patternVersion = null,
        string? style = null)
    {
        AddControl(actions, node, name, "group", type, new Dictionary<string, string?>
        {
            ["Pattern"] = pattern,
            ["PatternVersion"] = patternVersion,
            ["Style"] = style,
        });
    }

    private static void AddTab(List<BridgeDesignerAction> actions, string name, string? style = null)
    {
        AddControl(actions, null, name, "tab", "Tab", new Dictionary<string, string?>
        {
            ["Style"] = style,
        });
    }

    private static void AddTabPage(
        List<BridgeDesignerAction> actions,
        string node,
        string name,
        string? caption,
        string? pattern = null,
        string? patternVersion = null)
    {
        AddControl(actions, node, name, "tabPage", "TabPage", new Dictionary<string, string?>
        {
            ["Caption"] = caption,
            ["Pattern"] = pattern,
            ["PatternVersion"] = patternVersion,
        });
    }

    private static void AddButtonGroup(List<BridgeDesignerAction> actions, string? node, string name, string? style = null)
    {
        AddControl(actions, node, name, "buttonGroup", "ButtonGroup", new Dictionary<string, string?>
        {
            ["Style"] = style,
        });
    }

    private static void AddCommandButton(List<BridgeDesignerAction> actions, string? node, string name)
    {
        AddControl(actions, node, name, "commandButton", "CommandButton", new Dictionary<string, string?>
        {
            ["Command"] = name,
            ["Text"] = name,
        });
    }

    private static void AddFieldControls(
        List<BridgeDesignerAction> actions,
        string node,
        string prefix,
        IReadOnlyList<string> fields,
        string? dataSource)
    {
        foreach (var field in fields.Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            AddControl(actions, node, prefix + "_" + field, "string", "String", new Dictionary<string, string?>
            {
                ["DataField"] = field,
                ["DataSource"] = dataSource,
            });
        }
    }

    private static void AddControl(
        List<BridgeDesignerAction> actions,
        string? node,
        string name,
        string controlType,
        string type,
        IReadOnlyDictionary<string, string?>? extra = null)
    {
        var props = new JsonObject
        {
            ["name"] = name,
            ["controlType"] = controlType,
        };

        if (extra is not null)
        {
            foreach (var property in extra)
            {
                if (!string.IsNullOrWhiteSpace(property.Value))
                {
                    props[property.Key] = property.Value;
                }
            }
        }

        actions.Add(new BridgeDesignerAction("new-control", "form", node, props));
    }

    private static void AddExtensionControl(
        List<BridgeDesignerAction> actions,
        string? node,
        string name,
        string extensionName)
    {
        var props = new JsonObject
        {
            ["name"] = name,
            ["controlType"] = "control",
            ["FormControlExtension"] = new JsonObject
            {
                ["Name"] = extensionName,
            },
        };

        actions.Add(new BridgeDesignerAction("new-control", "form", node, props));
    }

    private static string ControlChildrenPath(params string[] controls)
    {
        var path = "Design";
        foreach (var control in controls)
        {
            path += "/Controls[" + control + "]";
        }

        return path + "/Controls";
    }

    private static IReadOnlyList<FormSectionSpec> ParseSections(IReadOnlyList<string> raw)
    {
        if (raw.Count == 0) return Array.Empty<FormSectionSpec>();
        var list = new List<FormSectionSpec>(raw.Count);
        foreach (var s in raw)
        {
            var idx = s.IndexOf(':');
            if (idx > 0)
                list.Add(new FormSectionSpec(s[..idx].Trim(), s[(idx + 1)..].Trim()));
            else
                list.Add(new FormSectionSpec(s.Trim(), s.Trim()));
        }
        return list;
    }
}
