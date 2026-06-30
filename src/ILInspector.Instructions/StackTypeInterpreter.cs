using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Instructions;

/// <summary>
/// The per-instruction typed evaluation-stack states for one method body, plus completeness.
/// <see cref="StackBefore"/> is keyed by IL offset; index 0 is the bottom of the stack.
/// </summary>
public sealed record TypedStackResult(
    ImmutableArray<DecodedInstruction> Instructions,
    BlockGraph Blocks,
    ImmutableDictionary<int, ImmutableArray<StackValue>> StackBefore,
    bool IsComplete,
    string? IncompleteReason)
{
    /// <summary>The evaluation stack just before <paramref name="offset"/> executes (bottom-to-top), or empty if unreached.</summary>
    public ImmutableArray<StackValue> StackBeforeOffset(int offset)
        => StackBefore.TryGetValue(offset, out var stack) ? stack : [];

    /// <summary>
    /// The receiver slot of a <c>call</c>/<c>callvirt</c>/<c>newobj</c> at <paramref name="callOffset"/>,
    /// resolved from the typed stack: the value <paramref name="argumentsBelow"/> slots below the top.
    /// For an instance call with <c>n</c> non-<c>this</c> arguments, pass <c>n</c> to get <c>this</c>.
    /// Returns <c>null</c> when the stack is too shallow (e.g. typed-stack incomplete).
    /// </summary>
    public StackValue? ReceiverAt(int callOffset, int argumentsBelow)
    {
        var stack = StackBeforeOffset(callOffset);
        int index = stack.Length - 1 - argumentsBelow;
        return index >= 0 && index < stack.Length ? stack[index] : null;
    }
}

/// <summary>
/// Abstract-interprets a decoded method body to a typed evaluation stack with value provenance.
/// This is the witness a typed-stack model supplies that more opcode heuristics cannot: stack
/// element types, receiver types, and which instruction produced each slot. Worklist over the
/// EH-aware <see cref="BlockGraph"/>; predecessors are joined position-wise (<see cref="StackValue.Merge"/>);
/// catch/filter handler entries start with the in-flight exception on the stack. Fail-closed:
/// any unresolved stack effect or height conflict degrades the whole result to incomplete.
/// </summary>
public static class StackTypeInterpreter
{
    public static TypedStackResult Interpret(
        ImmutableArray<DecodedInstruction> instructions,
        BlockGraph blocks,
        bool methodReturnsValue,
        IStackTypeResolver? resolver = null)
    {
        resolver ??= DefaultStackTypeResolver.Instance;
        var stackBefore = ImmutableDictionary.CreateBuilder<int, ImmutableArray<StackValue>>();

        if (!blocks.IsComplete)
            return new TypedStackResult(instructions, blocks, stackBefore.ToImmutable(), false,
                blocks.IncompleteReason ?? "Block graph is incomplete.");
        if (instructions.IsDefaultOrEmpty)
            return new TypedStackResult(instructions, blocks, stackBefore.ToImmutable(), true, null);

        var instructionsByOffset = instructions.ToDictionary(i => i.Offset);
        var blockEntry = new ImmutableArray<StackValue>?[blocks.Blocks.Length];
        var ehEntryBlocks = ExceptionEntryStacks(blocks);

        var worklist = new Queue<int>();
        // Entry block starts empty; EH entry blocks seed from their handler kind.
        SeedEntry(0, []);
        foreach (var (blockIndex, entry) in ehEntryBlocks)
            SeedEntry(blockIndex, entry);

        string? incompleteReason = null;

        while (worklist.Count > 0)
        {
            int blockIndex = worklist.Dequeue();
            var block = blocks.Blocks[blockIndex];
            var stack = new List<StackValue>(blockEntry[blockIndex]!.Value);

            foreach (var instruction in InstructionsIn(block))
            {
                stackBefore[instruction.Offset] = [.. stack];
                string? reason = Transition(instruction, stack, resolver, methodReturnsValue);
                if (reason is not null)
                {
                    incompleteReason ??= reason;
                    return new TypedStackResult(instructions, blocks, stackBefore.ToImmutable(), false, incompleteReason);
                }
            }

            var exit = stack.ToImmutableArray();
            foreach (int successor in block.Edges.Successors)
            {
                if (ehEntryBlocks.ContainsKey(successor))
                    continue; // EH entry stack is fixed by handler kind, not by the protected region's exit.
                if (!Propagate(successor, exit, out string? mergeReason))
                {
                    incompleteReason ??= mergeReason;
                    return new TypedStackResult(instructions, blocks, stackBefore.ToImmutable(), false, incompleteReason);
                }
            }
        }

        return new TypedStackResult(instructions, blocks, stackBefore.ToImmutable(), true, null);

        void SeedEntry(int blockIndex, ImmutableArray<StackValue> entry)
        {
            if (blockEntry[blockIndex] is null)
            {
                blockEntry[blockIndex] = entry;
                worklist.Enqueue(blockIndex);
            }
        }

        bool Propagate(int successor, ImmutableArray<StackValue> incoming, out string? reason)
        {
            reason = null;
            if (blockEntry[successor] is not { } existing)
            {
                blockEntry[successor] = incoming;
                worklist.Enqueue(successor);
                return true;
            }
            if (existing.Length != incoming.Length)
            {
                reason = $"Evaluation stack height disagreement entering block {successor} ({existing.Length} vs {incoming.Length}).";
                return false;
            }
            var merged = ImmutableArray.CreateBuilder<StackValue>(existing.Length);
            bool changed = false;
            for (int i = 0; i < existing.Length; i++)
            {
                var m = existing[i].Merge(incoming[i]);
                if (!m.Equals(existing[i]))
                    changed = true;
                merged.Add(m);
            }
            if (changed)
            {
                blockEntry[successor] = merged.MoveToImmutable();
                worklist.Enqueue(successor);
            }
            return true;
        }

        IEnumerable<DecodedInstruction> InstructionsIn(InstructionBlock block)
        {
            foreach (var instruction in instructions)
                if (instruction.Offset >= block.Start && instruction.Offset < block.End)
                    yield return instruction;
        }
    }

    static Dictionary<int, ImmutableArray<StackValue>> ExceptionEntryStacks(BlockGraph blocks)
    {
        var offsetToBlock = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Blocks.Length; i++)
            offsetToBlock[blocks.Blocks[i].Start] = i;

        var result = new Dictionary<int, ImmutableArray<StackValue>>();
        foreach (var region in blocks.Regions)
        {
            // Catch and filter handlers begin with the in-flight exception object on the stack.
            ImmutableArray<StackValue> exceptionStack = [StackValue.Of(StackType.ObjectReference, StackValue.NoProducer)];
            switch (region.Kind)
            {
                case HandlerKind.Catch:
                    if (offsetToBlock.TryGetValue(region.HandlerStart, out int catchBlock))
                        result[catchBlock] = exceptionStack;
                    break;
                case HandlerKind.Filter:
                    if (offsetToBlock.TryGetValue(region.FilterStart, out int filterBlock))
                        result[filterBlock] = exceptionStack;
                    if (offsetToBlock.TryGetValue(region.HandlerStart, out int filterHandlerBlock))
                        result[filterHandlerBlock] = exceptionStack;
                    break;
                case HandlerKind.Finally:
                case HandlerKind.Fault:
                    if (offsetToBlock.TryGetValue(region.HandlerStart, out int handlerBlock))
                        result[handlerBlock] = [];
                    break;
            }
        }
        return result;
    }

    /// <summary>Applies one instruction's ECMA-335 Partition III stack transition. Returns null on success, or a reason string on underflow / an unresolved effect.</summary>
    static string? Transition(
        DecodedInstruction instruction,
        List<StackValue> stack,
        IStackTypeResolver resolver,
        bool methodReturnsValue)
    {
        var op = instruction.OpCode;
        int at = instruction.Offset;

        switch (op)
        {
            // ---- no stack effect / prefixes ----
            case ILOpCode.Nop or ILOpCode.Break or ILOpCode.Volatile or ILOpCode.Tail
                or ILOpCode.Unaligned or ILOpCode.Readonly or ILOpCode.Constrained
                or ILOpCode.Br or ILOpCode.Br_s or ILOpCode.Leave or ILOpCode.Leave_s
                or ILOpCode.Endfinally or ILOpCode.Jmp:
                return null;

            // ---- constants ----
            case ILOpCode.Ldnull: return Push(StackType.ObjectReference);
            case ILOpCode.Ldstr: return Push(StackType.ObjectReference);
            case >= ILOpCode.Ldc_i4_m1 and <= ILOpCode.Ldc_i4_8: return Push(StackType.Int32);
            case ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4: return Push(StackType.Int32);
            case ILOpCode.Ldc_i8: return Push(StackType.Int64);
            case ILOpCode.Ldc_r4 or ILOpCode.Ldc_r8: return Push(StackType.Float);
            case ILOpCode.Sizeof: return Push(StackType.Int32);
            case ILOpCode.Arglist: return Push(StackType.NativeInt);
            case ILOpCode.Ldtoken: return Push(StackType.ValueType);

            // ---- locals / arguments ----
            case >= ILOpCode.Ldarg_0 and <= ILOpCode.Ldarg_3: return Push(resolver.Argument((int)op - (int)ILOpCode.Ldarg_0));
            case ILOpCode.Ldarg_s or ILOpCode.Ldarg: return Push(resolver.Argument((int)instruction.OperandValue));
            case ILOpCode.Ldarga_s or ILOpCode.Ldarga: return Push(StackType.ManagedPointer);
            case >= ILOpCode.Ldloc_0 and <= ILOpCode.Ldloc_3: return Push(resolver.Local((int)op - (int)ILOpCode.Ldloc_0));
            case ILOpCode.Ldloc_s or ILOpCode.Ldloc: return Push(resolver.Local((int)instruction.OperandValue));
            case ILOpCode.Ldloca_s or ILOpCode.Ldloca: return Push(StackType.ManagedPointer);
            case >= ILOpCode.Stloc_0 and <= ILOpCode.Stloc_3: return Pop(1);
            case ILOpCode.Stloc_s or ILOpCode.Stloc or ILOpCode.Starg_s or ILOpCode.Starg: return Pop(1);

            // ---- stack manipulation ----
            case ILOpCode.Pop: return Pop(1);
            case ILOpCode.Dup:
                if (stack.Count == 0) return Underflow();
                stack.Add(stack[^1] with { ProducerOffset = at });
                return null;

            // ---- fields ----
            case ILOpCode.Ldsfld: return Push(resolver.Field((int)instruction.OperandValue));
            case ILOpCode.Ldsflda: return Push(StackType.ManagedPointer);
            case ILOpCode.Stsfld: return Pop(1);
            case ILOpCode.Ldfld: return Replace(1, resolver.Field((int)instruction.OperandValue));
            case ILOpCode.Ldflda: return Replace(1, StackType.ManagedPointer);
            case ILOpCode.Stfld: return Pop(2);

            // ---- function pointers ----
            case ILOpCode.Ldftn: return Push(StackType.NativeInt);
            case ILOpCode.Ldvirtftn: return Replace(1, StackType.NativeInt);

            // ---- indirect ----
            case ILOpCode.Ldind_i1 or ILOpCode.Ldind_u1 or ILOpCode.Ldind_i2 or ILOpCode.Ldind_u2
                or ILOpCode.Ldind_i4 or ILOpCode.Ldind_u4:
                return Replace(1, StackType.Int32);
            case ILOpCode.Ldind_i8: return Replace(1, StackType.Int64);
            case ILOpCode.Ldind_i: return Replace(1, StackType.NativeInt);
            case ILOpCode.Ldind_r4 or ILOpCode.Ldind_r8: return Replace(1, StackType.Float);
            case ILOpCode.Ldind_ref: return Replace(1, StackType.ObjectReference);
            case ILOpCode.Stind_i1 or ILOpCode.Stind_i2 or ILOpCode.Stind_i4 or ILOpCode.Stind_i8
                or ILOpCode.Stind_i or ILOpCode.Stind_r4 or ILOpCode.Stind_r8 or ILOpCode.Stind_ref:
                return Pop(2);
            case ILOpCode.Ldobj: return Replace(1, resolver.TypeOfToken((int)instruction.OperandValue));
            case ILOpCode.Stobj or ILOpCode.Cpobj: return Pop(2);
            case ILOpCode.Initobj: return Pop(1);

            // ---- arithmetic / logic ----
            case ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Div or ILOpCode.Div_un
                or ILOpCode.Rem or ILOpCode.Rem_un or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor
                or ILOpCode.Add_ovf or ILOpCode.Add_ovf_un or ILOpCode.Sub_ovf or ILOpCode.Sub_ovf_un
                or ILOpCode.Mul_ovf or ILOpCode.Mul_ovf_un:
                return BinaryNumeric();
            case ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Shr_un:
                if (stack.Count < 2) return Underflow();
                stack.RemoveAt(stack.Count - 1); // shift amount; result keeps the shifted value's type, already on top
                return null;
            case ILOpCode.Neg or ILOpCode.Not:
                return stack.Count >= 1 ? null : Underflow();
            case ILOpCode.Ceq or ILOpCode.Cgt or ILOpCode.Cgt_un or ILOpCode.Clt or ILOpCode.Clt_un:
                return Reduce(2, StackType.Int32);
            case ILOpCode.Ckfinite:
                return Replace(1, StackType.Float);

            // ---- conversions ----
            case ILOpCode.Conv_i1 or ILOpCode.Conv_u1 or ILOpCode.Conv_i2 or ILOpCode.Conv_u2
                or ILOpCode.Conv_i4 or ILOpCode.Conv_u4
                or ILOpCode.Conv_ovf_i1 or ILOpCode.Conv_ovf_u1 or ILOpCode.Conv_ovf_i2 or ILOpCode.Conv_ovf_u2
                or ILOpCode.Conv_ovf_i4 or ILOpCode.Conv_ovf_u4
                or ILOpCode.Conv_ovf_i1_un or ILOpCode.Conv_ovf_u1_un or ILOpCode.Conv_ovf_i2_un or ILOpCode.Conv_ovf_u2_un
                or ILOpCode.Conv_ovf_i4_un or ILOpCode.Conv_ovf_u4_un:
                return Replace(1, StackType.Int32);
            case ILOpCode.Conv_i8 or ILOpCode.Conv_u8 or ILOpCode.Conv_ovf_i8 or ILOpCode.Conv_ovf_u8
                or ILOpCode.Conv_ovf_i8_un or ILOpCode.Conv_ovf_u8_un:
                return Replace(1, StackType.Int64);
            case ILOpCode.Conv_r4 or ILOpCode.Conv_r8 or ILOpCode.Conv_r_un:
                return Replace(1, StackType.Float);
            case ILOpCode.Conv_i or ILOpCode.Conv_u or ILOpCode.Conv_ovf_i or ILOpCode.Conv_ovf_u
                or ILOpCode.Conv_ovf_i_un or ILOpCode.Conv_ovf_u_un:
                return Replace(1, StackType.NativeInt);

            // ---- object model ----
            case ILOpCode.Box: return Replace(1, StackType.ObjectReference);
            case ILOpCode.Unbox: return Replace(1, StackType.ManagedPointer);
            case ILOpCode.Unbox_any: return Replace(1, resolver.TypeOfToken((int)instruction.OperandValue));
            case ILOpCode.Castclass or ILOpCode.Isinst: return Replace(1, StackType.ObjectReference);
            case ILOpCode.Newarr: return Replace(1, StackType.ObjectReference);
            case ILOpCode.Ldlen: return Replace(1, StackType.NativeInt);
            case ILOpCode.Ldelema: return Reduce(2, StackType.ManagedPointer);
            case ILOpCode.Ldelem_i1 or ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_i2 or ILOpCode.Ldelem_u2
                or ILOpCode.Ldelem_i4 or ILOpCode.Ldelem_u4:
                return Reduce(2, StackType.Int32);
            case ILOpCode.Ldelem_i8: return Reduce(2, StackType.Int64);
            case ILOpCode.Ldelem_i: return Reduce(2, StackType.NativeInt);
            case ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8: return Reduce(2, StackType.Float);
            case ILOpCode.Ldelem_ref: return Reduce(2, StackType.ObjectReference);
            case ILOpCode.Ldelem: return Reduce(2, resolver.TypeOfToken((int)instruction.OperandValue));
            case ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2 or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8
                or ILOpCode.Stelem_i or ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8 or ILOpCode.Stelem_ref
                or ILOpCode.Stelem:
                return Pop(3);
            case ILOpCode.Mkrefany: return Replace(1, StackType.TypedReference);
            case ILOpCode.Refanyval: return Replace(1, StackType.ManagedPointer);
            case ILOpCode.Refanytype: return Replace(1, StackType.ValueType);
            case ILOpCode.Localloc: return Replace(1, StackType.UnmanagedPointer);
            case ILOpCode.Cpblk or ILOpCode.Initblk: return Pop(3);

            // ---- calls ----
            case ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj:
            {
                bool isNewObj = op == ILOpCode.Newobj;
                if (!resolver.TryResolveCall((int)instruction.OperandValue, isNewObj, out int popCount, out bool pushes, out var pushType))
                    return $"Unresolved {op} signature at IL_{at:X4}; typed stack cannot continue.";
                if (Pop(popCount) is { } e) return e;
                if (pushes) stack.Add(StackValue.Of(pushType, at));
                return null;
            }
            case ILOpCode.Calli:
            {
                if (!resolver.TryResolveCalli((int)instruction.OperandValue, out int popCount, out bool pushes, out var pushType))
                    return $"Unresolved calli signature at IL_{at:X4}; typed stack cannot continue.";
                if (Pop(popCount + 1) is { } e) return e; // + function pointer
                if (pushes) stack.Add(StackValue.Of(pushType, at));
                return null;
            }

            // ---- branches / control transfer ----
            case ILOpCode.Brfalse or ILOpCode.Brfalse_s or ILOpCode.Brtrue or ILOpCode.Brtrue_s
                or ILOpCode.Switch:
                return Pop(1);
            case ILOpCode.Beq or ILOpCode.Beq_s or ILOpCode.Bne_un or ILOpCode.Bne_un_s
                or ILOpCode.Bge or ILOpCode.Bge_s or ILOpCode.Bge_un or ILOpCode.Bge_un_s
                or ILOpCode.Bgt or ILOpCode.Bgt_s or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s
                or ILOpCode.Ble or ILOpCode.Ble_s or ILOpCode.Ble_un or ILOpCode.Ble_un_s
                or ILOpCode.Blt or ILOpCode.Blt_s or ILOpCode.Blt_un or ILOpCode.Blt_un_s:
                return Pop(2);
            case ILOpCode.Throw: return Pop(1);
            case ILOpCode.Rethrow: return null;
            case ILOpCode.Endfilter: return Pop(1);
            case ILOpCode.Ret: return methodReturnsValue ? Pop(1) : null;

            default:
                return $"Unsupported opcode {op} at IL_{at:X4} for typed-stack tracking.";
        }

        // ---- local helpers (null = success, non-null = failure reason) ----
        string? Push(StackType type)
        {
            stack.Add(StackValue.Of(type, at));
            return null;
        }
        string? Pop(int count)
        {
            if (count < 0 || stack.Count < count) return Underflow();
            stack.RemoveRange(stack.Count - count, count);
            return null;
        }
        // Pop `count` and push one value of `type` (produced here); also serves "replace top".
        string? Reduce(int count, StackType type) => Pop(count) ?? Push(type);
        string? Replace(int count, StackType type) => Reduce(count, type);
        string? BinaryNumeric()
        {
            if (stack.Count < 2) return Underflow();
            var b = stack[^1];
            var a = stack[^2];
            stack.RemoveRange(stack.Count - 2, 2);
            return Push(BinaryNumericResult(a.Type, b.Type, op));
        }
        string Underflow() => $"Evaluation stack underflow at IL_{at:X4} ({op}).";
    }

    /// <summary>ECMA-335 Partition III §1.5 binary numeric result type (coarsened to the stack lattice).</summary>
    static StackType BinaryNumericResult(StackType a, StackType b, ILOpCode op)
    {
        if (a == StackType.Unknown || b == StackType.Unknown)
            return StackType.Unknown;

        bool aPtr = a is StackType.ManagedPointer or StackType.UnmanagedPointer;
        bool bPtr = b is StackType.ManagedPointer or StackType.UnmanagedPointer;

        // Pointer subtraction (& - &) yields a native int; pointer +/- integer yields the pointer type.
        if (aPtr && bPtr)
            return op is ILOpCode.Sub or ILOpCode.Sub_ovf or ILOpCode.Sub_ovf_un ? StackType.NativeInt : StackType.Unknown;
        if (aPtr) return a;
        if (bPtr) return b;

        if (a == b) return a; // i4/i4, i8/i8, F/F, I/I
        if ((a == StackType.Int32 && b == StackType.NativeInt) || (a == StackType.NativeInt && b == StackType.Int32))
            return StackType.NativeInt;
        return StackType.Unknown;
    }
}
