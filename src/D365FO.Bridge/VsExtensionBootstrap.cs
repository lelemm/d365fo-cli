// <copyright file="VsExtensionBootstrap.cs" company="d365fo-cli contributors">
// MIT
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace D365FO.Bridge
{
    /// <summary>
    /// Discovers the installed Finance and Operations Visual Studio extension.
    /// The VS extension folder name is cache-generated, so discovery is based
    /// on the VSIX manifest identity instead of a hard-coded directory.
    /// </summary>
    internal static class VsExtensionBootstrap
    {
        private const string ExtensionIdentity = "DynamicsFnO.DeveloperTools";
        private static readonly object _lock = new object();
        private static bool _resolved;
        private static string _extensionPath;
        private static string _lastError;

        internal static string ExtensionPath
        {
            get
            {
                EnsureResolved();
                return _extensionPath;
            }
        }

        internal static string LastError
        {
            get
            {
                EnsureResolved();
                return _lastError;
            }
        }

        internal static string ResolveExtensionPath()
        {
            EnsureResolved();
            return _extensionPath;
        }

        internal static IEnumerable<string> GetAssemblyProbePaths()
        {
            var extension = ResolveExtensionPath();
            if (!string.IsNullOrWhiteSpace(extension) && Directory.Exists(extension))
            {
                yield return extension;
            }

            foreach (var vsRoot in EnumerateVisualStudioRoots())
            {
                var ide = Path.Combine(vsRoot, "Common7", "IDE");
                var publicAssemblies = Path.Combine(ide, "PublicAssemblies");
                var privateAssemblies = Path.Combine(ide, "PrivateAssemblies");
                if (Directory.Exists(publicAssemblies)) yield return publicAssemblies;
                if (Directory.Exists(privateAssemblies)) yield return privateAssemblies;
                if (Directory.Exists(ide)) yield return ide;
            }
        }

        private static void EnsureResolved()
        {
            if (_resolved) return;
            lock (_lock)
            {
                if (_resolved) return;
                _lastError = null;

                var explicitPath = Environment.GetEnvironmentVariable("D365FO_VS_EXTENSION_PATH");
                if (!string.IsNullOrWhiteSpace(explicitPath))
                {
                    var full = Path.GetFullPath(explicitPath);
                    if (Directory.Exists(full))
                    {
                        _extensionPath = full;
                        _resolved = true;
                        return;
                    }

                    _lastError = "D365FO_VS_EXTENSION_PATH does not exist: " + full;
                    _resolved = true;
                    return;
                }

                foreach (var extensionsRoot in EnumerateExtensionRoots())
                {
                    if (!Directory.Exists(extensionsRoot)) continue;
                    string[] manifests;
                    try
                    {
                        manifests = Directory.GetFiles(extensionsRoot, "extension.vsixmanifest", SearchOption.AllDirectories);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var manifest in manifests)
                    {
                        if (IsDynamicsExtensionManifest(manifest))
                        {
                            _extensionPath = Path.GetDirectoryName(manifest);
                            _resolved = true;
                            return;
                        }
                    }
                }

                _lastError = "DynamicsFnO.DeveloperTools VS extension was not found. Set D365FO_VS_EXTENSION_PATH to the extension directory.";
                _resolved = true;
            }
        }

        private static IEnumerable<string> EnumerateExtensionRoots()
        {
            foreach (var vsRoot in EnumerateVisualStudioRoots())
            {
                yield return Path.Combine(vsRoot, "Common7", "IDE", "Extensions");
            }
        }

        private static IEnumerable<string> EnumerateVisualStudioRoots()
        {
            var roots = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Visual Studio", "2022", "Professional"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Visual Studio", "2022", "Enterprise"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Visual Studio", "2022", "Community"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "2019", "Professional"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "2019", "Enterprise"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "2019", "Community"),
            };

            foreach (var root in roots)
            {
                if (Directory.Exists(root)) yield return root;
            }
        }

        private static bool IsDynamicsExtensionManifest(string manifestPath)
        {
            try
            {
                var doc = XDocument.Load(manifestPath);
                var identity = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Identity");
                var id = identity != null ? (string)identity.Attribute("Id") : null;
                return string.Equals(id, ExtensionIdentity, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
