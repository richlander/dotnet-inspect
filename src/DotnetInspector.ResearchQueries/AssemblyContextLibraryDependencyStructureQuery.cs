using ILInspector.Analysis;
using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>
/// Composes the Research-owned whole-library dependency document from one
/// implementation participant while the query layer owns the image snapshot.
/// </summary>
public static class AssemblyContextLibraryDependencyStructureQuery
{
    public static AssemblyContextEntry<LibraryDependencyStructureResult>
        ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant) =>
        ExecuteParticipant(group, participant, CancellationToken.None);

    public static AssemblyContextEntry<LibraryDependencyStructureResult>
        ExecuteParticipant(
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
                        LibraryBodyAnalysisRequest.Create(
                            LibraryBodyAnalysisFeatures.MethodEvidence),
                        resolver);
                return LibraryDependencyStructure.Execute(execution);
            });
    }
}
