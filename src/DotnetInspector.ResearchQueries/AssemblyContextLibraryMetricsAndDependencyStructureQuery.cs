using ILInspector.Analysis;
using ILInspector.Research;

namespace DotnetInspector.Queries;

public sealed record LibraryMetricsAndDependencyStructureResult(
    LibraryMetricsResult Metrics,
    LibraryDependencyStructureResult DependencyStructure);

/// <summary>
/// Composes the Research-owned whole-library metrics and dependency documents
/// from one implementation participant and one Analysis execution.
/// </summary>
public static class
    AssemblyContextLibraryMetricsAndDependencyStructureQuery
{
    public static AssemblyContextEntry<
        LibraryMetricsAndDependencyStructureResult> ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant) =>
        ExecuteParticipant(group, participant, CancellationToken.None);

    public static AssemblyContextEntry<
        LibraryMetricsAndDependencyStructureResult> ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        cancellationToken.ThrowIfCancellationRequested();

        return AssemblyContextQueryExecutor.ExecuteParticipantOverSnapshot(
            group,
            participant,
            cancellationToken,
            (subject, snapshot) =>
            {
                AssemblyContextAnalysisSource.BindingPolicyResolver resolver =
                    AssemblyContextAnalysisSource.Resolver(group, subject);
                LibraryBodyAnalysisExecution execution =
                    LibraryBodyAnalysisService.ExecuteImage(
                        AssemblyContextAnalysisSource.Name(subject),
                        snapshot.Content,
                        LibraryBodyAnalysisRequest
                            .CreateCompleteImplementationProfile(),
                        resolver);
                return Execute(execution);
            });
    }

    public static LibraryMetricsAndDependencyStructureResult Execute(
        LibraryBodyAnalysisExecution execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        return new(
            LibraryMetricsQuery.Execute(execution),
            LibraryDependencyStructure.Execute(execution));
    }
}
