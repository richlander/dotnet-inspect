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
        ApiOptions? options = null)
    {
        var (allocations, opportunities) = AnalysisScopeFor(requestedSections);
        Func<Analysis.TypeRef, bool>? bodyTypeScope = null;
        if (type is not null && requestedSections is not null
            && !requestedSections.Contains(SectionNames.TopLeverage)
            && !requestedSections.Contains(SectionNames.PerformanceTriage))
        {
            bodyTypeScope = typeRef => SameType(typeRef, type);
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
        if (!string.Equals(typeRef.Namespace, type.Namespace ?? "", StringComparison.Ordinal))
            return false;

        if (type.MetadataName != null)
            return string.Equals(typeRef.Name, type.MetadataName, StringComparison.Ordinal);

        return string.Equals(typeRef.Name.Replace('+', '.'), type.Name, StringComparison.Ordinal);
    }

    internal static IReadOnlyList<MemberExceptionRegion> ResolveExceptionRegions(
        string assemblyPath,
        IEnumerable<ApiMember> members)
    {
        using var context = PdbContext.Open(assemblyPath);
        return members
            .Where(member => member.MetadataToken is not null)
            .SelectMany(member => context.ResolveExceptionRegions(member.MetadataToken!.Value, out _)
                .Select(region => new MemberExceptionRegion(member, region)))
            .ToList();
    }
}
