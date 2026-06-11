using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using DotnetInspector.Metadata;

namespace DotnetInspector.Decompiler;

/// <summary>
/// Builds an <see cref="ILAstMethod"/> from IL bytecode by walking each basic
/// block, decoding opcodes into <see cref="ILAstExpression"/> nodes, and
/// replacing stack operations with explicit <see cref="ILAstAssignment"/>s.
/// </summary>
public static class ILAstBuilder
{
    /// <summary>
    /// Build the ILAst for a method.
    /// </summary>
    public static ILAstMethod Build(MethodBodyContext context)
    {
        var cfg = ControlFlowGraph.Create(context);
        var simResult = StackSimulator.Simulate(context, cfg);
        return Build(context, cfg, simResult);
    }

    /// <summary>
    /// Build the ILAst from pre-computed CFG and simulation results.
    /// </summary>
    public static ILAstMethod Build(
        MethodBodyContext context,
        ControlFlowGraph cfg,
        StackSimulationResult simResult)
    {
        var method = new ILAstMethod();
        method.Parameters.AddRange(simResult.Parameters);
        method.Locals.AddRange(simResult.Locals);

        for (int i = 0; i < cfg.BasicBlocks.Count; i++)
        {
            var bb = cfg.BasicBlocks[i];
            var block = new ILAstBlock { Label = $"Block_{i}", Offset = bb.Start };

            simResult.BlockEntryStacks.TryGetValue(bb.Start, out var entryStack);
            entryStack ??= StackState.Empty;

            BuildBlock(context, bb, entryStack, block);
            method.Blocks.Add(block);
        }

        return method;
    }

    static void BuildBlock(
        MethodBodyContext context,
        BasicBlock bb,
        StackState entryStack,
        ILAstBlock block)
    {
        var ilBytes = context.ILBytes.AsSpan(bb.Start, bb.Size);
        var reader = new ILReaderLite(ilBytes, baseOffset: bb.Start);
        var stack = new Stack<ILAstExpression>();

        // Push entry stack values as synthetic ldloc-like expressions
        for (int i = 0; i < entryStack.Height; i++)
        {
            var sv = entryStack.Values[i];
            stack.Push(new ILAstExpression
            {
                OpCode = ILOpCode.Nop,
                Operand = $"S_in_{i}",
                ResultType = sv,
                Offset = bb.Start
            });
        }

        while (reader.HasNext)
        {
            int offset = bb.Start + reader.Offset;
            var opcode = reader.ReadILOpcode();

            var node = DecodeInstruction(context, ref reader, opcode, offset, stack);
            if (node is null) continue;

            // Opcodes that push a value: wrap in assignment if needed later
            // Opcodes that don't push: emit as statement
            if (PushesValue(opcode, context, node))
            {
                stack.Push(node);
            }
            else
            {
                // A statement that runs while values sit on the simulated stack
                // executes BEFORE those values render (they evaluate at their
                // consumption site). Any stacked value the statement could
                // invalidate must be spilled to a slot first, or the rendered
                // expression reads post-mutation state (Release csc keeps
                // captured field reads on the stack across the mutation).
                SpillVulnerableStackValues(node, stack, block, offset);
                block.Nodes.Add(new ILAstStatement { Expression = node, Offset = offset });
            }
        }

        // Flush remaining stack values as explicit S_out assignments.
        // These represent values passed to successor blocks via the stack
        // (e.g., ternary branches that load a value and branch to a join point).
        if (stack.Count > 0)
        {
            var remaining = stack.ToArray();
            Array.Reverse(remaining);

            // The values were computed before the block's terminator ran, so
            // the spill must precede it — a trailing branch statement must stay
            // last for condition extraction and structuring.
            int insertAt = block.Nodes.Count;
            if (insertAt > 0 && block.Nodes[insertAt - 1] is ILAstStatement { Expression.OpCode: var termOp }
                && (termOp.IsBranch() || termOp is ILOpCode.Switch or ILOpCode.Leave or ILOpCode.Leave_s))
            {
                insertAt--;
            }

            for (int i = 0; i < remaining.Length; i++)
            {
                var variable = new ILVariable(
                    ILVariableKind.StackSlot,
                    remaining[i].ResultType,
                    index: i);
                block.Nodes.Insert(insertAt++, new ILAstAssignment
                {
                    Variable = variable,
                    Value = remaining[i],
                    Offset = remaining[i].Offset
                });
            }
        }
    }

    /// <summary>
    /// Spills stack entries that <paramref name="stmt"/> could invalidate:
    /// reads of a field the statement writes, reads of a local/argument the
    /// statement stores, element/indirect reads when it writes through one,
    /// and any mutable-state read when it calls (a callee can mutate
    /// anything). Spilled entries become slot loads; dup-shared occurrences
    /// are replaced together so the tree is never evaluated twice.
    /// </summary>
    static void SpillVulnerableStackValues(
        ILAstExpression stmt, Stack<ILAstExpression> stack, ILAstBlock block, int offset)
    {
        if (stack.Count == 0)
            return;

        string? writtenField = stmt.OpCode is ILOpCode.Stfld or ILOpCode.Stsfld ? stmt.Operand : null;
        string? writtenLocal = stmt.OpCode is ILOpCode.Stloc or ILOpCode.Stloc_s
            or ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3
            ? stmt.Operand ?? StoredSlotName(stmt.OpCode)
            : stmt.OpCode is ILOpCode.Starg or ILOpCode.Starg_s ? stmt.Operand : null;
        bool writesElementOrIndirect = stmt.OpCode is ILOpCode.Stelem or ILOpCode.Stelem_i
            or ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2 or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8
            or ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8 or ILOpCode.Stelem_ref
            or ILOpCode.Stind_i or ILOpCode.Stind_i1 or ILOpCode.Stind_i2 or ILOpCode.Stind_i4
            or ILOpCode.Stind_i8 or ILOpCode.Stind_r4 or ILOpCode.Stind_r8 or ILOpCode.Stind_ref
            or ILOpCode.Stobj or ILOpCode.Initobj or ILOpCode.Cpobj or ILOpCode.Cpblk or ILOpCode.Initblk;
        bool stmtCalls = TreeContainsCall(stmt);

        if (writtenField is null && writtenLocal is null && !writesElementOrIndirect && !stmtCalls)
            return;

        var entries = stack.ToArray(); // top of stack first
        bool spilled = false;
        for (int i = 0; i < entries.Length; i++)
        {
            var value = entries[i];
            if (value.OpCode == ILOpCode.Nop)
                continue; // already a slot or block-entry load

            bool vulnerable =
                (writtenField is not null && TreeReadsField(value, writtenField))
                || (writtenLocal is not null && TreeReadsLocalOrArg(value, writtenLocal))
                || (writesElementOrIndirect && TreeReadsElementOrIndirect(value))
                || (stmtCalls && TreeReadsMutableState(value));
            if (!vulnerable)
                continue;

            int position = entries.Length - 1 - i; // stack slot index from the bottom
            var variable = new ILVariable(ILVariableKind.StackSlot, value.ResultType, index: position);
            block.Nodes.Add(new ILAstAssignment { Variable = variable, Value = value, Offset = offset });
            var slotLoad = new ILAstExpression
            {
                OpCode = ILOpCode.Nop,
                Operand = variable.Name,
                ResultType = value.ResultType,
                Offset = offset,
            };
            for (int j = 0; j < entries.Length; j++)
            {
                if (ReferenceEquals(entries[j], value))
                    entries[j] = slotLoad;
            }
            spilled = true;
        }

        if (!spilled)
            return;
        stack.Clear();
        for (int j = entries.Length - 1; j >= 0; j--)
            stack.Push(entries[j]);
    }

    static string? StoredSlotName(ILOpCode op) => op switch
    {
        ILOpCode.Stloc_0 => "V_0",
        ILOpCode.Stloc_1 => "V_1",
        ILOpCode.Stloc_2 => "V_2",
        ILOpCode.Stloc_3 => "V_3",
        _ => null,
    };

    static bool TreeContainsCall(ILAstExpression expr)
    {
        if (expr.OpCode is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Calli or ILOpCode.Newobj)
            return true;
        foreach (var arg in expr.Arguments)
            if (TreeContainsCall(arg))
                return true;
        return false;
    }

    static bool TreeReadsField(ILAstExpression expr, string field)
    {
        if (expr.OpCode is ILOpCode.Ldfld or ILOpCode.Ldsfld or ILOpCode.Ldflda or ILOpCode.Ldsflda
            && expr.Operand == field)
            return true;
        foreach (var arg in expr.Arguments)
            if (TreeReadsField(arg, field))
                return true;
        return false;
    }

    static bool TreeReadsLocalOrArg(ILAstExpression expr, string name)
    {
        if (expr.OpCode is ILOpCode.Ldloc or ILOpCode.Ldloc_s
            or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3
            or ILOpCode.Ldloca or ILOpCode.Ldloca_s
            or ILOpCode.Ldarg or ILOpCode.Ldarg_s
            or ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3
            or ILOpCode.Ldarga or ILOpCode.Ldarga_s)
        {
            string? loaded = expr.Operand ?? expr.OpCode switch
            {
                ILOpCode.Ldloc_0 => "V_0",
                ILOpCode.Ldloc_1 => "V_1",
                ILOpCode.Ldloc_2 => "V_2",
                ILOpCode.Ldloc_3 => "V_3",
                ILOpCode.Ldarg_0 => "P_0",
                ILOpCode.Ldarg_1 => "P_1",
                ILOpCode.Ldarg_2 => "P_2",
                ILOpCode.Ldarg_3 => "P_3",
                _ => null,
            };
            if (loaded == name)
                return true;
        }
        foreach (var arg in expr.Arguments)
            if (TreeReadsLocalOrArg(arg, name))
                return true;
        return false;
    }

    static bool TreeReadsElementOrIndirect(ILAstExpression expr)
    {
        if (expr.OpCode is ILOpCode.Ldelem or ILOpCode.Ldelema
            or ILOpCode.Ldelem_i or ILOpCode.Ldelem_i1 or ILOpCode.Ldelem_i2 or ILOpCode.Ldelem_i4
            or ILOpCode.Ldelem_i8 or ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8 or ILOpCode.Ldelem_ref
            or ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_u4
            or ILOpCode.Ldind_i or ILOpCode.Ldind_i1 or ILOpCode.Ldind_i2 or ILOpCode.Ldind_i4
            or ILOpCode.Ldind_i8 or ILOpCode.Ldind_r4 or ILOpCode.Ldind_r8 or ILOpCode.Ldind_ref
            or ILOpCode.Ldind_u1 or ILOpCode.Ldind_u2 or ILOpCode.Ldind_u4 or ILOpCode.Ldobj)
            return true;
        foreach (var arg in expr.Arguments)
            if (TreeReadsElementOrIndirect(arg))
                return true;
        return false;
    }

    static bool TreeReadsMutableState(ILAstExpression expr)
    {
        if (expr.OpCode is ILOpCode.Ldfld or ILOpCode.Ldsfld or ILOpCode.Ldflda or ILOpCode.Ldsflda)
            return true;
        return TreeReadsElementOrIndirect(expr);
    }

    static ILAstExpression? DecodeInstruction(
        MethodBodyContext context,
        ref ILReaderLite reader,
        ILOpCode opcode,
        int offset,
        Stack<ILAstExpression> stack)
    {
        switch (opcode)
        {
            // Loads — push a value
            case ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3:
            {
                int index = opcode - ILOpCode.Ldarg_0;
                return MakeLoad(opcode, $"P_{index}", ResolveArgType(context, index), offset);
            }

            case ILOpCode.Ldarg_s:
            {
                int index = reader.ReadILByte();
                return MakeLoad(opcode, $"P_{index}", ResolveArgType(context, index), offset);
            }

            case ILOpCode.Ldarg:
            {
                int index = reader.ReadILUInt16();
                return MakeLoad(opcode, $"P_{index}", ResolveArgType(context, index), offset);
            }

            case ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3:
            {
                int index = opcode - ILOpCode.Ldloc_0;
                return MakeLoad(opcode, ResolveLocalName(context, index), ResolveLocalType(context, index), offset);
            }

            case ILOpCode.Ldloc_s:
            {
                int index = reader.ReadILByte();
                return MakeLoad(opcode, ResolveLocalName(context, index), ResolveLocalType(context, index), offset);
            }

            case ILOpCode.Ldloc:
            {
                int index = reader.ReadILUInt16();
                return MakeLoad(opcode, ResolveLocalName(context, index), ResolveLocalType(context, index), offset);
            }

            // Address-of
            case ILOpCode.Ldloca_s:
            {
                int index = reader.ReadILByte();
                return MakeLoad(opcode, ResolveLocalName(context, index), StackValue.CreateByRef(), offset);
            }

            case ILOpCode.Ldloca:
            {
                int index = reader.ReadILUInt16();
                return MakeLoad(opcode, ResolveLocalName(context, index), StackValue.CreateByRef(), offset);
            }

            case ILOpCode.Ldarga_s:
            {
                int index = reader.ReadILByte();
                return MakeLoad(opcode, $"P_{index}", StackValue.CreateByRef(), offset);
            }

            case ILOpCode.Ldarga:
            {
                int index = reader.ReadILUInt16();
                return MakeLoad(opcode, $"P_{index}", StackValue.CreateByRef(), offset);
            }

            // Constants
            case ILOpCode.Ldc_i4_m1: return MakeLiteral(opcode, "-1", StackValueKind.Int32, offset);
            case ILOpCode.Ldc_i4_0: return MakeLiteral(opcode, "0", StackValueKind.Int32, offset);
            case ILOpCode.Ldc_i4_1: return MakeLiteral(opcode, "1", StackValueKind.Int32, offset);
            case ILOpCode.Ldc_i4_2: return MakeLiteral(opcode, "2", StackValueKind.Int32, offset);
            case ILOpCode.Ldc_i4_3: return MakeLiteral(opcode, "3", StackValueKind.Int32, offset);
            case ILOpCode.Ldc_i4_4: return MakeLiteral(opcode, "4", StackValueKind.Int32, offset);
            case ILOpCode.Ldc_i4_5: return MakeLiteral(opcode, "5", StackValueKind.Int32, offset);
            case ILOpCode.Ldc_i4_6: return MakeLiteral(opcode, "6", StackValueKind.Int32, offset);
            case ILOpCode.Ldc_i4_7: return MakeLiteral(opcode, "7", StackValueKind.Int32, offset);
            case ILOpCode.Ldc_i4_8: return MakeLiteral(opcode, "8", StackValueKind.Int32, offset);

            case ILOpCode.Ldc_i4_s:
                return MakeLiteral(opcode, ((sbyte)reader.ReadILByte()).ToString(), StackValueKind.Int32, offset);

            case ILOpCode.Ldc_i4:
                return MakeLiteral(opcode, ((int)reader.ReadILUInt32()).ToString(), StackValueKind.Int32, offset);

            case ILOpCode.Ldc_i8:
                return MakeLiteral(opcode, ((long)reader.ReadILUInt64()).ToString(), StackValueKind.Int64, offset);

            case ILOpCode.Ldc_r4:
            {
                float val = BitConverter.Int32BitsToSingle((int)reader.ReadILUInt32());
                return MakeLiteral(opcode, val.ToString("R", System.Globalization.CultureInfo.InvariantCulture), StackValueKind.Float, offset);
            }

            case ILOpCode.Ldc_r8:
            {
                double val = BitConverter.Int64BitsToDouble((long)reader.ReadILUInt64());
                return MakeLiteral(opcode, val.ToString("R", System.Globalization.CultureInfo.InvariantCulture), StackValueKind.Float, offset);
            }

            case ILOpCode.Ldnull:
                return MakeLiteral(opcode, "null", StackValueKind.ObjRef, offset);

            case ILOpCode.Ldstr:
            {
                int token = reader.ReadILToken();
                string? str = ResolveString(context.Reader, token);
                return MakeLiteral(opcode, $"\"{str}\"", StackValueKind.ObjRef, offset);
            }

            // Dup
            case ILOpCode.Dup:
            {
                var val = TryPop(stack);
                var dupExpr = new ILAstExpression
                {
                    OpCode = opcode,
                    ResultType = val.ResultType,
                    Offset = offset,
                    Arguments = { val }
                };
                // Push the original value back plus the dup copy
                stack.Push(val);
                return dupExpr;
            }

            // Pop
            case ILOpCode.Pop:
            {
                var val = TryPop(stack);
                return new ILAstExpression
                {
                    OpCode = opcode,
                    Arguments = { val },
                    ResultType = StackValue.CreateUnknown(),
                    Offset = offset
                };
            }

            // Stores
            case ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3:
            {
                int index = opcode - ILOpCode.Stloc_0;
                var val = TryPop(stack);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = ResolveLocalName(context, index),
                    Arguments = { val }, ResultType = StackValue.CreateUnknown(),
                    Offset = offset
                };
            }

            case ILOpCode.Stloc_s:
            {
                int index = reader.ReadILByte();
                var val = TryPop(stack);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = ResolveLocalName(context, index),
                    Arguments = { val }, ResultType = StackValue.CreateUnknown(),
                    Offset = offset
                };
            }

            case ILOpCode.Stloc:
            {
                int index = reader.ReadILUInt16();
                var val = TryPop(stack);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = ResolveLocalName(context, index),
                    Arguments = { val }, ResultType = StackValue.CreateUnknown(),
                    Offset = offset
                };
            }

            case ILOpCode.Starg_s:
            {
                int index = reader.ReadILByte();
                var val = TryPop(stack);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = $"P_{index}",
                    Arguments = { val }, ResultType = StackValue.CreateUnknown(),
                    Offset = offset
                };
            }

            case ILOpCode.Starg:
            {
                int index = reader.ReadILUInt16();
                var val = TryPop(stack);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = $"P_{index}",
                    Arguments = { val }, ResultType = StackValue.CreateUnknown(),
                    Offset = offset
                };
            }

            // Binary arithmetic/comparison — pop 2, push 1
            case ILOpCode.Add or ILOpCode.Add_ovf or ILOpCode.Add_ovf_un or
                 ILOpCode.Sub or ILOpCode.Sub_ovf or ILOpCode.Sub_ovf_un or
                 ILOpCode.Mul or ILOpCode.Mul_ovf or ILOpCode.Mul_ovf_un or
                 ILOpCode.Div or ILOpCode.Div_un or
                 ILOpCode.Rem or ILOpCode.Rem_un or
                 ILOpCode.And or ILOpCode.Or or ILOpCode.Xor or
                 ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Shr_un or
                 ILOpCode.Ceq or ILOpCode.Cgt or ILOpCode.Cgt_un or
                 ILOpCode.Clt or ILOpCode.Clt_un:
            {
                var right = TryPop(stack);
                var left = TryPop(stack);
                var kind = opcode is ILOpCode.Ceq or ILOpCode.Cgt or ILOpCode.Cgt_un
                    or ILOpCode.Clt or ILOpCode.Clt_un
                    ? StackValueKind.Int32
                    : left.ResultType.Kind;
                return new ILAstExpression
                {
                    OpCode = opcode,
                    Arguments = { left, right },
                    ResultType = StackValue.CreatePrimitive(kind),
                    Offset = offset
                };
            }

            // Unary
            case ILOpCode.Neg or ILOpCode.Not:
            {
                var operand = TryPop(stack);
                return new ILAstExpression
                {
                    OpCode = opcode,
                    Arguments = { operand },
                    ResultType = operand.ResultType,
                    Offset = offset
                };
            }

            // Conversions
            case ILOpCode opc when IsConversion(opc):
            {
                var operand = TryPop(stack);
                return new ILAstExpression
                {
                    OpCode = opcode,
                    Arguments = { operand },
                    ResultType = StackValue.CreatePrimitive(opcode.ResultKind()),
                    Offset = offset
                };
            }

            // Calls
            case ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj:
            {
                int token = reader.ReadILToken();
                return BuildCall(context.Reader, token, opcode, offset, stack, context.GenericContext);
            }

            // Field access
            case ILOpCode.Ldfld:
            case ILOpCode.Ldflda:
            {
                int token = reader.ReadILToken();
                var obj = TryPop(stack);
                var (fieldName, fieldType) = ResolveField(context.Reader, token);
                // ldflda returns a managed pointer to the field
                var resultType = opcode == ILOpCode.Ldflda
                    ? StackValue.CreatePrimitive(StackValueKind.ByRef)
                    : fieldType;
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = fieldName,
                    Arguments = { obj }, ResultType = resultType,
                    Offset = offset,
                    Member = MemberRefInfo.FromQualifiedName(fieldName, fieldType.TypeName)
                };
            }

            case ILOpCode.Ldsfld:
            case ILOpCode.Ldsflda:
            {
                int token = reader.ReadILToken();
                var (fieldName, fieldType) = ResolveField(context.Reader, token);
                var resultType = opcode == ILOpCode.Ldsflda
                    ? StackValue.CreatePrimitive(StackValueKind.ByRef)
                    : fieldType;
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = fieldName,
                    ResultType = resultType, Offset = offset,
                    Member = MemberRefInfo.FromQualifiedName(fieldName, fieldType.TypeName, isStatic: true)
                };
            }

            case ILOpCode.Stfld:
            {
                int token = reader.ReadILToken();
                var val = TryPop(stack);
                var obj = TryPop(stack);
                var (fieldName, fieldType) = ResolveField(context.Reader, token);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = fieldName,
                    Arguments = { obj, val }, ResultType = StackValue.CreateUnknown(),
                    Offset = offset,
                    Member = MemberRefInfo.FromQualifiedName(fieldName, fieldType.TypeName)
                };
            }

            case ILOpCode.Stsfld:
            {
                int token = reader.ReadILToken();
                var val = TryPop(stack);
                var (fieldName, fieldType) = ResolveField(context.Reader, token);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = fieldName,
                    Arguments = { val }, ResultType = StackValue.CreateUnknown(),
                    Offset = offset,
                    Member = MemberRefInfo.FromQualifiedName(fieldName, fieldType.TypeName, isStatic: true)
                };
            }

            // Branches
            case ILOpCode.Br or ILOpCode.Br_s or ILOpCode.Leave or ILOpCode.Leave_s:
            {
                int target = reader.ReadBranchDestination(opcode);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = $"IL_{target:X4}",
                    ResultType = StackValue.CreateUnknown(), Offset = offset
                };
            }

            case ILOpCode.Brfalse or ILOpCode.Brfalse_s or ILOpCode.Brtrue or ILOpCode.Brtrue_s:
            {
                int target = reader.ReadBranchDestination(opcode);
                var cond = TryPop(stack);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = $"IL_{target:X4}",
                    Arguments = { cond }, ResultType = StackValue.CreateUnknown(),
                    Offset = offset
                };
            }

            case ILOpCode.Beq or ILOpCode.Beq_s or
                 ILOpCode.Bge or ILOpCode.Bge_s or ILOpCode.Bge_un or ILOpCode.Bge_un_s or
                 ILOpCode.Bgt or ILOpCode.Bgt_s or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s or
                 ILOpCode.Ble or ILOpCode.Ble_s or ILOpCode.Ble_un or ILOpCode.Ble_un_s or
                 ILOpCode.Blt or ILOpCode.Blt_s or ILOpCode.Blt_un or ILOpCode.Blt_un_s or
                 ILOpCode.Bne_un or ILOpCode.Bne_un_s:
            {
                int target = reader.ReadBranchDestination(opcode);
                var right = TryPop(stack);
                var left = TryPop(stack);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = $"IL_{target:X4}",
                    Arguments = { left, right }, ResultType = StackValue.CreateUnknown(),
                    Offset = offset
                };
            }

            // Switch
            case ILOpCode.Switch:
            {
                uint count = reader.ReadILUInt32();
                int baseOffset = reader.Offset + (int)count * 4; // after all target offsets
                var targets = new List<string>();
                for (uint i = 0; i < count; i++)
                {
                    int delta = (int)reader.ReadILUInt32();
                    targets.Add($"IL_{(baseOffset + delta):X4}");
                }
                var index = TryPop(stack);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = string.Join(", ", targets),
                    Arguments = { index }, ResultType = StackValue.CreateUnknown(),
                    Offset = offset
                };
            }

            // Return
            case ILOpCode.Ret:
            {
                var expr = new ILAstExpression
                {
                    OpCode = opcode, ResultType = StackValue.CreateUnknown(),
                    Offset = offset
                };
                if (context.HasReturnValue && stack.Count > 0)
                    expr.Arguments.Add(TryPop(stack));
                return expr;
            }

            // Throw
            case ILOpCode.Throw:
            {
                var obj = TryPop(stack);
                return new ILAstExpression
                {
                    OpCode = opcode, Arguments = { obj },
                    ResultType = StackValue.CreateUnknown(), Offset = offset
                };
            }

            // Cast/type ops
            case ILOpCode.Castclass or ILOpCode.Isinst:
            {
                int token = reader.ReadILToken();
                var obj = TryPop(stack);
                string? typeName = ResolveTypeName(context.Reader, token, context.GenericContext);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = typeName,
                    Arguments = { obj },
                    ResultType = StackValue.CreateObjRef(typeName),
                    Offset = offset
                };
            }

            case ILOpCode.Box:
            {
                int token = reader.ReadILToken();
                var val = TryPop(stack);
                string? typeName = ResolveTypeName(context.Reader, token, context.GenericContext);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = typeName,
                    Arguments = { val },
                    ResultType = StackValue.CreateObjRef("object"),
                    Offset = offset
                };
            }

            case ILOpCode.Unbox_any:
            {
                int token = reader.ReadILToken();
                var obj = TryPop(stack);
                string? typeName = ResolveTypeName(context.Reader, token, context.GenericContext);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = typeName,
                    Arguments = { obj },
                    ResultType = StackValue.FromTypeName(typeName ?? "object"),
                    Offset = offset
                };
            }

            // Object creation with type token
            case ILOpCode.Newarr:
            {
                int token = reader.ReadILToken();
                var size = TryPop(stack);
                string? typeName = ResolveTypeName(context.Reader, token, context.GenericContext);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = typeName,
                    Arguments = { size },
                    ResultType = StackValue.CreateObjRef($"{typeName}[]"),
                    Offset = offset
                };
            }

            case ILOpCode.Ldlen:
            {
                var arr = TryPop(stack);
                return new ILAstExpression
                {
                    OpCode = opcode, Arguments = { arr },
                    ResultType = StackValue.CreatePrimitive(StackValueKind.NativeInt),
                    Offset = offset
                };
            }

            case ILOpCode.Ldtoken:
            {
                int token = reader.ReadILToken();
                string? name = ResolveTokenName(context.Reader, token, context.GenericContext);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = name,
                    ResultType = StackValue.CreateValueType("System.RuntimeHandle"),
                    Offset = offset
                };
            }

            case ILOpCode.Sizeof:
            {
                int token = reader.ReadILToken();
                string? typeName = ResolveTypeName(context.Reader, token, context.GenericContext);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = typeName,
                    ResultType = StackValue.CreatePrimitive(StackValueKind.Int32),
                    Offset = offset
                };
            }

            // Function pointer load
            case ILOpCode.Ldftn:
            {
                int token = reader.ReadILToken();
                string? name = ResolveMethodRefNameFromToken(context.Reader, token, context.GenericContext);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = name,
                    ResultType = StackValue.CreatePrimitive(StackValueKind.NativeInt),
                    Offset = offset,
                    Member = name is null ? null : MemberRefInfo.FromQualifiedName(name)
                };
            }

            case ILOpCode.Ldvirtftn:
            {
                int token = reader.ReadILToken();
                var obj = TryPop(stack);
                string? name = ResolveMethodRefNameFromToken(context.Reader, token, context.GenericContext);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = name,
                    Arguments = { obj },
                    ResultType = StackValue.CreatePrimitive(StackValueKind.NativeInt),
                    Offset = offset,
                    Member = name is null ? null : MemberRefInfo.FromQualifiedName(name)
                };
            }

            // Nop / break / prefixes / endfinally / rethrow
            case ILOpCode.Nop or ILOpCode.Break:
                return new ILAstExpression { OpCode = opcode, ResultType = StackValue.CreateUnknown(), Offset = offset };

            case ILOpCode.Endfinally or ILOpCode.Rethrow:
                return new ILAstExpression { OpCode = opcode, ResultType = StackValue.CreateUnknown(), Offset = offset };

            case ILOpCode.Endfilter:
            {
                var val = TryPop(stack);
                return new ILAstExpression
                {
                    OpCode = opcode, Arguments = { val },
                    ResultType = StackValue.CreateUnknown(), Offset = offset
                };
            }

            case ILOpCode.Volatile or ILOpCode.Tail or ILOpCode.Readonly:
                return null; // prefix, skip

            case ILOpCode.Unaligned:
                reader.ReadILByte();
                return null;

            case ILOpCode.Constrained:
                reader.ReadILToken();
                return null;

            // Initobj
            case ILOpCode.Initobj:
            {
                int token = reader.ReadILToken();
                var addr = TryPop(stack);
                string? typeName = ResolveTypeName(context.Reader, token, context.GenericContext);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = typeName,
                    Arguments = { addr }, ResultType = StackValue.CreateUnknown(),
                    Offset = offset
                };
            }

            // Array element load: pop array, pop index, push element
            case ILOpCode.Ldelem or ILOpCode.Ldelem_i or ILOpCode.Ldelem_i1 or
                 ILOpCode.Ldelem_i2 or ILOpCode.Ldelem_i4 or ILOpCode.Ldelem_i8 or
                 ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8 or ILOpCode.Ldelem_ref or
                 ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_u4:
            {
                // The token form (ldelem !T in generic code) carries the element
                // type; resolving it gives the result a real stack kind — Unknown
                // would read as void and the load would never join the stack.
                string? elementType = null;
                if (opcode == ILOpCode.Ldelem)
                {
                    int token = reader.ReadILToken();
                    elementType = ResolveTypeName(context.Reader, token, context.GenericContext);
                }
                var index = TryPop(stack);
                var array = TryPop(stack);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = elementType, Arguments = { array, index },
                    ResultType = opcode == ILOpCode.Ldelem
                        ? StackValue.FromTypeName(elementType ?? "object")
                        : StackValue.CreatePrimitive(opcode == ILOpCode.Ldelem_ref ? StackValueKind.ObjRef : opcode.ResultKind()),
                    Offset = offset
                };
            }

            // Array element address
            case ILOpCode.Ldelema:
            {
                reader.ReadILToken(); // type token
                var index = TryPop(stack);
                var array = TryPop(stack);
                return new ILAstExpression
                {
                    OpCode = opcode, Arguments = { array, index },
                    ResultType = StackValue.CreatePrimitive(StackValueKind.ByRef),
                    Offset = offset
                };
            }

            // Array element store: pop array, pop index, pop value
            case ILOpCode.Stelem or ILOpCode.Stelem_i or ILOpCode.Stelem_i1 or
                 ILOpCode.Stelem_i2 or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8 or
                 ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8 or ILOpCode.Stelem_ref:
            {
                if (opcode == ILOpCode.Stelem)
                    reader.ReadILToken(); // type token
                var value = TryPop(stack);
                var index = TryPop(stack);
                var array = TryPop(stack);
                return new ILAstExpression
                {
                    OpCode = opcode, Arguments = { array, index, value },
                    ResultType = StackValue.CreateUnknown(),
                    Offset = offset
                };
            }

            // Indirect loads/stores, etc. — generic handling
            default:
                return HandleGeneric(context, ref reader, opcode, offset, stack);
        }
    }

    static ILAstExpression HandleGeneric(
        MethodBodyContext context, ref ILReaderLite reader,
        ILOpCode opcode, int offset, Stack<ILAstExpression> stack)
    {
        // Determine pop/push from the opcode category
        var resultKind = opcode.ResultKind();
        int popCount = EstimatePopCount(opcode);
        bool pushes = resultKind != StackValueKind.Unknown || IsPushOpcode(opcode);

        var expr = new ILAstExpression
        {
            OpCode = opcode,
            ResultType = pushes ? StackValue.CreatePrimitive(resultKind) : StackValue.CreateUnknown(),
            Offset = offset
        };

        for (int i = 0; i < popCount && stack.Count > 0; i++)
            expr.Arguments.Add(TryPop(stack));

        if (!reader.TrySkip(opcode))
        { /* already at end */ }

        return expr;
    }

    static ILAstExpression BuildCall(
        MetadataReader reader, int token, ILOpCode opcode,
        int offset, Stack<ILAstExpression> stack, GenericContext? callerGenericContext = null)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            int paramCount;
            bool isStatic;
            string returnType;
            string? methodName;
            ImmutableArray<string> parameterTypes = default;
            ImmutableArray<CallArgumentModifier> parameterModifiers = default;

            switch (handle.Kind)
            {
                case HandleKind.MethodDefinition:
                {
                    var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                    var sig = methodDef.DecodeSignature(SignatureDecoder.Instance, callerGenericContext);
                    paramCount = sig.ParameterTypes.Length;
                    parameterTypes = sig.ParameterTypes;
                    parameterModifiers = GetParameterModifiers(reader, methodDef, parameterTypes);
                    isStatic = methodDef.Attributes.HasFlag(MethodAttributes.Static);
                    returnType = sig.ReturnType;
                    var declType = reader.GetTypeDefinition(methodDef.GetDeclaringType());
                    methodName = $"{reader.GetFullTypeName(declType)}::{reader.GetString(methodDef.Name)}";
                    break;
                }

                case HandleKind.MemberReference:
                {
                    var memberRef = reader.GetMemberReference((MemberReferenceHandle)handle);
                    var genericCtx = StackSimulator.BuildGenericContextForMemberRef(reader, memberRef, callerGenericContext);
                    // Merge caller's method params for !!N resolution in TypeSpec parents
                    if (callerGenericContext?.MethodParameters.Count > 0 && genericCtx is not null
                        && genericCtx.MethodParameters.Count == 0)
                    {
                        genericCtx = new GenericContext(genericCtx.TypeParameters, callerGenericContext.MethodParameters);
                    }
                    else if (callerGenericContext?.MethodParameters.Count > 0 && genericCtx is null)
                    {
                        genericCtx = callerGenericContext;
                    }
                    var sig = memberRef.DecodeMethodSignature(SignatureDecoder.Instance, genericCtx);
                    paramCount = sig.ParameterTypes.Length;
                    parameterTypes = sig.ParameterTypes;
                    parameterModifiers = GetDefaultParameterModifiers(parameterTypes);
                    isStatic = !sig.Header.IsInstance;
                    returnType = sig.ReturnType;
                    // The NAME's parent TypeSpec decodes in the caller's context
                    // (its !N are the caller's type params); the SIGNATURE above
                    // decodes in the instantiation context built from it.
                    methodName = ResolveMethodRefName(reader, memberRef, callerGenericContext);
                    break;
                }

                case HandleKind.MethodSpecification:
                {
                    var spec = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                    return BuildCall(reader, MetadataTokens.GetToken(spec.Method), opcode, offset, stack, callerGenericContext);
                }

                default:
                    return new ILAstExpression
                    {
                        OpCode = opcode, Operand = $"token:0x{token:X8}",
                        ResultType = StackValue.CreateUnknown(), Offset = offset
                    };
            }

            // Pop arguments in reverse order
            int totalPop = paramCount;
            if (opcode != ILOpCode.Newobj && !isStatic)
                totalPop++;

            var args = new ILAstExpression[totalPop];
            for (int i = totalPop - 1; i >= 0; i--)
                args[i] = TryPop(stack);

            // Annotate arguments with expected parameter types (for enum resolution)
            if (!parameterTypes.IsDefault)
            {
                int paramStart = (!isStatic && opcode != ILOpCode.Newobj) ? 1 : 0;
                for (int i = 0; i < parameterTypes.Length && (i + paramStart) < args.Length; i++)
                {
                    args[i + paramStart].ExpectedType = parameterTypes[i];
                    if (!parameterModifiers.IsDefault && i < parameterModifiers.Length)
                        args[i + paramStart].ExpectedArgumentModifier = parameterModifiers[i];
                }
            }

            // Compute result type
            StackValue resultType;
            if (opcode == ILOpCode.Newobj)
                resultType = StackValue.CreateObjRef(methodName?.Split("::")[0]);
            else if (returnType is "System.Void" or "void")
                resultType = StackValue.CreateUnknown();
            else
                resultType = StackValue.FromTypeName(returnType);

            var expr = new ILAstExpression
            {
                OpCode = opcode, Operand = methodName,
                ResultType = resultType, Offset = offset,
                IsStaticCall = isStatic,
                Member = methodName is null
                    ? null
                    : MemberRefInfo.FromQualifiedName(methodName, returnType, parameterTypes, isStatic)
            };
            expr.Arguments.AddRange(args);

            return expr;
        }
        catch
        {
            return new ILAstExpression
            {
                OpCode = opcode, Operand = $"token:0x{token:X8}",
                ResultType = StackValue.CreateUnknown(), Offset = offset
            };
        }
    }

    static ImmutableArray<CallArgumentModifier> GetParameterModifiers(
        MetadataReader reader,
        MethodDefinition methodDef,
        ImmutableArray<string> parameterTypes)
    {
        var modifiers = GetDefaultParameterModifiers(parameterTypes).ToBuilder();

        foreach (var parameterHandle in methodDef.GetParameters())
        {
            var parameter = reader.GetParameter(parameterHandle);
            if (parameter.SequenceNumber == 0)
                continue;

            var index = parameter.SequenceNumber - 1;
            if (index < 0 || index >= modifiers.Count)
                continue;

            if (AttributeReader.HasAttribute(
                    reader,
                    parameter.GetCustomAttributes(),
                    "System.Runtime.CompilerServices.IsReadOnlyAttribute"))
            {
                modifiers[index] = CallArgumentModifier.In;
            }
            else if ((parameter.Attributes & ParameterAttributes.Out) != 0)
            {
                modifiers[index] = CallArgumentModifier.Out;
            }
        }

        return modifiers.ToImmutable();
    }

    static ImmutableArray<CallArgumentModifier> GetDefaultParameterModifiers(ImmutableArray<string> parameterTypes)
    {
        if (parameterTypes.IsDefault)
            return default;

        var modifiers = ImmutableArray.CreateBuilder<CallArgumentModifier>(parameterTypes.Length);
        foreach (var parameterType in parameterTypes)
        {
            modifiers.Add(parameterType.StartsWith("ref ", StringComparison.Ordinal)
                ? CallArgumentModifier.Ref
                : CallArgumentModifier.None);
        }

        return modifiers.ToImmutable();
    }

    static bool PushesValue(ILOpCode opcode, MethodBodyContext context, ILAstExpression node)
    {
        // Void results don't push
        if (node.ResultType.Kind == StackValueKind.Unknown)
            return false;

        // Stores, branches, ret, throw don't push
        if (opcode is ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or
            ILOpCode.Stloc_3 or ILOpCode.Stloc_s or ILOpCode.Stloc or
            ILOpCode.Starg_s or ILOpCode.Starg or
            ILOpCode.Stfld or ILOpCode.Stsfld or
            ILOpCode.Stind_i or ILOpCode.Stind_i1 or ILOpCode.Stind_i2 or
            ILOpCode.Stind_i4 or ILOpCode.Stind_i8 or
            ILOpCode.Stind_r4 or ILOpCode.Stind_r8 or ILOpCode.Stind_ref or
            ILOpCode.Stobj or ILOpCode.Stelem or
            ILOpCode.Stelem_i or ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2 or
            ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8 or
            ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8 or ILOpCode.Stelem_ref or
            ILOpCode.Ret or ILOpCode.Throw or ILOpCode.Rethrow or
            ILOpCode.Pop or ILOpCode.Initobj or ILOpCode.Cpobj or
            ILOpCode.Cpblk or ILOpCode.Initblk or
            ILOpCode.Endfinally or ILOpCode.Endfilter or
            ILOpCode.Nop or ILOpCode.Break or ILOpCode.Jmp)
            return false;

        // Branches don't push
        if (opcode.IsBranch())
            return false;

        if (opcode == ILOpCode.Switch)
            return false;

        return true;
    }

    static bool IsConversion(ILOpCode opcode) => opcode switch
    {
        ILOpCode.Conv_i1 or ILOpCode.Conv_i2 or ILOpCode.Conv_i4 or ILOpCode.Conv_i8 or
        ILOpCode.Conv_u1 or ILOpCode.Conv_u2 or ILOpCode.Conv_u4 or ILOpCode.Conv_u8 or
        ILOpCode.Conv_i or ILOpCode.Conv_u or
        ILOpCode.Conv_r4 or ILOpCode.Conv_r8 or ILOpCode.Conv_r_un or
        ILOpCode.Conv_ovf_i1 or ILOpCode.Conv_ovf_i2 or ILOpCode.Conv_ovf_i4 or ILOpCode.Conv_ovf_i8 or
        ILOpCode.Conv_ovf_u1 or ILOpCode.Conv_ovf_u2 or ILOpCode.Conv_ovf_u4 or ILOpCode.Conv_ovf_u8 or
        ILOpCode.Conv_ovf_i or ILOpCode.Conv_ovf_u or
        ILOpCode.Conv_ovf_i1_un or ILOpCode.Conv_ovf_i2_un or ILOpCode.Conv_ovf_i4_un or ILOpCode.Conv_ovf_i8_un or
        ILOpCode.Conv_ovf_u1_un or ILOpCode.Conv_ovf_u2_un or ILOpCode.Conv_ovf_u4_un or ILOpCode.Conv_ovf_u8_un or
        ILOpCode.Conv_ovf_i_un or ILOpCode.Conv_ovf_u_un
            => true,
        _ => false
    };

    static int EstimatePopCount(ILOpCode opcode) => opcode switch
    {
        ILOpCode.Ldind_i1 or ILOpCode.Ldind_i2 or ILOpCode.Ldind_i4 or ILOpCode.Ldind_i8 or
        ILOpCode.Ldind_u1 or ILOpCode.Ldind_u2 or ILOpCode.Ldind_u4 or
        ILOpCode.Ldind_r4 or ILOpCode.Ldind_r8 or ILOpCode.Ldind_i or ILOpCode.Ldind_ref or
        ILOpCode.Unbox or ILOpCode.Unbox_any or ILOpCode.Ldobj or
        ILOpCode.Ldlen or ILOpCode.Refanytype or ILOpCode.Refanyval or
        ILOpCode.Ldflda or ILOpCode.Ldvirtftn or ILOpCode.Mkrefany or ILOpCode.Ckfinite
            => 1,
        ILOpCode.Stind_i or ILOpCode.Stind_i1 or ILOpCode.Stind_i2 or ILOpCode.Stind_i4 or
        ILOpCode.Stind_i8 or ILOpCode.Stind_r4 or ILOpCode.Stind_r8 or ILOpCode.Stind_ref or
        ILOpCode.Stobj or ILOpCode.Cpobj or
        ILOpCode.Ldelem_i1 or ILOpCode.Ldelem_i2 or ILOpCode.Ldelem_i4 or ILOpCode.Ldelem_i8 or
        ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_u4 or
        ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8 or ILOpCode.Ldelem_i or ILOpCode.Ldelem_ref or
        ILOpCode.Ldelem or ILOpCode.Ldelema
            => 2,
        ILOpCode.Stelem_i or ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2 or ILOpCode.Stelem_i4 or
        ILOpCode.Stelem_i8 or ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8 or ILOpCode.Stelem_ref or
        ILOpCode.Stelem or ILOpCode.Cpblk or ILOpCode.Initblk
            => 3,
        _ => 0
    };

    static bool IsPushOpcode(ILOpCode opcode) => opcode switch
    {
        ILOpCode.Ldind_i1 or ILOpCode.Ldind_i2 or ILOpCode.Ldind_i4 or ILOpCode.Ldind_i8 or
        ILOpCode.Ldind_u1 or ILOpCode.Ldind_u2 or ILOpCode.Ldind_u4 or
        ILOpCode.Ldind_r4 or ILOpCode.Ldind_r8 or ILOpCode.Ldind_i or ILOpCode.Ldind_ref or
        ILOpCode.Ldobj or ILOpCode.Ldelem or
        ILOpCode.Ldelem_i1 or ILOpCode.Ldelem_i2 or ILOpCode.Ldelem_i4 or ILOpCode.Ldelem_i8 or
        ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_u4 or
        ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8 or ILOpCode.Ldelem_i or ILOpCode.Ldelem_ref or
        ILOpCode.Ldelema or ILOpCode.Unbox or ILOpCode.Unbox_any or ILOpCode.Ldflda or
        ILOpCode.Ckfinite or ILOpCode.Refanytype or ILOpCode.Refanyval or
        ILOpCode.Mkrefany or ILOpCode.Ldvirtftn or ILOpCode.Ldftn or ILOpCode.Localloc or
        ILOpCode.Arglist or ILOpCode.Ldsflda
            => true,
        _ => false
    };

    // --- Resolution helpers ---

    static ILAstExpression MakeLoad(ILOpCode opcode, string operand, StackValue resultType, int offset) =>
        new() { OpCode = opcode, Operand = operand, ResultType = resultType, Offset = offset };

    static ILAstExpression MakeLiteral(ILOpCode opcode, string value, StackValueKind kind, int offset) =>
        new() { OpCode = opcode, Operand = value, ResultType = StackValue.CreatePrimitive(kind), Offset = offset };

    static ILAstExpression TryPop(Stack<ILAstExpression> stack) =>
        stack.Count > 0
            ? stack.Pop()
            : new ILAstExpression
              {
                  OpCode = ILOpCode.Nop, Operand = "??",
                  ResultType = StackValue.CreateUnknown()
              };

    static StackValue ResolveArgType(MethodBodyContext context, int index)
    {
        if (context.HasThis)
        {
            if (index == 0) return StackValue.CreateObjRef();
            index--;
        }
        if (index < context.ParameterTypes.Count)
            return StackValue.FromTypeName(context.ParameterTypes[index]);
        return StackValue.CreateUnknown();
    }

    static StackValue ResolveLocalType(MethodBodyContext context, int index) =>
        index < context.LocalTypes.Count
            ? StackValue.FromTypeName(context.LocalTypes[index])
            : StackValue.CreateUnknown();

    static string ResolveLocalName(MethodBodyContext context, int index) =>
        index < context.LocalNames.Count && context.LocalNames[index] is { } name
            ? name
            : $"V_{index}";

    static (string Name, StackValue Type) ResolveField(MetadataReader reader, int token)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind == HandleKind.FieldDefinition)
            {
                var fieldDef = reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                string typeName = fieldDef.DecodeSignature(SignatureDecoder.Instance, genericContext: null);
                var declType = reader.GetTypeDefinition(fieldDef.GetDeclaringType());
                string name = $"{reader.GetFullTypeName(declType)}::{reader.GetString(fieldDef.Name)}";
                return (name, StackValue.FromTypeName(typeName));
            }
            if (handle.Kind == HandleKind.MemberReference)
            {
                var memberRef = reader.GetMemberReference((MemberReferenceHandle)handle);
                string typeName = memberRef.DecodeFieldSignature(SignatureDecoder.Instance, genericContext: null);
                return (reader.GetString(memberRef.Name), StackValue.FromTypeName(typeName));
            }
        }
        // Fallback to raw token when metadata is malformed or token is unresolvable
        catch { }
        return ($"field:0x{token:X8}", StackValue.CreateUnknown());
    }

    static string? ResolveTypeName(MetadataReader reader, int token, GenericContext? genericContext = null)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind == HandleKind.TypeDefinition)
                return reader.GetFullTypeName(reader.GetTypeDefinition((TypeDefinitionHandle)handle));
            if (handle.Kind == HandleKind.TypeReference)
            {
                var typeRef = reader.GetTypeReference((TypeReferenceHandle)handle);
                string ns = reader.GetString(typeRef.Namespace);
                string name = reader.GetString(typeRef.Name);
                return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            }
            if (handle.Kind == HandleKind.TypeSpecification)
                return reader.GetTypeSpecification((TypeSpecificationHandle)handle)
                    .DecodeSignature(SignatureDecoder.Instance, genericContext);
        }
        // Fallback when metadata token cannot be decoded (malformed IL or cross-assembly ref)
        catch { }
        return null;
    }

    static string? ResolveString(MetadataReader reader, int token)
    {
        try { return reader.GetUserString(MetadataTokens.UserStringHandle(token)); }
        // Fallback when string token is invalid
        catch { return null; }
    }

    static string? ResolveTokenName(MetadataReader reader, int token, GenericContext? genericContext = null)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind == HandleKind.TypeDefinition)
                return reader.GetFullTypeName(reader.GetTypeDefinition((TypeDefinitionHandle)handle));
            if (handle.Kind == HandleKind.TypeReference)
            {
                var typeRef = reader.GetTypeReference((TypeReferenceHandle)handle);
                string ns = reader.GetString(typeRef.Namespace);
                string name = reader.GetString(typeRef.Name);
                return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            }
            if (handle.Kind == HandleKind.TypeSpecification)
                return reader.GetTypeSpecification((TypeSpecificationHandle)handle)
                    .DecodeSignature(SignatureDecoder.Instance, genericContext);
            if (handle.Kind == HandleKind.FieldDefinition)
            {
                var fieldDef = reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                string fieldName = reader.GetString(fieldDef.Name);
                var declType = fieldDef.GetDeclaringType();
                if (!declType.IsNil)
                {
                    var typeDef = reader.GetTypeDefinition(declType);
                    return $"{reader.GetFullTypeName(typeDef)}::{fieldName}";
                }
                return fieldName;
            }
            if (handle.Kind == HandleKind.MethodDefinition)
            {
                var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                string methodNameStr = reader.GetString(methodDef.Name);
                var declType = methodDef.GetDeclaringType();
                if (!declType.IsNil)
                {
                    var typeDef = reader.GetTypeDefinition(declType);
                    return $"{reader.GetFullTypeName(typeDef)}::{methodNameStr}";
                }
                return methodNameStr;
            }
            if (handle.Kind == HandleKind.MemberReference)
            {
                var memberRef = reader.GetMemberReference((MemberReferenceHandle)handle);
                return ResolveMethodRefName(reader, memberRef, genericContext) ?? reader.GetString(memberRef.Name);
            }
        }
        // Fallback to raw token when metadata is malformed or token is unresolvable
        catch { }
        return $"token:0x{token:X8}";
    }

    static string? ResolveMethodRefName(MetadataReader reader, MemberReference memberRef, GenericContext? genericContext = null)
    {
        string name = reader.GetString(memberRef.Name);
        var parent = memberRef.Parent;
        try
        {
            if (parent.Kind == HandleKind.TypeReference)
            {
                var typeRef = reader.GetTypeReference((TypeReferenceHandle)parent);
                string ns = reader.GetString(typeRef.Namespace);
                string tname = reader.GetString(typeRef.Name);
                string fullName = string.IsNullOrEmpty(ns) ? tname : $"{ns}.{tname}";
                return $"{fullName}::{name}";
            }
            if (parent.Kind == HandleKind.TypeDefinition)
            {
                var typeDef = reader.GetTypeDefinition((TypeDefinitionHandle)parent);
                return $"{reader.GetFullTypeName(typeDef)}::{name}";
            }
            if (parent.Kind == HandleKind.TypeSpecification)
            {
                var typeSpec = reader.GetTypeSpecification((TypeSpecificationHandle)parent);
                string typeName = typeSpec.DecodeSignature(SignatureDecoder.Instance, genericContext);
                return $"{typeName}::{name}";
            }
        }
        // Fallback when parent type cannot be resolved
        catch { }
        return name;
    }

    static string? ResolveMethodRefNameFromToken(MetadataReader reader, int token, GenericContext? genericContext = null)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.MethodDefinition => FormatMethodDefName(reader, (MethodDefinitionHandle)handle),
                HandleKind.MemberReference => ResolveMethodRefName(reader,
                    reader.GetMemberReference((MemberReferenceHandle)handle), genericContext),
                HandleKind.MethodSpecification => ResolveMethodRefNameFromToken(reader,
                    MetadataTokens.GetToken(reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method),
                    genericContext),
                _ => $"token:0x{token:X8}"
            };
        }
        // Fallback to raw token when metadata is malformed or token is unresolvable
        catch { return $"token:0x{token:X8}"; }
    }

    static string FormatMethodDefName(MetadataReader reader, MethodDefinitionHandle handle)
    {
        var method = reader.GetMethodDefinition(handle);
        string name = reader.GetString(method.Name);
        var declType = method.GetDeclaringType();
        if (!declType.IsNil)
        {
            var typeDef = reader.GetTypeDefinition(declType);
            return $"{reader.GetFullTypeName(typeDef)}::{name}";
        }
        return name;
    }
}
