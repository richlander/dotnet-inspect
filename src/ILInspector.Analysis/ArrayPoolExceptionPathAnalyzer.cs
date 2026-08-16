using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

static class ArrayPoolExceptionPathAnalyzer
{
    internal static LeakExitKind PathExitsWithoutRelease(
        ImmutableArray<DecodedInstruction> instructions,
        BlockGraph graph,
        IReadOnlyDictionary<int, MemberRef> calls,
        int startOffset,
        ImmutableArray<int> releases)
    {
        int startBlock = graph.BlockIndexAt(startOffset);
        if (startBlock < 0)
            return LeakExitKind.None;

        var releaseSet = releases.ToHashSet();
        var visited = new HashSet<(int Block, bool Released)>();
        var stack = new Stack<(int Block, bool Released, int StartOffset)>();
        stack.Push((startBlock, Released: false, StartOffset: startOffset));
        bool sawExceptionExit = false;

        while (stack.Count > 0)
        {
            var (blockIndex, releasedIn, blockStartOffset) = stack.Pop();
            if (!visited.Add((blockIndex, releasedIn)))
                continue;

            var block = graph.Blocks[blockIndex];
            bool released = ProcessBlockForRelease(instructions, calls, block, blockStartOffset, releaseSet, releasedIn);
            var successors = block.Edges.Successors;
            if (!released && block.Edges.ExitsMethod && successors.Count == 0)
            {
                if (BlockExitsByException(instructions, calls, block))
                    sawExceptionExit = true;
                else
                    return LeakExitKind.Normal;
            }
            foreach (int successor in successors)
                stack.Push((successor, released, graph.Blocks[successor].Start));
        }

        return sawExceptionExit ? LeakExitKind.Exception : LeakExitKind.None;
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

    static bool BlockExitsByException(
        ImmutableArray<DecodedInstruction> instructions,
        IReadOnlyDictionary<int, MemberRef> calls,
        InstructionBlock block)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.Offset < block.Start || instruction.Offset >= block.End)
                continue;
            if (IsDefinitelyThrowingCall(instruction, calls))
                return true;
        }

        return false;
    }

    internal static bool ReachesInSameBlock(BlockGraph graph, int fromOffset, int toOffset)
    {
        int fromBlock = graph.BlockIndexAt(fromOffset);
        int toBlock = graph.BlockIndexAt(toOffset);
        return fromBlock >= 0 && fromBlock == toBlock && fromOffset < toOffset;
    }

    static bool ReleasedBeforeUseInSameBlock(BlockGraph graph, ImmutableArray<int> releases, int useOffset)
        => releases.Any(release => ReachesInSameBlock(graph, release, useOffset));

    internal static ImmutableArray<ArrayPoolExceptionBoundary> UnprotectedThrowingBoundaries(
        BlockGraph graph,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions,
        IReadOnlySet<(int TryOffset, int TryLength, int HandlerOffset)> catchAllCleanup,
        ImmutableArray<int> releases,
        ImmutableArray<ArrayPoolExceptionBoundary> throwingBoundaries)
    {
        var result = ImmutableArray.CreateBuilder<ArrayPoolExceptionBoundary>();
        var seenOffsets = new HashSet<int>();
        foreach (var boundary in throwingBoundaries.OrderBy(static boundary => boundary.ILOffset))
        {
            if (seenOffsets.Add(boundary.ILOffset)
                && !ReleasedBeforeUseInSameBlock(graph, releases, boundary.ILOffset)
                && !HasCleanupReleaseForUse(
                    exceptionRegions,
                    catchAllCleanup,
                    releases,
                    boundary.ILOffset))
            {
                result.Add(boundary);
            }
        }

        return result.ToImmutable();
    }

    static bool HasCleanupReleaseForUse(
        IReadOnlyCollection<ExceptionRegion> exceptionRegions,
        IReadOnlySet<(int TryOffset, int TryLength, int HandlerOffset)> catchAllCleanup,
        ImmutableArray<int> releases,
        int useOffset)
    {
        // Exception regions are ordered innermost-first (ECMA-335 II.19: most-nested clauses
        // precede enclosing ones), which is also EH search order. Walk the handlers that cover the
        // boundary in that order: a `finally`/`fault` always runs, so a release inside one protects
        // the boundary. A catch-all (`catch {}` / `catch (Exception)`) protects the boundary only
        // if it is the FIRST catch/filter that covers it - any earlier catch/filter (a sibling
        // typed catch, or an inner catch on a nested try) would handle the exception first and, if
        // it does not release, the array still leaks.
        bool interceptingCatchSeen = false;
        foreach (var region in exceptionRegions)
        {
            if (!ContainsOffset(region.TryOffset, region.TryLength, useOffset))
                continue;

            if (region.Kind is ExceptionRegionKind.Finally or ExceptionRegionKind.Fault)
            {
                if (releases.Any(release => ContainsOffset(region.HandlerOffset, region.HandlerLength, release)))
                    return true;
                continue;
            }

            if (region.Kind is not (ExceptionRegionKind.Catch or ExceptionRegionKind.Filter))
                continue;

            if (!interceptingCatchSeen
                && catchAllCleanup.Contains((region.TryOffset, region.TryLength, region.HandlerOffset))
                && releases.Any(release => ContainsOffset(region.HandlerOffset, region.HandlerLength, release)))
                return true;

            interceptingCatchSeen = true;
        }

        return false;
    }

    // Try-range/handler identity of catch-all clauses (`catch {}` = `System.Object`, or
    // `catch (Exception)`) - the catch shapes that catch every managed exception (non-CLS throws
    // are wrapped as RuntimeWrappedException by the default
    // RuntimeCompatibility(WrapNonExceptionThrows=true)). Whether such a clause actually protects a
    // given boundary is decided per-boundary in HasCleanupReleaseForUse, which fails closed if an
    // earlier catch/filter could intercept the exception first. Empty when no catch-type resolver
    // is supplied (e.g. direct AnalyzeMethod callers), preserving the prior finally/fault-only
    // behavior. Conditional releases inside the handler are credited at the same fidelity as the
    // existing finally/fault check (containment, not post-domination).
    internal static IReadOnlySet<(int TryOffset, int TryLength, int HandlerOffset)> ComputeCreditableCatchCleanup(
        IReadOnlyCollection<ExceptionRegion> exceptionRegions,
        Func<int, TypeRef?>? resolveCatchType)
    {
        if (resolveCatchType is null)
            return EmptyCatchCleanup;

        HashSet<(int, int, int)>? creditable = null;
        foreach (var region in exceptionRegions)
        {
            if (region.Kind is not ExceptionRegionKind.Catch || region.CatchType.IsNil)
                continue;
            var catchType = resolveCatchType(MetadataTokens.GetToken(region.CatchType));
            if (catchType is null
                || !(FrameworkIdentity.IsCoreLibraryType(catchType, "System", "Object")
                    || FrameworkIdentity.IsCoreLibraryType(catchType, "System", "Exception")))
                continue;
            (creditable ??= []).Add((region.TryOffset, region.TryLength, region.HandlerOffset));
        }

        return creditable is null ? EmptyCatchCleanup : creditable;
    }

    static readonly IReadOnlySet<(int TryOffset, int TryLength, int HandlerOffset)> EmptyCatchCleanup =
        ImmutableHashSet<(int, int, int)>.Empty;

    internal static TypeRef? ResolveCatchTypeRef(
        MetadataReader reader,
        EntityHandle handle,
        GenericScope scope)
        => handle.Kind switch
    {
        HandleKind.TypeDefinition => TypeRefDecoder.Instance.GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeReference => TypeRefDecoder.Instance.GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
        HandleKind.TypeSpecification => TypeRefDecoder.Instance.GetTypeFromSpecification(reader, scope, (TypeSpecificationHandle)handle, 0),
        _ => null,
    };

    static bool ContainsOffset(int start, int length, int offset)
        => offset >= start && offset < start + length;

    internal static ArrayPoolExceptionBoundary? FindBoundaryAfterSetup(
        ImmutableArray<DecodedInstruction> instructions,
        ReachingDefinitionsResult reaching,
        IReadOnlyDictionary<int, MemberRef> calls,
        ArrayPoolExceptionBoundary setup,
        int depth = 0)
    {
        if (depth >= 4)
            return null;

        if (!ArrayPoolUseClassifier.TryFindInstruction(
            instructions,
            setup.ILOffset,
            out int setupIndex,
            out var setupInstruction))
        {
            return null;
        }

        // Constructor signatures return void, but newobj pushes the constructed value.
        if (setupInstruction.OpCode != ILOpCode.Newobj
            && FrameworkIdentity.IsCoreLibraryType(
                setup.Operation.ReturnType,
                "System",
                "Void"))
        {
            // Roslyn can initialize a value-type wrapper local through
            // ldloca + call .ctor instead of newobj + stloc.
            if (TryFindInPlaceWrapperLocal(
                    instructions,
                    setupIndex,
                    setup.Operation,
                    out int slot))
            {
                return FindBoundaryAfterInPlaceSetup(
                    instructions,
                    reaching,
                    calls,
                    setupIndex,
                    slot,
                    depth);
            }

            return null;
        }

        int extraArguments = 0;
        for (int i = setupIndex + 1;
            i < instructions.Length && i <= setupIndex + 16;
            i++)
        {
            var instruction = instructions[i];
            if (instruction.OpCode == ILOpCode.Nop)
                continue;
            if (IsSetupArgumentPush(instruction.OpCode))
            {
                extraArguments++;
                continue;
            }

            if (extraArguments == 0
                && ArrayPoolUseClassifier.TryReadStoreLocal(instruction, out int slot))
            {
                var definition = reaching.Definitions.FirstOrDefault(candidate =>
                    !candidate.IsArgument
                    && candidate.Slot == slot
                    && candidate.Offset == instruction.Offset);
                if (definition is null)
                    return null;

                return FindBoundaryFromLocalDefinition(
                    instructions,
                    reaching,
                    calls,
                    definition,
                    depth);
            }

            if (!calls.TryGetValue(instruction.Offset, out var callee))
                return null;

            int consumedArguments =
                callee.ParameterTypes.Length
                + (instruction.OpCode != ILOpCode.Newobj && callee.HasThis
                    ? 1
                    : 0);
            if (consumedArguments <= extraArguments)
            {
                extraArguments -= consumedArguments;
                if (instruction.OpCode == ILOpCode.Newobj
                    || !FrameworkIdentity.IsCoreLibraryType(
                        callee.ReturnType,
                        "System",
                        "Void"))
                {
                    extraArguments++;
                }

                continue;
            }

            var boundary =
                new ArrayPoolExceptionBoundary(instruction.Offset, callee);
            if (!ArrayPoolUseClassifier.IsNonThrowingSetupBoundary(callee))
            {
                return boundary;
            }

            if (FrameworkIdentity.IsCoreLibraryType(
                callee.ReturnType,
                "System",
                "Void"))
            {
                return null;
            }

            extraArguments = 0;
        }

        return null;
    }

    static ArrayPoolExceptionBoundary? FindBoundaryFromLocalDefinition(
        ImmutableArray<DecodedInstruction> instructions,
        ReachingDefinitionsResult reaching,
        IReadOnlyDictionary<int, MemberRef> calls,
        LocalDefinition definition,
        int depth)
        => FindBoundaryFromLocalUses(
            instructions,
            reaching,
            calls,
            reaching.UsesOf(definition),
            definition.Slot,
            depth);

    static ArrayPoolExceptionBoundary? FindBoundaryFromLocalUses(
        ImmutableArray<DecodedInstruction> instructions,
        ReachingDefinitionsResult reaching,
        IReadOnlyDictionary<int, MemberRef> calls,
        IEnumerable<LocalUse> uses,
        int slot,
        int depth)
    {
        if (depth >= 4)
            return null;

        foreach (var use in uses.OrderBy(static use => use.Offset))
        {
            var classification = ArrayPoolUseClassifier.ClassifyUse(
                instructions,
                calls,
                use.Offset,
                slot);
            if (classification.CandidateShape
                    != "cross-method-suppressed"
                || classification.Boundary is not { } boundary)
            {
                if (classification.CandidateShape
                        == "alias-or-field-suppressed"
                    && FindBoundaryAfterLocalAlias(
                        instructions,
                        reaching,
                        calls,
                        use.Offset,
                        depth + 1) is { } aliasBoundary)
                {
                    return aliasBoundary;
                }
                continue;
            }
            if (!classification.NonThrowingSetupBoundary)
                return boundary;

            if (FindBoundaryAfterSetup(
                    instructions,
                    reaching,
                    calls,
                    boundary,
                    depth + 1) is { } downstream)
            {
                return downstream;
            }
        }

        return null;
    }

    static ArrayPoolExceptionBoundary? FindBoundaryAfterLocalAlias(
        ImmutableArray<DecodedInstruction> instructions,
        ReachingDefinitionsResult reaching,
        IReadOnlyDictionary<int, MemberRef> calls,
        int loadOffset,
        int depth)
    {
        if (depth >= 4
            || !ArrayPoolUseClassifier.TryFindInstruction(
                instructions,
                loadOffset,
                out _,
                out var load)
            || !ArrayPoolUseClassifier.TryFindNextNonNop(
                instructions,
                load.NextOffset,
                out var store)
            || !ArrayPoolUseClassifier.TryReadStoreLocal(store, out int aliasSlot))
        {
            return null;
        }

        var definition = reaching.Definitions.FirstOrDefault(candidate =>
            !candidate.IsArgument
            && candidate.Slot == aliasSlot
            && candidate.Offset == store.Offset);
        return definition is null
            ? null
            : FindBoundaryFromLocalDefinition(
                instructions,
                reaching,
                calls,
                definition,
                depth);
    }

    static ArrayPoolExceptionBoundary? FindBoundaryAfterInPlaceSetup(
        ImmutableArray<DecodedInstruction> instructions,
        ReachingDefinitionsResult reaching,
        IReadOnlyDictionary<int, MemberRef> calls,
        int setupIndex,
        int slot,
        int depth)
        => FindBoundaryFromLocalUses(
            instructions,
            reaching,
            calls,
            FindNormalFlowLocalUses(
                instructions,
                calls,
                setupIndex,
                slot),
            slot,
            depth);

    static ImmutableArray<LocalUse> FindNormalFlowLocalUses(
        ImmutableArray<DecodedInstruction> instructions,
        IReadOnlyDictionary<int, MemberRef> calls,
        int setupIndex,
        int slot)
    {
        if (setupIndex + 1 >= instructions.Length)
            return [];

        var definitionReceivers = new HashSet<int>();
        for (int i = 0; i < instructions.Length; i++)
        {
            if (TryFindAddressWriteLocal(
                    instructions,
                    i,
                    out int writeSlot,
                    out int receiverIndex)
                && writeSlot == slot)
            {
                definitionReceivers.Add(receiverIndex);
                continue;
            }

            if (calls.TryGetValue(instructions[i].Offset, out var member)
                && TryFindInPlaceWrapperLocal(
                    instructions,
                    i,
                    member,
                    out int constructorSlot,
                    out receiverIndex)
                && constructorSlot == slot)
            {
                definitionReceivers.Add(receiverIndex);
            }
        }

        var indexByOffset = instructions
            .Select((instruction, index) =>
                (instruction.Offset, Index: index))
            .ToDictionary(static pair => pair.Offset, static pair => pair.Index);
        var pending = new Stack<int>();
        var visited = new HashSet<int>();
        var uses = new Dictionary<int, LocalUse>();

        // The local is defined only when the constructor returns, so follow
        // normal IL edges and stop before any later write to the same slot.
        pending.Push(setupIndex + 1);
        while (pending.TryPop(out int index))
        {
            if (!visited.Add(index))
                continue;

            var instruction = instructions[index];
            if (definitionReceivers.Contains(index)
                || (ArrayPoolUseClassifier.TryReadStoreLocal(instruction, out int storedSlot)
                    && storedSlot == slot))
            {
                continue;
            }

            if (ArrayPoolUseClassifier.IsLoadLocalOrAddress(instruction, slot))
            {
                bool address = ArrayPoolUseClassifier.TryReadLoadLocalAddress(
                    instruction,
                    out _);
                uses.TryAdd(
                    instruction.Offset,
                    new LocalUse(
                        slot,
                        IsArgument: false,
                        instruction.Offset,
                        address,
                        []));
            }

            if (instruction.LeavesRegion)
                continue;

            foreach (int target in instruction.BranchTargets)
                if (indexByOffset.TryGetValue(target, out int targetIndex))
                    pending.Push(targetIndex);

            if (instruction.FallsThrough && index + 1 < instructions.Length)
                pending.Push(index + 1);
        }

        return uses.Values
            .OrderBy(static use => use.Offset)
            .ToImmutableArray();
    }

    static bool TryFindAddressWriteLocal(
        ImmutableArray<DecodedInstruction> instructions,
        int writeIndex,
        out int slot,
        out int receiverIndex)
    {
        slot = -1;
        receiverIndex = -1;
        int pushedValues = instructions[writeIndex].OpCode switch
        {
            ILOpCode.Initobj => 0,
            ILOpCode.Stobj or ILOpCode.Cpobj => 1,
            _ => -1,
        };
        if (pushedValues < 0)
            return false;

        int index = writeIndex - 1;
        for (int remaining = pushedValues; remaining > 0; remaining--)
        {
            while (index >= 0 && instructions[index].OpCode == ILOpCode.Nop)
                index--;
            if (index < 0
                || !IsSetupArgumentPush(instructions[index].OpCode))
            {
                return false;
            }
            index--;
        }

        while (index >= 0 && instructions[index].OpCode == ILOpCode.Nop)
            index--;
        if (index < 0
            || !ArrayPoolUseClassifier.TryReadLoadLocalAddress(instructions[index], out slot))
        {
            return false;
        }

        receiverIndex = index;
        return true;
    }

    static bool TryFindInPlaceWrapperLocal(
        ImmutableArray<DecodedInstruction> instructions,
        int setupIndex,
        MemberRef member,
        out int slot)
        => TryFindInPlaceWrapperLocal(
            instructions,
            setupIndex,
            member,
            out slot,
            out _);

    static bool TryFindInPlaceWrapperLocal(
        ImmutableArray<DecodedInstruction> instructions,
        int setupIndex,
        MemberRef member,
        out int slot,
        out int receiverIndex)
    {
        slot = -1;
        receiverIndex = -1;
        if (instructions[setupIndex].OpCode != ILOpCode.Call
            || !member.HasThis
            || member.Name != ".ctor"
            || !ArrayPoolUseClassifier.IsTypedWrapperType(member.DeclaringType))
        {
            return false;
        }

        int index = setupIndex - 1;
        for (int remaining = member.ParameterTypes.Length;
            remaining > 0;
            remaining--)
        {
            while (index >= 0 && instructions[index].OpCode == ILOpCode.Nop)
                index--;
            if (index < 0
                || !IsSetupArgumentPush(instructions[index].OpCode))
            {
                return false;
            }
            index--;
        }

        while (index >= 0 && instructions[index].OpCode == ILOpCode.Nop)
            index--;
        if (index < 0
            || !ArrayPoolUseClassifier.TryReadLoadLocalAddress(instructions[index], out slot))
        {
            return false;
        }

        receiverIndex = index;
        return true;
    }

    static bool IsSetupArgumentPush(ILOpCode opcode)
        => ArrayPoolUseClassifier.IsSimpleArgumentPush(opcode)
            || opcode is ILOpCode.Ldc_i8 or ILOpCode.Ldc_r4 or ILOpCode.Ldc_r8
                or ILOpCode.Ldarga_s or ILOpCode.Ldarga
                or ILOpCode.Ldloca_s or ILOpCode.Ldloca
                or ILOpCode.Ldstr
                or ILOpCode.Ldsfld or ILOpCode.Ldsflda
                or ILOpCode.Ldtoken or ILOpCode.Ldftn
                or ILOpCode.Sizeof or ILOpCode.Arglist;

    internal enum LeakExitKind
    {
        None,
        Normal,
        Exception,
    }
}
