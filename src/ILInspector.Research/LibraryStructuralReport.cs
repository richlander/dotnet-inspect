using System.Collections.Immutable;
using ILInspector.Analysis;

namespace ILInspector.Research;

public enum LibraryStructuralReportUnavailableReason
{
    ImplementationProfilesNotRequested,
    NonWholeLibraryScope,
}

public abstract record LibraryStructuralReportResult
{
    private LibraryStructuralReportResult()
    {
    }

    public sealed record Available(
        LibraryStructuralReportDocument Document)
        : LibraryStructuralReportResult;

    public sealed record Unavailable(
        LibraryStructuralReportUnavailableReason Reason,
        string Message,
        LibraryBodyAnalysisReceipt Receipt,
        ImplementationProfilePopulationCoverageReceipt Coverage)
        : LibraryStructuralReportResult;
}

public enum LibraryStructuralMetric
{
    InstructionCount,
    NormalFlowCyclomaticComplexity,
    LoopCount,
    ExceptionRegionCount,
    DirectCallCount,
    AllocationCount,
}

public sealed record LibraryStructuralReportDocument(
    LibraryBodyAnalysisReceipt AnalysisReceipt,
    string MethodologyVersion,
    LibraryStructuralPopulationReceipt Population,
    ImmutableArray<LibraryStructuralMetricDistribution> Distributions,
    LibraryStructuralBooleanDisposition AsyncStateMachinePresence,
    ImmutableArray<LibraryStructuralTypeSummary> TypeSummaries,
    ImmutableArray<LibraryStructuralTypeRelationship> EntangledRelationships,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

public sealed record LibraryStructuralTypeSummary(
    TypeRef Type,
    int BodyCount,
    int InstructionCount,
    int ComplexityTotal,
    int LoopCount,
    int DirectCallCount,
    int AllocationCount);

public sealed record LibraryStructuralTypeRelationship(
    TypeRef Source,
    TypeRef Target,
    int CallSiteCount,
    int SourceDegree,
    int TargetDegree);

public sealed record LibraryStructuralPopulationReceipt(
    ImplementationProfilePopulationCoverageReceipt Coverage,
    int PhysicalEvidenceBodyCount,
    int ProfiledPhysicalEvidenceBodyCount,
    int LogicalOwnerCount,
    int CompleteProfileCount,
    int IncompleteProfileCount,
    ImmutableArray<LibraryStructuralReasonCount> IncompleteReasons,
    ImmutableArray<LibraryStructuralReasonCount> UnavailableReasons);

public sealed record LibraryStructuralReasonCount(
    string Reason,
    int Count);

public sealed record LibraryStructuralMetricDistribution(
    LibraryStructuralMetric Metric,
    int CompleteBodyCount,
    int? Minimum,
    int? P50,
    int? P90,
    int? P95,
    int? P99,
    int? Maximum,
    ImmutableArray<LibraryStructuralExtremeBody> MaximumBodies,
    int AdditionalMaximumBodyCount);

public sealed record LibraryStructuralExtremeBody(
    MethodIdentity EvidenceMethod,
    MethodIdentity LogicalOwner,
    int Value);

public sealed record LibraryStructuralBooleanDisposition(
    string Name,
    int CompleteBodyCount,
    int PresentCount,
    int AbsentCount);

public static class LibraryStructuralReport
{
    public const string CurrentMethodologyVersion = "library-metrics.v1";
    public const int MaximumEntangledTypeCount = 24;

    public static LibraryStructuralReportResult Execute(
        LibraryBodyAnalysisExecution analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        return Execute(
            analysis.ImplementationProfiles,
            analysis.CallGraph);
    }

    public static LibraryStructuralReportResult Execute(
        LibraryImplementationProfileAnalysisResult analysis) =>
        Execute(analysis, callGraph: null);

    static LibraryStructuralReportResult Execute(
        LibraryImplementationProfileAnalysisResult analysis,
        LibraryCallGraphAnalysisResult? callGraph)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        if (!analysis.Coverage.WasRequested)
        {
            return Unavailable(
                LibraryStructuralReportUnavailableReason
                    .ImplementationProfilesNotRequested,
                "Library Metrics requires implementation-profile evidence.",
                analysis);
        }

        if (!analysis.Coverage.HasFullMethodEvidenceScope)
        {
            return Unavailable(
                LibraryStructuralReportUnavailableReason.NonWholeLibraryScope,
                "Library Metrics requires unscoped whole-library method "
                    + "evidence coverage.",
                analysis);
        }

        ValidateProfileCoverage(analysis);

        ImmutableArray<MethodImplementationProfile> profiles =
            analysis.Profiles.IsDefault ? [] : analysis.Profiles;
        ImmutableArray<MethodImplementationProfile> completeProfiles =
        [
            .. profiles.Where(static profile => profile.IsComplete),
        ];
        var population = new LibraryStructuralPopulationReceipt(
            analysis.Coverage,
            analysis.Coverage.ManagedMethodBodyCount,
            analysis.Coverage.ProfiledEvidenceBodyCount,
            profiles
                .Select(static profile => profile.Method.MetadataToken)
                .Distinct()
                .Count(),
            completeProfiles.Length,
            profiles.Length - completeProfiles.Length,
            CountIncompleteReasons(profiles),
            CountUnavailableReasons(analysis.Coverage));
        var document = new LibraryStructuralReportDocument(
            analysis.Receipt,
            CurrentMethodologyVersion,
            population,
            [
                Distribution(
                    LibraryStructuralMetric.InstructionCount,
                    completeProfiles,
                    static profile => profile.InstructionCount),
                Distribution(
                    LibraryStructuralMetric.NormalFlowCyclomaticComplexity,
                    completeProfiles,
                    static profile => profile.NormalFlowCyclomaticComplexity),
                Distribution(
                    LibraryStructuralMetric.LoopCount,
                    completeProfiles,
                    static profile => profile.LoopCount),
                Distribution(
                    LibraryStructuralMetric.ExceptionRegionCount,
                    completeProfiles,
                    static profile => ExceptionRegionCount(profile)),
                Distribution(
                    LibraryStructuralMetric.DirectCallCount,
                    completeProfiles,
                    static profile => profile.DirectCallCount),
                Distribution(
                    LibraryStructuralMetric.AllocationCount,
                    completeProfiles,
                    static profile => profile.AllocationCount),
            ],
            new LibraryStructuralBooleanDisposition(
                "async-state-machine",
                completeProfiles.Length,
                completeProfiles.Count(static profile => profile.Async),
                completeProfiles.Count(static profile => !profile.Async)),
            TypeSummaries(completeProfiles),
            EntangledRelationships(completeProfiles, callGraph),
            analysis.Receipt.Diagnostics);
        return new LibraryStructuralReportResult.Available(document);
    }

    static ImmutableArray<LibraryStructuralTypeSummary> TypeSummaries(
        ImmutableArray<MethodImplementationProfile> profiles) =>
    [
        .. profiles
            .GroupBy(static profile => profile.Method.DeclaringType)
            .Select(group => new LibraryStructuralTypeSummary(
                group.Key,
                group.Count(),
                group.Sum(static profile => profile.InstructionCount),
                group.Sum(static profile =>
                    profile.NormalFlowCyclomaticComplexity),
                group.Sum(static profile => profile.LoopCount),
                group.Sum(static profile => profile.DirectCallCount),
                group.Sum(static profile => profile.AllocationCount)))
            .OrderByDescending(static summary => summary.InstructionCount)
            .ThenBy(
                static summary => summary.Type.ToQualifiedDisplayString(),
                StringComparer.Ordinal),
    ];

    static ImmutableArray<LibraryStructuralTypeRelationship>
        EntangledRelationships(
            ImmutableArray<MethodImplementationProfile> profiles,
            LibraryCallGraphAnalysisResult? callGraph)
    {
        if (callGraph is null || profiles.IsEmpty)
            return [];

        HashSet<int> completeBodies =
        [
            .. profiles.Select(static profile =>
                profile.EvidenceMethod.MetadataToken),
        ];
        IReadOnlyDictionary<int, MethodIdentity> methods =
            callGraph.DeclaredMethods
                .GroupBy(static method => method.MetadataToken)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.First());
        var edges = callGraph.DirectCalls
            .Where(call =>
                completeBodies.Contains(call.EvidenceMethod.MetadataToken)
                && call.Kind is CallKind.Call
                    or CallKind.CallVirtual
                    or CallKind.NewObject
                && call.CalleeDefinitionToken != 0
                && methods.ContainsKey(call.CalleeDefinitionToken)
                && !call.Caller.DeclaringType.Equals(methods[
                    call.CalleeDefinitionToken].DeclaringType))
            .GroupBy(call => (
                Source: call.Caller.DeclaringType,
                Target: methods[call.CalleeDefinitionToken].DeclaringType))
            .Select(group => (
                group.Key.Source,
                group.Key.Target,
                CallSiteCount: group.Count()))
            .ToArray();
        if (edges.Length == 0)
            return [];

        Dictionary<TypeRef, HashSet<TypeRef>> neighbors = [];
        Dictionary<TypeRef, int> callSiteVolumes = [];
        foreach (var edge in edges)
        {
            AddNeighbor(edge.Source, edge.Target);
            AddNeighbor(edge.Target, edge.Source);
            AddCallSiteVolume(edge.Source, edge.CallSiteCount);
            AddCallSiteVolume(edge.Target, edge.CallSiteCount);
        }

        var selectedTypes = neighbors
            .OrderByDescending(static pair => pair.Value.Count)
            .ThenByDescending(
                pair => callSiteVolumes[pair.Key])
            .ThenBy(
                static pair => pair.Key.ToQualifiedDisplayString(),
                StringComparer.Ordinal)
            .Take(MaximumEntangledTypeCount)
            .Select(static pair => pair.Key)
            .ToHashSet();

        return
        [
            .. edges
                .Where(edge =>
                    selectedTypes.Contains(edge.Source)
                    && selectedTypes.Contains(edge.Target))
                .Select(edge => new LibraryStructuralTypeRelationship(
                    edge.Source,
                    edge.Target,
                    edge.CallSiteCount,
                    neighbors[edge.Source].Count,
                    neighbors[edge.Target].Count))
                .OrderByDescending(static edge => edge.CallSiteCount)
                .ThenBy(
                    static edge => edge.Source.ToQualifiedDisplayString(),
                    StringComparer.Ordinal)
                .ThenBy(
                    static edge => edge.Target.ToQualifiedDisplayString(),
                    StringComparer.Ordinal),
        ];

        void AddNeighbor(TypeRef source, TypeRef target)
        {
            if (!neighbors.TryGetValue(source, out HashSet<TypeRef>? values))
            {
                values = [];
                neighbors.Add(source, values);
            }
            values.Add(target);
        }

        void AddCallSiteVolume(TypeRef type, int callSiteCount)
        {
            callSiteVolumes.TryGetValue(type, out int volume);
            callSiteVolumes[type] = volume + callSiteCount;
        }
    }

    static LibraryStructuralReportResult.Unavailable Unavailable(
        LibraryStructuralReportUnavailableReason reason,
        string message,
        LibraryImplementationProfileAnalysisResult analysis) =>
        new(
            reason,
            message,
            analysis.Receipt,
            analysis.Coverage);

    static void ValidateProfileCoverage(
        LibraryImplementationProfileAnalysisResult analysis)
    {
        ImmutableArray<MethodImplementationProfile> profiles =
            analysis.Profiles.IsDefault ? [] : analysis.Profiles;
        HashSet<int> coverageTokens =
        [
            .. analysis.Coverage.ProfiledEvidenceBodies
                .Select(static method => method.MetadataToken),
        ];
        HashSet<int> profileTokens = [];
        foreach (MethodImplementationProfile profile in profiles)
        {
            if (!profileTokens.Add(profile.EvidenceMethod.MetadataToken))
            {
                throw new InvalidOperationException(
                    "Library Metrics requires one profile per physical "
                    + $"evidence body; duplicate profile token "
                    + $"0x{profile.EvidenceMethod.MetadataToken:X8}.");
            }
        }

        if (!profileTokens.SetEquals(coverageTokens))
        {
            throw new InvalidOperationException(
                "Library Metrics requires profile rows to match the "
                + "Analysis-issued profiled-body coverage receipt.");
        }
    }

    static ImmutableArray<LibraryStructuralReasonCount> CountIncompleteReasons(
        ImmutableArray<MethodImplementationProfile> profiles) =>
    [
        .. profiles
            .SelectMany(static profile =>
                profile.IncompleteReasons.IsDefault
                    ? []
                    : profile.IncompleteReasons)
            .GroupBy(static reason => reason, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => new LibraryStructuralReasonCount(
                group.Key,
                group.Count())),
    ];

    static ImmutableArray<LibraryStructuralReasonCount> CountUnavailableReasons(
        ImplementationProfilePopulationCoverageReceipt coverage) =>
    [
        .. coverage.UnavailableBodies
            .GroupBy(static body => body.Reason.ToString(), StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => new LibraryStructuralReasonCount(
                group.Key,
                group.Count())),
    ];

    static LibraryStructuralMetricDistribution Distribution(
        LibraryStructuralMetric metric,
        ImmutableArray<MethodImplementationProfile> profiles,
        Func<MethodImplementationProfile, int> selector)
    {
        if (profiles.IsEmpty)
        {
            return new(
                metric,
                0,
                null,
                null,
                null,
                null,
                null,
                null,
                [],
                0);
        }

        int[] values = profiles
            .Select(selector)
            .Order()
            .ToArray();
        int maximum = values[^1];
        ImmutableArray<LibraryStructuralExtremeBody> maximumBodies =
        [
            .. profiles
                .Where(profile => selector(profile) == maximum)
                .OrderBy(static profile =>
                    profile.EvidenceMethod.AssemblyName,
                    StringComparer.Ordinal)
                .ThenBy(static profile =>
                    profile.EvidenceMethod.DeclaringType
                        .ToQualifiedDisplayString(),
                    StringComparer.Ordinal)
                .ThenBy(static profile => profile.EvidenceMethod.Name,
                    StringComparer.Ordinal)
                .ThenBy(static profile => profile.EvidenceMethod.MetadataToken)
                .Take(5)
                .Select(profile => new LibraryStructuralExtremeBody(
                    profile.EvidenceMethod,
                    profile.Method,
                    maximum)),
        ];
        return new(
            metric,
            values.Length,
            values[0],
            NearestRank(values, 50),
            NearestRank(values, 90),
            NearestRank(values, 95),
            NearestRank(values, 99),
            maximum,
            maximumBodies,
            values.Count(value => value == maximum) - maximumBodies.Length);
    }

    static int NearestRank(
        int[] sortedValues,
        int percentile)
    {
        int position = Math.Clamp(
            (int)Math.Ceiling(percentile / 100.0 * sortedValues.Length),
            1,
            sortedValues.Length);
        return sortedValues[position - 1];
    }

    static int ExceptionRegionCount(MethodImplementationProfile profile) =>
        profile.CatchCount
        + profile.FilterCount
        + profile.FinallyCount
        + profile.FaultCount;
}
