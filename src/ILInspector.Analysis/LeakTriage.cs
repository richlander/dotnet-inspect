using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

public sealed record LeakTriageFinding(
    MethodIdentity Method,
    string Shape,
    string Evidence,
    string Severity,
    int RentOffset,
    int? ILOffset);

public static class LeakTriageAnalyzer
{
    public static ImmutableArray<LeakTriageFinding> AnalyzeAssembly(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var findings = ImmutableArray.CreateBuilder<LeakTriageFinding>();
        foreach (var body in AnalysisMethodBodies.Enumerate(path))
            findings.AddRange(AnalyzeMethod(body.Method, body.Il, body.ExceptionRegions, body.ResolveMethod));

        return findings.ToImmutable();
    }

    public static ImmutableArray<LeakTriageFinding> AnalyzeMethod(
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
            return [];

        try
        {
            var instructions = InstructionDecoder.Decode(il);
            var graph = BlockGraph.Build(il.Length, instructions, exceptionRegions);
            if (!graph.IsComplete)
                return [];

            var reaching = ReachingDefinitions.Analyze(il, AnalysisMethodBodies.ArgumentSlotCount(method), exceptionRegions);
            if (!reaching.IsComplete)
                return [];

            var calls = ArrayPoolRecognizers.BuildCallMap(instructions, resolveMethod);
            var rents = ArrayPoolRecognizers.FindAcquires(instructions, graph, reaching, calls).ToImmutableArray();
            if (rents.Length == 0)
                return [];

            var findings = ImmutableArray.CreateBuilder<LeakTriageFinding>();
            foreach (var rent in rents)
                AnalyzeRent(method, instructions, graph, reaching, calls, rent, findings);

            return findings.ToImmutable();
        }
        catch (Exception ex) when (AnalysisMethodBodies.IsRecoverable(ex))
        {
            return [];
        }
    }

    static void AnalyzeRent(
        MethodIdentity method,
        ImmutableArray<DecodedInstruction> instructions,
        BlockGraph graph,
        ReachingDefinitionsResult reaching,
        IReadOnlyDictionary<int, MemberRef> calls,
        ArrayPoolAcquire rent,
        ImmutableArray<LeakTriageFinding>.Builder findings)
    {
        var releases = ImmutableArray.CreateBuilder<int>();
        var safeUses = ImmutableArray.CreateBuilder<int>();
        bool ambiguous = false;

        foreach (var use in reaching.UsesOf(rent.Definition))
        {
            if (use.Address)
            {
                ambiguous = true;
                break;
            }

            var kind = ClassifyUse(instructions, calls, use.Offset, rent.Slot);
            switch (kind)
            {
                case UseKind.Release:
                    releases.Add(use.Offset);
                    break;
                case UseKind.LocalUse:
                    safeUses.Add(use.Offset);
                    break;
                default:
                    ambiguous = true;
                    break;
            }
        }

        if (ambiguous)
            return;

        var releaseOffsets = releases.ToImmutable();
        var safeUseOffsets = safeUses.ToImmutable();

        // Multiple releases often encode correlated branch predicates (`if (c) return; if (!c) return`).
        // Without predicate facts, fail closed on leaks and only keep same-block misuse shapes below.
        if (releaseOffsets.Length <= 1
            && PathExitsWithoutRelease(instructions, graph, calls, rent.StoreOffset, releaseOffsets))
        {
            findings.Add(new LeakTriageFinding(
                method,
                "arraypool-rent-not-returned",
                $"ArrayPool<T>.Shared.Rent at IL_{rent.RentOffset:X4} is not returned on every modeled path.",
                "high",
                rent.RentOffset,
                rent.RentOffset));
        }

        // Same-block reachability avoids inventing impossible paths across correlated branches.
        if (releaseOffsets.Length > 0
            && safeUseOffsets.Any(use => releaseOffsets.Any(release => ReachesInSameBlock(graph, release, use))))
        {
            findings.Add(new LeakTriageFinding(
                method,
                "arraypool-use-after-return",
                $"Use of rented array reaches past Return at IL_{releaseOffsets.Min():X4}.",
                "high",
                rent.RentOffset,
                safeUseOffsets.Where(use => releaseOffsets.Any(release => ReachesInSameBlock(graph, release, use))).Min()));
        }

        if (releaseOffsets.Length > 1
            && releaseOffsets.Any(first => releaseOffsets.Any(second => first != second && ReachesInSameBlock(graph, first, second))))
        {
            findings.Add(new LeakTriageFinding(
                method,
                "arraypool-double-return",
                $"Rented array can reach a second Return after IL_{releaseOffsets.Min():X4}.",
                "high",
                rent.RentOffset,
                releaseOffsets.Skip(1).DefaultIfEmpty(releaseOffsets[0]).Min()));
        }
    }

    static bool PathExitsWithoutRelease(
        ImmutableArray<DecodedInstruction> instructions,
        BlockGraph graph,
        IReadOnlyDictionary<int, MemberRef> calls,
        int startOffset,
        ImmutableArray<int> releases)
    {
        int startBlock = graph.BlockIndexAt(startOffset);
        if (startBlock < 0)
            return false;

        var releaseSet = releases.ToHashSet();
        var visited = new HashSet<(int Block, bool Released)>();
        var stack = new Stack<(int Block, bool Released, int StartOffset)>();
        stack.Push((startBlock, Released: false, StartOffset: startOffset));

        while (stack.Count > 0)
        {
            var (blockIndex, releasedIn, blockStartOffset) = stack.Pop();
            if (!visited.Add((blockIndex, releasedIn)))
                continue;

            var block = graph.Blocks[blockIndex];
            bool released = ProcessBlockForRelease(instructions, calls, block, blockStartOffset, releaseSet, releasedIn);
            var successors = block.Edges.Successors;
            if (!released && block.Edges.ExitsMethod && successors.Count == 0)
                return true;
            foreach (int successor in successors)
                stack.Push((successor, released, graph.Blocks[successor].Start));
        }

        return false;
    }

    static bool ProcessBlockForRelease(
        ImmutableArray<DecodedInstruction> instructions,
        IReadOnlyDictionary<int, MemberRef> calls,
        InstructionBlock block,
        int startOffset,
        IReadOnlySet<int> releases,
        bool released)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.Offset < block.Start || instruction.Offset >= block.End)
                continue;
            if (instruction.Offset < startOffset)
                continue;
            if (releases.Contains(instruction.Offset))
                released = true;
            if (!released && IsDefinitelyThrowingCall(instruction, calls))
                return false;
        }
        return released;
    }

    static bool IsDefinitelyThrowingCall(DecodedInstruction instruction, IReadOnlyDictionary<int, MemberRef> calls)
        => instruction.OpCode is ILOpCode.Throw or ILOpCode.Rethrow
           || (calls.TryGetValue(instruction.Offset, out var callee)
               && FrameworkIdentity.IsCoreLibraryType(callee.DeclaringType, "System", "ThrowHelper"));

    static bool ReachesInSameBlock(BlockGraph graph, int fromOffset, int toOffset)
    {
        int fromBlock = graph.BlockIndexAt(fromOffset);
        int toBlock = graph.BlockIndexAt(toOffset);
        return fromBlock >= 0 && fromBlock == toBlock && fromOffset < toOffset;
    }

    static UseKind ClassifyUse(
        ImmutableArray<DecodedInstruction> instructions,
        IReadOnlyDictionary<int, MemberRef> calls,
        int loadOffset,
        int slot)
    {
        if (!ArrayPoolRecognizers.TryFindInstruction(instructions, loadOffset, out int index, out var load)
            || !ArrayPoolRecognizers.IsLoadLocal(load, slot))
            return UseKind.Ambiguous;

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
                return extra == 0 ? UseKind.LocalUse : UseKind.Ambiguous;
            if (ArrayPoolRecognizers.IsElementRead(opcode))
                return extra == 1 ? UseKind.LocalUse : UseKind.Ambiguous;
            if (ArrayPoolRecognizers.IsElementStore(opcode))
                return extra == 2 ? UseKind.LocalUse : UseKind.Ambiguous;
            if (calls.TryGetValue(instruction.Offset, out var callee))
            {
                if (ArrayPoolRecognizers.IsArrayPoolReturn(callee) && extra < callee.ParameterTypes.Length)
                    return UseKind.Release;
                return UseKind.Ambiguous;
            }
            return UseKind.Ambiguous;
        }

        return UseKind.Ambiguous;
    }

    enum UseKind
    {
        Release,
        LocalUse,
        Ambiguous,
    }
}
