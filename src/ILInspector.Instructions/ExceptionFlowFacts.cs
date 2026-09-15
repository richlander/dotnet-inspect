using System.Collections.Immutable;
using System.Reflection.Metadata;
using ILInspector.Metadata;

namespace ILInspector.Instructions;

/// <summary>The role a validated exception region has in decoded IL.</summary>
public enum InstructionExceptionRegionRole
{
    Protected,
    Filter,
    Handler,
}

/// <summary>Owner-issued identity for one validated region in a Metadata body observation.</summary>
public readonly record struct InstructionExceptionRegionId
{
    internal InstructionExceptionRegionId(
        MethodBodyEvidenceId body,
        InstructionExceptionRegionRole role,
        int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        Body = body;
        Role = role;
        Ordinal = ordinal;
    }

    public MethodBodyEvidenceId Body { get; }
    public InstructionExceptionRegionRole Role { get; }
    public int Ordinal { get; }
}

/// <summary>One instruction-aligned protected, filter, or handler region.</summary>
public sealed record InstructionExceptionRegion
{
    internal InstructionExceptionRegion(
        InstructionExceptionRegionId id,
        MethodBodyExtent extent,
        ImmutableArray<MethodExceptionClauseId> clauses)
    {
        if (clauses.IsDefaultOrEmpty)
            throw new ArgumentException("A validated region must belong to at least one clause.", nameof(clauses));
        if (clauses.Any(clause => clause.Body != id.Body))
            throw new ArgumentException("Region and clause identities must belong to the same body.", nameof(clauses));

        Id = id;
        Extent = extent;
        Clauses = clauses;
    }

    public InstructionExceptionRegionId Id { get; }
    public MethodBodyExtent Extent { get; }
    public ImmutableArray<MethodExceptionClauseId> Clauses { get; }
}

/// <summary>One Metadata clause associated with its validated Instructions regions.</summary>
public sealed record InstructionExceptionClause
{
    internal InstructionExceptionClause(
        MethodExceptionClause clause,
        InstructionExceptionRegionId protectedRegion,
        InstructionExceptionRegionId handlerRegion,
        InstructionExceptionRegionId? filterRegion)
    {
        ArgumentNullException.ThrowIfNull(clause);
        if (clause.Id.Body != protectedRegion.Body
            || clause.Id.Body != handlerRegion.Body
            || filterRegion is { } filter && clause.Id.Body != filter.Body)
        {
            throw new ArgumentException("Clause and region identities must belong to the same body.", nameof(clause));
        }

        Clause = clause;
        ProtectedRegion = protectedRegion;
        HandlerRegion = handlerRegion;
        FilterRegion = filterRegion;
    }

    public MethodExceptionClause Clause { get; }
    public MethodExceptionClauseId Id => Clause.Id;
    public ExceptionRegionKind Kind => Clause.Kind;
    public InstructionExceptionRegionId ProtectedRegion { get; }
    public InstructionExceptionRegionId HandlerRegion { get; }
    public InstructionExceptionRegionId? FilterRegion { get; }
}

public enum NormalContinuationKind
{
    Block,
    MethodExit,
}

/// <summary>
/// Canonical logical destination reached if every scheduled cleanup completes normally.
/// </summary>
public readonly record struct NormalContinuationId
{
    internal NormalContinuationId(
        MethodBodyEvidenceId body,
        NormalContinuationKind kind,
        int blockStart)
    {
        if (kind == NormalContinuationKind.Block)
            ArgumentOutOfRangeException.ThrowIfNegative(blockStart);
        else if (blockStart != -1)
            throw new ArgumentOutOfRangeException(nameof(blockStart));

        Body = body;
        Kind = kind;
        BlockStart = blockStart;
    }

    public MethodBodyEvidenceId Body { get; }
    public NormalContinuationKind Kind { get; }
    public int BlockStart { get; }
}

public enum InstructionNormalTransferKind
{
    Branch,
    Leave,
    Return,
}

/// <summary>One <c>finally</c> dispatched during normal completion.</summary>
public readonly record struct InstructionCleanupHandler(
    MethodExceptionClauseId Clause,
    InstructionExceptionRegionId Handler);

/// <summary>Complete EH facts for one supported normal control-flow edge.</summary>
public sealed record InstructionNormalTransfer
{
    internal InstructionNormalTransfer(
        InstructionNormalTransferKind kind,
        int sourceOffset,
        int? destinationOffset,
        ImmutableArray<InstructionExceptionRegion> sourceContext,
        ImmutableArray<InstructionExceptionRegion> destinationContext,
        ImmutableArray<InstructionExceptionRegion> regionsLeft,
        ImmutableArray<InstructionCleanupHandler> cleanupHandlers,
        ImmutableArray<InstructionExceptionRegion> regionsEntered,
        NormalContinuationId continuation)
    {
        Kind = kind;
        SourceOffset = sourceOffset;
        DestinationOffset = destinationOffset;
        SourceContext = sourceContext;
        DestinationContext = destinationContext;
        RegionsLeft = regionsLeft;
        CleanupHandlers = cleanupHandlers;
        RegionsEntered = regionsEntered;
        Continuation = continuation;
    }

    public InstructionNormalTransferKind Kind { get; }
    public int SourceOffset { get; }
    public int? DestinationOffset { get; }
    public ImmutableArray<InstructionExceptionRegion> SourceContext { get; }
    public ImmutableArray<InstructionExceptionRegion> DestinationContext { get; }
    public ImmutableArray<InstructionExceptionRegion> RegionsLeft { get; }
    public ImmutableArray<InstructionCleanupHandler> CleanupHandlers { get; }
    public ImmutableArray<InstructionExceptionRegion> RegionsEntered { get; }
    public NormalContinuationId Continuation { get; }
}

/// <summary>Typed reason an Instructions exception-flow fact was unavailable.</summary>
public enum InstructionExceptionFlowUnavailableReason
{
    MissingMetadataEvidence,
    DecodeFailure,
    BodyIdentityMismatch,
    InvalidRegionBoundary,
    InvalidRegionTopology,
    InvalidControlTransfer,
    NotInstructionBoundary,
    InstructionMismatch,
    UnsupportedTransfer,
    UnknownDestination,
    BlockMismatch,
}

/// <summary>Closed result for Instructions exception-flow construction and queries.</summary>
public abstract record InstructionExceptionFlowResult<T>
{
    private protected InstructionExceptionFlowResult()
    {
    }

    public sealed record Available : InstructionExceptionFlowResult<T>
    {
        internal Available(T value)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));
            Value = value;
        }

        public T Value { get; }
    }

    public sealed record Unavailable : InstructionExceptionFlowResult<T>
    {
        internal Unavailable(
            InstructionExceptionFlowUnavailableReason reason,
            string detail)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(detail);
            Reason = reason;
            Detail = detail;
        }

        public InstructionExceptionFlowUnavailableReason Reason { get; }
        public string Detail { get; }
    }

    public sealed record Ambiguous : InstructionExceptionFlowResult<T>
    {
        internal Ambiguous(ImmutableArray<T> candidates, string detail)
        {
            if (candidates.IsDefaultOrEmpty)
                throw new ArgumentException("An ambiguous result must preserve its candidates.", nameof(candidates));
            ArgumentException.ThrowIfNullOrWhiteSpace(detail);
            Candidates = candidates;
            Detail = detail;
        }

        public ImmutableArray<T> Candidates { get; }
        public string Detail { get; }
    }
}

/// <summary>
/// Validated decoded-IL topology and normal-transfer facts for one Metadata body observation.
/// </summary>
public sealed class InstructionExceptionFlowFacts
{
    readonly ImmutableArray<DecodedInstruction> _instructions;
    readonly BlockGraph _blocks;
    readonly ExceptionFlowTopology _topology;
    readonly ImmutableDictionary<int, InstructionExceptionRegion> _regionsByOrdinal;

    internal InstructionExceptionFlowFacts(
        MethodBodyEvidenceId body,
        ImmutableArray<InstructionExceptionClause> clauses,
        ImmutableArray<InstructionExceptionRegion> regions,
        ImmutableArray<DecodedInstruction> instructions,
        BlockGraph blocks,
        ExceptionFlowTopology topology)
    {
        Body = body;
        Clauses = clauses;
        Regions = regions;
        _instructions = instructions;
        _blocks = blocks;
        _topology = topology;
        _regionsByOrdinal = regions.ToImmutableDictionary(region => region.Id.Ordinal);
    }

    public MethodBodyEvidenceId Body { get; }
    public ImmutableArray<InstructionExceptionClause> Clauses { get; }
    public ImmutableArray<InstructionExceptionRegion> Regions { get; }

    /// <summary>
    /// Returns the outer-to-inner validated regions containing an admitted instruction offset.
    /// </summary>
    public InstructionExceptionFlowResult<ImmutableArray<InstructionExceptionRegion>>
        LocationAt(int instructionOffset)
    {
        if (InstructionAt(instructionOffset) is null)
        {
            return new InstructionExceptionFlowResult<
                ImmutableArray<InstructionExceptionRegion>>.Unavailable(
                    InstructionExceptionFlowUnavailableReason.NotInstructionBoundary,
                    $"IL_{instructionOffset:X4} is not an admitted instruction boundary.");
        }

        return new InstructionExceptionFlowResult<
            ImmutableArray<InstructionExceptionRegion>>.Available(
                TypedContextAt(instructionOffset));
    }

    /// <summary>Returns the context for an instruction from this body observation.</summary>
    public InstructionExceptionFlowResult<ImmutableArray<InstructionExceptionRegion>>
        LocationAt(DecodedInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        if (!ReferenceEquals(InstructionAt(instruction.Offset), instruction))
        {
            return new InstructionExceptionFlowResult<
                ImmutableArray<InstructionExceptionRegion>>.Unavailable(
                    InstructionExceptionFlowUnavailableReason.InstructionMismatch,
                    "The instruction does not belong to this method-body observation.");
        }

        return new InstructionExceptionFlowResult<
            ImmutableArray<InstructionExceptionRegion>>.Available(
                TypedContextAt(instruction.Offset));
    }

    /// <summary>Returns the context shared by every instruction in an owner-issued block.</summary>
    public InstructionExceptionFlowResult<ImmutableArray<InstructionExceptionRegion>>
        LocationAt(InstructionBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        if ((uint)block.Index >= (uint)_blocks.Blocks.Length
            || !ReferenceEquals(_blocks.Blocks[block.Index], block))
        {
            return new InstructionExceptionFlowResult<
                ImmutableArray<InstructionExceptionRegion>>.Unavailable(
                    InstructionExceptionFlowUnavailableReason.BlockMismatch,
                    "The block does not belong to this method-body observation.");
        }

        ImmutableArray<InstructionExceptionRegion> context = TypedContextAt(block.Start);
        foreach (DecodedInstruction instruction in _instructions)
        {
            if (instruction.Offset < block.Start)
                continue;
            if (instruction.Offset >= block.End)
                break;
            if (!TypedContextAt(instruction.Offset).SequenceEqual(context))
            {
                return new InstructionExceptionFlowResult<
                    ImmutableArray<InstructionExceptionRegion>>.Unavailable(
                        InstructionExceptionFlowUnavailableReason.BlockMismatch,
                        "The block crosses validated exception-region contexts.");
            }
        }

        return new InstructionExceptionFlowResult<
            ImmutableArray<InstructionExceptionRegion>>.Available(context);
    }

    /// <summary>
    /// Returns facts for one encoded branch/leave edge, or one direct return when
    /// <paramref name="logicalDestinationOffset"/> is null.
    /// </summary>
    public InstructionExceptionFlowResult<InstructionNormalTransfer> NormalTransferAt(
        int sourceOffset,
        int? logicalDestinationOffset)
    {
        DecodedInstruction? source = InstructionAt(sourceOffset);
        if (source is null)
        {
            return Unavailable(
                InstructionExceptionFlowUnavailableReason.NotInstructionBoundary,
                $"IL_{sourceOffset:X4} is not an admitted instruction boundary.");
        }

        if (!_topology.TryValidateNormalTransfer(
                source,
                logicalDestinationOffset,
                out InstructionNormalTransferKind kind,
                out InstructionExceptionFlowUnavailableReason reason,
                out string detail))
        {
            return Unavailable(reason, detail);
        }

        ImmutableArray<InstructionExceptionRegion> sourceContext =
            TypedContextAt(sourceOffset);
        ImmutableArray<InstructionExceptionRegion> destinationContext =
            logicalDestinationOffset is { } destination
                ? TypedContextAt(destination)
                : [];

        var destinationIds = destinationContext
            .Select(region => region.Id)
            .ToHashSet();
        var sourceIds = sourceContext
            .Select(region => region.Id)
            .ToHashSet();

        ImmutableArray<InstructionExceptionRegion> regionsLeft =
            [.. sourceContext
                .Where(region => !destinationIds.Contains(region.Id))
                .Reverse()];
        ImmutableArray<InstructionExceptionRegion> regionsEntered =
            [.. destinationContext
                .Where(region => !sourceIds.Contains(region.Id))];

        ImmutableArray<InstructionCleanupHandler> cleanupHandlers =
            [.. regionsLeft
                .Where(region => region.Id.Role == InstructionExceptionRegionRole.Protected)
                .SelectMany(region => Clauses
                    .Where(clause => clause.ProtectedRegion == region.Id
                        && clause.Kind == ExceptionRegionKind.Finally)
                    .OrderBy(clause => clause.Id.Ordinal)
                    .Select(clause => new InstructionCleanupHandler(
                        clause.Id,
                        clause.HandlerRegion)))];

        NormalContinuationId continuation;
        if (logicalDestinationOffset is { } target)
        {
            int blockIndex = _blocks.BlockIndexAt(target);
            if (blockIndex < 0 || _blocks.Blocks[blockIndex].Start != target)
            {
                return Unavailable(
                    InstructionExceptionFlowUnavailableReason.UnknownDestination,
                    $"IL_{target:X4} is not a canonical destination block.");
            }

            continuation = new NormalContinuationId(
                Body,
                NormalContinuationKind.Block,
                target);
        }
        else
        {
            continuation = new NormalContinuationId(
                Body,
                NormalContinuationKind.MethodExit,
                blockStart: -1);
        }

        return new InstructionExceptionFlowResult<InstructionNormalTransfer>.Available(
            new InstructionNormalTransfer(
                kind,
                sourceOffset,
                logicalDestinationOffset,
                sourceContext,
                destinationContext,
                regionsLeft,
                cleanupHandlers,
                regionsEntered,
                continuation));
    }

    ImmutableArray<InstructionExceptionRegion> TypedContextAt(int offset) =>
        [.. _topology.ContextAt(offset)
            .Select(region => _regionsByOrdinal[region.Ordinal])];

    DecodedInstruction? InstructionAt(int offset)
    {
        int lo = 0;
        int hi = _instructions.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int current = _instructions[mid].Offset;
            if (current == offset)
                return _instructions[mid];
            if (current < offset)
                lo = mid + 1;
            else
                hi = mid - 1;
        }

        return null;
    }

    static InstructionExceptionFlowResult<InstructionNormalTransfer>.Unavailable
        Unavailable(
            InstructionExceptionFlowUnavailableReason reason,
            string detail) => new(reason, detail);
}

internal readonly record struct ExceptionFlowExtent(int Start, int End)
{
    internal int Length => End - Start;
    internal bool Contains(int offset) => offset >= Start && offset < End;
    internal bool Contains(ExceptionFlowExtent other) =>
        Start <= other.Start && End >= other.End;
    internal bool Overlaps(ExceptionFlowExtent other) =>
        Start < other.End && other.Start < End;
}

internal sealed record ExceptionFlowTopologyRegion(
    int Ordinal,
    InstructionExceptionRegionRole Role,
    ExceptionFlowExtent Extent,
    ImmutableArray<int> ClauseOrdinals);

internal sealed record ExceptionFlowClauseDescriptor(
    int Ordinal,
    ExceptionRegionKind Kind,
    ExceptionFlowExtent Protected,
    ExceptionFlowExtent Handler,
    ExceptionFlowExtent? Filter,
    MethodExceptionClause? Clause);

internal sealed class ExceptionFlowTopology
{
    ExceptionFlowTopology(
        ImmutableArray<ExceptionFlowClauseDescriptor> clauses,
        ImmutableArray<ExceptionRegionModel> models,
        ImmutableArray<ExceptionFlowTopologyRegion> regions,
        ImmutableArray<InstructionExceptionClause> issuedClauses,
        ImmutableArray<InstructionExceptionRegion> issuedRegions,
        InstructionExceptionFlowUnavailableReason? unavailableReason,
        string? incompleteReason,
        string? blockGraphIncompleteReason)
    {
        Clauses = clauses;
        Models = models;
        Regions = regions;
        IssuedClauses = issuedClauses;
        IssuedRegions = issuedRegions;
        UnavailableReason = unavailableReason;
        IncompleteReason = incompleteReason;
        BlockGraphIncompleteReason = blockGraphIncompleteReason;
    }

    internal ImmutableArray<ExceptionFlowClauseDescriptor> Clauses { get; }
    internal ImmutableArray<ExceptionRegionModel> Models { get; }
    internal ImmutableArray<ExceptionFlowTopologyRegion> Regions { get; }
    internal ImmutableArray<InstructionExceptionClause> IssuedClauses { get; }
    internal ImmutableArray<InstructionExceptionRegion> IssuedRegions { get; }
    internal InstructionExceptionFlowUnavailableReason? UnavailableReason { get; }
    internal string? IncompleteReason { get; }
    internal string? BlockGraphIncompleteReason { get; }
    internal bool IsComplete => IncompleteReason is null;

    internal static ExceptionFlowTopology Create(
        int ilLength,
        ImmutableArray<DecodedInstruction> instructions,
        IReadOnlyCollection<ExceptionRegion> regions)
    {
        var descriptors = ImmutableArray.CreateBuilder<ExceptionFlowClauseDescriptor>();
        int ordinal = 0;
        foreach (ExceptionRegion region in regions)
        {
            if (!TryExtent(
                    region.TryOffset,
                    region.TryLength,
                    ilLength,
                    out var protectedExtent))
            {
                return Invalid(
                    InstructionExceptionFlowUnavailableReason.InvalidRegionBoundary,
                    "Exception try region has unsupported boundaries.");
            }
            if (!TryExtent(
                    region.HandlerOffset,
                    region.HandlerLength,
                    ilLength,
                    out var handlerExtent))
            {
                return Invalid(
                    InstructionExceptionFlowUnavailableReason.InvalidRegionBoundary,
                    "Exception handler region has unsupported boundaries.");
            }

            ExceptionFlowExtent? filterExtent = null;
            if (region.Kind == ExceptionRegionKind.Filter)
            {
                if (region.FilterOffset < 0 || region.FilterOffset >= region.HandlerOffset)
                {
                    return Invalid(
                        InstructionExceptionFlowUnavailableReason.InvalidRegionBoundary,
                        "Filter region has an empty or reversed extent.");
                }
                filterExtent = new ExceptionFlowExtent(region.FilterOffset, region.HandlerOffset);
            }

            descriptors.Add(new ExceptionFlowClauseDescriptor(
                ordinal++,
                region.Kind,
                protectedExtent,
                handlerExtent,
                filterExtent,
                Clause: null));
        }

        return Build(
            ilLength,
            instructions,
            descriptors.ToImmutable(),
            body: null);
    }

    internal static ExceptionFlowTopology Create(
        int ilLength,
        ImmutableArray<DecodedInstruction> instructions,
        MethodBodyData body)
    {
        MethodExceptionRegionCatalog catalog = body.ExceptionRegionCatalog;
        if (catalog.Body != body.EvidenceId
            || catalog.Clauses.Any(clause => clause.Id.Body != body.EvidenceId))
        {
            return Invalid(
                InstructionExceptionFlowUnavailableReason.BodyIdentityMismatch,
                "Method body and clause identities do not describe one observation.");
        }

        ImmutableArray<ExceptionFlowClauseDescriptor> descriptors =
            [.. catalog.Clauses.Select(clause => new ExceptionFlowClauseDescriptor(
                clause.Id.Ordinal,
                clause.Kind,
                new ExceptionFlowExtent(
                    clause.ProtectedExtent.Start,
                    clause.ProtectedExtent.End),
                new ExceptionFlowExtent(
                    clause.HandlerExtent.Start,
                    clause.HandlerExtent.End),
                clause.FilterExtent is { } filter
                    ? new ExceptionFlowExtent(filter.Start, filter.End)
                    : null,
                clause))];

        return Build(
            ilLength,
            instructions,
            descriptors,
            body.EvidenceId);
    }

    static ExceptionFlowTopology Build(
        int ilLength,
        ImmutableArray<DecodedInstruction> instructions,
        ImmutableArray<ExceptionFlowClauseDescriptor> clauses,
        MethodBodyEvidenceId? body)
    {
        HashSet<int> boundaries = AdmittedBoundaries(instructions, ilLength);
        foreach (ExceptionFlowClauseDescriptor clause in clauses)
        {
            if (!IsSupportedKind(clause.Kind)
                || !IsBoundary(clause.Protected)
                || !IsBoundary(clause.Handler)
                || clause.Filter is { } filter && !IsBoundary(filter))
            {
                return Invalid(
                    InstructionExceptionFlowUnavailableReason.InvalidRegionBoundary,
                    "Exception regions must use supported kinds and whole-instruction boundaries.");
            }

            if (clause.Protected.Overlaps(clause.Handler)
                || clause.Filter is { } ownFilter
                    && (clause.Protected.Overlaps(ownFilter)
                        || ownFilter.Overlaps(clause.Handler)))
            {
                return Invalid(
                    InstructionExceptionFlowUnavailableReason.InvalidRegionTopology,
                    "A clause's protected, filter, and handler extents must be disjoint.");
            }
        }

        var protectedGroups = clauses
            .GroupBy(clause => clause.Protected)
            .OrderBy(group => group.Min(clause => clause.Ordinal))
            .ToArray();
        foreach (IGrouping<ExceptionFlowExtent, ExceptionFlowClauseDescriptor> group in protectedGroups)
        {
            ExceptionFlowClauseDescriptor[] groupClauses =
                [.. group.OrderBy(clause => clause.Ordinal)];
            if (groupClauses.Length > 1
                && groupClauses.Any(clause =>
                    clause.Kind is ExceptionRegionKind.Finally or ExceptionRegionKind.Fault))
            {
                return Invalid(
                    InstructionExceptionFlowUnavailableReason.InvalidRegionTopology,
                    "A shared protected extent cannot combine finally or fault with another clause.");
            }
        }

        for (int left = 0; left < protectedGroups.Length; left++)
        {
            ExceptionFlowExtent leftExtent = protectedGroups[left].Key;
            int leftFirst = protectedGroups[left].Min(clause => clause.Ordinal);
            int leftLast = protectedGroups[left].Max(clause => clause.Ordinal);
            for (int right = left + 1; right < protectedGroups.Length; right++)
            {
                ExceptionFlowExtent rightExtent = protectedGroups[right].Key;
                int rightFirst = protectedGroups[right].Min(clause => clause.Ordinal);
                int rightLast = protectedGroups[right].Max(clause => clause.Ordinal);
                if (leftExtent.Contains(rightExtent)
                    && leftExtent != rightExtent
                    && rightLast >= leftFirst
                    || rightExtent.Contains(leftExtent)
                    && rightExtent != leftExtent
                    && leftLast >= rightFirst)
                {
                    return Invalid(
                        InstructionExceptionFlowUnavailableReason.InvalidRegionTopology,
                        "Nested protected extents must preserve inner-before-outer metadata order.");
                }
            }
        }

        var topologyRegions = ImmutableArray.CreateBuilder<ExceptionFlowTopologyRegion>();
        var protectedOrdinals = new Dictionary<ExceptionFlowExtent, int>();
        var handlerOrdinals = new Dictionary<int, int>();
        var filterOrdinals = new Dictionary<int, int>();
        int regionOrdinal = 0;
        foreach (ExceptionFlowClauseDescriptor clause in clauses.OrderBy(clause => clause.Ordinal))
        {
            if (!protectedOrdinals.ContainsKey(clause.Protected))
            {
                ImmutableArray<int> groupOrdinals =
                    [.. clauses
                        .Where(candidate => candidate.Protected == clause.Protected)
                        .OrderBy(candidate => candidate.Ordinal)
                        .Select(candidate => candidate.Ordinal)];
                protectedOrdinals.Add(clause.Protected, regionOrdinal);
                topologyRegions.Add(new ExceptionFlowTopologyRegion(
                    regionOrdinal++,
                    InstructionExceptionRegionRole.Protected,
                    clause.Protected,
                    groupOrdinals));
            }

            if (clause.Filter is { } filter)
            {
                filterOrdinals.Add(clause.Ordinal, regionOrdinal);
                topologyRegions.Add(new ExceptionFlowTopologyRegion(
                    regionOrdinal++,
                    InstructionExceptionRegionRole.Filter,
                    filter,
                    [clause.Ordinal]));
            }

            handlerOrdinals.Add(clause.Ordinal, regionOrdinal);
            topologyRegions.Add(new ExceptionFlowTopologyRegion(
                regionOrdinal++,
                InstructionExceptionRegionRole.Handler,
                clause.Handler,
                [clause.Ordinal]));
        }

        ImmutableArray<ExceptionFlowTopologyRegion> topology = topologyRegions.ToImmutable();
        for (int left = 0; left < topology.Length; left++)
        {
            for (int right = left + 1; right < topology.Length; right++)
            {
                ExceptionFlowExtent a = topology[left].Extent;
                ExceptionFlowExtent b = topology[right].Extent;
                if (!a.Overlaps(b))
                    continue;
                if (a == b || !a.Contains(b) && !b.Contains(a))
                {
                    return Invalid(
                        InstructionExceptionFlowUnavailableReason.InvalidRegionTopology,
                        "Exception regions must form a strict non-crossing topology.");
                }
            }
        }

        foreach (ExceptionFlowClauseDescriptor clause in clauses)
        {
            var components =
                ImmutableArray.CreateBuilder<ExceptionFlowTopologyRegion>(3);
            components.Add(topology[protectedOrdinals[clause.Protected]]);
            if (clause.Filter is not null)
                components.Add(topology[filterOrdinals[clause.Ordinal]]);
            components.Add(topology[handlerOrdinals[clause.Ordinal]]);
            ImmutableArray<int> enclosingContext =
                EnclosingRegionOrdinals(components[0]);
            if (components.Skip(1).Any(
                    component => !EnclosingRegionOrdinals(component)
                        .SequenceEqual(enclosingContext)))
            {
                return Invalid(
                    InstructionExceptionFlowUnavailableReason.InvalidRegionTopology,
                    "A clause's protected, filter, and handler extents must share one enclosing context.");
            }
        }

        ImmutableArray<ExceptionRegionModel> models =
            [.. clauses
                .OrderBy(clause => clause.Ordinal)
                .Select(clause => new ExceptionRegionModel(
                    ToHandlerKind(clause.Kind),
                    clause.Protected.Start,
                    clause.Protected.End,
                    clause.Handler.Start,
                    clause.Handler.End,
                    clause.Filter?.Start ?? -1,
                    clause.Filter?.End ?? -1))];

        ImmutableArray<InstructionExceptionRegion> issuedRegions = [];
        ImmutableArray<InstructionExceptionClause> issuedClauses = [];
        if (body is { } evidence)
        {
            var issuedRegionBuilder =
                ImmutableArray.CreateBuilder<InstructionExceptionRegion>(topology.Length);
            foreach (ExceptionFlowTopologyRegion region in topology)
            {
                MethodExceptionClause firstClause =
                    clauses[region.ClauseOrdinals[0]].Clause!;
                MethodBodyExtent extent = region.Role switch
                {
                    InstructionExceptionRegionRole.Protected =>
                        firstClause.ProtectedExtent,
                    InstructionExceptionRegionRole.Filter =>
                        firstClause.FilterExtent!.Value,
                    _ => firstClause.HandlerExtent,
                };
                issuedRegionBuilder.Add(new InstructionExceptionRegion(
                    new InstructionExceptionRegionId(
                        evidence,
                        region.Role,
                        region.Ordinal),
                    extent,
                    [.. region.ClauseOrdinals.Select(ordinal => clauses[ordinal].Clause!.Id)]));
            }
            issuedRegions = issuedRegionBuilder.MoveToImmutable();

            var issuedClauseBuilder =
                ImmutableArray.CreateBuilder<InstructionExceptionClause>(clauses.Length);
            foreach (ExceptionFlowClauseDescriptor clause in clauses.OrderBy(clause => clause.Ordinal))
            {
                issuedClauseBuilder.Add(new InstructionExceptionClause(
                    clause.Clause!,
                    issuedRegions[protectedOrdinals[clause.Protected]].Id,
                    issuedRegions[handlerOrdinals[clause.Ordinal]].Id,
                    filterOrdinals.TryGetValue(clause.Ordinal, out int filterOrdinal)
                        ? issuedRegions[filterOrdinal].Id
                        : null));
            }
            issuedClauses = issuedClauseBuilder.MoveToImmutable();
        }

        var result = new ExceptionFlowTopology(
            clauses,
            models,
            topology,
            issuedClauses,
            issuedRegions,
            unavailableReason: null,
            incompleteReason: null,
            blockGraphIncompleteReason: null);

        if (!result.TryValidateAllNormalTransfers(
                instructions,
                ilLength,
                boundaries,
                out InstructionExceptionFlowUnavailableReason transferUnavailableReason,
                out string transferReason))
        {
            return new ExceptionFlowTopology(
                clauses,
                models,
                topology,
                issuedClauses: [],
                issuedRegions: [],
                transferUnavailableReason,
                transferReason,
                blockGraphIncompleteReason: null);
        }

        return result;

        bool IsBoundary(ExceptionFlowExtent extent) =>
            extent.Start < extent.End
            && boundaries.Contains(extent.Start)
            && boundaries.Contains(extent.End);

        ImmutableArray<int> EnclosingRegionOrdinals(
            ExceptionFlowTopologyRegion component) =>
            [.. topology
                .Where(candidate =>
                    candidate.Extent != component.Extent
                    && candidate.Extent.Contains(component.Extent))
                .Select(candidate => candidate.Ordinal)
                .Order()];
    }

    internal ImmutableArray<ExceptionFlowTopologyRegion> ContextAt(int offset) =>
        [.. Regions
            .Where(region => region.Extent.Contains(offset))
            .OrderByDescending(region => region.Extent.Length)
            .ThenBy(region => region.Extent.Start)
            .ThenBy(region => region.Ordinal)];

    internal ImmutableArray<ExceptionRegionModel> CleanupModels(int sourceOffset, int targetOffset) =>
        [.. Models
            .Where(region => region.Kind == HandlerKind.Finally
                && region.ContainsTry(sourceOffset)
                && !region.ContainsTry(targetOffset))
            .OrderBy(region => region.TryEnd - region.TryStart)
            .ThenByDescending(region => region.TryStart)];

    internal bool TryValidateNormalTransfer(
        DecodedInstruction source,
        int? destinationOffset,
        out InstructionNormalTransferKind kind,
        out InstructionExceptionFlowUnavailableReason reason,
        out string detail) =>
        TryValidateNormalTransfer(
            source,
            destinationOffset,
            isSequentialFallthrough: destinationOffset is { } destination
                && source.FallsThrough
                && source.NextOffset == destination
                && !source.BranchTargets.Contains(destination),
            out kind,
            out reason,
            out detail);

    bool TryValidateNormalTransfer(
        DecodedInstruction source,
        int? destinationOffset,
        bool isSequentialFallthrough,
        out InstructionNormalTransferKind kind,
        out InstructionExceptionFlowUnavailableReason reason,
        out string detail)
    {
        kind = default;
        reason = InstructionExceptionFlowUnavailableReason.UnsupportedTransfer;
        detail = $"IL_{source.Offset:X4} is not a supported branch, leave, or return transfer.";

        if (source.OpCode == ILOpCode.Ret)
        {
            kind = InstructionNormalTransferKind.Return;
            if (destinationOffset is not null)
            {
                reason = InstructionExceptionFlowUnavailableReason.UnknownDestination;
                detail = "A direct return has the method-exit destination.";
                return false;
            }
            if (!ContextAt(source.Offset).IsEmpty)
            {
                reason = InstructionExceptionFlowUnavailableReason.InvalidControlTransfer;
                detail = "A direct return cannot originate inside an exception region.";
                return false;
            }
            return true;
        }

        if (!source.Branches || destinationOffset is not { } destination)
            return false;

        bool encodedTarget = source.BranchTargets.Contains(destination)
            || source.FallsThrough && source.NextOffset == destination;
        if (!encodedTarget)
        {
            reason = InstructionExceptionFlowUnavailableReason.UnknownDestination;
            detail = $"IL_{destination:X4} is not an encoded successor of IL_{source.Offset:X4}.";
            return false;
        }

        ImmutableArray<ExceptionFlowTopologyRegion> sourceContext =
            ContextAt(source.Offset);
        ImmutableArray<ExceptionFlowTopologyRegion> destinationContext =
            ContextAt(destination);

        if (source.LeavesRegion)
        {
            kind = InstructionNormalTransferKind.Leave;
            if (!IsLegalLeave(sourceContext, destinationContext))
            {
                reason = InstructionExceptionFlowUnavailableReason.InvalidControlTransfer;
                detail = "The leave crosses an unsupported exception-region boundary.";
                return false;
            }
            return true;
        }

        kind = InstructionNormalTransferKind.Branch;
        if (SameContext(sourceContext, destinationContext)
            || isSequentialFallthrough
                && IsLegalRegionEntry(sourceContext, destinationContext))
        {
            return true;
        }

        reason = InstructionExceptionFlowUnavailableReason.InvalidControlTransfer;
        detail = "The branch crosses an exception-region boundary that requires leave.";
        return false;
    }

    bool TryValidateAllNormalTransfers(
        ImmutableArray<DecodedInstruction> instructions,
        int ilLength,
        IReadOnlySet<int> admittedBoundaries,
        out InstructionExceptionFlowUnavailableReason unavailableReason,
        out string reason)
    {
        foreach (DecodedInstruction instruction in instructions)
        {
            if (instruction.OpCode == ILOpCode.Ret)
            {
                if (!TryValidateNormalTransfer(
                        instruction,
                        destinationOffset: null,
                        out _,
                        out unavailableReason,
                        out reason))
                    return false;
                continue;
            }

            if (instruction.Branches)
            {
                foreach (int target in instruction.BranchTargets)
                {
                    if (target < 0
                        || target >= ilLength
                        || !admittedBoundaries.Contains(target))
                    {
                        unavailableReason =
                            InstructionExceptionFlowUnavailableReason.UnknownDestination;
                        reason =
                            $"Branch target IL_{target:X4} is not an admitted destination.";
                        return false;
                    }
                    if (!TryValidateNormalTransfer(
                            instruction,
                            target,
                            isSequentialFallthrough: false,
                            out _,
                            out unavailableReason,
                            out reason))
                        return false;
                }
            }

            if (!instruction.Exits
                && !instruction.IsUnconditionalBranch
                && instruction.NextOffset < ilLength)
            {
                ImmutableArray<ExceptionFlowTopologyRegion> source =
                    ContextAt(instruction.Offset);
                ImmutableArray<ExceptionFlowTopologyRegion> destination =
                    ContextAt(instruction.NextOffset);
                if (!SameContext(source, destination)
                    && !IsLegalRegionEntry(source, destination))
                {
                    unavailableReason =
                        InstructionExceptionFlowUnavailableReason.InvalidControlTransfer;
                    reason =
                        $"Fallthrough at IL_{instruction.Offset:X4} crosses an unsupported exception-region boundary.";
                    return false;
                }
            }
        }

        unavailableReason =
            InstructionExceptionFlowUnavailableReason.InvalidControlTransfer;
        reason = "";
        return true;
    }

    bool IsLegalLeave(
        ImmutableArray<ExceptionFlowTopologyRegion> source,
        ImmutableArray<ExceptionFlowTopologyRegion> destination)
    {
        if (source.IsEmpty)
            return false;
        if (source.Any(region => region.Role == InstructionExceptionRegionRole.Filter))
            return false;

        HashSet<int> destinationOrdinals =
            destination.Select(region => region.Ordinal).ToHashSet();
        ImmutableArray<ExceptionFlowTopologyRegion> regionsLeft =
            [.. source.Where(region => !destinationOrdinals.Contains(region.Ordinal))];
        foreach (ExceptionFlowTopologyRegion handler in regionsLeft.Where(
                     region => region.Role == InstructionExceptionRegionRole.Handler))
        {
            ExceptionRegionKind kind = Clauses[handler.ClauseOrdinals[0]].Kind;
            if (kind is ExceptionRegionKind.Finally or ExceptionRegionKind.Fault)
                return false;
        }

        HashSet<int> sourceOrdinals =
            source.Select(region => region.Ordinal).ToHashSet();
        ImmutableArray<ExceptionFlowTopologyRegion> regionsEntered =
            [.. destination.Where(region => !sourceOrdinals.Contains(region.Ordinal))];
        if (regionsEntered.Any(region =>
                region.Role is InstructionExceptionRegionRole.Filter
                    or InstructionExceptionRegionRole.Handler))
            return false;

        HashSet<int> sourceProtected =
            source.Where(region => region.Role == InstructionExceptionRegionRole.Protected)
                .Select(region => region.Ordinal)
                .ToHashSet();
        return regionsEntered
            .Where(region => region.Role == InstructionExceptionRegionRole.Protected)
            .All(region => sourceProtected.Contains(region.Ordinal)
                || IsAssociatedHandlerProtectedRegion(source, region));
    }

    bool IsAssociatedHandlerProtectedRegion(
        ImmutableArray<ExceptionFlowTopologyRegion> source,
        ExceptionFlowTopologyRegion destinationProtected) =>
        source
            .Where(region => region.Role == InstructionExceptionRegionRole.Handler)
            .SelectMany(region => region.ClauseOrdinals)
            .Select(ordinal => Clauses[ordinal])
            .Any(clause => clause.Kind is
                    ExceptionRegionKind.Catch or ExceptionRegionKind.Filter
                && clause.Protected == destinationProtected.Extent);

    static bool IsLegalRegionEntry(
        ImmutableArray<ExceptionFlowTopologyRegion> source,
        ImmutableArray<ExceptionFlowTopologyRegion> destination)
    {
        if (source.Length >= destination.Length)
            return false;
        for (int index = 0; index < source.Length; index++)
        {
            if (source[index].Ordinal != destination[index].Ordinal)
                return false;
        }
        return destination[source.Length..].All(
            region => region.Role == InstructionExceptionRegionRole.Protected);
    }

    static bool SameContext(
        ImmutableArray<ExceptionFlowTopologyRegion> left,
        ImmutableArray<ExceptionFlowTopologyRegion> right) =>
        left.Select(region => region.Ordinal)
            .SequenceEqual(right.Select(region => region.Ordinal));

    static HashSet<int> AdmittedBoundaries(
        ImmutableArray<DecodedInstruction> instructions,
        int ilLength)
    {
        var boundaries = new HashSet<int> { ilLength };
        for (int index = 0; index < instructions.Length; index++)
        {
            if (index == 0 || !instructions[index - 1].OpCode.IsPrefix())
                boundaries.Add(instructions[index].Offset);
        }
        return boundaries;
    }

    static bool TryExtent(
        int start,
        int length,
        int ilLength,
        out ExceptionFlowExtent extent)
    {
        long end = (long)start + length;
        if (start < 0 || length <= 0 || end > ilLength)
        {
            extent = default;
            return false;
        }

        extent = new ExceptionFlowExtent(start, (int)end);
        return true;
    }

    static bool IsSupportedKind(ExceptionRegionKind kind) =>
        kind is ExceptionRegionKind.Catch
            or ExceptionRegionKind.Filter
            or ExceptionRegionKind.Finally
            or ExceptionRegionKind.Fault;

    static HandlerKind ToHandlerKind(ExceptionRegionKind kind) => kind switch
    {
        ExceptionRegionKind.Catch => HandlerKind.Catch,
        ExceptionRegionKind.Filter => HandlerKind.Filter,
        ExceptionRegionKind.Finally => HandlerKind.Finally,
        ExceptionRegionKind.Fault => HandlerKind.Fault,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    static ExceptionFlowTopology Invalid(
        InstructionExceptionFlowUnavailableReason reason,
        string detail) => new(
            clauses: [],
            models: [],
            regions: [],
            issuedClauses: [],
            issuedRegions: [],
            reason,
            detail,
            blockGraphIncompleteReason: detail);
}
