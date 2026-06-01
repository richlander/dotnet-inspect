using System.Reflection.PortableExecutable;
using DotnetInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

internal static class AuditSignalBuilder
{
    public static void PopulateLibraryAudit(string assemblyPath, LibraryInspection inspection, VerboseLogger logger)
    {
        List<AuditSignal> signals = [];

        var sourceLink = FormatSourceLink(inspection);
        Add(signals, "Provenance", "SourceLink", sourceLink.Value, sourceLink.Evidence);
        Add(signals, "Provenance", "Deterministic", FormatBool(inspection.IsDeterministic),
            "PE debug directory and path normalization");
        Add(signals, "Provenance", "Public key token", inspection.AssemblyInfo?.PublicKeyToken ?? "None",
            "assembly identity");

        Add(signals, "Dependencies", "Direct assembly references",
            (inspection.AssemblyInfo?.References?.Count ?? 0).ToString(),
            "AssemblyRef table");

        int? pInvokeMethodCount = null;

        if (inspection.AssemblyInfo?.TransitiveReferences is { Count: > 0 } transitive)
        {
            Add(signals, "Dependencies", "Transitive assembly references",
                transitive.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString(),
                "resolved assembly reference closure");
            Add(signals, "Dependencies", "Max reference depth",
                transitive.Count == 0 ? "0" : transitive.Max(r => r.Depth + 1).ToString(),
                "resolved assembly reference closure");
        }

        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            var metadata = AssemblyDetailScanner.ScanAuditMetadata(peReader);
            pInvokeMethodCount = metadata.PInvokeMethodCount;

            Add(signals, "Compatibility", "IsTrimmable", FormatNullableBool(metadata.IsTrimmable),
                FormatAssemblyMetadataEvidence("IsTrimmable", metadata.IsTrimmable));
            Add(signals, "Compatibility", "IsAotCompatible", FormatNullableBool(metadata.IsAotCompatible),
                FormatAssemblyMetadataEvidence("IsAotCompatible", metadata.IsAotCompatible));
            Add(signals, "Compatibility", "RequiresUnreferencedCode", FormatCount(metadata.RequiresUnreferencedCodeCount),
                "RequiresUnreferencedCodeAttribute");
            Add(signals, "Compatibility", "RequiresDynamicCode", FormatCount(metadata.RequiresDynamicCodeCount),
                "RequiresDynamicCodeAttribute");
            Add(signals, "Compatibility", "RequiresAssemblyFiles", FormatCount(metadata.RequiresAssemblyFilesCount),
                "RequiresAssemblyFilesAttribute");
            Add(signals, "Compatibility", "DynamicDependency", FormatCount(metadata.DynamicDependencyCount),
                "DynamicDependencyAttribute");

            Add(signals, "Memory safety", "Memory safety model",
                FormatMemorySafetyModel(metadata.MemorySafetyRulesVersion),
                "module MemorySafetyRulesAttribute");
            Add(signals, "Memory safety", "RequiresUnsafe members",
                FormatCount(metadata.RequiresUnsafeCount),
                "RequiresUnsafeAttribute");
            Add(signals, "Memory safety", "Disable runtime marshalling",
                FormatBool(metadata.HasDisableRuntimeMarshalling),
                "DisableRuntimeMarshallingAttribute");
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning audit metadata in {assemblyPath}: {ex.Message}");
        }

        if (inspection.UnsafeMethods == null || inspection.PInvokeMethods == null || inspection.AsyncMethods == null)
            LibraryMetadataService.ScanClassifiedMethods(assemblyPath, inspection, logger);

        Add(signals, "Memory safety", "Unsafe public signatures",
            FormatCount(inspection.UnsafeMethods?.Count ?? 0), "public pointer signatures");
        Add(signals, "Interop", "P/Invoke methods",
            FormatCount(pInvokeMethodCount ?? inspection.PInvokeMethods?.Count ?? 0), "all PInvokeImpl metadata");

        if (inspection.AllSourcesAccessible.HasValue || inspection.TotalSourceFiles > 0)
        {
            Add(signals, "Source audit", "Source coverage", inspection.AllSourcesAccessible == true ? "Complete" : "Partial",
                $"{inspection.AccessibleSourceFiles}/{inspection.TotalSourceFiles} tracked source files accessible or embedded");
        }

        Add(signals, "Audit", "Scope",
            FormatLibraryAuditScope(inspection),
            "not a security or trust assessment");

        inspection.AuditSignals = signals;
    }

    public static async Task PopulatePackageAuditAsync(
        InspectionResult result,
        bool includeNuGetAudit,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        List<AuditSignal> signals = [];

        var selectedGroup = result.DependencyGroups is { Count: > 0 }
            ? DependencyResolutionService.FindBestMatchingTfmGroup(result.DependencyGroups, result.Tfm ?? "")
            : null;
        var directDependencies = selectedGroup?.Dependencies ?? [];

        Add(signals, "Package", "Target frameworks",
            (result.TargetFrameworks?.Count ?? 0).ToString(), "package lib/tools assets");
        Add(signals, "Package", "Assemblies",
            result.AssemblyCount.ToString(), "package DLL assets");
        Add(signals, "Package", "RID-specific assets",
            FormatBool(result.HasRidSpecificAssets), "runtimes/ assets");
        Add(signals, "Package", "Native dependencies",
            FormatBool(result.HasNativeDependencies), "native/runtimes assets");
        Add(signals, "Package", "Readme", FormatBool(result.HasReadme), "nuspec/package files");
        Add(signals, "Package", "License",
            string.IsNullOrWhiteSpace(result.License) ? "Not declared" : result.License, "nuspec metadata");
        Add(signals, "Package", "Repository",
            string.IsNullOrWhiteSpace(result.Repository) ? "Not declared" : result.Repository, "nuspec metadata");

        Add(signals, "Dependencies", "Dependency groups",
            (result.DependencyGroups?.Count ?? 0).ToString(), "nuspec dependency groups");
        Add(signals, "Dependencies", "Direct dependencies",
            directDependencies.Count.ToString(), selectedGroup?.TargetFramework ?? result.Tfm ?? "selected TFM");

        if (result.SignatureResult is { } sig)
        {
            Add(signals, "Provenance", "Package signature",
                result.Signed == true ? "Signed" : sig.IsUnsigned ? "Unsigned" : "Unknown",
                sig.StatusMessage ?? "NuGet package signature");
        }

        if (includeNuGetAudit)
        {
            if (result.Published is { Year: > 1901 } published)
                Add(signals, "NuGet", "Package age", FormatAge(DateTimeOffset.UtcNow - published), "NuGet registration");

            Add(signals, "NuGet", "Known vulnerabilities",
                FormatCount(result.Vulnerabilities?.Count ?? 0), "NuGet advisory data");

            var transitive = await ResolveTransitiveDependenciesAsync(directDependencies, result.Tfm, httpClient, logger);
            if (transitive is { Count: > 0 })
            {
                var closure = Flatten(transitive).ToList();
                Add(signals, "Dependencies", "Resolved dependency closure",
                    closure.Select(n => n.PackageId).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString(),
                    "resolved NuGet dependency closure");
                Add(signals, "Dependencies", "Max dependency depth",
                    GetMaxDepth(transitive).ToString(), "resolved NuGet dependency closure");
            }

            var ageSummary = await GetDependencyAgeSummaryAsync(directDependencies, httpClient, logger);
            if (ageSummary != null)
            {
                Add(signals, "NuGet", "Direct dependency age",
                    $"min {ageSummary.MinDays}d, median {ageSummary.MedianDays}d, max {ageSummary.MaxDays}d",
                    $"{ageSummary.Count} direct dependencies with published dates");
            }
        }

        Add(signals, "Audit", "Scope",
            includeNuGetAudit ? "Metadata + NuGet registry signals" : "Metadata signals only",
            "not a security or trust assessment");

        result.AuditSignals = signals;
    }

    private static async Task<List<DependencyNode>?> ResolveTransitiveDependenciesAsync(
        List<PackageDependency> directDependencies,
        string? tfm,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        if (directDependencies.Count == 0 || string.IsNullOrWhiteSpace(tfm))
            return null;

        try
        {
            return await DependencyResolutionService.ResolveDependencyTreeAsync(
                httpClient, directDependencies, tfm, [], logger.Log).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error resolving dependency audit: {ex.Message}");
            return null;
        }
    }

    private static async Task<DependencyAgeSummary?> GetDependencyAgeSummaryAsync(
        List<PackageDependency> directDependencies,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        List<int> ages = [];
        foreach (var dep in directDependencies)
        {
            var version = DependencyResolutionService.ResolveVersionFromRange(dep.Version);
            if (version == null)
                continue;

            var published = await PackageMetadataService.GetPublishedDateAsync(
                httpClient, dep.Id, version, logger.Log).ConfigureAwait(false);
            if (published is not { Year: > 1901 })
                continue;

            ages.Add(Math.Max(0, (int)Math.Round((DateTimeOffset.UtcNow - published.Value).TotalDays)));
        }

        if (ages.Count == 0)
            return null;

        ages.Sort();
        return new DependencyAgeSummary(
            ages.Count,
            ages[0],
            ages[ages.Count / 2],
            ages[^1]);
    }

    private static int GetMaxDepth(List<DependencyNode> nodes)
        => nodes.Count == 0 ? 0 : nodes.Max(n => 1 + GetMaxDepth(n.Children));

    private static IEnumerable<DependencyNode> Flatten(IEnumerable<DependencyNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
                yield return child;
        }
    }

    private static void Add(List<AuditSignal> rows, string area, string signal, string value, string evidence)
        => rows.Add(new AuditSignal(area, signal, value, evidence));

    private static (string Value, string Evidence) FormatSourceLink(LibraryInspection inspection)
    {
        if (inspection.HasSourceLink)
        {
            return ("Present", FormatPdbEvidence(inspection));
        }

        if (!string.IsNullOrWhiteSpace(inspection.SourceLinkUnavailableReason))
        {
            if (inspection.SourceLinkUnavailableReason == "PDB checked; no SourceLink data")
                return ("Not found", FormatPdbEvidence(inspection));

            return ("Not found", inspection.SourceLinkUnavailableReason);
        }

        if (!string.IsNullOrWhiteSpace(inspection.PdbFormat) || !string.IsNullOrWhiteSpace(inspection.PdbLocation))
            return ("Not found", FormatPdbEvidence(inspection));

        return ("Not checked", "PDB not checked");
    }

    private static string FormatLibraryAuditScope(LibraryInspection inspection)
    {
        if (inspection.TotalSourceFiles > 0)
            return "Metadata + SourceLink verification signals";

        if (inspection.HasSourceLink ||
            !string.IsNullOrWhiteSpace(inspection.PdbFormat) ||
            !string.IsNullOrWhiteSpace(inspection.PdbLocation) ||
            !string.IsNullOrWhiteSpace(inspection.SymbolServer) ||
            inspection.WindowsPdbDetected)
        {
            return "Metadata + symbol signals";
        }

        return "Metadata signals only";
    }

    private static string FormatPdbEvidence(LibraryInspection inspection)
    {
        var source = !string.IsNullOrWhiteSpace(inspection.SymbolServer)
            ? inspection.SymbolServer
            : !string.IsNullOrWhiteSpace(inspection.PdbLocation)
                ? inspection.PdbLocation.ToLowerInvariant()
                : "unknown";

        return $"PDB (source: {source})";
    }

    private static string FormatBool(bool value) => value ? "Yes" : "No";

    private static string FormatMemorySafetyModel(int? version) => version switch
    {
        >= 2 => $"Updated (v{version})",
        { } marked => $"Marked v{marked}",
        null => "Not marked"
    };

    private static string FormatNullableBool(bool? value) => value switch
    {
        true => "Yes",
        false => "No",
        null => "Not marked"
    };

    private static string FormatAssemblyMetadataEvidence(string key, bool? value) =>
        value is null
            ? $"AssemblyMetadata key \"{key}\" not found"
            : $"AssemblyMetadata(\"{key}\", \"{value}\")";

    private static string FormatCount(int count) => count.ToString();

    private static string FormatAge(TimeSpan age)
    {
        var days = Math.Max(0, (int)Math.Round(age.TotalDays));
        return days < 365 ? $"{days}d" : $"{days / 365.0:F1}y";
    }
}
