// <copyright file="WizardScaffolder.cs" company="d365fo-cli contributors">
// MIT
// </copyright>

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;

namespace D365FO.Bridge
{
    /// <summary>
    /// Headless versions of Visual Studio's multi-page D365FO wizards. The VS
    /// wizard controllers are DTE/project-bound, so this class consumes the
    /// same logical wizard choices as JSON and materializes the Microsoft Ax*
    /// metadata objects directly.
    /// </summary>
    internal static class WizardScaffolder
    {
        internal static JsonObject RunDataEntityWizard(JsonObject args)
        {
            var steps = ReadSteps(args);
            var name = ReadString(args, "name") ?? ReadString(steps, "name") ?? ReadString(steps, "entityName");
            var operation = ReadOperation(args);
            var model = ReadString(args, "model");
            var overwrite = ReadBool(args, "overwrite", false);

            if (string.IsNullOrWhiteSpace(name)) return Fail("MISSING_ARG", "name is required.");
            if (!string.Equals(operation, "render", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(model))
                return Fail("MISSING_ARG", "model is required for create/update.");

            if (!MetadataBootstrap.TryInitializeAssemblyResolution())
            {
                return Fail("METADATA_UNAVAILABLE", MetadataBootstrap.LastError ?? "Microsoft metadata assemblies are unavailable.");
            }

            string error;
            var artifacts = BuildDataEntityArtifacts(name, steps, out error);
            if (artifacts == null) return Fail("WIZARD_FAILED", error);
            return Finish(operation, model, overwrite, artifacts);
        }

        internal static JsonObject RunWorkflowWizard(JsonObject args)
        {
            var steps = ReadSteps(args);
            var name = ReadString(args, "name") ?? ReadString(steps, "name") ?? ReadString(steps, "workflowName");
            var operation = ReadOperation(args);
            var model = ReadString(args, "model");
            var overwrite = ReadBool(args, "overwrite", false);

            if (string.IsNullOrWhiteSpace(name)) return Fail("MISSING_ARG", "name is required.");
            if (!string.Equals(operation, "render", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(model))
                return Fail("MISSING_ARG", "model is required for create/update.");

            if (!MetadataBootstrap.TryInitializeAssemblyResolution())
            {
                return Fail("METADATA_UNAVAILABLE", MetadataBootstrap.LastError ?? "Microsoft metadata assemblies are unavailable.");
            }

            string error;
            var artifacts = BuildWorkflowArtifacts(name, steps, out error);
            if (artifacts == null) return Fail("WIZARD_FAILED", error);
            return Finish(operation, model, overwrite, artifacts);
        }

        private static List<Artifact> BuildDataEntityArtifacts(string name, JsonObject steps, out string error)
        {
            error = null;
            var table = ReadString(steps, "table") ?? ReadString(steps, "rootTable") ?? ReadString(steps, "rootDataSource");
            if (string.IsNullOrWhiteSpace(table))
            {
                error = "Data entity wizard step 'table' is required.";
                return null;
            }

            var entity = CreateAx("AxDataEntityView");
            Set(entity, "Name", name);
            Set(entity, "PublicEntityName", ReadString(steps, "publicEntityName") ?? ReadString(steps, "publicEntity") ?? name);
            var publicEntity = ReadString(steps, "publicEntityName") ?? ReadString(steps, "publicEntity") ?? name;
            Set(entity, "PublicCollectionName", ReadString(steps, "publicCollectionName") ?? ReadString(steps, "publicCollection") ?? publicEntity + "s");
            Set(entity, "IsPublic", "Yes");
            Set(entity, "DataManagementEnabled", "Yes");
            SetOptional(entity, "Label", ReadString(steps, "label"));
            SetOptional(entity, "EntityCategory", ReadString(steps, "entityCategory"));
            SetOptional(entity, "PrimaryCompanyContext", ReadString(steps, "primaryCompanyContext"));

            var viewMetadata = CreateAx("AxQuerySimple");
            Set(viewMetadata, "Name", name);
            var rootDs = CreateAx("AxQuerySimpleRootDataSource");
            Set(rootDs, "Name", table);
            Set(rootDs, "Table", table);
            AddToCollection(Get(viewMetadata, "DataSources"), rootDs);
            Set(entity, "ViewMetadata", viewMetadata);

            var fields = steps != null ? steps["fields"] as JsonArray : null;
            if (fields != null)
            {
                foreach (var node in fields)
                {
                    var field = node as JsonObject;
                    if (field == null) continue;
                    var fieldName = ReadString(field, "name");
                    if (string.IsNullOrWhiteSpace(fieldName)) continue;
                    var mapped = CreateAx("AxDataEntityViewMappedField");
                    Set(mapped, "Name", fieldName);
                    Set(mapped, "DataSource", ReadString(field, "dataSource") ?? table);
                    Set(mapped, "DataField", ReadString(field, "dataField") ?? fieldName);
                    if (ReadBool(field, "mandatory", false) || ReadBool(field, "isMandatory", false))
                    {
                        Set(mapped, "Mandatory", "Yes");
                    }
                    SetOptional(mapped, "Label", ReadString(field, "label"));
                    AddToCollection(Get(entity, "Fields"), mapped);
                }
            }

            return new List<Artifact>
            {
                new Artifact("dataEntityView", name, entity),
            };
        }

        private static List<Artifact> BuildWorkflowArtifacts(string name, JsonObject steps, out string error)
        {
            error = null;
            var table = ReadString(steps, "table") ?? ReadString(steps, "tableName");
            if (string.IsNullOrWhiteSpace(table))
            {
                error = "Workflow wizard step 'table' is required.";
                return null;
            }

            var documentClass = ReadString(steps, "documentClassName") ?? ReadString(steps, "documentClass") ?? name + "Document";
            var queryName = ReadString(steps, "queryName") ?? ReadString(steps, "query") ?? documentClass.Replace("Document", "") + "Query";
            var submitClass = ReadString(steps, "submitClassName") ?? ReadString(steps, "submitClass") ?? name + "SubmitToWorkflow";
            var submitMenu = ReadString(steps, "submitMenuItemName") ?? ReadString(steps, "submitMenuItem") ?? name + "Submit";
            var documentMenu = ReadString(steps, "documentMenuItemName") ?? ReadString(steps, "documentMenuItem") ?? name + "MenuItem";
            var generateSubmit = !ReadBool(steps, "noSubmitStub", false) && !ReadBool(steps, "skipSubmitStub", false);

            var template = CreateAx("AxWorkflowTemplate");
            Set(template, "Name", name);
            Set(template, "Document", documentClass);
            Set(template, "DocumentMenuItem", documentMenu);
            Set(template, "SubmitToWorkflowMenuItem", submitMenu);
            SetOptional(template, "Category", ReadString(steps, "category"));
            SetOptional(template, "Label", ReadString(steps, "label"));
            SetOptional(template, "HelpText", ReadString(steps, "helpText"));

            var artifacts = new List<Artifact>
            {
                new Artifact("workflowTemplate", name, template),
                new Artifact("class", documentClass, BuildWorkflowDocumentClass(documentClass, queryName)),
            };

            if (generateSubmit)
            {
                artifacts.Add(new Artifact("class", submitClass, BuildSubmitToWorkflowClass(submitClass, table, name)));
                artifacts.Add(new Artifact("menuItemAction", submitMenu, BuildMenuItemAction(submitMenu, submitClass, ReadString(steps, "submitMenuItemLabel"))));
            }

            var approvalName = ReadString(steps, "approvalName") ?? ReadString(steps, "approval");
            if (!string.IsNullOrWhiteSpace(approvalName))
            {
                AddWorkflowElementReference(template, approvalName, "Approval");
                artifacts.Add(new Artifact("workflowApproval", approvalName, BuildWorkflowApproval(approvalName, documentClass, ReadString(steps, "approvalLabel"))));
            }

            var taskName = ReadString(steps, "taskName") ?? ReadString(steps, "task");
            if (!string.IsNullOrWhiteSpace(taskName))
            {
                AddWorkflowElementReference(template, taskName, "Task");
                artifacts.Add(new Artifact("workflowTask", taskName, BuildWorkflowTask(taskName, documentClass, ReadString(steps, "taskLabel"))));
            }

            return artifacts;
        }

        private static object BuildWorkflowDocumentClass(string className, string queryName)
        {
            var ax = CreateAx("AxClass");
            Set(ax, "Name", className);
            Set(ax, "Extends", "WorkflowDocument");
            SetClassDeclaration(ax, "class " + className + " extends WorkflowDocument\n{\n}\n");
            AddMethod(ax, "getQueryName",
                "public QueryName getQueryName()\n" +
                "{\n" +
                "    return queryStr(" + queryName + ");\n" +
                "}\n");
            return ax;
        }

        private static object BuildSubmitToWorkflowClass(string className, string tableName, string workflowName)
        {
            var ax = CreateAx("AxClass");
            Set(ax, "Name", className);
            SetClassDeclaration(ax, "internal final class " + className + "\n{\n}\n");
            AddMethod(ax, "main",
                "public static void main(Args _args)\n" +
                "{\n" +
                "    " + tableName + " record = _args ? _args.record() : null;\n" +
                "\n" +
                "    if (record)\n" +
                "    {\n" +
                "        Workflow::activateFromWorkflowType(workflowTypeStr(" + workflowName + "), record.RecId, '', NoYes::No);\n" +
                "    }\n" +
                "}\n");
            return ax;
        }

        private static object BuildMenuItemAction(string name, string className, string label)
        {
            var ax = CreateAx("AxMenuItemAction");
            Set(ax, "Name", name);
            Set(ax, "Object", className);
            Set(ax, "ObjectType", "Class");
            SetOptional(ax, "Label", label);
            return ax;
        }

        private static object BuildWorkflowApproval(string name, string documentClass, string label)
        {
            var ax = CreateAx("AxWorkflowApproval");
            Set(ax, "Name", name);
            Set(ax, "Document", documentClass);
            SetOptional(ax, "Label", label);
            Set(ax, "Approve", BuildOutcome("Approve", "Complete"));
            Set(ax, "Deny", BuildOutcome("Deny", "Deny"));
            Set(ax, "Reject", BuildOutcome("Reject", "Return"));
            Set(ax, "RequestChange", BuildOutcome("RequestChange", "RequestChange"));
            return ax;
        }

        private static object BuildWorkflowTask(string name, string documentClass, string label)
        {
            var ax = CreateAx("AxWorkflowTask");
            Set(ax, "Name", name);
            Set(ax, "Document", documentClass);
            SetOptional(ax, "Label", label);
            AddToCollection(Get(ax, "WorkflowOutcomes"), BuildOutcome("Complete", "Complete"));
            return ax;
        }

        private static object BuildOutcome(string name, string type)
        {
            var outcome = CreateAx("AxWorkflowOutcome");
            Set(outcome, "Name", name);
            Set(outcome, "Type", type);
            return outcome;
        }

        private static void AddWorkflowElementReference(object template, string name, string type)
        {
            var reference = CreateAx("AxWorkflowElementReference");
            Set(reference, "Name", name);
            Set(reference, "ElementName", name);
            Set(reference, "Type", type);
            AddToCollection(Get(template, "SupportedElements"), reference);
        }

        private static JsonObject Finish(string operation, string model, bool overwrite, List<Artifact> artifacts)
        {
            var result = new JsonObject
            {
                ["ok"] = true,
                ["source"] = "vs-extension-wizard",
                ["operation"] = operation,
            };
            if (!string.IsNullOrWhiteSpace(model)) result["model"] = model;
            if (MetadataBootstrap.VsExtensionPath != null) result["vsExtensionPath"] = MetadataBootstrap.VsExtensionPath;

            if (!string.Equals(operation, "render", StringComparison.OrdinalIgnoreCase))
            {
                var save = SaveArtifacts(operation, model, overwrite, artifacts);
                if (save != null) return save;
            }

            var files = new JsonArray();
            foreach (var artifact in artifacts)
            {
                string xml, code, message;
                if (!MetadataObjectFactory.TrySerialize(artifact.Kind, artifact.Ax, out xml, out code, out message))
                {
                    return Fail(code, message);
                }

                var file = new JsonObject
                {
                    ["kind"] = artifact.Kind,
                    ["name"] = artifact.Name,
                    ["xml"] = xml,
                };
                if (!string.IsNullOrWhiteSpace(artifact.Path)) file["path"] = artifact.Path;
                files.Add(file);
            }
            result["files"] = files;
            return result;
        }

        private static JsonObject SaveArtifacts(string operation, string model, bool overwrite, List<Artifact> artifacts)
        {
            if (!MetadataBootstrap.TryInitialize())
            {
                return Fail("METADATA_UNAVAILABLE", MetadataBootstrap.LastError ?? "IMetadataProvider failed to initialise.");
            }

            var modelInfo = MetadataBootstrap.ReadModelInfo(model);
            if (modelInfo == null) return Fail("MODEL_NOT_FOUND", "Model '" + model + "' was not returned by ModelManifest.");
            var msi = MetadataBootstrap.BuildModelSaveInfo(modelInfo);
            if (msi == null) return Fail("MODEL_SAVE_INFO_FAILED", "Could not construct ModelSaveInfo for '" + model + "'.");

            foreach (var artifact in artifacts)
            {
                var saveOperation = operation;
                if (overwrite &&
                    string.Equals(operation, "create", StringComparison.OrdinalIgnoreCase) &&
                    MetadataBootstrap.ArtifactExists(artifact.Kind, artifact.Name))
                {
                    saveOperation = "update";
                }

                var saved = MetadataBootstrap.SaveArtifact(artifact.Kind, saveOperation, artifact.Ax, null, msi);
                if (!saved.ok)
                {
                    return Fail(saveOperation.ToUpperInvariant() + "_FAILED", artifact.Kind + " " + artifact.Name + ": " + saved.error);
                }

                var modelFolder = MetadataBootstrap.GetModelFolder(model, out _);
                var subfolder = MetadataBootstrap.GetAxSubfolder(artifact.Kind);
                if (!string.IsNullOrWhiteSpace(modelFolder) && !string.IsNullOrWhiteSpace(subfolder))
                {
                    artifact.Path = Path.Combine(modelFolder, subfolder, artifact.Name + ".xml");
                }
            }

            return null;
        }

        private static JsonObject ReadSteps(JsonObject args)
        {
            var steps = args != null ? args["steps"] as JsonObject : null;
            return steps ?? new JsonObject();
        }

        private static string ReadOperation(JsonObject args)
        {
            var operation = ReadString(args, "operation");
            operation = string.IsNullOrWhiteSpace(operation) ? "render" : operation.Trim();
            if (string.Equals(operation, "render", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(operation, "create", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(operation, "update", StringComparison.OrdinalIgnoreCase))
            {
                return operation;
            }
            return "render";
        }

        private static string ReadString(JsonObject obj, string key)
        {
            if (obj == null || string.IsNullOrWhiteSpace(key)) return null;
            var node = obj[key];
            if (node == null) return null;
            var value = node as JsonValue;
            string text;
            if (value != null && value.TryGetValue<string>(out text))
            {
                return text;
            }
            return node.ToString().Trim('"');
        }

        private static bool ReadBool(JsonObject obj, string key, bool defaultValue)
        {
            if (obj == null || string.IsNullOrWhiteSpace(key)) return defaultValue;
            var node = obj[key];
            if (node == null) return defaultValue;
            bool value;
            if (bool.TryParse(node.ToString().Trim('"'), out value)) return value;
            return defaultValue;
        }

        private static object CreateAx(string shortName)
        {
            var type = MetadataBootstrap.GetMetaModelTypeByShortName(shortName);
            if (type == null) throw new InvalidOperationException("Could not resolve " + shortName + ".");
            return Activator.CreateInstance(type);
        }

        private static object Get(object target, string propertyName)
        {
            if (target == null) return null;
            var prop = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            return prop != null ? prop.GetValue(target) : null;
        }

        private static void SetOptional(object target, string propertyName, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) Set(target, propertyName, value);
        }

        private static void Set(object target, string propertyName, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName)) return;
            var prop = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (prop == null || !prop.CanWrite) return;
            var propertyType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            if (value != null && propertyType.IsEnum && value is string)
            {
                value = Enum.Parse(propertyType, (string)value, true);
            }
            prop.SetValue(target, value);
        }

        private static void SetClassDeclaration(object axClass, string declaration)
        {
            var sourceCode = Get(axClass, "SourceCode");
            if (sourceCode == null) return;
            Set(sourceCode, "Declaration", declaration);
        }

        private static void AddMethod(object axClass, string name, string source)
        {
            var method = CreateAx("AxMethod");
            Set(method, "Name", name);
            Set(method, "Source", source);
            AddToCollection(Get(axClass, "Methods"), method);
        }

        private static void AddToCollection(object collection, object item)
        {
            if (collection == null || item == null) return;

            var methods = collection.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);
            foreach (var method in methods)
            {
                if (!string.Equals(method.Name, "Add", StringComparison.Ordinal)) continue;
                var parameters = method.GetParameters();
                if (parameters.Length != 1) continue;
                if (!parameters[0].ParameterType.IsAssignableFrom(item.GetType())) continue;
                method.Invoke(collection, new[] { item });
                return;
            }

            var list = collection as IList;
            if (list != null) list.Add(item);
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

        private sealed class Artifact
        {
            internal Artifact(string kind, string name, object ax)
            {
                Kind = kind;
                Name = name;
                Ax = ax;
            }

            internal string Kind { get; private set; }
            internal string Name { get; private set; }
            internal object Ax { get; private set; }
            internal string Path { get; set; }
        }
    }
}
