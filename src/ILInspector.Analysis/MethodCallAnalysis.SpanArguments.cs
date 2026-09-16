using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

/// <summary>
/// Recognition of the two span lowerings the C# compiler emits for a
/// collection-expression argument, scoped to those exact shapes.
/// </summary>
/// <remarks>
/// This is compiler-lowering recognition, not a general span or alias analysis.
/// Anything outside the two recognized shapes — an extra address use, a
/// non-literal element index, an untrusted buffer type, a store the walk cannot
/// attribute — leaves the fact unresolved so the consumer fails closed.
/// </remarks>
internal static partial class MethodCallAnalysis
{
    sealed partial class StackValueSourceResolver
    {
        /// <summary>
        /// Element provenance for a span-shaped argument, or null when the
        /// argument was not produced by either recognized lowering.
        /// </summary>
        internal SpanArgumentElements? ResolveSpanArgument(
            int callOffset,
            int parameterCount,
            int argumentIndex)
        {
            if (!IsComplete)
                return null;

            ImmutableArray<StackValue> stack =
                _stack.StackBeforeOffset(callOffset);
            int stackIndex = stack.Length - parameterCount + argumentIndex;
            if (stackIndex < 0 || stackIndex >= stack.Length)
                return null;

            int producer = stack[stackIndex].ProducerOffset;
            if (producer == StackValue.NoProducer
                || !_callsByOffset.TryGetValue(
                    producer,
                    out DirectCall? shape))
            {
                return null;
            }

            if (shape.Kind == CallKind.NewObject
                && IsReadOnlySpanConstructor(shape.Callee))
            {
                return ResolveSingleElementSpan(argumentIndex, shape);
            }

            if (shape.Kind == CallKind.Call
                && IsInlineArrayAsReadOnlySpan(shape.Callee))
            {
                return ResolveInlineArraySpan(argumentIndex, shape);
            }

            return null;
        }

        static SpanArgumentElements Unresolved(int argumentIndex)
            => new(argumentIndex, ResolvedValueSets.Empty, IsResolved: false);

        SpanArgumentElements ResolveSingleElementSpan(
            int argumentIndex,
            DirectCall constructor)
        {
            ImmutableArray<StackValue> stack =
                _stack.StackBeforeOffset(constructor.ILOffset);
            if (stack.Length < 1)
                return Unresolved(argumentIndex);

            int addressOffset = stack[^1].ProducerOffset;
            if (!TryReadLocalAddressSlot(addressOffset, out int slot))
                return Unresolved(argumentIndex);

            LocalSlotUsage usage = UsageOfLocal(slot);
            if (usage.AddressOffsets.Length != 1
                || usage.AddressOffsets[0] != addressOffset
                || usage.StoreOffsets.Length != 1
                || !usage.LoadOffsets.IsEmpty)
            {
                return Unresolved(argumentIndex);
            }

            int storeOffset = usage.StoreOffsets[0];
            if (storeOffset >= addressOffset
                || !SameBlock(storeOffset, constructor.ILOffset)
                || !IsConsumedOnlyBy(addressOffset, constructor.ILOffset))
            {
                return Unresolved(argumentIndex);
            }

            ResolvedValueSet value = ResolveStackSlot(
                storeOffset,
                depthFromTop: 0);
            return value.IsResolved
                ? new(argumentIndex, new([value]), IsResolved: true)
                : Unresolved(argumentIndex);
        }

        SpanArgumentElements ResolveInlineArraySpan(
            int argumentIndex,
            DirectCall asSpan)
        {
            ImmutableArray<StackValue> stack =
                _stack.StackBeforeOffset(asSpan.ILOffset);
            if (stack.Length < 2)
                return Unresolved(argumentIndex);

            if (ResolveValue(stack[^1].ProducerOffset, []).Single is not
                {
                    Kind: ResolvedValueSourceKind.Int32Literal,
                } lengthLiteral)
            {
                return Unresolved(argumentIndex);
            }

            int length = lengthLiteral.Int32Value;
            int addressOffset = stack[^2].ProducerOffset;
            if (length <= 0
                || length > MaxRecognizedSpanElements
                || !TryReadLocalAddressSlot(addressOffset, out int slot)
                || !IsTrustedInlineArrayBuffer(slot, length))
            {
                return Unresolved(argumentIndex);
            }

            LocalSlotUsage usage = UsageOfLocal(slot);
            if (!usage.StoreOffsets.IsEmpty
                || !usage.LoadOffsets.IsEmpty
                || usage.AddressOffsets.Length != length + 2)
            {
                return Unresolved(argumentIndex);
            }

            var addresses = new HashSet<int>(usage.AddressOffsets);
            if (!addresses.Contains(addressOffset))
                return Unresolved(argumentIndex);

            var consumers = new Dictionary<int, int>
            {
                [addressOffset] = asSpan.ILOffset,
            };
            int initializations = 0;
            foreach (DecodedInstruction instruction
                in _context.Instructions.Instructions)
            {
                if (instruction.OpCode != ILOpCode.Initobj)
                    continue;
                ImmutableArray<StackValue> before =
                    _stack.StackBeforeOffset(instruction.Offset);
                if (before.Length < 1
                    || !addresses.Contains(before[^1].ProducerOffset))
                {
                    continue;
                }
                if (!consumers.TryAdd(
                        before[^1].ProducerOffset,
                        instruction.Offset))
                {
                    return Unresolved(argumentIndex);
                }
                initializations++;
            }

            if (initializations != 1)
                return Unresolved(argumentIndex);

            var elementRefs = new Dictionary<int, int>();
            foreach (DirectCall call in _callsByOffset.Values)
            {
                if (call.Kind != CallKind.Call
                    || !IsInlineArrayElementRef(call.Callee))
                {
                    continue;
                }
                ImmutableArray<StackValue> before =
                    _stack.StackBeforeOffset(call.ILOffset);
                if (before.Length < 2
                    || !addresses.Contains(before[^2].ProducerOffset))
                {
                    continue;
                }
                if (!consumers.TryAdd(before[^2].ProducerOffset, call.ILOffset)
                    || ResolveValue(before[^1].ProducerOffset, []).Single is not
                    {
                        Kind: ResolvedValueSourceKind.Int32Literal,
                    } index
                    || index.Int32Value < 0
                    || index.Int32Value >= length
                    || !elementRefs.TryAdd(index.Int32Value, call.ILOffset))
                {
                    return Unresolved(argumentIndex);
                }
            }

            if (consumers.Count != addresses.Count
                || elementRefs.Count != length)
            {
                return Unresolved(argumentIndex);
            }

            foreach ((int address, int consumer) in consumers)
            {
                if (!IsConsumedOnlyBy(address, consumer)
                    || !SameBlock(address, asSpan.ILOffset))
                {
                    return Unresolved(argumentIndex);
                }
            }

            var elements =
                ImmutableArray.CreateBuilder<ResolvedValueSet>(length);
            for (int index = 0; index < length; index++)
            {
                if (!elementRefs.TryGetValue(index, out int elementRef)
                    || ResolveElementStore(elementRef, asSpan.ILOffset) is not
                        { } value)
                {
                    return Unresolved(argumentIndex);
                }
                elements.Add(value);
            }

            return new(
                argumentIndex,
                new(elements.MoveToImmutable()),
                IsResolved: true);
        }

        /// <summary>
        /// The value written through one element reference, when exactly one
        /// reference-typed indirect store consumes it and nothing else does.
        /// </summary>
        ResolvedValueSet? ResolveElementStore(
            int elementRefOffset,
            int spanOffset)
        {
            int storeOffset = -1;
            foreach (DecodedInstruction instruction
                in _context.Instructions.Instructions)
            {
                if (instruction.OpCode is not (ILOpCode.Stind_ref
                    or ILOpCode.Stobj))
                {
                    continue;
                }
                ImmutableArray<StackValue> before =
                    _stack.StackBeforeOffset(instruction.Offset);
                if (before.Length < 2
                    || before[^2].ProducerOffset != elementRefOffset)
                {
                    continue;
                }
                if (storeOffset >= 0)
                    return null;
                storeOffset = instruction.Offset;
            }

            if (storeOffset < 0
                || storeOffset >= spanOffset
                || !SameBlock(storeOffset, spanOffset)
                || !IsConsumedOnlyBy(elementRefOffset, storeOffset))
            {
                return null;
            }

            ResolvedValueSet value = ResolveStackSlot(
                storeOffset,
                depthFromTop: 0);
            return value.IsResolved ? value : null;
        }

        /// <summary>
        /// True when the value produced at <paramref name="producerOffset"/>
        /// leaves the evaluation stack only at <paramref name="consumerOffset"/>
        /// and is never duplicated.
        /// </summary>
        bool IsConsumedOnlyBy(int producerOffset, int consumerOffset)
        {
            bool consumed = false;
            foreach (DecodedInstruction instruction
                in _context.Instructions.Instructions)
            {
                ImmutableArray<StackValue> before =
                    _stack.StackBeforeOffset(instruction.Offset);
                if (!Carries(before, producerOffset))
                    continue;
                if (instruction.OpCode == ILOpCode.Dup
                    && before.Length > 0
                    && before[^1].ProducerOffset == producerOffset)
                {
                    return false;
                }

                bool survives = !instruction.TerminatesBlock
                    && Carries(
                        _stack.StackBeforeOffset(instruction.NextOffset),
                        producerOffset);
                if (survives)
                    continue;
                if (instruction.Offset != consumerOffset)
                    return false;
                consumed = true;
            }

            return consumed;
        }

        static bool Carries(
            ImmutableArray<StackValue> stack,
            int producerOffset)
        {
            foreach (StackValue value in stack)
            {
                if (value.ProducerOffset == producerOffset)
                    return true;
            }

            return false;
        }

        bool SameBlock(int left, int right)
        {
            int leftBlock = _context.Blocks.BlockIndexAt(left);
            return _context.Blocks.IsComplete
                && leftBlock >= 0
                && leftBlock == _context.Blocks.BlockIndexAt(right);
        }

        bool TryReadLocalAddressSlot(int offset, out int slot)
        {
            slot = -1;
            if (offset == StackValue.NoProducer
                || _context.InstructionAt(offset)
                    is not { OpCode: ILOpCode.Ldloca_s or ILOpCode.Ldloca }
                        instruction)
            {
                return false;
            }

            slot = MethodInstructionFacts.OperandInt32(instruction);
            return true;
        }

        LocalSlotUsage UsageOfLocal(int slot)
        {
            var addresses = ImmutableArray.CreateBuilder<int>();
            var stores = ImmutableArray.CreateBuilder<int>();
            var loads = ImmutableArray.CreateBuilder<int>();
            foreach (DecodedInstruction instruction
                in _context.Instructions.Instructions)
            {
                if (instruction.OpCode is ILOpCode.Ldloca_s or ILOpCode.Ldloca)
                {
                    if (MethodInstructionFacts.OperandInt32(instruction)
                        == slot)
                    {
                        addresses.Add(instruction.Offset);
                    }
                    continue;
                }

                if (!MethodInstructionFacts.TryReadLocalSlot(
                        instruction,
                        out LocalSlotAccess access)
                    || access.IsArgument
                    || access.Slot != slot)
                {
                    continue;
                }

                (access.IsStore ? stores : loads).Add(instruction.Offset);
            }

            return new(
                addresses.ToImmutable(),
                stores.ToImmutable(),
                loads.ToImmutable());
        }

        /// <summary>
        /// True when the buffer local is the core library's
        /// <c>InlineArray{N}`1</c> for exactly <paramref name="length"/>
        /// elements. Anchoring to the trusted core library stops a same-assembly
        /// type from impersonating the compiler's buffer.
        /// </summary>
        bool IsTrustedInlineArrayBuffer(int slot, int length)
        {
            ImmutableArray<TypeRef> locals = _context.LocalTypes;
            if (slot < 0 || slot >= locals.Length)
                return false;

            TypeRef local = locals[slot];
            if (local.Kind != TypeRefKind.GenericInstance
                || local.TypeArguments.Length != 1
                || local.ElementType is not { } definition)
            {
                return false;
            }

            return definition.Kind == TypeRefKind.Definition
                && definition.Assembly == TypeRef.CoreLibrary
                && definition.TrustedFrameworkAssembly
                && definition.Namespace == "System.Runtime.CompilerServices"
                && definition.Name == $"InlineArray{length}`1";
        }

        static bool IsReadOnlySpanConstructor(MemberRef callee)
            => callee.Name == ".ctor"
                && callee.ParameterTypes.Length == 1
                && IsCoreLibGenericDefinition(
                    callee.DeclaringType,
                    "System",
                    "ReadOnlySpan`1");

        static bool IsInlineArrayAsReadOnlySpan(MemberRef callee)
            => callee.Name == "InlineArrayAsReadOnlySpan"
                && callee.ParameterTypes.Length == 2
                && IsCompilerGeneratedHelperOwner(callee.DeclaringType);

        static bool IsInlineArrayElementRef(MemberRef callee)
            => callee.Name == "InlineArrayElementRef"
                && callee.ParameterTypes.Length == 2
                && IsCompilerGeneratedHelperOwner(callee.DeclaringType);

        // The helpers themselves are emitted into the inspected assembly, so
        // their owner cannot be trusted by assembly identity. Trust instead
        // comes from the corelib InlineArray buffer type the lowering must use.
        static bool IsCompilerGeneratedHelperOwner(TypeRef declaringType)
            => declaringType.Kind == TypeRefKind.Definition
                && declaringType.Namespace.Length == 0
                && declaringType.Name == "<PrivateImplementationDetails>";

        static bool IsCoreLibGenericDefinition(
            TypeRef type,
            string ns,
            string name)
        {
            TypeRef definition = type.Kind == TypeRefKind.GenericInstance
                ? type.ElementType ?? type
                : type;
            return definition.Kind == TypeRefKind.Definition
                && definition.Assembly == TypeRef.CoreLibrary
                && definition.TrustedFrameworkAssembly
                && definition.Namespace == ns
                && definition.Name == name;
        }

        readonly record struct LocalSlotUsage(
            ImmutableArray<int> AddressOffsets,
            ImmutableArray<int> StoreOffsets,
            ImmutableArray<int> LoadOffsets);
    }

    /// <summary>
    /// Upper bound on a recognized span lowering's element count. The C#
    /// compiler emits an <c>InlineArrayN`1</c> buffer per length, so a
    /// pathological literal cannot drive unbounded work here.
    /// </summary>
    const int MaxRecognizedSpanElements = 64;
}
