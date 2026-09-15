using System.Collections.Immutable;
using System.Reflection.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>
/// Owner-issued identity for one detached method-body observation.
/// </summary>
public readonly record struct MethodBodyEvidenceId
{
    internal MethodBodyEvidenceId(
        MetadataMethodAddress method,
        Guid observationId)
    {
        if (observationId == Guid.Empty)
            throw new ArgumentException("A body observation ID cannot be empty.", nameof(observationId));

        Method = method;
        ObservationId = observationId;
    }

    public MetadataMethodAddress Method { get; }
    public Guid ObservationId { get; }
}

/// <summary>
/// Owner-issued identity for one exception clause in metadata order.
/// </summary>
public readonly record struct MethodExceptionClauseId
{
    internal MethodExceptionClauseId(
        MethodBodyEvidenceId body,
        int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        Body = body;
        Ordinal = ordinal;
    }

    public MethodBodyEvidenceId Body { get; }
    public int Ordinal { get; }
}

/// <summary>A half-open extent in one method body's IL stream.</summary>
public readonly record struct MethodBodyExtent
{
    internal MethodBodyExtent(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        if (end < start)
            throw new ArgumentOutOfRangeException(nameof(end));

        Start = start;
        End = end;
    }

    public int Start { get; }
    public int End { get; }
    public int Length => End - Start;

    public bool Contains(int offset) => offset >= Start && offset < End;
}

/// <summary>Detached catch-type token and strict name-resolution evidence.</summary>
public sealed record MethodExceptionCatchType
{
    internal MethodExceptionCatchType(
        int metadataToken,
        MetadataTypeNameResult name)
    {
        ArgumentNullException.ThrowIfNull(name);
        MetadataToken = metadataToken;
        Name = name;
    }

    public int MetadataToken { get; }
    public MetadataTypeNameResult Name { get; }

    public string? DisplayName =>
        Name is MetadataTypeNameResult.Resolved resolved
            ? resolved.Value
            : null;
}

/// <summary>One physical exception clause from a method body.</summary>
public sealed record MethodExceptionClause
{
    internal MethodExceptionClause(
        MethodExceptionClauseId id,
        ExceptionRegionKind kind,
        MethodBodyExtent protectedExtent,
        MethodBodyExtent handlerExtent,
        MethodBodyExtent? filterExtent,
        MethodExceptionCatchType? catchType)
    {
        if ((kind == ExceptionRegionKind.Filter) != filterExtent.HasValue)
        {
            throw new ArgumentException(
                "Only a filter clause has a filter extent.",
                nameof(filterExtent));
        }

        if ((kind == ExceptionRegionKind.Catch) != (catchType is not null))
        {
            throw new ArgumentException(
                "Only a catch clause has catch-type evidence.",
                nameof(catchType));
        }

        Id = id;
        Kind = kind;
        ProtectedExtent = protectedExtent;
        HandlerExtent = handlerExtent;
        FilterExtent = filterExtent;
        CatchType = catchType;
    }

    public MethodExceptionClauseId Id { get; }
    public ExceptionRegionKind Kind { get; }
    public MethodBodyExtent ProtectedExtent { get; }
    public MethodBodyExtent HandlerExtent { get; }
    public MethodBodyExtent? FilterExtent { get; }
    public MethodExceptionCatchType? CatchType { get; }
}

/// <summary>The physical role one clause extent has at an IL offset.</summary>
public enum MethodExceptionRegionRole
{
    Protected,
    Filter,
    Handler,
}

/// <summary>One clause extent containing a numeric IL offset.</summary>
public readonly record struct MethodExceptionRegionContext(
    MethodExceptionClause Clause,
    MethodExceptionRegionRole Role);

/// <summary>
/// Complete physical exception-clause evidence for one detached body.
/// </summary>
public sealed record MethodExceptionRegionCatalog
{
    internal MethodExceptionRegionCatalog(
        MethodBodyEvidenceId body,
        ImmutableArray<MethodExceptionClause> clauses)
    {
        clauses = clauses.IsDefault ? [] : clauses;
        for (int ordinal = 0; ordinal < clauses.Length; ordinal++)
        {
            MethodExceptionClause clause = clauses[ordinal]
                ?? throw new ArgumentException(
                    "An exception catalog cannot contain a null clause.",
                    nameof(clauses));
            if (clause.Id.Body != body || clause.Id.Ordinal != ordinal)
            {
                throw new ArgumentException(
                    "Exception clause identities must match the catalog body and metadata order.",
                    nameof(clauses));
            }
        }

        Body = body;
        Clauses = clauses;
    }

    public MethodBodyEvidenceId Body { get; }
    public ImmutableArray<MethodExceptionClause> Clauses { get; }
    public bool HasExceptionRegions => !Clauses.IsEmpty;

    /// <summary>
    /// Returns every declared clause extent containing a numeric IL offset.
    /// This does not claim the offset is an instruction boundary or reachable.
    /// </summary>
    public ImmutableArray<MethodExceptionRegionContext> ContextsAt(int ilOffset)
    {
        var contexts = ImmutableArray.CreateBuilder<MethodExceptionRegionContext>();
        foreach (MethodExceptionClause clause in Clauses)
        {
            if (clause.FilterExtent is { } filter && filter.Contains(ilOffset))
            {
                contexts.Add(new MethodExceptionRegionContext(
                    clause,
                    MethodExceptionRegionRole.Filter));
            }
            if (clause.HandlerExtent.Contains(ilOffset))
            {
                contexts.Add(new MethodExceptionRegionContext(
                    clause,
                    MethodExceptionRegionRole.Handler));
            }
            if (clause.ProtectedExtent.Contains(ilOffset))
            {
                contexts.Add(new MethodExceptionRegionContext(
                    clause,
                    MethodExceptionRegionRole.Protected));
            }
        }

        return contexts.ToImmutable();
    }
}

/// <summary>
/// A copied method body and complete physical exception-clause catalog from one
/// owner-issued observation. It carries no reader-backed state.
/// </summary>
public sealed record MethodBodyData
{
    internal MethodBodyData(
        ImmutableArray<byte> il,
        ImmutableArray<ExceptionRegion> exceptionRegions,
        MethodExceptionRegionCatalog exceptionRegionCatalog)
    {
        ArgumentNullException.ThrowIfNull(exceptionRegionCatalog);
        il = il.IsDefault ? [] : il;
        exceptionRegions = exceptionRegions.IsDefault ? [] : exceptionRegions;
        if (exceptionRegions.Length != exceptionRegionCatalog.Clauses.Length)
        {
            throw new ArgumentException(
                "The legacy region array and exception catalog must describe the same clauses.",
                nameof(exceptionRegions));
        }

        IL = il;
        ExceptionRegions = exceptionRegions;
        ExceptionRegionCatalog = exceptionRegionCatalog;
    }

    public MethodBodyEvidenceId EvidenceId => ExceptionRegionCatalog.Body;
    public ImmutableArray<byte> IL { get; }

    /// <summary>
    /// Raw compatibility handoff retained until Instructions adopts
    /// <see cref="ExceptionRegionCatalog"/>.
    /// </summary>
    public ImmutableArray<ExceptionRegion> ExceptionRegions { get; }

    public MethodExceptionRegionCatalog ExceptionRegionCatalog { get; }
}
