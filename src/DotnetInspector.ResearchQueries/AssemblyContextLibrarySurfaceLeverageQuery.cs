using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>
/// Typed outcome of composing signature-evidence structural salience for one
/// exact Library population.
/// </summary>
public abstract record LibrarySurfaceLeverageResult
{
    private LibrarySurfaceLeverageResult()
    {
    }

    public sealed record AvailableIndex(
        LibraryStructuralNamespaceLeverageIndex Index)
        : LibrarySurfaceLeverageResult;

    public sealed record AvailableShard(
        LibraryStructuralTypeLeverageShard Shard)
        : LibrarySurfaceLeverageResult;

    public sealed record AvailableExhaustive(
        LibraryStructuralSalienceDocument Document)
        : LibrarySurfaceLeverageResult;

    public sealed record Rejected(
        MetadataLibrarySignatureUseRejectionKind Kind,
        string Detail,
        MetadataOperationCounters? Counters)
        : LibrarySurfaceLeverageResult;
}

/// <summary>
/// Produces signature-only namespace and Type leverage without running body
/// analysis or the rest of Library Metrics.
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

    public static InspectionQuery<LibrarySurfaceLeverageResult>
        NamespaceIndexDefinition { get; } =
        new("Library namespace leverage", InspectionCost.Unbounded);

    public static InspectionQuery<LibrarySurfaceLeverageResult>
        TypeShardDefinition { get; } =
        new("Namespace Type leverage", InspectionCost.Unbounded);

    public static AssemblyContextEntry<LibrarySurfaceLeverageResult>
        ExecuteNamespaceIndexParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            CancellationToken cancellationToken = default) =>
        ExecuteParticipant(
            group,
            participant,
            exactNamespace: null,
            exhaustive: false,
            cancellationToken);

    public static AssemblyContextEntry<LibrarySurfaceLeverageResult>
        ExecuteTypeShardParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            string exactNamespace,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exactNamespace);
        return ExecuteParticipant(
            group,
            participant,
            exactNamespace,
            exhaustive: false,
            cancellationToken);
    }

    public static AssemblyContextEntry<LibrarySurfaceLeverageResult>
        ExecuteExhaustiveParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            CancellationToken cancellationToken = default) =>
        ExecuteParticipant(
            group,
            participant,
            exactNamespace: null,
            exhaustive: true,
            cancellationToken);

    private static AssemblyContextEntry<LibrarySurfaceLeverageResult>
        ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            string? exactNamespace,
            bool exhaustive,
            CancellationToken cancellationToken)
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
                if (exactNamespace is not null)
                {
                    return Acquire(
                        session,
                        new(s_policy, exactNamespace),
                        cancellationToken) switch
                    {
                        MetadataLibrarySignatureUseOutcome.Available
                            available =>
                            new LibrarySurfaceLeverageResult.AvailableShard(
                                LibraryStructuralReport
                                    .CreateTypeLeverageShard(
                                        available.Result)),
                        MetadataLibrarySignatureUseOutcome.Rejected
                            rejected =>
                            Rejected(rejected),
                        _ => throw new InvalidOperationException(
                            "Unknown Library signature-use outcome."),
                    };
                }

                MetadataLibrarySignatureUseOutcome wholeOutcome =
                    Acquire(
                        session,
                        new(s_policy),
                        cancellationToken);
                if (wholeOutcome
                    is MetadataLibrarySignatureUseOutcome.Rejected
                        wholeRejected)
                {
                    return Rejected(wholeRejected);
                }
                MetadataLibrarySignatureUseResult whole =
                    ((MetadataLibrarySignatureUseOutcome.Available)
                        wholeOutcome).Result;
                LibraryStructuralNamespaceLeverageIndex index =
                    LibraryStructuralReport.CreateNamespaceLeverageIndex(
                        whole);
                if (!exhaustive)
                {
                    return new
                        LibrarySurfaceLeverageResult.AvailableIndex(index);
                }

                var shards =
                    new List<LibraryStructuralTypeLeverageShard>(
                        index.Rows.Length);
                foreach (LibraryStructuralNamespaceLeverageRow row
                    in index.Rows)
                {
                    MetadataLibrarySignatureUseOutcome shardOutcome =
                        Acquire(
                            session,
                            new(s_policy, row.Namespace),
                            cancellationToken);
                    if (shardOutcome
                        is MetadataLibrarySignatureUseOutcome.Rejected
                            shardRejected)
                    {
                        return Rejected(shardRejected);
                    }
                    shards.Add(
                        LibraryStructuralReport.CreateTypeLeverageShard(
                            ((MetadataLibrarySignatureUseOutcome.Available)
                                shardOutcome).Result));
                }

                return new
                    LibrarySurfaceLeverageResult.AvailableExhaustive(
                        LibraryStructuralReport.CreateStructuralSalience(
                            index,
                            shards));
            });
    }

    private static MetadataLibrarySignatureUseOutcome Acquire(
        AssemblyInspectionSession session,
        MetadataLibrarySignatureUseRequest request,
        CancellationToken cancellationToken) =>
        session.LibrarySignatureUses(request, cancellationToken);

    private static LibrarySurfaceLeverageResult.Rejected Rejected(
        MetadataLibrarySignatureUseOutcome.Rejected rejected) =>
        new(
            rejected.Kind,
            rejected.Detail,
            rejected.Counters);
}
