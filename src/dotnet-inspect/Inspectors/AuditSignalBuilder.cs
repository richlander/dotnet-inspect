using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using NuGetFetch;

namespace DotnetInspector.Inspectors;

internal static class AuditSignalBuilder
{
    private delegate SignalValue? SignalResolver<TContext>(in TContext context);

    private readonly record struct SignalValue(string Value, string Evidence);

    private readonly record struct SignalRow<TContext>(
        string Area,
        string Signal,
        SignalResolver<TContext> Resolve);

    private readonly record struct LibrarySignalContext(
        LibraryInspection Inspection,
        AssemblyAuditMetadata? Metadata,
        int? PInvokeMethodCount);

    private readonly record struct PackageSignalContext(
        InspectionResult Result,
        DirectDependencySelection DirectDependencies,
        DependencySignalSummary DependencySignals);

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

            // Reuse the same reader for the classified-methods scan rather than re-opening the file.
            if (inspection.UnsafeMethods == null || inspection.PInvokeMethods == null || inspection.AsyncMethods == null)
                LibraryMetadataService.ScanClassifiedMethods(peReader, assemblyPath, inspection, logger);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning audit metadata in {assemblyPath}: {ex.Message}");
        }

        var context = new LibrarySignalContext(inspection, metadata, pInvokeMethodCount);
        AddLibrarySignals(signals, in context);

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
        var context = new PackageSignalContext(result, directDependencies, dependencySignals);
        AddPackageSignals(signals, in context);

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

    private static void Add(List<AuditSignal> rows, string area, string signal, (string Value, string Evidence) value)
        => Add(rows, area, signal, value.Value, value.Evidence);

    private static void AddLibrarySignals(List<AuditSignal> rows, in LibrarySignalContext context)
    {
        ReadOnlySpan<SignalRow<LibrarySignalContext>> registry =
        [
            LibrarySignalRows.ProvenanceSourceLink,
            LibrarySignalRows.ProvenanceSourceLinkAvailability,
            LibrarySignalRows.ProvenanceDeterministic,
            LibrarySignalRows.DependenciesDirectAssemblyReferences,
            LibrarySignalRows.CompatibilityAsyncKind,
            LibrarySignalRows.DependenciesTransitiveAssemblyReferences,
            LibrarySignalRows.DependenciesMaxReferenceDepth,
            LibrarySignalRows.CompatibilityIsTrimmable,
            LibrarySignalRows.CompatibilityIsAotCompatible,
            LibrarySignalRows.CompatibilityRequiresUnreferencedCode,
            LibrarySignalRows.CompatibilityRequiresDynamicCode,
            LibrarySignalRows.CompatibilityRequiresAssemblyFiles,
            LibrarySignalRows.CompatibilityDynamicDependency,
            LibrarySignalRows.MemorySafetyModel,
            LibrarySignalRows.MemorySafetyRequiresUnsafeMembers,
            LibrarySignalRows.MemorySafetyDisableRuntimeMarshalling,
            LibrarySignalRows.MemorySafetyUnsafePublicSignatures,
            LibrarySignalRows.InteropPInvokeMethods
        ];

        AddSignals(rows, in context, registry);
    }

    private static void AddPackageSignals(List<AuditSignal> rows, in PackageSignalContext context)
    {
        ReadOnlySpan<SignalRow<PackageSignalContext>> registry =
        [
            PackageSignalRows.CompatibilitySupportedTfm,
            PackageSignalRows.CompatibilityPortable,
            PackageSignalRows.DocumentationReadme,
            PackageSignalRows.DocumentationAgentDocumentation,
            PackageSignalRows.LegalLicense,
            PackageSignalRows.ProvenanceSymbols,
            PackageSignalRows.ProvenanceSourceLink,
            PackageSignalRows.DependenciesDirectDependencies,
            PackageSignalRows.NuGetPackageAge,
            PackageSignalRows.NuGetKnownVulnerabilities,
            PackageSignalRows.DependenciesWithVulnerabilities,
            PackageSignalRows.DependenciesDeprecatedDependencies,
            PackageSignalRows.DependenciesDependencyAge
        ];

        AddSignals(rows, in context, registry);
    }

    private static void AddSignals<TContext>(
        List<AuditSignal> rows,
        in TContext context,
        ReadOnlySpan<SignalRow<TContext>> registry)
    {
        foreach (ref readonly var row in registry)
        {
            if (row.Resolve(in context) is { } value)
                Add(rows, row.Area, row.Signal, value.Value, value.Evidence);
        }
    }

    private static SignalValue ToSignalValue(this (string Value, string Evidence) value) =>
        new(value.Value, value.Evidence);

    private static class LibrarySignalRows
    {
        public static SignalRow<LibrarySignalContext> ProvenanceSourceLink =>
            new("Provenance", "SourceLink", ResolveProvenanceSourceLink);

        public static SignalRow<LibrarySignalContext> ProvenanceSourceLinkAvailability =>
            new("Provenance", "SourceLink availability", ResolveProvenanceSourceLinkAvailability);

        public static SignalRow<LibrarySignalContext> ProvenanceDeterministic =>
            new("Provenance", "Deterministic", ResolveProvenanceDeterministic);

        public static SignalRow<LibrarySignalContext> DependenciesDirectAssemblyReferences =>
            new("Dependencies", "Direct assembly references", ResolveDependenciesDirectAssemblyReferences);

        public static SignalRow<LibrarySignalContext> CompatibilityAsyncKind =>
            new("Compatibility", "Async Kind", ResolveCompatibilityAsyncKind);

        public static SignalRow<LibrarySignalContext> DependenciesTransitiveAssemblyReferences =>
            new("Dependencies", "Transitive assembly references", ResolveDependenciesTransitiveAssemblyReferences);

        public static SignalRow<LibrarySignalContext> DependenciesMaxReferenceDepth =>
            new("Dependencies", "Max reference depth", ResolveDependenciesMaxReferenceDepth);

        public static SignalRow<LibrarySignalContext> CompatibilityIsTrimmable =>
            new("Compatibility", "IsTrimmable", ResolveCompatibilityIsTrimmable);

        public static SignalRow<LibrarySignalContext> CompatibilityIsAotCompatible =>
            new("Compatibility", "IsAotCompatible", ResolveCompatibilityIsAotCompatible);

        public static SignalRow<LibrarySignalContext> CompatibilityRequiresUnreferencedCode =>
            new("Compatibility", "RequiresUnreferencedCode", ResolveCompatibilityRequiresUnreferencedCode);

        public static SignalRow<LibrarySignalContext> CompatibilityRequiresDynamicCode =>
            new("Compatibility", "RequiresDynamicCode", ResolveCompatibilityRequiresDynamicCode);

        public static SignalRow<LibrarySignalContext> CompatibilityRequiresAssemblyFiles =>
            new("Compatibility", "RequiresAssemblyFiles", ResolveCompatibilityRequiresAssemblyFiles);

        public static SignalRow<LibrarySignalContext> CompatibilityDynamicDependency =>
            new("Compatibility", "DynamicDependency", ResolveCompatibilityDynamicDependency);

        public static SignalRow<LibrarySignalContext> MemorySafetyModel =>
            new("Memory safety", "Memory safety model", ResolveMemorySafetyModel);

        public static SignalRow<LibrarySignalContext> MemorySafetyRequiresUnsafeMembers =>
            new("Memory safety", "RequiresUnsafe members", ResolveMemorySafetyRequiresUnsafeMembers);

        public static SignalRow<LibrarySignalContext> MemorySafetyDisableRuntimeMarshalling =>
            new("Memory safety", "Disable runtime marshalling", ResolveMemorySafetyDisableRuntimeMarshalling);

        public static SignalRow<LibrarySignalContext> MemorySafetyUnsafePublicSignatures =>
            new("Memory safety", "Unsafe public signatures", ResolveMemorySafetyUnsafePublicSignatures);

        public static SignalRow<LibrarySignalContext> InteropPInvokeMethods =>
            new("Interop", "P/Invoke methods", ResolveInteropPInvokeMethods);

        private static SignalValue? ResolveProvenanceSourceLink(in LibrarySignalContext context) =>
            FormatSourceLink(context.Inspection).ToSignalValue();

        private static SignalValue? ResolveProvenanceSourceLinkAvailability(in LibrarySignalContext context) =>
            FormatSourceLinkAvailability(context.Inspection).ToSignalValue();

        private static SignalValue? ResolveProvenanceDeterministic(in LibrarySignalContext context) =>
            new(FormatBool(context.Inspection.IsDeterministic), "PE debug directory and path normalization");

        private static SignalValue? ResolveDependenciesDirectAssemblyReferences(in LibrarySignalContext context) =>
            new((context.Inspection.AssemblyInfo?.References?.Count ?? 0).ToString(), "AssemblyRef table");

        private static SignalValue? ResolveCompatibilityAsyncKind(in LibrarySignalContext context) =>
            new(ResolveAsyncKind(context.Inspection), "public async method classification");

        private static SignalValue? ResolveDependenciesTransitiveAssemblyReferences(in LibrarySignalContext context) =>
            context.Inspection.AssemblyInfo?.TransitiveReferences is { Count: > 0 } transitive
                ? new SignalValue(
                    transitive.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString(),
                    "resolved assembly reference closure")
                : null;

        private static SignalValue? ResolveDependenciesMaxReferenceDepth(in LibrarySignalContext context) =>
            context.Inspection.AssemblyInfo?.TransitiveReferences is { Count: > 0 } transitive
                ? new SignalValue(transitive.Max(r => r.Depth + 1).ToString(), "resolved assembly reference closure")
                : null;

        private static SignalValue? ResolveCompatibilityIsTrimmable(in LibrarySignalContext context) =>
            context.Metadata == null
                ? null
                : new SignalValue(FormatNullableBool(context.Metadata.IsTrimmable), FormatAssemblyMetadataEvidence("IsTrimmable", context.Metadata.IsTrimmable));

        private static SignalValue? ResolveCompatibilityIsAotCompatible(in LibrarySignalContext context) =>
            context.Metadata == null
                ? null
                : new SignalValue(FormatNullableBool(context.Metadata.IsAotCompatible), FormatAssemblyMetadataEvidence("IsAotCompatible", context.Metadata.IsAotCompatible));

        private static SignalValue? ResolveCompatibilityRequiresUnreferencedCode(in LibrarySignalContext context) =>
            context.Metadata == null
                ? null
                : new SignalValue(FormatCount(context.Metadata.RequiresUnreferencedCodeCount), "RequiresUnreferencedCodeAttribute");

        private static SignalValue? ResolveCompatibilityRequiresDynamicCode(in LibrarySignalContext context) =>
            context.Metadata == null
                ? null
                : new SignalValue(FormatCount(context.Metadata.RequiresDynamicCodeCount), "RequiresDynamicCodeAttribute");

        private static SignalValue? ResolveCompatibilityRequiresAssemblyFiles(in LibrarySignalContext context) =>
            context.Metadata == null
                ? null
                : new SignalValue(FormatCount(context.Metadata.RequiresAssemblyFilesCount), "RequiresAssemblyFilesAttribute");

        private static SignalValue? ResolveCompatibilityDynamicDependency(in LibrarySignalContext context) =>
            context.Metadata == null
                ? null
                : new SignalValue(FormatCount(context.Metadata.DynamicDependencyCount), "DynamicDependencyAttribute");

        private static SignalValue? ResolveMemorySafetyModel(in LibrarySignalContext context) =>
            context.Metadata == null
                ? null
                : new SignalValue(FormatMemorySafetyModel(context.Metadata.MemorySafetyRulesVersion), "module MemorySafetyRulesAttribute");

        private static SignalValue? ResolveMemorySafetyRequiresUnsafeMembers(in LibrarySignalContext context) =>
            context.Metadata == null
                ? null
                : new SignalValue(FormatCount(context.Metadata.RequiresUnsafeCount), "RequiresUnsafeAttribute");

        private static SignalValue? ResolveMemorySafetyDisableRuntimeMarshalling(in LibrarySignalContext context) =>
            context.Metadata == null
                ? null
                : new SignalValue(FormatBool(context.Metadata.HasDisableRuntimeMarshalling), "DisableRuntimeMarshallingAttribute");

        private static SignalValue? ResolveMemorySafetyUnsafePublicSignatures(in LibrarySignalContext context) =>
            new(FormatCount(context.Inspection.UnsafeMethods?.Count ?? 0), "public pointer signatures");

        private static SignalValue? ResolveInteropPInvokeMethods(in LibrarySignalContext context) =>
            new(FormatCount(context.PInvokeMethodCount ?? context.Inspection.PInvokeMethods?.Count ?? 0), "all PInvokeImpl metadata");
    }

    private static class PackageSignalRows
    {
        public static SignalRow<PackageSignalContext> CompatibilitySupportedTfm =>
            new("Compatibility", "Supported TFM", ResolveCompatibilitySupportedTfm);

        public static SignalRow<PackageSignalContext> CompatibilityPortable =>
            new("Compatibility", "Portable", ResolveCompatibilityPortable);

        public static SignalRow<PackageSignalContext> DocumentationReadme =>
            new("Documentation", "README", ResolveDocumentationReadme);

        public static SignalRow<PackageSignalContext> DocumentationAgentDocumentation =>
            new("Documentation", "Agent documentation", ResolveDocumentationAgentDocumentation);

        public static SignalRow<PackageSignalContext> LegalLicense =>
            new("Legal", "License", ResolveLegalLicense);

        public static SignalRow<PackageSignalContext> ProvenanceSymbols =>
            new("Provenance", "Symbols", ResolveProvenanceSymbols);

        public static SignalRow<PackageSignalContext> ProvenanceSourceLink =>
            new("Provenance", "SourceLink", ResolveProvenanceSourceLink);

        public static SignalRow<PackageSignalContext> DependenciesDirectDependencies =>
            new("Dependencies", "Direct dependencies", ResolveDependenciesDirectDependencies);

        public static SignalRow<PackageSignalContext> NuGetPackageAge =>
            new("NuGet", "Package age", ResolveNuGetPackageAge);

        public static SignalRow<PackageSignalContext> NuGetKnownVulnerabilities =>
            new("NuGet", "Known vulnerabilities", ResolveNuGetKnownVulnerabilities);

        public static SignalRow<PackageSignalContext> DependenciesWithVulnerabilities =>
            new("Dependencies", "Dependencies with vulnerabilities", ResolveDependenciesWithVulnerabilities);

        public static SignalRow<PackageSignalContext> DependenciesDeprecatedDependencies =>
            new("Dependencies", "Deprecated dependencies", ResolveDependenciesDeprecatedDependencies);

        public static SignalRow<PackageSignalContext> DependenciesDependencyAge =>
            new("Dependencies", "Dependency age", ResolveDependenciesDependencyAge);

        private static SignalValue? ResolveCompatibilitySupportedTfm(in PackageSignalContext context) =>
            FormatSupportedTfm(context.Result).ToSignalValue();

        private static SignalValue? ResolveCompatibilityPortable(in PackageSignalContext context) =>
            FormatPortability(context.Result).ToSignalValue();

        private static SignalValue? ResolveDocumentationReadme(in PackageSignalContext context) =>
            new(FormatBool(context.Result.HasReadme), context.Result.PackageReadmeFile ?? "package files");

        private static SignalValue? ResolveDocumentationAgentDocumentation(in PackageSignalContext context) =>
            new(FormatBool(context.Result.HasAgentDocumentation), "AGENTS.md");

        private static SignalValue? ResolveLegalLicense(in PackageSignalContext context) =>
            new(string.IsNullOrWhiteSpace(context.Result.License) ? "Not declared" : context.Result.License, "nuspec metadata");

        private static SignalValue? ResolveProvenanceSymbols(in PackageSignalContext context) =>
            context.Result.BinarySignals is { TotalBinaries: > 0 } binarySignals
                ? new SignalValue(FormatCoverage(binarySignals.SymbolsAvailable, binarySignals.TotalBinaries), FormatPdbSourceEvidence(binarySignals))
                : null;

        private static SignalValue? ResolveProvenanceSourceLink(in PackageSignalContext context) =>
            context.Result.BinarySignals is { TotalBinaries: > 0 } binarySignals
                ? new SignalValue(FormatCoverage(binarySignals.SourceLinkAvailable, binarySignals.TotalBinaries), FormatSourceLinkEvidence(binarySignals))
                : null;

        private static SignalValue? ResolveDependenciesDirectDependencies(in PackageSignalContext context) =>
            new(context.DirectDependencies.Dependencies.Count.ToString(), context.DirectDependencies.Evidence);

        private static SignalValue? ResolveNuGetPackageAge(in PackageSignalContext context) =>
            context.Result.Published is { Year: > 1901 } published
                ? new SignalValue(FormatAge(DateTimeOffset.UtcNow - published), "NuGet registration")
                : null;

        private static SignalValue? ResolveNuGetKnownVulnerabilities(in PackageSignalContext context) =>
            new(FormatCount(context.Result.Vulnerabilities?.Count ?? 0), "NuGet advisory data");

        private static SignalValue? ResolveDependenciesWithVulnerabilities(in PackageSignalContext context) =>
            new(context.DependencySignals.VulnerableDependencies.ToString(), FormatDependencyRegistryEvidence(context.DependencySignals));

        private static SignalValue? ResolveDependenciesDeprecatedDependencies(in PackageSignalContext context) =>
            new(context.DependencySignals.DeprecatedDependencies.ToString(), FormatDependencyRegistryEvidence(context.DependencySignals));

        private static SignalValue? ResolveDependenciesDependencyAge(in PackageSignalContext context) =>
            context.DependencySignals.AgeSummary is { } ageSummary
                ? new SignalValue(
                    $"min {ageSummary.MinDays}d, median {ageSummary.MedianDays}d, max {ageSummary.MaxDays}d",
                    FormatDependencyAgeEvidence(context.DependencySignals))
                : null;
    }

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

        var orderedTfms = TfmSelector.OrderByTfmPriorityDescending(
                tfms.Distinct(StringComparer.OrdinalIgnoreCase),
                tfm => tfm)
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
            return ("Present", FormatPdbEvidence(inspection, hasSourceLink: true));
        }

        if (!string.IsNullOrWhiteSpace(inspection.SourceLinkUnavailableReason))
        {
            if (inspection.SourceLinkUnavailableReason == "PDB checked; no SourceLink data")
                return ("Not found", FormatPdbEvidence(inspection, hasSourceLink: false));

            return ("Not found", inspection.SourceLinkUnavailableReason);
        }

        if (!string.IsNullOrWhiteSpace(inspection.PdbFormat) || !string.IsNullOrWhiteSpace(inspection.PdbLocation))
            return ("Not found", FormatPdbEvidence(inspection, hasSourceLink: false));

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

    private static string FormatPdbEvidence(LibraryInspection inspection, bool hasSourceLink)
    {
        var source = !string.IsNullOrWhiteSpace(inspection.SymbolServer)
            ? inspection.SymbolServer
            : !string.IsNullOrWhiteSpace(inspection.PdbLocation)
                ? inspection.PdbLocation.ToLowerInvariant()
                : "unknown";

        return hasSourceLink
            ? $"SourceLink data found in PDB from {source}"
            : $"PDB found from {source}; no SourceLink data";
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
