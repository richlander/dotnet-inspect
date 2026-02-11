using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using DotnetInspector.Metadata;

namespace DotnetInspector.Decompiler;

/// <summary>
/// Result of stack simulation for a method.
/// </summary>
public sealed class StackSimulationResult
{
    /// <summary>Incoming stack state at each basic block (keyed by block start offset).</summary>
    public Dictionary<int, StackState> BlockEntryStacks { get; } = [];

    /// <summary>Variables introduced at basic block boundaries.</summary>
    public List<ILVariable> StackSlotVariables { get; } = [];

    /// <summary>Method parameter variables.</summary>
    public List<ILVariable> Parameters { get; } = [];

    /// <summary>Local variables.</summary>
    public List<ILVariable> Locals { get; } = [];

    /// <summary>Exception handler slot variables.</summary>
    public List<ILVariable> ExceptionSlots { get; } = [];
}

/// <summary>
/// Forward stack type simulation over the control flow graph. Walks each
/// basic block tracking <see cref="StackState"/> (typed stack) and propagates
/// outgoing state to successors using a worklist. Introduces
/// <see cref="ILVariable"/> instances at block boundaries.
/// </summary>
public static class StackSimulator
{
    /// <summary>
    /// Simulate the evaluation stack for the given method, producing typed
    /// stack states at every basic block entry and variables at boundaries.
    /// </summary>
    public static StackSimulationResult Simulate(MethodBodyContext context, ControlFlowGraph cfg)
    {
        var result = new StackSimulationResult();

        // Build parameter variables
        BuildParameterVariables(context, result);

        // Build local variables
        BuildLocalVariables(context, result);

        // Build exception handler variables
        BuildExceptionVariables(context, cfg, result);

        // Initialize entry block with empty stack
        if (cfg.BasicBlocks.Count == 0)
            return result;

        var worklist = new Queue<BasicBlock>();
        var entryBlock = cfg.BasicBlocks[0];
        result.BlockEntryStacks[entryBlock.Start] = StackState.Empty;
        worklist.Enqueue(entryBlock);

        // Initialize exception handler entry blocks
        foreach (var region in context.ExceptionRegions)
        {
            var handlerBlock = cfg.Lookup(region.HandlerOffset);
            if (handlerBlock is null) continue;

            if (region.Kind is ExceptionRegionKind.Catch or ExceptionRegionKind.Filter)
            {
                // Catch/filter handlers start with the exception object on the stack
                string? exTypeName = region.Kind == ExceptionRegionKind.Catch
                    ? ResolveExceptionTypeName(context.Reader, region.CatchType)
                    : "object";
                var exValue = StackValue.CreateObjRef(exTypeName);
                result.BlockEntryStacks[handlerBlock.Start] = StackState.Empty.Push(exValue);
            }
            else
            {
                // Finally/fault handlers start with empty stack
                result.BlockEntryStacks[handlerBlock.Start] = StackState.Empty;
            }

            if (!worklist.Contains(handlerBlock))
                worklist.Enqueue(handlerBlock);

            if (region.Kind == ExceptionRegionKind.Filter)
            {
                var filterBlock = cfg.Lookup(region.FilterOffset);
                if (filterBlock is not null)
                {
                    var filterValue = StackValue.CreateObjRef("System.Object");
                    result.BlockEntryStacks[filterBlock.Start] = StackState.Empty.Push(filterValue);
                    if (!worklist.Contains(filterBlock))
                        worklist.Enqueue(filterBlock);
                }
            }
        }

        // Worklist algorithm: simulate each block, propagate to successors
        while (worklist.Count > 0)
        {
            var block = worklist.Dequeue();
            if (!result.BlockEntryStacks.TryGetValue(block.Start, out var entryState))
                entryState = StackState.Empty;

            var exitState = SimulateBlock(context, block, entryState);

            // Propagate to successors
            foreach (var target in block.Targets)
            {
                var targetState = exitState;

                // leave clears the stack
                if (IsLeaveTarget(context, block, target))
                    targetState = StackState.Empty;

                // Exception handler entries already have their own entry stacks
                if (IsExceptionHandlerEntry(context, target.Start))
                    continue;

                if (result.BlockEntryStacks.TryGetValue(target.Start, out var existing))
                {
                    var (merged, changed) = existing.Merge(targetState);
                    if (changed)
                    {
                        result.BlockEntryStacks[target.Start] = merged;
                        worklist.Enqueue(target);
                    }
                }
                else
                {
                    result.BlockEntryStacks[target.Start] = targetState;
                    worklist.Enqueue(target);
                }
            }
        }

        // Introduce stack slot variables at block boundaries
        int slotIndex = 0;
        foreach (var block in cfg.BasicBlocks)
        {
            if (!result.BlockEntryStacks.TryGetValue(block.Start, out var state))
                continue;

            for (int i = 0; i < state.Height; i++)
            {
                var value = state.Values[i];
                var variable = new ILVariable(ILVariableKind.StackSlot, value, slotIndex++);
                result.StackSlotVariables.Add(variable);
            }
        }

        return result;
    }

    /// <summary>
    /// Simulate a single basic block, starting from the given entry stack state.
    /// Returns the stack state at the exit of the block.
    /// </summary>
    static StackState SimulateBlock(MethodBodyContext context, BasicBlock block, StackState state)
    {
        var ilBytes = context.ILBytes.AsSpan(block.Start, block.Size);
        var reader = new ILReaderLite(ilBytes);

        while (reader.HasNext)
        {
            var opcode = reader.ReadILOpcode();
            state = ApplyOpcode(context, ref reader, opcode, state);
        }

        return state;
    }

    /// <summary>
    /// Simulate a single basic block and record the stack state before each instruction.
    /// Returns a dictionary mapping absolute IL offsets to their pre-instruction stack states.
    /// </summary>
    internal static Dictionary<int, StackState> SimulateBlockDetailed(
        MethodBodyContext context, BasicBlock block, StackState entryState)
    {
        var result = new Dictionary<int, StackState>();
        var ilBytes = context.ILBytes.AsSpan(block.Start, block.Size);
        var reader = new ILReaderLite(ilBytes);
        var state = entryState;

        while (reader.HasNext)
        {
            int offsetInBlock = reader.Offset;
            int absoluteOffset = block.Start + offsetInBlock;
            result[absoluteOffset] = state;

            var opcode = reader.ReadILOpcode();
            state = ApplyOpcode(context, ref reader, opcode, state);
        }

        return result;
    }

    /// <summary>
    /// Apply a single opcode to the stack state, returning the new state.
    /// </summary>
    static StackState ApplyOpcode(MethodBodyContext context, ref ILReaderLite reader, ILOpCode opcode, StackState state)
    {
        // Check if this opcode has a known result kind
        var resultKind = opcode.ResultKind();

        switch (opcode)
        {
            // Push 1 — loads with known types
            case ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3:
            {
                int argIndex = opcode - ILOpCode.Ldarg_0;
                return state.Push(ResolveArgType(context, argIndex));
            }

            case ILOpCode.Ldarg_s:
            {
                int argIndex = reader.ReadILByte();
                return state.Push(ResolveArgType(context, argIndex));
            }

            case ILOpCode.Ldarg:
            {
                int argIndex = reader.ReadILUInt16();
                return state.Push(ResolveArgType(context, argIndex));
            }

            case ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3:
            {
                int locIndex = opcode - ILOpCode.Ldloc_0;
                return state.Push(ResolveLocalType(context, locIndex));
            }

            case ILOpCode.Ldloc_s:
            {
                int locIndex = reader.ReadILByte();
                return state.Push(ResolveLocalType(context, locIndex));
            }

            case ILOpCode.Ldloc:
            {
                int locIndex = reader.ReadILUInt16();
                return state.Push(ResolveLocalType(context, locIndex));
            }

            // Address-of loads
            case ILOpCode.Ldloca_s:
            {
                int locIndex = reader.ReadILByte();
                string? refType = locIndex < context.LocalTypes.Count ? context.LocalTypes[locIndex] : null;
                return state.Push(StackValue.CreateByRef(refType));
            }

            case ILOpCode.Ldloca:
            {
                int locIndex = reader.ReadILUInt16();
                string? refType = locIndex < context.LocalTypes.Count ? context.LocalTypes[locIndex] : null;
                return state.Push(StackValue.CreateByRef(refType));
            }

            case ILOpCode.Ldarga_s:
            {
                reader.ReadILByte();
                return state.Push(StackValue.CreateByRef());
            }

            case ILOpCode.Ldarga:
            {
                reader.ReadILUInt16();
                return state.Push(StackValue.CreateByRef());
            }

            // Constants
            case ILOpCode.Ldnull:
                return state.Push(StackValue.CreateObjRef(null));

            case ILOpCode.Ldstr:
                reader.ReadILToken();
                return state.Push(StackValue.CreateObjRef("string"));

            case ILOpCode.Ldc_i4_m1 or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or
                 ILOpCode.Ldc_i4_2 or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or
                 ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or
                 ILOpCode.Ldc_i4_8:
                return state.Push(StackValue.CreatePrimitive(StackValueKind.Int32));

            case ILOpCode.Ldc_i4_s:
                reader.ReadILByte();
                return state.Push(StackValue.CreatePrimitive(StackValueKind.Int32));

            case ILOpCode.Ldc_i4:
                reader.ReadILUInt32();
                return state.Push(StackValue.CreatePrimitive(StackValueKind.Int32));

            case ILOpCode.Ldc_i8:
                reader.ReadILUInt64();
                return state.Push(StackValue.CreatePrimitive(StackValueKind.Int64));

            case ILOpCode.Ldc_r4:
                reader.ReadILUInt32();
                return state.Push(StackValue.CreatePrimitive(StackValueKind.Float));

            case ILOpCode.Ldc_r8:
                reader.ReadILUInt64();
                return state.Push(StackValue.CreatePrimitive(StackValueKind.Float));

            // Dup
            case ILOpCode.Dup:
                return state.Push(state.Peek());

            // Pop
            case ILOpCode.Pop:
                return state.Pop(1);

            // Stores (pop 1, push 0)
            case ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3:
                return state.Pop(1);

            case ILOpCode.Stloc_s:
                reader.ReadILByte();
                return state.Pop(1);

            case ILOpCode.Stloc:
                reader.ReadILUInt16();
                return state.Pop(1);

            case ILOpCode.Starg_s:
                reader.ReadILByte();
                return state.Pop(1);

            case ILOpCode.Starg:
                reader.ReadILUInt16();
                return state.Pop(1);

            // Call instructions
            case ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj:
            {
                int token = reader.ReadILToken();
                return ApplyCall(context.Reader, token, opcode, state);
            }

            case ILOpCode.Calli:
            {
                reader.ReadILToken();
                // Simplified: pop function pointer + unknown args, push unknown
                return state.Pop(1).Push(StackValue.CreateUnknown());
            }

            // Field operations
            case ILOpCode.Ldfld:
            {
                int token = reader.ReadILToken();
                state = state.Pop(1); // pop object
                return state.Push(ResolveFieldType(context.Reader, token));
            }

            case ILOpCode.Ldsfld:
            {
                int token = reader.ReadILToken();
                return state.Push(ResolveFieldType(context.Reader, token));
            }

            case ILOpCode.Stfld:
                reader.ReadILToken();
                return state.Pop(2); // pop object + value

            case ILOpCode.Stsfld:
                reader.ReadILToken();
                return state.Pop(1); // pop value

            case ILOpCode.Ldflda:
            {
                int token = reader.ReadILToken();
                state = state.Pop(1);
                return state.Push(StackValue.CreateByRef());
            }

            case ILOpCode.Ldsflda:
                reader.ReadILToken();
                return state.Push(StackValue.CreateByRef());

            // Comparisons (pop 2, push Int32)
            case ILOpCode.Ceq or ILOpCode.Cgt or ILOpCode.Cgt_un or
                 ILOpCode.Clt or ILOpCode.Clt_un:
                return state.Pop(2).Push(StackValue.CreatePrimitive(StackValueKind.Int32));

            // Binary arithmetic (pop 2, push result — type depends on operands)
            case ILOpCode.Add or ILOpCode.Add_ovf or ILOpCode.Add_ovf_un or
                 ILOpCode.Sub or ILOpCode.Sub_ovf or ILOpCode.Sub_ovf_un or
                 ILOpCode.Mul or ILOpCode.Mul_ovf or ILOpCode.Mul_ovf_un or
                 ILOpCode.Div or ILOpCode.Div_un or
                 ILOpCode.Rem or ILOpCode.Rem_un or
                 ILOpCode.And or ILOpCode.Or or ILOpCode.Xor or
                 ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Shr_un:
            {
                var right = state.Peek(0);
                var left = state.Peek(1);
                state = state.Pop(2);
                return state.Push(MergeBinaryResult(left, right));
            }

            // Unary (pop 1, push 1 same-ish type)
            case ILOpCode.Neg or ILOpCode.Not:
            {
                var operand = state.Peek();
                state = state.Pop(1);
                return state.Push(operand);
            }

            // Conversions (pop 1, push known type)
            case ILOpCode.Conv_i1 or ILOpCode.Conv_i2 or ILOpCode.Conv_i4 or
                 ILOpCode.Conv_u1 or ILOpCode.Conv_u2 or ILOpCode.Conv_u4 or
                 ILOpCode.Conv_ovf_i1 or ILOpCode.Conv_ovf_i2 or ILOpCode.Conv_ovf_i4 or
                 ILOpCode.Conv_ovf_u1 or ILOpCode.Conv_ovf_u2 or ILOpCode.Conv_ovf_u4 or
                 ILOpCode.Conv_ovf_i1_un or ILOpCode.Conv_ovf_i2_un or ILOpCode.Conv_ovf_i4_un or
                 ILOpCode.Conv_ovf_u1_un or ILOpCode.Conv_ovf_u2_un or ILOpCode.Conv_ovf_u4_un:
                return state.Pop(1).Push(StackValue.CreatePrimitive(StackValueKind.Int32));

            case ILOpCode.Conv_i8 or ILOpCode.Conv_u8 or
                 ILOpCode.Conv_ovf_i8 or ILOpCode.Conv_ovf_u8 or
                 ILOpCode.Conv_ovf_i8_un or ILOpCode.Conv_ovf_u8_un:
                return state.Pop(1).Push(StackValue.CreatePrimitive(StackValueKind.Int64));

            case ILOpCode.Conv_r4 or ILOpCode.Conv_r8 or ILOpCode.Conv_r_un:
                return state.Pop(1).Push(StackValue.CreatePrimitive(StackValueKind.Float));

            case ILOpCode.Conv_i or ILOpCode.Conv_u or
                 ILOpCode.Conv_ovf_i or ILOpCode.Conv_ovf_u or
                 ILOpCode.Conv_ovf_i_un or ILOpCode.Conv_ovf_u_un:
                return state.Pop(1).Push(StackValue.CreatePrimitive(StackValueKind.NativeInt));

            // Cast/type check (pop 1, push ObjRef)
            case ILOpCode.Castclass or ILOpCode.Isinst:
            {
                int token = reader.ReadILToken();
                state = state.Pop(1);
                string? typeName = ResolveTypeName(context.Reader, token);
                return state.Push(StackValue.CreateObjRef(typeName));
            }

            case ILOpCode.Box:
                reader.ReadILToken();
                return state.Pop(1).Push(StackValue.CreateObjRef("object"));

            case ILOpCode.Unbox:
                reader.ReadILToken();
                return state.Pop(1).Push(StackValue.CreateByRef());

            case ILOpCode.Unbox_any:
            {
                int token = reader.ReadILToken();
                state = state.Pop(1);
                string? typeName = ResolveTypeName(context.Reader, token);
                return state.Push(StackValue.FromTypeName(typeName ?? "object"));
            }

            // Array operations
            case ILOpCode.Newarr:
                reader.ReadILToken();
                return state.Pop(1).Push(StackValue.CreateObjRef());

            case ILOpCode.Ldlen:
                return state.Pop(1).Push(StackValue.CreatePrimitive(StackValueKind.NativeInt));

            case ILOpCode.Ldelem_i1 or ILOpCode.Ldelem_i2 or ILOpCode.Ldelem_i4 or
                 ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_u4:
                return state.Pop(2).Push(StackValue.CreatePrimitive(StackValueKind.Int32));

            case ILOpCode.Ldelem_i8:
                return state.Pop(2).Push(StackValue.CreatePrimitive(StackValueKind.Int64));

            case ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8:
                return state.Pop(2).Push(StackValue.CreatePrimitive(StackValueKind.Float));

            case ILOpCode.Ldelem_i:
                return state.Pop(2).Push(StackValue.CreatePrimitive(StackValueKind.NativeInt));

            case ILOpCode.Ldelem_ref:
                return state.Pop(2).Push(StackValue.CreateObjRef());

            case ILOpCode.Ldelem:
            {
                int token = reader.ReadILToken();
                state = state.Pop(2);
                string? typeName = ResolveTypeName(context.Reader, token);
                return state.Push(StackValue.FromTypeName(typeName ?? "object"));
            }

            case ILOpCode.Ldelema:
                reader.ReadILToken();
                return state.Pop(2).Push(StackValue.CreateByRef());

            case ILOpCode.Stelem_i or ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2 or
                 ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8 or
                 ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8 or ILOpCode.Stelem_ref:
                return state.Pop(3);

            case ILOpCode.Stelem:
                reader.ReadILToken();
                return state.Pop(3);

            // Indirect loads
            case ILOpCode.Ldind_i1 or ILOpCode.Ldind_i2 or ILOpCode.Ldind_i4 or
                 ILOpCode.Ldind_u1 or ILOpCode.Ldind_u2 or ILOpCode.Ldind_u4:
                return state.Pop(1).Push(StackValue.CreatePrimitive(StackValueKind.Int32));

            case ILOpCode.Ldind_i8:
                return state.Pop(1).Push(StackValue.CreatePrimitive(StackValueKind.Int64));

            case ILOpCode.Ldind_r4 or ILOpCode.Ldind_r8:
                return state.Pop(1).Push(StackValue.CreatePrimitive(StackValueKind.Float));

            case ILOpCode.Ldind_i:
                return state.Pop(1).Push(StackValue.CreatePrimitive(StackValueKind.NativeInt));

            case ILOpCode.Ldind_ref:
                return state.Pop(1).Push(StackValue.CreateObjRef());

            // Indirect stores
            case ILOpCode.Stind_i or ILOpCode.Stind_i1 or ILOpCode.Stind_i2 or
                 ILOpCode.Stind_i4 or ILOpCode.Stind_i8 or
                 ILOpCode.Stind_r4 or ILOpCode.Stind_r8 or ILOpCode.Stind_ref:
                return state.Pop(2);

            // Object creation via token (ldobj/stobj/cpobj/initobj/sizeof/mkrefany/refanytype/refanyval)
            case ILOpCode.Ldobj:
            {
                int token = reader.ReadILToken();
                state = state.Pop(1);
                string? typeName = ResolveTypeName(context.Reader, token);
                return state.Push(StackValue.FromTypeName(typeName ?? "object"));
            }

            case ILOpCode.Stobj or ILOpCode.Cpobj:
                reader.ReadILToken();
                return state.Pop(2);

            case ILOpCode.Initobj:
                reader.ReadILToken();
                return state.Pop(1);

            case ILOpCode.Sizeof:
                reader.ReadILToken();
                return state.Push(StackValue.CreatePrimitive(StackValueKind.Int32));

            case ILOpCode.Mkrefany:
                reader.ReadILToken();
                return state.Pop(1).Push(StackValue.CreateValueType("System.TypedReference"));

            case ILOpCode.Refanytype:
                return state.Pop(1).Push(StackValue.CreatePrimitive(StackValueKind.NativeInt));

            case ILOpCode.Refanyval:
                reader.ReadILToken();
                return state.Pop(1).Push(StackValue.CreateByRef());

            // Ldtoken
            case ILOpCode.Ldtoken:
                reader.ReadILToken();
                return state.Push(StackValue.CreateValueType("System.RuntimeHandle"));

            // Localloc
            case ILOpCode.Localloc:
                return state.Pop(1).Push(StackValue.CreatePrimitive(StackValueKind.NativeInt));

            // Branches — consume comparison operands, skip target
            case ILOpCode.Br or ILOpCode.Br_s or ILOpCode.Leave or ILOpCode.Leave_s:
                reader.ReadBranchDestination(opcode);
                return state;

            case ILOpCode.Brfalse or ILOpCode.Brfalse_s or ILOpCode.Brtrue or ILOpCode.Brtrue_s:
                reader.ReadBranchDestination(opcode);
                return state.Pop(1);

            case ILOpCode.Beq or ILOpCode.Beq_s or
                 ILOpCode.Bge or ILOpCode.Bge_s or ILOpCode.Bge_un or ILOpCode.Bge_un_s or
                 ILOpCode.Bgt or ILOpCode.Bgt_s or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s or
                 ILOpCode.Ble or ILOpCode.Ble_s or ILOpCode.Ble_un or ILOpCode.Ble_un_s or
                 ILOpCode.Blt or ILOpCode.Blt_s or ILOpCode.Blt_un or ILOpCode.Blt_un_s or
                 ILOpCode.Bne_un or ILOpCode.Bne_un_s:
                reader.ReadBranchDestination(opcode);
                return state.Pop(2);

            // Switch
            case ILOpCode.Switch:
            {
                uint count = reader.ReadILUInt32();
                for (uint i = 0; i < count; i++)
                    reader.ReadILUInt32();
                return state.Pop(1);
            }

            // Return
            case ILOpCode.Ret:
                return context.HasReturnValue ? state.Pop(1) : state;

            // Throw (empties stack)
            case ILOpCode.Throw:
                return StackState.Empty;

            case ILOpCode.Rethrow:
                return StackState.Empty;

            // Endfinally/endfilter
            case ILOpCode.Endfinally:
                return StackState.Empty;

            case ILOpCode.Endfilter:
                return state.Pop(1);

            // Nop / break / prefixes
            case ILOpCode.Nop or ILOpCode.Break:
                return state;

            case ILOpCode.Volatile or ILOpCode.Tail or ILOpCode.Readonly:
                return state;

            case ILOpCode.Unaligned:
                reader.ReadILByte();
                return state;

            case ILOpCode.Constrained:
                reader.ReadILToken();
                return state;

            // Ckfinite (pop 1 float, push 1 float)
            case ILOpCode.Ckfinite:
                return state;

            // Arglist
            case ILOpCode.Arglist:
                return state.Push(StackValue.CreateValueType("System.RuntimeArgumentHandle"));

            // Ldftn / ldvirtftn
            case ILOpCode.Ldftn:
                reader.ReadILToken();
                return state.Push(StackValue.CreatePrimitive(StackValueKind.NativeInt));

            case ILOpCode.Ldvirtftn:
                reader.ReadILToken();
                return state.Pop(1).Push(StackValue.CreatePrimitive(StackValueKind.NativeInt));

            // Cpblk / initblk (pop 3)
            case ILOpCode.Cpblk or ILOpCode.Initblk:
                return state.Pop(3);

            // Jmp
            case ILOpCode.Jmp:
                reader.ReadILToken();
                return StackState.Empty;

            // Default: try to skip and handle generically
            default:
                if (!reader.TrySkip(opcode))
                    return state;
                if (resultKind != StackValueKind.Unknown)
                    return state.Push(StackValue.CreatePrimitive(resultKind));
                return state;
        }
    }

    static StackValue MergeBinaryResult(StackValue left, StackValue right)
    {
        // ECMA-335 Table III.2: binary numeric operations result type
        if (left.Kind == right.Kind)
            return left;
        if (left.Kind is StackValueKind.NativeInt || right.Kind is StackValueKind.NativeInt)
            return StackValue.CreatePrimitive(StackValueKind.NativeInt);
        if (left.Kind is StackValueKind.Float || right.Kind is StackValueKind.Float)
            return StackValue.CreatePrimitive(StackValueKind.Float);
        if (left.Kind is StackValueKind.Int64 || right.Kind is StackValueKind.Int64)
            return StackValue.CreatePrimitive(StackValueKind.Int64);
        return StackValue.CreatePrimitive(StackValueKind.Int32);
    }

    static StackState ApplyCall(MetadataReader reader, int token, ILOpCode opcode, StackState state)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            int paramCount;
            bool isStatic;
            string returnType;

            switch (handle.Kind)
            {
                case HandleKind.MethodDefinition:
                    var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                    var defSig = methodDef.DecodeSignature(Metadata.SignatureDecoder.Instance, genericContext: null);
                    paramCount = defSig.ParameterTypes.Length;
                    isStatic = methodDef.Attributes.HasFlag(MethodAttributes.Static);
                    returnType = defSig.ReturnType;
                    break;

                case HandleKind.MemberReference:
                {
                    var memberRef = reader.GetMemberReference((MemberReferenceHandle)handle);
                    var genericCtx = BuildGenericContextForMemberRef(reader, memberRef);
                    var refSig = memberRef.DecodeMethodSignature(Metadata.SignatureDecoder.Instance, genericCtx);
                    paramCount = refSig.ParameterTypes.Length;
                    isStatic = !refSig.Header.IsInstance;
                    returnType = refSig.ReturnType;
                    break;
                }

                case HandleKind.MethodSpecification:
                {
                    var spec = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                    var methodTypeArgs = spec.DecodeSignature(Metadata.SignatureDecoder.Instance, genericContext: null);
                    var genericCtx = BuildGenericContextForMethodSpec(reader, spec.Method, methodTypeArgs);
                    return ApplyCallWithContext(reader, spec.Method, opcode, state, genericCtx);
                }

                default:
                    return state;
            }

            // Pop arguments
            int popCount = paramCount;
            if (opcode == ILOpCode.Newobj)
            {
                // newobj doesn't pop 'this' (it's being created)
            }
            else if (!isStatic)
            {
                popCount++; // pop 'this'
            }

            state = state.Pop(popCount);

            // Push result
            if (opcode == ILOpCode.Newobj)
            {
                // newobj pushes the new object
                var declaringType = ResolveDeclaringType(reader, handle);
                state = state.Push(StackValue.CreateObjRef(declaringType));
            }
            else if (returnType is not ("System.Void" or "void"))
            {
                state = state.Push(StackValue.FromTypeName(returnType));
            }

            return state;
        }
        catch
        {
            return state;
        }
    }

    /// <summary>
    /// Apply a call with an explicit generic context (used for MethodSpecification).
    /// </summary>
    static StackState ApplyCallWithContext(MetadataReader reader, EntityHandle methodHandle,
        ILOpCode opcode, StackState state, Metadata.GenericContext? genericCtx)
    {
        try
        {
            int paramCount;
            bool isStatic;
            string returnType;

            switch (methodHandle.Kind)
            {
                case HandleKind.MethodDefinition:
                    var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)methodHandle);
                    var defSig = methodDef.DecodeSignature(Metadata.SignatureDecoder.Instance, genericCtx);
                    paramCount = defSig.ParameterTypes.Length;
                    isStatic = methodDef.Attributes.HasFlag(MethodAttributes.Static);
                    returnType = defSig.ReturnType;
                    break;

                case HandleKind.MemberReference:
                    var memberRef = reader.GetMemberReference((MemberReferenceHandle)methodHandle);
                    genericCtx ??= BuildGenericContextForMemberRef(reader, memberRef);
                    var refSig = memberRef.DecodeMethodSignature(Metadata.SignatureDecoder.Instance, genericCtx);
                    paramCount = refSig.ParameterTypes.Length;
                    isStatic = !refSig.Header.IsInstance;
                    returnType = refSig.ReturnType;
                    break;

                default:
                    return state;
            }

            int popCount = paramCount;
            if (opcode == ILOpCode.Newobj) { }
            else if (!isStatic) popCount++;

            state = state.Pop(popCount);

            if (opcode == ILOpCode.Newobj)
            {
                var declaringType = ResolveDeclaringType(reader, methodHandle);
                state = state.Push(StackValue.CreateObjRef(declaringType));
            }
            else if (returnType is not ("System.Void" or "void"))
            {
                state = state.Push(StackValue.FromTypeName(returnType));
            }

            return state;
        }
        catch
        {
            return state;
        }
    }

    /// <summary>
    /// Build a GenericContext from a MemberReference's parent type (if it's a generic instantiation).
    /// </summary>
    internal static Metadata.GenericContext? BuildGenericContextForMemberRef(MetadataReader reader, MemberReference memberRef)
    {
        try
        {
            if (memberRef.Parent.Kind == HandleKind.TypeSpecification)
            {
                var typeSpec = reader.GetTypeSpecification((TypeSpecificationHandle)memberRef.Parent);
                // Decode the parent type to get type arguments embedded in the name
                var parentType = typeSpec.DecodeSignature(Metadata.SignatureDecoder.Instance, genericContext: null);
                // Extract type arguments from the generic instantiation name like "Dict<string, Logger>"
                var typeArgs = ExtractGenericArguments(parentType);
                if (typeArgs.Count > 0)
                    return new Metadata.GenericContext(typeArgs, []);
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Build a GenericContext for a MethodSpecification (generic method instantiation).
    /// </summary>
    static Metadata.GenericContext? BuildGenericContextForMethodSpec(
        MetadataReader reader, EntityHandle baseMethod, ImmutableArray<string> methodTypeArgs)
    {
        try
        {
            IReadOnlyList<string> typeParams = [];

            // Get type arguments from the declaring type if applicable
            if (baseMethod.Kind == HandleKind.MemberReference)
            {
                var memberRef = reader.GetMemberReference((MemberReferenceHandle)baseMethod);
                var parentCtx = BuildGenericContextForMemberRef(reader, memberRef);
                if (parentCtx is not null)
                    typeParams = parentCtx.TypeParameters;
            }

            return new Metadata.GenericContext(typeParams, [.. methodTypeArgs]);
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Extract generic type arguments from a decoded type name like "Dict&lt;string, int&gt;".
    /// </summary>
    static IReadOnlyList<string> ExtractGenericArguments(string typeName)
    {
        int openAngle = typeName.IndexOf('<');
        if (openAngle < 0) return [];

        // Find matching closing angle bracket
        int depth = 0;
        List<string> args = [];
        int argStart = openAngle + 1;

        for (int i = openAngle; i < typeName.Length; i++)
        {
            switch (typeName[i])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    if (depth == 0)
                    {
                        var arg = typeName[argStart..i].Trim();
                        if (arg.Length > 0) args.Add(arg);
                        return args;
                    }
                    break;
                case ',' when depth == 1:
                    args.Add(typeName[argStart..i].Trim());
                    argStart = i + 1;
                    break;
            }
        }

        return args;
    }

    static string? ResolveDeclaringType(MetadataReader reader, EntityHandle methodHandle)
    {
        try
        {
            if (methodHandle.Kind == HandleKind.MethodDefinition)
            {
                var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)methodHandle);
                var typeDef = reader.GetTypeDefinition(methodDef.GetDeclaringType());
                return reader.GetFullTypeName(typeDef);
            }
            if (methodHandle.Kind == HandleKind.MemberReference)
            {
                var memberRef = reader.GetMemberReference((MemberReferenceHandle)methodHandle);
                var parent = memberRef.Parent;
                if (parent.Kind == HandleKind.TypeReference)
                {
                    var typeRef = reader.GetTypeReference((TypeReferenceHandle)parent);
                    string ns = reader.GetString(typeRef.Namespace);
                    string name = reader.GetString(typeRef.Name);
                    return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                }
                if (parent.Kind == HandleKind.TypeDefinition)
                {
                    var typeDef = reader.GetTypeDefinition((TypeDefinitionHandle)parent);
                    return reader.GetFullTypeName(typeDef);
                }
            }
        }
        catch { }
        return null;
    }

    static StackValue ResolveArgType(MethodBodyContext context, int argIndex)
    {
        // Adjust for 'this' parameter
        if (context.HasThis)
        {
            if (argIndex == 0)
                return StackValue.CreateObjRef(context.DeclaringType);
            argIndex--;
        }

        if (argIndex < context.ParameterTypes.Count)
            return StackValue.FromTypeName(context.ParameterTypes[argIndex]);

        return StackValue.CreateUnknown();
    }

    static StackValue ResolveLocalType(MethodBodyContext context, int locIndex)
    {
        if (locIndex < context.LocalTypes.Count)
            return StackValue.FromTypeName(context.LocalTypes[locIndex]);
        return StackValue.CreateUnknown();
    }

    static StackValue ResolveFieldType(MetadataReader reader, int token)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind == HandleKind.FieldDefinition)
            {
                var fieldDef = reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                string typeName = fieldDef.DecodeSignature(Metadata.SignatureDecoder.Instance, genericContext: null);
                return StackValue.FromTypeName(typeName);
            }
            if (handle.Kind == HandleKind.MemberReference)
            {
                var memberRef = reader.GetMemberReference((MemberReferenceHandle)handle);
                var genericCtx = BuildGenericContextForMemberRef(reader, memberRef);
                string typeName = memberRef.DecodeFieldSignature(Metadata.SignatureDecoder.Instance, genericCtx);
                return StackValue.FromTypeName(typeName);
            }
        }
        catch { }
        return StackValue.CreateUnknown();
    }

    static string? ResolveTypeName(MetadataReader reader, int token)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind == HandleKind.TypeDefinition)
            {
                var typeDef = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                return reader.GetFullTypeName(typeDef);
            }
            if (handle.Kind == HandleKind.TypeReference)
            {
                var typeRef = reader.GetTypeReference((TypeReferenceHandle)handle);
                string ns = reader.GetString(typeRef.Namespace);
                string name = reader.GetString(typeRef.Name);
                return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            }
            if (handle.Kind == HandleKind.TypeSpecification)
            {
                var typeSpec = reader.GetTypeSpecification((TypeSpecificationHandle)handle);
                return typeSpec.DecodeSignature(Metadata.SignatureDecoder.Instance, genericContext: null);
            }
        }
        catch { }
        return null;
    }

    static string? ResolveExceptionTypeName(MetadataReader reader, EntityHandle catchType)
    {
        if (catchType.IsNil) return "System.Object";
        return ResolveTypeName(reader, MetadataTokens.GetToken(catchType));
    }

    static bool IsLeaveTarget(MethodBodyContext context, BasicBlock source, BasicBlock target)
    {
        // Check if the edge from source→target crosses an exception region boundary via leave
        var ilBytes = context.ILBytes.AsSpan(source.Start, source.Size);
        var reader = new ILReaderLite(ilBytes);
        ILOpCode lastOpcode = ILOpCode.Nop;
        while (reader.HasNext)
        {
            lastOpcode = reader.ReadILOpcode();
            if (!reader.TrySkip(lastOpcode))
                break;
        }
        return lastOpcode is ILOpCode.Leave or ILOpCode.Leave_s;
    }

    static bool IsExceptionHandlerEntry(MethodBodyContext context, int offset)
    {
        foreach (var region in context.ExceptionRegions)
        {
            if (region.HandlerOffset == offset)
                return true;
            if (region.Kind == ExceptionRegionKind.Filter && region.FilterOffset == offset)
                return true;
        }
        return false;
    }

    static void BuildParameterVariables(MethodBodyContext context, StackSimulationResult result)
    {
        int index = 0;
        if (context.HasThis)
        {
            result.Parameters.Add(new ILVariable(
                ILVariableKind.Parameter, StackValueKind.ObjRef, null, index++));
        }
        foreach (var typeName in context.ParameterTypes)
        {
            var sv = StackValue.FromTypeName(typeName);
            result.Parameters.Add(new ILVariable(
                ILVariableKind.Parameter, sv.Kind, typeName, index++));
        }
    }

    static void BuildLocalVariables(MethodBodyContext context, StackSimulationResult result)
    {
        for (int i = 0; i < context.LocalTypes.Count; i++)
        {
            var typeName = context.LocalTypes[i];
            var sv = StackValue.FromTypeName(typeName);
            result.Locals.Add(new ILVariable(
                ILVariableKind.Local, sv.Kind, typeName, i));
        }
    }

    static void BuildExceptionVariables(MethodBodyContext context, ControlFlowGraph cfg, StackSimulationResult result)
    {
        int index = 0;
        foreach (var region in context.ExceptionRegions)
        {
            if (region.Kind is ExceptionRegionKind.Catch or ExceptionRegionKind.Filter)
            {
                string? typeName = region.Kind == ExceptionRegionKind.Catch
                    ? ResolveExceptionTypeName(context.Reader, region.CatchType)
                    : "System.Object";
                result.ExceptionSlots.Add(new ILVariable(
                    ILVariableKind.ExceptionSlot, StackValueKind.ObjRef, typeName, index++));
            }
        }
    }
}
