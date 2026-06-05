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
    private readonly record struct LibrarySignalContext(
        LibraryInspection Inspection,
        AssemblyAuditMetadata? Metadata,
        int? PInvokeMethodCount);

    private readonly record struct PackageSignalContext(
        InspectionResult Result,
        DirectDependencySelection DirectDependencies,
        DependencySignalSummary DependencySignals);

    private enum LibrarySignal
    {
        SourceLink,
        SourceLinkAvailability,
        Deterministic,
        DirectAssemblyReferences,
        AsyncKind,
        TransitiveAssemblyReferences,
        MaxReferenceDepth,
        IsTrimmable,
        IsAotCompatible,
        RequiresUnreferencedCode,
        RequiresDynamicCode,
        RequiresAssemblyFiles,
        DynamicDependency,
        MemorySafetyModel,
        RequiresUnsafeMembers,
        DisableRuntimeMarshalling,
        UnsafePublicSignatures,
        PInvokeMethods
    }

    private enum PackageSignal
    {
        SupportedTfm,
        Portable,
        Readme,
        License,
        Symbols,
        SourceLink,
        DirectDependencies,
        PackageAge,
        KnownVulnerabilities,
        DependenciesWithVulnerabilities,
        DeprecatedDependencies,
        DependencyAge
    }

    private static ReadOnlySpan<LibrarySignal> LibrarySignals =>
    [
        LibrarySignal.SourceLink,
        LibrarySignal.SourceLinkAvailability,
        LibrarySignal.Deterministic,
        LibrarySignal.DirectAssemblyReferences,
        LibrarySignal.AsyncKind,
        LibrarySignal.TransitiveAssemblyReferences,
        LibrarySignal.MaxReferenceDepth,
        LibrarySignal.IsTrimmable,
        LibrarySignal.IsAotCompatible,
        LibrarySignal.RequiresUnreferencedCode,
        LibrarySignal.RequiresDynamicCode,
        LibrarySignal.RequiresAssemblyFiles,
        LibrarySignal.DynamicDependency,
        LibrarySignal.MemorySafetyModel,
        LibrarySignal.RequiresUnsafeMembers,
        LibrarySignal.DisableRuntimeMarshalling,
        LibrarySignal.UnsafePublicSignatures,
        LibrarySignal.PInvokeMethods
    ];

    private static ReadOnlySpan<PackageSignal> PackageSignals =>
    [
        PackageSignal.SupportedTfm,
        PackageSignal.Portable,
        PackageSignal.Readme,
        PackageSignal.License,
        PackageSignal.Symbols,
        PackageSignal.SourceLink,
        PackageSignal.DirectDependencies,
        PackageSignal.PackageAge,
        PackageSignal.KnownVulnerabilities,
        PackageSignal.DependenciesWithVulnerabilities,
        PackageSignal.DeprecatedDependencies,
        PackageSignal.DependencyAge
    ];

    public static void PopulateLibraryAudit(string assemblyPath, LibraryInspection inspection, VerboseLogger logger)
    {
        List<AuditSignal> signals = [];
        AssemblyAuditMetadata? metadata = null;
        int? pInvokeMethodCount = null;

        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            metadata = AssemblyDetailScanner.ScanAuditMetadata(peReader);
            pInvokeMethodCount = metadata.PInvokeMethodCount;
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning audit metadata in {assemblyPath}: {ex.Message}");
        }

        if (inspection.UnsafeMethods == null || inspection.PInvokeMethods == null || inspection.AsyncMethods == null)
            LibraryMetadataService.ScanClassifiedMethods(assemblyPath, inspection, logger);

        AddSignals(signals, new LibrarySignalContext(inspection, metadata, pInvokeMethodCount));

        inspection.AuditSignals = signals;
    }

    public static async Task PopulatePackageAuditAsync(
        InspectionResult result,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        List<AuditSignal> signals = [];

        var directDependencies = GetDirectDependenciesForLatestTfm(result);
        var dependencySignals = await GetDependencySignalsAsync(directDependencies.Dependencies, httpClient, logger);
        AddSignals(signals, new PackageSignalContext(result, directDependencies, dependencySignals));

        result.AuditSignals = signals;
    }

    private readonly record struct DependencySignalSummary(
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

    private static void AddSignals(List<AuditSignal> rows, in LibrarySignalContext context)
    {
        foreach (var signal in LibrarySignals)
            AddLibrarySignal(rows, signal, context);
    }

    private static void AddSignals(List<AuditSignal> rows, in PackageSignalContext context)
    {
        foreach (var signal in PackageSignals)
            AddPackageSignal(rows, signal, context);
    }

    private static void AddLibrarySignal(List<AuditSignal> rows, LibrarySignal signal, in LibrarySignalContext context)
    {
        var inspection = context.Inspection;
        var metadata = context.Metadata;

        switch (signal)
        {
            case LibrarySignal.SourceLink:
                Add(rows, "Provenance", "SourceLink", FormatSourceLink(inspection));
                break;
            case LibrarySignal.SourceLinkAvailability:
                Add(rows, "Provenance", "SourceLink availability", FormatSourceLinkAvailability(inspection));
                break;
            case LibrarySignal.Deterministic:
                Add(rows, "Provenance", "Deterministic", FormatBool(inspection.IsDeterministic), "PE debug directory and path normalization");
                break;
            case LibrarySignal.DirectAssemblyReferences:
                Add(rows, "Dependencies", "Direct assembly references", (inspection.AssemblyInfo?.References?.Count ?? 0).ToString(), "AssemblyRef table");
                break;
            case LibrarySignal.AsyncKind:
                Add(rows, "Compatibility", "Async Kind", ResolveAsyncKind(inspection), "public async method classification");
                break;
            case LibrarySignal.TransitiveAssemblyReferences:
                if (inspection.AssemblyInfo?.TransitiveReferences is { Count: > 0 } transitiveRefs)
                {
                    Add(rows, "Dependencies", "Transitive assembly references",
                        transitiveRefs.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString(),
                        "resolved assembly reference closure");
                }
                break;
            case LibrarySignal.MaxReferenceDepth:
                if (inspection.AssemblyInfo?.TransitiveReferences is { Count: > 0 } transitiveDepth)
                    Add(rows, "Dependencies", "Max reference depth", transitiveDepth.Max(r => r.Depth + 1).ToString(), "resolved assembly reference closure");
                break;
            case LibrarySignal.IsTrimmable:
                if (metadata != null)
                    Add(rows, "Compatibility", "IsTrimmable", FormatNullableBool(metadata.IsTrimmable), FormatAssemblyMetadataEvidence("IsTrimmable", metadata.IsTrimmable));
                break;
            case LibrarySignal.IsAotCompatible:
                if (metadata != null)
                    Add(rows, "Compatibility", "IsAotCompatible", FormatNullableBool(metadata.IsAotCompatible), FormatAssemblyMetadataEvidence("IsAotCompatible", metadata.IsAotCompatible));
                break;
            case LibrarySignal.RequiresUnreferencedCode:
                if (metadata != null)
                    Add(rows, "Compatibility", "RequiresUnreferencedCode", FormatCount(metadata.RequiresUnreferencedCodeCount), "RequiresUnreferencedCodeAttribute");
                break;
            case LibrarySignal.RequiresDynamicCode:
                if (metadata != null)
                    Add(rows, "Compatibility", "RequiresDynamicCode", FormatCount(metadata.RequiresDynamicCodeCount), "RequiresDynamicCodeAttribute");
                break;
            case LibrarySignal.RequiresAssemblyFiles:
                if (metadata != null)
                    Add(rows, "Compatibility", "RequiresAssemblyFiles", FormatCount(metadata.RequiresAssemblyFilesCount), "RequiresAssemblyFilesAttribute");
                break;
            case LibrarySignal.DynamicDependency:
                if (metadata != null)
                    Add(rows, "Compatibility", "DynamicDependency", FormatCount(metadata.DynamicDependencyCount), "DynamicDependencyAttribute");
                break;
            case LibrarySignal.MemorySafetyModel:
                if (metadata != null)
                    Add(rows, "Memory safety", "Memory safety model", FormatMemorySafetyModel(metadata.MemorySafetyRulesVersion), "module MemorySafetyRulesAttribute");
                break;
            case LibrarySignal.RequiresUnsafeMembers:
                if (metadata != null)
                    Add(rows, "Memory safety", "RequiresUnsafe members", FormatCount(metadata.RequiresUnsafeCount), "RequiresUnsafeAttribute");
                break;
            case LibrarySignal.DisableRuntimeMarshalling:
                if (metadata != null)
                    Add(rows, "Memory safety", "Disable runtime marshalling", FormatBool(metadata.HasDisableRuntimeMarshalling), "DisableRuntimeMarshallingAttribute");
                break;
            case LibrarySignal.UnsafePublicSignatures:
                Add(rows, "Memory safety", "Unsafe public signatures", FormatCount(inspection.UnsafeMethods?.Count ?? 0), "public pointer signatures");
                break;
            case LibrarySignal.PInvokeMethods:
                Add(rows, "Interop", "P/Invoke methods", FormatCount(context.PInvokeMethodCount ?? inspection.PInvokeMethods?.Count ?? 0), "all PInvokeImpl metadata");
                break;
        }
    }

    private static void AddPackageSignal(List<AuditSignal> rows, PackageSignal signal, in PackageSignalContext context)
    {
        var result = context.Result;

        switch (signal)
        {
            case PackageSignal.SupportedTfm:
                Add(rows, "Compatibility", "Supported TFM", FormatSupportedTfm(result));
                break;
            case PackageSignal.Portable:
                Add(rows, "Compatibility", "Portable", FormatPortability(result));
                break;
            case PackageSignal.Readme:
                Add(rows, "Documentation", "README", FormatBool(result.HasReadme), "nuspec/package files");
                break;
            case PackageSignal.License:
                Add(rows, "Legal", "License", string.IsNullOrWhiteSpace(result.License) ? "Not declared" : result.License, "nuspec metadata");
                break;
            case PackageSignal.Symbols:
                if (result.BinarySignals is { TotalBinaries: > 0 } symbolSignals)
                    Add(rows, "Provenance", "Symbols", FormatCoverage(symbolSignals.SymbolsAvailable, symbolSignals.TotalBinaries), FormatPdbSourceEvidence(symbolSignals));
                break;
            case PackageSignal.SourceLink:
                if (result.BinarySignals is { TotalBinaries: > 0 } sourceLinkSignals)
                    Add(rows, "Provenance", "SourceLink", FormatCoverage(sourceLinkSignals.SourceLinkAvailable, sourceLinkSignals.TotalBinaries), FormatSourceLinkEvidence(sourceLinkSignals));
                break;
            case PackageSignal.DirectDependencies:
                Add(rows, "Dependencies", "Direct dependencies", context.DirectDependencies.Dependencies.Count.ToString(), context.DirectDependencies.Evidence);
                break;
            case PackageSignal.PackageAge:
                if (result.Published is { Year: > 1901 } published)
                    Add(rows, "NuGet", "Package age", FormatAge(DateTimeOffset.UtcNow - published), "NuGet registration");
                break;
            case PackageSignal.KnownVulnerabilities:
                Add(rows, "NuGet", "Known vulnerabilities", FormatCount(result.Vulnerabilities?.Count ?? 0), "NuGet advisory data");
                break;
            case PackageSignal.DependenciesWithVulnerabilities:
                Add(rows, "Dependencies", "Dependencies with vulnerabilities", context.DependencySignals.VulnerableDependencies.ToString(), FormatDependencyRegistryEvidence(context.DependencySignals));
                break;
            case PackageSignal.DeprecatedDependencies:
                Add(rows, "Dependencies", "Deprecated dependencies", context.DependencySignals.DeprecatedDependencies.ToString(), FormatDependencyRegistryEvidence(context.DependencySignals));
                break;
            case PackageSignal.DependencyAge:
                if (context.DependencySignals.AgeSummary is { } ageSummary)
                {
                    Add(rows, "Dependencies", "Dependency age",
                        $"min {ageSummary.MinDays}d, median {ageSummary.MedianDays}d, max {ageSummary.MaxDays}d",
                        FormatDependencyAgeEvidence(context.DependencySignals));
                }
                break;
        }
    }

    private static void Add(List<AuditSignal> rows, string area, string signal, (string Value, string Evidence) value)
        => Add(rows, area, signal, value.Value, value.Evidence);

    private static string ResolveAsyncKind(LibraryInspection inspection) =>
        (inspection.HasRuntimeAsync, inspection.HasStateMachineAsync) switch
        {
            (true, true) => "Mixed",
            (true, false) => AsyncMethodSummary.RuntimeKind,
            (false, true) => AsyncMethodSummary.StateMachineKind,
            _ => "None",
        };

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

    private readonly record struct DirectDependencySelection(List<PackageDependency> Dependencies, string Evidence);

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
            return "SourceLink data found in " + FormatPdbSources(
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
        AddPdbSourcePart(parts, "PDBs from package", inPackage, total);
        AddPdbSourcePart(parts, "PDBs from .snupkg", snupkg, total);
        AddPdbSourcePart(parts, "PDBs from msdl.microsoft.com", msdl, total);
        AddPdbSourcePart(parts, "PDBs from other symbol servers", other, total);
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

        return $"SourceLink data found in PDB from {source}";
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
