using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

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

        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var assembly = reader.GetAssemblyDefinition();
        var assemblyName = reader.GetString(assembly.Name);
        var mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
        var findings = ImmutableArray.CreateBuilder<LeakTriageFinding>();

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var methodDef = reader.GetMethodDefinition(methodHandle);
                if (methodDef.RelativeVirtualAddress == 0)
                    continue;

                try
                {
                    var scope = CreateScope(reader, typeDef, methodDef);
                    var signature = methodDef.DecodeSignature(TypeRefDecoder.Instance, scope);
                    var method = new MethodIdentity(
                        assemblyName,
                        mvid,
                        TypeRefDecoder.Instance.GetTypeFromDefinition(reader, typeHandle, 0),
                        reader.GetString(methodDef.Name),
                        signature.ParameterTypes,
                        signature.ReturnType,
                        MetadataTokens.GetToken(methodHandle),
                        (methodDef.Attributes & MethodAttributes.Static) != 0);
                    var body = peReader.GetMethodBody(methodDef.RelativeVirtualAddress);
                    findings.AddRange(AnalyzeMethod(
                        method,
                        body.GetILBytes() ?? [],
                        body.ExceptionRegions,
                        token => MemberResolver.ResolveMethod(reader, MetadataTokens.EntityHandle(token), scope)));
                }
                catch (Exception ex) when (IsRecoverable(ex))
                {
                    // Correctness triage is fail-closed: malformed or unsupported method
                    // evidence yields no accusations for that method.
                }
            }
        }

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

            var reaching = ReachingDefinitions.Analyze(il, ArgumentSlotCount(method), exceptionRegions);
            if (!reaching.IsComplete)
                return [];

            var calls = BuildCallMap(instructions, resolveMethod);
            var rents = FindRents(method, instructions, graph, reaching, calls).ToImmutableArray();
            if (rents.Length == 0)
                return [];

            var findings = ImmutableArray.CreateBuilder<LeakTriageFinding>();
            foreach (var rent in rents)
                AnalyzeRent(method, instructions, graph, reaching, calls, rent, findings);

            return findings.ToImmutable();
        }
        catch (Exception ex) when (IsRecoverable(ex))
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
        RentedLocal rent,
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
            var successors = LifecycleSuccessors(instructions, graph, block).ToArray();
            if (!released && block.Edges.ExitsMethod && successors.Length == 0)
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

    static IEnumerable<int> LifecycleSuccessors(ImmutableArray<DecodedInstruction> instructions, BlockGraph graph, InstructionBlock block)
    {
        if (LastInstruction(instructions, block) is { LeavesRegion: true } terminator)
        {
            var finallyHandlers = graph.Regions
                .Where(region => region.Kind is HandlerKind.Finally or HandlerKind.Fault && region.ContainsTry(terminator.Offset))
                .OrderBy(region => region.TryEnd - region.TryStart)
                .Select(region => graph.BlockIndexAt(region.HandlerStart))
                .Where(index => index >= 0)
                .Distinct()
                .ToArray();
            if (finallyHandlers.Length > 0)
                return [finallyHandlers[0]];
        }

        return block.Edges.Successors;
    }

    static DecodedInstruction? LastInstruction(ImmutableArray<DecodedInstruction> instructions, InstructionBlock block)
    {
        DecodedInstruction? last = null;
        foreach (var instruction in instructions)
            if (instruction.Offset >= block.Start && instruction.Offset < block.End)
                last = instruction;
        return last;
    }

    static IEnumerable<RentedLocal> FindRents(
        MethodIdentity method,
        ImmutableArray<DecodedInstruction> instructions,
        BlockGraph graph,
        ReachingDefinitionsResult reaching,
        IReadOnlyDictionary<int, MemberRef> calls)
    {
        foreach (var instruction in instructions)
        {
            if (!calls.TryGetValue(instruction.Offset, out var callee) || !IsArrayPoolRent(callee))
                continue;
            if (!RentUsesSharedReceiver(instructions, graph, calls, instruction.Offset))
                continue;
            if (!TryFindNextNonNop(instructions, instruction.NextOffset, out var store)
                || !TryReadStoreLocal(store, out int slot))
                continue;

            var definition = reaching.Definitions.FirstOrDefault(d =>
                !d.IsArgument && d.Slot == slot && d.Offset == store.Offset);
            if (definition is null)
                continue;

            yield return new RentedLocal(instruction.Offset, store.Offset, slot, definition);
        }
    }

    static bool RentUsesSharedReceiver(
        ImmutableArray<DecodedInstruction> instructions,
        BlockGraph graph,
        IReadOnlyDictionary<int, MemberRef> calls,
        int rentOffset)
    {
        int blockIndex = graph.BlockIndexAt(rentOffset);
        if (blockIndex < 0)
            return false;
        var block = graph.Blocks[blockIndex];
        for (int i = instructions.Length - 1, inspected = 0; i >= 0; i--)
        {
            var instruction = instructions[i];
            if (instruction.Offset < block.Start)
                return false;
            if (instruction.Offset >= rentOffset || instruction.OpCode == ILOpCode.Nop)
                continue;
            if (calls.TryGetValue(instruction.Offset, out var callee))
                return IsArrayPoolSharedGetter(callee);
            if (!IsSimpleArgumentPush(instruction.OpCode))
                return false;
            if (++inspected > 4)
                return false;
        }
        return false;
    }

    static UseKind ClassifyUse(
        ImmutableArray<DecodedInstruction> instructions,
        IReadOnlyDictionary<int, MemberRef> calls,
        int loadOffset,
        int slot)
    {
        if (!TryFindInstruction(instructions, loadOffset, out int index, out var load)
            || !IsLoadLocal(load, slot))
            return UseKind.Ambiguous;

        int extra = 0;
        for (int i = index + 1; i < instructions.Length; i++)
        {
            var instruction = instructions[i];
            var opcode = instruction.OpCode;
            if (IsSimpleArgumentPush(opcode))
            {
                extra++;
                continue;
            }
            if (opcode == ILOpCode.Ldlen)
                return extra == 0 ? UseKind.LocalUse : UseKind.Ambiguous;
            if (IsElementRead(opcode))
                return extra == 1 ? UseKind.LocalUse : UseKind.Ambiguous;
            if (IsElementStore(opcode))
                return extra == 2 ? UseKind.LocalUse : UseKind.Ambiguous;
            if (calls.TryGetValue(instruction.Offset, out var callee))
            {
                if (IsArrayPoolReturn(callee) && extra < callee.ParameterTypes.Length)
                    return UseKind.Release;
                return UseKind.Ambiguous;
            }
            return UseKind.Ambiguous;
        }

        return UseKind.Ambiguous;
    }

    static IReadOnlyDictionary<int, MemberRef> BuildCallMap(
        ImmutableArray<DecodedInstruction> instructions,
        Func<int, MemberRef> resolveMethod)
    {
        var calls = new Dictionary<int, MemberRef>();
        foreach (var instruction in instructions)
        {
            if (instruction.OpCode is not (ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj))
                continue;
            calls[instruction.Offset] = resolveMethod(checked((int)instruction.OperandValue));
        }
        return calls;
    }

    static bool TryFindNextNonNop(ImmutableArray<DecodedInstruction> instructions, int offset, out DecodedInstruction instruction)
    {
        foreach (var candidate in instructions)
        {
            if (candidate.Offset < offset || candidate.OpCode == ILOpCode.Nop)
                continue;
            instruction = candidate;
            return true;
        }
        instruction = default!;
        return false;
    }

    static bool TryFindInstruction(ImmutableArray<DecodedInstruction> instructions, int offset, out int index, out DecodedInstruction instruction)
    {
        for (int i = 0; i < instructions.Length; i++)
        {
            if (instructions[i].Offset != offset)
                continue;
            index = i;
            instruction = instructions[i];
            return true;
        }
        index = -1;
        instruction = default!;
        return false;
    }

    static bool TryReadStoreLocal(DecodedInstruction instruction, out int slot)
    {
        slot = instruction.OpCode switch
        {
            ILOpCode.Stloc_0 => 0,
            ILOpCode.Stloc_1 => 1,
            ILOpCode.Stloc_2 => 2,
            ILOpCode.Stloc_3 => 3,
            ILOpCode.Stloc_s or ILOpCode.Stloc => checked((int)instruction.OperandValue),
            _ => -1,
        };
        return slot >= 0;
    }

    static bool IsLoadLocal(DecodedInstruction instruction, int slot)
        => instruction.OpCode switch
        {
            ILOpCode.Ldloc_0 => slot == 0,
            ILOpCode.Ldloc_1 => slot == 1,
            ILOpCode.Ldloc_2 => slot == 2,
            ILOpCode.Ldloc_3 => slot == 3,
            ILOpCode.Ldloc_s or ILOpCode.Ldloc => instruction.OperandValue == slot,
            _ => false,
        };

    static bool IsSimpleArgumentPush(ILOpCode opcode)
        => opcode is ILOpCode.Ldc_i4_m1 or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
            or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6
            or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8 or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4
            or ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3
            or ILOpCode.Ldarg_s or ILOpCode.Ldarg
            or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3
            or ILOpCode.Ldloc_s or ILOpCode.Ldloc
            or ILOpCode.Ldnull;

    static bool IsElementRead(ILOpCode opcode)
        => opcode is ILOpCode.Ldelem or ILOpCode.Ldelem_i or ILOpCode.Ldelem_i1 or ILOpCode.Ldelem_i2
            or ILOpCode.Ldelem_i4 or ILOpCode.Ldelem_i8 or ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8
            or ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_u4 or ILOpCode.Ldelem_ref;

    static bool IsElementStore(ILOpCode opcode)
        => opcode is ILOpCode.Stelem or ILOpCode.Stelem_i or ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2
            or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8 or ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8
            or ILOpCode.Stelem_ref;

    static bool IsArrayPoolRent(MemberRef member)
        => member.Kind != MemberKind.Unsupported
           && member.Name == "Rent"
           && member.HasThis
           && member.ParameterTypes.Length == 1
           && FrameworkIdentity.IsCoreLibraryType(member.ParameterTypes[0], "System", "Int32")
           && member.ReturnType.Kind == TypeRefKind.SzArray
           && IsArrayPoolType(member.DeclaringType);

    static bool IsArrayPoolReturn(MemberRef member)
        => member.Kind != MemberKind.Unsupported
           && member.Name == "Return"
           && member.HasThis
           && member.ReturnType.Equals(TypeRef.CoreLib("System", "Void"))
           && member.ParameterTypes.Length is 1 or 2
           && member.ParameterTypes[0].Kind == TypeRefKind.SzArray
           && IsArrayPoolType(member.DeclaringType);

    static bool IsArrayPoolSharedGetter(MemberRef member)
        => member.Kind != MemberKind.Unsupported
           && member.Name == "get_Shared"
           && !member.HasThis
           && member.ParameterTypes.Length == 0
           && IsArrayPoolType(member.DeclaringType)
           && IsArrayPoolType(member.ReturnType);

    static bool IsArrayPoolType(TypeRef type)
        => FrameworkIdentity.IsKnownFrameworkType(type, "System.Buffers", "System.Buffers", "ArrayPool`1")
           || FrameworkIdentity.IsCoreLibraryType(type, "System.Buffers", "ArrayPool`1");

    static int ArgumentSlotCount(MethodIdentity method)
        => method.ParameterTypes.Length + (method.IsStatic ? 0 : 1);

    static GenericScope CreateScope(MetadataReader reader, TypeDefinition typeDef, MethodDefinition methodDef)
        => new(GenericParameterNames(reader, typeDef.GetGenericParameters()), GenericParameterNames(reader, methodDef.GetGenericParameters()));

    static ImmutableArray<string> GenericParameterNames(MetadataReader reader, GenericParameterHandleCollection handles)
    {
        if (handles.Count == 0)
            return [];
        var names = ImmutableArray.CreateBuilder<string>(handles.Count);
        foreach (var handle in handles)
            names.Add(reader.GetString(reader.GetGenericParameter(handle).Name));
        return names.MoveToImmutable();
    }

    static bool IsRecoverable(Exception ex)
        => ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException or IndexOutOfRangeException;

    sealed record RentedLocal(int RentOffset, int StoreOffset, int Slot, LocalDefinition Definition);

    enum UseKind
    {
        Release,
        LocalUse,
        Ambiguous,
    }
}
