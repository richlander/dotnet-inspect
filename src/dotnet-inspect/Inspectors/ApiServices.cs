using DotnetInspector.Packages;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

/// <summary>
/// API surface extraction: finding types, extracting full APIs, resolving type forwarders.
/// Delegates enrichment (PDB, source, docs) to <see cref="SourceEnricher"/>.
/// </summary>
internal static class ApiServices
{
    // ===== Extraction Pipeline =====

    internal sealed record LoadedApiSurface(
        ApiSurface Api,
        string ApiDllPath,
        string PdbLookupPath);

    internal static LoadedApiSurface? LoadFullApi(
        string searchPath,
        string? runtimeAssemblyPath,
        string? packagePath,
        string? packageName,
        string? apiSource,
        string? apiVersion,
        string? selectedTfm,
        VerboseLogger logger,
        bool includeAll)
    {
        var (api, apiDllPath) = ExtractFullApi(searchPath, logger, includeAll);
        if (api == null || apiDllPath == null)
            return null;

        ResolveForwardedTypes(api, apiDllPath, logger, includeAll);

        if (!string.IsNullOrEmpty(packagePath))
        {
            var (parsedPackageName, _) = PackageExtractor.ParsePackageReference(packagePath);
            api.Name = packageName ?? parsedPackageName;
        }
        else
        {
            api.Name = Path.GetFileNameWithoutExtension(apiDllPath);
        }

        api.Tfm = selectedTfm;
        api.Source = apiSource;
        api.Version = apiVersion;
        api.Library = Path.GetFileName(apiDllPath);

        return new LoadedApiSurface(api, apiDllPath, runtimeAssemblyPath ?? apiDllPath);
    }

    // ===== Type Lookup =====

    internal static (ApiType? type, string? assembly, string? dllPath, ApiSurface? surface) FindType(string typeName, string searchPath, VerboseLogger logger, bool includeAll)
    {
        string[] dllFiles;
        if (File.Exists(searchPath) && searchPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            dllFiles = [searchPath];
        }
        else if (Directory.Exists(searchPath))
        {
            dllFiles = Directory.GetFiles(searchPath, "*.dll", SearchOption.AllDirectories);
        }
        else
        {
            return (null, null, null, null);
        }

        foreach (var dllFile in dllFiles)
        {
            var api = AssemblyReader.ExtractApiSurface(dllFile, includeAll);
            if (api == null)
                continue;

            var match = api.Types.FirstOrDefault(t => TypeMatcher.Matches(t.FullName, typeName));

            if (match != null)
            {
                logger.Log($"Found in: {Path.GetFileName(dllFile)}");
                return (match, Path.GetFileName(dllFile), dllFile, api);
            }
        }

        return (null, null, null, null);
    }

    // ===== Full API Extraction =====

    internal static (ApiSurface? api, string? dllPath) ExtractFullApi(string searchPath, VerboseLogger logger, bool includeAll)
    {
        string? dllFile;
        if (File.Exists(searchPath) && searchPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            dllFile = searchPath;
        }
        else if (Directory.Exists(searchPath))
        {
            // Check ref/ (ref packages) then lib/
            string? contentDir = null;
            foreach (var subdir in new[] { "ref", "lib" })
            {
                var candidate = Path.Combine(searchPath, subdir);
                if (Directory.Exists(candidate))
                {
                    contentDir = candidate;
                    break;
                }
            }

            if (contentDir != null)
            {
                var dlls = Directory.GetFiles(contentDir, "*.dll", SearchOption.AllDirectories).ToList();
                var (selectedPath, selectedTfm) = TfmSelector.SelectHighestTfmAssembly(dlls, searchPath);
                dllFile = selectedPath;
                if (selectedTfm != null)
                {
                    logger.Log($"Auto-selected TFM: {selectedTfm}");
                }
            }
            else
            {
                var dlls = Directory.GetFiles(searchPath, "*.dll", SearchOption.AllDirectories).ToList();
                var (selectedPath, _) = TfmSelector.SelectHighestTfmAssembly(dlls, searchPath);
                dllFile = selectedPath ?? dlls.FirstOrDefault();
            }
        }
        else
        {
            return (null, null);
        }

        if (dllFile == null)
        {
            return (null, null);
        }

        logger.Log($"Extracting API from: {Path.GetFileName(dllFile)}");

        var api = AssemblyReader.ExtractApiSurface(dllFile, includeAll);
        return (api, api != null ? dllFile : null);
    }

    // ===== Type Forwarder Resolution =====

    /// <summary>
    /// Resolves types from forwarded assemblies and merges them into the API surface.
    /// Like curl -L, this follows type forwarders to their target assemblies.
    /// </summary>
    internal static void ResolveForwardedTypes(ApiSurface api, string dllPath, VerboseLogger logger, bool includeAll)
    {
        if (api.TypeForwarders.Count == 0)
            return;

        var assemblyDir = Path.GetDirectoryName(dllPath);
        if (assemblyDir == null)
            return;

        var byAssembly = api.TypeForwarders
            .GroupBy(f => f.TargetAssembly)
            .ToDictionary(g => g.Key, g => g.Select(f => f.TypeName).ToHashSet(StringComparer.OrdinalIgnoreCase));

        logger.Log($"Resolving {api.TypeForwarders.Count} forwarded types from {byAssembly.Count} libraries...");

        int resolvedCount = 0;

        foreach (var (targetAssembly, forwardedTypeNames) in byAssembly)
        {
            // The forwarder target is an AssemblyRef name read straight out of the inspected
            // assembly's metadata, so it is attacker-controlled, and it is about to be joined
            // onto the assembly's directory and opened. Path.Combine(root, untrustedValue) is
            // not a containment check (docs/design/untrusted-data-threat-model.md, "Derived
            // paths"), so refuse unsafe names rather than sanitize them.
            if (!HardenedPath.IsSafePathComponent(targetAssembly))
            {
                logger.Log($"Warning: refusing to resolve forwarded types to library with unsafe assembly name: '{targetAssembly}'");
                continue;
            }

            var targetPath = Path.Combine(assemblyDir, targetAssembly + ".dll");
            if (!File.Exists(targetPath))
            {
                logger.Log($"Target library '{targetAssembly}' not found, skipping.");
                continue;
            }

            try
            {
                var targetApi = AssemblyReader.ExtractApiSurface(targetPath, includeAll);
                if (targetApi == null)
                    continue;

                foreach (var type in targetApi.Types)
                {
                    if (forwardedTypeNames.Contains(type.FullName))
                    {
                        type.IsForwarded = true;
                        type.SourceAssemblyPath = targetPath;
                        api.Types.Add(type);
                        api.PublicMethodCount += type.Members.Count(DotnetInspector.Sections.ApiMemberSectionDescriptors.IsMethodLike);
                        api.PublicPropertyCount += type.Members.Count(m => m.Kind == "property");
                        api.PublicEventCount += type.Members.Count(m => m.Kind == "event");
                        api.PublicFieldCount += type.Members.Count(m => m.Kind == "field");
                        resolvedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Log($"Error reading '{targetAssembly}': {ex.Message}");
            }
        }

        if (resolvedCount > 0)
        {
            api.IsTypeForwardingAssembly = PlatformResolver.IsFacadeOnlyAssembly(dllPath);
            api.PublicTypeCount = api.Types.Count;
            api.Types = api.Types.OrderBy(t => t.FullName).ToList();
            logger.Log($"Resolved {resolvedCount} types from forwarded libraries.");
        }
    }
}
