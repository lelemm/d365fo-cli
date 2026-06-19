using System.Text.Json.Nodes;
using D365FO.Core;
using D365FO.Core.Bridge;

namespace D365FO.Cli.Commands.Get;

/// <summary>
/// Default-on entry point for bridge-primary reads. When the bridge is enabled
/// and spawns successfully, read
/// helpers here call the live <c>IMetadataProvider</c>-backed handlers and
/// return the deserialised payload. Any bridge failure / unavailability
/// returns null and the CLI falls back to the SQLite index.
/// </summary>
internal static class BridgeGate
{
    internal static bool ShouldTry() => D365FoSettings.ResolveFlag("D365FO_BRIDGE_ENABLED", defaultValue: true);

    /// <summary>
    /// Build bridge options from the unified config resolver so values set in
    /// settings.json (not just real env vars) reach the bridge child process.
    /// </summary>
    private static BridgeOptions DefaultOptions() => new()
    {
        MetadataBinPath = D365FoSettings.Resolve("D365FO_BIN_PATH"),
        PackagesPath = D365FoSettings.Resolve("D365FO_PACKAGES_PATH"),
        VsExtensionPath = D365FoSettings.Resolve("D365FO_VS_EXTENSION_PATH"),
        CustomPackagesPaths = D365FoSettings.FromEnvironment().CustomPackagesPaths,
        XrefConnectionString = D365FoSettings.Resolve("D365FO_XREF_CONNECTIONSTRING"),
    };

    internal static object? TryReadClass(string name) => TryRead("readClass", name);
    internal static object? TryReadTable(string name) => TryRead("readTable", name);
    internal static object? TryReadEdt(string name) => TryRead("readEdt", name);
    internal static object? TryReadEnum(string name) => TryRead("readEnum", name);
    internal static object? TryReadForm(string name) => TryRead("readForm", name);

    /// <summary>
    /// Persist a raw Ax* XML blob into <paramref name="model"/> via the
    /// live metadata provider. Returns (true, null) on success, (false,
    /// message) on any failure — including bridge unavailability. Callers
    /// should surface the error back to the user because the generate
    /// command has no on-disk fallback for this operation.
    /// </summary>
    internal static (bool ok, string? error) TrySaveObject(string kind, string name, string model, string? xml)
    {
        if (!BridgeClient.IsAvailable())
        {
            return (false, "bridge is not available (expected bundled bridge\\D365FO.Bridge.exe next to the CLI; D365FO_BRIDGE_PATH can override this for custom layouts).");
        }

        try
        {
            var options = DefaultOptions();
            using var client = new BridgeClient(options);
            var args = new JsonObject
            {
                ["kind"] = kind,
                ["name"] = name,
                ["model"] = model,
            };
            if (!string.IsNullOrEmpty(xml)) args["xml"] = xml;

            var result = client.SendAsync("createObject", args).GetAwaiter().GetResult();
            if (result is null) return (false, "bridge returned no result");

            var ok = (bool?)result["ok"] ?? false;
            if (!ok)
            {
                var err = (string?)result["error"] ?? "UNKNOWN";
                var msg = (string?)result["message"] ?? string.Empty;
                return (false, err + ": " + msg);
            }
            return (true, null);
        }
        catch (BridgeException ex)
        {
            return (false, "bridge error: " + ex.Message);
        }
    }

    /// <summary>
    /// Ask the bridge to scaffold/canonicalize an Ax metadata object. For
    /// render operations the result contains <c>xml</c>; for create/update the
    /// bridge persists through <c>IMetadataProvider</c> and returns path/model
    /// details when available.
    /// </summary>
    internal static (bool ok, string? error, JsonObject? result) TryScaffoldObject(
        string kind,
        string name,
        string operation,
        string? model,
        bool overwrite,
        string? xml,
        JsonObject? properties = null)
    {
        if (!BridgeClient.IsAvailable())
        {
            return (false, "bridge is not available (expected bundled bridge\\D365FO.Bridge.exe next to the CLI; D365FO_BRIDGE_PATH can override this for custom layouts).", null);
        }

        try
        {
            var options = DefaultOptions();
            using var client = new BridgeClient(options);
            var args = new JsonObject
            {
                ["kind"] = kind,
                ["name"] = name,
                ["operation"] = operation,
                ["overwrite"] = overwrite,
            };
            if (!string.IsNullOrWhiteSpace(model)) args["model"] = model;
            if (properties is not null)
            {
                args["properties"] = properties.DeepClone();
            }
            else if (!string.IsNullOrEmpty(xml))
            {
                args["properties"] = new JsonObject { ["xml"] = xml };
            }

            var result = client.SendAsync("scaffoldObject", args).GetAwaiter().GetResult();
            if (result is null) return (false, "bridge returned no result", null);

            var ok = (bool?)result["ok"] ?? false;
            if (!ok)
            {
                var err = (string?)result["error"] ?? "UNKNOWN";
                var msg = (string?)result["message"] ?? string.Empty;
                return (false, err + ": " + msg, result);
            }

            return (true, null, result);
        }
        catch (BridgeException ex)
        {
            return (false, "bridge error: " + ex.Message, null);
        }
    }

    /// <summary>
    /// Drive a bridge-side headless wizard. The payload is intentionally JSON
    /// shaped so the CLI can pass either command-line-derived step data or a
    /// captured wizard transcript without taking a dependency on VS UI types.
    /// </summary>
    internal static (bool ok, string? error, JsonObject? result) TryRunWizard(string method, JsonObject args)
    {
        if (!BridgeClient.IsAvailable())
        {
            return (false, "bridge is not available (expected bundled bridge\\D365FO.Bridge.exe next to the CLI; D365FO_BRIDGE_PATH can override this for custom layouts).", null);
        }

        try
        {
            var options = DefaultOptions();
            using var client = new BridgeClient(options);
            var result = client.SendAsync(method, args).GetAwaiter().GetResult();
            if (result is null) return (false, "bridge returned no result", null);

            var ok = (bool?)result["ok"] ?? false;
            if (!ok)
            {
                var err = (string?)result["error"] ?? "UNKNOWN";
                var msg = (string?)result["message"] ?? string.Empty;
                return (false, err + ": " + msg, result);
            }

            return (true, null, result);
        }
        catch (BridgeException ex)
        {
            return (false, "bridge error: " + ex.Message, null);
        }
    }

    internal static (bool ok, string? error, JsonObject? result) TryDesignerCatalog(JsonObject args)
        => TryDesigner("designerCatalog", args);

    internal static (bool ok, string? error, JsonObject? result) TryDesignerActions(JsonObject args)
        => TryDesigner("designerActions", args);

    internal static (bool ok, string? error, JsonObject? result) TryDesignerRun(JsonObject args)
        => TryDesigner("designerRun", args);

    internal static (bool ok, string? error, JsonObject? result) TryDesignerProperties(JsonObject args)
        => TryDesigner("designerProperties", args);

    internal static (bool ok, string? error, JsonObject? result) TryDesignerPropertyOptions(JsonObject args)
        => TryDesigner("designerPropertyOptions", args);

    internal static (bool ok, string? error, JsonObject? result) TryLintFile(string file, string? model)
    {
        if (!BridgeClient.IsAvailable())
        {
            return (false, "bridge is not available (expected bundled bridge\\D365FO.Bridge.exe next to the CLI; D365FO_BRIDGE_PATH can override this for custom layouts).", null);
        }

        try
        {
            var options = DefaultOptions();
            using var client = new BridgeClient(options);
            var args = new JsonObject { ["file"] = file };
            if (!string.IsNullOrWhiteSpace(model)) args["model"] = model;
            var result = client.SendAsync("lintFile", args).GetAwaiter().GetResult();
            if (result is null) return (false, "bridge returned no result", null);

            var ok = (bool?)result["ok"] ?? false;
            if (!ok)
            {
                var err = (string?)result["error"] ?? "UNKNOWN";
                var msg = (string?)result["message"] ?? string.Empty;
                return (false, err + ": " + msg, result);
            }

            return (true, null, result);
        }
        catch (BridgeException ex)
        {
            return (false, "bridge error: " + ex.Message, null);
        }
    }

    /// <summary>
    /// Query the DYNAMICSXREFDB for reverse references via the bridge.
    /// Returns the raw bridge JSON (tag _source already included by the
    /// bridge) or null on any failure — callers fall back to the regex
    /// scanner.
    /// </summary>
    internal static JsonObject? TryFindReferences(string symbol, string? kind, int limit)
    {
        if (!BridgeClient.IsAvailable()) return null;
        try
        {
            var options = DefaultOptions();
            using var client = new BridgeClient(options);
            var args = new JsonObject { ["symbol"] = symbol, ["limit"] = limit };
            if (!string.IsNullOrEmpty(kind)) args["kind"] = kind;
            var result = client.SendAsync("findReferences", args).GetAwaiter().GetResult();
            if (result is null) return null;
            var ok = (bool?)result["ok"] ?? false;
            if (!ok) return null;
            return result;
        }
        catch (BridgeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Ask the bridge for the on-disk folder that owns <paramref name="model"/>
    /// (via <c>ModelManifest.GetFolderForModel</c>). Returns null on any
    /// failure — callers should surface a clear error to the user.
    /// </summary>
    internal static string? TryGetModelFolder(string model)
    {
        if (!BridgeClient.IsAvailable()) return null;
        try
        {
            var options = DefaultOptions();
            using var client = new BridgeClient(options);
            var result = client.SendAsync("getModelFolder", new JsonObject { ["name"] = model })
                .GetAwaiter().GetResult();
            if (result is null) return null;
            var ok = (bool?)result["ok"] ?? false;
            if (!ok) return null;
            return (string?)result["folder"];
        }
        catch (BridgeException)
        {
            return null;
        }
    }

    private static object? TryRead(string method, string name)
    {
        if (!BridgeClient.IsAvailable())
        {
            return null;
        }

        try
        {
            var options = DefaultOptions();
            using var client = new BridgeClient(options);
            var result = client.SendAsync(method, new JsonObject { ["name"] = name })
                .GetAwaiter()
                .GetResult();

            if (result is null)
            {
                return null;
            }

            // Bridge signals unavailability / not-found / serialisation errors
            // by returning { ok:false, error:..., message:... }. Treat any
            // ok==false as "bridge declined" → caller falls back to index.
            var ok = (bool?)result["ok"];
            if (ok == false)
            {
                return null;
            }

            // Unwrap the bridge envelope: the handler returns
            // { ok:true, kind, name, source, data: <payload> } — hand the
            // payload to the CLI and surface the provenance separately so
            // the final envelope is { ok:true, data: { _source:"bridge", ... } }.
            if (result["data"] is JsonNode payload)
            {
                if (payload is JsonObject payloadObj)
                {
                    // Honour a non-default source (e.g. "bridge-kernel" for
                    // fallbacks) if the handler set one, otherwise default.
                    payloadObj["_source"] = (string?)result["source"] ?? "bridge";
                    return payloadObj;
                }
                return payload;
            }

            return result;
        }
        catch (BridgeException)
        {
            return null;
        }
    }

    private static (bool ok, string? error, JsonObject? result) TryDesigner(string method, JsonObject args)
    {
        if (!BridgeClient.IsAvailable())
        {
            return (false, "bridge is not available (expected bundled bridge\\D365FO.Bridge.exe next to the CLI; D365FO_BRIDGE_PATH can override this for custom layouts).", null);
        }

        try
        {
            var options = DefaultOptions();
            using var client = new BridgeClient(options);
            var result = client.SendAsync(method, args).GetAwaiter().GetResult();
            if (result is null) return (false, "bridge returned no result", null);

            var ok = (bool?)result["ok"] ?? false;
            if (!ok)
            {
                var err = (string?)result["error"] ?? "UNKNOWN";
                var msg = (string?)result["message"] ?? string.Empty;
                return (false, err + ": " + msg, result);
            }

            return (true, null, result);
        }
        catch (BridgeException ex)
        {
            return (false, "bridge error: " + ex.Message, null);
        }
    }
}
