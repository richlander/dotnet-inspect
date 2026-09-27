using ILInspector.Analysis;
using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>Typed result of composing one library's dependency structure.</summary>
public abstract record LibraryDependencyStructureQueryResult
{
    private LibraryDependencyStructureQueryResult()
    {
    }

    /// <summary>The Research-owned document for one exact library.</summary>
    public sealed record Available(
        LibraryDependencyStructureDocument Document)
        : LibraryDependencyStructureQueryResult;

    /// <summary>
    /// Research could not issue the document from the supplied Analysis
    /// evidence, preserving the Research unavailable outcome.
    /// </summary>
    public sealed record Unavailable(
        LibraryDependencyStructureResult.Unavailable Outcome)
        : LibraryDependencyStructureQueryResult;

    /// <summary>The image contains no managed metadata and therefore has no method bodies.</summary>
    public sealed record NoMetadata : LibraryDependencyStructureQueryResult;

    /// <summary>The query failed while acquiring or composing the document.</summary>
    public sealed record Failed(Exception Error) : LibraryDependencyStructureQueryResult;
}

/// <summary>
/// Carries the completed Research-owned Library Dependency Structure document
/// from focused Analysis call-graph evidence.
/// </summary>
public static class LibraryDependencyStructureQuery
{
    public static InspectionQuery<LibraryDependencyStructureQueryResult> Definition { get; } =
        new("Library dependency structure", InspectionCost.Unbounded);

    public static LibraryDependencyStructureQueryResult Execute(
        LibraryBodyAnalysisExecution analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        try
        {
            return LibraryDependencyStructure.Execute(analysis) switch
            {
                LibraryDependencyStructureResult.Available available =>
                    new LibraryDependencyStructureQueryResult.Available(available.Document),
                LibraryDependencyStructureResult.Unavailable unavailable =>
                    new LibraryDependencyStructureQueryResult.Unavailable(unavailable),
                var unknown => throw new InvalidOperationException(
                    "Unknown Library Dependency Structure result "
                    + $"'{unknown.GetType().Name}'."),
            };
        }
        catch (Exception ex)
        {
            return new LibraryDependencyStructureQueryResult.Failed(ex);
        }
    }
}

/// <summary>
/// Composes the Library Dependency Structure document from one implementation
/// participant while the query layer owns the image snapshot (Browser/Wasm host).
/// </summary>
public static class AssemblyContextLibraryDependencyStructureQuery
{
    public static AssemblyContextEntry<LibraryDependencyStructureQueryResult> ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant) =>
        ExecuteParticipant(group, participant, CancellationToken.None);

    public static AssemblyContextEntry<LibraryDependencyStructureQueryResult> ExecuteParticipant(
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
                return LibraryDependencyStructureQuery.Execute(execution);
            });
    }
}
