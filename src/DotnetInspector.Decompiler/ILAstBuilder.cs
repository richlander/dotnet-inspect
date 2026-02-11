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
        var reader = new ILReaderLite(ilBytes);
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
                // If there are unconsumed stack values, flush them as assignments
                block.Nodes.Add(new ILAstStatement { Expression = node, Offset = offset });
            }
        }
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
                return MakeLoad(opcode, $"V_{index}", ResolveLocalType(context, index), offset);
            }

            case ILOpCode.Ldloc_s:
            {
                int index = reader.ReadILByte();
                return MakeLoad(opcode, $"V_{index}", ResolveLocalType(context, index), offset);
            }

            case ILOpCode.Ldloc:
            {
                int index = reader.ReadILUInt16();
                return MakeLoad(opcode, $"V_{index}", ResolveLocalType(context, index), offset);
            }

            // Address-of
            case ILOpCode.Ldloca_s:
            {
                int index = reader.ReadILByte();
                return MakeLoad(opcode, $"V_{index}", StackValue.CreateByRef(), offset);
            }

            case ILOpCode.Ldloca:
            {
                int index = reader.ReadILUInt16();
                return MakeLoad(opcode, $"V_{index}", StackValue.CreateByRef(), offset);
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
                return MakeLiteral(opcode, val.ToString("G9"), StackValueKind.Float, offset);
            }

            case ILOpCode.Ldc_r8:
            {
                double val = BitConverter.Int64BitsToDouble((long)reader.ReadILUInt64());
                return MakeLiteral(opcode, val.ToString("G17"), StackValueKind.Float, offset);
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
                    OpCode = opcode, Operand = $"V_{index}",
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
                    OpCode = opcode, Operand = $"V_{index}",
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
                    OpCode = opcode, Operand = $"V_{index}",
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
                return BuildCall(context.Reader, token, opcode, offset, stack);
            }

            // Field access
            case ILOpCode.Ldfld:
            {
                int token = reader.ReadILToken();
                var obj = TryPop(stack);
                var (fieldName, fieldType) = ResolveField(context.Reader, token);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = fieldName,
                    Arguments = { obj }, ResultType = fieldType,
                    Offset = offset
                };
            }

            case ILOpCode.Ldsfld:
            {
                int token = reader.ReadILToken();
                var (fieldName, fieldType) = ResolveField(context.Reader, token);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = fieldName,
                    ResultType = fieldType, Offset = offset
                };
            }

            case ILOpCode.Stfld:
            {
                int token = reader.ReadILToken();
                var val = TryPop(stack);
                var obj = TryPop(stack);
                var (fieldName, _) = ResolveField(context.Reader, token);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = fieldName,
                    Arguments = { obj, val }, ResultType = StackValue.CreateUnknown(),
                    Offset = offset
                };
            }

            case ILOpCode.Stsfld:
            {
                int token = reader.ReadILToken();
                var val = TryPop(stack);
                var (fieldName, _) = ResolveField(context.Reader, token);
                return new ILAstExpression
                {
                    OpCode = opcode, Operand = fieldName,
                    Arguments = { val }, ResultType = StackValue.CreateUnknown(),
                    Offset = offset
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
                if (opcode == ILOpCode.Ldelem)
                    reader.ReadILToken(); // type token, unused for now
                var index = TryPop(stack);
                var array = TryPop(stack);
                return new ILAstExpression
                {
                    OpCode = opcode, Arguments = { array, index },
                    ResultType = StackValue.CreatePrimitive(opcode == ILOpCode.Ldelem_ref ? StackValueKind.ObjRef : opcode.ResultKind()),
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
        int offset, Stack<ILAstExpression> stack)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            int paramCount;
            bool isStatic;
            string returnType;
            string? methodName;

            switch (handle.Kind)
            {
                case HandleKind.MethodDefinition:
                {
                    var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                    var sig = methodDef.DecodeSignature(SignatureDecoder.Instance, genericContext: null);
                    paramCount = sig.ParameterTypes.Length;
                    isStatic = methodDef.Attributes.HasFlag(MethodAttributes.Static);
                    returnType = sig.ReturnType;
                    var declType = reader.GetTypeDefinition(methodDef.GetDeclaringType());
                    methodName = $"{reader.GetFullTypeName(declType)}::{reader.GetString(methodDef.Name)}";
                    break;
                }

                case HandleKind.MemberReference:
                {
                    var memberRef = reader.GetMemberReference((MemberReferenceHandle)handle);
                    var genericCtx = StackSimulator.BuildGenericContextForMemberRef(reader, memberRef);
                    var sig = memberRef.DecodeMethodSignature(SignatureDecoder.Instance, genericCtx);
                    paramCount = sig.ParameterTypes.Length;
                    isStatic = !sig.Header.IsInstance;
                    returnType = sig.ReturnType;
                    methodName = ResolveMethodRefName(reader, memberRef, genericCtx);
                    break;
                }

                case HandleKind.MethodSpecification:
                {
                    var spec = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                    return BuildCall(reader, MetadataTokens.GetToken(spec.Method), opcode, offset, stack);
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
                IsStaticCall = isStatic
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
        catch { }
        return null;
    }

    static string? ResolveString(MetadataReader reader, int token)
    {
        try { return reader.GetUserString(MetadataTokens.UserStringHandle(token)); }
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
                return reader.GetString(fieldDef.Name);
            }
            if (handle.Kind == HandleKind.MethodDefinition)
            {
                var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                return reader.GetString(methodDef.Name);
            }
            if (handle.Kind == HandleKind.MemberReference)
            {
                var memberRef = reader.GetMemberReference((MemberReferenceHandle)handle);
                return reader.GetString(memberRef.Name);
            }
        }
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
        catch { }
        return name;
    }
}
