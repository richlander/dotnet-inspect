using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace DotnetInspector.Decompiler.Pipeline;

/// <summary>
/// Builds the typed IR for one method while its <see cref="MetadataSource"/>
/// is alive; the resulting <see cref="IrFunction"/> is fully materialized.
/// Slice scope: branching bodies whose evaluation stack is empty at every
/// block boundary (no exception regions, no stack-carrying edges). IL
/// outside the slice becomes an explicit <see cref="UnsupportedNode"/> with
/// a <see cref="DiagnosticIds.UnsupportedConstruct"/> diagnostic and import
/// stops — fidelity degrades honestly, output never guesses.
/// </summary>
public static class IrImporter
{
    public static IrFunction? Import(MetadataSource source, string typeFullName, string methodName, int overloadIndex = 0)
    {
        var reader = source.Reader;
        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            string ns = reader.GetString(typeDef.Namespace);
            string name = reader.GetString(typeDef.Name);
            if ((ns.Length == 0 ? name : $"{ns}.{name}") != typeFullName)
                continue;

            // Overload indices count every name match, body or not (parity
            // with legacy selection).
            int seen = 0;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != methodName)
                    continue;
                if (seen++ != overloadIndex)
                    continue;
                if (method.RelativeVirtualAddress == 0)
                    return null;

                var imported = MethodImporter.Import(source, typeDefHandle, methodHandle);
                return Build(source, imported, CallerScope(reader, typeDef, method));
            }
            return null;
        }
        return null;
    }

    /// <summary>
    /// Imports every method body in the assembly — the sweep front door the
    /// differential harness registers as the replacement pipeline.
    /// </summary>
    public static IEnumerable<(string TypeName, string MethodName, IrFunction Function)> ImportAssembly(MetadataSource source)
    {
        var reader = source.Reader;
        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            string ns = reader.GetString(typeDef.Namespace);
            string name = reader.GetString(typeDef.Name);
            string typeName = ns.Length == 0 ? name : $"{ns}.{name}";
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (method.RelativeVirtualAddress == 0)
                    continue;
                string memberName = reader.GetString(method.Name);
                IrFunction function;
                try
                {
                    // Metadata import and IR build are both inside the guard:
                    // an importer crash is a pipeline bug, but one method's bug
                    // must not end an assembly sweep — it surfaces as a
                    // diagnosed partial function, like an out-of-slice stop.
                    function = Build(
                        source,
                        MethodImporter.Import(source, typeDefHandle, methodHandle),
                        CallerScope(reader, typeDef, method));
                }
                catch (Exception ex)
                {
                    function = CrashFunction(memberName, typeName, ex);
                }
                yield return (typeName, memberName, function);
            }
        }
    }

    static IrFunction CrashFunction(string methodName, string typeName, Exception ex)
    {
        var block = new Block(0);
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(TypeRef.Unsupported("import failed"), [], false, 0);
        var function = new IrFunction(methodName, TypeRef.Definition("", "", typeName), signature, [], container);
        block.Add(new ExpressionStatement(new UnsupportedNode(0, "(importer crash)", $"{ex.GetType().Name}: {ex.Message}")));
        function.Diagnostics.Add(new DecompilerDiagnostic(
            DiagnosticIds.InternalError, $"importer crash: {ex.GetType().Name}: {ex.Message}"));
        return function;
    }

    static GenericScope CallerScope(MetadataReader reader, TypeDefinition typeDef, MethodDefinition method)
        => new(GenericParameterNames(reader, typeDef.GetGenericParameters()),
               GenericParameterNames(reader, method.GetGenericParameters()));

    static IrFunction Build(MetadataSource source, ImportedMethod method, GenericScope callerScope)
    {
        var container = new BlockContainer();
        var function = new IrFunction(method.Name, method.DeclaringType, method.Signature, method.Body.Locals, container);

        if (!method.Body.Handlers.IsEmpty)
        {
            var block = new Block(0);
            container.Add(block);
            Stop(function, block, new Stack<IrExpression>(), 0, "(exception regions)", "exception regions are outside the slice");
            return function;
        }

        var span = method.Body.IL.AsSpan();
        var leaders = FindLeaders(span);
        var state = new BuildState();

        foreach (int leader in leaders)
        {
            var block = new Block(leader);
            container.Add(block);
            if (!BuildBlock(source, method, function, block, span, leader, NextLeader(leaders, leader, span.Length), callerScope, state))
                return function;  // honest stop already recorded
        }

        function.CheckInvariant();
        return function;
    }

    /// <summary>Block leaders: entry, branch targets, and instructions following a branch.</summary>
    static SortedSet<int> FindLeaders(ReadOnlySpan<byte> il)
    {
        var leaders = new SortedSet<int> { 0 };
        var reader = new ILReaderLite(il);
        while (reader.HasNext)
        {
            var opcode = reader.ReadILOpcode();
            if (IsBranch(opcode))
            {
                leaders.Add(reader.ReadBranchDestination(opcode));
                if (reader.Offset < il.Length)
                    leaders.Add(reader.Offset);
            }
            else
            {
                reader.Skip(opcode);
                if (opcode is ILOpCode.Ret or ILOpCode.Throw && reader.Offset < il.Length)
                    leaders.Add(reader.Offset);
            }
        }
        return leaders;
    }

    static bool IsBranch(ILOpCode opcode)
        => opcode is >= ILOpCode.Br_s and <= ILOpCode.Blt_un_s or >= ILOpCode.Br and <= ILOpCode.Blt_un;

    static int NextLeader(SortedSet<int> leaders, int current, int ilLength)
    {
        foreach (int leader in leaders.GetViewBetween(current + 1, int.MaxValue))
            return leader;
        return ilLength;
    }

    /// <summary>Cross-block import state: stack types at block entries, blocks already built, and the dup slot counter.</summary>
    sealed class BuildState
    {
        public Dictionary<int, List<TypeRef?>> EntryStacks { get; } = [];
        public HashSet<int> Built { get; } = [];
        public int NextDupSlot { get; set; } = StoreStackSlot.DupSlotBase;
    }

    /// <summary>
    /// Spills the leftover evaluation stack into position-indexed slots and
    /// records the entry stack of every target. Position indexing makes all
    /// predecessors of a join store to the same slots. Returns false on a
    /// depth disagreement or a stack-carrying back edge (out of slice).
    /// </summary>
    static bool PropagateAndSpill(IrFunction function, Block body, Stack<IrExpression> stack, BuildState state, int[] targets, int offset)
    {
        var values = stack.Reverse().ToArray();
        stack.Clear();
        var types = values.Select(v => v.ResultType).ToList();
        for (int i = 0; i < values.Length; i++)
            body.Add(new StoreStackSlot(i, values[i]));
        foreach (int target in targets)
        {
            if (state.EntryStacks.TryGetValue(target, out var existing))
            {
                if (existing.Count != types.Count)
                {
                    Stop(function, body, stack, offset, "(join)",
                        "evaluation-stack depth disagrees between paths into a join, outside the slice");
                    return false;
                }
                for (int i = 0; i < types.Count; i++)
                {
                    if (Equals(existing[i], types[i]) || types[i] is null)
                        continue;
                    if (existing[i] is null)
                    {
                        if (!state.Built.Contains(target))
                            existing[i] = types[i];
                        continue;
                    }
                    if (state.Built.Contains(target))
                    {
                        // The join was already built with the first type; a
                        // conflicting later edge cannot be retyped honestly.
                        Stop(function, body, stack, offset, "(join)",
                            "evaluation-stack types disagree between paths into a join, outside the slice");
                        return false;
                    }
                    existing[i] = null;  // unknown — honest, never a guess
                }
            }
            else if (state.Built.Contains(target) && types.Count > 0)
            {
                Stop(function, body, stack, offset, "(back edge)",
                    "stack-carrying back edge, outside the slice");
                return false;
            }
            else
            {
                state.EntryStacks[target] = types;
            }
        }
        return true;
    }

    /// <summary>Builds one block. Returns false when the import stopped honestly inside it.</summary>
    static bool BuildBlock(MetadataSource source, ImportedMethod method, IrFunction function, Block body,
        ReadOnlySpan<byte> il, int start, int end, GenericScope callerScope, BuildState state)
    {
        var stack = new Stack<IrExpression>();
        var reader = new ILReaderLite(il[..end], currentOffset: start);
        int offset = start;

        // Entry values arrive in position-indexed slots stored by predecessors.
        state.Built.Add(start);
        var entry = state.EntryStacks.TryGetValue(start, out var carried) ? carried : [];
        state.EntryStacks[start] = entry;
        for (int i = 0; i < entry.Count; i++)
            stack.Push(new LoadStackSlot(i, entry[i]));

        try
        {
        while (reader.HasNext)
        {
            offset = reader.Offset;
            var opcode = reader.ReadILOpcode();
            switch (opcode)
            {
                case ILOpCode.Nop:
                    break;

                case ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3:
                    stack.Push(MakeLoadArgument(method, opcode - ILOpCode.Ldarg_0));
                    break;
                case ILOpCode.Ldarg_s:
                    stack.Push(MakeLoadArgument(method, reader.ReadILByte()));
                    break;
                case ILOpCode.Ldarg:
                    stack.Push(MakeLoadArgument(method, reader.ReadILUInt16()));
                    break;
                case ILOpCode.Starg_s:
                    body.Add(MakeStoreArgument(method, reader.ReadILByte(), Pop(stack)));
                    break;
                case ILOpCode.Starg:
                    body.Add(MakeStoreArgument(method, reader.ReadILUInt16(), Pop(stack)));
                    break;

                case ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3:
                    stack.Push(MakeLoadLocal(method, opcode - ILOpCode.Ldloc_0));
                    break;
                case ILOpCode.Ldloc_s:
                    stack.Push(MakeLoadLocal(method, reader.ReadILByte()));
                    break;
                case ILOpCode.Ldloc:
                    stack.Push(MakeLoadLocal(method, reader.ReadILUInt16()));
                    break;

                case ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3:
                    body.Add(MakeStoreLocal(method, opcode - ILOpCode.Stloc_0, Pop(stack)));
                    break;
                case ILOpCode.Stloc_s:
                    body.Add(MakeStoreLocal(method, reader.ReadILByte(), Pop(stack)));
                    break;
                case ILOpCode.Stloc:
                    body.Add(MakeStoreLocal(method, reader.ReadILUInt16(), Pop(stack)));
                    break;

                case >= ILOpCode.Ldc_i4_m1 and <= ILOpCode.Ldc_i4_8:
                    stack.Push(new Constant(opcode - ILOpCode.Ldc_i4_0, TypeRef.CoreLib("System", "Int32")));
                    break;
                case ILOpCode.Ldc_i4_s:
                    stack.Push(new Constant((int)(sbyte)reader.ReadILByte(), TypeRef.CoreLib("System", "Int32")));
                    break;
                case ILOpCode.Ldc_i4:
                    stack.Push(new Constant((int)reader.ReadILUInt32(), TypeRef.CoreLib("System", "Int32")));
                    break;
                case ILOpCode.Ldc_i8:
                    stack.Push(new Constant((long)reader.ReadILUInt64(), TypeRef.CoreLib("System", "Int64")));
                    break;
                case ILOpCode.Ldnull:
                    stack.Push(new Constant(null, TypeRef.CoreLib("System", "Object")));
                    break;
                case ILOpCode.Ldstr:
                    stack.Push(new Constant(
                        source.Reader.GetUserString(MetadataTokens.UserStringHandle(reader.ReadILToken())),
                        TypeRef.CoreLib("System", "String")));
                    break;

                case ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Div or ILOpCode.Rem
                    or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor or ILOpCode.Shl or ILOpCode.Shr
                    or ILOpCode.Add_ovf or ILOpCode.Sub_ovf or ILOpCode.Mul_ovf
                    or ILOpCode.Div_un or ILOpCode.Rem_un or ILOpCode.Shr_un
                    or ILOpCode.Add_ovf_un or ILOpCode.Sub_ovf_un or ILOpCode.Mul_ovf_un:
                {
                    var right = Pop(stack);
                    var left = Pop(stack);
                    stack.Push(new Binary(BinaryKindOf(opcode), IsChecked(opcode), IsUnsigned(opcode), left, right));
                    break;
                }

                case ILOpCode.Ceq or ILOpCode.Cgt or ILOpCode.Cgt_un or ILOpCode.Clt or ILOpCode.Clt_un:
                {
                    var right = Pop(stack);
                    var left = Pop(stack);
                    stack.Push(new Comparison(
                        opcode switch
                        {
                            ILOpCode.Ceq => ComparisonKind.Equal,
                            ILOpCode.Cgt or ILOpCode.Cgt_un => ComparisonKind.GreaterThan,
                            _ => ComparisonKind.LessThan,
                        },
                        opcode is ILOpCode.Cgt_un or ILOpCode.Clt_un, left, right));
                    break;
                }

                case ILOpCode.Conv_i1 or ILOpCode.Conv_i2 or ILOpCode.Conv_i4 or ILOpCode.Conv_i8
                    or ILOpCode.Conv_u1 or ILOpCode.Conv_u2 or ILOpCode.Conv_u4 or ILOpCode.Conv_u8
                    or ILOpCode.Conv_r4 or ILOpCode.Conv_r8 or ILOpCode.Conv_i or ILOpCode.Conv_u
                    or ILOpCode.Conv_r_un
                    or ILOpCode.Conv_ovf_i1 or ILOpCode.Conv_ovf_i2 or ILOpCode.Conv_ovf_i4 or ILOpCode.Conv_ovf_i8
                    or ILOpCode.Conv_ovf_u1 or ILOpCode.Conv_ovf_u2 or ILOpCode.Conv_ovf_u4 or ILOpCode.Conv_ovf_u8
                    or ILOpCode.Conv_ovf_i or ILOpCode.Conv_ovf_u
                    or ILOpCode.Conv_ovf_i1_un or ILOpCode.Conv_ovf_i2_un or ILOpCode.Conv_ovf_i4_un or ILOpCode.Conv_ovf_i8_un
                    or ILOpCode.Conv_ovf_u1_un or ILOpCode.Conv_ovf_u2_un or ILOpCode.Conv_ovf_u4_un or ILOpCode.Conv_ovf_u8_un
                    or ILOpCode.Conv_ovf_i_un or ILOpCode.Conv_ovf_u_un:
                    stack.Push(MakeConvert(opcode, Pop(stack)));
                    break;

                case ILOpCode.Call or ILOpCode.Callvirt:
                {
                    var callee = ResolveMethod(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope);
                    if (callee.DeclaringType.Kind == TypeRefKind.Unsupported)
                    {
                        // Unknown arity would mis-pop the stack and corrupt
                        // everything downstream — stop here instead.
                        Stop(function, body, stack, offset, "call", $"unresolvable callee: {callee.DeclaringType.UnsupportedReason}");
                        return false;
                    }
                    int argumentCount = callee.ParameterTypes.Length + (callee.HasThis ? 1 : 0);
                    var arguments = new IrExpression[argumentCount];
                    for (int i = argumentCount - 1; i >= 0; i--)
                        arguments[i] = Pop(stack);
                    var call = new Call(callee, opcode == ILOpCode.Callvirt, arguments);
                    if (callee.ReturnType is { Name: "Void", Namespace: "System" })
                        body.Add(new ExpressionStatement(call));
                    else
                        stack.Push(call);
                    break;
                }

                case ILOpCode.Ldfld or ILOpCode.Ldsfld:
                {
                    var field = ResolveField(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope);
                    stack.Push(new LoadField(field, opcode == ILOpCode.Ldfld ? Pop(stack) : null));
                    break;
                }
                case ILOpCode.Stfld:
                {
                    var field = ResolveField(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope);
                    var value = Pop(stack);
                    body.Add(new StoreField(field, Pop(stack), value));
                    break;
                }
                case ILOpCode.Stsfld:
                {
                    var field = ResolveField(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope);
                    body.Add(new StoreField(field, null, Pop(stack)));
                    break;
                }

                case ILOpCode.Newobj:
                {
                    var constructor = ResolveMethod(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope);
                    var arguments = new IrExpression[constructor.ParameterTypes.Length];
                    for (int i = arguments.Length - 1; i >= 0; i--)
                        arguments[i] = Pop(stack);
                    stack.Push(new NewObject(constructor, arguments));
                    break;
                }

                case ILOpCode.Throw:
                {
                    // A leader follows every throw (FindLeaders), so the block
                    // ends here and unreachable IL lands in its own block. Any
                    // pending stack values were evaluated before the exception
                    // argument; they spill as statements in evaluation order.
                    var thrown = Pop(stack);
                    foreach (var pending in stack.Reverse())
                        body.Add(new ExpressionStatement(pending));
                    stack.Clear();
                    body.Add(new Throw(thrown));
                    break;
                }

                case ILOpCode.Neg:
                    stack.Push(new Unary(UnaryKind.Negate, Pop(stack)));
                    break;
                case ILOpCode.Not:
                    stack.Push(new Unary(UnaryKind.BitwiseNot, Pop(stack)));
                    break;

                case ILOpCode.Dup:
                {
                    // Trees cannot share nodes: dup materializes through a slot.
                    var value = Pop(stack);
                    int slot = state.NextDupSlot++;
                    body.Add(new StoreStackSlot(slot, value));
                    stack.Push(new LoadStackSlot(slot, value.ResultType));
                    stack.Push(new LoadStackSlot(slot, value.ResultType));
                    break;
                }

                case ILOpCode.Ldlen:
                    stack.Push(new ArrayLength(Pop(stack)));
                    break;

                case ILOpCode.Box:
                    stack.Push(new Box(ResolveTypeToken(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope), Pop(stack)));
                    break;

                case ILOpCode.Isinst:
                    stack.Push(new IsInstance(ResolveTypeToken(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope), Pop(stack)));
                    break;

                case ILOpCode.Castclass:
                    stack.Push(new CastClass(ResolveTypeToken(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope), Pop(stack)));
                    break;

                case ILOpCode.Newarr:
                {
                    var elementType = ResolveTypeToken(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope);
                    stack.Push(new NewArray(elementType, Pop(stack)));
                    break;
                }

                case ILOpCode.Ldtoken:
                {
                    var handle = MetadataTokens.EntityHandle(reader.ReadILToken());
                    switch (handle.Kind)
                    {
                        case HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification:
                        {
                            var type = ResolveTypeToken(source.Reader, handle, callerScope);
                            stack.Push(new LoadToken(RuntimeTokenKind.Type, type, type.ToDisplayString()));
                            break;
                        }
                        case HandleKind.MethodDefinition or HandleKind.MethodSpecification:
                        {
                            var callee = ResolveMethod(source.Reader, handle, callerScope);
                            stack.Push(new LoadToken(RuntimeTokenKind.Method, null, $"{callee.DeclaringType.ToDisplayString()}.{callee.Name}"));
                            break;
                        }
                        case HandleKind.FieldDefinition:
                        {
                            var field = ResolveField(source.Reader, handle, callerScope);
                            stack.Push(new LoadToken(RuntimeTokenKind.Field, null, $"{field.DeclaringType.ToDisplayString()}.{field.Name}"));
                            break;
                        }
                        case HandleKind.MemberReference:
                        {
                            var member = source.Reader.GetMemberReference((MemberReferenceHandle)handle);
                            if (member.GetKind() == MemberReferenceKind.Field)
                            {
                                var field = ResolveField(source.Reader, handle, callerScope);
                                stack.Push(new LoadToken(RuntimeTokenKind.Field, null, $"{field.DeclaringType.ToDisplayString()}.{field.Name}"));
                            }
                            else
                            {
                                var callee = ResolveMethod(source.Reader, handle, callerScope);
                                stack.Push(new LoadToken(RuntimeTokenKind.Method, null, $"{callee.DeclaringType.ToDisplayString()}.{callee.Name}"));
                            }
                            break;
                        }
                        default:
                            Stop(function, body, stack, offset, "ldtoken", $"unrepresentable token kind {handle.Kind}");
                            return false;
                    }
                    break;
                }

                case ILOpCode.Pop:
                    body.Add(new ExpressionStatement(Pop(stack)));
                    break;

                case ILOpCode.Br or ILOpCode.Br_s:
                {
                    int target = reader.ReadBranchDestination(opcode);
                    if (!PropagateAndSpill(function, body, stack, state, [target], offset))
                        return false;
                    body.Add(new Branch(target));
                    break;
                }

                case ILOpCode.Brtrue or ILOpCode.Brtrue_s or ILOpCode.Brfalse or ILOpCode.Brfalse_s:
                {
                    var condition = Pop(stack);
                    if (opcode is ILOpCode.Brfalse or ILOpCode.Brfalse_s)
                        condition = new LogicalNot(condition);
                    int target = reader.ReadBranchDestination(opcode);
                    if (!PropagateAndSpill(function, body, stack, state, [target, end], offset))
                        return false;
                    body.Add(new ConditionalBranch(condition, target));
                    break;
                }

                case >= ILOpCode.Beq_s and <= ILOpCode.Blt_un_s or >= ILOpCode.Beq and <= ILOpCode.Blt_un:
                {
                    int target = reader.ReadBranchDestination(opcode);
                    var right = Pop(stack);
                    var left = Pop(stack);
                    var (kind, isUnsigned) = ComparisonOf(opcode);
                    if (!PropagateAndSpill(function, body, stack, state, [target, end], offset))
                        return false;
                    body.Add(new ConditionalBranch(new Comparison(kind, isUnsigned, left, right), target));
                    break;
                }

                case ILOpCode.Ret:
                    body.Add(new Return(stack.Count > 0 ? Pop(stack) : null));
                    break;

                default:
                    Stop(function, body, stack, offset, opcode.ToString().ToLowerInvariant(),
                        "opcode is outside the slice");
                    return false;
            }
        }

        }
        catch (OutOfSliceException ex)
        {
            Stop(function, body, stack, offset, "(stack underflow)", ex.Message);
            return false;
        }

        if (stack.Count > 0)
        {
            if (end >= il.Length)
            {
                Stop(function, body, stack, end, "(method end)",
                    "evaluation stack is not empty at the end of the method");
                return false;
            }
            return PropagateAndSpill(function, body, stack, state, [end], end);
        }
        return true;
    }

    /// <summary>A block whose entry expects stack values is a stack-carrying edge — out of slice, reported honestly via the importer's stop path.</summary>
    sealed class OutOfSliceException(string reason) : Exception(reason);

    static IrExpression Pop(Stack<IrExpression> stack)
        => stack.Count > 0
            ? stack.Pop()
            : throw new OutOfSliceException("evaluation stack carries values into this block, outside the slice");

    /// <summary>Records the honest stopping point: spill the pending stack as statements, append the unsupported marker, attach the diagnostic.</summary>
    static void Stop(IrFunction function, Block body, Stack<IrExpression> stack, int offset, string opcode, string reason)
    {
        foreach (var pending in stack.Reverse())
            body.Add(new ExpressionStatement(pending));
        stack.Clear();
        body.Add(new ExpressionStatement(new UnsupportedNode(offset, opcode, reason)));
        function.Diagnostics.Add(new DecompilerDiagnostic(
            DiagnosticIds.UnsupportedConstruct, $"IL_{offset:X4} {opcode}: {reason}"));
        function.CheckInvariant();
    }

    static (ComparisonKind Kind, bool IsUnsigned) ComparisonOf(ILOpCode opcode) => opcode switch
    {
        ILOpCode.Beq or ILOpCode.Beq_s => (ComparisonKind.Equal, false),
        ILOpCode.Bne_un or ILOpCode.Bne_un_s => (ComparisonKind.NotEqual, false),
        ILOpCode.Bge or ILOpCode.Bge_s => (ComparisonKind.GreaterThanOrEqual, false),
        ILOpCode.Bge_un or ILOpCode.Bge_un_s => (ComparisonKind.GreaterThanOrEqual, true),
        ILOpCode.Bgt or ILOpCode.Bgt_s => (ComparisonKind.GreaterThan, false),
        ILOpCode.Bgt_un or ILOpCode.Bgt_un_s => (ComparisonKind.GreaterThan, true),
        ILOpCode.Ble or ILOpCode.Ble_s => (ComparisonKind.LessThanOrEqual, false),
        ILOpCode.Ble_un or ILOpCode.Ble_un_s => (ComparisonKind.LessThanOrEqual, true),
        ILOpCode.Blt or ILOpCode.Blt_s => (ComparisonKind.LessThan, false),
        _ => (ComparisonKind.LessThan, true),
    };

    static Convert MakeConvert(ILOpCode opcode, IrExpression operand)
    {
        string name = opcode switch
        {
            ILOpCode.Conv_i1 or ILOpCode.Conv_ovf_i1 or ILOpCode.Conv_ovf_i1_un => "SByte",
            ILOpCode.Conv_u1 or ILOpCode.Conv_ovf_u1 or ILOpCode.Conv_ovf_u1_un => "Byte",
            ILOpCode.Conv_i2 or ILOpCode.Conv_ovf_i2 or ILOpCode.Conv_ovf_i2_un => "Int16",
            ILOpCode.Conv_u2 or ILOpCode.Conv_ovf_u2 or ILOpCode.Conv_ovf_u2_un => "UInt16",
            ILOpCode.Conv_i4 or ILOpCode.Conv_ovf_i4 or ILOpCode.Conv_ovf_i4_un => "Int32",
            ILOpCode.Conv_u4 or ILOpCode.Conv_ovf_u4 or ILOpCode.Conv_ovf_u4_un => "UInt32",
            ILOpCode.Conv_i8 or ILOpCode.Conv_ovf_i8 or ILOpCode.Conv_ovf_i8_un => "Int64",
            ILOpCode.Conv_u8 or ILOpCode.Conv_ovf_u8 or ILOpCode.Conv_ovf_u8_un => "UInt64",
            ILOpCode.Conv_r4 => "Single",
            ILOpCode.Conv_r8 or ILOpCode.Conv_r_un => "Double",
            ILOpCode.Conv_i or ILOpCode.Conv_ovf_i or ILOpCode.Conv_ovf_i_un => "IntPtr",
            _ => "UIntPtr",
        };
        bool isChecked = opcode is ILOpCode.Conv_ovf_i1 or ILOpCode.Conv_ovf_i2 or ILOpCode.Conv_ovf_i4 or ILOpCode.Conv_ovf_i8
            or ILOpCode.Conv_ovf_u1 or ILOpCode.Conv_ovf_u2 or ILOpCode.Conv_ovf_u4 or ILOpCode.Conv_ovf_u8
            or ILOpCode.Conv_ovf_i or ILOpCode.Conv_ovf_u
            or ILOpCode.Conv_ovf_i1_un or ILOpCode.Conv_ovf_i2_un or ILOpCode.Conv_ovf_i4_un or ILOpCode.Conv_ovf_i8_un
            or ILOpCode.Conv_ovf_u1_un or ILOpCode.Conv_ovf_u2_un or ILOpCode.Conv_ovf_u4_un or ILOpCode.Conv_ovf_u8_un
            or ILOpCode.Conv_ovf_i_un or ILOpCode.Conv_ovf_u_un;
        bool isUnsigned = opcode is ILOpCode.Conv_r_un
            or ILOpCode.Conv_ovf_i1_un or ILOpCode.Conv_ovf_i2_un or ILOpCode.Conv_ovf_i4_un or ILOpCode.Conv_ovf_i8_un
            or ILOpCode.Conv_ovf_u1_un or ILOpCode.Conv_ovf_u2_un or ILOpCode.Conv_ovf_u4_un or ILOpCode.Conv_ovf_u8_un
            or ILOpCode.Conv_ovf_i_un or ILOpCode.Conv_ovf_u_un;
        return new Convert(TypeRef.CoreLib("System", name), isChecked, isUnsigned, operand);
    }

    static LoadArgument MakeLoadArgument(ImportedMethod method, int index)
    {
        if (method.Signature.HasThis)
        {
            if (index == 0)
                return new LoadArgument(0, "this", method.DeclaringType);
            var p = method.Signature.Parameters[index - 1];
            return new LoadArgument(index, p.Name, p.Type);
        }
        var parameter = method.Signature.Parameters[index];
        return new LoadArgument(index, parameter.Name, parameter.Type);
    }

    static StoreArgument MakeStoreArgument(ImportedMethod method, int index, IrExpression value)
    {
        if (method.Signature.HasThis)
        {
            if (index == 0)
                return new StoreArgument(0, "this", method.DeclaringType, value);
            var p = method.Signature.Parameters[index - 1];
            return new StoreArgument(index, p.Name, p.Type, value);
        }
        var parameter = method.Signature.Parameters[index];
        return new StoreArgument(index, parameter.Name, parameter.Type, value);
    }

    static LoadLocal MakeLoadLocal(ImportedMethod method, int index)
        => new(index, method.Body.Locals[index]);

    static StoreLocal MakeStoreLocal(ImportedMethod method, int index, IrExpression value)
        => new(index, method.Body.Locals[index], value);

    internal static MethodRef ResolveMethod(MetadataReader reader, EntityHandle handle, GenericScope callerScope)
    {
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
            {
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                var declaring = TypeRefDecoder.Instance.GetTypeFromDefinition(reader, method.GetDeclaringType(), 0);
                var typeScope = new GenericScope(GenericParameterNames(reader, reader.GetTypeDefinition(method.GetDeclaringType()).GetGenericParameters()), []);
                var signature = method.DecodeSignature(TypeRefDecoder.Instance, typeScope);
                return new MethodRef(declaring, reader.GetString(method.Name), signature.ReturnType, signature.ParameterTypes, signature.Header.IsInstance);
            }
            case HandleKind.MemberReference:
            {
                var member = reader.GetMemberReference((MemberReferenceHandle)handle);
                var declaring = ResolveParentType(reader, member.Parent, callerScope);
                var signature = member.DecodeMethodSignature(TypeRefDecoder.Instance, GenericScope.Empty);
                // The signature's !N are the declaring type's parameters;
                // instantiate them against the parent's type arguments so a
                // call on List<int> reports int, not T.
                var typeArguments = declaring.Kind == TypeRefKind.GenericInstance ? declaring.TypeArguments : [];
                return new MethodRef(
                    declaring,
                    reader.GetString(member.Name),
                    signature.ReturnType.Instantiate(typeArguments, []),
                    [.. signature.ParameterTypes.Select(p => p.Instantiate(typeArguments, []))],
                    signature.Header.IsInstance);
            }
            case HandleKind.MethodSpecification:
            {
                // A generic method instantiation: resolve the underlying
                // method, then instantiate its !!N against the decoded type
                // arguments so the call site reports concrete types.
                var spec = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                var generic = ResolveMethod(reader, spec.Method, callerScope);
                var methodArguments = spec.DecodeSignature(TypeRefDecoder.Instance, callerScope);
                return generic with
                {
                    TypeArguments = methodArguments,
                    ReturnType = generic.ReturnType.Instantiate([], methodArguments),
                    ParameterTypes = [.. generic.ParameterTypes.Select(p => p.Instantiate([], methodArguments))],
                };
            }
            default:
                return new MethodRef(TypeRef.Unsupported($"callee handle kind {handle.Kind}"), "?", TypeRef.Unsupported("unknown return"), [], false);
        }
    }

    internal static FieldRef ResolveField(MetadataReader reader, EntityHandle handle, GenericScope callerScope)
    {
        switch (handle.Kind)
        {
            case HandleKind.FieldDefinition:
            {
                var field = reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                var declaring = TypeRefDecoder.Instance.GetTypeFromDefinition(reader, field.GetDeclaringType(), 0);
                var typeScope = new GenericScope(GenericParameterNames(reader, reader.GetTypeDefinition(field.GetDeclaringType()).GetGenericParameters()), []);
                return new FieldRef(declaring, reader.GetString(field.Name), field.DecodeSignature(TypeRefDecoder.Instance, typeScope));
            }
            case HandleKind.MemberReference:
            {
                var member = reader.GetMemberReference((MemberReferenceHandle)handle);
                var declaring = ResolveParentType(reader, member.Parent, callerScope);
                var fieldType = member.DecodeFieldSignature(TypeRefDecoder.Instance, GenericScope.Empty);
                if (declaring.Kind == TypeRefKind.GenericInstance)
                    fieldType = fieldType.Instantiate(declaring.TypeArguments, []);
                return new FieldRef(declaring, reader.GetString(member.Name), fieldType);
            }
            default:
                return new FieldRef(TypeRef.Unsupported($"field handle kind {handle.Kind}"), "?", TypeRef.Unsupported("unknown field type"));
        }
    }

    static TypeRef ResolveTypeToken(MetadataReader reader, EntityHandle handle, GenericScope callerScope) => handle.Kind switch
    {
        HandleKind.TypeDefinition => TypeRefDecoder.Instance.GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeReference => TypeRefDecoder.Instance.GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
        HandleKind.TypeSpecification => TypeRefDecoder.Instance.GetTypeFromSpecification(reader, callerScope, (TypeSpecificationHandle)handle, 0),
        _ => TypeRef.Unsupported($"type token kind {handle.Kind}"),
    };

    static TypeRef ResolveParentType(MetadataReader reader, EntityHandle parent, GenericScope callerScope) => parent.Kind switch
    {
        HandleKind.TypeDefinition => TypeRefDecoder.Instance.GetTypeFromDefinition(reader, (TypeDefinitionHandle)parent, 0),
        HandleKind.TypeReference => TypeRefDecoder.Instance.GetTypeFromReference(reader, (TypeReferenceHandle)parent, 0),
        // A MemberRef parent TypeSpec's !N are the CALLER's type parameters.
        HandleKind.TypeSpecification => TypeRefDecoder.Instance.GetTypeFromSpecification(reader, callerScope, (TypeSpecificationHandle)parent, 0),
        _ => TypeRef.Unsupported($"member parent kind {parent.Kind}"),
    };

    static ImmutableArray<string> GenericParameterNames(MetadataReader reader, GenericParameterHandleCollection handles)
    {
        if (handles.Count == 0)
            return [];
        var names = ImmutableArray.CreateBuilder<string>(handles.Count);
        foreach (var handle in handles)
            names.Add(reader.GetString(reader.GetGenericParameter(handle).Name));
        return names.MoveToImmutable();
    }

    static BinaryKind BinaryKindOf(ILOpCode opcode) => opcode switch
    {
        ILOpCode.Add or ILOpCode.Add_ovf or ILOpCode.Add_ovf_un => BinaryKind.Add,
        ILOpCode.Sub or ILOpCode.Sub_ovf or ILOpCode.Sub_ovf_un => BinaryKind.Subtract,
        ILOpCode.Mul or ILOpCode.Mul_ovf or ILOpCode.Mul_ovf_un => BinaryKind.Multiply,
        ILOpCode.Div or ILOpCode.Div_un => BinaryKind.Divide,
        ILOpCode.Rem or ILOpCode.Rem_un => BinaryKind.Remainder,
        ILOpCode.And => BinaryKind.And,
        ILOpCode.Or => BinaryKind.Or,
        ILOpCode.Xor => BinaryKind.Xor,
        ILOpCode.Shl => BinaryKind.ShiftLeft,
        _ => BinaryKind.ShiftRight,
    };

    static bool IsChecked(ILOpCode opcode) => opcode is ILOpCode.Add_ovf or ILOpCode.Add_ovf_un
        or ILOpCode.Sub_ovf or ILOpCode.Sub_ovf_un or ILOpCode.Mul_ovf or ILOpCode.Mul_ovf_un;

    static bool IsUnsigned(ILOpCode opcode) => opcode is ILOpCode.Div_un or ILOpCode.Rem_un
        or ILOpCode.Shr_un or ILOpCode.Add_ovf_un or ILOpCode.Sub_ovf_un or ILOpCode.Mul_ovf_un;
}

/// <summary>Indented tree dump of the IR — the stage projection for this layer (consumed by the harness's --dump as the pipeline grows).</summary>
public static class IrPrinter
{
    public static string Dump(IrFunction function)
    {
        var sb = new System.Text.StringBuilder();
        Append(sb, function, 0);
        foreach (var diagnostic in function.Diagnostics)
            sb.AppendLine($"// {diagnostic}");
        sb.AppendLine($"// fidelity: {function.Fidelity}");
        return sb.ToString();
    }

    static void Append(System.Text.StringBuilder sb, IrNode node, int indent)
    {
        sb.Append(' ', indent * 2).AppendLine(node.Describe());
        foreach (var child in node.Children)
            Append(sb, child, indent + 1);
    }
}
