// <copyright file="Handlers.cs" company="d365fo-cli contributors">
// MIT
// </copyright>

using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Serialization;

namespace D365FO.Bridge
{
    /// <summary>
    /// JSON-RPC method implementations. All handlers return a <see cref="JsonNode"/>
    /// that becomes the <c>result</c> field. Handlers must not throw — the caller
    /// wraps exceptions into JSON-RPC errors.
    /// </summary>
    internal sealed class Handlers
    {
        internal JsonObject Ping()
        {
            var diag = MetadataBootstrap.Diagnostics();
            return new JsonObject
            {
                ["pong"] = true,
                ["version"] = Program.BridgeVersion,
                ["clr"] = Environment.Version.ToString(),
                ["framework"] = RuntimeInformation(),
                ["binPath"] = (string)diag["binPath"],
                ["packagesPath"] = (string)diag["packagesPath"],
                ["vsExtensionPath"] = (string)diag["vsExtensionPath"],
                ["metadataLoaded"] = (bool)diag["loaded"],
                ["metadataError"] = (string)diag["error"],
            };
        }

        internal JsonObject ReadClass(JsonObject args) { return ReadArtifact(args, "Classes", "class"); }
        internal JsonObject ReadTable(JsonObject args) { return ReadArtifact(args, "Tables", "table"); }
        internal JsonObject ReadEdt(JsonObject args) { return ReadArtifact(args, "Edts", "edt"); }
        internal JsonObject ReadEnum(JsonObject args) { return ReadArtifact(args, "Enums", "enum"); }
        internal JsonObject ReadForm(JsonObject args) { return ReadArtifact(args, "Forms", "form"); }

        // --- write path -----------------------------------------------------

        internal JsonObject SaveObject(JsonObject args) { return WriteArtifact(args, "create"); }
        internal JsonObject UpdateObject(JsonObject args) { return WriteArtifact(args, "update"); }
        internal JsonObject DeleteObject(JsonObject args) { return WriteArtifact(args, "delete"); }
        internal JsonObject ScaffoldObject(JsonObject args) { return MetadataObjectFactory.Scaffold(args); }
        internal JsonObject RunDataEntityWizard(JsonObject args) { return WizardScaffolder.RunDataEntityWizard(args); }
        internal JsonObject RunWorkflowWizard(JsonObject args) { return WizardScaffolder.RunWorkflowWizard(args); }
        internal JsonObject DesignerCatalog(JsonObject args) { return DesignerActionRunner.Catalog(args); }
        internal JsonObject DesignerActions(JsonObject args) { return DesignerActionRunner.Actions(args); }
        internal JsonObject DesignerRun(JsonObject args) { return DesignerActionRunner.Run(args); }
        internal JsonObject DesignerProperties(JsonObject args) { return DesignerActionRunner.Properties(args); }
        internal JsonObject DesignerPropertyOptions(JsonObject args) { return DesignerActionRunner.PropertyOptions(args); }
        internal JsonObject LintFile(JsonObject args) { return VsLintRunner.LintFile(args); }

        // --- xref (DYNAMICSXREFDB) -------------------------------------------

        internal JsonObject FindReferences(JsonObject args)
        {
            string symbol = args != null ? (string)args["symbol"] : null;
            string kind   = args != null ? (string)args["kind"]   : null;
            int    limit  = 200;
            if (args != null && args["limit"] is JsonNode ln && ln.GetValue<object>() is object lv && int.TryParse(lv.ToString(), out var li)) limit = li;
            return XrefRepository.Find(symbol, kind, limit);
        }

        // --- model manifest -------------------------------------------------

        internal JsonObject GetModelFolder(JsonObject args)
        {
            string name = args != null ? (string)args["name"] : null;
            if (string.IsNullOrWhiteSpace(name)) return Fail("MISSING_ARG", "name is required");
            if (!MetadataBootstrap.TryInitialize())
            {
                return Fail("METADATA_UNAVAILABLE", MetadataBootstrap.LastError ?? "IMetadataProvider failed to initialise.");
            }
            var folder = MetadataBootstrap.GetModelFolder(name, out var err);
            if (folder == null) return Fail(err ?? "MODEL_NOT_FOUND", err ?? ("Model '" + name + "' was not returned by ModelManifest."));
            return new JsonObject
            {
                ["ok"] = true,
                ["name"] = name,
                ["folder"] = folder,
                ["source"] = "bridge",
            };
        }

        /// <summary>
        /// Shared implementation for create/update/delete. Accepts args with
        /// <c>kind</c>, <c>name</c>, <c>model</c>, and for create/update an
        /// optional <c>xml</c> blob (full Ax* XML as on disk). When xml is
        /// missing on create, produces a minimal artefact with just
        /// <c>Name</c> set.
        /// </summary>
        private JsonObject WriteArtifact(JsonObject args, string op)
        {
            string kind  = args != null ? (string)args["kind"]  : null;
            string name  = args != null ? (string)args["name"]  : null;
            string model = args != null ? (string)args["model"] : null;
            string xml   = args != null ? (string)args["xml"]   : null;
            bool overwrite = args != null && ((bool?)args["overwrite"] ?? false);

            if (string.IsNullOrWhiteSpace(kind))  return Fail("MISSING_ARG", "kind is required");
            if (string.IsNullOrWhiteSpace(name))  return Fail("MISSING_ARG", "name is required");
            if (string.IsNullOrWhiteSpace(model)) return Fail("MISSING_ARG", "model is required");
            if (string.Equals(op, "update", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(xml))
            {
                return Fail("MISSING_ARG", "xml is required for update");
            }
            kind = MetadataBootstrap.NormalizeKind(kind);
            if (kind == null || !MetadataBootstrap.KindToCollection.ContainsKey(kind))
            {
                return Fail("INVALID_KIND", "kind must be one of the supported Ax metadata kinds.");
            }

            if (!string.Equals(op, "delete", StringComparison.OrdinalIgnoreCase))
            {
                var scaffoldArgs = new JsonObject
                {
                    ["kind"] = kind,
                    ["name"] = name,
                    ["model"] = model,
                    ["operation"] = op,
                    ["overwrite"] = overwrite,
                };
                if (!string.IsNullOrEmpty(xml))
                {
                    scaffoldArgs["properties"] = new JsonObject { ["xml"] = xml };
                }

                return MetadataObjectFactory.Scaffold(scaffoldArgs);
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
            if (msi == null) return Fail("MODEL_SAVE_INFO_FAILED", "Could not construct ModelSaveInfo for '" + model + "'.");

            // Delete path — no Ax instance needed.
            if (string.Equals(op, "delete", StringComparison.OrdinalIgnoreCase))
            {
                var (ok, err) = MetadataBootstrap.SaveArtifact(kind, "delete", null, name, msi);
                if (!ok) return Fail("DELETE_FAILED", err);
                return new JsonObject { ["ok"] = true, ["kind"] = kind, ["name"] = name, ["model"] = model, ["source"] = "bridge", ["op"] = "delete" };
            }

            // Create/Update — need an Ax* instance. For polymorphic kinds (edt, edtExtension)
            // the kind→base-type mapping resolves to an abstract class; the concrete subtype
            // must come from the input XML's root element name (e.g. AxEdtString).
            Type axType;
            object ax;
            if (!string.IsNullOrEmpty(xml))
            {
                string rootLocalName;
                try
                {
                    using (var sr = new StringReader(xml))
                    using (var xr = System.Xml.XmlReader.Create(sr, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit }))
                    {
                        xr.MoveToContent();
                        rootLocalName = xr.NodeType == System.Xml.XmlNodeType.Element ? xr.LocalName : null;
                    }
                }
                catch (Exception ex)
                {
                    return Fail("XML_PARSE_FAILED", ex.Message);
                }
                if (string.IsNullOrEmpty(rootLocalName))
                    return Fail("XML_PARSE_FAILED", "Could not read root element of input xml.");

                axType = MetadataBootstrap.GetMetaModelTypeByShortName(rootLocalName)
                         ?? MetadataBootstrap.GetMetaModelType(kind);
                if (axType == null) return Fail("TYPE_NOT_FOUND", "Could not resolve Ax type for root element '" + rootLocalName + "'.");
                if (axType.IsAbstract)
                    return Fail("ABSTRACT_TYPE", "Root element '" + rootLocalName + "' maps to abstract type '" + axType.FullName + "'. Use a concrete subtype root such as AxEdtString.");

                try
                {
                    var serializer = new XmlSerializer(axType);
                    using (var reader = new StringReader(xml))
                    {
                        ax = serializer.Deserialize(reader);
                    }
                }
                catch (Exception ex)
                {
                    var inner = ex.InnerException?.Message;
                    return Fail("XML_DESERIALIZE_FAILED", ex.Message + (inner != null ? " / " + inner : string.Empty));
                }
            }
            else
            {
                axType = MetadataBootstrap.GetMetaModelType(kind);
                if (axType == null) return Fail("TYPE_NOT_FOUND", "Could not resolve Ax type for kind '" + kind + "'.");
                if (axType.IsAbstract)
                    return Fail("ABSTRACT_TYPE", "Cannot construct kind '" + kind + "' without xml — base type '" + axType.FullName + "' is abstract. Provide xml with a concrete root element such as AxEdtString.");
                var ctor = axType.GetConstructor(Type.EmptyTypes);
                if (ctor == null) return Fail("TYPE_NOT_FOUND", "Ax type '" + axType.Name + "' has no parameterless ctor.");
                ax = ctor.Invoke(null);
            }

            // Always enforce Name from the request — it's authoritative.
            var nameProp = axType.GetProperty("Name");
            if (nameProp != null && nameProp.CanWrite) nameProp.SetValue(ax, name);

            var (ok2, err2) = MetadataBootstrap.SaveArtifact(kind, op, ax, null, msi);
            if (!ok2)
            {
                return Fail(op.ToUpperInvariant() + "_FAILED", err2);
            }
            return new JsonObject
            {
                ["ok"] = true,
                ["kind"] = kind,
                ["name"] = name,
                ["model"] = model,
                ["source"] = "bridge",
                ["op"] = op,
            };
        }

        private JsonObject ReadArtifact(JsonObject args, string collectionName, string kind)
        {
            string name = args != null ? (string)args["name"] : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                return Fail("MISSING_ARG", "name is required");
            }

            if (!MetadataBootstrap.TryInitialize())
            {
                return Fail("METADATA_UNAVAILABLE",
                    MetadataBootstrap.LastError ??
                    "IMetadataProvider failed to initialise; set D365FO_PACKAGES_PATH on a D365FO VM.");
            }

            object artifact;
            try
            {
                artifact = MetadataBootstrap.ReadArtifact(collectionName, name);
            }
            catch (Exception ex)
            {
                return Fail("READ_FAILED", ex.GetType().Name + ": " + ex.Message);
            }

            if (artifact == null)
            {
                // Kernel-enum fallback: NoYes, Exists, ... are CLR enums
                // compiled into the X++ runtime assemblies. Only attempt the
                // probe when the original request was for an enum.
                if (string.Equals(kind, "enum", StringComparison.OrdinalIgnoreCase))
                {
                    var kernel = MetadataBootstrap.TryResolveKernelEnum(name);
                    if (kernel != null)
                    {
                        return new JsonObject
                        {
                            ["ok"] = true,
                            ["kind"] = kind,
                            ["name"] = name,
                            ["source"] = "bridge-kernel",
                            ["data"] = kernel,
                        };
                    }
                }
                return Fail("NOT_FOUND", kind + " '" + name + "' was not returned by IMetadataProvider.");
            }

            JsonNode body;
            try
            {
                body = AxSerializer.ToJson(artifact);
            }
            catch (Exception ex)
            {
                return Fail("SERIALIZE_FAILED", ex.GetType().Name + ": " + ex.Message);
            }

            return new JsonObject
            {
                ["ok"] = true,
                ["kind"] = kind,
                ["name"] = name,
                ["source"] = "bridge",
                ["data"] = body,
            };
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

        private static string RuntimeInformation()
        {
            try
            {
                var asm = typeof(object).Assembly;
                var attr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                return attr != null ? attr.InformationalVersion : asm.GetName().Version != null ? asm.GetName().Version.ToString() : "unknown";
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
