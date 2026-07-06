using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

/// <summary>One recognized <c>ArrayPool&lt;T&gt;.Shared.Rent</c> acquisition: the rent call, the
/// store that names the rented array, its local slot, and the reaching-definition it creates.</summary>
internal sealed record ArrayPoolAcquire(int RentOffset, int StoreOffset, int Slot, LocalDefinition Definition);

/// <summary>
/// The purely syntactic ArrayPool acquire/release/use recognizers shared by
/// <see cref="LeakTriageAnalyzer"/> (findings) and <see cref="ResourceLifecycleCensus"/>
/// (measurement). This is the single source of truth for "what IL shape is a Rent / Return /
/// Shared receiver / element access", so the finding path and the census census cannot drift on
/// what counts as ArrayPool ownership. Slice 2 (#2439) generalizes these into pluggable,
/// per-resource-family recognizers; keeping them isolated here is the seam.
/// </summary>
internal static class ArrayPoolRecognizers
{
    public static IEnumerable<ArrayPoolAcquire> FindAcquires(
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

            yield return new ArrayPoolAcquire(instruction.Offset, store.Offset, slot, definition);
        }
    }

    public static bool RentUsesSharedReceiver(
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

    public static IReadOnlyDictionary<int, MemberRef> BuildCallMap(
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

    public static bool TryFindNextNonNop(ImmutableArray<DecodedInstruction> instructions, int offset, out DecodedInstruction instruction)
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

    public static bool TryFindInstruction(ImmutableArray<DecodedInstruction> instructions, int offset, out int index, out DecodedInstruction instruction)
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

    public static bool TryReadStoreLocal(DecodedInstruction instruction, out int slot)
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

    public static bool IsLoadLocal(DecodedInstruction instruction, int slot)
        => instruction.OpCode switch
        {
            ILOpCode.Ldloc_0 => slot == 0,
            ILOpCode.Ldloc_1 => slot == 1,
            ILOpCode.Ldloc_2 => slot == 2,
            ILOpCode.Ldloc_3 => slot == 3,
            ILOpCode.Ldloc_s or ILOpCode.Ldloc => instruction.OperandValue == slot,
            _ => false,
        };

    public static bool IsSimpleArgumentPush(ILOpCode opcode)
        => opcode is ILOpCode.Ldc_i4_m1 or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
            or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6
            or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8 or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4
            or ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3
            or ILOpCode.Ldarg_s or ILOpCode.Ldarg
            or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3
            or ILOpCode.Ldloc_s or ILOpCode.Ldloc
            or ILOpCode.Ldnull;

    public static bool IsElementRead(ILOpCode opcode)
        => opcode is ILOpCode.Ldelem or ILOpCode.Ldelem_i or ILOpCode.Ldelem_i1 or ILOpCode.Ldelem_i2
            or ILOpCode.Ldelem_i4 or ILOpCode.Ldelem_i8 or ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8
            or ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_u4 or ILOpCode.Ldelem_ref;

    public static bool IsElementStore(ILOpCode opcode)
        => opcode is ILOpCode.Stelem or ILOpCode.Stelem_i or ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2
            or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8 or ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8
            or ILOpCode.Stelem_ref;

    public static bool IsArrayPoolRent(MemberRef member)
        => member.Kind != MemberKind.Unsupported
           && member.Name == "Rent"
           && member.HasThis
           && member.ParameterTypes.Length == 1
           && FrameworkIdentity.IsCoreLibraryType(member.ParameterTypes[0], "System", "Int32")
           && member.ReturnType.Kind == TypeRefKind.SzArray
           && IsArrayPoolType(member.DeclaringType);

    public static bool IsArrayPoolReturn(MemberRef member)
        => member.Kind != MemberKind.Unsupported
           && member.Name == "Return"
           && member.HasThis
           && member.ReturnType.Equals(TypeRef.CoreLib("System", "Void"))
           && member.ParameterTypes.Length is 1 or 2
           && member.ParameterTypes[0].Kind == TypeRefKind.SzArray
           && IsArrayPoolType(member.DeclaringType);

    public static bool IsArrayPoolSharedGetter(MemberRef member)
        => member.Kind != MemberKind.Unsupported
           && member.Name == "get_Shared"
           && !member.HasThis
           && member.ParameterTypes.Length == 0
           && IsArrayPoolType(member.DeclaringType)
           && IsArrayPoolType(member.ReturnType);

    public static bool IsArrayPoolType(TypeRef type)
        => FrameworkIdentity.IsKnownFrameworkType(type, "System.Buffers", "System.Buffers", "ArrayPool`1")
           || FrameworkIdentity.IsCoreLibraryType(type, "System.Buffers", "ArrayPool`1");
}
