using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.ControlFlow;
using ILInspector.Instructions;

namespace ILInspector.Analysis;

internal enum NewObjectConstructionKind
{
    Heap,
    NonHeap,
    UnresolvedExternalValueType,
}

/// <summary>
/// The metadata- and IL-dependent judgments <see cref="MethodAllocationAnalysis"/>
/// cannot make from the shared Layer-0 context. The assembly reader that owns the
/// metadata reader, the caller's generic scope, and the raw IL bytes implements
/// this, so none of them reach allocation analysis and no second decode or
/// metadata traversal path can appear here.
/// </summary>
/// <remarks>
/// Each member preserves its existing owner contract: resolution may return an
/// unsupported value or throw, while yes/no judgments answer conservatively.
/// </remarks>
internal interface IMethodAllocationResolver
{
    /// <summary>
    /// Resolves a type token (TypeDef/TypeRef/TypeSpec). Returns
    /// <see cref="TypeRef.Unsupported"/> for a malformed or unknown token.
    /// </summary>
    TypeRef ResolveType(int token);

    /// <summary>
    /// Resolves a method/constructor operand token using the shared member
    /// resolver, including its unsupported-member result for unknown shapes.
    /// </summary>
    MemberRef ResolveMember(int token);

    /// <summary>
    /// Classifies whether a <c>newobj</c> allocates, including the unresolved
    /// external value-type case that remains visible as an annotation.
    /// </summary>
    NewObjectConstructionKind ClassifyConstruction(
        int operandToken,
        TypeRef declaringType);

    /// <summary>True when the constructor operand is a delegate constructor.</summary>
    bool IsDelegateConstructor(int operandToken, MemberRef constructor);

    /// <summary>
    /// True when a <c>box</c> of this operand is a positively identified value
    /// type that unconditionally allocates.
    /// </summary>
    bool IsAllocatingValueTypeBox(int operandToken, TypeRef boxed);

    /// <summary>
    /// True when the token is an in-assembly reference type, used for the
    /// <c>newarr</c> element-size estimate.
    /// </summary>
    bool IsInAssemblyReferenceType(int typeToken);

    /// <summary>
    /// The declaring type and name behind a field-store operand, or
    /// <c>(null, null)</c> when it cannot be resolved.
    /// </summary>
    (TypeRef? DeclaringType, string? Name) ResolveFieldOwner(int fieldToken);

    /// <summary>
    /// Reaching definitions over the caller-owned raw IL. Throws when the body
    /// cannot be analyzed; escape classification then answers
    /// <see cref="AllocationEscape.Unknown"/>.
    /// </summary>
    ReachingDefinitionsResult AnalyzeReachingDefinitions();
}

/// <summary>
/// One method's allocation occurrences from a single scan: the discovered
/// occurrences that optimization-opportunity collection reuses, and the
/// escape-refined occurrences the allocation output publishes. Discovery may
/// already identify an allocation on a throw path.
/// </summary>
internal sealed record MethodAllocationResult(
    ImmutableArray<AllocationOccurrence> DiscoveredOccurrences,
    ImmutableArray<AllocationOccurrence> ClassifiedOccurrences)
{
    internal static readonly MethodAllocationResult Empty = new([], []);
}

/// <summary>
/// Owns allocation interpretation for one decoded method body: where allocations
/// occur, what shape they are, how often a call executes them, and whether the
/// produced value escapes.
/// </summary>
/// <remarks>
/// <para>
/// It consumes the shared <see cref="MethodBodyAnalysisContext"/> and reuses its
/// canonical <see cref="MethodInstructions"/>, exception-region-aware blocks, and
/// loop regions; it never decodes IL, builds a block graph, recomputes loop
/// regions, or decodes a local signature again.
/// </para>
/// <para>
/// The path-context, path-confidence, and post-dominance indexes are private
/// Layer-1 interpretations: they are allocation's reading of the shared control
/// flow, not neutral context facts, so they stay here. Optimization-opportunity
/// collection and call-site acquisition consume them through the query methods
/// rather than rebuilding them.
/// </para>
/// </remarks>
internal sealed class MethodAllocationAnalysis
{
    readonly MethodBodyAnalysisContext _context;
    readonly AllocationPathContextIndex _pathContexts;
    readonly AllocationPathConfidenceIndex _pathConfidences;
    readonly AllocationPostDominanceIndex _postDominances;

    internal MethodAllocationAnalysis(MethodBodyAnalysisContext context)
    {
        _context = context;
        _pathContexts = AllocationPathContextIndex.Create(context);
        _pathConfidences =
            AllocationPathConfidenceIndex.Create(context, _pathContexts);
        _postDominances =
            AllocationPostDominanceIndex.Create(context, _pathContexts);
    }

    /// <summary>
    /// Scans the body once for allocation occurrences and returns both the raw
    /// occurrences and their escape-refined form. One scan feeds both the
    /// published allocation facts and optimization-opportunity collection.
    /// </summary>
    internal MethodAllocationResult Collect(IMethodAllocationResolver resolver)
    {
        var raw = CollectOccurrences(resolver);
        return new MethodAllocationResult(
            raw,
            raw.IsDefaultOrEmpty ? raw : ClassifyEscapes(raw, resolver));
    }

    internal AllocationPathContext PathContextAt(
        int offset,
        AllocationEscape escape = AllocationEscape.Unknown)
    {
        if (escape == AllocationEscape.ThrowPath)
            return AllocationPathContext.ErrorPath;
        int blockIndex = _context.Blocks.BlockIndexAt(offset);
        var blockContext = _pathContexts.ContextFor(blockIndex);
        if (blockContext == AllocationPathContext.ErrorPath)
            return AllocationPathContext.ErrorPath;
        if (_context.IsInLoopRegion(offset))
            return AllocationPathContext.LoopBody;

        return blockContext;
    }

    internal AllocationPathConfidence PathConfidenceAt(
        int offset,
        AllocationEscape escape = AllocationEscape.Unknown)
    {
        if (escape == AllocationEscape.ThrowPath)
            return AllocationPathConfidence.Unknown;
        int blockIndex = _context.Blocks.BlockIndexAt(offset);
        return _pathConfidences.ConfidenceFor(blockIndex);
    }

    internal AllocationPostDominance PostDominanceAt(
        int offset,
        AllocationEscape escape = AllocationEscape.Unknown)
    {
        if (escape == AllocationEscape.ThrowPath)
            return AllocationPostDominance.Unknown;
        int blockIndex = _context.Blocks.BlockIndexAt(offset);
        return _postDominances.PostDominanceFor(blockIndex);
    }

    // Per-invocation multiplicity, consolidated from the existing path axes.
    // Precedence favors soundness (never confidently wrong) over completeness:
    //   post-dominates return -> the block reaches a return with NO loop backedge,
    //     so it runs at most once (Once if it also dominates return, else Conditional).
    //     This correctly demotes a `return new T()` early-exit inside a loop.
    //   thrown value in a loop -> ambiguous (caught-in-loop iterates N times,
    //     uncaught exits after one) so fail-honest Unknown; a non-loop throw is
    //     Conditional (runs 0/1).
    //   loop body        -> Loop (0..N per call; N is not statically resolved)
    //   error path       -> Conditional (runs only when the exception fires)
    //   dominates-return -> Once
    //   behind a branch  -> Conditional
    //   otherwise        -> Unknown (fail-honest)
    internal AllocationMultiplicity MultiplicityAt(
        int offset,
        AllocationEscape escape = AllocationEscape.Unknown)
    {
        int blockIndex = _context.Blocks.BlockIndexAt(offset);
        var confidence = _pathConfidences.ConfidenceFor(blockIndex);

        // Reaches a return without cycling back -> at most one execution per call,
        // even when the block's IL offset happens to sit inside a loop region.
        if (_postDominances.PostDominanceFor(blockIndex) == AllocationPostDominance.ReturnPostDominates)
            return confidence == AllocationPathConfidence.DominatesReturn
                ? AllocationMultiplicity.Once
                : AllocationMultiplicity.Conditional;

        bool inLoop = _context.IsInLoopRegion(offset);
        if (escape == AllocationEscape.ThrowPath)
            return inLoop ? AllocationMultiplicity.Unknown : AllocationMultiplicity.Conditional;
        if (inLoop)
        {
            // A block inside a loop region only iterates if it can flow back to the
            // loop backedge. One that exits first — an early return OR an uncaught
            // throw after the allocation — runs at most once, so it is not a hot loop.
            if (!_postDominances.ReachesCycleFor(blockIndex))
                return confidence == AllocationPathConfidence.DominatesReturn
                    ? AllocationMultiplicity.Once
                    : AllocationMultiplicity.Conditional;
            return AllocationMultiplicity.Loop;
        }

        var context = _pathContexts.ContextFor(blockIndex);
        if (context == AllocationPathContext.ErrorPath)
            return AllocationMultiplicity.Conditional;
        if (confidence == AllocationPathConfidence.DominatesReturn)
            return AllocationMultiplicity.Once;
        if (confidence == AllocationPathConfidence.BehindBranch
            || context is AllocationPathContext.Branch or AllocationPathContext.SwitchArm)
            return AllocationMultiplicity.Conditional;

        return AllocationMultiplicity.Unknown;
    }

    ImmutableArray<AllocationOccurrence> CollectOccurrences(
        IMethodAllocationResolver resolver)
    {
        var caller = _context.Method;
        var occurrences = ImmutableArray.CreateBuilder<AllocationOccurrence>();
        ILOpCode previousOpcode = default;
        int? pendingArrayLength = null;
        int pendingArrayLengthBlock = -1;
        foreach (var instruction in _context.Instructions.Instructions)
        {
            int offset = instruction.Offset;
            var opcode = instruction.OpCode;
            try
            {
                switch (opcode)
                {
                    case ILOpCode.Ldc_i4_m1:
                    case ILOpCode.Ldc_i4_0:
                    case ILOpCode.Ldc_i4_1:
                    case ILOpCode.Ldc_i4_2:
                    case ILOpCode.Ldc_i4_3:
                    case ILOpCode.Ldc_i4_4:
                    case ILOpCode.Ldc_i4_5:
                    case ILOpCode.Ldc_i4_6:
                    case ILOpCode.Ldc_i4_7:
                    case ILOpCode.Ldc_i4_8:
                        SetPendingArrayLength(
                            opcode switch
                            {
                                ILOpCode.Ldc_i4_m1 => -1,
                                ILOpCode.Ldc_i4_0 => 0,
                                ILOpCode.Ldc_i4_1 => 1,
                                ILOpCode.Ldc_i4_2 => 2,
                                ILOpCode.Ldc_i4_3 => 3,
                                ILOpCode.Ldc_i4_4 => 4,
                                ILOpCode.Ldc_i4_5 => 5,
                                ILOpCode.Ldc_i4_6 => 6,
                                ILOpCode.Ldc_i4_7 => 7,
                                _ => 8,
                            },
                            offset);
                        break;
                    case ILOpCode.Ldc_i4_s:
                        SetPendingArrayLength((int)instruction.OperandValue, offset);
                        break;
                    case ILOpCode.Ldc_i4:
                        SetPendingArrayLength(MethodInstructionFacts.OperandInt32(instruction), offset);
                        break;
                    case ILOpCode.Newarr:
                    {
                        int token = MethodInstructionFacts.OperandInt32(instruction);
                        var element = resolver.ResolveType(token);
                        var array = TypeRef.SzArray(element);
                        var (estimatedSizeBytes, sizeTier) = EstimateNewarrSize(resolver, element, token, ValidPendingArrayLength(offset));
                        occurrences.Add(MakeAllocation(
                            caller, offset, token, AllocationKind.Array, array, array.ToDisplayString(), countsAsHeapAllocation: true,
                            AllocationFrequency.Always, _context.IsInLoopRegion(offset),
                            AllocationEscape.Unknown, AllocationFactSource.Newarr,
                            estimatedSizeBytes, sizeTier));
                        ClearPendingArrayLength();
                        break;
                    }
                    case ILOpCode.Newobj:
                    {
                        ClearPendingArrayLength();
                        int token = MethodInstructionFacts.OperandInt32(instruction);
                        var constructor = resolver.ResolveMember(token);
                        switch (resolver.ClassifyConstruction(
                            token,
                            constructor.DeclaringType))
                        {
                            case NewObjectConstructionKind.UnresolvedExternalValueType:
                                occurrences.Add(MakeAllocation(
                                    caller, offset, token, AllocationKind.Object, constructor.DeclaringType, LegacyDetail(constructor.DeclaringType, AllocationKind.Object), countsAsHeapAllocation: false,
                                    AllocationFrequency.Always, _context.IsInLoopRegion(offset),
                                    AllocationEscape.Unknown, AllocationFactSource.Newobj));
                                break;
                            case NewObjectConstructionKind.Heap:
                                if (ClassifyNewObjectAllocation(
                                    offset,
                                    instruction.NextOffset,
                                    token,
                                    constructor) is { } occurrence)
                                {
                                    occurrences.Add(occurrence);
                                }
                                break;
                        }
                        break;
                    }
                    case ILOpCode.Call:
                    case ILOpCode.Callvirt:
                    {
                        ClearPendingArrayLength();
                        int token = MethodInstructionFacts.OperandInt32(instruction);
                        var callee = resolver.ResolveMember(token);
                        if (RepeatedScanAnalysis.IsInterfaceEnumeratorAllocation(callee))
                        {
                            occurrences.Add(MakeAllocation(
                                caller, offset, token, AllocationKind.Enumerator, callee.ReturnType, callee.ReturnType.ToDisplayString(), countsAsHeapAllocation: false,
                                AllocationFrequency.Always, _context.IsInLoopRegion(offset),
                                AllocationEscape.Unknown, AllocationFactSource.GetEnumeratorCall));
                        }
                        break;
                    }
                    case ILOpCode.Box:
                    {
                        ClearPendingArrayLength();
                        int token = MethodInstructionFacts.OperandInt32(instruction);
                        var boxed = resolver.ResolveType(token);
                        occurrences.Add(MakeAllocation(
                            caller, offset, token, AllocationKind.Box, boxed, boxed.ToDisplayString(),
                            countsAsHeapAllocation: resolver.IsAllocatingValueTypeBox(token, boxed),
                            AllocationFrequency.Always, _context.IsInLoopRegion(offset),
                            MethodBodyFlowProbe.BoxFeedsThrowSoon(
                                _context.Instructions,
                                instruction.NextOffset)
                                ? AllocationEscape.ThrowPath
                                : AllocationEscape.Unknown,
                            AllocationFactSource.Box));
                        break;
                    }
                    default:
                        ClearPendingArrayLength();
                        break;
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException or IndexOutOfRangeException)
            {
                break;
            }

            if (opcode != ILOpCode.Nop)
                previousOpcode = opcode;
        }
        return occurrences.ToImmutable();

        void SetPendingArrayLength(int length, int instructionOffset)
        {
            pendingArrayLength = length;
            pendingArrayLengthBlock = _context.Blocks.BlockIndexAt(instructionOffset);
        }

        void ClearPendingArrayLength()
        {
            pendingArrayLength = null;
            pendingArrayLengthBlock = -1;
        }

        int? ValidPendingArrayLength(int newarrOffset)
            => pendingArrayLength is { } length
                && _context.Blocks.IsComplete
                && pendingArrayLengthBlock >= 0
                && pendingArrayLengthBlock == _context.Blocks.BlockIndexAt(newarrOffset)
                ? length
                : null;

        AllocationOccurrence? ClassifyNewObjectAllocation(
            int newObjectOffset,
            int afterNewObjectPosition,
            int operandToken,
            MemberRef constructor)
        {
            var type = constructor.DeclaringType;
            if (type.Kind is TypeRefKind.SzArray or TypeRefKind.Array)
            {
                return MakeAllocation(
                    caller, newObjectOffset, operandToken, AllocationKind.Array, type, LegacyDetail(type, AllocationKind.Array), countsAsHeapAllocation: true,
                    AllocationFrequency.Always, _context.IsInLoopRegion(newObjectOffset),
                    AllocationEscape.Unknown, AllocationFactSource.Newobj);
            }

            AllocationKind kind = AllocationKind.Object;
            var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType ?? type : type;
            string name = definition.Name;
            if (resolver.IsDelegateConstructor(operandToken, constructor))
                kind = AllocationKind.Delegate;
            else if (name.Contains("c__DisplayClass", StringComparison.Ordinal))
                kind = AllocationKind.Closure;
            else if (name.Contains(CompilerGeneratedNames.StateMachineInfix, StringComparison.Ordinal))
                kind = AllocationKind.StateMachine;

            bool followsFunctionPointer = previousOpcode is ILOpCode.Ldftn or ILOpCode.Ldvirtftn;
            if (kind == AllocationKind.Delegate && !followsFunctionPointer)
                kind = AllocationKind.Object;

            var frequency = kind == AllocationKind.Delegate && DelegateNewObjectIsCachedOnce(newObjectOffset, afterNewObjectPosition)
                ? AllocationFrequency.CachedOnce
                : AllocationFrequency.Always;
            return MakeAllocation(
                caller, newObjectOffset, operandToken, kind, type, LegacyDetail(type, kind), countsAsHeapAllocation: true,
                frequency, _context.IsInLoopRegion(newObjectOffset),
                MethodBodyFlowProbe.NewObjectFeedsThrowSoon(
                    _context.Instructions,
                    afterNewObjectPosition)
                    ? AllocationEscape.ThrowPath
                    : AllocationEscape.Unknown,
                AllocationFactSource.Newobj);
        }
    }

    AllocationOccurrence MakeAllocation(
        MethodIdentity method,
        int offset,
        int? operandToken,
        AllocationKind kind,
        TypeRef? allocatedType,
        string? detail,
        bool countsAsHeapAllocation,
        AllocationFrequency frequency,
        bool inLoop,
        AllocationEscape escape,
        AllocationFactSource source,
        int? estimatedSizeBytes = null,
        AllocationSizeTier sizeTier = AllocationSizeTier.Unknown)
        => new(
            method,
            offset,
            operandToken,
            kind,
            allocatedType,
            detail,
            countsAsHeapAllocation,
            frequency,
            inLoop,
            escape,
            source,
            RuntimeAllocationType(kind, allocatedType),
            PathContextAt(offset, escape),
            PathConfidenceAt(offset, escape),
            estimatedSizeBytes,
            sizeTier)
        {
            PostDominance = PostDominanceAt(offset, escape),
            Multiplicity = MultiplicityAt(offset, escape),
            ChurnedType = ChurnedTypeFor(kind, allocatedType),
        };

    static string LegacyDetail(TypeRef type, AllocationKind kind)
    {
        if (kind is AllocationKind.Closure or AllocationKind.StateMachine)
            return LeafDisplayName(type);
        return type.ToDisplayString();
    }

    static string LeafDisplayName(TypeRef type)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType ?? type : type;
        string name = definition.Name;
        int nested = name.LastIndexOf('+');
        if (nested >= 0)
            name = name[(nested + 1)..];
        int arity = name.IndexOf('`');
        return arity >= 0 ? name[..arity] : name;
    }

    bool DelegateNewObjectIsCachedOnce(int newObjectOffset, int afterNewObjectPosition)
    {
        try
        {
            if (!TryFindDelegateCacheProbe(newObjectOffset, out int probeFieldToken, out int branchTarget))
                return false;
            return TryReadDelegateCacheStore(afterNewObjectPosition, out int storeFieldToken, out int storeOffset)
                && storeFieldToken == probeFieldToken
                && branchTarget > storeOffset;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException or IndexOutOfRangeException)
        {
            return false;
        }
    }

    bool TryFindDelegateCacheProbe(int newObjectOffset, out int fieldToken, out int branchTarget)
    {
        fieldToken = 0;
        branchTarget = -1;
        foreach (var instruction in _context.Instructions.Instructions)
        {
            if (instruction.Offset >= newObjectOffset)
                break;
            if (instruction.OpCode == ILOpCode.Ldsfld)
            {
                int candidateToken = MethodInstructionFacts.OperandInt32(instruction);
                if (TryReadLdsfldDupBranchOverOffset(instruction.NextOffset, newObjectOffset, out int candidateBranchTarget))
                {
                    fieldToken = candidateToken;
                    branchTarget = candidateBranchTarget;
                }
            }
        }
        return fieldToken != 0;
    }

    bool TryReadLdsfldDupBranchOverOffset(int position, int targetOffset, out int branchTarget)
    {
        branchTarget = -1;
        var instructions = _context.Instructions.Instructions;
        int dupIndex = _context.NextNonNopIndexAtOrAfter(position);
        if (dupIndex >= instructions.Length)
            return false;
        var dup = instructions[dupIndex];
        if (dup.OpCode != ILOpCode.Dup)
            return false;
        int branchIndex = _context.NextNonNopIndexAtOrAfter(dup.NextOffset);
        if (branchIndex >= instructions.Length)
            return false;
        var branch = instructions[branchIndex];
        return branch.OpCode is ILOpCode.Brtrue or ILOpCode.Brtrue_s
            && MethodInstructionFacts.TrySingleBranchTarget(branch, out branchTarget)
            && branchTarget > targetOffset;
    }

    bool TryReadDelegateCacheStore(int position, out int fieldToken, out int storeOffset)
    {
        fieldToken = 0;
        storeOffset = -1;
        var instructions = _context.Instructions.Instructions;
        int index = _context.NextNonNopIndexAtOrAfter(position);
        if (index >= instructions.Length)
            return false;
        var instruction = instructions[index];
        storeOffset = instruction.Offset;
        if (instruction.OpCode == ILOpCode.Dup)
        {
            index = _context.NextNonNopIndexAtOrAfter(instruction.NextOffset);
            if (index >= instructions.Length)
                return false;
            instruction = instructions[index];
            storeOffset = instruction.Offset;
        }
        if (instruction.OpCode != ILOpCode.Stsfld)
            return false;
        fieldToken = MethodInstructionFacts.OperandInt32(instruction);
        return true;
    }

    const int X64SzArrayHeaderBytes = 24;
    const int X64ReferenceOrPointerElementBytes = 8;
    const int X64ObjectAlignmentBytes = 8;

    // Exact size estimates are calibrated to the x64 managed object layout:
    // 8-byte object header + 8-byte method-table pointer + 4-byte length padded to 24,
    // then element payload rounded up to the 8-byte allocation quantum.
    static (int? Size, AllocationSizeTier Tier) EstimateNewarrSize(
        IMethodAllocationResolver resolver,
        TypeRef element,
        int elementToken,
        int? length)
    {
        if (length is null or < 0)
            return (null, AllocationSizeTier.Unknown);
        if (!TryGetNewarrElementSize(resolver, element, elementToken, out int elementSize))
            return (null, AllocationSizeTier.Unknown);

        long rawSize = X64SzArrayHeaderBytes + (long)length.Value * elementSize;
        long alignedSize = AlignUp(rawSize, X64ObjectAlignmentBytes);
        return alignedSize <= int.MaxValue
            ? ((int)alignedSize, AllocationSizeTier.Exact)
            : (null, AllocationSizeTier.Unknown);
    }

    static bool TryGetNewarrElementSize(
        IMethodAllocationResolver resolver,
        TypeRef element,
        int elementToken,
        out int size)
    {
        if (TryGetPrimitiveElementSize(element, out size))
            return true;
        if (element.Kind is TypeRefKind.Pointer or TypeRefKind.SzArray or TypeRefKind.Array)
        {
            size = X64ReferenceOrPointerElementBytes;
            return true;
        }
        if (element.Kind != TypeRefKind.Definition)
        {
            size = 0;
            return false;
        }
        if (IsKnownCoreLibraryReferenceElement(element)
            || resolver.IsInAssemblyReferenceType(elementToken))
        {
            size = X64ReferenceOrPointerElementBytes;
            return true;
        }

        size = 0;
        return false;
    }

    static bool TryGetPrimitiveElementSize(TypeRef element, out int size)
    {
        if (element.Kind != TypeRefKind.Definition || element.Namespace != "System")
        {
            size = 0;
            return false;
        }

        size = element.Name switch
        {
            "Boolean" or "Byte" or "SByte" => 1,
            "Char" or "Int16" or "UInt16" => 2,
            "Int32" or "UInt32" or "Single" => 4,
            "Int64" or "UInt64" or "Double" or "IntPtr" or "UIntPtr" => 8,
            _ => 0,
        };
        return size != 0 && FrameworkIdentity.IsCoreLibraryType(element, "System", element.Name);
    }

    static bool IsKnownCoreLibraryReferenceElement(TypeRef element)
        => FrameworkIdentity.IsCoreLibraryType(element, "System", "Object")
           || FrameworkIdentity.IsCoreLibraryType(element, "System", "String")
           || FrameworkIdentity.IsCoreLibraryType(element, "System", "Type");

    static long AlignUp(long value, int alignment)
        => (value + alignment - 1) / alignment * alignment;

    static string? RuntimeAllocationType(AllocationKind kind, TypeRef? allocatedType)
    {
        if (allocatedType is null)
            return kind == AllocationKind.Object ? "object" : null;
        return kind switch
        {
            AllocationKind.Box => $"boxed {RuntimeTypeName(allocatedType)}",
            AllocationKind.Closure => $"display class ({RuntimeTypeName(allocatedType)})",
            AllocationKind.StateMachine => $"state machine ({RuntimeTypeName(allocatedType)})",
            _ => RuntimeTypeName(allocatedType),
        };
    }

    // The backing array a growable collection churns as it resizes — the type that
    // actually allocates at runtime, distinct from the collection object itself. Only
    // the single-backing-array collections are reported; Dictionary/HashSet grow
    // multiple internal arrays (buckets + entries) so they stay fail-honest null.
    static string? ChurnedTypeFor(AllocationKind kind, TypeRef? allocatedType)
    {
        if (kind != AllocationKind.Object || allocatedType is null)
            return null;

        if (FrameworkIdentity.IsKnownFrameworkType(allocatedType, "System.Text", "System.Text", "StringBuilder"))
            return "System.Char[]";

        if (allocatedType.Kind == TypeRefKind.GenericInstance
            && allocatedType.TypeArguments.Length == 1
            && (FrameworkIdentity.IsKnownFrameworkType(allocatedType, "System.Collections", "System.Collections.Generic", "List`1")
                || FrameworkIdentity.IsKnownFrameworkType(allocatedType, "System.Collections", "System.Collections.Generic", "Queue`1")
                || FrameworkIdentity.IsKnownFrameworkType(allocatedType, "System.Collections", "System.Collections.Generic", "Stack`1")))
            return $"{RuntimeTypeName(allocatedType.TypeArguments[0])}[]";

        return null;
    }

    static string RuntimeTypeName(TypeRef type)
        => type.Kind switch
        {
            TypeRefKind.Definition => type.Namespace.Length == 0 ? StripMetadataGenericArity(type.Name) : $"{type.Namespace}.{StripMetadataGenericArity(type.Name)}",
            TypeRefKind.GenericInstance => $"{RuntimeTypeName(type.ElementType ?? type)}<{string.Join(", ", type.TypeArguments.Select(RuntimeTypeName))}>",
            TypeRefKind.SzArray => $"{RuntimeTypeName(type.ElementType!)}[]",
            TypeRefKind.Array => $"{RuntimeTypeName(type.ElementType!)}[{new string(',', type.Rank - 1)}]",
            TypeRefKind.ByRef => $"ref {RuntimeTypeName(type.ElementType!)}",
            TypeRefKind.Pointer => $"{RuntimeTypeName(type.ElementType!)}*",
            _ => type.ToQualifiedDisplayString(),
        };

    static string StripMetadataGenericArity(string name)
    {
        if (!name.Contains('`', StringComparison.Ordinal))
            return name;
        return string.Join("+", name.Split('+').Select(segment =>
        {
            int tick = segment.IndexOf('`');
            return tick < 0 ? segment : segment[..tick];
        }));
    }

    ImmutableArray<AllocationOccurrence> ClassifyEscapes(
        ImmutableArray<AllocationOccurrence> occurrences,
        IMethodAllocationResolver resolver)
    {
        ReachingDefinitionsResult? reachingDefinitions = null;
        bool reachingDefinitionsAttempted = false;

        ReachingDefinitionsResult? GetReachingDefinitions()
        {
            if (!reachingDefinitionsAttempted)
            {
                reachingDefinitionsAttempted = true;
                try
                {
                    reachingDefinitions = resolver.AnalyzeReachingDefinitions();
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException or IndexOutOfRangeException)
                {
                    reachingDefinitions = null;
                }
            }
            return reachingDefinitions;
        }

        var builder = ImmutableArray.CreateBuilder<AllocationOccurrence>(occurrences.Length);
        foreach (var occurrence in occurrences)
        {
            if (occurrence.Escape != AllocationEscape.Unknown
                || _context.InstructionAt(occurrence.ILOffset) is not { } instruction)
            {
                builder.Add(occurrence);
                continue;
            }

            var escape = ClassifyProducedValueEscape(
                GetReachingDefinitions,
                resolver,
                instruction.NextOffset,
                occurrence.Kind,
                occurrence.AllocatedType);

            builder.Add(escape.Escape == AllocationEscape.Unknown
                ? occurrence
                : occurrence with
                {
                    Escape = escape.Escape,
                    EscapeKind = escape.Escape == AllocationEscape.Escapes ? escape.Kind : AllocationEscapeKind.None,
                    PathContext = escape.Escape == AllocationEscape.ThrowPath ? AllocationPathContext.ErrorPath : occurrence.PathContext,
                    PathConfidence = escape.Escape == AllocationEscape.ThrowPath ? AllocationPathConfidence.Unknown : occurrence.PathConfidence,
                    PostDominance = escape.Escape == AllocationEscape.ThrowPath ? AllocationPostDominance.Unknown : occurrence.PostDominance,
                    Multiplicity = escape.Escape == AllocationEscape.ThrowPath
                        ? (occurrence.Multiplicity == AllocationMultiplicity.Loop ? AllocationMultiplicity.Unknown : AllocationMultiplicity.Conditional)
                        : occurrence.Multiplicity,
                });
        }
        return builder.MoveToImmutable();
    }

    // Verdict plus the objective refinement of WHERE an Escapes value escapes.
    // Kind is only meaningful when Escape == Escapes; otherwise it stays None.
    readonly record struct EscapeClassification(AllocationEscape Escape, AllocationEscapeKind Kind)
    {
        public static readonly EscapeClassification Unknown = new(AllocationEscape.Unknown, AllocationEscapeKind.None);
        public static readonly EscapeClassification LocalOnly = new(AllocationEscape.LocalOnly, AllocationEscapeKind.None);
        public static readonly EscapeClassification ThrowPath = new(AllocationEscape.ThrowPath, AllocationEscapeKind.None);
        public static EscapeClassification Escapes(AllocationEscapeKind kind) => new(AllocationEscape.Escapes, kind);
    }

    EscapeClassification ClassifyProducedValueEscape(
        Func<ReachingDefinitionsResult?> reachingDefinitionsProvider,
        IMethodAllocationResolver resolver,
        int positionAfterValue,
        AllocationKind kind,
        TypeRef? allocatedType)
        => ClassifyStackValueUse(reachingDefinitionsProvider, resolver, positionAfterValue, kind, allocatedType, []);

    EscapeClassification ClassifyDefinitionEscape(
        ReachingDefinitionsResult reachingDefinitions,
        Func<ReachingDefinitionsResult?> reachingDefinitionsProvider,
        IMethodAllocationResolver resolver,
        LocalDefinition definition,
        AllocationKind kind,
        TypeRef? allocatedType,
        HashSet<int> visitingDefinitions)
    {
        if (!reachingDefinitions.IsComplete)
            return EscapeClassification.Unknown;
        if (!visitingDefinitions.Add(definition.Id))
            return EscapeClassification.Unknown;

        var verdict = EscapeClassification.LocalOnly;
        foreach (var use in reachingDefinitions.UsesOf(definition))
        {
            EscapeClassification useEscape;
            if (use.Address)
            {
                useEscape = EscapeClassification.Escapes(AllocationEscapeKind.None);
            }
            else if (TryPositionAfterLoadSlot(use.Offset, use.Slot, use.IsArgument, out int positionAfterLoad))
            {
                useEscape = ClassifyStackValueUse(
                    reachingDefinitionsProvider,
                    resolver,
                    positionAfterLoad,
                    kind,
                    allocatedType,
                    visitingDefinitions);
            }
            else
            {
                useEscape = EscapeClassification.Unknown;
            }

            verdict = JoinEscape(verdict, useEscape);
            // Escapes(None) is absorbing under JoinEscape (further merges keep it
            // Escapes/None), so we can stop. A single-kind Escapes must keep scanning
            // the remaining uses so a conflicting sink can fail honest to None — the
            // verdict stays Escapes either way, only the kind can degrade.
            if (verdict.Escape == AllocationEscape.Escapes && verdict.Kind == AllocationEscapeKind.None)
                break;
        }

        visitingDefinitions.Remove(definition.Id);
        return verdict;
    }

    EscapeClassification ClassifyStackValueUse(
        Func<ReachingDefinitionsResult?> reachingDefinitionsProvider,
        IMethodAllocationResolver resolver,
        int position,
        AllocationKind kind,
        TypeRef? allocatedType,
        HashSet<int> visitingDefinitions)
    {
        try
        {
            var instructions = _context.Instructions.Instructions;
            int index = _context.NextNonNopIndexAtOrAfter(position);
            if (index >= instructions.Length)
                return EscapeClassification.LocalOnly;

            var instruction = instructions[index];
            if (TryReadStoreSlotDefinition(instruction, out var storeAccess))
            {
                var reachingDefinitions = reachingDefinitionsProvider();
                if (reachingDefinitions is null || !reachingDefinitions.IsComplete)
                    return EscapeClassification.Unknown;
                var definition = reachingDefinitions.Definitions.FirstOrDefault(def =>
                    def.IsArgument == storeAccess.IsArgument
                    && def.Slot == storeAccess.Slot
                    && def.Offset == instruction.Offset);
                return definition is null
                    ? EscapeClassification.Unknown
                    : ClassifyDefinitionEscape(
                        reachingDefinitions,
                        reachingDefinitionsProvider,
                        resolver,
                        definition,
                        kind,
                        allocatedType,
                        visitingDefinitions);
            }

            if (kind == AllocationKind.Array)
                return ClassifyArrayStackValueUse(resolver, index, allocatedType);

            return ClassifyImmediateConsumer(resolver, instruction, kind, allocatedType, stackValuesAbove: 0);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException or IndexOutOfRangeException)
        {
            return EscapeClassification.Unknown;
        }
    }

    EscapeClassification ClassifyArrayStackValueUse(
        IMethodAllocationResolver resolver,
        int startIndex,
        TypeRef? allocatedType)
    {
        var instructions = _context.Instructions.Instructions;
        int stackValuesAbove = 0;
        for (int index = startIndex; index < instructions.Length; index++)
        {
            var instruction = instructions[index];
            var opcode = instruction.OpCode;
            switch (opcode)
            {
                case ILOpCode.Nop:
                    continue;
                case ILOpCode.Ldc_i4_m1 or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
                    or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6
                    or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8 or ILOpCode.Ldnull
                    or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4 or ILOpCode.Ldc_r4
                    or ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8 or ILOpCode.Ldstr
                    or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3
                    or ILOpCode.Ldloc_s or ILOpCode.Ldloc or ILOpCode.Ldloca_s or ILOpCode.Ldloca
                    or ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3
                    or ILOpCode.Ldarg_s or ILOpCode.Ldarg or ILOpCode.Ldarga_s or ILOpCode.Ldarga:
                    stackValuesAbove++;
                    continue;
                case ILOpCode.Pop:
                    if (stackValuesAbove == 0)
                        return EscapeClassification.LocalOnly;
                    stackValuesAbove--;
                    continue;
                case ILOpCode.Ldlen:
                    return stackValuesAbove == 0 ? EscapeClassification.LocalOnly : EscapeClassification.Unknown;
                case ILOpCode.Ldelem or ILOpCode.Ldelem_i or ILOpCode.Ldelem_i1 or ILOpCode.Ldelem_i2
                    or ILOpCode.Ldelem_i4 or ILOpCode.Ldelem_i8 or ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8
                    or ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_u4 or ILOpCode.Ldelem_ref:
                    return stackValuesAbove == 1 ? EscapeClassification.LocalOnly : EscapeClassification.Unknown;
                case ILOpCode.Stelem or ILOpCode.Stelem_i or ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2
                    or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8 or ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8
                    or ILOpCode.Stelem_ref:
                    return stackValuesAbove switch
                    {
                        0 => EscapeClassification.Escapes(AllocationEscapeKind.Collection),
                        2 => EscapeClassification.LocalOnly,
                        _ => EscapeClassification.Unknown,
                    };
                default:
                    return ClassifyImmediateConsumer(resolver, instruction, AllocationKind.Array, allocatedType, stackValuesAbove);
            }
        }
        return EscapeClassification.Unknown;
    }

    EscapeClassification ClassifyImmediateConsumer(
        IMethodAllocationResolver resolver,
        DecodedInstruction instruction,
        AllocationKind kind,
        TypeRef? allocatedType,
        int stackValuesAbove)
    {
        if (stackValuesAbove != 0)
            return EscapeClassification.Unknown;

        if (CompilerGeneratedNames.IsDisplayClass(allocatedType)
            && TryClassifyDisplayClassDelegateTarget(resolver, instruction.Offset, out var displayClassEscape))
        {
            return displayClassEscape;
        }

        switch (instruction.OpCode)
        {
            case ILOpCode.Pop:
                return EscapeClassification.LocalOnly;
            case ILOpCode.Ret:
                return EscapeClassification.Escapes(AllocationEscapeKind.Return);
            case ILOpCode.Throw:
                return EscapeClassification.ThrowPath;
            case ILOpCode.Stfld:
                return EscapeClassification.Escapes(ClassifyFieldStoreEscapeKind(resolver, instruction));
            case ILOpCode.Stsfld:
                return EscapeClassification.Escapes(AllocationEscapeKind.Static);
            case ILOpCode.Stelem:
            case ILOpCode.Stelem_i:
            case ILOpCode.Stelem_i1:
            case ILOpCode.Stelem_i2:
            case ILOpCode.Stelem_i4:
            case ILOpCode.Stelem_i8:
            case ILOpCode.Stelem_r4:
            case ILOpCode.Stelem_r8:
            case ILOpCode.Stelem_ref:
                // Collection detection is limited to single-dim array element stores.
                // List<T>.Add / multidim Set(...) are calls (fall through to Unknown),
                // and span element stores lower to stobj/stind (Escapes(None) below):
                // those stay fail-honest rather than being labelled Collection.
                return EscapeClassification.Escapes(AllocationEscapeKind.Collection);
            case ILOpCode.Stobj:
            case ILOpCode.Stind_i:
            case ILOpCode.Stind_i1:
            case ILOpCode.Stind_i2:
            case ILOpCode.Stind_i4:
            case ILOpCode.Stind_i8:
            case ILOpCode.Stind_r4:
            case ILOpCode.Stind_r8:
            case ILOpCode.Stind_ref:
                return EscapeClassification.Escapes(AllocationEscapeKind.None);
            case ILOpCode.Unbox_any:
                return kind == AllocationKind.Box ? EscapeClassification.LocalOnly : EscapeClassification.Unknown;
            case ILOpCode.Call:
            case ILOpCode.Callvirt:
            case ILOpCode.Newobj:
            {
                int token = MethodInstructionFacts.OperandInt32(instruction);
                var callee = resolver.ResolveMember(token);
                return IsSpanSafeLocalSink(callee, allocatedType)
                    ? EscapeClassification.LocalOnly
                    : EscapeClassification.Unknown;
            }
            default:
                return EscapeClassification.Unknown;
        }
    }

    bool TryClassifyDisplayClassDelegateTarget(
        IMethodAllocationResolver resolver,
        int position,
        out EscapeClassification classification)
    {
        classification = EscapeClassification.Unknown;
        var instructions = _context.Instructions.Instructions;
        int index = _context.NextNonNopIndexAtOrAfter(position);
        if (index >= instructions.Length)
            return false;

        // Track only copies of the display-class allocation (true) vs unrelated stack
        // values (false); fail closed as soon as a shape needs fuller stack semantics.
        var stack = new List<bool> { true };
        const int MaxScanInstructions = 48;
        int maxIndex = Math.Min(instructions.Length, index + MaxScanInstructions);
        for (; index < maxIndex; index++)
        {
            var instruction = instructions[index];
            if (instruction.Branches || instruction.Exits)
                return false;

            switch (instruction.OpCode)
            {
                case ILOpCode.Nop:
                    continue;
                case ILOpCode.Dup:
                    if (stack.Count == 0)
                        return false;
                    stack.Add(stack[^1]);
                    continue;
                case ILOpCode.Stfld:
                    if (!Pop(stack, 2))
                        return false;
                    if (!stack.Contains(true))
                        return false;
                    continue;
                case ILOpCode.Ldfld:
                    if (!Pop(stack, 1))
                        return false;
                    stack.Add(false);
                    continue;
                case ILOpCode.Ldftn:
                    stack.Add(false);
                    if (StackTopIsDelegateTarget(stack)
                        && NextNonNopIsDelegateConstructor(resolver, instruction.NextOffset))
                    {
                        classification = EscapeClassification.Escapes(AllocationEscapeKind.Capture);
                        return true;
                    }
                    return false;
                case ILOpCode.Ldvirtftn:
                    if (!Pop(stack, 1))
                        return false;
                    stack.Add(false);
                    if (StackTopIsDelegateTarget(stack)
                        && NextNonNopIsDelegateConstructor(resolver, instruction.NextOffset))
                    {
                        classification = EscapeClassification.Escapes(AllocationEscapeKind.Capture);
                        return true;
                    }
                    return false;
                default:
                    if (IsSimpleBinaryStackReplacement(instruction.OpCode))
                    {
                        if (!PopUntracked(stack, 2))
                            return false;
                        stack.Add(false);
                        continue;
                    }
                    if (IsSimpleStackPush(instruction.OpCode))
                    {
                        stack.Add(false);
                        continue;
                    }
                    return false;
            }
        }

        return false;

        static bool Pop(List<bool> stack, int count)
        {
            if (stack.Count < count)
                return false;
            stack.RemoveRange(stack.Count - count, count);
            return true;
        }

        static bool PopUntracked(List<bool> stack, int count)
        {
            if (stack.Count < count)
                return false;
            for (int i = stack.Count - count; i < stack.Count; i++)
            {
                if (stack[i])
                    return false;
            }
            stack.RemoveRange(stack.Count - count, count);
            return true;
        }

        static bool StackTopIsDelegateTarget(List<bool> stack)
            => stack.Count >= 2 && !stack[^1] && stack[^2];
    }

    bool NextNonNopIsDelegateConstructor(
        IMethodAllocationResolver resolver,
        int position)
    {
        var instructions = _context.Instructions.Instructions;
        int index = _context.NextNonNopIndexAtOrAfter(position);
        if (index >= instructions.Length)
            return false;

        var instruction = instructions[index];
        if (instruction.OpCode != ILOpCode.Newobj)
            return false;

        int token = MethodInstructionFacts.OperandInt32(instruction);
        var constructor = resolver.ResolveMember(token);
        return resolver.IsDelegateConstructor(token, constructor);
    }

    static bool IsSimpleStackPush(ILOpCode opcode)
        => opcode is ILOpCode.Ldc_i4_m1 or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
            or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6
            or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8 or ILOpCode.Ldnull
            or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4 or ILOpCode.Ldc_r4
            or ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8 or ILOpCode.Ldstr
            or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3
            or ILOpCode.Ldloc_s or ILOpCode.Ldloc or ILOpCode.Ldloca_s or ILOpCode.Ldloca
            or ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3
            or ILOpCode.Ldarg_s or ILOpCode.Ldarg or ILOpCode.Ldarga_s or ILOpCode.Ldarga;

    static bool IsSimpleBinaryStackReplacement(ILOpCode opcode)
        => opcode is ILOpCode.Add or ILOpCode.Add_ovf or ILOpCode.Add_ovf_un
            or ILOpCode.Sub or ILOpCode.Sub_ovf or ILOpCode.Sub_ovf_un
            or ILOpCode.Mul or ILOpCode.Mul_ovf or ILOpCode.Mul_ovf_un
            or ILOpCode.Div or ILOpCode.Div_un or ILOpCode.Rem or ILOpCode.Rem_un
            or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor
            or ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Shr_un
            or ILOpCode.Ceq or ILOpCode.Cgt or ILOpCode.Cgt_un or ILOpCode.Clt or ILOpCode.Clt_un;

    // stfld into a compiler-generated closure display class (<>c__DisplayClass) or
    // async/iterator state machine (<...>d__) hoists the value into that object's
    // lifetime; report it as a capture escape. The iterator result field
    // `<>2__current` is the exception: it holds the yielded value exposed to the
    // consumer (not a captured local), and its promotion is genuinely ambiguous,
    // so it stays fail-honest None. Any other/unresolvable field store is a plain
    // field escape (fail-honest).
    static AllocationEscapeKind ClassifyFieldStoreEscapeKind(
        IMethodAllocationResolver resolver,
        DecodedInstruction instruction)
    {
        try
        {
            var (declaring, fieldName) = resolver.ResolveFieldOwner(
                MethodInstructionFacts.OperandInt32(instruction));
            if (declaring is null)
                return AllocationEscapeKind.Field;

            string leaf = CompilerGeneratedNames.LeafName(declaring);
            // Match only the compiler-generated unspeakable names: closures are
            // `<>c__DisplayClass...` and iterator/async state machines are `<...>d__...`.
            // Both contain '<'/'>' which a user-defined type name cannot, so this
            // never fires on a real user type that merely echoes the suffix.
            bool isClosure = leaf.Contains(CompilerGeneratedNames.DisplayClassPrefix, StringComparison.Ordinal);
            bool isStateMachine = leaf.Contains(CompilerGeneratedNames.StateMachineInfix, StringComparison.Ordinal);
            if (!isClosure && !isStateMachine)
                return AllocationEscapeKind.Field;

            // The yielded value stored into the iterator's `<>2__current` is exposed
            // to the consumer, not a hoisted capture; don't over-claim capture.
            if (isStateMachine && fieldName == "<>2__current")
                return AllocationEscapeKind.None;

            return AllocationEscapeKind.Capture;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException or IndexOutOfRangeException)
        {
            return AllocationEscapeKind.Field;
        }
    }

    static EscapeClassification JoinEscape(EscapeClassification left, EscapeClassification right)
    {
        if (left.Escape == AllocationEscape.Escapes && right.Escape == AllocationEscape.Escapes)
            return EscapeClassification.Escapes(left.Kind == right.Kind ? left.Kind : AllocationEscapeKind.None);
        if (left.Escape == AllocationEscape.Escapes)
            return left;
        if (right.Escape == AllocationEscape.Escapes)
            return right;
        if (left.Escape == AllocationEscape.Unknown || right.Escape == AllocationEscape.Unknown)
            return EscapeClassification.Unknown;
        if (left.Escape == AllocationEscape.ThrowPath || right.Escape == AllocationEscape.ThrowPath)
            return EscapeClassification.ThrowPath;
        return EscapeClassification.LocalOnly;
    }

    static bool TryReadStoreSlotDefinition(DecodedInstruction instruction, out LocalSlotAccess access)
    {
        if (MethodInstructionFacts.TryReadLocalSlot(
                instruction,
                out access)
            && access.IsStore)
            return true;
        return false;
    }

    bool TryPositionAfterLoadSlot(int offset, int slot, bool isArgument, out int positionAfterLoad)
    {
        positionAfterLoad = offset;
        if (_context.InstructionAt(offset) is not { } instruction
            || !MethodInstructionFacts.TryReadLocalSlot(
                instruction,
                out var access)
            || access.IsStore
            || access.IsArgument != isArgument
            || access.Slot != slot)
        {
            return false;
        }
        positionAfterLoad = instruction.NextOffset;
        return true;
    }

    static bool IsSpanSafeLocalSink(MemberRef member, TypeRef? allocatedType)
    {
        if (member.Kind == MemberKind.Unsupported)
            return false;

        if (FrameworkIdentity.IsKnownFrameworkType(member.DeclaringType, "System.Text", "System.Text", "StringBuilder")
            && member.Name is "Append" or "AppendLine")
        {
            return member.ParameterTypes.Any(parameter =>
                IsStringType(parameter)
                || IsReadOnlySpanOfChar(parameter)
                || IsSpanOfChar(parameter));
        }

        if (member.Name is "AppendFormatted" or "AppendLiteral"
            && IsTrustedFrameworkInterpolatedStringHandler(member.DeclaringType)
            && member.DeclaringType.Name.Contains("InterpolatedStringHandler", StringComparison.Ordinal))
        {
            return member.ParameterTypes.Any(parameter =>
                IsStringType(parameter)
                || IsReadOnlySpanOfChar(parameter)
                || IsSpanOfChar(parameter));
        }

        if (member.Name == "TryParse"
            && member.ParameterTypes.Any(IsReadOnlySpanOfChar)
            && IsPrimitiveParseDeclaringType(member.DeclaringType))
        {
            return true;
        }

        return allocatedType is not null
            && IsMemoryExtensionsLocalSink(member)
            && member.ParameterTypes.Any(parameter => SameTypeIgnoringByRef(parameter, allocatedType));
    }

    static bool IsStringType(TypeRef type)
        => FrameworkIdentity.IsCoreLibraryType(type, "System", "String");

    static bool IsPrimitiveParseDeclaringType(TypeRef type)
        => FrameworkIdentity.IsCoreLibraryType(type, "System", "Boolean")
            || FrameworkIdentity.IsCoreLibraryType(type, "System", "Byte")
            || FrameworkIdentity.IsCoreLibraryType(type, "System", "SByte")
            || FrameworkIdentity.IsCoreLibraryType(type, "System", "Int16")
            || FrameworkIdentity.IsCoreLibraryType(type, "System", "UInt16")
            || FrameworkIdentity.IsCoreLibraryType(type, "System", "Int32")
            || FrameworkIdentity.IsCoreLibraryType(type, "System", "UInt32")
            || FrameworkIdentity.IsCoreLibraryType(type, "System", "Int64")
            || FrameworkIdentity.IsCoreLibraryType(type, "System", "UInt64")
            || FrameworkIdentity.IsCoreLibraryType(type, "System", "Single")
            || FrameworkIdentity.IsCoreLibraryType(type, "System", "Double")
            || FrameworkIdentity.IsCoreLibraryType(type, "System", "Decimal")
            || FrameworkIdentity.IsCoreLibraryType(type, "System", "DateTime")
            || FrameworkIdentity.IsCoreLibraryType(type, "System", "DateTimeOffset")
            || FrameworkIdentity.IsCoreLibraryType(type, "System", "Guid");

    static bool IsTrustedFrameworkInterpolatedStringHandler(TypeRef type)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType ?? type : type;
        return definition.TrustedFrameworkAssembly
            && definition.Namespace is "System.Runtime.CompilerServices" or "System.Text";
    }

    static bool IsReadOnlySpanOfChar(TypeRef type)
        => IsSpanDefinition(type, "ReadOnlySpan", "Char");

    static bool IsSpanOfChar(TypeRef type)
        => IsSpanDefinition(type, "Span", "Char");

    static bool IsSpanDefinition(TypeRef type, string spanName, string elementName)
    {
        if (type.Kind != TypeRefKind.GenericInstance || type.TypeArguments.Length != 1)
            return false;
        var definition = type.ElementType;
        return definition is not null
            && FrameworkIdentity.IsCoreLibraryType(definition, "System", spanName + "`1")
            && FrameworkIdentity.IsCoreLibraryType(type.TypeArguments[0], "System", elementName);
    }

    static bool IsMemoryExtensionsLocalSink(MemberRef member)
    {
        if (!FrameworkIdentity.IsCoreLibraryType(member.DeclaringType, "System", "MemoryExtensions"))
            return false;
        return member.Name is "IndexOf" or "LastIndexOf" or "SequenceEqual"
            or "StartsWith" or "EndsWith" or "Contains";
    }

    static bool SameTypeIgnoringByRef(TypeRef parameter, TypeRef value)
        => (parameter.Kind == TypeRefKind.ByRef ? parameter.ElementType ?? parameter : parameter).Equals(value);

    sealed class AllocationPathContextIndex
    {
        readonly bool[] _branchBlocks;
        readonly bool[] _switchArmBlocks;
        readonly bool[] _errorPathBlocks;

        AllocationPathContextIndex(bool[] branchBlocks, bool[] switchArmBlocks, bool[] errorPathBlocks)
        {
            _branchBlocks = branchBlocks;
            _switchArmBlocks = switchArmBlocks;
            _errorPathBlocks = errorPathBlocks;
        }

        public static AllocationPathContextIndex Create(MethodBodyAnalysisContext context)
        {
            var blockGraph = context.Blocks;
            var branchBlocks = new bool[blockGraph.Blocks.Length];
            var switchArmBlocks = new bool[blockGraph.Blocks.Length];
            var errorPathBlocks = new bool[blockGraph.Blocks.Length];
            var lastByBlock = LastInstructionsByBlock(context);
            var predecessorCounts = PredecessorCounts(blockGraph);

            foreach (var region in blockGraph.Regions)
            {
                switch (region.Kind)
                {
                    case HandlerKind.Catch:
                    case HandlerKind.Fault:
                        MarkBlocksInRange(blockGraph, errorPathBlocks, region.HandlerStart, region.HandlerEnd);
                        break;
                    case HandlerKind.Filter:
                        MarkBlocksInRange(blockGraph, errorPathBlocks, region.FilterStart, region.FilterEnd);
                        MarkBlocksInRange(blockGraph, errorPathBlocks, region.HandlerStart, region.HandlerEnd);
                        break;
                }
            }

            for (int blockIndex = 0; blockIndex < lastByBlock.Length; blockIndex++)
            {
                var terminator = lastByBlock[blockIndex];
                if (terminator is null || !terminator.Branches)
                    continue;

                if (terminator.OpCode == ILOpCode.Switch)
                {
                    foreach (int target in terminator.BranchTargets)
                        MarkSwitchArm(blockGraph, switchArmBlocks, predecessorCounts, target);
                    if (terminator.FallsThrough)
                    {
                        int defaultBlock = blockGraph.BlockIndexAt(terminator.NextOffset);
                        MarkSwitchArm(blockGraph, switchArmBlocks, predecessorCounts, terminator.NextOffset);
                        if (defaultBlock >= 0
                            && lastByBlock[defaultBlock] is { IsUnconditionalBranch: true, BranchTargets.Length: 1 } redirect)
                        {
                            MarkSwitchArm(blockGraph, switchArmBlocks, predecessorCounts, redirect.BranchTargets[0]);
                        }
                    }
                    continue;
                }

                if (terminator.IsUnconditionalBranch)
                    continue;

                foreach (int target in terminator.BranchTargets)
                    MarkBranchArm(blockGraph, branchBlocks, predecessorCounts, target);
                if (terminator.FallsThrough)
                    MarkBranchArm(blockGraph, branchBlocks, predecessorCounts, terminator.NextOffset);
            }

            return new AllocationPathContextIndex(branchBlocks, switchArmBlocks, errorPathBlocks);
        }

        public AllocationPathContext ContextFor(int blockIndex)
        {
            if ((uint)blockIndex >= (uint)_branchBlocks.Length)
                return AllocationPathContext.StraightLine;
            if (_errorPathBlocks[blockIndex])
                return AllocationPathContext.ErrorPath;
            if (_switchArmBlocks[blockIndex])
                return AllocationPathContext.SwitchArm;
            if (_branchBlocks[blockIndex])
                return AllocationPathContext.Branch;
            return AllocationPathContext.StraightLine;
        }

        static DecodedInstruction?[] LastInstructionsByBlock(MethodBodyAnalysisContext context)
        {
            var blockGraph = context.Blocks;
            var lastByBlock = new DecodedInstruction?[blockGraph.Blocks.Length];
            int cursor = 0;
            foreach (var instruction in context.Instructions.Instructions)
            {
                while (cursor + 1 < blockGraph.Blocks.Length
                    && instruction.Offset >= blockGraph.Blocks[cursor + 1].Start)
                {
                    cursor++;
                }
                if ((uint)cursor < (uint)lastByBlock.Length)
                    lastByBlock[cursor] = instruction;
            }
            return lastByBlock;
        }

        static int[] PredecessorCounts(BlockGraph blockGraph)
        {
            var counts = new int[blockGraph.Blocks.Length];
            foreach (var block in blockGraph.Blocks)
            {
                foreach (int successor in block.Edges.Successors)
                    if ((uint)successor < (uint)counts.Length)
                        counts[successor]++;
            }
            return counts;
        }

        static void MarkBlocksInRange(BlockGraph blockGraph, bool[] targets, int start, int end)
        {
            for (int i = 0; i < blockGraph.Blocks.Length; i++)
            {
                var block = blockGraph.Blocks[i];
                if (block.Start < end && block.End > start)
                    targets[i] = true;
            }
        }

        static void MarkSwitchArm(BlockGraph blockGraph, bool[] switchArmBlocks, int[] predecessorCounts, int offset)
        {
            int blockIndex = blockGraph.BlockIndexAt(offset);
            if ((uint)blockIndex < (uint)switchArmBlocks.Length && predecessorCounts[blockIndex] <= 1)
                switchArmBlocks[blockIndex] = true;
        }

        static void MarkBranchArm(BlockGraph blockGraph, bool[] branchBlocks, int[] predecessorCounts, int offset)
        {
            int blockIndex = blockGraph.BlockIndexAt(offset);
            if ((uint)blockIndex < (uint)branchBlocks.Length && predecessorCounts[blockIndex] <= 1)
                branchBlocks[blockIndex] = true;
        }
    }

    static int[] ReturnBlocks(MethodBodyAnalysisContext context)
    {
        var returns = new List<int>();
        foreach (var instruction in context.Instructions.Instructions)
        {
            if (instruction.OpCode != ILOpCode.Ret)
                continue;
            int blockIndex = context.Blocks.BlockIndexAt(instruction.Offset);
            if (blockIndex >= 0)
                returns.Add(blockIndex);
        }
        return [.. returns.Distinct().Order()];
    }

    sealed class AllocationPathConfidenceIndex
    {
        readonly AllocationPathConfidence[] _confidenceByBlock;

        AllocationPathConfidenceIndex(AllocationPathConfidence[] confidenceByBlock)
        {
            _confidenceByBlock = confidenceByBlock;
        }

        public static AllocationPathConfidenceIndex Create(
            MethodBodyAnalysisContext context,
            AllocationPathContextIndex pathContexts)
        {
            var blockGraph = context.Blocks;
            var confidenceByBlock = new AllocationPathConfidence[blockGraph.Blocks.Length];
            if (blockGraph.Blocks.Length == 0)
                return new AllocationPathConfidenceIndex(confidenceByBlock);

            var edges = blockGraph.Blocks.Select(static block => block.Edges).ToArray();
            var dominators = Dominators.Of(edges);
            var returnBlocks = ReturnBlocks(context);
            for (int blockIndex = 0; blockIndex < blockGraph.Blocks.Length; blockIndex++)
            {
                var pathContext = pathContexts.ContextFor(blockIndex);
                if (pathContext == AllocationPathContext.ErrorPath)
                    continue;
                if (pathContext is AllocationPathContext.Branch or AllocationPathContext.SwitchArm)
                {
                    confidenceByBlock[blockIndex] = AllocationPathConfidence.BehindBranch;
                    continue;
                }

                if (returnBlocks.Length > 0
                    && returnBlocks.All(returnBlock => dominators.Dominates(blockIndex, returnBlock)))
                {
                    confidenceByBlock[blockIndex] = AllocationPathConfidence.DominatesReturn;
                }
            }
            return new AllocationPathConfidenceIndex(confidenceByBlock);
        }

        public AllocationPathConfidence ConfidenceFor(int blockIndex)
            => (uint)blockIndex < (uint)_confidenceByBlock.Length
                ? _confidenceByBlock[blockIndex]
                : AllocationPathConfidence.Unknown;
    }

    sealed class AllocationPostDominanceIndex
    {
        readonly AllocationPostDominance[] _postDominanceByBlock;
        readonly bool[] _reachesCycleByBlock;

        AllocationPostDominanceIndex(AllocationPostDominance[] postDominanceByBlock, bool[] reachesCycleByBlock)
        {
            _postDominanceByBlock = postDominanceByBlock;
            _reachesCycleByBlock = reachesCycleByBlock;
        }

        public static AllocationPostDominanceIndex Create(
            MethodBodyAnalysisContext context,
            AllocationPathContextIndex pathContexts)
        {
            var blockGraph = context.Blocks;
            var postDominanceByBlock = new AllocationPostDominance[blockGraph.Blocks.Length];
            if (blockGraph.Blocks.Length == 0)
                return new AllocationPostDominanceIndex(postDominanceByBlock, []);

            var edges = blockGraph.Blocks.Select(static block => block.Edges).ToArray();
            var reachesCycleOnly = BackwardReachable(edges, CyclicBlocks(edges));
            var postDominators = PostDominators.Of(edges);
            var returnBlocks = ReturnBlocks(context);
            if (returnBlocks.Length == 0)
                return new AllocationPostDominanceIndex(postDominanceByBlock, reachesCycleOnly);

            var reachesReturn = BackwardReachable(edges, returnBlocks);
            var returnSet = returnBlocks.ToHashSet();
            var reachesNonReturnExit = BackwardReachable(edges, Enumerable.Range(0, edges.Length)
                .Where(block => !returnSet.Contains(block)
                    && (edges[block].ExitsMethod
                        || edges[block].ExternalTargets.Count > 0
                        || edges[block].LeavesRegion)));
            var reachesNonExitingBlock = BackwardReachable(edges, Enumerable.Range(0, edges.Length)
                .Where(block => postDominators.ImmediatePostDominator(block) == PostDominators.None));
            var reachesCycle = reachesCycleOnly;

            for (int blockIndex = 0; blockIndex < blockGraph.Blocks.Length; blockIndex++)
            {
                if (pathContexts.ContextFor(blockIndex) == AllocationPathContext.ErrorPath)
                    continue;
                if (reachesReturn[blockIndex]
                    && !reachesNonReturnExit[blockIndex]
                    && !reachesNonExitingBlock[blockIndex]
                    && !reachesCycle[blockIndex])
                {
                    postDominanceByBlock[blockIndex] = AllocationPostDominance.ReturnPostDominates;
                }
            }

            return new AllocationPostDominanceIndex(postDominanceByBlock, reachesCycle);
        }

        public AllocationPostDominance PostDominanceFor(int blockIndex)
            => (uint)blockIndex < (uint)_postDominanceByBlock.Length
                ? _postDominanceByBlock[blockIndex]
                : AllocationPostDominance.Unknown;

        // Whether control can flow from this block back to a loop backedge. A block
        // inside a loop region that CANNOT (it exits first via return/throw) runs at
        // most once per call and is not a hot loop.
        public bool ReachesCycleFor(int blockIndex)
            => (uint)blockIndex < (uint)_reachesCycleByBlock.Length && _reachesCycleByBlock[blockIndex];

        static bool[] BackwardReachable(IReadOnlyList<BlockEdges> edges, IEnumerable<int> seeds)
        {
            var reachable = new bool[edges.Count];
            var predecessors = new List<int>[edges.Count];
            for (int i = 0; i < predecessors.Length; i++)
                predecessors[i] = [];
            for (int from = 0; from < edges.Count; from++)
                foreach (int to in edges[from].Successors)
                    if ((uint)to < (uint)predecessors.Length)
                        predecessors[to].Add(from);

            var stack = new Stack<int>();
            foreach (int seed in seeds)
            {
                if ((uint)seed >= (uint)reachable.Length || reachable[seed])
                    continue;
                reachable[seed] = true;
                stack.Push(seed);
            }

            while (stack.Count > 0)
            {
                int block = stack.Pop();
                foreach (int predecessor in predecessors[block])
                {
                    if (reachable[predecessor])
                        continue;
                    reachable[predecessor] = true;
                    stack.Push(predecessor);
                }
            }

            return reachable;
        }

        static IEnumerable<int> CyclicBlocks(IReadOnlyList<BlockEdges> edges)
        {
            var state = new byte[edges.Count]; // 0 = unvisited, 1 = active, 2 = done
            var activePath = new List<int>();
            var activePosition = new int[edges.Count];
            Array.Fill(activePosition, -1);
            var cyclic = new bool[edges.Count];

            for (int root = 0; root < edges.Count; root++)
            {
                if (state[root] != 0)
                    continue;

                var frames = new Stack<(int Block, int NextSuccessor)>();
                Enter(root, frames);
                while (frames.Count > 0)
                {
                    var (block, nextSuccessor) = frames.Pop();
                    var successors = edges[block].Successors;
                    if (nextSuccessor >= successors.Count)
                    {
                        state[block] = 2;
                        activePosition[block] = -1;
                        activePath.RemoveAt(activePath.Count - 1);
                        continue;
                    }

                    frames.Push((block, nextSuccessor + 1));
                    int successor = successors[nextSuccessor];
                    if ((uint)successor >= (uint)edges.Count)
                        continue;

                    if (state[successor] == 0)
                    {
                        Enter(successor, frames);
                        continue;
                    }

                    if (state[successor] == 1 && activePosition[successor] >= 0)
                    {
                        for (int i = activePosition[successor]; i < activePath.Count; i++)
                            cyclic[activePath[i]] = true;
                    }
                }
            }

            return Enumerable.Range(0, cyclic.Length).Where(block => cyclic[block]).ToArray();

            void Enter(int block, Stack<(int Block, int NextSuccessor)> frames)
            {
                state[block] = 1;
                activePosition[block] = activePath.Count;
                activePath.Add(block);
                frames.Push((block, 0));
            }
        }
    }
}
