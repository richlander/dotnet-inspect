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
/// workspace snapshot and Analysis execution.
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
        int methodToken) =>
        ExecuteParticipant(
            group,
            participant,
            methodToken,
            CancellationToken.None);

    public static AssemblyContextEntry<AssemblyMethodAnalysis> ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        int methodToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        cancellationToken.ThrowIfCancellationRequested();

        return AssemblyContextQueryExecutor.ExecuteParticipantOverSnapshot(
            group,
            participant,
            cancellationToken,
            (subject, snapshot) => Analyze(
                group,
                participant,
                subject,
                snapshot,
                methodToken,
                cancellationToken));
    }

    static AssemblyMethodAnalysis Analyze(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyContextSubject subject,
        AssemblyImageSnapshot snapshot,
        int methodToken,
        CancellationToken cancellationToken)
    {
        LibraryCallGraphAnalysisResult? callGraph = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolver = AssemblyContextAnalysisSource.Resolver(
                group,
                subject);
            cancellationToken.ThrowIfCancellationRequested();
            LibraryBodyAnalysisRequest request =
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities,
                    new HashSet<int> { methodToken });
            LibraryBodyAnalysisExecution execution =
                LibraryBodyAnalysisService.ExecuteImage(
                    AssemblyContextAnalysisSource.Name(subject),
                    snapshot.Content,
                    request,
                    resolver);
            callGraph = execution.CallGraph;
            cancellationToken.ThrowIfCancellationRequested();

            MethodIdentity? declaration = callGraph.DeclaredMethods.FirstOrDefault(
                method => method.MetadataToken == methodToken);
            if (declaration is null)
            {
                throw new ArgumentException(
                    $"Token 0x{methodToken:X8} is not a MethodDef in "
                        + $"'{subject.Identity.Name}'.",
                    nameof(methodToken));
            }

            MethodIdentity? method = callGraph.Methods.FirstOrDefault(
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

            callGraph.MethodSignals.TryGetValue(
                methodToken,
                out MethodSignals? signals);
            execution.Allocations.Occurrences.TryGetValue(
                methodToken,
                out ImmutableArray<AllocationOccurrence> allocations);
            callGraph.DirectCallsByEvidenceMethod.TryGetValue(
                methodToken,
                out ImmutableArray<DirectCall> directCalls);
            execution.Safety.Occurrences.TryGetValue(
                methodToken,
                out ImmutableArray<UnsafetyOccurrence> unsafetyOccurrences);
            execution.Safety.GetEvidenceByMember().TryGetValue(
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
                    .. execution.Optimization.Opportunities.Where(
                        opportunity =>
                            (opportunity.EvidenceMethodToken
                                ?? opportunity.Method.MetadataToken)
                            == methodToken),
                ],
                [
                    .. execution.Receipt.Diagnostics.Where(
                        diagnostic =>
                            diagnostic.MethodToken == methodToken),
                ]);
            resolver.ValidateForPublication();
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally
        {
            callGraph?.ReleaseCaches();
        }
    }

    static ImmutableArray<T> EmptyIfDefault<T>(
        ImmutableArray<T> values)
        => values.IsDefault
            ? []
            : values;
}
