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
    private sealed record SignalDescriptor<TContext>(
        string Area,
        string Signal,
        Func<TContext, SignalValue?> Resolve);

    private readonly record struct SignalValue(string Value, string Evidence);

    private sealed record LibrarySignalContext(
        LibraryInspection Inspection,
        AssemblyAuditMetadata? Metadata,
        int? PInvokeMethodCount);

    private sealed record PackageSignalContext(
        InspectionResult Result,
        DirectDependencySelection DirectDependencies,
        DependencySignalSummary DependencySignals);

    private static readonly SignalDescriptor<LibrarySignalContext>[] LibrarySignals =
    [
        new("Provenance", "SourceLink", ctx => FormatSourceLink(ctx.Inspection).ToSignalValue()),
        new("Provenance", "SourceLink availability", ctx => FormatSourceLinkAvailability(ctx.Inspection).ToSignalValue()),
        new("Provenance", "Deterministic", ctx => new(FormatBool(ctx.Inspection.IsDeterministic), "PE debug directory and path normalization")),
        new("Dependencies", "Direct assembly references", ctx => new((ctx.Inspection.AssemblyInfo?.References?.Count ?? 0).ToString(), "AssemblyRef table")),
        new("Compatibility", "Async Kind", ctx => new(ResolveAsyncKind(ctx.Inspection), "public async method classification")),
        new("Dependencies", "Transitive assembly references", ctx =>
            ctx.Inspection.AssemblyInfo?.TransitiveReferences is { Count: > 0 } transitive
                ? new SignalValue(
                    transitive.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString(),
                    "resolved assembly reference closure")
                : null),
        new("Dependencies", "Max reference depth", ctx =>
            ctx.Inspection.AssemblyInfo?.TransitiveReferences is { Count: > 0 } transitive
                ? new SignalValue(transitive.Max(r => r.Depth + 1).ToString(), "resolved assembly reference closure")
                : null),
        new("Compatibility", "IsTrimmable", ctx => ctx.Metadata == null
            ? null
            : new SignalValue(FormatNullableBool(ctx.Metadata.IsTrimmable), FormatAssemblyMetadataEvidence("IsTrimmable", ctx.Metadata.IsTrimmable))),
        new("Compatibility", "IsAotCompatible", ctx => ctx.Metadata == null
            ? null
            : new SignalValue(FormatNullableBool(ctx.Metadata.IsAotCompatible), FormatAssemblyMetadataEvidence("IsAotCompatible", ctx.Metadata.IsAotCompatible))),
        new("Compatibility", "RequiresUnreferencedCode", ctx => ctx.Metadata == null
            ? null
            : new SignalValue(FormatCount(ctx.Metadata.RequiresUnreferencedCodeCount), "RequiresUnreferencedCodeAttribute")),
        new("Compatibility", "RequiresDynamicCode", ctx => ctx.Metadata == null
            ? null
            : new SignalValue(FormatCount(ctx.Metadata.RequiresDynamicCodeCount), "RequiresDynamicCodeAttribute")),
        new("Compatibility", "RequiresAssemblyFiles", ctx => ctx.Metadata == null
            ? null
            : new SignalValue(FormatCount(ctx.Metadata.RequiresAssemblyFilesCount), "RequiresAssemblyFilesAttribute")),
        new("Compatibility", "DynamicDependency", ctx => ctx.Metadata == null
            ? null
            : new SignalValue(FormatCount(ctx.Metadata.DynamicDependencyCount), "DynamicDependencyAttribute")),
        new("Memory safety", "Memory safety model", ctx => ctx.Metadata == null
            ? null
            : new SignalValue(FormatMemorySafetyModel(ctx.Metadata.MemorySafetyRulesVersion), "module MemorySafetyRulesAttribute")),
        new("Memory safety", "RequiresUnsafe members", ctx => ctx.Metadata == null
            ? null
            : new SignalValue(FormatCount(ctx.Metadata.RequiresUnsafeCount), "RequiresUnsafeAttribute")),
        new("Memory safety", "Disable runtime marshalling", ctx => ctx.Metadata == null
            ? null
            : new SignalValue(FormatBool(ctx.Metadata.HasDisableRuntimeMarshalling), "DisableRuntimeMarshallingAttribute")),
        new("Memory safety", "Unsafe public signatures", ctx => new(FormatCount(ctx.Inspection.UnsafeMethods?.Count ?? 0), "public pointer signatures")),
        new("Interop", "P/Invoke methods", ctx => new(FormatCount(ctx.PInvokeMethodCount ?? ctx.Inspection.PInvokeMethods?.Count ?? 0), "all PInvokeImpl metadata")),
    ];

    private static readonly SignalDescriptor<PackageSignalContext>[] PackageSignals =
    [
        new("Compatibility", "Supported TFM", ctx => FormatSupportedTfm(ctx.Result).ToSignalValue()),
        new("Compatibility", "Portable", ctx => FormatPortability(ctx.Result).ToSignalValue()),
        new("Documentation", "README", ctx => new(FormatBool(ctx.Result.HasReadme), "nuspec/package files")),
        new("Legal", "License", ctx => new(string.IsNullOrWhiteSpace(ctx.Result.License) ? "Not declared" : ctx.Result.License, "nuspec metadata")),
        new("Provenance", "Symbols", ctx => ctx.Result.BinarySignals is { TotalBinaries: > 0 } binarySignals
            ? new SignalValue(FormatCoverage(binarySignals.SymbolsAvailable, binarySignals.TotalBinaries), FormatPdbSourceEvidence(binarySignals))
            : null),
        new("Provenance", "SourceLink", ctx => ctx.Result.BinarySignals is { TotalBinaries: > 0 } binarySignals
            ? new SignalValue(FormatCoverage(binarySignals.SourceLinkAvailable, binarySignals.TotalBinaries), FormatSourceLinkEvidence(binarySignals))
            : null),
        new("Dependencies", "Direct dependencies", ctx => new(ctx.DirectDependencies.Dependencies.Count.ToString(), ctx.DirectDependencies.Evidence)),
        new("NuGet", "Package age", ctx => ctx.Result.Published is { Year: > 1901 } published
            ? new SignalValue(FormatAge(DateTimeOffset.UtcNow - published), "NuGet registration")
            : null),
        new("NuGet", "Known vulnerabilities", ctx => new(FormatCount(ctx.Result.Vulnerabilities?.Count ?? 0), "NuGet advisory data")),
        new("Dependencies", "Dependencies with vulnerabilities", ctx => new(ctx.DependencySignals.VulnerableDependencies.ToString(), FormatDependencyRegistryEvidence(ctx.DependencySignals))),
        new("Dependencies", "Deprecated dependencies", ctx => new(ctx.DependencySignals.DeprecatedDependencies.ToString(), FormatDependencyRegistryEvidence(ctx.DependencySignals))),
        new("Dependencies", "Dependency age", ctx => ctx.DependencySignals.AgeSummary is { } ageSummary
            ? new SignalValue(
                $"min {ageSummary.MinDays}d, median {ageSummary.MedianDays}d, max {ageSummary.MaxDays}d",
                FormatDependencyAgeEvidence(ctx.DependencySignals))
            : null),
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

        AddSignals(signals, new LibrarySignalContext(inspection, metadata, pInvokeMethodCount), LibrarySignals);

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
        AddSignals(signals, new PackageSignalContext(result, directDependencies, dependencySignals), PackageSignals);

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

    private static void AddSignals<TContext>(
        List<AuditSignal> rows,
        TContext context,
        IEnumerable<SignalDescriptor<TContext>> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            if (descriptor.Resolve(context) is { } value)
                Add(rows, descriptor.Area, descriptor.Signal, value.Value, value.Evidence);
        }
    }

    private static SignalValue ToSignalValue(this (string Value, string Evidence) value) =>
        new(value.Value, value.Evidence);

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
