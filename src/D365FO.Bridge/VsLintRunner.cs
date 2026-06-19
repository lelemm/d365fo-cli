// <copyright file="VsLintRunner.cs" company="d365fo-cli contributors">
// MIT
// </copyright>

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Xml;

namespace D365FO.Bridge
{
    /// <summary>
    /// Runs Microsoft D365FO best-practice diagnostics through the Visual Studio
    /// extension / metadata assemblies. This intentionally stays reflection-only:
    /// the bridge can build without the D365FO SDK installed, then bind late on
    /// the developer VM.
    /// </summary>
    internal static class VsLintRunner
    {
        private static readonly Dictionary<string, string> RootToKind =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "AxClass", "class" },
                { "AxTable", "table" },
                { "AxForm", "form" },
                { "AxEdt", "edt" },
                { "AxEdtString", "edt" },
                { "AxEdtInt", "edt" },
                { "AxEdtInt64", "edt" },
                { "AxEdtReal", "edt" },
                { "AxEdtDate", "edt" },
                { "AxEdtUtcDateTime", "edt" },
                { "AxEdtEnum", "edt" },
                { "AxEnum", "enum" },
                { "AxQuery", "query" },
                { "AxDataEntityView", "dataEntityView" },
                { "AxTableExtension", "tableExtension" },
                { "AxFormExtension", "formExtension" },
                { "AxMenuItemDisplay", "menuItemDisplay" },
                { "AxMenuItemAction", "menuItemAction" },
                { "AxMenuItemOutput", "menuItemOutput" },
                { "AxSecurityPrivilege", "securityPrivilege" },
                { "AxSecurityDuty", "securityDuty" },
                { "AxSecurityRole", "securityRole" },
                { "AxService", "service" },
                { "AxServiceGroup", "serviceGroup" },
            };

        private static readonly Dictionary<string, string> KindToTarget =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "class", "Class" },
                { "table", "Table" },
                { "form", "Form" },
                { "edt", "ExtendedDataType" },
                { "enum", "Enum" },
                { "query", "Query" },
                { "dataEntityView", "DataEntityView" },
                { "tableExtension", "TableExtension" },
                { "formExtension", "FormExtension" },
                { "menuItemDisplay", "MenuItemDisplay" },
                { "menuItemAction", "MenuItemAction" },
                { "menuItemOutput", "MenuItemOutput" },
                { "securityPrivilege", "SecurityPrivilege" },
                { "securityDuty", "SecurityDuty" },
                { "securityRole", "SecurityRole" },
                { "service", "Service" },
                { "serviceGroup", "ServiceGroup" },
            };

        internal static JsonObject LintFile(JsonObject args)
        {
            string file = args != null ? (string)args["file"] : null;
            string model = args != null ? (string)args["model"] : null;
            string kind = args != null ? (string)args["kind"] : null;

            if (string.IsNullOrWhiteSpace(file)) return Fail("MISSING_ARG", "file is required");
            if (!File.Exists(file)) return Fail("INPUT_NOT_FOUND", "File not found: " + file);

            var xml = File.ReadAllText(file);
            if (!LooksLikeXml(xml))
            {
                return Fail(
                    "UNSUPPORTED_FILE",
                    "Bridge lint currently supports saved AOT XML files. Raw .xpp text should use the offline validator fallback.");
            }

            if (!MetadataBootstrap.TryInitialize())
            {
                return Fail(
                    "METADATA_UNAVAILABLE",
                    MetadataBootstrap.LastError ??
                    "IMetadataProvider failed to initialise; set D365FO_PACKAGES_PATH on a D365FO VM.");
            }

            LoadBestPracticeAssemblies();

            string rootName;
            try
            {
                rootName = ReadRootName(xml);
            }
            catch (Exception ex)
            {
                return Fail("XML_PARSE_FAILED", ex.Message);
            }

            if (string.IsNullOrWhiteSpace(kind))
            {
                RootToKind.TryGetValue(rootName ?? string.Empty, out kind);
            }
            kind = MetadataBootstrap.NormalizeKind(kind);
            if (string.IsNullOrWhiteSpace(kind) || !RootToKind.ContainsValue(kind))
            {
                return Fail("UNSUPPORTED_KIND", "Bridge lint does not know how to map XML root '" + rootName + "' to a BP target.");
            }

            if (!KindToTarget.TryGetValue(kind, out var targetName))
            {
                return Fail("UNSUPPORTED_KIND", "Bridge lint does not have a BestPracticeCheckerTargets mapping for kind '" + kind + "'.");
            }

            if (!MetadataObjectFactory.TryDeserialize(kind, xml, out var ax, out var code, out var message))
            {
                return Fail(code, message);
            }

            model = string.IsNullOrWhiteSpace(model) ? InferModelFromPath(file) : model;
            if (string.IsNullOrWhiteSpace(model))
            {
                return Fail("MISSING_MODEL", "Could not infer model from file path. Pass --model for bridge lint.");
            }

            try
            {
                var sink = CreateDiagnosticsSink();
                var framework = CreateBestPracticeFramework(model, sink);
                var targets = ParseTarget(targetName);

                var run = framework.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "RunChecksOn") return false;
                        var ps = m.GetParameters();
                        return ps.Length == 2 &&
                               ps[0].ParameterType.IsAssignableFrom(ax.GetType()) &&
                               ps[1].ParameterType.IsEnum;
                    });

                if (run == null)
                {
                    return Fail("BP_RUNNER_UNAVAILABLE", "BestPracticeFramework.RunChecksOn(INamedObject, BestPracticeCheckerTargets) was not found.");
                }

                run.Invoke(framework, new[] { ax, targets });
                var diagnostics = ReadDiagnostics(sink);
                var errors = diagnostics.Count(d => IsError((string)d["severity"]));
                var warnings = diagnostics.Count(d => IsWarning((string)d["severity"]));

                return new JsonObject
                {
                    ["ok"] = true,
                    ["source"] = "vs-extension-best-practice-framework",
                    ["file"] = Path.GetFullPath(file),
                    ["kind"] = kind,
                    ["model"] = model,
                    ["root"] = rootName,
                    ["errors"] = errors,
                    ["warnings"] = warnings,
                    ["diagnostics"] = ToJsonArray(diagnostics),
                    ["vsExtensionPath"] = MetadataBootstrap.VsExtensionPath ?? string.Empty,
                };
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException ?? ex;
                return Fail("BP_RUN_FAILED", inner.GetType().Name + ": " + inner.Message);
            }
            catch (Exception ex)
            {
                return Fail("BP_RUN_FAILED", ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool LooksLikeXml(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith("<", StringComparison.Ordinal);
        }

        private static string ReadRootName(string xml)
        {
            using (var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            }))
            {
                reader.MoveToContent();
                return reader.NodeType == XmlNodeType.Element ? reader.LocalName : null;
            }
        }

        private static void LoadBestPracticeAssemblies()
        {
            foreach (var file in new[]
                     {
                         "Microsoft.Dynamics.AX.Framework.Xlnt.XppCore.dll",
                         "Microsoft.Dynamics.AX.Framework.Xlnt.XppParser.dll",
                         "Microsoft.Dynamics.AX.Framework.Xlnt.XppParser.Pass2.dll",
                         "Microsoft.Dynamics.AX.Framework.BestPractices.Common.dll",
                         "Microsoft.Dynamics.AX.Framework.BestPracticeExtensions.dll",
                         "Microsoft.Dynamics.AX.Framework.BestPracticeFramework.dll",
                         "Microsoft.Dynamics.AX.Framework.CodeStyleRules.dll",
                         "Microsoft.Dynamics.AX.Framework.StaticCodeValidationRules.dll",
                         "Microsoft.Dynamics.AX.Framework.BestPracticeFramework.UIRules.dll",
                     })
            {
                var path = MetadataBootstrap.FindAssemblyPath(file);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    try { Assembly.LoadFrom(path); } catch { }
                }
            }
        }

        private static object CreateDiagnosticsSink()
        {
            var type = ResolveType("Microsoft.Dynamics.AX.Framework.BestPractices.NullDiagnosticSink");
            if (type == null) throw new InvalidOperationException("NullDiagnosticSink was not found.");
            return Activator.CreateInstance(type);
        }

        private static object CreateBestPracticeFramework(string model, object sink)
        {
            var provider = MetadataBootstrap.GetProvider();
            if (provider == null) throw new InvalidOperationException(MetadataBootstrap.LastError ?? "metadata provider unavailable");

            var validatorType = ResolveType("Microsoft.Dynamics.AX.Framework.Xlnt.XppParser.Pass2.CompilerMetadataValidator");
            var serviceType = ResolveType("Microsoft.Dynamics.AX.Framework.Xlnt.XppParser.Pass2.ServiceMetadataProvider");
            var multipassType = ResolveType("Microsoft.Dynamics.AX.Framework.Xlnt.XppParser.Pass2.MultipassAdministrator");
            var wrapperType = ResolveType("Microsoft.Dynamics.AX.Framework.BestPractices.Common.Extensions.XppCompilerWrapper");
            var optionsType = ResolveType("Microsoft.Dynamics.AX.Framework.BestPractices.BestPracticeOptions");
            var factoryType = ResolveType("Microsoft.Dynamics.AX.Framework.BestPractices.BestPracticeFrameworkFactory");

            if (validatorType == null || serviceType == null || multipassType == null ||
                wrapperType == null || optionsType == null || factoryType == null)
            {
                throw new InvalidOperationException("One or more Microsoft best-practice framework types were not found.");
            }

            var validator = Activator.CreateInstance(validatorType);
            var service = CreateServiceMetadataProvider(serviceType, provider, model, validator);
            TrySet(service, "DiagnosticSink", sink);

            var multipass = CreateInstance(multipassType, service);
            TrySet(multipass, "DiagnosticsHandler", sink);
            var wrapper = CreateInstance(wrapperType, multipass, sink);

            var options = Activator.CreateInstance(optionsType);
            TrySet(options, "ModelName", model);
            TrySet(options, "ModuleName", model);
            TrySet(options, "PackagesRootPath", MetadataBootstrap.PackagesPath);
            TrySet(options, "MetadataFolderPath", MetadataBootstrap.PackagesPath);
            TrySet(options, "CompilerMetadataPath", MetadataBootstrap.BinPath);
            TrySet(options, "NoParallel", true);
            SetReferencedAssembliesFolders(options, optionsType);

            var enabledRules = ReadEnabledRules(service, model);
            var ctor = factoryType.GetConstructors()
                .FirstOrDefault(c =>
                {
                    var ps = c.GetParameters();
                    return ps.Length == 8 &&
                           ps[1].ParameterType.IsAssignableFrom(sink.GetType()) &&
                           ps[2].ParameterType.IsAssignableFrom(service.GetType()) &&
                           ps[3].ParameterType.IsAssignableFrom(provider.GetType()) &&
                           ps[4].ParameterType.IsAssignableFrom(wrapper.GetType());
                });
            if (ctor == null)
            {
                throw new MissingMethodException("BestPracticeFrameworkFactory 8-argument constructor was not found.");
            }

            var factory = ctor.Invoke(new[] { model, sink, service, provider, wrapper, true, enabledRules, options });
            return factoryType.GetMethod("Create", Type.EmptyTypes).Invoke(factory, null);
        }

        private static object CreateServiceMetadataProvider(Type serviceType, object provider, string model, object validator)
        {
            foreach (var ctor in serviceType.GetConstructors())
            {
                var ps = ctor.GetParameters();
                if (ps.Length == 3 &&
                    ps[0].ParameterType.IsAssignableFrom(provider.GetType()) &&
                    ps[1].ParameterType == typeof(string) &&
                    ps[2].ParameterType.IsAssignableFrom(validator.GetType()))
                {
                    return ctor.Invoke(new[] { provider, model, validator });
                }
            }

            throw new MissingMethodException("ServiceMetadataProvider(IMetadataProvider,string,CompilerMetadataValidator) constructor was not found.");
        }

        private static object CreateInstance(Type type, params object[] args)
        {
            foreach (var ctor in type.GetConstructors())
            {
                var ps = ctor.GetParameters();
                if (ps.Length != args.Length) continue;
                var ok = true;
                for (var i = 0; i < ps.Length; i++)
                {
                    if (args[i] != null && !ps[i].ParameterType.IsAssignableFrom(args[i].GetType()))
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) return ctor.Invoke(args);
            }

            throw new MissingMethodException("Constructor not found on " + type.FullName + ".");
        }

        private static void TrySet(object target, string property, object value)
        {
            if (target == null || value == null) return;
            var prop = target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite && prop.PropertyType.IsAssignableFrom(value.GetType()))
            {
                prop.SetValue(target, value, null);
            }
        }

        private static void SetReferencedAssembliesFolders(object options, Type optionsType)
        {
            var prop = optionsType.GetProperty("ReferencedAssembliesFolders", BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanWrite) return;
            var listType = typeof(List<>).MakeGenericType(typeof(string));
            var list = (IList)Activator.CreateInstance(listType);
            if (!string.IsNullOrWhiteSpace(MetadataBootstrap.BinPath)) list.Add(MetadataBootstrap.BinPath);
            if (!string.IsNullOrWhiteSpace(MetadataBootstrap.VsExtensionPath)) list.Add(MetadataBootstrap.VsExtensionPath);
            prop.SetValue(options, list, null);
        }

        private static object ReadEnabledRules(object service, string model)
        {
            var method = service.GetType().GetMethod("EnabledRules", new[] { typeof(string) });
            if (method == null) return Array.Empty<string>();
            try
            {
                return method.Invoke(service, new object[] { model }) ?? Array.Empty<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static object ParseTarget(string targetName)
        {
            var type = ResolveType("Microsoft.Dynamics.AX.Framework.BestPractices.Extensions.BestPracticeCheckerTargets");
            if (type == null) throw new InvalidOperationException("BestPracticeCheckerTargets was not found.");
            return Enum.Parse(type, targetName, true);
        }

        private static List<JsonObject> ReadDiagnostics(object sink)
        {
            var result = new List<JsonObject>();
            var method = sink.GetType().GetMethod("Diagnostics", Type.EmptyTypes);
            if (method == null) return result;
            if (!(method.Invoke(sink, null) is IEnumerable diagnostics)) return result;

            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic == null) continue;
                var item = new JsonObject
                {
                    ["moniker"] = ReadString(diagnostic, "Moniker"),
                    ["severity"] = ReadString(diagnostic, "Severity"),
                    ["diagnosticType"] = ReadString(diagnostic, "DiagnosticType"),
                    ["message"] = ReadString(diagnostic, "Message"),
                    ["path"] = ReadString(diagnostic, "Path"),
                    ["elementType"] = ReadString(diagnostic, "ElementType"),
                    ["ignored"] = ReadBool(diagnostic, "Ignored"),
                };

                var position = ReadObject(diagnostic, "Position");
                if (position != null)
                {
                    item["line"] = ReadInt(position, "StartLine");
                    item["column"] = ReadInt(position, "StartCol");
                    item["endLine"] = ReadInt(position, "EndLine");
                    item["endColumn"] = ReadInt(position, "EndCol");
                }

                result.Add(item);
            }

            return result;
        }

        private static JsonArray ToJsonArray(IEnumerable<JsonObject> diagnostics)
        {
            var array = new JsonArray();
            foreach (var diagnostic in diagnostics)
            {
                array.Add(diagnostic);
            }
            return array;
        }

        private static string InferModelFromPath(string file)
        {
            var full = Path.GetFullPath(file);
            var parts = full.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 1; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("Ax", StringComparison.OrdinalIgnoreCase))
                {
                    return parts[i - 1];
                }
            }
            return null;
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType(fullName, false);
                if (type != null) return type;
            }

            foreach (var dll in Directory.EnumerateFiles(MetadataBootstrap.VsExtensionPath ?? AppDomain.CurrentDomain.BaseDirectory, "*.dll"))
            {
                try
                {
                    var asm = Assembly.LoadFrom(dll);
                    var type = asm.GetType(fullName, false);
                    if (type != null) return type;
                }
                catch
                {
                    // Keep probing.
                }
            }

            return null;
        }

        private static object ReadObject(object target, string property)
        {
            var prop = target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
            return prop == null ? null : prop.GetValue(target, null);
        }

        private static string ReadString(object target, string property)
        {
            var value = ReadObject(target, property);
            return value == null ? null : value.ToString();
        }

        private static bool ReadBool(object target, string property)
        {
            var value = ReadObject(target, property);
            return value is bool b && b;
        }

        private static int? ReadInt(object target, string property)
        {
            var value = ReadObject(target, property);
            if (value == null) return null;
            try { return Convert.ToInt32(value); } catch { return null; }
        }

        private static bool IsError(string severity)
        {
            return string.Equals(severity, "Error", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(severity, "Fatal", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWarning(string severity)
        {
            return string.Equals(severity, "Warning", StringComparison.OrdinalIgnoreCase);
        }

        private static JsonObject Fail(string code, string message)
        {
            return new JsonObject
            {
                ["ok"] = false,
                ["error"] = code,
                ["message"] = message,
                ["source"] = "vs-extension-best-practice-framework",
            };
        }
    }
}
