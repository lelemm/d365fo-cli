using System.Text.Json.Nodes;
using D365FO.Cli.Commands.Get;
using D365FO.Core;
using D365FO.Core.Scaffolding;

namespace D365FO.Cli.Commands.Generate;

internal enum GenerateBackend
{
    Auto,
    Bridge,
    Legacy,
}

internal static class GenerateBackendResolver
{
    internal static bool TryResolve(string? raw, out GenerateBackend backend, out string? error)
    {
        var value = string.IsNullOrWhiteSpace(raw)
            ? D365FoSettings.Resolve("D365FO_SCAFFOLDING_BACKEND")
            : raw;
        value = string.IsNullOrWhiteSpace(value) ? "auto" : value.Trim();

        switch (value.ToLowerInvariant())
        {
            case "auto":
                backend = GenerateBackend.Auto;
                error = null;
                return true;
            case "bridge":
                backend = GenerateBackend.Bridge;
                error = null;
                return true;
            case "legacy":
                backend = GenerateBackend.Legacy;
                error = null;
                return true;
            default:
                backend = GenerateBackend.Auto;
                error = $"Unsupported scaffolding backend '{value}'. Expected auto, bridge, or legacy.";
                return false;
        }
    }

    internal static bool ShouldUseBridge(GenerateBackend backend) => backend switch
    {
        GenerateBackend.Bridge => true,
        GenerateBackend.Auto => BridgeGate.ShouldTry(),
        _ => false,
    };
}

internal readonly record struct BridgeScaffoldAttempt(bool Handled, int ExitCode)
{
    internal static BridgeScaffoldAttempt NotHandled => new(false, 0);
}

internal sealed record BridgeWizardFile(string Kind, string Name, string Xml, string? Path);
internal sealed record BridgeDesignerAction(string ActionId, string ParentKind, string? Node, JsonObject Properties);

internal sealed record BridgeScaffoldSummary(
    string Kind,
    string Name,
    string? Path,
    long? Bytes,
    string? BackupPath,
    string? Model,
    string Operation,
    string Source,
    JsonArray DesignerActions);

internal static class GenerateBridgeScaffolding
{
    internal static BridgeScaffoldAttempt TryWrite(
        OutputMode.Kind outputKind,
        GenerateBackend backend,
        string? installTo,
        bool overwrite,
        string axKind,
        string name,
        string? xml,
        string? outPath,
        IReadOnlyList<string>? warnings = null,
        JsonObject? properties = null,
        IReadOnlyList<BridgeDesignerAction>? designerActions = null,
        Func<BridgeScaffoldSummary, object>? successFactory = null)
    {
        if (!GenerateBackendResolver.ShouldUseBridge(backend))
        {
            return BridgeScaffoldAttempt.NotHandled;
        }

        var hasInstall = !string.IsNullOrWhiteSpace(installTo);
        var hasOut = !string.IsNullOrWhiteSpace(outPath);
        var operation = hasInstall && !hasOut ? "create" : "render";
        var model = string.Equals(operation, "create", StringComparison.OrdinalIgnoreCase) ? installTo : null;

        if (operation == "render" && string.IsNullOrWhiteSpace(outPath))
        {
            return new BridgeScaffoldAttempt(
                true,
                RenderHelpers.Render(outputKind, ToolResult<object>.Fail(
                    D365FoErrorCodes.BadInput,
                    "--out is required when rendering through the bridge.")));
        }

        var (ok, error, result) = BridgeGate.TryScaffoldObject(axKind, name, operation, model, overwrite, null, properties);
        if (!ok)
        {
            return new BridgeScaffoldAttempt(
                true,
                RenderHelpers.Render(outputKind, ToolResult<object>.Fail(
                    "BRIDGE_SCAFFOLD_FAILED",
                    error ?? "Bridge scaffolding failed.",
                    "Use --backend legacy to use the local XML scaffolder.")));
        }

        var combinedWarnings = MergeWarnings(warnings, result);
        var actionSummaries = new JsonArray();
        var actions = designerActions ?? Array.Empty<BridgeDesignerAction>();
        string? renderedXml = (string?)result?["xml"];

        if (actions.Count > 0)
        {
            var applied = ApplyDesignerActions(
                outputKind,
                operation,
                name,
                model,
                renderedXml,
                actions,
                actionSummaries,
                ref combinedWarnings,
                out var finalXml);
            if (applied.Handled)
            {
                return applied;
            }

            renderedXml = finalXml;
        }

        if (operation == "render")
        {
            var bridgeXml = renderedXml;
            if (string.IsNullOrWhiteSpace(bridgeXml))
            {
                return new BridgeScaffoldAttempt(
                    true,
                    RenderHelpers.Render(outputKind, ToolResult<object>.Fail(
                        "BRIDGE_SCAFFOLD_FAILED",
                        "Bridge render succeeded but did not return XML.")));
            }

            try
            {
                var res = ScaffoldFileWriter.Write(bridgeXml!, outPath!, overwrite);
                var summary = new BridgeScaffoldSummary(
                    axKind,
                    name,
                    res.Path,
                    res.Bytes,
                    res.BackupPath,
                    installTo,
                    "render",
                    (string?)result?["source"] ?? "bridge",
                    actionSummaries);
                var payload = successFactory?.Invoke(summary) ?? new
                {
                    kind = axKind,
                    name,
                    path = res.Path,
                    bytes = res.Bytes,
                    backup = res.BackupPath,
                    backend = "bridge",
                    source = (string?)result?["source"] ?? "bridge",
                    model = installTo,
                    designerActions = actionSummaries,
                };
                return new BridgeScaffoldAttempt(
                    true,
                    RenderHelpers.Render(outputKind, ToolResult<object>.Success(payload, combinedWarnings)));
            }
            catch (Exception ex)
            {
                return new BridgeScaffoldAttempt(
                    true,
                    RenderHelpers.Render(outputKind, ToolResult<object>.Fail(D365FoErrorCodes.WriteFailed, ex.Message)));
            }
        }

        var installSummary = new BridgeScaffoldSummary(
            axKind,
            name,
            (string?)result?["path"],
            null,
            null,
            (string?)result?["model"] ?? installTo,
            (string?)result?["operation"] ?? operation,
            (string?)result?["source"] ?? "bridge",
            actionSummaries);
        var installPayload = successFactory?.Invoke(installSummary) ?? new
        {
            kind = axKind,
            name,
            path = (string?)result?["path"],
            model = (string?)result?["model"] ?? installTo,
            backend = "bridge",
            source = (string?)result?["source"] ?? "bridge",
            operation = (string?)result?["operation"] ?? operation,
            designerActions = actionSummaries,
        };
        return new BridgeScaffoldAttempt(
            true,
            RenderHelpers.Render(outputKind, ToolResult<object>.Success(installPayload, combinedWarnings)));
    }

    private static BridgeScaffoldAttempt ApplyDesignerActions(
        OutputMode.Kind outputKind,
        string operation,
        string parent,
        string? model,
        string? initialXml,
        IReadOnlyList<BridgeDesignerAction> actions,
        JsonArray actionSummaries,
        ref IReadOnlyList<string>? warnings,
        out string? finalXml)
    {
        finalXml = initialXml;
        string? tempDir = null;
        string? tempFile = null;

        try
        {
            if (string.Equals(operation, "render", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(finalXml))
                {
                    return BridgeFailure(outputKind, "Bridge render succeeded but did not return XML for designer automation.");
                }

                tempDir = Path.Combine(Path.GetTempPath(), "d365fo-bridge-designer-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                tempFile = Path.Combine(tempDir, parent + ".xml");
                File.WriteAllText(tempFile, finalXml);
            }

            foreach (var action in actions)
            {
                var args = new JsonObject
                {
                    ["actionId"] = action.ActionId,
                    ["parentKind"] = action.ParentKind,
                    ["parent"] = parent,
                    ["operation"] = string.Equals(operation, "create", StringComparison.OrdinalIgnoreCase) ? "update" : "render",
                    ["properties"] = action.Properties.DeepClone(),
                };
                if (!string.IsNullOrWhiteSpace(action.Node)) args["node"] = action.Node;
                if (string.Equals(operation, "render", StringComparison.OrdinalIgnoreCase))
                {
                    args["file"] = tempFile;
                }
                else
                {
                    args["model"] = model;
                }

                var (ok, error, result) = BridgeGate.TryDesignerRun(args);
                if (!ok)
                {
                    return BridgeFailure(
                        outputKind,
                        $"Bridge designer action '{action.ActionId}' failed: {error ?? "unknown error"}");
                }

                MergeWarningsInPlace(ref warnings, result);
                actionSummaries.Add(new JsonObject
                {
                    ["actionId"] = action.ActionId,
                    ["createdKind"] = (string?)result?["createdKind"],
                    ["createdPath"] = (string?)result?["createdPath"],
                    ["nextCatalogKind"] = (string?)result?["nextCatalogKind"],
                });

                if (string.Equals(operation, "render", StringComparison.OrdinalIgnoreCase))
                {
                    finalXml = (string?)result?["xml"];
                    if (string.IsNullOrWhiteSpace(finalXml))
                    {
                        return BridgeFailure(outputKind, $"Bridge designer action '{action.ActionId}' succeeded but did not return XML.");
                    }

                    File.WriteAllText(tempFile!, finalXml);
                }
            }

            return BridgeScaffoldAttempt.NotHandled;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempDir) && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); }
                catch { /* best-effort cleanup */ }
            }
        }
    }

    private static BridgeScaffoldAttempt BridgeFailure(OutputMode.Kind outputKind, string message) =>
        new(
            true,
            RenderHelpers.Render(outputKind, ToolResult<object>.Fail(
                "BRIDGE_DESIGNER_AUTOMATION_FAILED",
                message)));

    internal static int RenderBackendError(OutputMode.Kind outputKind, string error) =>
        RenderHelpers.Render(outputKind, ToolResult<object>.Fail(D365FoErrorCodes.BadInput, error));

    internal static bool TryLoadWizardSteps(string? raw, out JsonObject steps, out string? error)
    {
        steps = new JsonObject();
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        string json;
        try
        {
            json = File.Exists(raw) ? File.ReadAllText(raw) : raw;
        }
        catch (Exception ex)
        {
            error = "Could not read wizard JSON: " + ex.Message;
            return false;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (Exception ex)
        {
            error = "Could not parse wizard JSON: " + ex.Message;
            return false;
        }

        if (node is not JsonObject obj)
        {
            error = "Wizard JSON must be an object.";
            return false;
        }

        var source = obj["steps"] as JsonObject ?? obj;
        steps = (JsonObject)source.DeepClone();
        return true;
    }

    internal static IReadOnlyList<BridgeWizardFile> GetWizardFiles(JsonObject? result)
    {
        var files = new List<BridgeWizardFile>();
        if (result?["files"] is not JsonArray array)
        {
            return files;
        }

        foreach (var node in array)
        {
            if (node is not JsonObject file)
            {
                continue;
            }

            var kind = (string?)file["kind"];
            var name = (string?)file["name"];
            var xml = (string?)file["xml"];
            if (string.IsNullOrWhiteSpace(kind) ||
                string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(xml))
            {
                continue;
            }

            files.Add(new BridgeWizardFile(kind!, name!, xml!, (string?)file["path"]));
        }

        return files;
    }

    internal static BridgeWizardFile? FindWizardFile(
        IReadOnlyList<BridgeWizardFile> files,
        string kind,
        string? name = null)
    {
        return files.FirstOrDefault(file =>
            string.Equals(file.Kind, kind, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(name) || string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase)));
    }

    internal static IReadOnlyList<string>? MergeWarnings(IReadOnlyList<string>? localWarnings, JsonObject? result)
    {
        List<string>? merged = null;
        if (localWarnings is { Count: > 0 })
        {
            merged = new List<string>(localWarnings);
        }

        if (result?["warnings"] is JsonArray bridgeWarnings)
        {
            merged ??= new List<string>();
            foreach (var warning in bridgeWarnings)
            {
                var text = (string?)warning;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    merged.Add(text!);
                }
            }
        }

        return merged;
    }

    private static void MergeWarningsInPlace(ref IReadOnlyList<string>? localWarnings, JsonObject? result)
    {
        var merged = MergeWarnings(localWarnings, result);
        localWarnings = merged;
    }
}
