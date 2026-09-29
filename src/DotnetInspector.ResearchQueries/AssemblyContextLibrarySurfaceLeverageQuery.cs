using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>
/// Typed outcome of composing surface Type leverage for one exact Library.
/// </summary>
public abstract record LibrarySurfaceLeverageResult
{
    private LibrarySurfaceLeverageResult()
    {
    }

    public sealed record Available(
        LibraryStructuralTypeLeverageDocument Document)
        : LibrarySurfaceLeverageResult;

    public sealed record Rejected(
        MetadataLibrarySignatureUseRejectionKind Kind,
        string Detail,
        MetadataOperationCounters? Counters)
        : LibrarySurfaceLeverageResult;
}

/// <summary>
/// Produces signature-only Type leverage without running body analysis or the
/// rest of Library Metrics.
/// </summary>
public static class AssemblyContextLibrarySurfaceLeverageQuery
{
    private static readonly MetadataOperationPolicy s_policy =
        new(
            maxMetadataRows: 10_000_000,
            maxDeclarationCandidates: 1_000_000,
            maxRelationshipEdges: 10_000_000,
            maxSignatureBytes: 256_000_000,
            maxGenericSubstitutionNodes: 10_000_000,
            maxStructuredNodes: 10_000_000,
            maxRetainedText: 16_000_000);

    public static InspectionQuery<LibrarySurfaceLeverageResult> Definition
        { get; } =
        new("Library surface Type leverage", InspectionCost.Unbounded);

    public static AssemblyContextEntry<LibrarySurfaceLeverageResult>
        ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        cancellationToken.ThrowIfCancellationRequested();

        return AssemblyContextQueryExecutor
            .ExecuteParticipantOverSnapshot<LibrarySurfaceLeverageResult>(
            group,
            participant,
            cancellationToken,
            (_, snapshot) =>
            {
                using AssemblyInspectionSession session =
                    AssemblyInspectionSession.Open(snapshot);
                MetadataLibrarySignatureUseOutcome outcome =
                    session.LibrarySignatureUses(
                        new(s_policy),
                        cancellationToken);
                return outcome switch
                {
                    MetadataLibrarySignatureUseOutcome.Available available =>
                        new LibrarySurfaceLeverageResult.Available(
                            LibraryStructuralReport.CreateTypeLeverage(
                                available.Result)),
                    MetadataLibrarySignatureUseOutcome.Rejected rejected =>
                        new LibrarySurfaceLeverageResult.Rejected(
                            rejected.Kind,
                            rejected.Detail,
                            rejected.Counters),
                    _ => throw new InvalidOperationException(
                        "Unknown Library signature-use outcome."),
                };
            });
    }
}
