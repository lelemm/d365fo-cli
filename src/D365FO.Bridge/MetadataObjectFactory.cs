// <copyright file="MetadataObjectFactory.cs" company="d365fo-cli contributors">
// MIT
// </copyright>

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Serialization;

namespace D365FO.Bridge
{
    /// <summary>
    /// Creates, canonicalizes, and saves Ax* metadata objects through the
    /// Microsoft metadata assemblies. The CLI can pass a lightweight property
    /// bag or a legacy XML seed; in both cases this factory materializes real
    /// MetaModel objects before returning XML or writing through
    /// <c>IMetadataProvider</c>.
    /// </summary>
    internal static class MetadataObjectFactory
    {
        private const string XsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";

        internal static JsonObject Scaffold(JsonObject args)
        {
            string kind = args != null ? (string)args["kind"] : null;
            string name = args != null ? (string)args["name"] : null;
            string model = args != null ? (string)args["model"] : null;
            string operation = args != null ? (string)args["operation"] : null;
            bool overwrite = args != null && ((bool?)args["overwrite"] ?? false);
            var properties = args != null ? args["properties"] as JsonObject : null;

            if (string.IsNullOrWhiteSpace(kind)) return Fail("MISSING_ARG", "kind is required");
            if (string.IsNullOrWhiteSpace(name)) return Fail("MISSING_ARG", "name is required");

            var canonicalKind = MetadataBootstrap.NormalizeKind(kind);
            if (canonicalKind == null || !MetadataBootstrap.KindToCollection.ContainsKey(canonicalKind))
            {
                return Fail("INVALID_KIND", "Unsupported metadata kind: " + kind);
            }

            operation = string.IsNullOrWhiteSpace(operation) ? "render" : operation.Trim();
            if (!string.Equals(operation, "render", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(operation, "create", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(operation, "update", StringComparison.OrdinalIgnoreCase))
            {
                return Fail("BAD_OPERATION", "operation must be one of: render, create, update");
            }

            if (!string.Equals(operation, "render", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(model))
            {
                return Fail("MISSING_ARG", "model is required for create/update");
            }

            if (!MetadataBootstrap.TryInitializeAssemblyResolution())
            {
                return Fail("METADATA_UNAVAILABLE",
                    MetadataBootstrap.LastError ??
                    "Microsoft metadata assemblies are unavailable. Set D365FO_VS_EXTENSION_PATH, D365FO_BIN_PATH, or D365FO_PACKAGES_PATH.");
            }

            var warnings = new List<string>();
            object ax;
            string seedXml = ReadXmlSeed(properties);
            var createdFromTemplate = string.IsNullOrWhiteSpace(seedXml);
            if (!string.IsNullOrWhiteSpace(seedXml))
            {
                if (!TryDeserialize(canonicalKind, seedXml, out ax, out var code, out var message))
                {
                    return Fail(code, message);
                }
            }
            else
            {
                if (!TryCreate(canonicalKind, properties, warnings, out ax, out var code, out var message))
                {
                    return Fail(code, message);
                }
            }

            SetName(ax, name);

            if (createdFromTemplate)
            {
                ApplyVsTemplateInitializer(canonicalKind, ax, warnings);
            }
            ApplyProperties(ax, properties, warnings);

            if (!TrySerialize(canonicalKind, ax, out var xml, out var serializeCode, out var serializeMessage))
            {
                return Fail(serializeCode, serializeMessage);
            }

            if (string.Equals(operation, "render", StringComparison.OrdinalIgnoreCase))
            {
                var renderResult = Success(canonicalKind, name, model, warnings, createdFromTemplate ? "vs-extension-template" : "bridge");
                renderResult["operation"] = "render";
                renderResult["xml"] = xml;
                return renderResult;
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

            var saveOperation = operation;
            if (overwrite &&
                string.Equals(operation, "create", StringComparison.OrdinalIgnoreCase) &&
                MetadataBootstrap.ArtifactExists(canonicalKind, name))
            {
                saveOperation = "update";
            }

            var (ok, err) = MetadataBootstrap.SaveArtifact(canonicalKind, saveOperation, ax, null, msi);
            if (!ok)
            {
                return Fail(saveOperation.ToUpperInvariant() + "_FAILED", err);
            }

            var result = Success(canonicalKind, name, model, warnings, createdFromTemplate ? "vs-extension-template" : "bridge");
            result["operation"] = saveOperation;
            var modelFolder = MetadataBootstrap.GetModelFolder(model, out _);
            var subfolder = MetadataBootstrap.GetAxSubfolder(canonicalKind);
            if (!string.IsNullOrWhiteSpace(modelFolder) && !string.IsNullOrWhiteSpace(subfolder))
            {
                result["path"] = Path.Combine(modelFolder, subfolder, name + ".xml");
            }
            result["xml"] = xml;
            return result;
        }

        internal static bool TryDeserialize(string kind, string xml, out object ax, out string code, out string message)
        {
            ax = null;
            code = null;
            message = null;

            string rootLocalName;
            string xsiTypeName;
            try
            {
                ReadXmlShape(xml, out rootLocalName, out xsiTypeName);
            }
            catch (Exception ex)
            {
                code = "XML_PARSE_FAILED";
                message = ex.Message;
                return false;
            }

            if (string.IsNullOrEmpty(rootLocalName))
            {
                code = "XML_PARSE_FAILED";
                message = "Could not read root element of input xml.";
                return false;
            }

            var baseType = MetadataBootstrap.GetMetaModelType(kind)
                           ?? MetadataBootstrap.GetMetaModelTypeByShortName(rootLocalName);
            Type concreteType = null;
            if (!string.IsNullOrEmpty(xsiTypeName))
            {
                concreteType = MetadataBootstrap.GetMetaModelTypeByShortName(xsiTypeName);
                if (concreteType == null)
                {
                    code = "TYPE_NOT_FOUND";
                    message = "Could not resolve Ax type for XMLSchema-instance type '" + xsiTypeName + "'.";
                    return false;
                }
            }
            else
            {
                concreteType = MetadataBootstrap.GetMetaModelTypeByShortName(rootLocalName) ?? baseType;
            }

            if (concreteType == null)
            {
                code = "TYPE_NOT_FOUND";
                message = "Could not resolve Ax type for root element '" + rootLocalName + "'.";
                return false;
            }

            if (baseType != null && !baseType.IsAssignableFrom(concreteType))
            {
                baseType = concreteType;
            }

            Type serializerType = baseType ?? concreteType;
            Type[] extraTypes = serializerType != concreteType && serializerType.IsAssignableFrom(concreteType)
                ? new[] { concreteType }
                : Type.EmptyTypes;

            Exception dataContractException = null;
            try
            {
                var serializer = new DataContractSerializer(concreteType);
                using (var reader = XmlReader.Create(new StringReader(xml), SafeXmlReaderSettings()))
                {
                    ax = serializer.ReadObject(reader);
                }
                return true;
            }
            catch (Exception ex)
            {
                dataContractException = ex;
            }

            try
            {
                var serializer = extraTypes.Length > 0
                    ? new XmlSerializer(serializerType, extraTypes)
                    : new XmlSerializer(serializerType, new XmlRootAttribute(rootLocalName));
                using (var reader = XmlReader.Create(new StringReader(xml), SafeXmlReaderSettings()))
                {
                    ax = serializer.Deserialize(reader);
                }
                return true;
            }
            catch (Exception ex)
            {
                code = "XML_DESERIALIZE_FAILED";
                var primary = dataContractException ?? ex;
                var inner = primary.InnerException != null ? " / " + primary.InnerException.Message : string.Empty;
                message = primary.Message + inner;
                return false;
            }
        }

        internal static bool TrySerialize(string kind, object ax, out string xml, out string code, out string message)
        {
            xml = null;
            code = null;
            message = null;
            if (ax == null)
            {
                code = "SERIALIZE_FAILED";
                message = "Ax object is null.";
                return false;
            }

            var concreteType = ax.GetType();
            var baseType = MetadataBootstrap.GetMetaModelType(kind);
            Type serializerType = concreteType;
            Type[] extraTypes = Type.EmptyTypes;
            if (baseType != null && baseType != concreteType && baseType.IsAssignableFrom(concreteType))
            {
                serializerType = baseType;
                extraTypes = new[] { concreteType };
            }

            Exception dataContractException = null;
            try
            {
                var serializer = new DataContractSerializer(concreteType);
                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    OmitXmlDeclaration = true,
                };
                using (var sw = new StringWriter(CultureInfo.InvariantCulture))
                using (var xw = XmlWriter.Create(sw, settings))
                {
                    serializer.WriteObject(xw, ax);
                    xw.Flush();
                    xml = sw.ToString();
                }
                return true;
            }
            catch (Exception ex)
            {
                dataContractException = ex;
            }

            try
            {
                var serializer = extraTypes.Length > 0
                    ? new XmlSerializer(serializerType, extraTypes)
                    : new XmlSerializer(serializerType);
                var ns = new XmlSerializerNamespaces();
                ns.Add("i", XsiNamespace);

                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    OmitXmlDeclaration = true,
                };
                using (var sw = new StringWriter(CultureInfo.InvariantCulture))
                using (var xw = XmlWriter.Create(sw, settings))
                {
                    serializer.Serialize(xw, ax, ns);
                    xw.Flush();
                    xml = sw.ToString();
                }
                return true;
            }
            catch (Exception ex)
            {
                code = "XML_SERIALIZE_FAILED";
                var primary = dataContractException ?? ex;
                var inner = primary.InnerException != null ? " / " + primary.InnerException.Message : string.Empty;
                message = primary.Message + inner;
                return false;
            }
        }

        private static bool TryCreate(
            string kind,
            JsonObject properties,
            List<string> warnings,
            out object ax,
            out string code,
            out string message)
        {
            ax = null;
            code = null;
            message = null;

            var axType = ResolveConcreteType(kind, properties);
            if (axType == null)
            {
                code = "TYPE_NOT_FOUND";
                message = "Could not resolve Ax type for kind '" + kind + "'.";
                return false;
            }
            if (axType.IsAbstract)
            {
                code = "ABSTRACT_TYPE";
                message = "Kind '" + kind + "' maps to abstract type '" + axType.FullName + "'. Supply properties.xml/sourceXml or a concrete properties.type.";
                return false;
            }

            var ctor = axType.GetConstructor(Type.EmptyTypes);
            if (ctor == null)
            {
                code = "TYPE_NOT_FOUND";
                message = "Ax type '" + axType.Name + "' has no parameterless ctor.";
                return false;
            }

            ax = ctor.Invoke(null);
            return true;
        }

        private static Type ResolveConcreteType(string kind, JsonObject properties)
        {
            var requestedType = properties != null ? (string)properties["type"] : null;
            if (string.IsNullOrWhiteSpace(requestedType))
            {
                requestedType = properties != null ? (string)properties["axType"] : null;
            }
            if (!string.IsNullOrWhiteSpace(requestedType))
            {
                var clean = StripPrefix(requestedType.Trim());
                var requested = MetadataBootstrap.GetMetaModelTypeByShortName(clean);
                if (requested != null) return requested;
            }

            return MetadataBootstrap.GetMetaModelType(kind);
        }

        private static void ApplyVsTemplateInitializer(string kind, object ax, List<string> warnings)
        {
            if (ax == null) return;

            string templateFieldName = null;
            if (string.Equals(kind, "class", StringComparison.OrdinalIgnoreCase))
            {
                templateFieldName = "Class";
            }
            else if (string.Equals(kind, "table", StringComparison.OrdinalIgnoreCase))
            {
                templateFieldName = "Table";
            }

            if (string.IsNullOrEmpty(templateFieldName)) return;

            try
            {
                var projectSystemPath = MetadataBootstrap.FindAssemblyPath("Microsoft.Dynamics.Framework.Tools.ProjectSystem.17.0.dll");
                if (string.IsNullOrEmpty(projectSystemPath) || !File.Exists(projectSystemPath))
                {
                    warnings.Add("VS item-template initializer was skipped because ProjectSystem.17.0.dll was not found.");
                    return;
                }

                var projectSystem = Assembly.LoadFrom(projectSystemPath);
                var wizardType = projectSystem.GetType("Microsoft.Dynamics.Framework.Tools.ProjectSystem.ItemCreationWizard", false);
                if (wizardType == null)
                {
                    warnings.Add("VS item-template initializer was skipped because ItemCreationWizard was not found.");
                    return;
                }

                var templateField = wizardType.GetField(templateFieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var template = templateField != null ? templateField.GetValue(null) as string : null;
                if (string.IsNullOrWhiteSpace(template))
                {
                    warnings.Add("VS item-template initializer was skipped because the " + templateFieldName + " template was not found.");
                    return;
                }

                var sourceCodeProp = ax.GetType().GetProperty("SourceCode", BindingFlags.Instance | BindingFlags.Public);
                var sourceCode = sourceCodeProp != null ? sourceCodeProp.GetValue(ax) : null;
                if (sourceCode == null && sourceCodeProp != null && sourceCodeProp.CanWrite)
                {
                    var ctor = sourceCodeProp.PropertyType.GetConstructor(Type.EmptyTypes);
                    if (ctor != null)
                    {
                        sourceCode = ctor.Invoke(null);
                        sourceCodeProp.SetValue(ax, sourceCode);
                    }
                }
                if (sourceCode == null)
                {
                    warnings.Add("VS item-template initializer was skipped because SourceCode was unavailable.");
                    return;
                }

                var declarationProp = sourceCode.GetType().GetProperty("Declaration", BindingFlags.Instance | BindingFlags.Public);
                if (declarationProp == null || !declarationProp.CanWrite)
                {
                    warnings.Add("VS item-template initializer was skipped because SourceCode.Declaration was not writable.");
                    return;
                }

                var nameProp = ax.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public);
                var name = nameProp != null ? nameProp.GetValue(ax) as string : null;
                declarationProp.SetValue(sourceCode, string.Format(CultureInfo.InvariantCulture, template, name ?? string.Empty));
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException ?? ex;
                warnings.Add("VS item-template initializer was skipped: " + inner.GetType().Name + ": " + inner.Message);
            }
            catch (Exception ex)
            {
                warnings.Add("VS item-template initializer was skipped: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void ApplyProperties(object ax, JsonObject properties, List<string> warnings)
        {
            if (ax == null || properties == null) return;

            foreach (var property in properties)
            {
                var key = property.Key;
                if (string.Equals(key, "xml", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, "sourceXml", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, "type", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, "axType", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var prop = ax.GetType().GetProperty(
                    key,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null || !prop.CanWrite || prop.GetIndexParameters().Length > 0)
                {
                    warnings.Add("Property '" + key + "' was ignored because it is not writable on " + ax.GetType().Name + ".");
                    continue;
                }

                if (property.Value is JsonObject nestedObject && !IsSimpleType(prop.PropertyType))
                {
                    var nestedTarget = prop.GetValue(ax);
                    if (nestedTarget == null)
                    {
                        if (!prop.CanWrite)
                        {
                            warnings.Add("Property '" + key + "' was ignored because it is null and not writable on " + ax.GetType().Name + ".");
                            continue;
                        }

                        nestedTarget = TryCreateInstance(prop.PropertyType);
                        if (nestedTarget == null)
                        {
                            warnings.Add("Property '" + key + "' was ignored because " + prop.PropertyType.Name + " could not be created.");
                            continue;
                        }

                        prop.SetValue(ax, nestedTarget);
                    }

                    ApplyProperties(nestedTarget, nestedObject, warnings);
                    continue;
                }

                if (!TryConvertJson(property.Value, prop.PropertyType, out var value))
                {
                    warnings.Add("Property '" + key + "' was ignored because the value could not be converted to " + prop.PropertyType.Name + ".");
                    continue;
                }

                try
                {
                    prop.SetValue(ax, value);
                }
                catch (Exception ex)
                {
                    warnings.Add("Property '" + key + "' was ignored: " + ex.Message);
                }
            }
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

        private static bool TryConvertJson(JsonNode node, Type targetType, out object value)
        {
            value = null;
            if (targetType == null) return false;

            var nullable = Nullable.GetUnderlyingType(targetType);
            if (node == null)
            {
                if (!targetType.IsValueType || nullable != null) return true;
                return false;
            }

            var effectiveType = nullable ?? targetType;
            try
            {
                if (effectiveType == typeof(string))
                {
                    value = (string)node;
                    return true;
                }
                if (effectiveType == typeof(bool))
                {
                    value = (bool?)node ?? bool.Parse(node.ToString());
                    return true;
                }
                if (effectiveType == typeof(int))
                {
                    value = (int?)node ?? int.Parse(node.ToString(), CultureInfo.InvariantCulture);
                    return true;
                }
                if (effectiveType == typeof(long))
                {
                    value = (long?)node ?? long.Parse(node.ToString(), CultureInfo.InvariantCulture);
                    return true;
                }
                if (effectiveType == typeof(decimal))
                {
                    value = (decimal?)node ?? decimal.Parse(node.ToString(), CultureInfo.InvariantCulture);
                    return true;
                }
                if (effectiveType.IsEnum)
                {
                    value = Enum.Parse(effectiveType, StripQuotes(node.ToString()), true);
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static void SetName(object ax, string name)
        {
            if (ax == null || string.IsNullOrWhiteSpace(name)) return;
            var nameProp = ax.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
            if (nameProp != null && nameProp.CanWrite)
            {
                nameProp.SetValue(ax, name);
            }
        }

        private static string ReadXmlSeed(JsonObject properties)
        {
            if (properties == null) return null;
            var xml = (string)properties["xml"];
            if (!string.IsNullOrWhiteSpace(xml)) return xml;
            return (string)properties["sourceXml"];
        }

        private static void ReadXmlShape(string xml, out string rootLocalName, out string xsiTypeName)
        {
            rootLocalName = null;
            xsiTypeName = null;
            using (var reader = XmlReader.Create(new StringReader(xml), SafeXmlReaderSettings()))
            {
                reader.MoveToContent();
                if (reader.NodeType != XmlNodeType.Element)
                {
                    return;
                }

                rootLocalName = reader.LocalName;
                xsiTypeName = StripPrefix(reader.GetAttribute("type", XsiNamespace));
            }
        }

        private static XmlReaderSettings SafeXmlReaderSettings()
        {
            return new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            };
        }

        private static JsonObject Success(string kind, string name, string model, List<string> warnings, string source)
        {
            var result = new JsonObject
            {
                ["ok"] = true,
                ["kind"] = kind,
                ["name"] = name,
                ["source"] = string.IsNullOrWhiteSpace(source) ? "bridge" : source,
            };
            if (!string.IsNullOrWhiteSpace(model)) result["model"] = model;
            if (MetadataBootstrap.VsExtensionPath != null) result["vsExtensionPath"] = MetadataBootstrap.VsExtensionPath;
            if (warnings != null && warnings.Count > 0)
            {
                var warningArray = new JsonArray();
                foreach (var warning in warnings) warningArray.Add(warning);
                result["warnings"] = warningArray;
            }
            return result;
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

        private static string StripPrefix(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var idx = value.IndexOf(':');
            return idx >= 0 ? value.Substring(idx + 1) : value;
        }

        private static string StripQuotes(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            return value.Trim().Trim('"');
        }
    }
}
