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

public sealed record LeakTriageCandidate(
    MethodIdentity Method,
    string Shape,
    string Evidence,
    int? RentOffset,
    int? ILOffset);

public sealed record LeakTriageResult(
    ImmutableArray<LeakTriageFinding> Findings,
    ImmutableArray<LeakTriageCandidate> Candidates);

public static class LeakTriageAnalyzer
{
    public static ImmutableArray<LeakTriageFinding> AnalyzeAssembly(string path)
        => AnalyzeAssemblyDetailed(path).Findings;

    public static LeakTriageResult AnalyzeAssemblyDetailed(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var assembly = reader.GetAssemblyDefinition();
        var assemblyName = reader.GetString(assembly.Name);
        var mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
        var findings = ImmutableArray.CreateBuilder<LeakTriageFinding>();
        var candidates = ImmutableArray.CreateBuilder<LeakTriageCandidate>();

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
                    var result = AnalyzeMethodDetailed(
                        method,
                        body.GetILBytes() ?? [],
                        body.ExceptionRegions,
                        token => MemberResolver.ResolveMethod(reader, MetadataTokens.EntityHandle(token), scope));
                    findings.AddRange(result.Findings);
                    candidates.AddRange(result.Candidates);
                }
                catch (Exception ex) when (IsRecoverable(ex))
                {
                    // Correctness triage is fail-closed: malformed or unsupported method
                    // evidence yields no accusations for that method.
                }
            }
        }

        return new LeakTriageResult(findings.ToImmutable(), candidates.ToImmutable());
    }

    public static ImmutableArray<LeakTriageFinding> AnalyzeMethod(
        MethodIdentity method,
        byte[] il,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions,
        Func<int, MemberRef> resolveMethod)
        => AnalyzeMethodDetailed(method, il, exceptionRegions, resolveMethod).Findings;

    public static LeakTriageResult AnalyzeMethodDetailed(
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
            if (!graph.IsComplete)
                return Suppressed(method, "incomplete-cfg-or-rd-suppressed", graph.IncompleteReason ?? "Incomplete CFG/RD evidence.", null, null);

            var reaching = ReachingDefinitions.Analyze(il, ArgumentSlotCount(method), exceptionRegions);
            if (!reaching.IsComplete)
                return Suppressed(method, "incomplete-cfg-or-rd-suppressed", reaching.IncompleteReason ?? "Incomplete CFG/RD evidence.", null, null);

            var calls = BuildCallMap(instructions, resolveMethod);
            var candidates = ImmutableArray.CreateBuilder<LeakTriageCandidate>();
            var rents = FindRents(method, instructions, graph, reaching, calls, candidates).ToImmutableArray();
            if (rents.Length == 0)
                return new LeakTriageResult([], candidates.ToImmutable());

            var findings = ImmutableArray.CreateBuilder<LeakTriageFinding>();
            foreach (var rent in rents)
                AnalyzeRent(method, instructions, graph, reaching, calls, rent, findings, candidates);

            return new LeakTriageResult(findings.ToImmutable(), candidates.ToImmutable());
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return Suppressed(method, "incomplete-cfg-or-rd-suppressed", ex.Message, null, null);
        }
    }

    static LeakTriageResult Empty { get; } = new([], []);

    static LeakTriageResult Suppressed(MethodIdentity method, string shape, string evidence, int? rentOffset, int? ilOffset)
        => new([], [new LeakTriageCandidate(method, shape, evidence, rentOffset, ilOffset)]);

    static void AnalyzeRent(
        MethodIdentity method,
        ImmutableArray<DecodedInstruction> instructions,
        BlockGraph graph,
        ReachingDefinitionsResult reaching,
        IReadOnlyDictionary<int, MemberRef> calls,
        RentedLocal rent,
        ImmutableArray<LeakTriageFinding>.Builder findings,
        ImmutableArray<LeakTriageCandidate>.Builder candidates)
    {
        var releases = ImmutableArray.CreateBuilder<int>();
        var safeUses = ImmutableArray.CreateBuilder<int>();
        bool ambiguous = false;

        foreach (var use in reaching.UsesOf(rent.Definition))
        {
            if (use.Address)
            {
                AddCandidate(candidates, method, "alias-or-field-suppressed", "Rented array address is observed.", rent.RentOffset, use.Offset);
                ambiguous = true;
                break;
            }

            var classification = ClassifyUse(instructions, calls, use.Offset, rent.Slot);
            switch (classification.Kind)
            {
                case UseKind.Release:
                    releases.Add(use.Offset);
                    break;
                case UseKind.LocalUse:
                    safeUses.Add(use.Offset);
                    break;
                default:
                    AddCandidate(candidates, method, classification.CandidateShape, classification.Evidence, rent.RentOffset, use.Offset);
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
        var exitKind = releaseOffsets.Length <= 1
            ? PathExitsWithoutRelease(instructions, graph, calls, rent.StoreOffset, releaseOffsets)
            : LeakExitKind.None;
        if (exitKind != LeakExitKind.None)
        {
            AddCandidate(
                candidates,
                method,
                exitKind == LeakExitKind.Exception ? "exception-path-leak-candidate" : "normal-path-leak-candidate",
                $"ArrayPool<T>.Shared.Rent at IL_{rent.RentOffset:X4} reaches an unreleased {(exitKind == LeakExitKind.Exception ? "exception" : "normal")} exit.",
                rent.RentOffset,
                rent.RentOffset);
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
            int useAfterReturn = safeUseOffsets.Where(use => releaseOffsets.Any(release => ReachesInSameBlock(graph, release, use))).Min();
            AddCandidate(
                candidates,
                method,
                "use-after-return-candidate",
                $"Use of rented array reaches past Return at IL_{releaseOffsets.Min():X4}.",
                rent.RentOffset,
                useAfterReturn);
            findings.Add(new LeakTriageFinding(
                method,
                "arraypool-use-after-return",
                $"Use of rented array reaches past Return at IL_{releaseOffsets.Min():X4}.",
                "high",
                rent.RentOffset,
                useAfterReturn));
        }

        if (releaseOffsets.Length > 1
            && releaseOffsets.Any(first => releaseOffsets.Any(second => first != second && ReachesInSameBlock(graph, first, second))))
        {
            AddCandidate(
                candidates,
                method,
                "double-return-candidate",
                $"Rented array can reach a second Return after IL_{releaseOffsets.Min():X4}.",
                rent.RentOffset,
                releaseOffsets.Skip(1).DefaultIfEmpty(releaseOffsets[0]).Min());
            findings.Add(new LeakTriageFinding(
                method,
                "arraypool-double-return",
                $"Rented array can reach a second Return after IL_{releaseOffsets.Min():X4}.",
                "high",
                rent.RentOffset,
                releaseOffsets.Skip(1).DefaultIfEmpty(releaseOffsets[0]).Min()));
        }
    }

    static void AddCandidate(
        ImmutableArray<LeakTriageCandidate>.Builder candidates,
        MethodIdentity method,
        string shape,
        string evidence,
        int? rentOffset,
        int? ilOffset)
        => candidates.Add(new LeakTriageCandidate(method, shape, evidence, rentOffset, ilOffset));

    static LeakExitKind PathExitsWithoutRelease(
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

    static bool ReachesInSameBlock(BlockGraph graph, int fromOffset, int toOffset)
    {
        int fromBlock = graph.BlockIndexAt(fromOffset);
        int toBlock = graph.BlockIndexAt(toOffset);
        return fromBlock >= 0 && fromBlock == toBlock && fromOffset < toOffset;
    }

    static IEnumerable<RentedLocal> FindRents(
        MethodIdentity method,
        ImmutableArray<DecodedInstruction> instructions,
        BlockGraph graph,
        ReachingDefinitionsResult reaching,
        IReadOnlyDictionary<int, MemberRef> calls,
        ImmutableArray<LeakTriageCandidate>.Builder candidates)
    {
        foreach (var instruction in instructions)
        {
            if (!calls.TryGetValue(instruction.Offset, out var callee) || !IsArrayPoolRent(callee))
                continue;
            if (!RentUsesSharedReceiver(instructions, graph, calls, instruction.Offset))
            {
                AddCandidate(
                    candidates,
                    method,
                    "ownership-transfer-suppressed",
                    $"ArrayPool<T>.Rent at IL_{instruction.Offset:X4} does not use the Shared receiver.",
                    instruction.Offset,
                    instruction.Offset);
                continue;
            }
            if (!TryFindNextNonNop(instructions, instruction.NextOffset, out var store)
                || !TryReadStoreLocal(store, out int slot))
            {
                AddCandidate(
                    candidates,
                    method,
                    "ownership-transfer-suppressed",
                    $"ArrayPool<T>.Shared.Rent at IL_{instruction.Offset:X4} is not stored to a modeled local.",
                    instruction.Offset,
                    instruction.Offset);
                continue;
            }

            var definition = reaching.Definitions.FirstOrDefault(d =>
                !d.IsArgument && d.Slot == slot && d.Offset == store.Offset);
            if (definition is null)
            {
                AddCandidate(
                    candidates,
                    method,
                    "incomplete-cfg-or-rd-suppressed",
                    $"ArrayPool<T>.Shared.Rent local definition at IL_{store.Offset:X4} is missing from reaching definitions.",
                    instruction.Offset,
                    store.Offset);
                continue;
            }

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

    static UseClassification ClassifyUse(
        ImmutableArray<DecodedInstruction> instructions,
        IReadOnlyDictionary<int, MemberRef> calls,
        int loadOffset,
        int slot)
    {
        if (!TryFindInstruction(instructions, loadOffset, out int index, out var load)
            || !IsLoadLocal(load, slot))
            return UseClassification.OwnershipTransfer("Rented array use could not be classified.");

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
                return extra == 0 ? UseClassification.LocalUse : UseClassification.OwnershipTransfer("Rented array length use is ambiguous.");
            if (IsElementRead(opcode))
                return extra == 1 ? UseClassification.LocalUse : UseClassification.OwnershipTransfer("Rented array element read is ambiguous.");
            if (IsElementStore(opcode))
                return extra == 2 ? UseClassification.LocalUse : UseClassification.OwnershipTransfer("Rented array element store is ambiguous.");
            if (IsFieldStore(opcode) || IsLocalStore(opcode))
                return UseClassification.AliasOrField("Rented array is stored into another location.");
            if (opcode == ILOpCode.Ret)
                return UseClassification.OwnershipTransfer("Rented array escapes through the return value.");
            if (calls.TryGetValue(instruction.Offset, out var callee))
            {
                if (IsArrayPoolReturn(callee) && extra < callee.ParameterTypes.Length)
                    return UseClassification.Release;
                return UseClassification.CrossMethod($"Rented array is passed to {callee.DeclaringType.Name}::{callee.Name}.");
            }
            return UseClassification.OwnershipTransfer($"Rented array reaches unsupported opcode {opcode}.");
        }

        return UseClassification.OwnershipTransfer("Rented array use reaches the end of the instruction stream.");
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

    static bool IsFieldStore(ILOpCode opcode)
        => opcode is ILOpCode.Stfld or ILOpCode.Stsfld;

    static bool IsLocalStore(ILOpCode opcode)
        => opcode is ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3
            or ILOpCode.Stloc_s or ILOpCode.Stloc;

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

    readonly record struct UseClassification(UseKind Kind, string CandidateShape, string Evidence)
    {
        public static UseClassification Release { get; } = new(UseKind.Release, "", "");
        public static UseClassification LocalUse { get; } = new(UseKind.LocalUse, "", "");
        public static UseClassification AliasOrField(string evidence) => new(UseKind.Ambiguous, "alias-or-field-suppressed", evidence);
        public static UseClassification CrossMethod(string evidence) => new(UseKind.Ambiguous, "cross-method-suppressed", evidence);
        public static UseClassification OwnershipTransfer(string evidence) => new(UseKind.Ambiguous, "ownership-transfer-suppressed", evidence);
    }

    enum LeakExitKind
    {
        None,
        Normal,
        Exception,
    }
}
