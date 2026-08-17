using System.Collections.Immutable;
using System.Text;

namespace ILInspector.Analysis;

public static partial class StructuralCloneAnalysis
{
    static StructuralCloneComparison AlignNear(
        StructuralCloneBodyFacts left,
        StructuralCloneBodyFacts right,
        StructuralCloneComparisonLimits limits,
        StructuralCloneComparison different)
    {
        if (left.Signature != right.Signature
            || left.InitLocals != right.InitLocals
            || !SameLocalTypes(left.Locals, right.Locals))
        {
            return different;
        }

        List<NearCandidate> candidates = BuildNearCandidates(
            left,
            right,
            limits,
            out StructuralCloneBlockerKind? enumerationLimit);
        if (enumerationLimit is { } enumerationLimitKind)
        {
            return NearLimit(
                left,
                right,
                different,
                enumerationLimitKind,
                enumerationLimitKind
                    == StructuralCloneBlockerKind.NearBlockElementLimit
                    ? $"Near alignment found a removable block affecting more "
                        + $"than {limits.MaximumNearBlockElements} elements."
                    : $"Near alignment requires more than "
                        + $"{limits.MaximumNearAlignmentCandidates} candidates.",
                candidates.Count,
                verificationSteps: 0);
        }

        var alternatives =
            new SortedDictionary<string, StructuralCloneAlignmentAlternative>(
                StringComparer.Ordinal);
        int verificationSteps = 0;
        foreach (NearCandidate candidate in candidates)
        {
            int remaining =
                limits.MaximumNearAlignmentVerificationSteps
                - verificationSteps;
            if (remaining < 1)
            {
                return NearLimit(
                    left,
                    right,
                    different,
                    StructuralCloneBlockerKind
                        .NearAlignmentVerificationStepLimit,
                    $"Near alignment reached "
                        + $"{limits.MaximumNearAlignmentVerificationSteps} "
                        + "verification steps.",
                    candidates.Count,
                    verificationSteps);
            }

            (StructuralCloneBodyFacts candidateLeft,
                StructuralCloneBodyFacts candidateRight) =
                ApplyCandidate(left, right, candidate);
            StructuralCloneComparison exact = CompareExact(
                candidateLeft,
                candidateRight,
                limits with
                {
                    MaximumVerificationSteps = Math.Min(
                        limits.MaximumVerificationSteps,
                        remaining),
                });
            verificationSteps = checked(
                verificationSteps + exact.Receipt.SearchSteps);
            if (exact.Disposition
                == StructuralCloneDisposition.LimitReached)
            {
                return NearLimit(
                    left,
                    right,
                    different,
                    StructuralCloneBlockerKind
                        .NearAlignmentVerificationStepLimit,
                    "Near alignment could not exhaust an exact-restoring "
                        + "candidate within its verification bounds.",
                    candidates.Count,
                    verificationSteps);
            }
            if (exact.Disposition
                    != StructuralCloneDisposition.Completed
                || exact.Relation != StructuralCloneRelation.Exact)
            {
                continue;
            }

            StructuralCloneAlignmentAlternative? alternative =
                BuildAlternative(left, right, candidate, exact);
            if (alternative is null)
                continue;
            alternatives.TryAdd(
                CandidateKey(candidate),
                alternative);
            if (alternatives.Count
                > limits.MaximumNearAlignmentAlternatives)
            {
                return NearLimit(
                    left,
                    right,
                    different,
                    StructuralCloneBlockerKind
                        .NearAlignmentAlternativeLimit,
                    $"Near alignment produced more than "
                        + $"{limits.MaximumNearAlignmentAlternatives} "
                        + "complete alternatives.",
                    candidates.Count,
                    verificationSteps);
            }
        }

        var receipt = new StructuralCloneAlignmentReceipt(
            candidates.Count,
            verificationSteps,
            Exhausted: true);
        if (alternatives.Count == 0)
        {
            return StructuralCloneComparison.Completed(
                left.Method,
                right.Method,
                StructuralCloneRelation.Different,
                correspondence: null,
                different.Receipt,
                alignmentReceipt: receipt);
        }

        ImmutableArray<StructuralCloneAlignmentAlternative> complete =
            [.. alternatives.Values];
        bool ambiguous =
            complete.Length > 1
            || complete.Any(static alternative =>
                alternative.Correspondence.Kind
                    == StructuralCloneCorrespondenceKind.Ambiguous);
        var alignment = new StructuralCloneAlignment(
            ambiguous
                ? StructuralCloneCorrespondenceKind.Ambiguous
                : StructuralCloneCorrespondenceKind.Unique,
            complete,
            receipt);
        return StructuralCloneComparison.Completed(
            left.Method,
            right.Method,
            StructuralCloneRelation.Near,
            correspondence: null,
            different.Receipt,
            alignment,
            receipt);
    }

    static StructuralCloneComparison NearLimit(
        StructuralCloneBodyFacts left,
        StructuralCloneBodyFacts right,
        StructuralCloneComparison different,
        StructuralCloneBlockerKind kind,
        string detail,
        int candidates,
        int verificationSteps)
        => StructuralCloneComparison.NotCompleted(
            left.Method,
            right.Method,
            StructuralCloneDisposition.LimitReached,
            [new StructuralCloneBlocker(kind, StructuralCloneSide.Both, detail)],
            different.Receipt,
            new StructuralCloneAlignmentReceipt(
                candidates,
                verificationSteps,
                Exhausted: false));

    static bool SameLocalTypes(
        ImmutableArray<StructuralCloneTypeIdentity> left,
        ImmutableArray<StructuralCloneTypeIdentity> right)
    {
        if (left.Length != right.Length)
            return false;
        bool[] used = new bool[right.Length];
        foreach (StructuralCloneTypeIdentity item in left)
        {
            int match = -1;
            for (int index = 0; index < right.Length; index++)
            {
                if (!used[index] && item.Equals(right[index]))
                {
                    match = index;
                    break;
                }
            }
            if (match < 0)
                return false;
            used[match] = true;
        }
        return true;
    }

    static List<NearCandidate> BuildNearCandidates(
        StructuralCloneBodyFacts left,
        StructuralCloneBodyFacts right,
        StructuralCloneComparisonLimits limits,
        out StructuralCloneBlockerKind? limitReached)
    {
        List<NearCandidate> candidates = [];
        bool candidateLimitReached = false;
        bool blockElementLimitReached = false;

        void Add(NearCandidate candidate)
        {
            if (candidates.Count
                >= limits.MaximumNearAlignmentCandidates)
            {
                candidateLimitReached = true;
                return;
            }
            candidates.Add(candidate);
        }

        int blockDelta =
            left.Graph.Blocks.Length - right.Graph.Blocks.Length;
        if (Math.Abs(blockDelta) == 1)
        {
            StructuralCloneBodyFacts larger =
                blockDelta > 0 ? left : right;
            for (int block = 1;
                block < larger.Graph.Blocks.Length
                    && !candidateLimitReached;
                block++)
            {
                if (BlockAffectedElements(larger, block)
                    <= limits.MaximumNearBlockElements)
                {
                    Add(new NearCandidate(
                        blockDelta > 0
                            ? NearCandidateKind.RemoveLeftBlock
                            : NearCandidateKind.RemoveRightBlock,
                        block,
                        -1,
                        -1,
                        -1));
                }
                else
                {
                    blockElementLimitReached = true;
                }
            }
            limitReached = candidateLimitReached
                ? StructuralCloneBlockerKind.NearAlignmentCandidateLimit
                : blockElementLimitReached
                    ? StructuralCloneBlockerKind.NearBlockElementLimit
                    : null;
            return candidates;
        }
        if (blockDelta != 0)
        {
            limitReached = null;
            return candidates;
        }

        ImmutableArray<(int Block, int Ordinal)> leftOperations =
            OperationLocations(left);
        ImmutableArray<(int Block, int Ordinal)> rightOperations =
            OperationLocations(right);
        int operationDelta =
            leftOperations.Length - rightOperations.Length;
        if (operationDelta == 0)
        {
            foreach ((int leftBlock, int leftOrdinal) in leftOperations)
            {
                foreach ((int rightBlock, int rightOrdinal)
                    in rightOperations)
                {
                    Add(new NearCandidate(
                        NearCandidateKind.ChangeOperation,
                        leftBlock,
                        leftOrdinal,
                        rightBlock,
                        rightOrdinal));
                    if (candidateLimitReached)
                    {
                        limitReached =
                            StructuralCloneBlockerKind
                                .NearAlignmentCandidateLimit;
                        return candidates;
                    }
                }
            }
        }
        else if (operationDelta == 1)
        {
            foreach ((int block, int ordinal) in leftOperations)
            {
                Add(new NearCandidate(
                    NearCandidateKind.RemoveLeftOperation,
                    block,
                    ordinal,
                    -1,
                    -1));
                if (candidateLimitReached)
                {
                    limitReached =
                        StructuralCloneBlockerKind
                            .NearAlignmentCandidateLimit;
                    return candidates;
                }
            }
        }
        else if (operationDelta == -1)
        {
            foreach ((int block, int ordinal) in rightOperations)
            {
                Add(new NearCandidate(
                    NearCandidateKind.RemoveRightOperation,
                    -1,
                    -1,
                    block,
                    ordinal));
                if (candidateLimitReached)
                {
                    limitReached =
                        StructuralCloneBlockerKind
                            .NearAlignmentCandidateLimit;
                    return candidates;
                }
            }
        }

        ImmutableArray<(int Block, int Ordinal)> leftEdges =
            EdgeLocations(left);
        ImmutableArray<(int Block, int Ordinal)> rightEdges =
            EdgeLocations(right);
        int edgeDelta = leftEdges.Length - rightEdges.Length;
        if (edgeDelta == 0)
        {
            foreach ((int leftBlock, int leftOrdinal) in leftEdges)
            {
                foreach ((int rightBlock, int rightOrdinal) in rightEdges)
                {
                    Add(new NearCandidate(
                        NearCandidateKind.ChangeEdge,
                        leftBlock,
                        leftOrdinal,
                        rightBlock,
                        rightOrdinal));
                    if (candidateLimitReached)
                    {
                        limitReached =
                            StructuralCloneBlockerKind
                                .NearAlignmentCandidateLimit;
                        return candidates;
                    }
                }
            }
        }
        else if (edgeDelta == 1)
        {
            foreach ((int block, int ordinal) in leftEdges)
            {
                Add(new NearCandidate(
                    NearCandidateKind.RemoveLeftEdge,
                    block,
                    ordinal,
                    -1,
                    -1));
                if (candidateLimitReached)
                {
                    limitReached =
                        StructuralCloneBlockerKind
                            .NearAlignmentCandidateLimit;
                    return candidates;
                }
            }
        }
        else if (edgeDelta == -1)
        {
            foreach ((int block, int ordinal) in rightEdges)
            {
                Add(new NearCandidate(
                    NearCandidateKind.RemoveRightEdge,
                    -1,
                    -1,
                    block,
                    ordinal));
                if (candidateLimitReached)
                {
                    limitReached =
                        StructuralCloneBlockerKind
                            .NearAlignmentCandidateLimit;
                    return candidates;
                }
            }
        }

        limitReached = candidateLimitReached
            ? StructuralCloneBlockerKind.NearAlignmentCandidateLimit
            : null;
        return candidates;
    }

    static int BlockAffectedElements(
        StructuralCloneBodyFacts body,
        int removedBlock)
    {
        int edges = body.Graph.Blocks.Sum(block =>
            block.Outgoing.Count(edge =>
                block.Index == removedBlock
                || edge.Target == removedBlock));
        return checked(
            1
            + body.Graph.Blocks[removedBlock].Operations.Length
            + edges);
    }

    static ImmutableArray<(int Block, int Ordinal)> OperationLocations(
        StructuralCloneBodyFacts body)
        =>
        [
            .. body.Graph.Blocks.SelectMany(block =>
                Enumerable.Range(0, block.Operations.Length)
                    .Select(ordinal => (block.Index, ordinal))),
        ];

    static ImmutableArray<(int Block, int Ordinal)> EdgeLocations(
        StructuralCloneBodyFacts body)
        =>
        [
            .. body.Graph.Blocks.SelectMany(block =>
                Enumerable.Range(0, block.Outgoing.Length)
                    .Select(ordinal => (block.Index, ordinal))),
        ];

    static (StructuralCloneBodyFacts Left, StructuralCloneBodyFacts Right)
        ApplyCandidate(
            StructuralCloneBodyFacts left,
            StructuralCloneBodyFacts right,
            NearCandidate candidate)
        => candidate.Kind switch
        {
            NearCandidateKind.ChangeOperation => (
                RemoveOperation(
                    left,
                    candidate.LeftBlock,
                    candidate.LeftOrdinal),
                RemoveOperation(
                    right,
                    candidate.RightBlock,
                    candidate.RightOrdinal)),
            NearCandidateKind.RemoveLeftOperation => (
                RemoveOperation(
                    left,
                    candidate.LeftBlock,
                    candidate.LeftOrdinal),
                right),
            NearCandidateKind.RemoveRightOperation => (
                left,
                RemoveOperation(
                    right,
                    candidate.RightBlock,
                    candidate.RightOrdinal)),
            NearCandidateKind.ChangeEdge => (
                RemoveEdge(
                    left,
                    candidate.LeftBlock,
                    candidate.LeftOrdinal),
                RemoveEdge(
                    right,
                    candidate.RightBlock,
                    candidate.RightOrdinal)),
            NearCandidateKind.RemoveLeftEdge => (
                RemoveEdge(
                    left,
                    candidate.LeftBlock,
                    candidate.LeftOrdinal),
                right),
            NearCandidateKind.RemoveRightEdge => (
                left,
                RemoveEdge(
                    right,
                    candidate.RightBlock,
                    candidate.RightOrdinal)),
            NearCandidateKind.RemoveLeftBlock => (
                RemoveBlock(left, candidate.LeftBlock),
                right),
            NearCandidateKind.RemoveRightBlock => (
                left,
                RemoveBlock(right, candidate.LeftBlock)),
            _ => throw new InvalidOperationException(
                $"Unknown near candidate {candidate.Kind}."),
        };

    static StructuralCloneBodyFacts RemoveOperation(
        StructuralCloneBodyFacts body,
        int block,
        int ordinal)
    {
        ImmutableArray<StructuralCloneBlock> blocks =
        [
            .. body.Graph.Blocks.Select(item =>
                item.Index == block
                    ? item with
                    {
                        Operations = item.Operations.RemoveAt(ordinal),
                    }
                    : item),
        ];
        return body with
        {
            InstructionCount = checked(body.InstructionCount - 1),
            Graph = new StructuralCloneGraph(blocks),
        };
    }

    static StructuralCloneBodyFacts RemoveEdge(
        StructuralCloneBodyFacts body,
        int sourceBlock,
        int ordinal)
    {
        ImmutableArray<StructuralCloneBlock> blocks =
        [
            .. body.Graph.Blocks.Select(block =>
                block.Index == sourceBlock
                    ? block with
                    {
                        Outgoing = block.Outgoing.RemoveAt(ordinal),
                    }
                    : block),
        ];
        return body with
        {
            Graph = RebuildIncoming(blocks),
        };
    }

    static StructuralCloneBodyFacts RemoveBlock(
        StructuralCloneBodyFacts body,
        int removedBlock)
    {
        int[] oldToNew = new int[body.Graph.Blocks.Length];
        Array.Fill(oldToNew, -1);
        int next = 0;
        for (int index = 0; index < oldToNew.Length; index++)
        {
            if (index != removedBlock)
                oldToNew[index] = next++;
        }

        ImmutableArray<StructuralCloneBlock> blocks =
        [
            .. body.Graph.Blocks
                .Where(block => block.Index != removedBlock)
                .Select(block => new StructuralCloneBlock(
                    oldToNew[block.Index],
                    block.Offset,
                    block.ExitsMethod,
                    block.Operations,
                    [
                        .. block.Outgoing
                            .Where(edge => edge.Target != removedBlock)
                            .Select(edge => edge with
                            {
                                Target = oldToNew[edge.Target],
                            }),
                    ],
                    [])),
        ];
        return body with
        {
            InstructionCount = checked(
                body.InstructionCount
                - body.Graph.Blocks[removedBlock].Operations.Length),
            Graph = RebuildIncoming(blocks),
        };
    }

    static StructuralCloneGraph RebuildIncoming(
        ImmutableArray<StructuralCloneBlock> blocks)
    {
        var incoming =
            new ImmutableArray<StructuralCloneEdge>.Builder[blocks.Length];
        for (int index = 0; index < incoming.Length; index++)
        {
            incoming[index] =
                ImmutableArray.CreateBuilder<StructuralCloneEdge>();
        }
        foreach (StructuralCloneBlock source in blocks)
        {
            foreach (StructuralCloneEdge edge in source.Outgoing)
            {
                incoming[edge.Target].Add(
                    new StructuralCloneEdge(
                        edge.Role,
                        source.Index));
            }
        }
        return new StructuralCloneGraph(
            [
                .. blocks.Select(block => block with
                {
                    Incoming = incoming[block.Index].ToImmutable(),
                }),
            ]);
    }

    static StructuralCloneAlignmentAlternative? BuildAlternative(
        StructuralCloneBodyFacts left,
        StructuralCloneBodyFacts right,
        NearCandidate candidate,
        StructuralCloneComparison exact)
    {
        StructuralCloneCorrespondence correspondence =
            exact.Correspondence
            ?? throw new InvalidOperationException(
                "An exact-restoring candidate has no correspondence.");

        if (candidate.Kind == NearCandidateKind.RemoveLeftBlock)
        {
            correspondence = TranslateCorrespondence(
                correspondence,
                RemainingBlocks(left, candidate.LeftBlock),
                RemainingBlocks(right, -1));
            return BlockAlternative(
                left,
                candidate.LeftBlock,
                StructuralCloneEditKind.Removed,
                correspondence);
        }
        if (candidate.Kind == NearCandidateKind.RemoveRightBlock)
        {
            correspondence = TranslateCorrespondence(
                correspondence,
                RemainingBlocks(left, -1),
                RemainingBlocks(right, candidate.LeftBlock));
            return BlockAlternative(
                right,
                candidate.LeftBlock,
                StructuralCloneEditKind.Inserted,
                correspondence);
        }

        StructuralCloneBlockEdit blockEdit;
        ImmutableArray<StructuralCloneOperationEdit> operations = [];
        ImmutableArray<StructuralCloneEdgeEdit> edges = [];
        switch (candidate.Kind)
        {
            case NearCandidateKind.ChangeOperation:
                if (!BlocksCorrespond(
                        correspondence,
                        candidate.LeftBlock,
                        candidate.RightBlock))
                {
                    return null;
                }
                blockEdit = ChangedBlock(
                    correspondence,
                    candidate.LeftBlock);
                operations =
                [
                    new StructuralCloneOperationEdit(
                        StructuralCloneEditKind.Changed,
                        OperationReference(
                            left,
                            candidate.LeftBlock,
                            candidate.LeftOrdinal),
                        OperationReference(
                            right,
                            candidate.RightBlock,
                            candidate.RightOrdinal)),
                ];
                break;
            case NearCandidateKind.RemoveLeftOperation:
                blockEdit = ChangedBlock(
                    correspondence,
                    candidate.LeftBlock);
                operations =
                [
                    new StructuralCloneOperationEdit(
                        StructuralCloneEditKind.Removed,
                        OperationReference(
                            left,
                            candidate.LeftBlock,
                            candidate.LeftOrdinal),
                        null),
                ];
                break;
            case NearCandidateKind.RemoveRightOperation:
                blockEdit = ChangedBlockForRight(
                    correspondence,
                    candidate.RightBlock);
                operations =
                [
                    new StructuralCloneOperationEdit(
                        StructuralCloneEditKind.Inserted,
                        null,
                        OperationReference(
                            right,
                            candidate.RightBlock,
                            candidate.RightOrdinal)),
                ];
                break;
            case NearCandidateKind.ChangeEdge:
                if (!BlocksCorrespond(
                        correspondence,
                        candidate.LeftBlock,
                        candidate.RightBlock))
                {
                    return null;
                }
                blockEdit = ChangedBlock(
                    correspondence,
                    candidate.LeftBlock);
                edges =
                [
                    new StructuralCloneEdgeEdit(
                        StructuralCloneEditKind.Changed,
                        EdgeReference(
                            left,
                            candidate.LeftBlock,
                            candidate.LeftOrdinal),
                        EdgeReference(
                            right,
                            candidate.RightBlock,
                            candidate.RightOrdinal)),
                ];
                break;
            case NearCandidateKind.RemoveLeftEdge:
                blockEdit = ChangedBlock(
                    correspondence,
                    candidate.LeftBlock);
                edges =
                [
                    new StructuralCloneEdgeEdit(
                        StructuralCloneEditKind.Removed,
                        EdgeReference(
                            left,
                            candidate.LeftBlock,
                            candidate.LeftOrdinal),
                        null),
                ];
                break;
            case NearCandidateKind.RemoveRightEdge:
                blockEdit = ChangedBlockForRight(
                    correspondence,
                    candidate.RightBlock);
                edges =
                [
                    new StructuralCloneEdgeEdit(
                        StructuralCloneEditKind.Inserted,
                        null,
                        EdgeReference(
                            right,
                            candidate.RightBlock,
                            candidate.RightOrdinal)),
                ];
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown near candidate {candidate.Kind}.");
        }

        return new StructuralCloneAlignmentAlternative(
            correspondence,
            [blockEdit],
            operations,
            edges);
    }

    static StructuralCloneAlignmentAlternative BlockAlternative(
        StructuralCloneBodyFacts larger,
        int block,
        StructuralCloneEditKind kind,
        StructuralCloneCorrespondence correspondence)
    {
        ImmutableArray<StructuralCloneOperationEdit> operations =
        [
            .. larger.Graph.Blocks[block].Operations.Select(
                (operation, ordinal) =>
                    new StructuralCloneOperationEdit(
                        kind,
                        kind == StructuralCloneEditKind.Removed
                            ? new StructuralCloneOperationReference(
                                block,
                                ordinal,
                                operation.OpCode,
                                operation.OperandKind,
                                operation.Value)
                            : null,
                        kind == StructuralCloneEditKind.Inserted
                            ? new StructuralCloneOperationReference(
                                block,
                                ordinal,
                                operation.OpCode,
                                operation.OperandKind,
                                operation.Value)
                            : null)),
        ];
        ImmutableArray<StructuralCloneEdgeEdit> edges =
        [
            .. larger.Graph.Blocks.SelectMany(source =>
                source.Outgoing
                    .Select((edge, ordinal) => (source, edge, ordinal))
                    .Where(item =>
                        item.source.Index == block
                        || item.edge.Target == block)
                    .Select(item =>
                    {
                        var reference = new StructuralCloneEdgeReference(
                            item.source.Index,
                            item.edge.Role.Kind,
                            item.edge.Role.Ordinal,
                            item.edge.Target);
                        return new StructuralCloneEdgeEdit(
                            kind,
                            kind == StructuralCloneEditKind.Removed
                                ? reference
                                : null,
                            kind == StructuralCloneEditKind.Inserted
                                ? reference
                                : null);
                    })),
        ];
        return new StructuralCloneAlignmentAlternative(
            correspondence,
            [
                new StructuralCloneBlockEdit(
                    kind,
                    kind == StructuralCloneEditKind.Removed
                        ? [block]
                        : [],
                    kind == StructuralCloneEditKind.Inserted
                        ? [block]
                        : []),
            ],
            operations,
            edges);
    }

    static StructuralCloneCorrespondence TranslateCorrespondence(
        StructuralCloneCorrespondence correspondence,
        ImmutableArray<int> leftOldBlocks,
        ImmutableArray<int> rightOldBlocks)
        => correspondence with
        {
            Blocks =
            [
                .. correspondence.Blocks
                    .Select(item => new StructuralCloneBlockClass(
                        leftOldBlocks[item.LeftBlock],
                        [
                            .. item.RightBlocks.Select(
                                right => rightOldBlocks[right]),
                        ]))
                    .OrderBy(static item => item.LeftBlock),
            ],
        };

    static ImmutableArray<int> RemainingBlocks(
        StructuralCloneBodyFacts body,
        int removed)
        =>
        [
            .. Enumerable.Range(0, body.Graph.Blocks.Length)
                .Where(index => index != removed),
        ];

    static bool BlocksCorrespond(
        StructuralCloneCorrespondence correspondence,
        int leftBlock,
        int rightBlock)
        => correspondence.Blocks
            .Single(item => item.LeftBlock == leftBlock)
            .RightBlocks.Contains(rightBlock);

    static StructuralCloneBlockEdit ChangedBlock(
        StructuralCloneCorrespondence correspondence,
        int leftBlock)
    {
        StructuralCloneBlockClass match = correspondence.Blocks.Single(
            item => item.LeftBlock == leftBlock);
        return new StructuralCloneBlockEdit(
            StructuralCloneEditKind.Changed,
            [leftBlock],
            match.RightBlocks);
    }

    static StructuralCloneBlockEdit ChangedBlockForRight(
        StructuralCloneCorrespondence correspondence,
        int rightBlock)
        => new(
            StructuralCloneEditKind.Changed,
            [
                .. correspondence.Blocks
                    .Where(item =>
                        item.RightBlocks.Contains(rightBlock))
                    .Select(static item => item.LeftBlock),
            ],
            [rightBlock]);

    static StructuralCloneOperationReference OperationReference(
        StructuralCloneBodyFacts body,
        int block,
        int ordinal)
    {
        StructuralCloneOperation operation =
            body.Graph.Blocks[block].Operations[ordinal];
        return new StructuralCloneOperationReference(
            block,
            ordinal,
            operation.OpCode,
            operation.OperandKind,
            operation.Value);
    }

    static StructuralCloneEdgeReference EdgeReference(
        StructuralCloneBodyFacts body,
        int source,
        int ordinal)
    {
        StructuralCloneEdge edge =
            body.Graph.Blocks[source].Outgoing[ordinal];
        return new StructuralCloneEdgeReference(
            source,
            edge.Role.Kind,
            edge.Role.Ordinal,
            edge.Target);
    }

    static string CandidateKey(NearCandidate candidate)
    {
        StringBuilder key = new();
        key.Append((int)candidate.Kind);
        key.Append(':');
        key.Append(candidate.LeftBlock);
        key.Append(':');
        key.Append(candidate.LeftOrdinal);
        key.Append(':');
        key.Append(candidate.RightBlock);
        key.Append(':');
        key.Append(candidate.RightOrdinal);
        return key.ToString();
    }
}

enum NearCandidateKind
{
    ChangeOperation,
    RemoveLeftOperation,
    RemoveRightOperation,
    ChangeEdge,
    RemoveLeftEdge,
    RemoveRightEdge,
    RemoveLeftBlock,
    RemoveRightBlock,
}

readonly record struct NearCandidate(
    NearCandidateKind Kind,
    int LeftBlock,
    int LeftOrdinal,
    int RightBlock,
    int RightOrdinal);
