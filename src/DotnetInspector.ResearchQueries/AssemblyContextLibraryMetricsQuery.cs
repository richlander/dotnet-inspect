using ILInspector.Analysis;

namespace DotnetInspector.Queries;

/// <summary>
/// Composes the Research-owned whole-library metrics document from one
/// implementation participant while the query layer owns the image snapshot.
/// </summary>
public static class AssemblyContextLibraryMetricsQuery
{
    public static AssemblyContextEntry<LibraryMetricsResult> ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant) =>
        ExecuteParticipant(group, participant, CancellationToken.None);

    public static AssemblyContextEntry<LibraryMetricsResult> ExecuteParticipant(
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
                return LibraryMetricsQuery.Execute(execution);
            });
    }
}
