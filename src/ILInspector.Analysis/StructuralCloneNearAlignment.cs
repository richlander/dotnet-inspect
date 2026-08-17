using System.Collections.Immutable;
using System.Reflection.Metadata;
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

        var indexBudget = new NearAlignmentIndexBudget(
            limits.MaximumNearAlignmentIndexSteps);
        List<NearCandidate> candidates;
        StructuralCloneBlockerKind? enumerationLimit;
        try
        {
            candidates = BuildNearCandidates(
                left,
                right,
                limits,
                indexBudget,
                out enumerationLimit);
        }
        catch (NearAlignmentIndexLimitReachedException)
        {
            return NearLimit(
                left,
                right,
                different,
                StructuralCloneBlockerKind.NearAlignmentIndexStepLimit,
                $"Near alignment reached "
                    + $"{limits.MaximumNearAlignmentIndexSteps} "
                    + "candidate-index steps.",
                indexBudget.Candidates,
                verificationSteps: 0,
                indexBudget.Steps);
        }
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
                verificationSteps: 0,
                indexBudget.Steps);
        }

        var alternatives =
            new SortedDictionary<string, StructuralCloneAlignmentAlternative>(
                StringComparer.Ordinal);
        var verificationBudget = new NearAlignmentVerificationBudget(
            limits.MaximumNearAlignmentVerificationSteps);
        foreach (NearCandidate candidate in candidates)
        {
            if (!verificationBudget.TryCharge(
                    IndexCost(left) + (long)IndexCost(right)))
            {
                return NearLimit(
                    left,
                    right,
                    different,
                    StructuralCloneBlockerKind
                        .NearAlignmentVerificationStepLimit,
                    $"Near alignment reached "
                        + $"{limits.MaximumNearAlignmentVerificationSteps} "
                        + "verification steps before the next candidate.",
                    candidates.Count,
                    verificationBudget.Steps,
                    indexBudget.Steps);
            }

            (StructuralCloneBodyFacts candidateLeft,
                StructuralCloneBodyFacts candidateRight) =
                ApplyCandidate(left, right, candidate);
            StructuralCloneWitnessConstraints? witnessConstraints =
                WitnessConstraints(left, right, candidate);
            StructuralCloneComparison exact = CompareExact(
                candidateLeft,
                candidateRight,
                limits,
                witnessConstraints,
                verificationBudget);
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
                    verificationBudget.Steps,
                    indexBudget.Steps);
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
                    verificationBudget.Steps,
                    indexBudget.Steps);
            }
        }

        var receipt = new StructuralCloneAlignmentReceipt(
            indexBudget.Steps,
            candidates.Count,
            verificationBudget.Steps,
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
        int verificationSteps,
        int indexSteps)
        => StructuralCloneComparison.NotCompleted(
            left.Method,
            right.Method,
            StructuralCloneDisposition.LimitReached,
            [new StructuralCloneBlocker(kind, StructuralCloneSide.Both, detail)],
            different.Receipt,
            new StructuralCloneAlignmentReceipt(
                indexSteps,
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
        NearAlignmentIndexBudget indexBudget,
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
            indexBudget.Candidates = candidates.Count;
        }

        int blockDelta =
            left.Graph.Blocks.Length - right.Graph.Blocks.Length;
        if (Math.Abs(blockDelta) == 1)
        {
            StructuralCloneBodyFacts larger =
                blockDelta > 0 ? left : right;
            StructuralCloneBodyFacts smaller =
                blockDelta > 0 ? right : left;
            indexBudget.Charge(IndexCost(smaller));
            StructuralCloneCandidateKey smallerKey =
                StructuralCloneCandidateKey.Create(smaller);
            for (int block = 1;
                block < larger.Graph.Blocks.Length
                    && !candidateLimitReached;
                block++)
            {
                indexBudget.Charge(checked(2 * IndexCost(larger)));
                StructuralCloneBodyFacts removed =
                    RemoveBlock(larger, block);
                if (!StructuralCloneCandidateKey.Create(removed)
                    .Equals(smallerKey))
                {
                    continue;
                }
                if (BlockAffectedElements(larger, block)
                    > limits.MaximumNearBlockElements)
                {
                    blockElementLimitReached = true;
                    continue;
                }
                Add(new NearCandidate(
                    blockDelta > 0
                        ? NearCandidateKind.RemoveLeftBlock
                        : NearCandidateKind.RemoveRightBlock,
                    block,
                    -1,
                    -1,
                    -1));
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

        int leftOperationCount = left.Graph.Blocks.Sum(
            static block => block.Operations.Length);
        int rightOperationCount = right.Graph.Blocks.Sum(
            static block => block.Operations.Length);
        ImmutableArray<NearBlockKey> leftBlockKeys =
            FullBlockKeys(left, indexBudget);
        ImmutableArray<NearBlockKey> rightBlockKeys =
            FullBlockKeys(right, indexBudget);
        NearBodyKey leftBodyKey = BodyKey(
            left,
            leftBlockKeys,
            indexBudget,
            out ImmutableArray<NearLocalKey> leftLocalKeys);
        NearBodyKey rightBodyKey = BodyKey(
            right,
            rightBlockKeys,
            indexBudget,
            out ImmutableArray<NearLocalKey> rightLocalKeys);
        ImmutableArray<NearOperationCandidateKey> leftOperationKeys =
            OperationMaskedCandidateKeys(
                left,
                leftBlockKeys,
                leftLocalKeys,
                leftBodyKey,
                indexBudget);
        ImmutableArray<NearOperationCandidateKey> rightOperationKeys =
            OperationMaskedCandidateKeys(
                right,
                rightBlockKeys,
                rightLocalKeys,
                rightBodyKey,
                indexBudget);
        int operationDelta =
            leftOperationCount - rightOperationCount;
        if (operationDelta == 0)
        {
            var rightIndex = new Dictionary<
                (NearBodyKey Body, NearBlockKey Block, int Ordinal),
                List<(int Block, int Ordinal)>>();
            foreach (NearOperationCandidateKey item
                in rightOperationKeys)
            {
                indexBudget.Charge();
                var key = (item.Body, item.BlockShape, item.Ordinal);
                if (!rightIndex.TryGetValue(
                        key,
                        out List<(int Block, int Ordinal)>? locations))
                {
                    rightIndex.Add(key, locations = []);
                }
                locations.Add((item.Block, item.Ordinal));
            }
            foreach (NearOperationCandidateKey item
                in leftOperationKeys)
            {
                indexBudget.Charge();
                var key = (item.Body, item.BlockShape, item.Ordinal);
                if (!rightIndex.TryGetValue(
                        key,
                        out List<(int Block, int Ordinal)>? matches))
                {
                    continue;
                }
                foreach ((int rightBlock, int rightOrdinal) in matches)
                {
                    indexBudget.Charge();
                    StructuralCloneOperation leftOperation = left.Graph
                        .Blocks[item.Block]
                        .Operations[item.Ordinal];
                    StructuralCloneOperation rightOperation = right.Graph
                        .Blocks[rightBlock]
                        .Operations[rightOrdinal];
                    if (!MayBeChangedOperation(
                            leftOperation,
                            rightOperation))
                    {
                        continue;
                    }
                    Add(new NearCandidate(
                        NearCandidateKind.ChangeOperation,
                        item.Block,
                        item.Ordinal,
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
            foreach (NearOperationCandidateKey item
                in leftOperationKeys)
            {
                indexBudget.Charge();
                if (item.Body != rightBodyKey)
                    continue;
                Add(new NearCandidate(
                    NearCandidateKind.RemoveLeftOperation,
                    item.Block,
                    item.Ordinal,
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
            foreach (NearOperationCandidateKey item
                in rightOperationKeys)
            {
                indexBudget.Charge();
                if (item.Body != leftBodyKey)
                    continue;
                Add(new NearCandidate(
                    NearCandidateKind.RemoveRightOperation,
                    -1,
                    -1,
                    item.Block,
                    item.Ordinal));
                if (candidateLimitReached)
                {
                    limitReached =
                        StructuralCloneBlockerKind
                            .NearAlignmentCandidateLimit;
                    return candidates;
                }
            }
        }

        int leftEdgeCount = left.Graph.Blocks.Sum(
            static block => block.Outgoing.Length);
        int rightEdgeCount = right.Graph.Blocks.Sum(
            static block => block.Outgoing.Length);
        ImmutableArray<NearEdgeCandidateKey> leftEdgeKeys =
            EdgeMaskedCandidateKeys(
                left,
                leftBlockKeys,
                leftLocalKeys,
                leftBodyKey,
                indexBudget);
        ImmutableArray<NearEdgeCandidateKey> rightEdgeKeys =
            EdgeMaskedCandidateKeys(
                right,
                rightBlockKeys,
                rightLocalKeys,
                rightBodyKey,
                indexBudget);
        int edgeDelta = leftEdgeCount - rightEdgeCount;
        if (edgeDelta == 0)
        {
            var rightIndex = new Dictionary<
                (NearBodyKey Body, NearBlockKey Block,
                    StructuralCloneEdgeRole Role),
                List<(int Block, int Ordinal)>>();
            foreach (NearEdgeCandidateKey item in rightEdgeKeys)
            {
                indexBudget.Charge();
                var key = (item.Body, item.Source, item.Role);
                if (!rightIndex.TryGetValue(
                        key,
                        out List<(int Block, int Ordinal)>? locations))
                {
                    rightIndex.Add(key, locations = []);
                }
                locations.Add((item.Block, item.Ordinal));
            }
            foreach (NearEdgeCandidateKey item in leftEdgeKeys)
            {
                indexBudget.Charge();
                var key = (item.Body, item.Source, item.Role);
                if (!rightIndex.TryGetValue(
                        key,
                        out List<(int Block, int Ordinal)>? matches))
                {
                    continue;
                }
                foreach ((int rightBlock, int rightOrdinal) in matches)
                {
                    indexBudget.Charge();
                    Add(new NearCandidate(
                        NearCandidateKind.ChangeEdge,
                        item.Block,
                        item.Ordinal,
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
            foreach (NearEdgeCandidateKey item in leftEdgeKeys)
            {
                indexBudget.Charge();
                if (item.Body != rightBodyKey)
                    continue;
                Add(new NearCandidate(
                    NearCandidateKind.RemoveLeftEdge,
                    item.Block,
                    item.Ordinal,
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
            foreach (NearEdgeCandidateKey item in rightEdgeKeys)
            {
                indexBudget.Charge();
                if (item.Body != leftBodyKey)
                    continue;
                Add(new NearCandidate(
                    NearCandidateKind.RemoveRightEdge,
                    -1,
                    -1,
                    item.Block,
                    item.Ordinal));
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

    static ImmutableArray<NearBlockKey> FullBlockKeys(
        StructuralCloneBodyFacts body,
        NearAlignmentIndexBudget indexBudget)
    {
        var results = ImmutableArray.CreateBuilder<NearBlockKey>(
            body.Graph.Blocks.Length);
        foreach (StructuralCloneBlock block in body.Graph.Blocks)
        {
            indexBudget.Charge(checked(
                1
                + block.Operations.Length
                + block.Outgoing.Length
                + block.Incoming.Length));
            results.Add(new NearBlockKey(
                block.Index == 0,
                block.ExitsMethod,
                block.Operations.Length,
                block.Outgoing.Length,
                block.Incoming.Length,
                OperationHash(body, block),
                RoleHash(block.Outgoing),
                RoleHash(block.Incoming)));
        }
        return results.MoveToImmutable();
    }

    static ImmutableArray<NearBlockKey> OperationMaskedBlockKeys(
        StructuralCloneBodyFacts body,
        StructuralCloneBlock block,
        NearAlignmentIndexBudget indexBudget)
    {
        indexBudget.Charge(checked(1 + block.Operations.Length));
        ulong[] values =
        [
            .. block.Operations.Select(operation =>
                OperationCode(body, operation)),
        ];
        ulong[] masked = HashesWithoutEach(values);
        ulong outgoing = RoleHash(block.Outgoing);
        ulong incoming = RoleHash(block.Incoming);
        return
        [
            .. masked.Select(hash => new NearBlockKey(
                block.Index == 0,
                block.ExitsMethod,
                block.Operations.Length - 1,
                block.Outgoing.Length,
                block.Incoming.Length,
                hash,
                outgoing,
                incoming)),
        ];
    }

    static ImmutableArray<NearOperationCandidateKey>
        OperationMaskedCandidateKeys(
            StructuralCloneBodyFacts body,
            ImmutableArray<NearBlockKey> fullBlockKeys,
            ImmutableArray<NearLocalKey> fullLocalKeys,
            NearBodyKey bodyKey,
            NearAlignmentIndexBudget indexBudget)
    {
        var results =
            ImmutableArray.CreateBuilder<NearOperationCandidateKey>();
        foreach (StructuralCloneBlock block in body.Graph.Blocks)
        {
            ImmutableArray<NearBlockLocalProfile> profiles =
                BlockLocalProfiles(block, indexBudget);
            ImmutableArray<NearBlockKey> maskedBlockKeys =
                OperationMaskedBlockKeys(body, block, indexBudget);
            for (int removed = 0;
                removed < block.Operations.Length;
                removed++)
            {
                indexBudget.Charge();
                NearBlockKey maskedBlock = maskedBlockKeys[removed];
                NearBodyKey candidateBody = ReplaceBlock(
                    bodyKey,
                    fullBlockKeys[block.Index],
                    maskedBlock);
                foreach (NearBlockLocalProfile profile in profiles)
                {
                    indexBudget.Charge();
                    NearLocalUseAggregate oldUses =
                        LocalUseAggregate(
                            fullBlockKeys[block.Index],
                            profile,
                            removedOrdinal: null);
                    NearLocalUseAggregate newUses =
                        LocalUseAggregate(
                            maskedBlock,
                            profile,
                            removed);
                    NearLocalKey updated = ReplaceLocalUses(
                        fullLocalKeys[profile.Local],
                        oldUses,
                        newUses);
                    candidateBody = ReplaceLocal(
                        candidateBody,
                        fullLocalKeys[profile.Local],
                        updated);
                }
                results.Add(new NearOperationCandidateKey(
                    block.Index,
                    removed,
                    candidateBody,
                    maskedBlock));
            }
        }
        return results.ToImmutable();
    }

    static ImmutableArray<NearEdgeCandidateKey> EdgeMaskedCandidateKeys(
        StructuralCloneBodyFacts body,
        ImmutableArray<NearBlockKey> fullKeys,
        ImmutableArray<NearLocalKey> fullLocalKeys,
        NearBodyKey bodyKey,
        NearAlignmentIndexBudget indexBudget)
    {
        var incomingRoleHashes =
            new Dictionary<(int Block, StructuralCloneEdgeRole Role),
                ulong>();
        foreach (StructuralCloneBlock block in body.Graph.Blocks)
        {
            indexBudget.Charge(checked(1 + block.Incoming.Length));
            ulong[] masked = RoleHashesWithoutEach(block.Incoming);
            for (int ordinal = 0;
                ordinal < block.Incoming.Length;
                ordinal++)
            {
                incomingRoleHashes.TryAdd(
                    (block.Index, block.Incoming[ordinal].Role),
                    masked[ordinal]);
            }
        }
        var incomingMasks =
            new Dictionary<(int Block, StructuralCloneEdgeRole Role),
                NearBlockKey>();
        ImmutableArray<NearBlockLocalProfile>[] localProfiles =
        [
            .. body.Graph.Blocks.Select(block =>
                BlockLocalProfiles(block, indexBudget)),
        ];
        var results =
            ImmutableArray.CreateBuilder<NearEdgeCandidateKey>();
        foreach (StructuralCloneBlock source in body.Graph.Blocks)
        {
            indexBudget.Charge(checked(1 + source.Outgoing.Length));
            ulong[] outgoingMasks = RoleHashesWithoutEach(source.Outgoing);
            for (int ordinal = 0;
                ordinal < source.Outgoing.Length;
                ordinal++)
            {
                StructuralCloneEdge edge = source.Outgoing[ordinal];
                NearBlockKey sourceMasked =
                    fullKeys[source.Index] with
                    {
                        OutgoingCount = source.Outgoing.Length - 1,
                        OutgoingRoles = outgoingMasks[ordinal],
                    };
                NearBodyKey candidateBody;
                if (edge.Target == source.Index)
                {
                    NearBlockKey selfMasked = sourceMasked with
                    {
                        IncomingCount = source.Incoming.Length - 1,
                        IncomingRoles =
                            incomingRoleHashes[(source.Index, edge.Role)],
                    };
                    candidateBody = ReplaceBlocksAndLocalUses(
                        body,
                        bodyKey,
                        fullKeys,
                        fullLocalKeys,
                        localProfiles,
                        [(source.Index, selfMasked)],
                        indexBudget);
                    sourceMasked = selfMasked;
                }
                else
                {
                    var incomingKey = (edge.Target, edge.Role);
                    if (!incomingMasks.TryGetValue(
                            incomingKey,
                            out NearBlockKey targetMasked))
                    {
                        StructuralCloneBlock target =
                            body.Graph.Blocks[edge.Target];
                        targetMasked = fullKeys[edge.Target] with
                        {
                            IncomingCount = target.Incoming.Length - 1,
                            IncomingRoles =
                                incomingRoleHashes[(edge.Target, edge.Role)],
                        };
                        incomingMasks.Add(incomingKey, targetMasked);
                    }
                    candidateBody = ReplaceBlocksAndLocalUses(
                        body,
                        bodyKey,
                        fullKeys,
                        fullLocalKeys,
                        localProfiles,
                        [
                            (source.Index, sourceMasked),
                            (edge.Target, targetMasked),
                        ],
                        indexBudget);
                }
                results.Add(new NearEdgeCandidateKey(
                    source.Index,
                    ordinal,
                    edge.Role,
                    candidateBody,
                    sourceMasked));
            }
        }
        return results.ToImmutable();
    }

    static NearBodyKey ReplaceBlocksAndLocalUses(
        StructuralCloneBodyFacts body,
        NearBodyKey bodyKey,
        ImmutableArray<NearBlockKey> fullBlockKeys,
        ImmutableArray<NearLocalKey> fullLocalKeys,
        ImmutableArray<NearBlockLocalProfile>[] localProfiles,
        ImmutableArray<(int Block, NearBlockKey Key)> replacements,
        NearAlignmentIndexBudget indexBudget)
    {
        NearBodyKey candidateBody = bodyKey;
        var updatedLocals = new Dictionary<int, NearLocalKey>();
        foreach ((int blockIndex, NearBlockKey newBlock)
            in replacements)
        {
            NearBlockKey oldBlock = fullBlockKeys[blockIndex];
            candidateBody = ReplaceBlock(
                candidateBody,
                oldBlock,
                newBlock);
            foreach (NearBlockLocalProfile profile
                in localProfiles[blockIndex])
            {
                indexBudget.Charge();
                int local = profile.Local;
                NearLocalKey updated =
                    updatedLocals.GetValueOrDefault(
                        local,
                        fullLocalKeys[local]);
                updated = ReplaceLocalUses(
                    updated,
                    LocalUseAggregate(
                        oldBlock,
                        profile,
                        removedOrdinal: null),
                    LocalUseAggregate(
                        newBlock,
                        profile,
                        removedOrdinal: null));
                updatedLocals[local] = updated;
            }
        }
        foreach ((int local, NearLocalKey updated) in updatedLocals)
        {
            candidateBody = ReplaceLocal(
                candidateBody,
                fullLocalKeys[local],
                updated);
        }
        return candidateBody;
    }

    static NearBodyKey BodyKey(
        StructuralCloneBodyFacts body,
        ImmutableArray<NearBlockKey> blocks,
        NearAlignmentIndexBudget indexBudget,
        out ImmutableArray<NearLocalKey> locals)
    {
        ulong sum = 0;
        ulong sumSquares = 0;
        foreach (NearBlockKey block in blocks)
        {
            indexBudget.Charge();
            ulong code = BlockCode(block);
            sum = unchecked(sum + code);
            sumSquares = unchecked(sumSquares + code * code);
        }
        NearLocalKey[] localValues =
        [
            .. body.Locals.Select(local => new NearLocalKey(
                local.GetHashCode(),
                UseCount: 0,
                Uses: 0,
                UseSquares: 0)),
        ];
        foreach (StructuralCloneBlock block in body.Graph.Blocks)
        {
            for (int ordinal = 0;
                ordinal < block.Operations.Length;
                ordinal++)
            {
                indexBudget.Charge();
                StructuralCloneOperation operation =
                    block.Operations[ordinal];
                if (operation.OperandKind
                    != StructuralCloneOperandKind.Local)
                {
                    continue;
                }
                int local = checked((int)operation.Value);
                localValues[local] = AddLocalUse(
                    localValues[local],
                    LocalUseCode(
                        blocks[block.Index],
                        operation.OpCode,
                        ordinal));
            }
        }
        ulong localSum = 0;
        ulong localSumSquares = 0;
        foreach (NearLocalKey local in localValues)
        {
            indexBudget.Charge();
            ulong code = LocalCode(local);
            localSum = unchecked(localSum + code);
            localSumSquares = unchecked(
                localSumSquares + code * code);
        }
        locals = [.. localValues];
        return new NearBodyKey(
            sum,
            sumSquares,
            localSum,
            localSumSquares);
    }

    static NearBodyKey ReplaceBlock(
        NearBodyKey body,
        NearBlockKey oldBlock,
        NearBlockKey newBlock)
    {
        ulong oldCode = BlockCode(oldBlock);
        ulong newCode = BlockCode(newBlock);
        return new NearBodyKey(
            unchecked(body.Sum - oldCode + newCode),
            unchecked(
                body.SumSquares
                - oldCode * oldCode
                + newCode * newCode),
            body.LocalSum,
            body.LocalSumSquares);
    }

    static NearBodyKey ReplaceLocal(
        NearBodyKey body,
        NearLocalKey oldLocal,
        NearLocalKey newLocal)
    {
        ulong oldCode = LocalCode(oldLocal);
        ulong newCode = LocalCode(newLocal);
        return body with
        {
            LocalSum = unchecked(
                body.LocalSum - oldCode + newCode),
            LocalSumSquares = unchecked(
                body.LocalSumSquares
                - oldCode * oldCode
                + newCode * newCode),
        };
    }

    static NearLocalKey AddLocalUse(
        NearLocalKey local,
        ulong use)
        => local with
        {
            UseCount = local.UseCount + 1,
            Uses = unchecked(local.Uses + use),
            UseSquares = unchecked(local.UseSquares + use * use),
        };

    static NearLocalKey ReplaceLocalUses(
        NearLocalKey local,
        NearLocalUseAggregate oldUses,
        NearLocalUseAggregate newUses)
        => local with
        {
            UseCount = local.UseCount
                - oldUses.Count
                + newUses.Count,
            Uses = unchecked(
                local.Uses - oldUses.Sum + newUses.Sum),
            UseSquares = unchecked(
                local.UseSquares
                - oldUses.SumSquares
                + newUses.SumSquares),
        };

    static ImmutableArray<NearBlockLocalProfile> BlockLocalProfiles(
        StructuralCloneBlock block,
        NearAlignmentIndexBudget indexBudget)
    {
        indexBudget.Charge(checked(1 + block.Operations.Length));
        return
        [
            .. block.Operations
                .Select((operation, ordinal) => (operation, ordinal))
                .Where(static item =>
                    item.operation.OperandKind
                        == StructuralCloneOperandKind.Local)
                .GroupBy(item =>
                    checked((int)item.operation.Value))
                .Select(group =>
                {
                    int[] ordinals =
                    [
                        .. group.Select(static item => item.ordinal),
                    ];
                    ulong[] rests =
                    [
                        .. group.Select(item => LocalUseRestCode(
                            item.operation.OpCode,
                            item.ordinal)),
                    ];
                    ulong[] prefixSums = new ulong[rests.Length + 1];
                    ulong[] prefixSquares =
                        new ulong[rests.Length + 1];
                    for (int index = 0; index < rests.Length; index++)
                    {
                        prefixSums[index + 1] = unchecked(
                            prefixSums[index] + rests[index]);
                        prefixSquares[index + 1] = unchecked(
                            prefixSquares[index]
                            + rests[index] * rests[index]);
                    }
                    return new NearBlockLocalProfile(
                        group.Key,
                        [.. ordinals],
                        [.. rests],
                        [.. prefixSums],
                        [.. prefixSquares]);
                })
                .OrderBy(static profile => profile.Local),
        ];
    }

    static int IndexCost(StructuralCloneBodyFacts body)
        => checked(
            body.Graph.Blocks.Length
            + body.Graph.Blocks.Sum(static block =>
                block.Operations.Length
                + block.Outgoing.Length
                + block.Incoming.Length)
            + body.Locals.Length);

    static long RefinementElementWork(StructuralCloneBodyFacts body)
        => 3L * body.Graph.Blocks.Length
            + 2L * body.Graph.Blocks.Sum(
                static block => block.Operations.Length)
            + 2L * body.Graph.Blocks.Sum(
                static block => block.Outgoing.Length)
            + 3L * body.Locals.Length;

    static NearLocalUseAggregate LocalUseAggregate(
        NearBlockKey block,
        NearBlockLocalProfile profile,
        int? removedOrdinal)
    {
        int removedIndex = removedOrdinal is { } removed
            ? profile.Ordinals.BinarySearch(removed)
            : -1;
        int afterIndex = removedOrdinal is { } value
            ? UpperBound(profile.Ordinals, value)
            : profile.Ordinals.Length;
        int countAfter = profile.Ordinals.Length - afterIndex;
        ulong sumAfter = unchecked(
            profile.PrefixSums[^1]
            - profile.PrefixSums[afterIndex]);
        ulong removedRest = removedIndex >= 0
            ? profile.Rests[removedIndex]
            : 0;
        ulong removedSquare = unchecked(removedRest * removedRest);
        int count = profile.Ordinals.Length
            - (removedIndex >= 0 ? 1 : 0);
        ulong restSum = unchecked(
            profile.PrefixSums[^1]
            - removedRest
            - (ulong)countAfter);
        ulong restSquares = unchecked(
            profile.PrefixSquares[^1]
            - removedSquare
            - 2 * sumAfter
            + (ulong)countAfter);
        const ulong Prime = 1099511628211;
        ulong blockPart = unchecked(BlockCode(block) * Prime * Prime);
        ulong sum = unchecked(
            (ulong)count * blockPart + restSum);
        ulong sumSquares = unchecked(
            (ulong)count * blockPart * blockPart
            + 2 * blockPart * restSum
            + restSquares);
        return new NearLocalUseAggregate(count, sum, sumSquares);
    }

    static int UpperBound(ImmutableArray<int> values, int value)
    {
        int low = 0;
        int high = values.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (values[middle] <= value)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }

    static ulong LocalUseRestCode(ILOpCode operation, int ordinal)
    {
        const ulong Prime = 1099511628211;
        return unchecked((uint)operation * Prime + (uint)ordinal);
    }

    static ulong LocalCode(NearLocalKey local)
    {
        const ulong Prime = 1099511628211;
        ulong hash = unchecked((uint)local.TypeHash);
        hash = unchecked(hash * Prime + (uint)local.UseCount);
        hash = unchecked(hash * Prime + local.Uses);
        return unchecked(hash * Prime + local.UseSquares);
    }

    static ulong LocalUseCode(
        NearBlockKey block,
        ILOpCode operation,
        int ordinal)
    {
        const ulong Prime = 1099511628211;
        return unchecked(
            BlockCode(block) * Prime * Prime
            + LocalUseRestCode(operation, ordinal));
    }

    static ulong BlockCode(NearBlockKey block)
    {
        const ulong Prime = 1099511628211;
        ulong hash = 1469598103934665603;
        hash = unchecked(hash * Prime + (block.Entry ? 1UL : 0UL));
        hash = unchecked(hash * Prime + (block.ExitsMethod ? 1UL : 0UL));
        hash = unchecked(hash * Prime + (uint)block.OperationCount);
        hash = unchecked(hash * Prime + (uint)block.OutgoingCount);
        hash = unchecked(hash * Prime + (uint)block.IncomingCount);
        hash = unchecked(hash * Prime + block.Operations);
        hash = unchecked(hash * Prime + block.OutgoingRoles);
        return unchecked(hash * Prime + block.IncomingRoles);
    }

    static ulong OperationHash(
        StructuralCloneBodyFacts body,
        StructuralCloneBlock block)
        => HashValues(
            [
                .. block.Operations.Select(operation =>
                    OperationCode(body, operation)),
            ]);

    static ulong OperationCode(
        StructuralCloneBodyFacts body,
        StructuralCloneOperation operation)
    {
        const ulong Prime = 1099511628211;
        ulong value = operation.OperandKind
            == StructuralCloneOperandKind.Local
                ? unchecked((uint)body.Locals[
                    checked((int)operation.Value)].GetHashCode())
                : unchecked((ulong)operation.Value);
        ulong hash = unchecked((uint)operation.OpCode);
        hash = unchecked(
            hash * Prime + (uint)operation.OperandKind);
        return unchecked(hash * Prime + value);
    }

    static bool MayBeChangedOperation(
        StructuralCloneOperation left,
        StructuralCloneOperation right)
        => left.OpCode != right.OpCode
            || left.OperandKind != right.OperandKind
            || left.OperandKind == StructuralCloneOperandKind.Local
            || left.Value != right.Value;

    static ulong RoleHash(
        ImmutableArray<StructuralCloneEdge> edges)
        => HashValues(
            [
                .. edges.Select(static edge => RoleCode(edge.Role))
                    .Order(),
            ]);

    static ulong[] RoleHashesWithoutEach(
        ImmutableArray<StructuralCloneEdge> edges)
    {
        (StructuralCloneEdgeRole Role, int Original)[] ordered =
        [
            .. edges.Select(
                    (edge, original) => (
                        Role: edge.Role,
                        Original: original))
                .OrderBy(static item => item.Role.Kind)
                .ThenBy(static item => item.Role.Ordinal)
                .ThenBy(static item => item.Original),
        ];
        ulong[] masked = HashesWithoutEach(
            [.. ordered.Select(static item => RoleCode(item.Role))]);
        ulong[] byOriginal = new ulong[edges.Length];
        for (int index = 0; index < ordered.Length; index++)
            byOriginal[ordered[index].Original] = masked[index];
        return byOriginal;
    }

    static ulong RoleCode(StructuralCloneEdgeRole role)
        => ((ulong)(uint)role.Kind << 32)
            | unchecked((uint)role.Ordinal);

    static ulong HashValues(ulong[] values)
    {
        const ulong Prime = 1099511628211;
        ulong hash = 0;
        foreach (ulong value in values)
            hash = unchecked(hash * Prime + value);
        return hash;
    }

    static ulong[] HashesWithoutEach(ulong[] values)
    {
        const ulong Prime = 1099511628211;
        ulong[] powers = new ulong[values.Length + 1];
        ulong[] prefixes = new ulong[values.Length + 1];
        ulong[] suffixes = new ulong[values.Length + 1];
        powers[0] = 1;
        for (int index = 0; index < values.Length; index++)
        {
            powers[index + 1] = unchecked(powers[index] * Prime);
            prefixes[index + 1] = unchecked(
                prefixes[index] * Prime + values[index]);
        }
        for (int index = values.Length - 1; index >= 0; index--)
        {
            suffixes[index] = unchecked(
                values[index] * powers[values.Length - index - 1]
                + suffixes[index + 1]);
        }
        ulong[] masked = new ulong[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            masked[index] = unchecked(
                prefixes[index] * powers[values.Length - index - 1]
                + suffixes[index + 1]);
        }
        return masked;
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

    static StructuralCloneWitnessConstraints? WitnessConstraints(
        StructuralCloneBodyFacts left,
        StructuralCloneBodyFacts right,
        NearCandidate candidate)
    {
        if (candidate.Kind == NearCandidateKind.ChangeOperation)
        {
            StructuralCloneOperation leftOperation = left.Graph
                .Blocks[candidate.LeftBlock]
                .Operations[candidate.LeftOrdinal];
            StructuralCloneOperation rightOperation = right.Graph
                .Blocks[candidate.RightBlock]
                .Operations[candidate.RightOrdinal];
            return new StructuralCloneWitnessConstraints(
                RequiredLeftBlock: candidate.LeftBlock,
                RequiredRightBlock: candidate.RightBlock,
                ForbiddenLeftLocal:
                    leftOperation.OpCode == rightOperation.OpCode
                    && leftOperation.OperandKind
                        == StructuralCloneOperandKind.Local
                    && rightOperation.OperandKind
                        == StructuralCloneOperandKind.Local
                        ? checked((int)leftOperation.Value)
                        : -1,
                ForbiddenRightLocal:
                    leftOperation.OpCode == rightOperation.OpCode
                    && leftOperation.OperandKind
                        == StructuralCloneOperandKind.Local
                    && rightOperation.OperandKind
                        == StructuralCloneOperandKind.Local
                        ? checked((int)rightOperation.Value)
                        : -1);
        }
        if (candidate.Kind == NearCandidateKind.ChangeEdge)
        {
            StructuralCloneEdge leftEdge = left.Graph
                .Blocks[candidate.LeftBlock]
                .Outgoing[candidate.LeftOrdinal];
            StructuralCloneEdge rightEdge = right.Graph
                .Blocks[candidate.RightBlock]
                .Outgoing[candidate.RightOrdinal];
            return new StructuralCloneWitnessConstraints(
                RequiredLeftBlock: candidate.LeftBlock,
                RequiredRightBlock: candidate.RightBlock,
                ForbiddenLeftBlock: leftEdge.Target,
                ForbiddenRightBlock: rightEdge.Target);
        }
        return null;
    }

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
                if (candidate.LeftOrdinal != candidate.RightOrdinal
                    || !BlocksCorrespond(
                        correspondence,
                        candidate.LeftBlock,
                        candidate.RightBlock))
                {
                    return null;
                }
                StructuralCloneOperationReference leftOperation =
                    OperationReference(
                        left,
                        candidate.LeftBlock,
                        candidate.LeftOrdinal);
                StructuralCloneOperationReference rightOperation =
                    OperationReference(
                        right,
                        candidate.RightBlock,
                        candidate.RightOrdinal);
                blockEdit = ChangedBlock(
                    correspondence,
                    candidate.LeftBlock);
                operations =
                [
                    new StructuralCloneOperationEdit(
                        StructuralCloneEditKind.Changed,
                        leftOperation,
                        rightOperation),
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
                StructuralCloneEdgeReference leftEdge = EdgeReference(
                    left,
                    candidate.LeftBlock,
                    candidate.LeftOrdinal);
                StructuralCloneEdgeReference rightEdge = EdgeReference(
                    right,
                    candidate.RightBlock,
                    candidate.RightOrdinal);
                if (leftEdge.Kind != rightEdge.Kind
                    || leftEdge.Ordinal != rightEdge.Ordinal)
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
                        leftEdge,
                        rightEdge),
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

readonly record struct NearBlockKey(
    bool Entry,
    bool ExitsMethod,
    int OperationCount,
    int OutgoingCount,
    int IncomingCount,
    ulong Operations,
    ulong OutgoingRoles,
    ulong IncomingRoles);

readonly record struct NearBodyKey(
    ulong Sum,
    ulong SumSquares,
    ulong LocalSum,
    ulong LocalSumSquares);

readonly record struct NearLocalKey(
    int TypeHash,
    int UseCount,
    ulong Uses,
    ulong UseSquares);

readonly record struct NearBlockLocalProfile(
    int Local,
    ImmutableArray<int> Ordinals,
    ImmutableArray<ulong> Rests,
    ImmutableArray<ulong> PrefixSums,
    ImmutableArray<ulong> PrefixSquares);

readonly record struct NearLocalUseAggregate(
    int Count,
    ulong Sum,
    ulong SumSquares);

sealed class NearAlignmentIndexBudget(int maximum)
{
    public int Steps { get; private set; }
    public int Candidates { get; set; }

    public void Charge(int steps = 1)
    {
        if (steps < 0 || steps > maximum - Steps)
        {
            Steps = maximum;
            throw new NearAlignmentIndexLimitReachedException();
        }
        Steps += steps;
    }
}

sealed class NearAlignmentIndexLimitReachedException : Exception;

sealed class NearAlignmentVerificationBudget(int maximum)
{
    public int Steps { get; private set; }
    public int Remaining => maximum - Steps;

    public bool TryCharge(long steps)
    {
        if (steps < 0 || steps > Remaining)
        {
            Steps = maximum;
            return false;
        }
        Steps += (int)steps;
        return true;
    }
}

readonly record struct NearOperationCandidateKey(
    int Block,
    int Ordinal,
    NearBodyKey Body,
    NearBlockKey BlockShape);

readonly record struct NearEdgeCandidateKey(
    int Block,
    int Ordinal,
    StructuralCloneEdgeRole Role,
    NearBodyKey Body,
    NearBlockKey Source);
