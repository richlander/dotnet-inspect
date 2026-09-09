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

    public static AssemblyContextEntry<MetadataImageResult>
        ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            MetadataRootKind root)
    {
        AssemblyContextMetadataRootQuery.Validate(root);
        return AssemblyContextQueryExecutor.ExecuteParticipant(
            group,
            participant,
            session => MetadataImageQuery.Execute(session, root));
    }

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
/// Produces ReadyToRun image outcomes over assembly-context participants.
/// </summary>
public static class AssemblyContextReadyToRunImageQuery
{
    public static InspectionQuery<
        AssemblyContextResult<ReadyToRunImageResult>>
        Definition { get; } =
        new("Assembly context ReadyToRun images", InspectionCost.Unbounded);

    public static AssemblyContextResult<ReadyToRunImageResult> Execute(
        AssemblyContextGroup group) =>
        AssemblyContextQueryExecutor.Execute(
            group,
            ReadyToRunImageQuery.Execute);

    public static AssemblyContextEntry<ReadyToRunImageResult>
        ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant) =>
        AssemblyContextQueryExecutor.ExecuteParticipant(
            group,
            participant,
            ReadyToRunImageQuery.Execute);
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
        MetadataTableWindowRequest request,
        MetadataRootKind root = MetadataRootKind.Cli)
    {
        ArgumentNullException.ThrowIfNull(request);
        AssemblyContextMetadataRootQuery.Validate(root);
        return AssemblyContextQueryExecutor.Execute(
            group,
            session => Inspect(session, request, root));
    }

    public static AssemblyContextEntry<MetadataTableWindow>
        ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            MetadataTableWindowRequest request,
            MetadataRootKind root = MetadataRootKind.Cli)
    {
        ArgumentNullException.ThrowIfNull(request);
        AssemblyContextMetadataRootQuery.Validate(root);
        return AssemblyContextQueryExecutor.ExecuteParticipant(
            group,
            participant,
            session => Inspect(session, request, root));
    }

    static MetadataTableWindow Inspect(
        AssemblyInspectionSession session,
        MetadataTableWindowRequest request,
        MetadataRootKind root)
    {
        var options = new MetadataProjectionOptions
        {
            Tables = ImmutableArray.Create(request.Table),
            StartRowId = request.StartRowId,
            MaxRowsPerTable = request.MaxRows,
        };
        MetadataTableProjection projection =
            root == MetadataRootKind.Cli
                ? session.MetadataTables(options)
                : AssemblyContextMetadataRootQuery.Require(
                    session,
                    root).Tables(options);
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
        HeapKind heap,
        MetadataRootKind root = MetadataRootKind.Cli)
    {
        ValidateHeap(heap);
        AssemblyContextMetadataRootQuery.Validate(root);
        return AssemblyContextQueryExecutor.Execute(
            group,
            session => Inspect(session, heap, root));
    }

    public static AssemblyContextEntry<MetadataHeapEntrySet>
        ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            HeapKind heap,
            MetadataRootKind root = MetadataRootKind.Cli)
    {
        ValidateHeap(heap);
        AssemblyContextMetadataRootQuery.Validate(root);
        return AssemblyContextQueryExecutor.ExecuteParticipant(
            group,
            participant,
            session => Inspect(session, heap, root));
    }

    static MetadataHeapEntrySet Inspect(
        AssemblyInspectionSession session,
        HeapKind heap,
        MetadataRootKind root)
    {
        var options = new MetadataProjectionOptions
        {
            MaxHeapEntries =
                MetadataProjectionOptions.DefaultMaxHeapEntries,
            MaxRowsPerTable =
                MetadataProjectionOptions.DefaultMaxRowsPerTable,
        };
        if (root == MetadataRootKind.Cli)
        {
            return session.MetadataHeapEntries(heap, options)
                ?? throw new BadImageFormatException(
                    "The selected image contains no managed metadata.");
        }

        return AssemblyContextMetadataRootQuery.Require(
            session,
            root).HeapEntries(heap, options);
    }

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

static class AssemblyContextMetadataRootQuery
{
    internal static MetadataRootInspection Require(
        AssemblyInspectionSession session,
        MetadataRootKind root) =>
        MetadataImageQuery.Execute(session, root) switch
        {
            MetadataImageResult.Available { Root: { } selected } =>
                selected,
            MetadataImageResult.MissingRoot =>
                throw new BadImageFormatException(
                    "The requested metadata root is absent."),
            MetadataImageResult.NoMetadata =>
                throw new BadImageFormatException(
                    "The selected image contains no managed metadata."),
            MetadataImageResult.Failed failed => throw failed.Error,
            _ => throw new InvalidOperationException(
                "Unknown metadata image result."),
        };

    internal static void Validate(MetadataRootKind root)
    {
        if (!Enum.IsDefined(root))
        {
            throw new ArgumentOutOfRangeException(
                nameof(root),
                root,
                "The requested metadata root is not defined.");
        }
    }
}
