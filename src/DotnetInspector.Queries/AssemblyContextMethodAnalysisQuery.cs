using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Exact Analysis and metadata evidence for one physical MethodDef body in an
/// assembly-context participant.
/// </summary>
public sealed record AssemblyMethodAnalysis(
    int RequestedMethodToken,
    MethodIdentity Method,
    MethodSignals Signals,
    ImmutableArray<AllocationOccurrence> Allocations,
    ImmutableArray<DirectCall> DirectCalls,
    ImmutableArray<UnsafetyOccurrence> UnsafetyOccurrences,
    ImmutableArray<UnsafeEvidence> UnsafeEvidence,
    ImmutableArray<MethodExceptionRegionInfo> ExceptionRegions,
    ImmutableArray<OptimizationOpportunity> OptimizationOpportunities,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

/// <summary>
/// Reads exact method-body evidence while the query layer owns the retained
/// workspace snapshot and Analysis index.
/// </summary>
public static class AssemblyContextMethodAnalysisQuery
{
    public static InspectionQuery<
        AssemblyContextEntry<AssemblyMethodAnalysis>> Definition { get; } =
        new(
            "Assembly context method analysis",
            InspectionCost.Unbounded);

    public static AssemblyContextEntry<AssemblyMethodAnalysis> ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        int methodToken)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);

        return AssemblyContextQueryExecutor.ExecuteParticipantOverSnapshot(
            group,
            participant,
            (subject, snapshot) => Analyze(
                group,
                participant,
                subject,
                snapshot,
                methodToken));
    }

    static AssemblyMethodAnalysis Analyze(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyContextSubject subject,
        AssemblyImageSnapshot snapshot,
        int methodToken)
    {
        LibraryBodyIndex? index = null;
        try
        {
            var resolver = AssemblyContextAnalysisSource.Resolver(
                group,
                subject);
            index = LibraryBodyIndex.OpenFromPrefetchedImage(
                AssemblyContextAnalysisSource.Name(subject),
                snapshot.Content,
                LibraryBodyAnalysisFeatures.OptimizationOpportunities,
                resolver,
                bodyScope: new HashSet<int> { methodToken });

            MethodIdentity? declaration = index.DeclaredMethods.FirstOrDefault(
                method => method.MetadataToken == methodToken);
            if (declaration is null)
            {
                throw new ArgumentException(
                    $"Token 0x{methodToken:X8} is not a MethodDef in "
                        + $"'{subject.Identity.Name}'.",
                    nameof(methodToken));
            }

            MethodIdentity? method = index.Methods.FirstOrDefault(
                candidate => candidate.MetadataToken == methodToken);
            if (method is null)
            {
                throw new InvalidOperationException(
                    $"Method '{declaration.Name}' (0x{methodToken:X8}) in "
                        + $"'{subject.Identity.Name}' does not have an IL body.");
            }

            using PdbContext metadata = PdbContext.OpenMetadataOnly(
                snapshot.RetainAssemblyReference(
                    participant.Assembly));
            IReadOnlyList<MethodExceptionRegionInfo> exceptionRegions =
                metadata.ResolveExceptionRegions(
                    methodToken,
                    out string? exceptionRegionError);
            if (exceptionRegionError is not null)
            {
                throw new InvalidOperationException(
                    exceptionRegionError);
            }

            index.GetMethodSignals().TryGetValue(
                methodToken,
                out MethodSignals? signals);
            index.GetAllocationOccurrences().TryGetValue(
                methodToken,
                out ImmutableArray<AllocationOccurrence> allocations);
            index.GetDirectCallsByEvidenceMethod().TryGetValue(
                methodToken,
                out ImmutableArray<DirectCall> directCalls);
            index.GetUnsafetyOccurrences().TryGetValue(
                methodToken,
                out ImmutableArray<UnsafetyOccurrence> unsafetyOccurrences);
            index.GetUnsafeEvidenceByMember().TryGetValue(
                methodToken,
                out ImmutableArray<UnsafeEvidence> unsafeEvidence);

            var result = new AssemblyMethodAnalysis(
                methodToken,
                method,
                signals ?? MethodSignals.None,
                EmptyIfDefault(allocations),
                EmptyIfDefault(directCalls),
                EmptyIfDefault(unsafetyOccurrences),
                EmptyIfDefault(unsafeEvidence),
                [.. exceptionRegions],
                [
                    .. index.OptimizationOpportunities.Where(
                        opportunity =>
                            (opportunity.EvidenceMethodToken
                                ?? opportunity.Method.MetadataToken)
                            == methodToken),
                ],
                [
                    .. index.Diagnostics.Where(
                        diagnostic =>
                            diagnostic.MethodToken == methodToken),
                ]);
            resolver.ValidateForPublication();
            return result;
        }
        finally
        {
            index?.ReleaseCallGraphCaches();
        }
    }

    static ImmutableArray<T> EmptyIfDefault<T>(
        ImmutableArray<T> values)
        => values.IsDefault
            ? []
            : values;
}
