using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

/// <summary>The canonical Slice-1 resource-lifecycle census bucket names (#2439). Candidate
/// buckets are measurement-only queries a clean acquire may satisfy several of at once;
/// suppression buckets are terminal reasons a single acquire was set aside as unreasoned.</summary>
public static class ResourceLifecycleBuckets
{
    // Candidate buckets (a clean acquire may fall into more than one).
    public const string NormalPathLeakCandidate = "normal-path-leak-candidate";
    public const string ExceptionPathLeakCandidate = "exception-path-leak-candidate";
    public const string UseAfterReturnCandidate = "use-after-return-candidate";
    public const string DoubleReturnCandidate = "double-return-candidate";

    // Suppression buckets (terminal; exactly one per suppressed acquire).
    public const string OwnershipTransferSuppressed = "ownership-transfer-suppressed";
    public const string AliasOrFieldSuppressed = "alias-or-field-suppressed";
    public const string CrossMethodSuppressed = "cross-method-suppressed";
    public const string IncompleteCfgOrRdSuppressed = "incomplete-cfg-or-rd-suppressed";

    /// <summary>Every bucket in a stable, human-meaningful order (candidates, then suppressions).</summary>
    public static readonly ImmutableArray<string> All =
    [
        NormalPathLeakCandidate,
        ExceptionPathLeakCandidate,
        UseAfterReturnCandidate,
        DoubleReturnCandidate,
        OwnershipTransferSuppressed,
        AliasOrFieldSuppressed,
        CrossMethodSuppressed,
        IncompleteCfgOrRdSuppressed,
    ];
}

/// <summary>One typed resource-lifecycle observation for one acquire: the resource family, the
/// bucket it fell into, the acquire IL offset, an optional detail offset (release/use/escape
/// site), and a short human evidence string. This is a fact, not a finding: it never feeds a
/// user-facing accusation.</summary>
public sealed record ResourceLifecycleFact(
    MethodIdentity Method,
    string ResourceKind,
    string Bucket,
    int AcquireOffset,
    int? DetailOffset,
    string Evidence);

/// <summary>A per-scope census result: the raw facts plus the number of acquires the census was
/// able to reason about (the candidate/suppressed denominator; excludes methods dropped as
/// incomplete).</summary>
public sealed record ResourceLifecycleCensusResult(
    ImmutableArray<ResourceLifecycleFact> Facts,
    int AcquiresObserved);

/// <summary>
/// A read-only, measurement-only census of ArrayPool resource lifecycles (#2439 Slice 1).
/// It consumes exactly the substrate the <see cref="LeakTriageAnalyzer"/> finding path does —
/// the EH-aware <see cref="BlockGraph"/>, the reaching-definition slot webs, and the shared
/// <see cref="ArrayPoolRecognizers"/> — but instead of accusing, it partitions every recognized
/// <c>ArrayPool&lt;T&gt;.Shared.Rent</c> acquire into typed candidate and suppression buckets so
/// the size and shape of each bucket can be measured on real corpora before any of them graduate
/// to a user-facing finding. It wires no product surface and changes no existing finding.
///
/// <para>Precision boundary: a use the census cannot positively classify as a release or a
/// local, non-escaping use (address-of, field/alias store, method return, unmodeled call) marks
/// the acquire as an <em>escape</em>, and an escaped acquire is terminally suppressed rather than
/// measured for leaks — the same fail-closed posture as the finding path, just recorded instead
/// of dropped. Candidate buckets are deliberately raw: unlike the finding path they do not
/// fail-closed on correlated-branch multi-release, so a `normal-path-leak-candidate` count is a
/// pre-graduation upper bound that Slice 4 must refine with predicate facts, not a bug.</para>
/// </summary>
public static class ResourceLifecycleCensus
{
    const string ResourceKind = "arraypool";

    public static ResourceLifecycleCensusResult CensusAssembly(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var facts = ImmutableArray.CreateBuilder<ResourceLifecycleFact>();
        int acquires = 0;
        foreach (var body in AnalysisMethodBodies.Enumerate(path))
        {
            var result = CensusMethod(body.Method, body.Il, body.ExceptionRegions, body.ResolveMethod);
            facts.AddRange(result.Facts);
            acquires += result.AcquiresObserved;
        }

        return new ResourceLifecycleCensusResult(facts.ToImmutable(), acquires);
    }

    public static ResourceLifecycleCensusResult CensusMethod(
        MethodIdentity method,
        byte[] il,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions,
        Func<int, MemberRef> resolveMethod)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(il);
        ArgumentNullException.ThrowIfNull(exceptionRegions);
        ArgumentNullException.ThrowIfNull(resolveMethod);

        if (il.Length == 0)
            return Empty;

        try
        {
            var instructions = InstructionDecoder.Decode(il);
            var graph = BlockGraph.Build(il.Length, instructions, exceptionRegions);
            var calls = ArrayPoolRecognizers.BuildCallMap(instructions, resolveMethod);

            int firstRentOffset = -1;
            foreach (var instruction in instructions)
            {
                if (calls.TryGetValue(instruction.Offset, out var callee) && ArrayPoolRecognizers.IsArrayPoolRent(callee))
                {
                    firstRentOffset = instruction.Offset;
                    break;
                }
            }

            if (firstRentOffset < 0)
                return Empty;

            var reaching = ReachingDefinitions.Analyze(il, AnalysisMethodBodies.ArgumentSlotCount(method), exceptionRegions);
            if (!graph.IsComplete || !reaching.IsComplete)
            {
                var reason = graph.IncompleteReason ?? reaching.IncompleteReason ?? "incomplete dataflow";
                return new ResourceLifecycleCensusResult(
                    [new ResourceLifecycleFact(
                        method,
                        ResourceKind,
                        ResourceLifecycleBuckets.IncompleteCfgOrRdSuppressed,
                        firstRentOffset,
                        firstRentOffset,
                        $"ArrayPool Rent at IL_{firstRentOffset:X4} not measured: {reason}")],
                    AcquiresObserved: 0);
            }

            var terminators = BuildTerminators(graph, instructions);
            var facts = ImmutableArray.CreateBuilder<ResourceLifecycleFact>();
            int acquires = 0;
            foreach (var acquire in ArrayPoolRecognizers.FindAcquires(instructions, graph, reaching, calls))
            {
                acquires++;
                CensusAcquire(method, instructions, graph, reaching, calls, terminators, acquire, facts);
            }

            return new ResourceLifecycleCensusResult(facts.ToImmutable(), acquires);
        }
        catch (Exception ex) when (AnalysisMethodBodies.IsRecoverable(ex))
        {
            return Empty;
        }
    }

    static void CensusAcquire(
        MethodIdentity method,
        ImmutableArray<DecodedInstruction> instructions,
        BlockGraph graph,
        ReachingDefinitionsResult reaching,
        IReadOnlyDictionary<int, MemberRef> calls,
        DecodedInstruction?[] terminators,
        ArrayPoolAcquire acquire,
        ImmutableArray<ResourceLifecycleFact>.Builder facts)
    {
        var releases = new List<int>();
        var safeUses = new List<int>();
        (string Bucket, int Offset, string Detail)? escape = null;

        foreach (var use in reaching.UsesOf(acquire.Definition))
        {
            if (use.Address)
            {
                escape = ChooseEscape(escape, (ResourceLifecycleBuckets.AliasOrFieldSuppressed, use.Offset, "rented array address taken"));
                continue;
            }

            switch (ClassifyUse(instructions, calls, use.Offset, acquire.Slot))
            {
                case ExtendedUseKind.Release:
                    releases.Add(use.Offset);
                    break;
                case ExtendedUseKind.LocalUse:
                    safeUses.Add(use.Offset);
                    break;
                case ExtendedUseKind.ReturnFromMethod:
                    escape = ChooseEscape(escape, (ResourceLifecycleBuckets.OwnershipTransferSuppressed, use.Offset, "rented array returned from method"));
                    break;
                case ExtendedUseKind.FieldStore:
                    escape = ChooseEscape(escape, (ResourceLifecycleBuckets.AliasOrFieldSuppressed, use.Offset, "rented array stored to a field"));
                    break;
                case ExtendedUseKind.AliasStore:
                    escape = ChooseEscape(escape, (ResourceLifecycleBuckets.AliasOrFieldSuppressed, use.Offset, "rented array aliased to another local/argument"));
                    break;
                case ExtendedUseKind.UnknownCall:
                    escape = ChooseEscape(escape, (ResourceLifecycleBuckets.CrossMethodSuppressed, use.Offset, "rented array passed to an unmodeled call"));
                    break;
                default:
                    escape = ChooseEscape(escape, (ResourceLifecycleBuckets.CrossMethodSuppressed, use.Offset, "rented array reaches an unmodeled use"));
                    break;
            }
        }

        if (escape is { } terminal)
        {
            facts.Add(new ResourceLifecycleFact(
                method, ResourceKind, terminal.Bucket, acquire.RentOffset, terminal.Offset,
                $"ArrayPool Rent at IL_{acquire.RentOffset:X4}: {terminal.Detail}."));
            return;
        }

        var releaseSet = releases.ToHashSet();

        if (ReachesNormalExitWithoutRelease(instructions, graph, terminators, acquire.StoreOffset, releaseSet))
        {
            facts.Add(new ResourceLifecycleFact(
                method, ResourceKind, ResourceLifecycleBuckets.NormalPathLeakCandidate, acquire.RentOffset, acquire.RentOffset,
                $"ArrayPool Rent at IL_{acquire.RentOffset:X4} reaches a normal return without a Return on some path."));
        }

        if (!ReleaseProtectedByFinally(instructions, graph.Regions, acquire, releases))
        {
            int? detail = releases.Count > 0 ? releases.Min() : null;
            facts.Add(new ResourceLifecycleFact(
                method, ResourceKind, ResourceLifecycleBuckets.ExceptionPathLeakCandidate, acquire.RentOffset, detail,
                $"ArrayPool Rent at IL_{acquire.RentOffset:X4} has no Return inside a covering finally/fault, so an exception before Return leaks."));
        }

        int? useAfter = FirstReachableAfter(graph, releases, safeUses);
        if (useAfter is { } useOffset)
        {
            facts.Add(new ResourceLifecycleFact(
                method, ResourceKind, ResourceLifecycleBuckets.UseAfterReturnCandidate, acquire.RentOffset, useOffset,
                $"Use of the rented array at IL_{useOffset:X4} is reachable after a Return."));
        }

        int? secondRelease = FirstReachableAfter(graph, releases, releases);
        if (secondRelease is { } releaseOffset)
        {
            facts.Add(new ResourceLifecycleFact(
                method, ResourceKind, ResourceLifecycleBuckets.DoubleReturnCandidate, acquire.RentOffset, releaseOffset,
                $"A second Return at IL_{releaseOffset:X4} is reachable after an earlier Return."));
        }
    }

    // Deterministic terminal-suppression precedence when an acquire escapes in several ways at
    // once: the most structurally definitive "we lost the array" wins, so the census is stable.
    static (string Bucket, int Offset, string Detail) ChooseEscape(
        (string Bucket, int Offset, string Detail)? current,
        (string Bucket, int Offset, string Detail) candidate)
    {
        if (current is not { } existing)
            return candidate;
        return Rank(candidate.Bucket) < Rank(existing.Bucket) ? candidate : existing;

        static int Rank(string bucket) => bucket switch
        {
            ResourceLifecycleBuckets.AliasOrFieldSuppressed => 0,
            ResourceLifecycleBuckets.CrossMethodSuppressed => 1,
            ResourceLifecycleBuckets.OwnershipTransferSuppressed => 2,
            _ => 3,
        };
    }

    // A normal (ret) exit is reachable from the acquire's store along a path with no Return.
    // Follows every CFG successor (including EH survivor edges, so a covering finally's Return
    // masks the path) but only ret terminals count as normal exits; throw/rethrow are dead ends
    // for the normal-path query and are measured separately by ReleaseProtectedByFinally.
    static bool ReachesNormalExitWithoutRelease(
        ImmutableArray<DecodedInstruction> instructions,
        BlockGraph graph,
        DecodedInstruction?[] terminators,
        int storeOffset,
        IReadOnlySet<int> releases)
    {
        int startBlock = graph.BlockIndexAt(storeOffset);
        if (startBlock < 0)
            return false;

        var visited = new HashSet<(int Block, bool Released)>();
        var stack = new Stack<(int Block, bool Released, int StartOffset)>();
        stack.Push((startBlock, false, storeOffset));

        while (stack.Count > 0)
        {
            var (blockIndex, releasedIn, blockStartOffset) = stack.Pop();
            if (!visited.Add((blockIndex, releasedIn)))
                continue;

            var block = graph.Blocks[blockIndex];
            bool released = releasedIn;
            foreach (var instruction in instructions)
            {
                if (instruction.Offset < block.Start || instruction.Offset >= block.End || instruction.Offset < blockStartOffset)
                    continue;
                if (releases.Contains(instruction.Offset))
                    released = true;
            }

            var terminator = terminators[blockIndex];
            if (terminator?.OpCode is ILOpCode.Ret)
            {
                if (!released)
                    return true;
                continue;
            }
            if (terminator?.OpCode is ILOpCode.Throw or ILOpCode.Rethrow)
                continue;

            foreach (int successor in block.Edges.Successors)
                stack.Push((successor, released, graph.Blocks[successor].Start));
        }

        return false;
    }

    // The acquire is exception-safe only when some Return runs inside a finally/fault handler
    // that actually covers the acquired value: either the store sits inside the protected try, or
    // it sits immediately before the try with only inert (non-throwing) ops in the gap. A Return
    // in a finally whose try starts *after* a throwing op (a call, an element access, ...) does
    // NOT cover an exception from that op - the finally is never entered - so the acquire is still
    // an exception-path leak candidate. (Reviewers GPT-5.5 + Gemini 3.1 Pro, PR #2447.)
    static bool ReleaseProtectedByFinally(
        ImmutableArray<DecodedInstruction> instructions,
        ImmutableArray<ExceptionRegionModel> regions,
        ArrayPoolAcquire acquire,
        IReadOnlyList<int> releases)
    {
        if (releases.Count == 0)
            return false;

        int storeNext = StoreNextOffset(instructions, acquire.StoreOffset);
        foreach (int release in releases)
        {
            foreach (var region in regions)
            {
                if (region.Kind is not (HandlerKind.Finally or HandlerKind.Fault) || !region.ContainsHandler(release))
                    continue;
                if (region.ContainsTry(acquire.StoreOffset))
                    return true;
                if (acquire.StoreOffset < region.TryStart && GapIsInert(instructions, storeNext, region.TryStart))
                    return true;
            }
        }
        return false;
    }

    static int StoreNextOffset(ImmutableArray<DecodedInstruction> instructions, int storeOffset)
    {
        foreach (var instruction in instructions)
            if (instruction.Offset == storeOffset)
                return instruction.NextOffset;
        return storeOffset + 1;
    }

    // Every instruction in [fromOffset, toOffset) is a straight-line, non-throwing op, so an
    // exception cannot escape the gap before the protecting try is entered. Deliberately narrow:
    // anything unrecognized (a call, an element access, a branch) makes the gap non-inert.
    static bool GapIsInert(ImmutableArray<DecodedInstruction> instructions, int fromOffset, int toOffset)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.Offset < fromOffset || instruction.Offset >= toOffset)
                continue;
            if (!IsInertGapOp(instruction.OpCode))
                return false;
        }
        return true;
    }

    static bool IsInertGapOp(ILOpCode opcode)
        => ArrayPoolRecognizers.IsSimpleArgumentPush(opcode)
            || IsLocalOrArgumentStore(opcode)
            || opcode is ILOpCode.Nop or ILOpCode.Dup or ILOpCode.Pop
                or ILOpCode.Ldc_i8 or ILOpCode.Ldc_r4 or ILOpCode.Ldc_r8;

    // The smallest target offset that is reachable strictly after some source offset: same block
    // and later in program order, or in any block reachable via >=1 successor edge (covers loops).
    static int? FirstReachableAfter(BlockGraph graph, IReadOnlyList<int> sources, IReadOnlyList<int> targets)
    {
        int? best = null;
        foreach (int source in sources)
        {
            int sourceBlock = graph.BlockIndexAt(source);
            if (sourceBlock < 0)
                continue;
            var reachable = SuccessorClosure(graph, sourceBlock);
            foreach (int target in targets)
            {
                if (source == target)
                    continue;
                int targetBlock = graph.BlockIndexAt(target);
                if (targetBlock < 0)
                    continue;
                bool after = reachable.Contains(targetBlock) || (targetBlock == sourceBlock && source < target);
                if (after && (best is null || target < best))
                    best = target;
            }
        }
        return best;
    }

    static HashSet<int> SuccessorClosure(BlockGraph graph, int fromBlock)
    {
        var seen = new HashSet<int>();
        var stack = new Stack<int>();
        foreach (int successor in graph.Blocks[fromBlock].Edges.Successors)
            stack.Push(successor);
        while (stack.Count > 0)
        {
            int block = stack.Pop();
            if (!seen.Add(block))
                continue;
            foreach (int successor in graph.Blocks[block].Edges.Successors)
                stack.Push(successor);
        }
        return seen;
    }

    static DecodedInstruction?[] BuildTerminators(BlockGraph graph, ImmutableArray<DecodedInstruction> instructions)
    {
        var terminators = new DecodedInstruction?[graph.Blocks.Length];
        foreach (var instruction in instructions)
        {
            int blockIndex = graph.BlockIndexAt(instruction.Offset);
            if (blockIndex >= 0)
                terminators[blockIndex] = instruction;
        }
        return terminators;
    }

    static ExtendedUseKind ClassifyUse(
        ImmutableArray<DecodedInstruction> instructions,
        IReadOnlyDictionary<int, MemberRef> calls,
        int loadOffset,
        int slot)
    {
        if (!ArrayPoolRecognizers.TryFindInstruction(instructions, loadOffset, out int index, out var load)
            || !ArrayPoolRecognizers.IsLoadLocal(load, slot))
            return ExtendedUseKind.Ambiguous;

        int extra = 0;
        for (int i = index + 1; i < instructions.Length; i++)
        {
            var instruction = instructions[i];
            var opcode = instruction.OpCode;
            if (ArrayPoolRecognizers.IsSimpleArgumentPush(opcode))
            {
                extra++;
                continue;
            }
            if (opcode == ILOpCode.Ldlen)
                return extra == 0 ? ExtendedUseKind.LocalUse : ExtendedUseKind.Ambiguous;
            if (ArrayPoolRecognizers.IsElementRead(opcode))
                return extra == 1 ? ExtendedUseKind.LocalUse : ExtendedUseKind.Ambiguous;
            if (ArrayPoolRecognizers.IsElementStore(opcode))
                return extra == 2 ? ExtendedUseKind.LocalUse : ExtendedUseKind.Ambiguous;
            if (opcode is ILOpCode.Ret)
                return extra == 0 ? ExtendedUseKind.ReturnFromMethod : ExtendedUseKind.Ambiguous;
            if (opcode is ILOpCode.Stfld or ILOpCode.Stsfld)
                return ExtendedUseKind.FieldStore;
            if (IsLocalOrArgumentStore(opcode))
                return ExtendedUseKind.AliasStore;
            if (calls.TryGetValue(instruction.Offset, out var callee))
            {
                if (ArrayPoolRecognizers.IsArrayPoolReturn(callee) && extra < callee.ParameterTypes.Length)
                    return ExtendedUseKind.Release;
                return ExtendedUseKind.UnknownCall;
            }
            return ExtendedUseKind.Ambiguous;
        }

        return ExtendedUseKind.Ambiguous;
    }

    static bool IsLocalOrArgumentStore(ILOpCode opcode)
        => opcode is ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3
            or ILOpCode.Stloc_s or ILOpCode.Stloc or ILOpCode.Starg_s or ILOpCode.Starg;

    static readonly ResourceLifecycleCensusResult Empty = new([], 0);

    enum ExtendedUseKind
    {
        Release,
        LocalUse,
        ReturnFromMethod,
        FieldStore,
        AliasStore,
        UnknownCall,
        Ambiguous,
    }
}
