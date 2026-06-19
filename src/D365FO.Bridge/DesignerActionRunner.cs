// <copyright file="DesignerActionRunner.cs" company="d365fo-cli contributors">
// MIT
// </copyright>

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using D365FO.Shared.Designer;

namespace D365FO.Bridge
{
    internal static class DesignerActionRunner
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private static readonly Dictionary<string, string> DesignerKindToMetadataKind =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "class", "class" },
                { "table", "table" },
                { "query", "query" },
                { "form", "form" },
                { "data-entity", "dataEntityView" },
                { "menu-item-display", "menuItemDisplay" },
                { "menu-item-action", "menuItemAction" },
                { "menu-item-output", "menuItemOutput" },
                { "security-privilege", "securityPrivilege" },
                { "security-duty", "securityDuty" },
                { "security-role", "securityRole" },
                { "service", "service" },
                { "service-group", "serviceGroup" },
                { "workflow-template", "workflowTemplate" },
                { "workflow-approval", "workflowApproval" },
                { "workflow-task", "workflowTask" },
            };

        internal static JsonObject Catalog(JsonObject args)
        {
            var parentKind = args != null ? (string)args["parentKind"] : null;
            var node = args != null ? (string)args["node"] : null;
            return CatalogPayload(parentKind, node, "bridge");
        }

        internal static JsonObject Actions(JsonObject args)
        {
            var parentKind = args != null ? (string)args["parentKind"] : null;
            var parent = args != null ? (string)args["parent"] : null;
            var model = args != null ? (string)args["model"] : null;
            var file = args != null ? (string)args["file"] : null;
            var node = args != null ? (string)args["node"] : null;

            var validation = ValidateParent(parentKind, parent, model, file);
            if (validation != null) return validation;

            var load = LoadParent(parentKind, parent, model, file);
            if (!load.Ok) return Fail(load.Code, load.Message);

            var payload = CatalogPayload(parentKind, node, "bridge");
            payload["parent"] = parent;
            if (!string.IsNullOrWhiteSpace(model)) payload["model"] = model;
            if (!string.IsNullOrWhiteSpace(file)) payload["file"] = file;
            return payload;
        }

        internal static JsonObject Properties(JsonObject args)
        {
            var parentKind = args != null ? (string)args["parentKind"] : null;
            var parent = args != null ? (string)args["parent"] : null;
            var model = args != null ? (string)args["model"] : null;
            var file = args != null ? (string)args["file"] : null;
            var node = args != null ? (string)args["node"] : null;

            var validation = ValidateParent(parentKind, parent, model, file);
            if (validation != null) return validation;

            if (!MetadataBootstrap.TryInitializeAssemblyResolution())
            {
                return Fail("METADATA_UNAVAILABLE",
                    MetadataBootstrap.LastError ??
                    "Microsoft metadata assemblies are unavailable. Set D365FO_VS_EXTENSION_PATH, D365FO_BIN_PATH, or D365FO_PACKAGES_PATH.");
            }

            var canonicalParentKind = DesignerKindCatalog.NormalizeKind(parentKind);
            var load = LoadParent(canonicalParentKind, parent, model, file);
            if (!load.Ok) return Fail(load.Code, load.Message);

            var warnings = new List<string>();
            if (!TryResolveTarget(load.Parent, node, out var target, warnings, out var targetError))
            {
                return Fail("NODE_NOT_FOUND", targetError);
            }

            var result = new JsonObject
            {
                ["ok"] = true,
                ["source"] = "bridge",
                ["parentKind"] = canonicalParentKind,
                ["parent"] = parent,
                ["node"] = node ?? string.Empty,
                ["targetType"] = target.GetType().FullName,
                ["properties"] = PropertySpecs(target),
            };
            if (!string.IsNullOrWhiteSpace(model)) result["model"] = model;
            if (!string.IsNullOrWhiteSpace(file)) result["file"] = file;
            if (warnings.Count > 0) result["warnings"] = ToJsonArray(warnings);
            return result;
        }

        internal static JsonObject PropertyOptions(JsonObject args)
        {
            var property = args != null ? (string)args["property"] : null;
            if (string.IsNullOrWhiteSpace(property)) return Fail("MISSING_ARG", "property is required");

            var properties = Properties(args);
            if (((bool?)properties["ok"] ?? false) == false) return properties;

            if (!(properties["properties"] is JsonArray props))
            {
                return Fail("PROPERTY_NOT_FOUND", "No properties were returned for the selected node.");
            }

            foreach (var prop in props.OfType<JsonObject>())
            {
                if (string.Equals((string)prop["name"], property, StringComparison.OrdinalIgnoreCase))
                {
                    return new JsonObject
                    {
                        ["ok"] = true,
                        ["source"] = "bridge",
                        ["parentKind"] = properties["parentKind"]?.DeepClone(),
                        ["parent"] = properties["parent"]?.DeepClone(),
                        ["node"] = properties["node"]?.DeepClone(),
                        ["targetType"] = properties["targetType"]?.DeepClone(),
                        ["property"] = prop.DeepClone(),
                        ["options"] = prop["options"]?.DeepClone() ?? new JsonArray(),
                    };
                }
            }

            return Fail("PROPERTY_NOT_FOUND", "Property '" + property + "' was not found on the selected node.");
        }

        internal static JsonObject Run(JsonObject args)
        {
            var actionId = args != null ? (string)args["actionId"] : null;
            var parentKind = args != null ? (string)args["parentKind"] : null;
            var parent = args != null ? (string)args["parent"] : null;
            var model = args != null ? (string)args["model"] : null;
            var file = args != null ? (string)args["file"] : null;
            var node = args != null ? (string)args["node"] : null;
            var operation = args != null ? ((string)args["operation"] ?? "render") : "render";
            var properties = args != null ? args["properties"] as JsonObject : null;

            if (string.IsNullOrWhiteSpace(actionId)) return Fail("MISSING_ARG", "actionId is required");
            var validation = ValidateParent(parentKind, parent, model, file);
            if (validation != null) return validation;

            var canonicalParentKind = DesignerKindCatalog.NormalizeKind(parentKind);
            var action = DesignerKindCatalog.FindAction(actionId, canonicalParentKind, node);
            if (action == null)
            {
                return Fail("INVALID_ACTION",
                    "Action '" + actionId + "' is not valid for parent kind '" + parentKind + "'" +
                    (string.IsNullOrWhiteSpace(node) ? "." : " at node '" + node + "'."));
            }
            var actionPath = string.IsNullOrWhiteSpace(node) ? action.AppliesToPath : node;

            properties = properties ?? new JsonObject();
            if (string.Equals(action.ActionKind, "property", StringComparison.OrdinalIgnoreCase))
            {
                return RunSetProperty(canonicalParentKind, parent, model, file, node, operation, properties);
            }

            var propertyMap = PropertiesToStrings(properties);
            foreach (var input in action.Inputs.Where(i => i.Required))
            {
                if (!propertyMap.ContainsKey(input.Name))
                {
                    return Fail("MISSING_PROPERTY", "Action '" + action.ActionId + "' requires property '" + input.Name + "'.");
                }
            }

            if (!MetadataBootstrap.TryInitializeAssemblyResolution())
            {
                return Fail("METADATA_UNAVAILABLE",
                    MetadataBootstrap.LastError ??
                    "Microsoft metadata assemblies are unavailable. Set D365FO_VS_EXTENSION_PATH, D365FO_BIN_PATH, or D365FO_PACKAGES_PATH.");
            }

            var load = LoadParent(canonicalParentKind, parent, model, file);
            if (!load.Ok) return Fail(load.Code, load.Message);

            var createdKind = DesignerKindCatalog.ResolveCreatedKind(action, propertyMap);
            var childKind = DesignerKindCatalog.FindKind(createdKind);
            if (childKind == null)
            {
                return Fail("INVALID_CREATED_KIND", "Catalog action creates unknown kind '" + createdKind + "'.");
            }

            var childType = MetadataBootstrap.GetMetaModelTypeByShortName(childKind.AxType);
            if (childType == null)
            {
                return Fail("TYPE_NOT_FOUND", "Could not resolve MetaModel type '" + childKind.AxType + "'.");
            }
            if (childType.IsAbstract)
            {
                return Fail("ABSTRACT_TYPE", "Created kind '" + createdKind + "' maps to abstract type '" + childType.FullName + "'.");
            }

            object child;
            try
            {
                child = Activator.CreateInstance(childType);
            }
            catch (Exception ex)
            {
                return Fail("CREATE_CHILD_FAILED", ex.GetType().Name + ": " + ex.Message);
            }

            var childName = propertyMap.ContainsKey("name") ? propertyMap["name"] : null;
            if (string.IsNullOrWhiteSpace(childName))
            {
                return Fail("MISSING_PROPERTY", "Action '" + action.ActionId + "' requires property 'name'.");
            }

            SetProperty(child, "Name", childName, new List<string>());
            var warnings = new List<string>();
            ApplyProperties(child, properties, warnings, action.CreatesKindSelector);

            if (!TryAddChild(load.Parent, actionPath, child, warnings, out var addError))
            {
                return Fail("ADD_CHILD_FAILED", addError);
            }

            var metadataKind = ToMetadataKind(canonicalParentKind);
            if (!MetadataObjectFactory.TrySerialize(metadataKind, load.Parent, out var xml, out var serializeCode, out var serializeMessage))
            {
                return Fail(serializeCode, serializeMessage);
            }

            var savedPath = (string)null;
            var saveOperation = operation;
            if (string.Equals(operation, "update", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(model))
                {
                    return Fail("MISSING_ARG", "model is required for update");
                }

                if (!MetadataBootstrap.TryInitialize())
                {
                    return Fail("METADATA_UNAVAILABLE",
                        MetadataBootstrap.LastError ??
                        "IMetadataProvider failed to initialise; set D365FO_PACKAGES_PATH on a D365FO VM.");
                }

                var modelInfo = MetadataBootstrap.ReadModelInfo(model);
                if (modelInfo == null)
                {
                    return Fail("MODEL_NOT_FOUND", "Model '" + model + "' was not returned by ModelManifest.");
                }

                var msi = MetadataBootstrap.BuildModelSaveInfo(modelInfo);
                if (msi == null)
                {
                    return Fail("MODEL_SAVE_INFO_FAILED", "Could not construct ModelSaveInfo for '" + model + "'.");
                }

                var save = MetadataBootstrap.SaveArtifact(metadataKind, "update", load.Parent, null, msi);
                if (!save.ok)
                {
                    return Fail("UPDATE_FAILED", save.error);
                }

                var modelFolder = MetadataBootstrap.GetModelFolder(model, out var _);
                var subfolder = MetadataBootstrap.GetAxSubfolder(metadataKind);
                if (!string.IsNullOrWhiteSpace(modelFolder) && !string.IsNullOrWhiteSpace(subfolder))
                {
                    savedPath = Path.Combine(modelFolder, subfolder, parent + ".xml");
                }
            }
            else
            {
                saveOperation = "render";
            }

            var result = new JsonObject
            {
                ["ok"] = true,
                ["actionId"] = action.ActionId,
                ["parentKind"] = canonicalParentKind,
                ["parent"] = parent,
                ["createdKind"] = createdKind,
                ["createdPath"] = FormatResultPath(ResultPathTemplate(action, actionPath), childName),
                ["nextCatalogKind"] = action.NextCatalogKind,
                ["operation"] = saveOperation,
                ["source"] = "bridge",
                ["xml"] = xml,
            };
            if (!string.IsNullOrWhiteSpace(model)) result["model"] = model;
            if (!string.IsNullOrWhiteSpace(savedPath)) result["path"] = savedPath;
            if (warnings.Count > 0)
            {
                var arr = new JsonArray();
                foreach (var warning in warnings) arr.Add(warning);
                result["warnings"] = arr;
            }
            return result;
        }

        private static JsonObject RunSetProperty(
            string canonicalParentKind,
            string parent,
            string model,
            string file,
            string node,
            string operation,
            JsonObject properties)
        {
            if (properties == null || properties.Count == 0)
            {
                return Fail("MISSING_PROPERTY", "set-property requires at least one property.");
            }

            if (!MetadataBootstrap.TryInitializeAssemblyResolution())
            {
                return Fail("METADATA_UNAVAILABLE",
                    MetadataBootstrap.LastError ??
                    "Microsoft metadata assemblies are unavailable. Set D365FO_VS_EXTENSION_PATH, D365FO_BIN_PATH, or D365FO_PACKAGES_PATH.");
            }

            var load = LoadParent(canonicalParentKind, parent, model, file);
            if (!load.Ok) return Fail(load.Code, load.Message);

            var warnings = new List<string>();
            if (!TryResolveTarget(load.Parent, node, out var target, warnings, out var targetError))
            {
                return Fail("NODE_NOT_FOUND", targetError);
            }

            if (target is IEnumerable && !(target is string))
            {
                return Fail("INVALID_NODE", "set-property requires --node to select one concrete metadata node, not a collection.");
            }

            var before = SnapshotProperties(target, properties);
            ApplyProperties(target, properties, warnings, ignoredKey: null);
            var after = SnapshotProperties(target, properties);

            var metadataKind = ToMetadataKind(canonicalParentKind);
            if (!MetadataObjectFactory.TrySerialize(metadataKind, load.Parent, out var xml, out var serializeCode, out var serializeMessage))
            {
                return Fail(serializeCode, serializeMessage);
            }

            var savedPath = (string)null;
            var saveOperation = operation;
            if (string.Equals(operation, "update", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(model))
                {
                    return Fail("MISSING_ARG", "model is required for update");
                }

                if (!MetadataBootstrap.TryInitialize())
                {
                    return Fail("METADATA_UNAVAILABLE",
                        MetadataBootstrap.LastError ??
                        "IMetadataProvider failed to initialise; set D365FO_PACKAGES_PATH on a D365FO VM.");
                }

                var modelInfo = MetadataBootstrap.ReadModelInfo(model);
                if (modelInfo == null)
                {
                    return Fail("MODEL_NOT_FOUND", "Model '" + model + "' was not returned by ModelManifest.");
                }

                var msi = MetadataBootstrap.BuildModelSaveInfo(modelInfo);
                if (msi == null)
                {
                    return Fail("MODEL_SAVE_INFO_FAILED", "Could not construct ModelSaveInfo for '" + model + "'.");
                }

                var save = MetadataBootstrap.SaveArtifact(metadataKind, "update", load.Parent, null, msi);
                if (!save.ok)
                {
                    return Fail("UPDATE_FAILED", save.error);
                }

                var modelFolder = MetadataBootstrap.GetModelFolder(model, out var _);
                var subfolder = MetadataBootstrap.GetAxSubfolder(metadataKind);
                if (!string.IsNullOrWhiteSpace(modelFolder) && !string.IsNullOrWhiteSpace(subfolder))
                {
                    savedPath = Path.Combine(modelFolder, subfolder, parent + ".xml");
                }
            }
            else
            {
                saveOperation = "render";
            }

            var result = new JsonObject
            {
                ["ok"] = true,
                ["actionId"] = "set-property",
                ["parentKind"] = canonicalParentKind,
                ["parent"] = parent,
                ["node"] = node ?? string.Empty,
                ["targetType"] = target.GetType().FullName,
                ["operation"] = saveOperation,
                ["source"] = "bridge",
                ["before"] = before,
                ["after"] = after,
                ["xml"] = xml,
            };
            if (!string.IsNullOrWhiteSpace(model)) result["model"] = model;
            if (!string.IsNullOrWhiteSpace(savedPath)) result["path"] = savedPath;
            if (warnings.Count > 0) result["warnings"] = ToJsonArray(warnings);
            return result;
        }

        private static JsonObject CatalogPayload(string parentKind, string node, string source)
        {
            var result = new JsonObject
            {
                ["ok"] = true,
                ["source"] = source,
            };

            if (!string.IsNullOrWhiteSpace(parentKind))
            {
                var canonical = DesignerKindCatalog.NormalizeKind(parentKind);
                var kind = DesignerKindCatalog.FindKind(canonical);
                if (kind == null)
                {
                    return Fail("INVALID_PARENT_KIND", "Unknown designer parent kind '" + parentKind + "'.");
                }

                result["parentKind"] = canonical;
                if (!string.IsNullOrWhiteSpace(node)) result["node"] = node;
                result["tree"] = DesignerKindCatalog.ToTree(full: true, parentKind: canonical);
                result["kind"] = JsonSerializer.SerializeToNode(kind, JsonOptions);
                result["actions"] = JsonSerializer.SerializeToNode(DesignerKindCatalog.ActionsFor(canonical, node), JsonOptions);
                return result;
            }

            result["tree"] = DesignerKindCatalog.ToTree(full: true);
            result["groups"] = JsonSerializer.SerializeToNode(DesignerKindCatalog.Groups, JsonOptions);
            result["actions"] = JsonSerializer.SerializeToNode(DesignerKindCatalog.Actions, JsonOptions);
            return result;
        }

        private static JsonObject ValidateParent(string parentKind, string parent, string model, string file)
        {
            if (string.IsNullOrWhiteSpace(parentKind)) return Fail("MISSING_ARG", "parentKind is required");
            if (string.IsNullOrWhiteSpace(parent)) return Fail("MISSING_ARG", "parent is required");
            if (string.IsNullOrWhiteSpace(model) == string.IsNullOrWhiteSpace(file))
            {
                return Fail("MISSING_ARG", "Pass exactly one of model or file.");
            }
            if (DesignerKindCatalog.FindKind(parentKind) == null)
            {
                return Fail("INVALID_PARENT_KIND", "Unknown designer parent kind '" + parentKind + "'.");
            }
            return null;
        }

        private static LoadResult LoadParent(string parentKind, string parent, string model, string file)
        {
            var canonicalParentKind = DesignerKindCatalog.NormalizeKind(parentKind);
            var metadataKind = ToMetadataKind(canonicalParentKind);
            if (string.IsNullOrWhiteSpace(metadataKind))
            {
                return LoadResult.Fail("UNSUPPORTED_PARENT_KIND", "Parent kind '" + parentKind + "' is not a top-level metadata object.");
            }

            if (!string.IsNullOrWhiteSpace(file))
            {
                if (!MetadataBootstrap.TryInitializeAssemblyResolution())
                {
                    return LoadResult.Fail("METADATA_UNAVAILABLE",
                        MetadataBootstrap.LastError ??
                        "Microsoft metadata assemblies are unavailable. Set D365FO_VS_EXTENSION_PATH, D365FO_BIN_PATH, or D365FO_PACKAGES_PATH.");
                }

                string xml;
                try
                {
                    xml = File.ReadAllText(file);
                }
                catch (Exception ex)
                {
                    return LoadResult.Fail("READ_FILE_FAILED", ex.Message);
                }

                if (!MetadataObjectFactory.TryDeserialize(metadataKind, xml, out var parentObject, out var code, out var message))
                {
                    return LoadResult.Fail(code, message);
                }

                return LoadResult.Success(parentObject);
            }

            if (!MetadataBootstrap.TryInitialize())
            {
                return LoadResult.Fail("METADATA_UNAVAILABLE",
                    MetadataBootstrap.LastError ??
                    "IMetadataProvider failed to initialise; set D365FO_PACKAGES_PATH on a D365FO VM.");
            }

            if (!MetadataBootstrap.KindToCollection.TryGetValue(metadataKind, out var collection))
            {
                return LoadResult.Fail("UNSUPPORTED_PARENT_KIND", "No provider collection is mapped for kind '" + metadataKind + "'.");
            }

            object artifact;
            try
            {
                artifact = MetadataBootstrap.ReadArtifact(collection, parent);
            }
            catch (Exception ex)
            {
                return LoadResult.Fail("READ_FAILED", ex.GetType().Name + ": " + ex.Message);
            }

            return artifact == null
                ? LoadResult.Fail("NOT_FOUND", "Parent object '" + parent + "' was not returned by IMetadataProvider.")
                : LoadResult.Success(artifact);
        }

        private static string ToMetadataKind(string designerKind)
        {
            if (string.IsNullOrWhiteSpace(designerKind)) return null;
            var canonical = DesignerKindCatalog.NormalizeKind(designerKind);
            return DesignerKindToMetadataKind.TryGetValue(canonical, out var metadataKind) ? metadataKind : canonical;
        }

        private static Dictionary<string, string> PropertiesToStrings(JsonObject properties)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (properties == null) return map;
            foreach (var kv in properties)
            {
                map[kv.Key] = kv.Value == null ? string.Empty : StripQuotes(kv.Value.ToJsonString());
            }
            return map;
        }

        private static bool TryAddChild(object parent, string path, object child, List<string> warnings, out string error)
        {
            error = null;
            if (parent == null)
            {
                error = "Parent object is null.";
                return false;
            }

            var segments = (path ?? string.Empty)
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                error = "Action path is empty.";
                return false;
            }

            object container = parent;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (!TryResolveSegment(container, segments[i], createIfMissing: true, out container, warnings, out error))
                {
                    return false;
                }
            }

            var collectionName = SegmentName(segments[segments.Length - 1]);
            var prop = FindProperty(container.GetType(), collectionName);
            if (prop == null)
            {
                error = "Collection property '" + collectionName + "' was not found on " + container.GetType().Name + ".";
                return false;
            }

            var collection = prop.GetValue(container, null);
            if (collection == null)
            {
                if (!prop.CanWrite)
                {
                    error = "Collection property '" + collectionName + "' is null and not writable on " + container.GetType().Name + ".";
                    return false;
                }

                collection = TryCreateInstance(prop.PropertyType);
                if (collection == null)
                {
                    error = "Could not create collection '" + collectionName + "' of type " + prop.PropertyType.FullName + ".";
                    return false;
                }

                prop.SetValue(container, collection, null);
            }

            var add = collection.GetType().GetMethods()
                .FirstOrDefault(m =>
                {
                    if (!string.Equals(m.Name, "Add", StringComparison.Ordinal)) return false;
                    var ps = m.GetParameters();
                    return ps.Length == 1 && ps[0].ParameterType.IsAssignableFrom(child.GetType());
                });
            if (add != null)
            {
                add.Invoke(collection, new[] { child });
                return true;
            }

            if (collection is IList list)
            {
                list.Add(child);
                return true;
            }

            error = "No compatible Add method was found on collection '" + collectionName + "' (" + collection.GetType().FullName + ").";
            return false;
        }

        private static bool TryResolveTarget(object parent, string path, out object target, List<string> warnings, out string error)
        {
            target = parent;
            error = null;
            if (parent == null)
            {
                error = "Parent object is null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                return true;
            }

            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                if (!TryResolveSegment(target, segment, createIfMissing: false, out target, warnings, out error))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryResolveSegment(object source, string segment, bool createIfMissing, out object resolved, List<string> warnings, out string error)
        {
            resolved = null;
            error = null;
            var propName = SegmentName(segment);
            var key = SegmentKey(segment);
            var prop = FindProperty(source.GetType(), propName);
            if (prop == null)
            {
                error = "Path segment '" + propName + "' was not found on " + source.GetType().Name + ".";
                return false;
            }

            var value = prop.GetValue(source, null);
            if (value == null && createIfMissing && prop.CanWrite)
            {
                value = TryCreateInstance(prop.PropertyType);
                if (value != null) prop.SetValue(source, value, null);
            }

            if (value == null)
            {
                error = "Path segment '" + propName + "' is null on " + source.GetType().Name + ".";
                return false;
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                resolved = value;
                return true;
            }

            if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    var name = FindProperty(item.GetType(), "Name")?.GetValue(item, null)?.ToString();
                    if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                    {
                        resolved = item;
                        return true;
                    }
                }
            }

            error = "Could not find item '" + key + "' in path segment '" + propName + "'.";
            return false;
        }

        private static PropertyInfo FindProperty(Type type, string name)
        {
            return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        }

        private static string SegmentName(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment)) return segment;
            var idx = segment.IndexOf('[');
            return idx < 0 ? segment : segment.Substring(0, idx);
        }

        private static string SegmentKey(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment)) return null;
            var start = segment.IndexOf('[');
            var end = segment.LastIndexOf(']');
            if (start < 0 || end <= start) return null;
            return segment.Substring(start + 1, end - start - 1);
        }

        private static object TryCreateInstance(Type type)
        {
            try
            {
                if (type == null || type.IsAbstract || type.IsInterface) return null;
                var ctor = type.GetConstructor(Type.EmptyTypes);
                return ctor == null ? null : ctor.Invoke(null);
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyProperties(object target, JsonObject properties, List<string> warnings, string ignoredKey)
        {
            if (target == null || properties == null) return;
            foreach (var kv in properties)
            {
                if (!string.IsNullOrWhiteSpace(ignoredKey) &&
                    string.Equals(kv.Key, ignoredKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                SetProperty(target, NormalizePropertyName(kv.Key), kv.Value, warnings);
            }
        }

        private static string NormalizePropertyName(string key)
        {
            if (string.Equals(key, "extendedDataType", StringComparison.OrdinalIgnoreCase)) return "ExtendedDataType";
            if (string.Equals(key, "edt", StringComparison.OrdinalIgnoreCase)) return "ExtendedDataType";
            if (string.Equals(key, "object", StringComparison.OrdinalIgnoreCase)) return "ObjectName";
            return key;
        }

        private static void SetProperty(object target, string propertyName, object rawValue, List<string> warnings)
        {
            var prop = FindProperty(target.GetType(), propertyName);
            if (prop == null || !prop.CanWrite || prop.GetIndexParameters().Length > 0)
            {
                if (!string.Equals(propertyName, "name", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add("Property '" + propertyName + "' was ignored because it is not writable on " + target.GetType().Name + ".");
                }
                return;
            }

            if (rawValue is JsonObject nestedObject && !IsSimpleType(prop.PropertyType))
            {
                var nestedTarget = prop.GetValue(target, null);
                if (nestedTarget == null)
                {
                    nestedTarget = TryCreateInstance(prop.PropertyType);
                    if (nestedTarget == null)
                    {
                        warnings.Add("Property '" + propertyName + "' was ignored because " + prop.PropertyType.Name + " could not be created.");
                        return;
                    }

                    prop.SetValue(target, nestedTarget, null);
                }

                ApplyProperties(nestedTarget, nestedObject, warnings, ignoredKey: null);
                return;
            }

            if (!TryConvert(rawValue, prop.PropertyType, out var converted))
            {
                warnings.Add("Property '" + propertyName + "' was ignored because the value could not be converted to " + prop.PropertyType.Name + ".");
                return;
            }

            try
            {
                prop.SetValue(target, converted, null);
            }
            catch (Exception ex)
            {
                warnings.Add("Property '" + propertyName + "' was ignored: " + ex.Message);
            }
        }

        private static bool TryConvert(object rawValue, Type targetType, out object value)
        {
            value = null;
            if (targetType == null) return false;
            var nullable = Nullable.GetUnderlyingType(targetType);
            var effectiveType = nullable ?? targetType;
            if (rawValue == null)
            {
                return !targetType.IsValueType || nullable != null;
            }

            var text = rawValue is JsonNode node ? StripQuotes(node.ToJsonString()) : rawValue.ToString();
            try
            {
                if (effectiveType == typeof(string))
                {
                    value = text;
                    return true;
                }
                if (effectiveType == typeof(bool))
                {
                    value = bool.Parse(text);
                    return true;
                }
                if (effectiveType == typeof(int))
                {
                    value = int.Parse(text, CultureInfo.InvariantCulture);
                    return true;
                }
                if (effectiveType == typeof(long))
                {
                    value = long.Parse(text, CultureInfo.InvariantCulture);
                    return true;
                }
                if (effectiveType.IsEnum)
                {
                    value = Enum.Parse(effectiveType, text, true);
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool IsSimpleType(Type type)
        {
            if (type == null) return true;
            var effectiveType = Nullable.GetUnderlyingType(type) ?? type;
            return effectiveType.IsPrimitive ||
                   effectiveType.IsEnum ||
                   effectiveType == typeof(string) ||
                   effectiveType == typeof(decimal) ||
                   effectiveType == typeof(DateTime) ||
                   effectiveType == typeof(Guid);
        }

        private static JsonArray PropertySpecs(object target)
        {
            var array = new JsonArray();
            if (target == null) return array;

            foreach (var prop in target.GetType()
                         .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.GetIndexParameters().Length == 0)
                         .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var propertyType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                var item = new JsonObject
                {
                    ["name"] = prop.Name,
                    ["type"] = FriendlyTypeName(prop.PropertyType),
                    ["writable"] = prop.CanWrite,
                    ["readable"] = prop.CanRead,
                    ["hasOptions"] = HasOptions(prop.PropertyType),
                    ["value"] = prop.CanRead ? SimpleValue(prop.GetValue(target, null)) : null,
                };

                var options = OptionsFor(prop.PropertyType);
                if (options.Count > 0) item["options"] = options;
                array.Add(item);
            }

            return array;
        }

        private static JsonObject SnapshotProperties(object target, JsonObject requested)
        {
            var snapshot = new JsonObject();
            if (target == null || requested == null) return snapshot;

            foreach (var kv in requested)
            {
                var prop = FindProperty(target.GetType(), NormalizePropertyName(kv.Key));
                snapshot[kv.Key] = prop != null && prop.CanRead
                    ? SimpleValue(prop.GetValue(target, null))
                    : null;
            }

            return snapshot;
        }

        private static JsonArray OptionsFor(Type type)
        {
            var array = new JsonArray();
            var effective = Nullable.GetUnderlyingType(type) ?? type;
            if (effective == typeof(bool))
            {
                array.Add(new JsonObject { ["name"] = "false", ["value"] = false });
                array.Add(new JsonObject { ["name"] = "true", ["value"] = true });
                return array;
            }

            if (!effective.IsEnum) return array;
            foreach (var name in Enum.GetNames(effective))
            {
                var value = Enum.Parse(effective, name);
                array.Add(new JsonObject
                {
                    ["name"] = name,
                    ["value"] = Convert.ToInt64(value, CultureInfo.InvariantCulture),
                });
            }

            return array;
        }

        private static bool HasOptions(Type type)
        {
            var effective = Nullable.GetUnderlyingType(type) ?? type;
            return effective == typeof(bool) || effective.IsEnum;
        }

        private static string FriendlyTypeName(Type type)
        {
            if (type == null) return string.Empty;
            var nullable = Nullable.GetUnderlyingType(type);
            var effective = nullable ?? type;
            var name = effective.IsGenericType ? effective.Name.Split('`')[0] : effective.Name;
            return nullable == null ? name : name + "?";
        }

        private static JsonNode SimpleValue(object value)
        {
            if (value == null) return null;
            var type = value.GetType();
            if (type.IsEnum) return JsonValue.Create(value.ToString());
            if (type == typeof(string)) return JsonValue.Create((string)value);
            if (type == typeof(bool)) return JsonValue.Create((bool)value);
            if (type == typeof(int)) return JsonValue.Create((int)value);
            if (type == typeof(long)) return JsonValue.Create((long)value);
            if (type == typeof(decimal)) return JsonValue.Create((decimal)value);
            if (type == typeof(double)) return JsonValue.Create((double)value);
            if (type == typeof(float)) return JsonValue.Create((float)value);
            if (type == typeof(DateTime)) return JsonValue.Create(((DateTime)value).ToString("O", CultureInfo.InvariantCulture));
            if (type == typeof(Guid)) return JsonValue.Create(value.ToString());
            return IsSimpleType(type) ? JsonValue.Create(value.ToString()) : null;
        }

        private static JsonArray ToJsonArray(IEnumerable<string> values)
        {
            var array = new JsonArray();
            foreach (var value in values) array.Add(value);
            return array;
        }

        private static string FormatResultPath(string template, string name)
        {
            return string.IsNullOrWhiteSpace(template)
                ? name
                : template.Replace("{name}", name ?? string.Empty);
        }

        private static string ResultPathTemplate(DesignerActionSpec action, string actionPath)
        {
            if (string.IsNullOrWhiteSpace(actionPath) ||
                DesignerKindCatalog.PathMatches(action.ResultPathTemplate, actionPath) ||
                string.Equals(action.AppliesToPath, actionPath, StringComparison.OrdinalIgnoreCase))
            {
                return action.ResultPathTemplate;
            }

            return actionPath.TrimEnd('/') + "[{name}]";
        }

        private static string StripQuotes(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            return value.Trim().Trim('"');
        }

        private sealed class LoadResult
        {
            public bool Ok { get; private set; }
            public object Parent { get; private set; }
            public string Code { get; private set; }
            public string Message { get; private set; }

            public static LoadResult Success(object parent)
            {
                return new LoadResult { Ok = true, Parent = parent };
            }

            public static LoadResult Fail(string code, string message)
            {
                return new LoadResult { Ok = false, Code = code, Message = message };
            }
        }

        private static JsonObject Fail(string code, string message)
        {
            return new JsonObject
            {
                ["ok"] = false,
                ["error"] = code,
                ["message"] = message,
            };
        }
    }
}
