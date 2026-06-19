// <copyright file="MetadataBootstrap.cs" company="d365fo-cli contributors">
// MIT
// </copyright>

using System;
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;

namespace D365FO.Bridge
{
    /// <summary>
    /// Late-bound accessor for Microsoft's <c>IMetadataProvider</c> stack.
    /// Resolves the Microsoft.Dynamics.AX.Metadata.* assemblies from the
    /// configured <c>D365FO_BIN_PATH</c> (or <c>D365FO_PACKAGES_PATH\bin</c>)
    /// at runtime, so this net48 console exe can be built on a workstation
    /// without the D365FO assemblies available and only actually wire up to
    /// them on a real VM.
    /// </summary>
    internal static class MetadataBootstrap
    {
        private static readonly object _lock = new object();
        private static bool _resolverInstalled;
        private static string _binPath;
        private static string _packagesPath;
        private static string _vsExtensionPath;
        private static string[] _customPackagesPaths = new string[0];

        // Cached reflection artefacts per logical provider instance.
        private static object _provider; // IMetadataProvider
        private static string _lastError;

        internal static string BinPath { get { return _binPath; } }
        internal static string PackagesPath { get { return _packagesPath; } }
        internal static string VsExtensionPath { get { return _vsExtensionPath; } }
        internal static string LastError { get { return _lastError; } }
        internal static bool IsLoaded { get { return _provider != null; } }

        /// <summary>
        /// Ensure the assembly-resolve hook is installed and the metadata
        /// provider instantiated. Returns true on success, stores a descriptive
        /// error in <see cref="LastError"/> on failure. Safe to call repeatedly
        /// — only the first call pays the reflection cost.
        /// </summary>
        internal static bool TryInitialize()
        {
            if (_provider != null) return true;
            lock (_lock)
            {
                if (_provider != null) return true;
                _lastError = null;

                if (!TryInitializeAssemblyResolution())
                    return false;

                try
                {
                    var storagePath = FindAssemblyPath("Microsoft.Dynamics.AX.Metadata.Storage.dll");
                    if (string.IsNullOrEmpty(storagePath))
                    {
                        _lastError = "Microsoft.Dynamics.AX.Metadata.Storage.dll was not found in D365FO_BIN_PATH, D365FO_PACKAGES_PATH\\bin, or D365FO_VS_EXTENSION_PATH.";
                        return false;
                    }

                    var storage = Assembly.LoadFrom(storagePath);
                    _provider = CreateProvider(storage);
                    if (_provider == null)
                    {
                        _lastError = "MetadataProviderFactory did not return a provider.";
                        return false;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    var inner = ex is TargetInvocationException && ex.InnerException != null
                        ? " / " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message
                        : string.Empty;
                    _lastError = ex.GetType().Name + ": " + ex.Message + inner;
                    _provider = null;
                    return false;
                }
            }
        }

        /// <summary>
        /// Initialize only assembly probing, without constructing an
        /// <c>IMetadataProvider</c>. Used by render-only scaffolding where the
        /// bridge needs Microsoft's MetaModel types but not a model manifest.
        /// </summary>
        internal static bool TryInitializeAssemblyResolution()
        {
            lock (_lock)
            {
                _lastError = null;

                _vsExtensionPath = VsExtensionBootstrap.ResolveExtensionPath();
                _binPath = ResolveBinPath(out _packagesPath);
                _customPackagesPaths = ResolveCustomPackagesPaths();
                if ((string.IsNullOrEmpty(_binPath) || !Directory.Exists(_binPath)) &&
                    (string.IsNullOrEmpty(_vsExtensionPath) || !Directory.Exists(_vsExtensionPath)))
                {
                    _lastError = "D365FO_BIN_PATH (or D365FO_PACKAGES_PATH\\bin) is not set, and the D365FO VS extension was not found. Set D365FO_BIN_PATH or D365FO_VS_EXTENSION_PATH.";
                    return false;
                }

                if (!_resolverInstalled)
                {
                    AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
                    _resolverInstalled = true;
                }

                return true;
            }
        }

        /// <summary>Return the cached <c>IMetadataProvider</c> or null.</summary>
        internal static object GetProvider()
        {
            return TryInitialize() ? _provider : null;
        }

        /// <summary>
        /// Expose runtime diagnostics as a JSON object — consumed by the
        /// <c>ping</c> JSON-RPC method.
        /// </summary>
        internal static JsonObject Diagnostics()
        {
            TryInitialize();
            return new JsonObject
            {
                ["loaded"] = _provider != null,
                ["binPath"] = _binPath ?? string.Empty,
                ["packagesPath"] = _packagesPath ?? string.Empty,
                ["vsExtensionPath"] = _vsExtensionPath ?? string.Empty,
                ["customPackagesPaths"] = string.Join(";", _customPackagesPaths),
                ["error"] = _lastError ?? string.Empty,
            };
        }

        private static string ResolveBinPath(out string packagesPath)
        {
            packagesPath = Environment.GetEnvironmentVariable("D365FO_PACKAGES_PATH");
            var bin = Environment.GetEnvironmentVariable("D365FO_BIN_PATH");
            if (string.IsNullOrEmpty(bin) && !string.IsNullOrEmpty(packagesPath))
            {
                bin = Path.Combine(packagesPath, "bin");
            }
            if (string.IsNullOrEmpty(bin) && !string.IsNullOrEmpty(_vsExtensionPath))
            {
                bin = _vsExtensionPath;
            }
            return bin;
        }

        /// <summary>
        /// Additional custom-model roots forwarded by the CLI through
        /// <c>D365FO_CUSTOM_PACKAGES_PATH</c> (comma/semicolon separated). On a
        /// UDE these hold the user's custom models, which live outside the
        /// platform PackagesLocalDirectory — without adding them to the provider
        /// the model would be invisible to <c>--install-to</c>.
        /// </summary>
        private static string[] ResolveCustomPackagesPaths()
        {
            // D365FO_EXTRA_PACKAGES_PATH is the deprecated pre-rename name; honored
            // as a fallback so a standalone bridge daemon still picks up custom roots.
            var raw = Environment.GetEnvironmentVariable("D365FO_CUSTOM_PACKAGES_PATH");
            if (string.IsNullOrWhiteSpace(raw))
                raw = Environment.GetEnvironmentVariable("D365FO_EXTRA_PACKAGES_PATH");
            if (string.IsNullOrWhiteSpace(raw)) return new string[0];
            var parts = raw.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new System.Collections.Generic.List<string>(parts.Length);
            foreach (var p in parts)
            {
                var trimmed = p.Trim();
                if (trimmed.Length > 0) list.Add(trimmed);
            }
            return list.ToArray();
        }

        /// <summary>
        /// All metadata roots to register with the provider, in priority order:
        /// the primary <c>D365FO_PACKAGES_PATH</c> first, then each custom root.
        /// Empty entries and case-insensitive duplicates are skipped.
        /// </summary>
        private static System.Collections.Generic.IEnumerable<string> EnumerateMetadataRoots()
        {
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(_packagesPath) && seen.Add(_packagesPath))
            {
                yield return _packagesPath;
            }
            foreach (var custom in _customPackagesPaths)
            {
                if (!string.IsNullOrEmpty(custom) && seen.Add(custom))
                {
                    yield return custom;
                }
            }
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                var name = new AssemblyName(args.Name).Name + ".dll";
                var path = FindAssemblyPath(name);
                if (File.Exists(path))
                {
                    return Assembly.LoadFrom(path);
                }
            }
            catch
            {
                // swallow — the resolver returning null lets the CLR try other handlers.
            }
            return null;
        }

        internal static string FindAssemblyPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            foreach (var dir in EnumerateAssemblyProbePaths())
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    var path = Path.Combine(dir, fileName);
                    if (File.Exists(path)) return path;
                }
                catch
                {
                    // Ignore malformed paths and continue probing.
                }
            }
            return null;
        }

        private static System.Collections.Generic.IEnumerable<string> EnumerateAssemblyProbePaths()
        {
            if (!string.IsNullOrEmpty(_binPath)) yield return _binPath;
            if (!string.IsNullOrEmpty(_vsExtensionPath)) yield return _vsExtensionPath;
            foreach (var probe in VsExtensionBootstrap.GetAssemblyProbePaths())
            {
                yield return probe;
            }
            yield return AppDomain.CurrentDomain.BaseDirectory;
        }

        /// <summary>
        /// Late-bound equivalent of:
        /// <code>
        /// var config = new DiskProviderConfiguration();
        /// config.AddMetadataPath(packagesPath); // or AddXppMetadataFolder
        /// return new MetadataProviderFactory().CreateRuntimeProviderWithExtensions(config);
        /// </code>
        /// Falls back through a few factory method names to cope with minor
        /// API drift between D365FO platform updates.
        /// </summary>
        private static object CreateProvider(Assembly storage)
        {
            var factoryType = storage.GetType("Microsoft.Dynamics.AX.Metadata.Storage.MetadataProviderFactory");
            var configType = storage.GetType("Microsoft.Dynamics.AX.Metadata.Storage.DiskProvider.DiskProviderConfiguration");
            if (factoryType == null || configType == null)
            {
                throw new InvalidOperationException("MetadataProviderFactory / DiskProviderConfiguration types were not found in Microsoft.Dynamics.AX.Metadata.Storage.dll.");
            }

            var config = CreateConfig(configType);

            // Add every package root: the primary PackagesLocalDirectory plus
            // any custom-model roots (UDE dual-folder setups). All of them must
            // be registered with the provider, otherwise models that live only
            // under a custom root cannot be resolved by `--install-to`.
            var addMethod = configType.GetMethod("AddMetadataPath", new[] { typeof(string) });
            object metadataPathsList = null;
            if (addMethod == null)
            {
                var prop = configType.GetProperty("MetadataPaths");
                metadataPathsList = prop != null ? prop.GetValue(config) : null;
            }

            foreach (var root in EnumerateMetadataRoots())
            {
                if (addMethod != null)
                {
                    addMethod.Invoke(config, new object[] { root });
                }
                else if (metadataPathsList != null)
                {
                    var addItem = metadataPathsList.GetType().GetMethod("Add", new[] { typeof(string) });
                    if (addItem != null) addItem.Invoke(metadataPathsList, new object[] { root });
                }
            }

            var factory = Activator.CreateInstance(factoryType);

            // Preferred order: CreateRuntimeProviderWithExtensions (overlay +
            // extension merge, matches what Visual Studio shows), then
            // CreateRuntimeProvider, then CreateDiskProvider as last resort.
            foreach (var methodName in new[] { "CreateRuntimeProviderWithExtensions", "CreateRuntimeProvider", "CreateDiskProvider" })
            {
                var m = factoryType.GetMethod(methodName, new[] { configType });
                if (m != null)
                {
                    return m.Invoke(factory, new object[] { config });
                }
            }

            throw new MissingMethodException("MetadataProviderFactory has no known Create*Provider(DiskProviderConfiguration) method.");
        }

        /// <summary>
        /// Cope with both <c>DiskProviderConfiguration()</c> (older PUs) and
        /// <c>DiskProviderConfiguration(string packageRoot)</c> signatures.
        /// </summary>
        private static object CreateConfig(Type configType)
        {
            // Try parameterless first — it's the most stable shape across PUs.
            var noArg = configType.GetConstructor(Type.EmptyTypes);
            if (noArg != null) return noArg.Invoke(null);

            var stringCtor = configType.GetConstructor(new[] { typeof(string) });
            if (stringCtor != null) return stringCtor.Invoke(new object[] { _packagesPath ?? _binPath });

            // Fallback: first public ctor, filled with nulls/defaults.
            var anyCtor = configType.GetConstructors()[0];
            var args = new object[anyCtor.GetParameters().Length];
            for (int i = 0; i < args.Length; i++)
            {
                var pt = anyCtor.GetParameters()[i].ParameterType;
                args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
            }
            return anyCtor.Invoke(args);
        }

        /// <summary>
        /// Read a single <c>Ax*</c> artefact through the provider collection
        /// named by <paramref name="collectionName"/> (e.g. "Classes",
        /// "Tables"). Returns the Ax object or null when not found.
        /// </summary>
        internal static object ReadArtifact(string collectionName, string name)
        {
            var provider = GetProvider();
            if (provider == null) return null;
            var coll = provider.GetType().GetProperty(collectionName)?.GetValue(provider);
            if (coll == null) return null;
            var read = coll.GetType().GetMethod("Read", new[] { typeof(string) });
            if (read == null) return null;
            try
            {
                return read.Invoke(coll, new object[] { name });
            }
            catch (TargetInvocationException tex)
            {
                // Provider raises TypeLoadException / FileNotFoundException
                // for missing artefacts on some SKUs — treat as "not found".
                _lastError = tex.InnerException?.Message ?? tex.Message;
                return null;
            }
        }

        /// <summary>
        /// Kernel-level fallback for system enums that are compiled into the
        /// X++ runtime assemblies (e.g. <c>NoYes</c>, <c>Exists</c>, the
        /// <c>Microsoft.Dynamics.Ax.Xpp.*</c> types). We probe the typical
        /// Xpp support assemblies and, if the type is a CLR enum, materialise
        /// a synthetic JsonObject that mirrors the shape an AxEnum would
        /// produce — just enough for agent prompts.
        /// </summary>
        internal static JsonObject TryResolveKernelEnum(string name)
        {
            if (string.IsNullOrEmpty(_binPath)) return null;
            var candidates = new[]
            {
                "Microsoft.Dynamics.AX.Xpp.Support.dll",
                "Microsoft.Dynamics.AX.Xpp.AxShared.dll",
                "Microsoft.Dynamics.AX.Xpp.Redirect.dll",
            };
            foreach (var dll in candidates)
            {
                var path = Path.Combine(_binPath, dll);
                if (!File.Exists(path)) continue;
                Assembly asm;
                try { asm = Assembly.LoadFrom(path); }
                catch { continue; }

                // Try the XPP primary namespace and a generic scan as a
                // fallback — some enums live under nested namespaces.
                var t = asm.GetType("Microsoft.Dynamics.Ax.Xpp." + name, false)
                        ?? asm.GetType("Microsoft.Dynamics.AX.Xpp." + name, false);
                if (t == null)
                {
                    foreach (var et in asm.GetExportedTypes())
                    {
                        if (et.IsEnum && string.Equals(et.Name, name, StringComparison.Ordinal))
                        {
                            t = et;
                            break;
                        }
                    }
                }
                if (t == null || !t.IsEnum) continue;

                var values = new System.Text.Json.Nodes.JsonArray();
                foreach (var v in Enum.GetValues(t))
                {
                    values.Add(new System.Text.Json.Nodes.JsonObject
                    {
                        ["Name"]  = v.ToString(),
                        ["Value"] = Convert.ToInt32(v).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    });
                }
                return new System.Text.Json.Nodes.JsonObject
                {
                    ["Name"]             = name,
                    ["EnumValues"]       = values,
                    ["IsKernelEnum"]     = "True",
                    ["ClrType"]          = t.FullName,
                    ["ClrAssembly"]      = asm.GetName().Name,
                };
            }
            return null;
        }

        /// <summary>
        /// Collection metadata: the short provider-collection name ("Classes"),
        /// the matching MetaModel type name ("AxClass"), and the assembly that
        /// ships it (Microsoft.Dynamics.AX.Metadata.dll).
        /// </summary>
        internal static readonly System.Collections.Generic.Dictionary<string, string> KindToCollection =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "class", "Classes" },
                { "table", "Tables" },
                { "edt",   "Edts"    },
                { "enum",  "Enums"   },
                { "form",  "Forms"   },
                { "query", "Queries" },
                { "dataEntityView", "DataEntityViews" },
                { "tableExtension", "TableExtensions" },
                { "formExtension", "FormExtensions" },
                { "edtExtension", "EdtExtensions" },
                { "enumExtension", "EnumExtensions" },
                { "menuItemDisplay", "MenuItemDisplays" },
                { "menuItemAction", "MenuItemActions" },
                { "menuItemOutput", "MenuItemOutputs" },
                { "securityPrivilege", "SecurityPrivileges" },
                { "securityDuty", "SecurityDuties" },
                { "securityRole", "SecurityRoles" },
                { "service", "Services" },
                { "serviceGroup", "ServiceGroups" },
                { "workflowTemplate", "WorkflowTemplates" },
                { "workflowApproval", "WorkflowApprovals" },
                { "workflowTask", "WorkflowTasks" },
            };

        private static readonly System.Collections.Generic.Dictionary<string, string> KindToTypeName =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "class", "Microsoft.Dynamics.AX.Metadata.MetaModel.AxClass" },
                { "table", "Microsoft.Dynamics.AX.Metadata.MetaModel.AxTable" },
                { "edt",   "Microsoft.Dynamics.AX.Metadata.MetaModel.AxEdt"   },
                { "enum",  "Microsoft.Dynamics.AX.Metadata.MetaModel.AxEnum"  },
                { "form",  "Microsoft.Dynamics.AX.Metadata.MetaModel.AxForm"  },
                { "query", "Microsoft.Dynamics.AX.Metadata.MetaModel.AxQuery" },
                { "dataEntityView", "Microsoft.Dynamics.AX.Metadata.MetaModel.AxDataEntityView" },
                { "tableExtension", "Microsoft.Dynamics.AX.Metadata.MetaModel.AxTableExtension" },
                { "formExtension", "Microsoft.Dynamics.AX.Metadata.MetaModel.AxFormExtension" },
                { "edtExtension", "Microsoft.Dynamics.AX.Metadata.MetaModel.AxEdtExtension" },
                { "enumExtension", "Microsoft.Dynamics.AX.Metadata.MetaModel.AxEnumExtension" },
                { "menuItemDisplay", "Microsoft.Dynamics.AX.Metadata.MetaModel.AxMenuItemDisplay" },
                { "menuItemAction", "Microsoft.Dynamics.AX.Metadata.MetaModel.AxMenuItemAction" },
                { "menuItemOutput", "Microsoft.Dynamics.AX.Metadata.MetaModel.AxMenuItemOutput" },
                { "securityPrivilege", "Microsoft.Dynamics.AX.Metadata.MetaModel.AxSecurityPrivilege" },
                { "securityDuty", "Microsoft.Dynamics.AX.Metadata.MetaModel.AxSecurityDuty" },
                { "securityRole", "Microsoft.Dynamics.AX.Metadata.MetaModel.AxSecurityRole" },
                { "service", "Microsoft.Dynamics.AX.Metadata.MetaModel.AxService" },
                { "serviceGroup", "Microsoft.Dynamics.AX.Metadata.MetaModel.AxServiceGroup" },
                { "workflowTemplate", "Microsoft.Dynamics.AX.Metadata.MetaModel.AxWorkflowTemplate" },
                { "workflowApproval", "Microsoft.Dynamics.AX.Metadata.MetaModel.AxWorkflowApproval" },
                { "workflowTask", "Microsoft.Dynamics.AX.Metadata.MetaModel.AxWorkflowTask" },
            };

        private static readonly System.Collections.Generic.Dictionary<string, string> KindToSubfolder =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "class", "AxClass" },
                { "table", "AxTable" },
                { "edt", "AxEdt" },
                { "enum", "AxEnum" },
                { "form", "AxForm" },
                { "query", "AxQuery" },
                { "dataEntityView", "AxDataEntityView" },
                { "tableExtension", "AxTableExtension" },
                { "formExtension", "AxFormExtension" },
                { "edtExtension", "AxEdtExtension" },
                { "enumExtension", "AxEnumExtension" },
                { "menuItemDisplay", "AxMenuItemDisplay" },
                { "menuItemAction", "AxMenuItemAction" },
                { "menuItemOutput", "AxMenuItemOutput" },
                { "securityPrivilege", "AxSecurityPrivilege" },
                { "securityDuty", "AxSecurityDuty" },
                { "securityRole", "AxSecurityRole" },
                { "service", "AxService" },
                { "serviceGroup", "AxServiceGroup" },
                { "workflowTemplate", "AxWorkflowTemplate" },
                { "workflowApproval", "AxWorkflowApproval" },
                { "workflowTask", "AxWorkflowTask" },
            };

        private static readonly System.Collections.Generic.Dictionary<string, string> KindAliases =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "AxClass", "class" },
                { "class", "class" },
                { "AxTable", "table" },
                { "table", "table" },
                { "AxEdt", "edt" },
                { "edt", "edt" },
                { "AxEnum", "enum" },
                { "enum", "enum" },
                { "AxForm", "form" },
                { "form", "form" },
                { "AxQuery", "query" },
                { "query", "query" },
                { "AxDataEntityView", "dataEntityView" },
                { "dataEntityView", "dataEntityView" },
                { "data-entity-view", "dataEntityView" },
                { "entity", "dataEntityView" },
                { "data-entity", "dataEntityView" },
                { "AxTableExtension", "tableExtension" },
                { "tableExtension", "tableExtension" },
                { "table-extension", "tableExtension" },
                { "AxFormExtension", "formExtension" },
                { "formExtension", "formExtension" },
                { "form-extension", "formExtension" },
                { "AxEdtExtension", "edtExtension" },
                { "edtExtension", "edtExtension" },
                { "edt-extension", "edtExtension" },
                { "AxEnumExtension", "enumExtension" },
                { "enumExtension", "enumExtension" },
                { "enum-extension", "enumExtension" },
                { "AxMenuItemDisplay", "menuItemDisplay" },
                { "menuItemDisplay", "menuItemDisplay" },
                { "menu-item-display", "menuItemDisplay" },
                { "AxMenuItemAction", "menuItemAction" },
                { "menuItemAction", "menuItemAction" },
                { "menu-item-action", "menuItemAction" },
                { "AxMenuItemOutput", "menuItemOutput" },
                { "menuItemOutput", "menuItemOutput" },
                { "menu-item-output", "menuItemOutput" },
                { "AxSecurityPrivilege", "securityPrivilege" },
                { "securityPrivilege", "securityPrivilege" },
                { "security-privilege", "securityPrivilege" },
                { "AxSecurityDuty", "securityDuty" },
                { "securityDuty", "securityDuty" },
                { "security-duty", "securityDuty" },
                { "AxSecurityRole", "securityRole" },
                { "securityRole", "securityRole" },
                { "security-role", "securityRole" },
                { "AxService", "service" },
                { "service", "service" },
                { "AxServiceGroup", "serviceGroup" },
                { "serviceGroup", "serviceGroup" },
                { "service-group", "serviceGroup" },
                { "AxWorkflowTemplate", "workflowTemplate" },
                { "workflowTemplate", "workflowTemplate" },
                { "workflow-template", "workflowTemplate" },
                { "AxWorkflowApproval", "workflowApproval" },
                { "workflowApproval", "workflowApproval" },
                { "workflow-approval", "workflowApproval" },
                { "AxWorkflowTask", "workflowTask" },
                { "workflowTask", "workflowTask" },
                { "workflow-task", "workflowTask" },
            };

        internal static string NormalizeKind(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind)) return null;
            return KindAliases.TryGetValue(kind.Trim(), out var canonical) ? canonical : kind.Trim();
        }

        internal static string GetAxSubfolder(string kind)
        {
            var canonical = NormalizeKind(kind);
            return canonical != null && KindToSubfolder.TryGetValue(canonical, out var folder) ? folder : null;
        }

        /// <summary>
        /// Look up the <c>AxClass</c>/<c>AxTable</c>/... Type from the loaded
        /// Microsoft.Dynamics.AX.Metadata assembly, for use with reflection-based
        /// construction and XmlSerializer-based deserialization.
        /// </summary>
        internal static Type GetMetaModelType(string kind)
        {
            var canonical = NormalizeKind(kind);
            if (canonical == null || !KindToTypeName.TryGetValue(canonical, out var typeName)) return null;
            return ResolveMetaModelType(typeName);
        }

        /// <summary>
        /// Look up a concrete MetaModel type by its short class name (e.g.
        /// <c>AxEdtString</c>). Used when the input XML's root element pins
        /// the precise subtype — the abstract <c>AxEdt</c> base cannot be
        /// constructed or deserialized.
        /// </summary>
        internal static Type GetMetaModelTypeByShortName(string shortName)
        {
            if (string.IsNullOrWhiteSpace(shortName)) return null;
            return ResolveMetaModelType("Microsoft.Dynamics.AX.Metadata.MetaModel." + shortName);
        }

        private static Type ResolveMetaModelType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                var t = asm.GetType(typeName, false);
                if (t != null) return t;
            }
            // Force-load if not present yet.
            try
            {
                var metaPath = FindAssemblyPath("Microsoft.Dynamics.AX.Metadata.dll");
                if (string.IsNullOrEmpty(metaPath)) return null;
                var meta = Assembly.LoadFrom(metaPath);
                return meta.GetType(typeName, false);
            }
            catch { return null; }
        }

        /// <summary>
        /// Resolve the on-disk folder that owns <paramref name="modelName"/>
        /// according to <c>ModelManifest.GetFolderForModel</c>. Returns null
        /// and fills <paramref name="error"/> when the model is unknown.
        /// </summary>
        internal static string GetModelFolder(string modelName, out string error)
        {
            error = null;
            var info = ReadModelInfo(modelName);
            if (info == null) { error = "MODEL_NOT_FOUND"; return null; }
            var provider = GetProvider();
            var mm = provider?.GetType().GetProperty("ModelManifest")?.GetValue(provider);
            if (mm == null) { error = "MANIFEST_UNAVAILABLE"; return null; }
            var get = mm.GetType().GetMethod("GetFolderForModel", new[] { info.GetType().GetInterface("IModelReference") ?? info.GetType() });
            if (get == null)
            {
                // Try any overload.
                foreach (var m in mm.GetType().GetMethods())
                {
                    if (m.Name == "GetFolderForModel" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsAssignableFrom(info.GetType()))
                    {
                        get = m; break;
                    }
                }
            }
            if (get == null) { error = "GET_FOLDER_NOT_FOUND"; return null; }
            try
            {
                return (string)get.Invoke(mm, new object[] { info });
            }
            catch (TargetInvocationException tex)
            {
                error = tex.InnerException?.Message ?? tex.Message;
                return null;
            }
        }

        /// <summary>
        /// Load a <c>ModelInfo</c> for the given model name (as surfaced in
        /// <c>Descriptor\Model.xml</c>). Returns null when unknown.
        /// </summary>
        internal static object ReadModelInfo(string modelName)
        {
            var provider = GetProvider();
            if (provider == null) return null;
            var mm = provider.GetType().GetProperty("ModelManifest")?.GetValue(provider);
            if (mm == null) return null;
            var read = mm.GetType().GetMethod("Read", new[] { typeof(string) });
            if (read == null) return null;
            try { return read.Invoke(mm, new object[] { modelName }); }
            catch (TargetInvocationException tex)
            {
                _lastError = tex.InnerException?.Message ?? tex.Message;
                return null;
            }
        }

        /// <summary>
        /// Build a <c>ModelSaveInfo</c> from a <c>ModelInfo</c> — prefers the
        /// <c>.ctor(IModelReference)</c> shape, falls back to parameterless ctor
        /// plus property-copy.
        /// </summary>
        internal static object BuildModelSaveInfo(object modelInfo)
        {
            if (modelInfo == null) return null;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            Type saveInfoType = null;
            foreach (var asm in assemblies)
            {
                saveInfoType = asm.GetType("Microsoft.Dynamics.AX.Metadata.MetaModel.ModelSaveInfo", false);
                if (saveInfoType != null) break;
            }
            if (saveInfoType == null) return null;

            // ctor(IModelReference) — ModelInfo implements IModelReference.
            foreach (var ctor in saveInfoType.GetConstructors())
            {
                var ps = ctor.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType.IsAssignableFrom(modelInfo.GetType()))
                {
                    return ctor.Invoke(new object[] { modelInfo });
                }
            }

            // Fallback: parameterless + copy Name/Id/Layer/SequenceId from ModelInfo.
            var def = saveInfoType.GetConstructor(Type.EmptyTypes);
            if (def == null) return null;
            var si = def.Invoke(null);
            CopyProp(modelInfo, si, "Name");
            CopyProp(modelInfo, si, "Id");
            CopyProp(modelInfo, si, "Layer");
            CopyProp(modelInfo, si, "SequenceId");
            return si;
        }

        private static void CopyProp(object src, object dst, string name)
        {
            var sp = src.GetType().GetProperty(name);
            var dp = dst.GetType().GetProperty(name);
            if (sp == null || dp == null || !dp.CanWrite) return;
            try { dp.SetValue(dst, sp.GetValue(src)); } catch { }
        }

        /// <summary>
        /// Perform a write operation (<c>Create</c>, <c>Update</c>, or
        /// <c>Delete</c>) on the collection matching <paramref name="kind"/>.
        /// For Create/Update, the <c>AxClass</c>/<c>AxTable</c>/etc instance
        /// must be provided in <paramref name="axInstance"/>. For Delete, the
        /// name is passed in <paramref name="nameForDelete"/> and
        /// <paramref name="axInstance"/> is ignored.
        /// Returns (true, null) on success; (false, message) on failure.
        /// </summary>
        internal static (bool ok, string error) SaveArtifact(string kind, string op, object axInstance, string nameForDelete, object modelSaveInfo)
        {
            var canonical = NormalizeKind(kind);
            if (canonical == null || !KindToCollection.TryGetValue(canonical, out var collectionName))
            {
                return (false, "Unknown kind: " + kind);
            }
            var provider = GetProvider();
            if (provider == null) return (false, _lastError ?? "provider unavailable");
            var coll = provider.GetType().GetProperty(collectionName)?.GetValue(provider);
            if (coll == null) return (false, collectionName + " provider not exposed");

            MethodInfo m;
            object[] callArgs;
            if (string.Equals(op, "delete", StringComparison.OrdinalIgnoreCase))
            {
                m = FindCollectionMethod(coll.GetType(), "Delete", typeof(string), modelSaveInfo.GetType());
                callArgs = new[] { (object)nameForDelete, modelSaveInfo };
            }
            else
            {
                var methodName = string.Equals(op, "update", StringComparison.OrdinalIgnoreCase) ? "Update" : "Create";
                m = FindCollectionMethod(coll.GetType(), methodName, axInstance.GetType(), modelSaveInfo.GetType());
                callArgs = new[] { axInstance, modelSaveInfo };
            }
            if (m == null)
            {
                return (false, op + " method not found on " + collectionName);
            }
            try
            {
                m.Invoke(coll, callArgs);
                return (true, null);
            }
            catch (TargetInvocationException tex)
            {
                var inner = tex.InnerException ?? tex;
                return (false, inner.GetType().Name + ": " + inner.Message);
            }
            catch (Exception ex)
            {
                return (false, ex.GetType().Name + ": " + ex.Message);
            }
        }

        internal static bool ArtifactExists(string kind, string name)
        {
            var canonical = NormalizeKind(kind);
            if (canonical == null || !KindToCollection.TryGetValue(canonical, out var collectionName))
            {
                return false;
            }

            return ReadArtifact(collectionName, name) != null;
        }

        private static MethodInfo FindCollectionMethod(Type collectionType, string methodName, Type firstArg, Type secondArg)
        {
            if (collectionType == null || string.IsNullOrEmpty(methodName) || firstArg == null || secondArg == null)
            {
                return null;
            }

            foreach (var method in collectionType.GetMethods())
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                var parameters = method.GetParameters();
                if (parameters.Length != 2)
                {
                    continue;
                }

                if (parameters[0].ParameterType.IsAssignableFrom(firstArg) &&
                    parameters[1].ParameterType.IsAssignableFrom(secondArg))
                {
                    return method;
                }
            }

            return null;
        }

        /// <summary>
        /// Enumerate all names in a provider collection (e.g. "Classes").
        /// Returns an empty list on failure.
        /// </summary>
        internal static System.Collections.Generic.List<string> ListNames(string collectionName)
        {
            var result = new System.Collections.Generic.List<string>();
            var provider = GetProvider();
            if (provider == null) return result;
            var coll = provider.GetType().GetProperty(collectionName)?.GetValue(provider);
            if (coll == null) return result;
            var listNames = coll.GetType().GetMethod("ListObjectNames", Type.EmptyTypes)
                           ?? coll.GetType().GetMethod("ListNames", Type.EmptyTypes);
            if (listNames == null) return result;
            try
            {
                var enumerable = listNames.Invoke(coll, null) as System.Collections.IEnumerable;
                if (enumerable == null) return result;
                foreach (var s in enumerable) result.Add(s?.ToString());
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }
            return result;
        }
    }
}
