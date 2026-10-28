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
                LibraryCallGraphAnalysisResult callGraph =
                    AssemblyContextCallGraphAnalysis.Analyze(
                        snapshot,
                        subject);
                try
                {
                    return LibraryDependencyStructure.Execute(callGraph);
                }
                finally
                {
                    callGraph.ReleaseCaches();
                }
            });
    }
}
