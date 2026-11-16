using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>Requests exact Count for each participant's extension candidates.</summary>
public sealed record AssemblyContextExtensionCandidateCountRequest;

/// <summary>Requests one bounded prefix from each participant's extension candidates.</summary>
public sealed record AssemblyContextExtensionCandidateRowsRequest
{
    public AssemblyContextExtensionCandidateRowsRequest(int maximumRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        MaximumRows = maximumRows;
    }

    public int MaximumRows { get; }
}

/// <summary>
/// Requests receiver-targeted extension candidates from every participant in
/// one retained assembly-context group.
/// </summary>
public sealed record AssemblyContextExtensionCandidatePopulationRequest
{
    public AssemblyContextExtensionCandidatePopulationRequest(
        MetadataExtensionReceiverSelection receiver,
        MetadataOperationPolicy policy,
        AssemblyContextExtensionCandidateCountRequest? count = null,
        AssemblyContextExtensionCandidateRowsRequest? rows = null,
        bool includeNonPublic = false)
    {
        Receiver = receiver
            ?? throw new ArgumentNullException(nameof(receiver));
        Policy = policy
            ?? throw new ArgumentNullException(nameof(policy));
        if (count is null && rows is null)
        {
            throw new ArgumentException(
                "An assembly-context extension candidate population must "
                    + "request Count, Rows, or both.");
        }

        Count = count;
        Rows = rows;
        IncludeNonPublic = includeNonPublic;
    }

    public MetadataExtensionReceiverSelection Receiver { get; }

    public MetadataOperationPolicy Policy { get; }

    public AssemblyContextExtensionCandidateCountRequest? Count { get; }

    public AssemblyContextExtensionCandidateRowsRequest? Rows { get; }

    public bool IncludeNonPublic { get; }

    internal MetadataExtensionRelationPopulationRequest ToMetadataRequest() =>
        new(
            Receiver,
            Policy,
            Count is null
                ? null
                : new MetadataExtensionRelationPopulationCountRequest(),
            Rows is null
                ? null
                : new MetadataExtensionRelationPopulationRowsRequest(
                    startOrdinal: 0,
                    Rows.MaximumRows),
            IncludeNonPublic);
}

/// <summary>
/// Executes the Metadata-owned extension candidate population independently
/// over every ordered participant in one retained assembly context.
/// </summary>
public static class AssemblyContextExtensionCandidatesQuery
{
    public static InspectionQuery<
        AssemblyContextResult<
            MetadataExtensionRelationPopulationOutcome>>
        Definition { get; } =
        new(
            "Assembly context extension candidates",
            InspectionCost.Unbounded);

    public static AssemblyContextResult<
        MetadataExtensionRelationPopulationOutcome> Execute(
            AssemblyContextGroup group,
            AssemblyContextExtensionCandidatePopulationRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(request);

        return AssemblyContextQueryExecutor.Execute(
            group,
            session => session.ExtensionRelations(
                request.ToMetadataRequest(),
                cancellationToken));
    }
}
