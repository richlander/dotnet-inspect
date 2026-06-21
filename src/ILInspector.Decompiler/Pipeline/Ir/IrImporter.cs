using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// A per-instruction snapshot of the evaluation stack (each element's result
/// type) immediately after an opcode is imported — the typed-IL projection's
/// data, captured as a byproduct of the importer's single stack simulation.
/// </summary>
internal readonly record struct IlTracePoint(int Offset, ImmutableArray<TypeRef?> StackTypes);

/// <summary>
/// Builds the typed IR for one method while its <see cref="MetadataSource"/>
/// is alive; the resulting <see cref="IrFunction"/> is fully materialized.
/// Slice scope: branching bodies including stack-carrying edges (values
/// crossing block boundaries materialize through stack slots) and exception
/// regions (imported flat; nesting is raising-pass work). Function-pointer
/// IL (ldftn/calli) and localloc remain outside the slice. IL outside
/// the slice becomes an explicit <see cref="UnsupportedNode"/> with
/// a <see cref="DiagnosticIds.UnsupportedConstruct"/> diagnostic and import
/// stops — fidelity degrades honestly, output never guesses.
/// </summary>
/// <summary>
/// One same-name overload as <see cref="IrImporter.Overloads"/> reports it: its
/// <see cref="Index"/> (the <c>overloadIndex</c> to pass back to
/// <see cref="IrImporter.Import"/>), decoded return/parameter types, and whether
/// it has an IL body. <see cref="Describe"/> renders a one-line signature.
/// </summary>
public readonly record struct OverloadInfo(
    int Index, TypeRef ReturnType, ImmutableArray<TypeRef> ParameterTypes,
    bool HasThis, bool HasBody, bool IsPublic)
{
    public string Describe()
        => $"({string.Join(", ", ParameterTypes.Select(p => p.ToDisplayString()))})";
}

public static class IrImporter
{
    /// <param name="publicOnly">
    /// Count only public overloads when resolving <paramref name="overloadIndex"/>,
    /// mirroring the legacy selection the product callers use: the API surface
    /// is public-ordered, so a public-only index must skip interleaved
    /// non-public same-name overloads — otherwise a private overload declared
    /// before a public one would be selected in its place.
    /// </param>
    /// <summary>
    /// Imports a method body addressed by a <see cref="MethodRef"/> — the
    /// reference form a pass already holds (e.g. a delegate creation's target
    /// method). Resolves the declaring type's metadata full name from the ref
    /// (<see cref="TypeRef.Name"/> spells nesting with <c>+</c>; the importer
    /// matches the <c>.</c> form) and forwards to the by-name front door. The
    /// synthesized lambda/local-function methods this serves have unique names,
    /// so overload index 0 is exact.
    /// </summary>
    public static IrFunction? Import(MetadataSource source, MethodRef method)
        => Import(source, ImporterTypeName(method.DeclaringType), method.Name);

    static string ImporterTypeName(TypeRef type)
    {
        string name = type.Name.Replace('+', '.');
        return type.Namespace.Length == 0 ? name : $"{type.Namespace}.{name}";
    }

    public static IrFunction? Import(MetadataSource source, string typeFullName, string methodName, int overloadIndex = 0, bool publicOnly = false)
    {
        // The single-method front door carries the same no-crash guarantee as
        // the assembly sweep (ImportAssembly): the product path calls this
        // directly, so any importer bug OR malformed-metadata read — including
        // the type/method lookup below — must surface as a diagnosed crash
        // function, never a thrown exception that takes down the whole command.
        try
        {
            var reader = source.Reader;
            foreach (var typeDefHandle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(typeDefHandle);
                // Nested-aware: a nested type has a nil namespace and a leaf
                // Name, so the full name must thread its declaring types
                // (Outer.Inner). The product passes such names; a raw ns+name
                // would never match and the body would silently disappear.
                if (reader.GetFullTypeName(typeDef) != typeFullName)
                    continue;

                // Overload indices count name matches at the requested
                // visibility, body or not (parity with legacy selection); a
                // selected overload without a body is null, not silently the
                // next one with a body.
                int seen = 0;
                foreach (var methodHandle in typeDef.GetMethods())
                {
                    var method = reader.GetMethodDefinition(methodHandle);
                    if (reader.GetString(method.Name) != methodName)
                        continue;
                    if (publicOnly && (method.Attributes & System.Reflection.MethodAttributes.MemberAccessMask) != System.Reflection.MethodAttributes.Public)
                        continue;
                    if (seen++ != overloadIndex)
                        continue;
                    if (method.RelativeVirtualAddress == 0)
                        return null;
                    return Build(source, MethodImporter.Import(source, typeDefHandle, methodHandle), CallerScope(reader, typeDef, method));
                }
                return null;
            }
            return null;
        }
        catch (Exception ex)
        {
            return CrashFunction(methodName, typeFullName, ex);
        }
    }

    /// <summary>
    /// Every same-name overload on the type, in metadata order, with its decoded
    /// signature and whether it carries an IL body. The harness uses this to
    /// disambiguate <c>--dump</c> when a name resolves to more than one method —
    /// the index lines up 1:1 with the <c>overloadIndex</c> of <see cref="Import"/>.
    /// Returns an empty list when the type or name is not found.
    /// </summary>
    public static IReadOnlyList<OverloadInfo> Overloads(MetadataSource source, string typeFullName, string methodName)
    {
        var reader = source.Reader;
        var result = new List<OverloadInfo>();
        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            if (reader.GetFullTypeName(typeDef) != typeFullName)
                continue;

            int index = 0;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != methodName)
                    continue;
                var signature = method.DecodeSignature(TypeRefDecoder.Instance, CallerScope(reader, typeDef, method));
                bool isPublic = (method.Attributes & System.Reflection.MethodAttributes.MemberAccessMask)
                    == System.Reflection.MethodAttributes.Public;
                result.Add(new OverloadInfo(
                    index++, signature.ReturnType, [.. signature.ParameterTypes],
                    signature.Header.IsInstance, method.RelativeVirtualAddress != 0, isPublic));
            }
            break;
        }
        return result;
    }

    /// <summary>
    /// Imports every method body in the assembly — the sweep front door the
    /// harness uses for corpus-wide inventory and gap scans.
    /// </summary>
    public static IEnumerable<(string TypeName, string MethodName, IrFunction Function)> ImportAssembly(MetadataSource source)
    {
        var reader = source.Reader;
        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            string typeName = reader.GetFullTypeName(typeDef);
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

    internal static GenericScope CallerScope(MetadataReader reader, TypeDefinition typeDef, MethodDefinition method)
        => new(GenericParameterNames(reader, typeDef.GetGenericParameters()),
               GenericParameterNames(reader, method.GetGenericParameters()));

    /// <summary>Internal for tests: synthetic IL can exercise join shapes C# never compiles to.</summary>
    internal static IrFunction Build(MetadataSource source, ImportedMethod method, GenericScope callerScope, List<IlTracePoint>? trace = null)
    {
        var container = new BlockContainer();
        var function = new IrFunction(method.Name, method.DeclaringType, method.Signature, method.Body.Locals, container)
        {
            Regions = method.Body.Handlers,
            LocalNames = method.Body.LocalNames,
            UsesUpdatedMemorySafetyRules = source.SimulateNewRules || ModuleUsesUpdatedMemorySafetyRules(source.Reader),
            SkipLocalsInit = method.Body.SkipLocalsInit,
        };
        var span = method.Body.IL.AsSpan();
        var leaders = FindLeaders(span, method.Body.Handlers);
        var state = new BuildState();
        foreach (var region in method.Body.Handlers)
        {
            // The CLR pushes the exception on entry to catch and filter code.
            if (region.Kind is HandlerKind.Catch)
                state.HandlerEntries[region.HandlerOffset] = region.CatchType;
            else if (region.Kind is HandlerKind.Filter)
            {
                state.HandlerEntries[region.FilterOffset] = null;
                state.HandlerEntries[region.HandlerOffset] = null;
            }
        }

        foreach (int leader in leaders)
        {
            var block = new Block(leader);
            container.Add(block);
            if (!BuildBlock(source, method, function, block, span, leader, NextLeader(leaders, leader, span.Length), callerScope, state, trace))
                return function;  // honest stop already recorded
        }

        ResolveTypeInfo(source, function);
        RecordUnsupportedTypeDiagnostics(function);
        function.CheckInvariant();
        return function;
    }

    /// <summary>
    /// Ensures every type the slice cannot represent reports *why* it lowered
    /// fidelity. A function-pointer parameter or a custom-modifier local sinks
    /// <see cref="IrFunction.Fidelity"/> through <see cref="TypeRef.ContainsUnsupported"/>,
    /// but unlike an out-of-slice opcode it carries no node-level stop — so
    /// without this the method counts as Partial with no diagnostic, invisible
    /// to the harness roadmap. One <see cref="DiagnosticIds.UnsupportedType"/>
    /// per distinct reason, deduped against reasons already reported, so a type
    /// repeated across many parameters reports once.
    /// </summary>
    static void RecordUnsupportedTypeDiagnostics(IrFunction function)
    {
        var reasons = new HashSet<string>();
        void Consider(TypeRef? type)
        {
            if (type is null || !type.ContainsUnsupported)
                return;
            foreach (var reason in type.UnsupportedReasons())
                reasons.Add(reason);
        }

        foreach (var node in function.Descendants.Prepend(function))
        {
            foreach (var type in node.DirectTypes)
                Consider(type);
            if (node is IrExpression expression)
                Consider(expression.ResultType);
        }

        if (reasons.Count == 0)
            return;

        var alreadyReported = function.Diagnostics
            .Select(d => d.Message)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var reason in reasons.OrderBy(r => r, StringComparer.Ordinal))
        {
            var message = $"type {reason}";
            if (alreadyReported.Add(message))
                function.Diagnostics.Add(new DecompilerDiagnostic(DiagnosticIds.UnsupportedType, message));
        }
    }

    /// <summary>
    /// Resolves the same-assembly shape of every definition type the function
    /// references, and the member map of every enum among them — materialized
    /// for the metadata-free printer. Walks both expression result types (the
    /// truthiness inputs) and node DirectTypes, so an enum that only appears as
    /// a call parameter type still resolves while metadata is live, before a
    /// later pass retypes its constant.
    /// </summary>
    static void ResolveTypeInfo(MetadataSource source, IrFunction function)
    {
        var shapes = new Dictionary<TypeRef, TypeShape>();
        Dictionary<TypeRef, IReadOnlyDictionary<long, string>>? enums = null;

        void Consider(TypeRef? type)
        {
            // ByRef/pointer carry the real type in their element: a `ref Enum`
            // parameter or `Enum*` only ever reaches the importer wrapped, so the
            // pointee enum's shape (and member names) would never be registered —
            // leaving `*styles & 64` as `int & 64` (CS0019). Unwrap to the pointee.
            while (type is { Kind: TypeRefKind.ByRef or TypeRefKind.Pointer, ElementType: { } element })
                type = element;
            if (type is not { Kind: TypeRefKind.Definition } || !shapes.TryAdd(type, default))
                return;
            var shape = source.ResolveShape(type);
            shapes[type] = shape;
            if (shape == TypeShape.Enum && source.ResolveEnumMembers(type) is { } members)
            {
                enums ??= [];
                enums[type] = members;
            }
        }

        foreach (var node in function.Descendants)
        {
            foreach (var type in node.DirectTypes)
                Consider(type);
            if (node is IrExpression expression)
                Consider(expression.ResultType);
        }

        // The signature's own types are not reached by any node's result type —
        // a method returning an enum it never loads (return TypeCode.Decimal as
        // ldc.i4 15) leaves the enum unresolved, so its constant stays a bare
        // int. Seed return and parameter types directly.
        Consider(function.Signature.ReturnType);
        foreach (var parameter in function.Signature.Parameters)
            Consider(parameter.Type);

        if (shapes.Count > 0)
            function.TypeShapes = shapes;
        if (enums is not null)
            function.EnumMembers = enums;
    }

    /// <summary>Block leaders: entry, branch and leave targets, instructions following a terminator, and every exception-region boundary.</summary>
    internal static SortedSet<int> FindLeaders(ReadOnlySpan<byte> il, System.Collections.Immutable.ImmutableArray<HandlerRegion> handlers)
    {
        var leaders = new SortedSet<int> { 0 };
        foreach (var region in handlers)
        {
            leaders.Add(region.TryOffset);
            leaders.Add(region.HandlerOffset);
            if (region.TryOffset + region.TryLength < il.Length)
                leaders.Add(region.TryOffset + region.TryLength);
            if (region.HandlerOffset + region.HandlerLength < il.Length)
                leaders.Add(region.HandlerOffset + region.HandlerLength);
            if (region.Kind == HandlerKind.Filter)
                leaders.Add(region.FilterOffset);
        }
        var reader = new ILReader(il);
        while (reader.HasNext)
        {
            var opcode = reader.ReadILOpcode();
            if (IsBranch(opcode) || opcode is ILOpCode.Leave or ILOpCode.Leave_s)
            {
                leaders.Add(reader.ReadBranchDestination(opcode));
                if (reader.Offset < il.Length)
                    leaders.Add(reader.Offset);
            }
            else if (opcode == ILOpCode.Switch)
            {
                uint count = reader.ReadILUInt32();
                var raw = new int[count];
                for (int i = 0; i < count; i++)
                    raw[i] = (int)reader.ReadILUInt32();
                int next = reader.Offset;
                foreach (int target in raw)
                    leaders.Add(next + target);
                if (next < il.Length)
                    leaders.Add(next);
            }
            else
            {
                reader.Skip(opcode);
                if (opcode is ILOpCode.Ret or ILOpCode.Throw or ILOpCode.Rethrow or ILOpCode.Endfinally or ILOpCode.Endfilter
                    && reader.Offset < il.Length)
                {
                    leaders.Add(reader.Offset);
                }
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
        public Dictionary<int, List<bool>> EntryStackNullLiterals { get; } = [];
        public HashSet<int> Built { get; } = [];
        public int NextDupSlot { get; set; } = StoreStackSlot.DupSlotBase;

        /// <summary>Catch/filter entry offsets and their exception types — the CLR-pushed value, not a slot load.</summary>
        public Dictionary<int, TypeRef?> HandlerEntries { get; } = [];
    }

    /// <summary>
    /// Spills the leftover evaluation stack into position-indexed slots and
    /// records the entry stack of every target. Position indexing makes all
    /// predecessors of a join store to the same slots. Returns false on a
    /// depth disagreement or a stack-carrying back edge (out of slice).
    /// </summary>
    static bool PropagateAndSpill(MetadataSource source, IrFunction function, Block body, Stack<IrExpression> stack, BuildState state, int[] targets, int offset)
    {
        var values = stack.Reverse().ToArray();
        stack.Clear();
        var types = values.Select(v => v.ResultType).ToList();
        var nullLiterals = values.Select(IsNullLiteral).ToList();
        for (int i = 0; i < values.Length; i++)
        {
            // Re-spilling a slot into itself (S_0 = S_0) happens whenever an
            // edge re-propagates values it loaded from the same slots.
            if (values[i] is LoadStackSlot identity && identity.Slot == i)
                continue;
            body.Add(new StoreStackSlot(i, values[i]));
        }
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
                if (!state.EntryStackNullLiterals.TryGetValue(target, out var existingNulls)
                    || existingNulls.Count != existing.Count)
                {
                    existingNulls = Enumerable.Repeat(false, existing.Count).ToList();
                    state.EntryStackNullLiterals[target] = existingNulls;
                }
                for (int i = 0; i < types.Count; i++)
                {
                    if (Equals(existing[i], types[i]))
                    {
                        existingNulls[i] = existingNulls[i] && nullLiterals[i];
                        continue;
                    }
                    if (types[i] is null)
                    {
                        existingNulls[i] = false;
                        continue;
                    }
                    if (existing[i] is null)
                    {
                        if (!state.Built.Contains(target))
                        {
                            existing[i] = types[i];
                            existingNulls[i] = nullLiterals[i];
                        }
                        continue;
                    }
                    // ECMA stack-type model: bool and int are the same I4
                    // stack entry, float and double the same F entry — the
                    // family-canonical type IS the ground truth there.
                    var merged = MergeSlotTypes(existing[i]!, types[i]!, source, existingNulls[i], nullLiterals[i]);
                    if (state.Built.Contains(target))
                    {
                        if (merged is null)
                        {
                            // The join was already built with the first type; a
                            // cross-family edge cannot be retyped honestly.
                            Stop(function, body, stack, offset, "(join)",
                                "evaluation-stack types disagree between paths into a join, outside the slice");
                            return false;
                        }
                        continue;
                    }
                    if (merged is null)
                    {
                        function.Diagnostics.Add(new DecompilerDiagnostic(
                            DiagnosticIds.UnsupportedConstruct,
                            $"IL_{offset:X4} (join-type): slot {i} type unknown — paths carry {existing[i]!.ToDisplayString()} and {types[i]!.ToDisplayString()}"));
                    }
                    existing[i] = merged;  // family-canonical, or null — never a guess
                    existingNulls[i] = false;
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
                state.EntryStackNullLiterals[target] = nullLiterals;
            }
        }
        return true;
    }

    static bool IsNullLiteral(IrExpression value) => value is Constant { Value: null };

    /// <summary>Builds one block. Returns false when the import stopped honestly inside it.</summary>
    static bool BuildBlock(MetadataSource source, ImportedMethod method, IrFunction function, Block body,
        ReadOnlySpan<byte> il, int start, int end, GenericScope callerScope, BuildState state, List<IlTracePoint>? trace = null)
    {
        var stack = new Stack<IrExpression>();
        var reader = new ILReader(il[..end], currentOffset: start);
        int offset = start;
        TypeRef? constrainedTo = null;
        bool volatilePrefix = false, readonlyPrefix = false;

        // Entry values arrive in position-indexed slots stored by predecessors —
        // except handler entries, where the CLR pushes the exception itself.
        state.Built.Add(start);
        if (state.HandlerEntries.TryGetValue(start, out var exceptionType))
        {
            stack.Push(new CaughtException(exceptionType));
            state.EntryStacks[start] = [];
            state.EntryStackNullLiterals[start] = [];
        }
        else
        {
            var entry = state.EntryStacks.TryGetValue(start, out var carried) ? carried : [];
            state.EntryStacks[start] = entry;
            for (int i = 0; i < entry.Count; i++)
                stack.Push(new LoadStackSlot(i, entry[i]));
        }

        // Provenance watermark: statements already in the block (e.g. entry
        // spills) are not ours to stamp; only statements appended past this
        // index by the loop below get the current instruction's offset.
        int stampedStatements = body.Children.Count;

        try
        {
        while (reader.HasNext)
        {
            offset = reader.Offset;
            var opcode = reader.ReadILOpcode();
            switch (opcode)
            {
                case ILOpCode.Constrained:
                    constrainedTo = ResolveTypeToken(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope);
                    continue;
                case ILOpCode.Volatile:
                    volatilePrefix = true;
                    continue;
                case ILOpCode.Readonly:
                    readonlyPrefix = true;
                    continue;
                case ILOpCode.Unaligned:
                    // Alignment is a JIT hint with no source-level meaning;
                    // the operand byte is consumed and the access proceeds.
                    reader.ReadILByte();
                    continue;

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
                    body.Add(MakeStoreLocalSpilling(method, opcode - ILOpCode.Stloc_0, Pop(stack), body, stack, state));
                    break;
                case ILOpCode.Stloc_s:
                    body.Add(MakeStoreLocalSpilling(method, reader.ReadILByte(), Pop(stack), body, stack, state));
                    break;
                case ILOpCode.Stloc:
                    body.Add(MakeStoreLocalSpilling(method, reader.ReadILUInt16(), Pop(stack), body, stack, state));
                    break;

                case >= ILOpCode.Ldc_i4_m1 and <= ILOpCode.Ldc_i4_8:
                    // Subtract as int, not as the enum: ushort enum
                    // subtraction wraps Ldc_i4_m1 - Ldc_i4_0 to 65535 before
                    // any cast can repair it, and -1 must stay -1.
                    stack.Push(new Constant((int)opcode - (int)ILOpCode.Ldc_i4_0, TypeRef.CoreLib("System", "Int32")));
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
                case ILOpCode.Ldc_r4:
                    stack.Push(new Constant(BitConverter.UInt32BitsToSingle(reader.ReadILUInt32()), TypeRef.CoreLib("System", "Single")));
                    break;
                case ILOpCode.Ldc_r8:
                    stack.Push(new Constant(BitConverter.UInt64BitsToDouble(reader.ReadILUInt64()), TypeRef.CoreLib("System", "Double")));
                    break;

                case ILOpCode.Sizeof:
                    stack.Push(new SizeOf(ResolveTypeToken(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope)));
                    break;

                case ILOpCode.Switch:
                {
                    uint count = reader.ReadILUInt32();
                    var rawTargets = new int[count];
                    for (int i = 0; i < count; i++)
                        rawTargets[i] = (int)reader.ReadILUInt32();
                    int next = reader.Offset;
                    var targets = ImmutableArray.CreateBuilder<int>((int)count);
                    foreach (int raw in rawTargets)
                        targets.Add(next + raw);
                    var value = Pop(stack);
                    var allSuccessors = targets.Append(end).Distinct().ToArray();
                    if (!PropagateAndSpill(source, function, body, stack, state, allSuccessors, offset))
                        return false;
                    body.Add(new SwitchBranch(value, targets.MoveToImmutable()));
                    break;
                }

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
                    // MemberRefs carry no MethodDef rows; resolve only the
                    // missing cross-assembly facts this callee can consume.
                    callee = source.CrossAssembly.Upgrade(callee, function.UsesUpdatedMemorySafetyRules);
                    int argumentCount = callee.ParameterTypes.Length + (callee.HasThis ? 1 : 0);
                    var arguments = new IrExpression[argumentCount];
                    for (int i = argumentCount - 1; i >= 0; i--)
                        arguments[i] = Pop(stack);
                    var call = new Call(callee, opcode == ILOpCode.Callvirt, arguments) { ConstrainedTo = constrainedTo };
                    constrainedTo = null;
                    if (callee.ReturnType is { Name: "Void", Namespace: "System" })
                    {
                        // Emitting the call as a statement creates a sequence
                        // point; any IL-earlier side-effecting value still pending
                        // lazily below must materialize first or it reorders past
                        // this call (runtime-async keeps such values on the eval
                        // stack across awaits — see MakeStoreLocalSpilling).
                        SpillUnstableBeforeSideEffect(body, stack, state);
                        body.Add(new ExpressionStatement(call));
                    }
                    else
                        stack.Push(call);
                    break;
                }

                case ILOpCode.Ldfld or ILOpCode.Ldsfld:
                {
                    var field = ResolveField(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope);
                    stack.Push(new LoadField(field, opcode == ILOpCode.Ldfld ? Pop(stack) : null) { IsVolatile = volatilePrefix });
                    volatilePrefix = false;
                    break;
                }
                case ILOpCode.Stfld:
                {
                    var field = ResolveField(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope);
                    var value = Pop(stack);
                    var instance = Pop(stack);
                    SpillUnstableBeforeSideEffect(body, stack, state);
                    body.Add(new StoreField(field, instance, value) { IsVolatile = volatilePrefix });
                    volatilePrefix = false;
                    break;
                }
                case ILOpCode.Stsfld:
                {
                    var field = ResolveField(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope);
                    var value = Pop(stack);
                    SpillUnstableBeforeSideEffect(body, stack, state);
                    body.Add(new StoreField(field, null, value) { IsVolatile = volatilePrefix });
                    volatilePrefix = false;
                    break;
                }

                case ILOpCode.Ldflda or ILOpCode.Ldsflda:
                {
                    var fieldHandle = MetadataTokens.EntityHandle(reader.ReadILToken());
                    var field = ResolveField(source.Reader, fieldHandle, callerScope);
                    var rvaData = opcode == ILOpCode.Ldsflda && fieldHandle.Kind == HandleKind.FieldDefinition
                        ? TryReadFieldRvaData(source, (FieldDefinitionHandle)fieldHandle)
                        : null;
                    stack.Push(new LoadFieldAddress(field, opcode == ILOpCode.Ldflda ? Pop(stack) : null)
                    {
                        FieldRvaData = rvaData,
                    });
                    break;
                }

                case ILOpCode.Ldloca_s:
                {
                    int index = reader.ReadILByte();
                    stack.Push(new LoadLocalAddress(index, method.Body.Locals[index]));
                    break;
                }
                case ILOpCode.Ldloca:
                {
                    int index = reader.ReadILUInt16();
                    stack.Push(new LoadLocalAddress(index, method.Body.Locals[index]));
                    break;
                }
                case ILOpCode.Ldarga_s:
                    stack.Push(MakeLoadArgumentAddress(method, reader.ReadILByte()));
                    break;
                case ILOpCode.Ldarga:
                    stack.Push(MakeLoadArgumentAddress(method, reader.ReadILUInt16()));
                    break;

                case ILOpCode.Ldelema:
                {
                    var elementType = ResolveTypeToken(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope);
                    var index = Pop(stack);
                    stack.Push(new LoadElementAddress(elementType, Pop(stack), index, readonlyPrefix));
                    readonlyPrefix = false;
                    break;
                }

                case ILOpCode.Ldobj:
                {
                    var type = ResolveTypeToken(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope);
                    stack.Push(new LoadIndirect(type, Pop(stack)) { IsVolatile = volatilePrefix });
                    volatilePrefix = false;
                    break;
                }
                case ILOpCode.Stobj:
                {
                    var type = ResolveTypeToken(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope);
                    var value = Pop(stack);
                    var address = Pop(stack);
                    SpillUnstableBeforeSideEffect(body, stack, state);
                    body.Add(new StoreIndirect(type, address, value) { IsVolatile = volatilePrefix });
                    volatilePrefix = false;
                    break;
                }
                case ILOpCode.Initobj:
                {
                    var type = ResolveTypeToken(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope);
                    var address = Pop(stack);
                    SpillUnstableBeforeSideEffect(body, stack, state);
                    body.Add(new InitObject(type, address));
                    break;
                }

                case >= ILOpCode.Ldind_i1 and <= ILOpCode.Ldind_ref:
                {
                    stack.Push(new LoadIndirect(IndirectTypeOf(opcode), Pop(stack)) { IsVolatile = volatilePrefix });
                    volatilePrefix = false;
                    break;
                }
                case ILOpCode.Stind_ref or (>= ILOpCode.Stind_i1 and <= ILOpCode.Stind_r8) or ILOpCode.Stind_i:
                {
                    var value = Pop(stack);
                    var address = Pop(stack);
                    SpillUnstableBeforeSideEffect(body, stack, state);
                    body.Add(new StoreIndirect(IndirectTypeOf(opcode), address, value) { IsVolatile = volatilePrefix });
                    volatilePrefix = false;
                    break;
                }

                case ILOpCode.Ldelem:
                {
                    var elementType = ResolveTypeToken(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope);
                    var index = Pop(stack);
                    stack.Push(new LoadElement(elementType, Pop(stack), index));
                    break;
                }
                case >= ILOpCode.Ldelem_i1 and <= ILOpCode.Ldelem_ref or ILOpCode.Ldelem_i:
                {
                    var index = Pop(stack);
                    stack.Push(new LoadElement(ElementTypeOf(opcode), Pop(stack), index));
                    break;
                }
                case ILOpCode.Stelem:
                {
                    var elementType = ResolveTypeToken(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope);
                    var value = Pop(stack);
                    var index = Pop(stack);
                    var array = Pop(stack);
                    SpillUnstableBeforeSideEffect(body, stack, state);
                    body.Add(new StoreElement(elementType, array, index, value));
                    break;
                }
                case >= ILOpCode.Stelem_i and <= ILOpCode.Stelem_ref:
                {
                    var value = Pop(stack);
                    var index = Pop(stack);
                    var array = Pop(stack);
                    SpillUnstableBeforeSideEffect(body, stack, state);
                    body.Add(new StoreElement(StelemElementType(opcode, array), array, index, value));
                    break;
                }

                case ILOpCode.Unbox:
                    stack.Push(new Unbox(ResolveTypeToken(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope), Pop(stack)));
                    break;
                case ILOpCode.Unbox_any:
                    stack.Push(new UnboxAny(ResolveTypeToken(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope), Pop(stack)));
                    break;

                case ILOpCode.Newobj:
                {
                    var ctorHandle = MetadataTokens.EntityHandle(reader.ReadILToken());
                    var constructor = ResolveMethod(source.Reader, ctorHandle, callerScope);
                    // A bare cross-assembly struct token carries no VALUETYPE
                    // byte; resolve its value-type-ness so a struct constructor
                    // (new DateTime(...)) is not misread as a heap allocation.
                    constructor = constructor with { DeclaringType = source.CrossAssembly.Upgrade(constructor.DeclaringType) };
                    constructor = source.CrossAssembly.Upgrade(constructor, function.UsesUpdatedMemorySafetyRules);
                    var arguments = new IrExpression[constructor.ParameterTypes.Length];
                    for (int i = arguments.Length - 1; i >= 0; i--)
                        arguments[i] = Pop(stack);
                    stack.Push(new NewObject(constructor, arguments)
                    {
                        AnonymousPropertyNames = ReadAnonymousPropertyNames(source.Reader, ctorHandle),
                    });
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
                            stack.Push(new LoadToken(RuntimeTokenKind.Field, null, $"{field.DeclaringType.ToDisplayString()}.{field.Name}")
                            {
                                FieldRvaData = TryReadFieldRvaData(source, (FieldDefinitionHandle)handle),
                            });
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
                {
                    // Popping a side-effect-free value (the dup/coalesce
                    // lowering discards copies constantly) needs no statement.
                    var popped = Pop(stack);
                    if (popped is not (Constant or LoadArgument or LoadLocal or LoadStackSlot))
                        body.Add(new ExpressionStatement(popped));
                    break;
                }

                case ILOpCode.Br or ILOpCode.Br_s:
                {
                    int target = reader.ReadBranchDestination(opcode);
                    if (!PropagateAndSpill(source, function, body, stack, state, [target], offset))
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
                    if (!PropagateAndSpill(source, function, body, stack, state, [target, end], offset))
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
                    if (!PropagateAndSpill(source, function, body, stack, state, [target, end], offset))
                        return false;
                    body.Add(new ConditionalBranch(new Comparison(kind, isUnsigned, left, right), target));
                    break;
                }

                case ILOpCode.Leave or ILOpCode.Leave_s:
                {
                    // leave empties the evaluation stack; pending values'
                    // side effects already happened, so they spill as
                    // statements in evaluation order.
                    int target = reader.ReadBranchDestination(opcode);
                    foreach (var pending in stack.Reverse())
                        body.Add(new ExpressionStatement(pending));
                    stack.Clear();
                    body.Add(new Leave(target));
                    break;
                }

                case ILOpCode.Endfinally:
                    // ECMA-335 III.3.35: the evaluation stack must be empty.
                    // Unlike leave (defined to empty the stack), a non-empty
                    // stack here is malformed IL — stop honestly.
                    if (stack.Count > 0)
                    {
                        Stop(function, body, stack, offset, "endfinally",
                            "evaluation stack is not empty at endfinally (malformed IL)");
                        return false;
                    }
                    body.Add(new EndFinally());
                    break;

                case ILOpCode.Endfilter:
                {
                    // ECMA-335 III.3.34: the stack holds exactly the verdict.
                    var verdict = Pop(stack);
                    if (stack.Count > 0)
                    {
                        Stop(function, body, stack, offset, "endfilter",
                            "evaluation stack holds more than the filter verdict (malformed IL)");
                        return false;
                    }
                    body.Add(new EndFilter(verdict));
                    break;
                }

                case ILOpCode.Rethrow:
                    body.Add(new Throw(new CaughtException(null)));
                    break;

                case ILOpCode.Ret:
                    body.Add(new Return(stack.Count > 0 ? Pop(stack) : null));
                    break;

                case ILOpCode.Ldftn:
                {
                    var target = ResolveMethod(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope);
                    stack.Push(new LoadFunctionPointer(target, isVirtual: false, instance: null));
                    break;
                }

                case ILOpCode.Ldvirtftn:
                {
                    var target = ResolveMethod(source.Reader, MetadataTokens.EntityHandle(reader.ReadILToken()), callerScope);
                    var instance = Pop(stack);
                    stack.Push(new LoadFunctionPointer(target, isVirtual: true, instance));
                    break;
                }

                case ILOpCode.Calli:
                {
                    // The operand is a standalone call-site signature: it gives
                    // the return and parameter types and whether the pointer is
                    // an instance function pointer. The function-pointer value
                    // is on top; the arguments are beneath it.
                    var signature = source.Reader
                        .GetStandaloneSignature((StandaloneSignatureHandle)MetadataTokens.EntityHandle(reader.ReadILToken()))
                        .DecodeMethodSignature(TypeRefDecoder.Instance, callerScope);
                    var pointer = Pop(stack);
                    int argumentCount = signature.ParameterTypes.Length + (signature.Header.IsInstance ? 1 : 0);
                    var arguments = new IrExpression[argumentCount];
                    for (int i = argumentCount - 1; i >= 0; i--)
                        arguments[i] = Pop(stack);
                    var callIndirect = new CallIndirect(pointer, arguments, signature.ReturnType, signature.ParameterTypes);
                    if (signature.ReturnType is { Name: "Void", Namespace: "System" })
                        body.Add(new ExpressionStatement(callIndirect));
                    else
                        stack.Push(callIndirect);
                    break;
                }

                case ILOpCode.Localloc:
                {
                    // localloc's operand is a native-int byte count; C#'s
                    // stackalloc takes the logical count, so strip the widening
                    // conversion the compiler emits to feed localloc.
                    var size = Pop(stack);
                    if (size is Convert conversion
                        && conversion.Target is { Namespace: "System", Name: "IntPtr" or "UIntPtr" })
                        size = (IrExpression)conversion.DetachChildren()[0];
                    stack.Push(new StackAllocate(size));
                    break;
                }

                default:
                    Stop(function, body, stack, offset, opcode.ToString().ToLowerInvariant(),
                        "opcode is outside the slice");
                    return false;
            }

            // Capture the post-opcode evaluation-stack types for the typed-IL
            // projection — a no-op unless a trace is requested. Prefix opcodes
            // `continue` above, so they are correctly excluded.
            trace?.Add(new IlTracePoint(offset, [.. stack.Reverse().Select(e => e.ResultType)]));

            // Stamp IL-offset provenance on the nodes this instruction created.
            // Operands it consumed were pushed (and stamped) by earlier
            // instructions, so StampSubtree stops at them; only the genuinely
            // new node(s) — the fresh stack top and any statements appended this
            // iteration — take the current offset. Prefix opcodes `continue`
            // above and never reach here.
            for (; stampedStatements < body.Children.Count; stampedStatements++)
                StampSubtree(body.Children[stampedStatements], offset);
            foreach (var pushed in stack)
                StampSubtree(pushed, offset);

            if (constrainedTo is not null || volatilePrefix || readonlyPrefix)
            {
                Stop(function, body, stack, offset, opcode.ToString().ToLowerInvariant(),
                    "an IL prefix applied to an instruction that does not accept it");
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
            return PropagateAndSpill(source, function, body, stack, state, [end], end);
        }
        return true;
    }

    /// <summary>
    /// Stamps <paramref name="offset"/> onto every node in the subtree that has
    /// no provenance yet, stopping at any already-stamped node. The stop is the
    /// point: operands a node consumed were stamped by the earlier instructions
    /// that pushed them, so descent halts there and only the freshly-created
    /// node(s) receive the current offset. A stamped node always has a fully
    /// stamped subtree, which keeps this O(new nodes) per instruction.
    /// </summary>
    static void StampSubtree(IrNode node, int offset)
    {
        if (node.SourceOffset >= 0)
            return;
        node.SetSourceOffset(offset);
        foreach (var child in node.Children)
            StampSubtree(child, offset);
    }

    /// <summary>
    /// Merges two slot types per the ECMA stack-type model. Equal types keep.
    /// Two reference types prefer the precise common base — whichever operand
    /// is a base class of the other (object is the universal base of every
    /// reference type) — the verifier's merge when one operand is an ancestor;
    /// it never invents a common ancestor, so the slot type is one a path
    /// already carried and every use still typechecks. Otherwise the stack
    /// families decide: bool and int are the same I4 entry, float and double
    /// the same F entry, and two reference values the same O entry (object) —
    /// the family-canonical type is the ground truth there. Anything else is
    /// null — an honest "unknown" rather than a guess.
    /// </summary>
    static TypeRef? MergeSlotTypes(TypeRef a, TypeRef b, MetadataSource source, bool aIsNullLiteral = false, bool bIsNullLiteral = false)
    {
        if (Equals(a, b))
            return a;
        // ldnull has no concrete reference type of its own. When the other path
        // proves a reference type, the null arm adopts that type at the join.
        if (aIsNullLiteral && IsReferenceType(source, b))
            return b;
        if (bIsNullLiteral && IsReferenceType(source, a))
            return a;
        if (IsReferenceType(source, a) && IsReferenceType(source, b))
        {
            // The nearest common supertype both paths are assignable to — an
            // implemented interface or a common base class (object only when
            // both chains genuinely resolve to it). Null leaves the slot
            // untyped and the method honestly Partial.
            if (source.MergeReferenceTypes(a, b) is { } merged)
                return merged;
        }
        var familyA = TypeFamilies.Of(a);
        var familyB = TypeFamilies.Of(b);
        if (familyA is not null && familyB is not null && familyA == familyB)
            return TypeFamilies.Canonical(familyA.Value);
        return null;
    }

    /// <summary>A reference type: object, string, an array, or a definition (or generic instantiation of one) the source resolves to a reference shape.</summary>
    static bool IsReferenceType(MetadataSource source, TypeRef type)
    {
        if (TypeFamilies.Of(type) == StackFamily.O)
            return true;
        // Signature decoding preserves the ELEMENT_TYPE_CLASS byte, which is
        // enough to classify cross-assembly reference types such as System.Type.
        if (type.DeclaredValueTypeHint == ValueTypeHint.ReferenceType)
            return true;
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        return definition is not null && source.ResolveShape(definition) == TypeShape.Reference;
    }

    /// <summary>A block whose entry expects stack values is a stack-carrying edge — out of slice, reported honestly via the importer's stop path.</summary>
    sealed class OutOfSliceException(string reason) : Exception(reason);

    static IrExpression Pop(Stack<IrExpression> stack)
        => stack.Count > 0
            ? stack.Pop()
            : throw new OutOfSliceException("evaluation stack carries values into this block, outside the slice");

    /// <summary>
    /// Spills pending stack entries that read mutable state (field/element/
    /// indirect loads, prior call results) into dup slots before a heap-mutating
    /// statement is emitted. The symbolic stack carries lazy expression trees, so
    /// a value read <em>before</em> a store but consumed <em>after</em> it would
    /// otherwise be re-evaluated against the post-store state — a reordering
    /// miscompile (issue #605, StaleFieldRead). Materializing the survivor pins
    /// it to its pre-store value. Stable entries (constants, locals, arguments,
    /// addresses, and pure compositions of them) are left in place so output
    /// stays minimal; the inliner re-collapses any single-use temp it can prove
    /// safe to fold.
    /// </summary>
    static void SpillUnstableBeforeSideEffect(Block body, Stack<IrExpression> stack, BuildState state)
    {
        if (stack.Count == 0)
            return;

        var values = stack.Reverse().ToArray(); // bottom-to-top
        bool anyUnstable = false;
        foreach (var value in values)
            if (!IsStableAcrossSideEffect(value)) { anyUnstable = true; break; }
        if (!anyUnstable)
            return;

        stack.Clear();
        foreach (var value in values) // bottom-to-top preserves evaluation order
        {
            if (IsStableAcrossSideEffect(value))
            {
                stack.Push(value);
                continue;
            }
            int slot = state.NextDupSlot++;
            body.Add(new StoreStackSlot(slot, value));
            stack.Push(new LoadStackSlot(slot, value.ResultType));
        }
    }

    /// <summary>
    /// True when re-evaluating <paramref name="expression"/> after an intervening
    /// heap/static store yields the same value — i.e. it does not read mutable
    /// heap state and has no side effect. Conservative: anything not on this
    /// allow-list (field/element/indirect/property loads, calls, allocations) is
    /// treated as unstable and spilled.
    /// </summary>
    static bool IsStableAcrossSideEffect(IrExpression expression)
    {
        // A managed pointer or pointer is an address: re-evaluating the address
        // expression yields the same location even after a store, and routing it
        // through a value slot would strip the ref-ness a later ref/out argument
        // needs (CS1620). Treat any byref/pointer-typed value as stable.
        if (expression.ResultType is { Kind: TypeRefKind.ByRef or TypeRefKind.Pointer })
            return true;

        return expression switch
        {
            Constant or SizeOf or LoadToken or TypeOf => true,
            LoadArgument or LoadLocal or LoadStackSlot => true,
            LoadLocalAddress or LoadArgumentAddress => true,
            LoadFunctionPointer or AddressOfMethod => true,
            Binary binary => IsStableAcrossSideEffect(binary.Left) && IsStableAcrossSideEffect(binary.Right),
            Comparison comparison => IsStableAcrossSideEffect(comparison.Left) && IsStableAcrossSideEffect(comparison.Right),
            Convert convert => IsStableAcrossSideEffect(convert.Operand),
            Unary unary => IsStableAcrossSideEffect(unary.Operand),
            LogicalNot logicalNot => IsStableAcrossSideEffect(logicalNot.Operand),
            _ => false,
        };
    }

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

    static LoadArgumentAddress MakeLoadArgumentAddress(ImportedMethod method, int index)
    {
        if (method.Signature.HasThis)
        {
            if (index == 0)
                return new LoadArgumentAddress(0, "this", method.DeclaringType);
            var p = method.Signature.Parameters[index - 1];
            return new LoadArgumentAddress(index, p.Name, p.Type);
        }
        var parameter = method.Signature.Parameters[index];
        return new LoadArgumentAddress(index, parameter.Name, parameter.Type);
    }

    /// <summary>Element/indirection type encoded by a typed ldind/stind/ldelem/stelem opcode; null for the .ref forms (the operand's type stands in).</summary>
    static TypeRef? IndirectTypeOf(ILOpCode opcode) => opcode switch
    {
        ILOpCode.Ldind_i1 or ILOpCode.Stind_i1 => TypeRef.CoreLib("System", "SByte"),
        ILOpCode.Ldind_u1 => TypeRef.CoreLib("System", "Byte"),
        ILOpCode.Ldind_i2 or ILOpCode.Stind_i2 => TypeRef.CoreLib("System", "Int16"),
        ILOpCode.Ldind_u2 => TypeRef.CoreLib("System", "UInt16"),
        ILOpCode.Ldind_i4 or ILOpCode.Stind_i4 => TypeRef.CoreLib("System", "Int32"),
        ILOpCode.Ldind_u4 => TypeRef.CoreLib("System", "UInt32"),
        ILOpCode.Ldind_i8 or ILOpCode.Stind_i8 => TypeRef.CoreLib("System", "Int64"),
        ILOpCode.Ldind_r4 or ILOpCode.Stind_r4 => TypeRef.CoreLib("System", "Single"),
        ILOpCode.Ldind_r8 or ILOpCode.Stind_r8 => TypeRef.CoreLib("System", "Double"),
        ILOpCode.Ldind_i or ILOpCode.Stind_i => TypeRef.CoreLib("System", "IntPtr"),
        _ => null,  // ldind.ref / stind.ref
    };

    /// <summary>
    /// Element type for a typed <c>stelem</c>, preferring the array's own element
    /// type over the opcode's default signedness. <c>stelem.i1</c> stores into
    /// <c>sbyte[]</c>, <c>byte[]</c>, and <c>bool[]</c> alike; typing the store
    /// from the opcode (always <c>sbyte</c>) makes the printer cast the value to
    /// <c>(sbyte)</c>, which is CS0266 against a <c>byte[]</c> element. When the
    /// array's static element type is a same-width integer sibling
    /// (byte/sbyte, short/ushort/char, int/uint, long/ulong), it is authoritative —
    /// the C# store goes through it — so use it; otherwise fall back to the opcode.
    /// </summary>
    static TypeRef? StelemElementType(ILOpCode opcode, IrExpression array)
    {
        var fromOpcode = ElementTypeOf(opcode);
        return fromOpcode is not null
            && array.ResultType is { Kind: TypeRefKind.SzArray, ElementType: { } element }
            && TypeFamilies.SameWidth(element, fromOpcode)
            ? element
            : fromOpcode;
    }

    static TypeRef? ElementTypeOf(ILOpCode opcode) => opcode switch
    {
        ILOpCode.Ldelem_i1 or ILOpCode.Stelem_i1 => TypeRef.CoreLib("System", "SByte"),
        ILOpCode.Ldelem_u1 => TypeRef.CoreLib("System", "Byte"),
        ILOpCode.Ldelem_i2 or ILOpCode.Stelem_i2 => TypeRef.CoreLib("System", "Int16"),
        ILOpCode.Ldelem_u2 => TypeRef.CoreLib("System", "UInt16"),
        ILOpCode.Ldelem_i4 or ILOpCode.Stelem_i4 => TypeRef.CoreLib("System", "Int32"),
        ILOpCode.Ldelem_u4 => TypeRef.CoreLib("System", "UInt32"),
        ILOpCode.Ldelem_i8 or ILOpCode.Stelem_i8 => TypeRef.CoreLib("System", "Int64"),
        ILOpCode.Ldelem_r4 or ILOpCode.Stelem_r4 => TypeRef.CoreLib("System", "Single"),
        ILOpCode.Ldelem_r8 or ILOpCode.Stelem_r8 => TypeRef.CoreLib("System", "Double"),
        ILOpCode.Ldelem_i or ILOpCode.Stelem_i => TypeRef.CoreLib("System", "IntPtr"),
        _ => null,  // ldelem.ref / stelem.ref
    };

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

    /// <summary>
    /// Builds a <see cref="StoreLocal"/>, first pinning any IL-earlier
    /// side-effecting value still pending lazily on the symbolic stack when the
    /// value being stored is itself side-effecting. Storing a side-effecting
    /// value (a call) is a sequence point; a lower lazy entry that was evaluated
    /// earlier in IL order would otherwise materialize <em>after</em> this store
    /// and reorder. Runtime-async (async v2) makes this reachable: it legally
    /// keeps a value-producing <c>await</c> on the evaluation stack across a
    /// later <c>await</c>, so <c>x = await a; y = await b;</c> imports as a bare
    /// <c>await a</c> stranded below the <c>y = await b</c> store. Stable stored
    /// values (locals, args, constants) carry no side effect and need no spill,
    /// keeping output minimal.
    /// </summary>
    static StoreLocal MakeStoreLocalSpilling(ImportedMethod method, int index, IrExpression value, Block body, Stack<IrExpression> stack, BuildState state)
    {
        if (!IsStableAcrossSideEffect(value))
            SpillUnstableBeforeSideEffect(body, stack, state);
        return MakeStoreLocal(method, index, value);
    }

    internal static MethodRef ResolveMethod(MetadataReader reader, EntityHandle handle, GenericScope callerScope)
    {
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
            {
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                var declaringTypeHandle = method.GetDeclaringType();
                var declaring = TypeRefDecoder.Instance.GetTypeFromDefinition(reader, declaringTypeHandle, 0);
                var declaringType = reader.GetTypeDefinition(declaringTypeHandle);
                var typeScope = new GenericScope(GenericParameterNames(reader, declaringType.GetGenericParameters()), []);
                var signature = method.DecodeSignature(TypeRefDecoder.Instance, typeScope);
                var parameterRefKinds = MethodDefinitionFacts.ReadParameterRefKinds(reader, method, signature.ParameterTypes);
                bool methodCompilerGenerated = MethodDefinitionFacts.HasCompilerGeneratedAttribute(reader, method.GetCustomAttributes());
                bool typeCompilerGenerated = MethodDefinitionFacts.HasCompilerGeneratedAttribute(reader, declaringType.GetCustomAttributes());
                return new MethodRef(declaring, reader.GetString(method.Name), signature.ReturnType, signature.ParameterTypes, signature.Header.IsInstance)
                {
                    IsSpecialName = (method.Attributes & System.Reflection.MethodAttributes.SpecialName) != 0,
                    ParameterRefKinds = parameterRefKinds.Kinds,
                    ParameterRefKindsFacts = parameterRefKinds.State,
                    RequiresUnsafe = MethodDefinitionFacts.HasRequiresUnsafeAttribute(reader, method),
                    CompilerGenerated = FactState(methodCompilerGenerated),
                    DeclaringTypeCompilerGenerated = FactState(typeCompilerGenerated),
                    IsExtension = FactState(MethodDefinitionFacts.HasExtensionAttribute(reader, method)),
                };
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
                string memberName = reader.GetString(member.Name);
                var parameterTypes = ImmutableArray.CreateRange(signature.ParameterTypes.Select(p => p.Instantiate(typeArguments, [])));
                var parameterRefKinds = MemberReferenceRefKinds(reader, member, memberName, parameterTypes);
                return new MethodRef(
                    declaring,
                    memberName,
                    signature.ReturnType.Instantiate(typeArguments, []),
                    parameterTypes,
                    signature.Header.IsInstance)
                {
                    // MemberRefs carry no flags; accessor-shape naming is the
                    // strongest local evidence without assembly resolution.
                    IsSpecialName = memberName.StartsWith("get_", StringComparison.Ordinal)
                        || memberName.StartsWith("set_", StringComparison.Ordinal)
                        || memberName.StartsWith("add_", StringComparison.Ordinal)
                        || memberName.StartsWith("remove_", StringComparison.Ordinal)
                        || memberName.StartsWith("op_", StringComparison.Ordinal)
                        || memberName is ".ctor" or ".cctor",
                    // A same-assembly call on a generic type instance is a
                    // MemberRef (TypeSpec parent), so its ref/out/in would
                    // otherwise be lost; recover it from the underlying MethodDef.
                    ParameterRefKinds = parameterRefKinds.Kinds,
                    ParameterRefKindsFacts = parameterRefKinds.State,
                };
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

    static MetadataFactState FactState(bool value) => value ? MetadataFactState.Yes : MetadataFactState.No;

    /// <summary>
    /// The property names of an anonymous-type constructor, in argument order, or
    /// empty when the constructor's declaring type is not a compiler-generated
    /// anonymous type. Anonymous types are always same-assembly MethodDefs, so the
    /// declaring <see cref="TypeDefinition"/> and its property rows are readable
    /// directly; the property table order matches the constructor's parameter
    /// order (Roslyn emits both in source-declaration order), so the names align
    /// 1:1 with the <c>newobj</c> arguments. Used by <c>AnonymousObjectPass</c>.
    /// </summary>
    static ImmutableArray<string> ReadAnonymousPropertyNames(MetadataReader reader, EntityHandle ctorHandle)
    {
        TypeDefinitionHandle typeHandle = ctorHandle.Kind switch
        {
            // A non-generic anonymous type's ctor is a MethodDef; a generic one
            // (the usual case — any anonymous type with properties is generic) is
            // a MemberRef whose parent TypeSpec resolves to the same-assembly def.
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)ctorHandle).GetDeclaringType(),
            HandleKind.MemberReference => DeclaringTypeDefinition(reader, reader.GetMemberReference((MemberReferenceHandle)ctorHandle).Parent) ?? default,
            _ => default,
        };
        if (typeHandle.IsNil)
            return [];
        var typeDef = reader.GetTypeDefinition(typeHandle);
        if (!reader.GetString(typeDef.Name).StartsWith("<>f__AnonymousType", StringComparison.Ordinal))
            return [];
        var names = ImmutableArray.CreateBuilder<string>();
        foreach (var handle in typeDef.GetProperties())
            names.Add(reader.GetString(reader.GetPropertyDefinition(handle).Name));
        return names.ToImmutable();
    }

    /// <summary>
    /// Recovers each by-ref parameter's call-site ref-kind (ref/out/in) from the
    /// MethodDef parameter rows. C# <c>in</c> is marked by
    /// <c>IsReadOnlyAttribute</c> — NOT the raw <c>In</c> metadata flag, which is
    /// also a marshalling directive; a true <c>out</c> carries the <c>Out</c> flag
    /// without <c>In</c> (an <c>[In, Out]</c> by-ref is interop marshalling on a
    /// <c>ref</c>). By-value parameters are <see cref="ArgumentRefKind.Value"/>.
    /// The result aligns 1:1 with <paramref name="parameterTypes"/>, or is empty
    /// when there are no by-ref parameters to spell.
    /// </summary>
    static ImmutableArray<ArgumentRefKind> ReadParameterRefKinds(MetadataReader reader, MethodDefinition method, ImmutableArray<TypeRef> parameterTypes)
    {
        bool anyByRef = false;
        foreach (var p in parameterTypes)
            if (p.Kind == TypeRefKind.ByRef) { anyByRef = true; break; }
        if (!anyByRef)
            return [];
        var kinds = new ArgumentRefKind[parameterTypes.Length];
        for (int i = 0; i < kinds.Length; i++)
            kinds[i] = parameterTypes[i].Kind == TypeRefKind.ByRef ? ArgumentRefKind.Ref : ArgumentRefKind.Value;
        foreach (var handle in method.GetParameters())
        {
            var parameter = reader.GetParameter(handle);
            int index = parameter.SequenceNumber - 1;  // sequence 0 is the return parameter
            if (index < 0 || index >= kinds.Length || parameterTypes[index].Kind != TypeRefKind.ByRef)
                continue;
            kinds[index] = ClassifyByRefParameter(reader, parameter);
        }
        return ImmutableArray.Create(kinds);
    }

    /// <summary>
    /// Recovers ref/out/in for a callee referenced as a MemberReference by
    /// resolving it back to its MethodDef parameter rows — the case that matters
    /// is a same-assembly call on a generic type instance (TypeSpec parent),
    /// where the keyword would otherwise be dropped. Returns empty (the printer
    /// keeps its default spelling) when the declaring type is not a same-assembly
    /// definition or no signature-matching method is found.
    /// </summary>
    static ParameterRefKindResult MemberReferenceRefKinds(MetadataReader reader, MemberReference member, string memberName, ImmutableArray<TypeRef> parameterTypes)
    {
        bool anyByRef = false;
        foreach (var p in parameterTypes)
            if (p.Kind == TypeRefKind.ByRef) { anyByRef = true; break; }
        if (!anyByRef)
            return new ParameterRefKindResult([], ParameterRefKindFacts.NotRequired);
        if (DeclaringTypeDefinition(reader, member.Parent) is not { } typeHandle)
            return new ParameterRefKindResult([], ParameterRefKindFacts.Unknown);

        var memberSignature = reader.GetBlobBytes(member.Signature);
        foreach (var methodHandle in reader.GetTypeDefinition(typeHandle).GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) != memberName)
                continue;
            // A MemberRef's signature is encoded in the same generic-definition
            // terms (!0/!1) as the MethodDef's, so the blobs match byte-for-byte
            // for the referenced method — a robust overload-safe match.
            if (reader.GetBlobBytes(method.Signature).AsSpan().SequenceEqual(memberSignature))
                return MethodDefinitionFacts.ReadParameterRefKinds(reader, method, parameterTypes);
        }
        return new ParameterRefKindResult([], ParameterRefKindFacts.Unknown);
    }

    /// <summary>
    /// The same-assembly <see cref="TypeDefinitionHandle"/> a MemberRef's parent
    /// names: the parent directly, or the generic type definition behind a
    /// <c>GENERICINST</c> TypeSpecification. Null for a TypeReference (the type is
    /// in another assembly, so its parameter rows are not available here).
    /// </summary>
    static TypeDefinitionHandle? DeclaringTypeDefinition(MetadataReader reader, EntityHandle parent)
    {
        switch (parent.Kind)
        {
            case HandleKind.TypeDefinition:
                return (TypeDefinitionHandle)parent;
            case HandleKind.TypeSpecification:
                var blob = reader.GetBlobReader(reader.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);
                if (blob.ReadByte() != (byte)SignatureTypeCode.GenericTypeInstance)
                    return null;
                blob.ReadByte();  // ELEMENT_TYPE_CLASS / ELEMENT_TYPE_VALUETYPE
                var underlying = blob.ReadTypeHandle();
                return underlying.Kind == HandleKind.TypeDefinition ? (TypeDefinitionHandle)underlying : null;
            default:
                return null;
        }
    }

    static ArgumentRefKind ClassifyByRefParameter(MetadataReader reader, System.Reflection.Metadata.Parameter parameter)
    {
        // `in` (IsReadOnlyAttribute) and `ref readonly` (RequiresLocationAttribute)
        // both take a readonly reference; the call site spells them without a
        // mutable-ref keyword (treated as In — rendered bare), which is valid for
        // a readonly or rvalue argument. Both also set the raw In flag (as do
        // interop-marshalled `ref` params), so the flag cannot detect them.
        if (HasReadOnlyRefAttribute(reader, parameter.GetCustomAttributes()))
            return ArgumentRefKind.In;
        var attributes = parameter.Attributes;
        if ((attributes & System.Reflection.ParameterAttributes.Out) != 0
            && (attributes & System.Reflection.ParameterAttributes.In) == 0)
            return ArgumentRefKind.Out;
        return ArgumentRefKind.Ref;
    }

    // Pure-SRM attribute presence check (the decompiler Pipeline stays SRM-only,
    // so it does not reach into the Metadata AttributeReader).
    static bool HasReadOnlyRefAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var handle in attributes)
            if (AttributeTypeName(reader, reader.GetCustomAttribute(handle).Constructor)
                is ("System.Runtime.CompilerServices", "IsReadOnlyAttribute" or "RequiresLocationAttribute"))
                return true;
        return false;
    }

    // A module compiled with /features:updated-memory-safety-rules is stamped
    // with a module-level MemorySafetyRulesAttribute. That is the only metadata
    // difference between an old-rules and a new-rules build (an `unsafe` block or
    // member modifier has no IL representation), so it is what the printer keys
    // on to choose explicit `unsafe { }` blocks over the member modifier.
    static bool ModuleUsesUpdatedMemorySafetyRules(MetadataReader reader)
    {
        foreach (var handle in reader.GetModuleDefinition().GetCustomAttributes())
            if (AttributeTypeName(reader, reader.GetCustomAttribute(handle).Constructor)
                is ("System.Runtime.CompilerServices", "MemorySafetyRulesAttribute"))
                return true;
        return false;
    }

    // A member declared `unsafe`/`extern` under the updated memory-safety rules
    // is stamped with RequiresUnsafeAttribute (no IL difference otherwise), which
    // is what makes its every call site require an unsafe context.
    static bool HasRequiresUnsafeAttribute(MetadataReader reader, MethodDefinition method)
    {
        foreach (var handle in method.GetCustomAttributes())
            if (AttributeTypeName(reader, reader.GetCustomAttribute(handle).Constructor)
                is ("System.Diagnostics.CodeAnalysis", "RequiresUnsafeAttribute"))
                return true;
        return false;
    }

    static bool HasCompilerGeneratedAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var handle in attributes)
            if (AttributeTypeName(reader, reader.GetCustomAttribute(handle).Constructor)
                is ("System.Runtime.CompilerServices", "CompilerGeneratedAttribute"))
                return true;
        return false;
    }

    static (string Namespace, string Name) AttributeTypeName(MetadataReader reader, EntityHandle constructor)
    {
        switch (constructor.Kind)
        {
            case HandleKind.MemberReference
                when reader.GetMemberReference((MemberReferenceHandle)constructor).Parent is { Kind: HandleKind.TypeReference } parent:
                var typeRef = reader.GetTypeReference((TypeReferenceHandle)parent);
                return (reader.GetString(typeRef.Namespace), reader.GetString(typeRef.Name));
            case HandleKind.MethodDefinition:
                var declaring = reader.GetTypeDefinition(reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType());
                return (reader.GetString(declaring.Namespace), reader.GetString(declaring.Name));
            default:
                return ("", "");
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

    /// <summary>
    /// The mapped initial-value bytes of a field with an RVA — a
    /// <c>&lt;PrivateImplementationDetails&gt;</c> constant-data field a constant
    /// array/span initializer points at — or null when the field has no RVA. The
    /// length is the field's value-type size (the synthesized
    /// <c>__StaticArrayInitTypeSize=N</c> struct).
    /// </summary>
    static byte[]? TryReadFieldRvaData(MetadataSource source, FieldDefinitionHandle handle)
    {
        var field = source.Reader.GetFieldDefinition(handle);
        int rva = field.GetRelativeVirtualAddress();
        if (rva == 0)
            return null;
        int size = field.DecodeSignature(FieldDataSizeProvider.Instance, null);
        if (size <= 0)
            return null;
        try
        {
            var block = source.Pe.GetSectionData(rva);
            if (block.Length < size)
                return null;
            return block.GetContent(0, size).ToArray();
        }
        catch
        {
            return null;
        }
    }

    internal static TypeRef ResolveTypeToken(MetadataReader reader, EntityHandle handle, GenericScope callerScope) => handle.Kind switch
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

/// <summary>
/// Decodes a field signature to the byte size of its type — for an RVA constant
/// field that is the synthesized <c>__StaticArrayInitTypeSize=N</c> value type,
/// whose explicit <see cref="TypeLayout.Size"/> is the mapped blob length.
/// Only the size is needed, so every constructed form passes its element size
/// through and unknowns yield 0 (the caller then skips the field).
/// </summary>
sealed class FieldDataSizeProvider : ISignatureTypeProvider<int, object?>
{
    public static readonly FieldDataSizeProvider Instance = new();

    public int GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        => reader.GetTypeDefinition(handle).GetLayout().Size;
    public int GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => 0;
    public int GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => 0;
    public int GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Boolean or PrimitiveTypeCode.SByte or PrimitiveTypeCode.Byte => 1,
        PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16 or PrimitiveTypeCode.Char => 2,
        PrimitiveTypeCode.Int32 or PrimitiveTypeCode.UInt32 or PrimitiveTypeCode.Single => 4,
        PrimitiveTypeCode.Int64 or PrimitiveTypeCode.UInt64 or PrimitiveTypeCode.Double => 8,
        _ => 0,
    };
    public int GetSZArrayType(int elementType) => 0;
    public int GetArrayType(int elementType, ArrayShape shape) => 0;
    public int GetByReferenceType(int elementType) => 0;
    public int GetPointerType(int elementType) => 0;
    public int GetGenericInstantiation(int genericType, ImmutableArray<int> typeArguments) => 0;
    public int GetGenericMethodParameter(object? genericContext, int index) => 0;
    public int GetGenericTypeParameter(object? genericContext, int index) => 0;
    public int GetModifiedType(int modifier, int unmodifiedType, bool isRequired) => unmodifiedType;
    public int GetPinnedType(int elementType) => elementType;
    public int GetFunctionPointerType(MethodSignature<int> signature) => 0;
}
