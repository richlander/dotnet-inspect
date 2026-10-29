using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Diff;

/// <summary>
/// The one analysis-set input builder for every pair-request subject
/// (diff-system.md#single-library-api-diff-terminal). The endpoint source
/// supplies the API surfaces, and the host its body readers; the operation
/// owns target resolution, when each reader runs, and which analyses are
/// unavailable.
/// </summary>
internal static class DiffAnalysisInputBuilder
{
    /// <summary>
    /// Builds the input for request targets already resolved by
    /// <see cref="DiffAnalysisTargets"/>. It runs the host's implementation
    /// comparison, which is a request precondition, before any producer.
    /// </summary>
    internal static DiffAnalysisInput Create(
        AnalysisSetValidationResult.Accepted selection,
        DiffAnalysisApiSource api,
        IReadOnlyList<string> fromPaths,
        IReadOnlyList<string> toPaths,
        DiffAnalysisTargets targets,
        StringLiteralComparisonQueryPlan? stringLiteralQuery,
        DiffPairContent content,
        Func<StringLiteralComparisonQueryPlan, RetainedFindingComparisonSet>?
            sourceStringLiterals)
    {
        DiffBodyContent? bodies = content.Bodies;
        ImplementationDiffResult? implementation = null;
        if (DiffAnalysisCatalog.RequiresImplementation(selection)
            && bodies?.CompareImplementation is { } compareImplementation)
        {
            try
            {
                implementation = compareImplementation();
            }
            catch (InvalidOperationException ex)
                when (ex is not DiffAnalysisTargetException)
            {
                throw new DiffAnalysisTargetException(ex.Message);
            }
        }

        // The host's body reader serves string literals when it supplies
        // one; otherwise the endpoint source reads them itself.
        Func<StringLiteralComparisonQueryPlan, RetainedFindingComparisonSet>?
            compareStringLiterals =
                bodies?.CompareStringLiterals ?? sourceStringLiterals;
        Func<RetainedFindingComparisonSet>? prepareStringLiterals =
            stringLiteralQuery is { } literal && compareStringLiterals is { } literals
                ? () => literals(literal)
                : null;
        Func<RetainedFindingComparisonSet>? prepareUnsafeMembers =
            DiffAnalysisCatalog.RequiresUnsafeMemberComparison(selection)
                && bodies?.CompareUnsafeMembers is { } unsafeMembers
                    ? () => unsafeMembers(targets.TypeNames)
                    : null;
        Func<ImplementationDiffResult>? prepareImplementation =
            implementation is null ? null : () => implementation;

        if (api is DiffAnalysisApiSource.Unavailable unavailable)
        {
            DiffAnalysisHostUnavailability[] unavailableAnalyses =
            [
                .. selection.Analyses
                    .Where(analysis =>
                        analysis.ParticipationFor(selection.Operation)
                            ?.For(selection.Surface)
                            ?.ProducerRoute
                        != DiffAnalysisCatalog.StringLiteralRoute)
                    .Select(analysis =>
                        new DiffAnalysisHostUnavailability(
                            analysis.Id,
                            unavailable.Reason)),
            ];
            return DiffAnalysisInput.WithoutApiSurfaces(
                fromPaths,
                toPaths,
                targets.TypeFilters,
                targets.TypeNames,
                targets.MemberIdentities,
                bodies?.CompareBodySignals,
                prepareImplementation,
                unavailableAnalyses,
                stringLiteralQuery,
                prepareStringLiterals,
                prepareUnsafeMembers);
        }

        var available = (DiffAnalysisApiSource.Available)api;
        return new DiffAnalysisInput(
            available.Before,
            available.After,
            fromPaths,
            toPaths,
            targets.TypeFilters,
            targets.TypeNames,
            targets.MemberIdentities,
            bodies?.CompareBodySignals,
            prepareImplementation,
            available.PrecomputedComparison,
            HostUnavailability(selection, content, stringLiteralQuery),
            stringLiteralQuery,
            prepareStringLiterals,
            prepareUnsafeMembers);
    }

    /// <summary>
    /// The host's unavailable analyses, then the analyses that read body
    /// assemblies when an endpoint's body population is empty.
    /// </summary>
    static List<DiffAnalysisHostUnavailability> HostUnavailability(
        AnalysisSetValidationResult.Accepted selection,
        DiffPairContent content,
        StringLiteralComparisonQueryPlan? stringLiteralQuery)
    {
        List<DiffAnalysisHostUnavailability> unavailable =
            [.. content.HostUnavailability];
        if (content.Bodies is not { } bodies
            || bodies.BeforeAssemblyCount > 0 && bodies.AfterAssemblyCount > 0)
        {
            return unavailable;
        }

        if (stringLiteralQuery is not null)
        {
            unavailable.Add(new DiffAnalysisHostUnavailability(
                new AnalysisDeclarationId("string-literals"),
                "String-literal Diff requires Library assemblies "
                    + "at both endpoints."));
        }
        if (DiffAnalysisCatalog.RequiresUnsafeMemberComparison(selection))
        {
            unavailable.Add(new DiffAnalysisHostUnavailability(
                new AnalysisDeclarationId("unsafe-member"),
                "Unsafe member Diff requires Library assemblies "
                    + "at both endpoints."));
        }
        return unavailable;
    }
}

/// <summary>The API surfaces an endpoint source supplies to the builder.</summary>
internal abstract record DiffAnalysisApiSource
{
    private DiffAnalysisApiSource()
    {
    }

    /// <summary>
    /// Both surfaces, and the owner-issued API comparison when the source
    /// already compared them.
    /// </summary>
    internal sealed record Available(
        ApiSurface Before,
        ApiSurface After,
        ApiFindingComparison? PrecomputedComparison) : DiffAnalysisApiSource;

    /// <summary>
    /// No complete surface pair: every analysis but string literals is
    /// unavailable for <paramref name="Reason"/>.
    /// </summary>
    internal sealed record Unavailable(string Reason) : DiffAnalysisApiSource;
}

/// <summary>
/// The request's targets as the input states them: the Type filters, the
/// exact Type names the analyses read, and the member identities of a Member
/// surface.
/// </summary>
internal sealed record DiffAnalysisTargets(
    IReadOnlySet<string> TypeFilters,
    IReadOnlyList<string> TypeNames,
    IReadOnlySet<string>? MemberIdentities)
{
    /// <summary>Exact targets, which need no surface.</summary>
    internal static DiffAnalysisTargets FromExact(DiffTargets targets) =>
        targets switch
        {
            DiffTargets.Library => new(
                new HashSet<string>(StringComparer.Ordinal),
                [],
                null),
            DiffTargets.Types types => new(
                new HashSet<string>(types.Names, StringComparer.Ordinal),
                types.Names,
                null),
            DiffTargets.Members members => new(
                new HashSet<string>(members.OwnerTypeNames, StringComparer.Ordinal),
                members.OwnerTypeNames,
                new HashSet<string>(members.Identities, StringComparer.Ordinal)),
            _ => throw new InvalidOperationException(
                "Unknown Diff targets."),
        };

    /// <summary>
    /// Resolves target patterns against both surfaces at the selection's
    /// report surface. A member target failure is a request failure, never an
    /// outcome, so it throws <see cref="DiffAnalysisTargetException"/> before
    /// any producer runs, whatever the set.
    /// </summary>
    internal static DiffAnalysisTargets Resolve(
        DiffTargetPatterns patterns,
        AnalysisReportSurfaceKind surface,
        ApiSurface fromSurface,
        ApiSurface toSurface)
    {
        DiffResolvedMemberTargets? members = null;
        if (surface == AnalysisReportSurfaceKind.Member)
        {
            try
            {
                members = DiffMemberTargetResolver.Resolve(
                    fromSurface,
                    toSurface,
                    patterns.MemberTargets,
                    patterns.TypeFilters);
            }
            catch (InvalidOperationException ex)
                when (ex is not DiffAnalysisTargetException)
            {
                throw new DiffAnalysisTargetException(ex.Message);
            }
        }
        IReadOnlyList<string> typeNames = surface switch
        {
            AnalysisReportSurfaceKind.Type => DiffAnalysisTypeFilter.Resolve(
                fromSurface,
                toSurface,
                patterns.TypeFilters),
            AnalysisReportSurfaceKind.Member when members is not null =>
                [.. members.TypeNames.Order(StringComparer.Ordinal)],
            _ => [],
        };
        return new(
            patterns.TypeFilters,
            typeNames,
            members?.MemberIdentities);
    }
}
