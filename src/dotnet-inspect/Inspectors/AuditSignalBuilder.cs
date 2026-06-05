using System.Reflection.PortableExecutable;
using DotnetInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using NuGetFetch;

namespace DotnetInspector.Inspectors;

internal static class AuditSignalBuilder
{
    public static void PopulateLibraryAudit(string assemblyPath, LibraryInspection inspection, VerboseLogger logger)
    {
        List<AuditSignal> signals = [];

        var sourceLink = FormatSourceLink(inspection);
        Add(signals, "Provenance", "SourceLink", sourceLink.Value, sourceLink.Evidence);
        var sourceLinkAvailability = FormatSourceLinkAvailability(inspection);
        Add(signals, "Provenance", "SourceLink availability",
            sourceLinkAvailability.Value, sourceLinkAvailability.Evidence);
        Add(signals, "Provenance", "Deterministic", FormatBool(inspection.IsDeterministic),
            "PE debug directory and path normalization");

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

        inspection.AuditSignals = signals;
    }

    public static async Task PopulatePackageAuditAsync(
        InspectionResult result,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        List<AuditSignal> signals = [];

        var directDependencies = GetDirectDependenciesForLatestTfm(result);

        var supportedTfm = FormatSupportedTfm(result);
        Add(signals, "Compatibility", "Supported TFM", supportedTfm.Value, supportedTfm.Evidence);
        var portability = FormatPortability(result);
        Add(signals, "Compatibility", "Portable", portability.Value, portability.Evidence);
        Add(signals, "Documentation", "README", FormatBool(result.HasReadme), "nuspec/package files");
        Add(signals, "Legal", "License",
            string.IsNullOrWhiteSpace(result.License) ? "Not declared" : result.License, "nuspec metadata");

        if (result.BinarySignals is { TotalBinaries: > 0 } binarySignals)
        {
            Add(signals, "Provenance", "Symbols",
                FormatCoverage(binarySignals.SymbolsAvailable, binarySignals.TotalBinaries),
                FormatPdbSourceEvidence(binarySignals));
            Add(signals, "Provenance", "SourceLink",
                FormatCoverage(binarySignals.SourceLinkAvailable, binarySignals.TotalBinaries),
                FormatSourceLinkEvidence(binarySignals));
        }

        Add(signals, "Dependencies", "Direct dependencies",
            directDependencies.Dependencies.Count.ToString(), directDependencies.Evidence);

        if (result.Published is { Year: > 1901 } published)
            Add(signals, "NuGet", "Package age", FormatAge(DateTimeOffset.UtcNow - published), "NuGet registration");

        Add(signals, "NuGet", "Known vulnerabilities",
            FormatCount(result.Vulnerabilities?.Count ?? 0), "NuGet advisory data");

        var dependencySignals = await GetDependencySignalsAsync(directDependencies.Dependencies, httpClient, logger);
        Add(signals, "Dependencies", "Dependencies with vulnerabilities",
            dependencySignals.VulnerableDependencies.ToString(),
            FormatDependencyRegistryEvidence(dependencySignals));
        Add(signals, "Dependencies", "Deprecated dependencies",
            dependencySignals.DeprecatedDependencies.ToString(),
            FormatDependencyRegistryEvidence(dependencySignals));
        if (dependencySignals.AgeSummary != null)
        {
            var ageSummary = dependencySignals.AgeSummary;
            Add(signals, "Dependencies", "Dependency age",
                $"min {ageSummary.MinDays}d, median {ageSummary.MedianDays}d, max {ageSummary.MaxDays}d",
                FormatDependencyAgeEvidence(dependencySignals));
        }

        result.AuditSignals = signals;
    }

    private sealed record DependencySignalSummary(
        int DirectDependencies,
        int CheckedDependencies,
        int VulnerableDependencies,
        int DeprecatedDependencies,
        DependencyAgeSummary? AgeSummary);

    private static async Task<DependencySignalSummary> GetDependencySignalsAsync(
        List<PackageDependency> directDependencies,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        List<int> ages = [];
        int checkedDependencies = 0;
        int vulnerableDependencies = 0;
        int deprecatedDependencies = 0;
        foreach (var dep in directDependencies)
        {
            var version = DependencyResolutionService.ResolveVersionFromRange(dep.Version);
            if (version == null)
                continue;

            var metadata = await PackageMetadataService.FetchAllMetadataAsync(
                httpClient, dep.Id, version, logger.Log).ConfigureAwait(false);
            checkedDependencies++;

            if (metadata.Vulnerabilities is { Count: > 0 })
                vulnerableDependencies++;
            if (metadata.Deprecation != null)
                deprecatedDependencies++;
            if (metadata.Published is { Year: > 1901 } published)
                ages.Add(Math.Max(0, (int)Math.Round((DateTimeOffset.UtcNow - published).TotalDays)));
        }

        DependencyAgeSummary? ageSummary = null;
        if (ages.Count > 0)
        {
            ages.Sort();
            ageSummary = new DependencyAgeSummary(
                ages.Count,
                ages[0],
                ages[ages.Count / 2],
                ages[^1]);
        }

        return new DependencySignalSummary(
            directDependencies.Count,
            checkedDependencies,
            vulnerableDependencies,
            deprecatedDependencies,
            ageSummary);
    }

    private static void Add(List<AuditSignal> rows, string area, string signal, string value, string evidence)
        => rows.Add(new AuditSignal(area, signal, value, evidence));

    private static string FormatDependencyRegistryEvidence(DependencySignalSummary summary)
        => summary.DirectDependencies == 0
            ? "0 direct dependencies"
            : $"NuGet registry data for {summary.CheckedDependencies}/{summary.DirectDependencies} direct dependencies";

    private static string FormatDependencyAgeEvidence(DependencySignalSummary summary)
        => summary.AgeSummary is not { } ageSummary
            ? "no dependency published dates"
            : ageSummary.Count == summary.DirectDependencies
                ? $"{ageSummary.Count} direct dependencies"
                : $"{ageSummary.Count}/{summary.DirectDependencies} direct dependencies with published dates";

    private sealed record DirectDependencySelection(List<PackageDependency> Dependencies, string Evidence);

    private static DirectDependencySelection GetDirectDependenciesForLatestTfm(InspectionResult result)
    {
        if (result.DependencyGroups is not { Count: > 0 })
            return new([], "no dependency groups");

        var group = DependencyResolutionService.FindBestMatchingTfmGroup(
            result.DependencyGroups, result.Tfm ?? "");
        if (group == null)
            return new([], "no dependency group for latest TFM");

        return new(group.Dependencies, group.TargetFramework);
    }

    private static (string Value, string Evidence) FormatSupportedTfm(InspectionResult result)
    {
        if (result.TargetFrameworks is not { Count: > 0 } tfms)
            return ("No", "no lib/tools target framework assets");

        var orderedTfms = tfms
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(TfmResolver.GetTfmPriority)
            .ThenBy(tfm => tfm, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var highest = orderedTfms[0];
        return (FormatBool(IsSupportedTfm(highest)), string.Join(", ", orderedTfms));
    }

    private static bool IsSupportedTfm(string tfm)
    {
        if (TryParseTfmVersion(tfm, "netstandard") is { } netstandard)
            return netstandard >= new Version(2, 0);

        if (TryParseTfmVersion(tfm, "net") is { } modernNet)
            return modernNet >= new Version(8, 0);

        return false;
    }

    private static Version? TryParseTfmVersion(string tfm, string prefix)
    {
        if (!tfm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var suffix = tfm[prefix.Length..];
        var length = 0;
        while (length < suffix.Length && (char.IsDigit(suffix[length]) || suffix[length] == '.'))
            length++;

        if (length == 0)
            return null;

        var versionText = suffix[..length];
        // Old .NET Framework TFMs use forms like net462; modern .NET TFMs use net8.0.
        if (prefix == "net" && !versionText.Contains('.'))
            return null;

        return Version.TryParse(versionText, out var version) ? version : null;
    }

    private static string FormatCoverage(int available, int total)
        => available == total ? $"Yes ({available}/{total})"
            : available == 0 ? $"No (0/{total})"
            : $"Partial ({available}/{total})";

    private static string FormatPdbSourceEvidence(PackageBinarySignals signals)
        => FormatPdbSources(
            signals.EmbeddedPdbs,
            signals.InPackagePdbs,
            signals.SnupkgPdbs,
            signals.MsdlPdbs,
            signals.OtherPdbs,
            signals.SymbolsAvailable,
            "no PDBs available");

    private static string FormatSourceLinkEvidence(PackageBinarySignals signals)
    {
        if (signals.SourceLinkAvailable > 0)
        {
            return "SourceLink data in " + FormatPdbSources(
                signals.EmbeddedSourceLinkPdbs,
                signals.InPackageSourceLinkPdbs,
                signals.SnupkgSourceLinkPdbs,
                signals.MsdlSourceLinkPdbs,
                signals.OtherSourceLinkPdbs,
                signals.SourceLinkAvailable,
                "PDBs");
        }

        if (signals.SymbolsAvailable > 0)
            return "checked " + FormatPdbSourceEvidence(signals);

        return "no PDBs available";
    }

    private static string FormatPdbSources(
        int embedded,
        int inPackage,
        int snupkg,
        int msdl,
        int other,
        int total,
        string fallback)
    {
        List<string> parts = [];
        AddPdbSourcePart(parts, "embedded PDBs", embedded, total);
        AddPdbSourcePart(parts, "in-package PDBs", inPackage, total);
        AddPdbSourcePart(parts, ".snupkg PDBs", snupkg, total);
        AddPdbSourcePart(parts, "msdl.microsoft.com PDBs", msdl, total);
        AddPdbSourcePart(parts, "other symbol-server PDBs", other, total);
        return parts.Count == 0 ? fallback : string.Join(", ", parts);
    }

    private static void AddPdbSourcePart(List<string> parts, string label, int count, int total)
    {
        if (count <= 0)
            return;

        parts.Add(count == total ? label : $"{label} ({count})");
    }

    private static (string Value, string Evidence) FormatPortability(InspectionResult result)
    {
        if (!result.HasRidSpecificAssets)
            return ("Yes", "no RID-specific assets");

        bool hasFallback = result.IsFrameworkDependent
            || result.SupportedRids?.Contains("any", StringComparer.OrdinalIgnoreCase) == true
            || result.RuntimeIdentifierPackages?.Any(r =>
                r.RuntimeIdentifier.Equals("any", StringComparison.OrdinalIgnoreCase)) == true;

        return hasFallback
            ? ("Yes", "RID-specific assets with fallback")
            : ("No", "RID-specific assets without fallback");
    }

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

    private static (string Value, string Evidence) FormatSourceLinkAvailability(LibraryInspection inspection)
    {
        if (inspection.AllSourcesAccessible.HasValue || inspection.TotalSourceFiles > 0)
        {
            return (inspection.AllSourcesAccessible == true ? "Complete" : "Partial",
                $"{inspection.AccessibleSourceFiles}/{inspection.TotalSourceFiles} tracked source files available");
        }

        if (!inspection.HasSourceLink)
            return ("Not available", "SourceLink data not available");

        return ("Not checked", "SourceLink availability not selected");
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
