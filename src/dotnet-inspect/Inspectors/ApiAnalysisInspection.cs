using DotnetInspector.Options;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Owns API-command policy for resolving analysis references and opening method-body indexes.
/// Output formatters consume the resulting immutable analysis facts and do not acquire sessions.
/// </summary>
internal static class ApiAnalysisInspection
{
    internal sealed record MemberExceptionRegion(ApiMember Member, MethodExceptionRegionInfo Region);

    internal static AssemblyDependencyResolver CreateReferenceResolver(string assemblyPath, ApiOptions? options = null)
        => new(new AssemblyDependencyResolutionOptions(assemblyPath)
        {
            ProjectAssetsPath = options?.ProjectAssetsPath,
            TargetFramework = options?.Tfm,
            IncludeDepsJsonAssets = false,
            IncludeAspNetCoreSharedFramework = false,
            PreferImplementationAssemblies = true,
        });

    /// <summary>
    /// Maps requested CLI sections to the expensive Analysis phases they consume.
    /// </summary>
    internal static (bool IncludeAllocations, bool IncludeOpportunities) AnalysisScopeFor(
        IReadOnlyCollection<string>? requestedSections)
    {
        if (requestedSections is null)
            return (true, true);

        bool opportunities = requestedSections.Contains(SectionNames.PerformanceTriage);
        bool allocations = opportunities || requestedSections.Contains(SectionNames.AllocationFacts);
        return (allocations, opportunities);
    }

    /// <summary>
    /// Opens the command-scoped Analysis index used by type and library sections. The resolver is
    /// built from <paramref name="options"/> so this type-scoped index honors <c>--project</c>
    /// (project-assets) and <c>--tfm</c> reference resolution, consistent with the member-analysis
    /// path (<see cref="ApiMemberAnalysisInspection"/>); passing <see langword="null"/> falls back to
    /// bare-assembly resolution.
    /// </summary>
    internal static Analysis.LibraryBodyIndex OpenTypeAnalysisIndex(
        string assemblyPath,
        IReadOnlyCollection<string>? requestedSections = null,
        ApiType? type = null,
        ApiOptions? options = null,
        ResolvedAssemblyReference? sourceAssembly = null)
    {
        var (allocations, opportunities) = AnalysisScopeFor(requestedSections);
        Func<Analysis.TypeRef, bool>? bodyTypeScope = null;
        if (type is not null && requestedSections is not null
            && !requestedSections.Contains(SectionNames.TopLeverage)
            && !requestedSections.Contains(SectionNames.PerformanceTriage))
        {
            bodyTypeScope = typeRef => SameType(typeRef, type);
        }

        if (sourceAssembly is not null)
        {
            string sourcePath = sourceAssembly.Path ?? assemblyPath;
            AssemblyImageSnapshotResult result = AssemblyImageSnapshot.Open(
                sourceAssembly,
                length => length <= AssemblyImageSnapshot.DefaultMaxRetainedImageBytes,
                static _ => { });
            AssemblyImageSnapshot snapshot = result switch
            {
                AssemblyImageSnapshotResult.Ready ready => ready.Snapshot,
                AssemblyImageSnapshotResult.Rejected rejected =>
                    throw new InvalidOperationException(
                        $"Type Analysis acquisition failed for '{sourcePath}' ({rejected.Failure.Kind}): {rejected.Failure.Detail}"),
                _ => throw new InvalidOperationException("Unknown assembly snapshot result."),
            };
            var features = Analysis.LibraryBodyAnalysisFeatures.MethodEvidence;
            if (allocations)
                features |= Analysis.LibraryBodyAnalysisFeatures.Allocations;
            if (opportunities)
                features |= Analysis.LibraryBodyAnalysisFeatures.OptimizationOpportunities;
            return MethodBodyInspectionSession.OpenWithPrefetchedImage(
                sourcePath,
                snapshot.Content,
                features,
                CreateReferenceResolver(sourcePath, options),
                sourceAssembly,
                bodyTypeScope: bodyTypeScope).BodyIndex;
        }

        return MethodBodyInspectionSession.Open(
            assemblyPath,
            CreateReferenceResolver(assemblyPath, options),
            allocations,
            opportunities,
            bodyScope: null,
            bodyTypeScope: bodyTypeScope).BodyIndex;
    }

    internal static bool SameType(Analysis.TypeRef typeRef, ApiType type)
    {
        if (typeRef.Kind != Analysis.TypeRefKind.Definition)
            return false;

        if (typeRef.Resolution?.Type is { } referenceName
            && type.DefinitionName is { } definitionName)
        {
            return referenceName == definitionName;
        }

        if (!string.Equals(typeRef.Namespace, type.Namespace ?? "", StringComparison.Ordinal))
            return false;

        if (type.MetadataName != null)
            return string.Equals(typeRef.Name, type.MetadataName, StringComparison.Ordinal);

        return string.Equals(typeRef.Name.Replace('+', '.'), type.Name, StringComparison.Ordinal);
    }

    internal static IReadOnlyList<MemberExceptionRegion> ResolveExceptionRegions(
        string assemblyPath,
        IEnumerable<ApiMember> members,
        ResolvedAssemblyReference? sourceAssembly = null)
    {
        using var context = sourceAssembly is null
            ? PdbContext.Open(assemblyPath)
            : PdbContext.Open(sourceAssembly);
        return members
            .Where(member => member.MetadataToken is not null)
            .SelectMany(member => context.ResolveExceptionRegions(member.MetadataToken!.Value, out _)
                .Select(region => new MemberExceptionRegion(member, region)))
            .ToList();
    }
}
