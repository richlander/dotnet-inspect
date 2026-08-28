using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// A bounded row window from one metadata table.
/// </summary>
public sealed record MetadataTableWindow(
    int StartRowId,
    int MaxRows,
    MetadataTableView Table);

/// <summary>
/// Validated bounds for one metadata-table window.
/// </summary>
public sealed record MetadataTableWindowRequest
{
    public const int MaxRowsLimit =
        MetadataProjectionOptions.DefaultMaxRowsPerTable;

    public MetadataTableWindowRequest(
        TableIndex table,
        int startRowId = MetadataProjectionOptions.DefaultStartRowId,
        int maxRows = MetadataProjectionOptions.DefaultMaxRowsPerTable)
    {
        if (!Enum.IsDefined(table)
            || !MetadataTableProjector.ProjectedTables.Contains(table))
        {
            throw new ArgumentOutOfRangeException(
                nameof(table),
                table,
                "The requested ECMA-335 table is not projected.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(startRowId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maxRows,
            MaxRowsLimit);

        Table = table;
        StartRowId = startRowId;
        MaxRows = maxRows;
    }

    public TableIndex Table { get; }
    public int StartRowId { get; }
    public int MaxRows { get; }
}

/// <summary>
/// Produces image-level metadata over a binding-consistent assembly context.
/// </summary>
public static class AssemblyContextMetadataImageQuery
{
    public static InspectionQuery<
        AssemblyContextResult<MetadataImageOverview>>
        Definition { get; } =
        new("Assembly context metadata images", InspectionCost.Unbounded);

    public static AssemblyContextResult<MetadataImageOverview> Execute(
        AssemblyContextGroup group) =>
        AssemblyContextQueryExecutor.Execute(
            group,
            static session => RequireOverview(
                MetadataImageQuery.Execute(session)));

    public static AssemblyContextEntry<MetadataImageOverview>
        ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant) =>
        AssemblyContextQueryExecutor.ExecuteParticipant(
            group,
            participant,
            static session => RequireOverview(
                MetadataImageQuery.Execute(session)));

    static MetadataImageOverview RequireOverview(
        MetadataImageResult result) =>
        result switch
        {
            MetadataImageResult.Available available =>
                available.Overview,
            MetadataImageResult.NoMetadata =>
                throw new BadImageFormatException(
                    "The assembly context participant contains no managed metadata."),
            MetadataImageResult.Failed failed => throw failed.Error,
            _ => throw new InvalidOperationException(
                "Unknown metadata image result."),
        };
}

/// <summary>
/// Produces bounded metadata-table windows over assembly-context participants.
/// </summary>
public static class AssemblyContextMetadataTableQuery
{
    public static InspectionQuery<
        AssemblyContextResult<MetadataTableWindow>>
        Definition { get; } =
        new("Assembly context metadata table window", InspectionCost.Unbounded);

    public static AssemblyContextResult<MetadataTableWindow> Execute(
        AssemblyContextGroup group,
        MetadataTableWindowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return AssemblyContextQueryExecutor.Execute(
            group,
            session => Inspect(session, request));
    }

    public static AssemblyContextEntry<MetadataTableWindow>
        ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            MetadataTableWindowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return AssemblyContextQueryExecutor.ExecuteParticipant(
            group,
            participant,
            session => Inspect(session, request));
    }

    static MetadataTableWindow Inspect(
        AssemblyInspectionSession session,
        MetadataTableWindowRequest request)
    {
        MetadataTableProjection projection = session.MetadataTables(
            new MetadataProjectionOptions
            {
                Tables = ImmutableArray.Create(request.Table),
                StartRowId = request.StartRowId,
                MaxRowsPerTable = request.MaxRows,
            });
        MetadataTableView table = projection.Tables.SingleOrDefault()
            ?? throw new BadImageFormatException(
                $"The selected image does not project the {request.Table} table.");
        return new MetadataTableWindow(
            request.StartRowId,
            request.MaxRows,
            table);
    }
}

/// <summary>
/// Produces bounded metadata-heap listings over assembly-context participants.
/// </summary>
public static class AssemblyContextMetadataHeapQuery
{
    public static InspectionQuery<
        AssemblyContextResult<MetadataHeapEntrySet>>
        Definition { get; } =
        new("Assembly context metadata heap", InspectionCost.Unbounded);

    public static AssemblyContextResult<MetadataHeapEntrySet> Execute(
        AssemblyContextGroup group,
        HeapKind heap)
    {
        ValidateHeap(heap);
        return AssemblyContextQueryExecutor.Execute(
            group,
            session => Inspect(session, heap));
    }

    public static AssemblyContextEntry<MetadataHeapEntrySet>
        ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            HeapKind heap)
    {
        ValidateHeap(heap);
        return AssemblyContextQueryExecutor.ExecuteParticipant(
            group,
            participant,
            session => Inspect(session, heap));
    }

    static MetadataHeapEntrySet Inspect(
        AssemblyInspectionSession session,
        HeapKind heap) =>
        session.MetadataHeapEntries(
            heap,
            new MetadataProjectionOptions
            {
                MaxHeapEntries =
                    MetadataProjectionOptions.DefaultMaxHeapEntries,
                MaxRowsPerTable =
                    MetadataProjectionOptions.DefaultMaxRowsPerTable,
            })
        ?? throw new BadImageFormatException(
            "The selected image contains no managed metadata.");

    static void ValidateHeap(HeapKind heap)
    {
        if (!Enum.IsDefined(heap))
        {
            throw new ArgumentOutOfRangeException(
                nameof(heap),
                heap,
                "The requested metadata heap is not defined.");
        }
    }
}
