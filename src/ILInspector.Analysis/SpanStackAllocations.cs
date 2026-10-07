using System.Collections.Immutable;
using System.Reflection.Metadata;
using ILInspector.Instructions;

namespace ILInspector.Analysis;

/// <summary>
/// Recognizes <c>stackalloc</c> lowered into <c>Span&lt;T&gt;</c> on the shared typed
/// stack. Producer provenance follows the allocation pointer through Roslyn's
/// initializer lowerings — including element expressions with branches, calls, and
/// conversions — to the <c>Span&lt;T&gt;(void*, int)</c> constructor, and proves which
/// stores are in-bounds initializer writes into that same allocation.
/// </summary>
/// <remarks>
/// Fail-closed: an incomplete typed stack, an unresolved producer, or an unprovable
/// displacement or bound recognizes nothing, so the reported roles stay.
/// </remarks>
internal sealed record SpanStackAllocations(
    ImmutableHashSet<int> WrappedAllocations,
    ImmutableHashSet<int> InitializerStores)
{
    const int MaxTraceDepth = 32;

    internal static SpanStackAllocations None { get; } = new([], []);

    internal static SpanStackAllocations Recognize(
        MethodBodyAnalysisContext context,
        Func<int, MemberRef> resolveMember)
    {
        TypedStackResult stack;
        try
        {
            stack = context.Instructions.InterpretStack(
                !context.Method.ReturnType.Equals(
                    TypeRef.CoreLib("System", "Void")),
                new MemberStackTypeResolver(resolveMember));
        }
        catch (Exception ex)
            when (ex is BadImageFormatException
                or InvalidOperationException
                or ArgumentException
                or OverflowException)
        {
            return None;
        }
        if (!stack.IsComplete)
            return None;

        var instructions = context.Instructions.Instructions
            .ToDictionary(instruction => instruction.Offset);
        var tracer = new Tracer(stack, instructions);

        var wrapped = new Dictionary<int, TypeRef>();
        foreach (DecodedInstruction instruction in instructions.Values)
        {
            if (instruction.OpCode != ILOpCode.Newobj)
                continue;
            MemberRef constructor = resolveMember(
                checked((int)instruction.OperandValue));
            if (SpanElementType(constructor) is not { } elementType
                || ArgumentAt(stack, instruction.Offset, 2, 0)
                    is not { } pointer
                || tracer.Address(pointer, 0) is not (int allocation, Linear displacement)
                || displacement != Linear.Zero)
            {
                continue;
            }
            wrapped[allocation] = elementType;
        }
        if (wrapped.Count == 0)
            return None;

        var stores = ImmutableHashSet.CreateBuilder<int>();
        foreach (DecodedInstruction instruction in instructions.Values)
        {
            if (!IsStore(instruction.OpCode)
                || ArgumentAt(stack, instruction.Offset, 2, 0)
                    is not { } address
                || tracer.Address(address, 0)
                    is not (int allocation, Linear displacement)
                || !wrapped.TryGetValue(allocation, out TypeRef? elementType)
                || ArgumentAt(stack, allocation, 1, 0) is not { } sizeValue
                || tracer.Evaluate(sizeValue, 0, exact: true)
                    is not { } size
                || StoreWidth(instruction, elementType, size)
                    is not { } width
                || !Linear.FitsWithin(displacement, width, size))
            {
                continue;
            }
            stores.Add(instruction.Offset);
        }
        return new(
            wrapped.Keys.ToImmutableHashSet(),
            stores.ToImmutable());
    }

    static TypeRef? SpanElementType(MemberRef constructor)
        => constructor.Name == ".ctor"
            && constructor.DeclaringType.Kind == TypeRefKind.GenericInstance
            && constructor.DeclaringType.ElementType is { } definition
            && FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System",
                "Span`1")
            && constructor.DeclaringType.TypeArguments.Length == 1
            && constructor.ParameterTypes.Length == 2
            && constructor.ParameterTypes[0].Kind == TypeRefKind.Pointer
                ? constructor.DeclaringType.TypeArguments[0]
                : null;

    // The value `index` positions from the bottom of the `count` operands consumed
    // by the instruction at `offset`.
    static StackValue? ArgumentAt(
        TypedStackResult stack,
        int offset,
        int count,
        int index)
    {
        ImmutableArray<StackValue> before =
            stack.StackBeforeOffset(offset);
        int position = before.Length - count + index;
        return position >= 0 && position < before.Length
            ? before[position]
            : null;
    }

    static bool IsStore(ILOpCode operation)
        => operation is ILOpCode.Stind_i1
            or ILOpCode.Stind_i2
            or ILOpCode.Stind_i4
            or ILOpCode.Stind_i8
            or ILOpCode.Stind_i
            or ILOpCode.Stind_r4
            or ILOpCode.Stind_r8
            or ILOpCode.Stobj;

    // A sizeof-scaled allocation names its element size symbolically; a native-int
    // or struct store into it has that same width. Otherwise native int is taken at
    // its 64-bit maximum so the bound stays conservative on every target.
    static Linear? StoreWidth(
        DecodedInstruction instruction,
        TypeRef elementType,
        Linear size)
        => instruction.OpCode switch
        {
            ILOpCode.Stind_i1 => Linear.Of(1),
            ILOpCode.Stind_i2 => Linear.Of(2),
            ILOpCode.Stind_i4 or ILOpCode.Stind_r4 => Linear.Of(4),
            ILOpCode.Stind_i8 or ILOpCode.Stind_r8 => Linear.Of(8),
            ILOpCode.Stind_i
                when size.SizeToken != 0
                    && (FrameworkIdentity.IsCoreLibraryType(
                            elementType,
                            "System",
                            "IntPtr")
                        || FrameworkIdentity.IsCoreLibraryType(
                            elementType,
                            "System",
                            "UIntPtr"))
                => Linear.SizeOf(size.SizeToken),
            ILOpCode.Stind_i => Linear.Of(8),
            ILOpCode.Stobj => Linear.SizeOf(
                checked((int)instruction.OperandValue)),
            _ => null,
        };

    /// <summary>
    /// A byte quantity <c>Constant + Scale * sizeof(SizeToken)</c>; a zero
    /// <see cref="SizeToken"/> means a plain constant.
    /// </summary>
    internal readonly record struct Linear(
        long Constant,
        long Scale,
        int SizeToken)
    {
        internal static Linear Zero => default;

        internal static Linear Of(long constant) => new(constant, 0, 0);

        internal static Linear SizeOf(int token) => new(0, 1, token);

        // Arithmetic that overflows is unprovable, not exceptional: IL wraps, so a
        // wrapped displacement proves nothing about the allocation bound.
        internal static Linear? Add(Linear left, Linear right)
            => Combine(left, right) is { } token
                && TryAdd(left.Constant, right.Constant, out long constant)
                && TryAdd(left.Scale, right.Scale, out long scale)
                    ? new(constant, scale, token)
                    : null;

        internal static Linear? Multiply(Linear left, Linear right)
            => left.Scale == 0
                ? Scaled(right, left.Constant)
                : right.Scale == 0
                    ? Scaled(left, right.Constant)
                    : null;

        // Sound for every element size of at least one byte: both the constant and
        // the sizeof-scaled parts of the stored extent fit within the allocation.
        internal static bool FitsWithin(
            Linear displacement,
            Linear width,
            Linear size)
            => displacement.Constant >= 0
                && displacement.Scale >= 0
                && Add(displacement, width) is { } extent
                && Combine(extent, size) is not null
                && extent.Constant <= size.Constant
                && extent.Scale <= size.Scale;

        static Linear? Scaled(Linear value, long factor)
            => TryMultiply(value.Constant, factor, out long constant)
                && TryMultiply(value.Scale, factor, out long scale)
                    ? new(constant, scale, value.SizeToken)
                    : null;

        static bool TryAdd(long left, long right, out long sum)
        {
            sum = unchecked(left + right);
            return ((left ^ sum) & (right ^ sum)) >= 0;
        }

        static bool TryMultiply(long left, long right, out long product)
        {
            Int128 wide = (Int128)left * right;
            product = unchecked((long)wide);
            return wide == product;
        }

        static int? Combine(Linear left, Linear right)
            => left.Scale == 0 ? right.SizeToken
                : right.Scale == 0 || left.SizeToken == right.SizeToken
                    ? left.SizeToken
                    : null;
    }

    sealed class Tracer(
        TypedStackResult stack,
        IReadOnlyDictionary<int, DecodedInstruction> instructions)
    {
        // The localloc that produced `value` and the byte displacement from it.
        internal (int Allocation, Linear Displacement)? Address(
            StackValue value,
            int depth)
        {
            if (depth > MaxTraceDepth
                || !instructions.TryGetValue(
                    value.ProducerOffset,
                    out DecodedInstruction? producer))
            {
                return null;
            }
            int at = producer.Offset;
            switch (producer.OpCode)
            {
                case ILOpCode.Localloc:
                    return (at, Linear.Zero);
                case ILOpCode.Dup:
                case ILOpCode.Conv_i:
                case ILOpCode.Conv_u:
                    return ArgumentAt(stack, at, 1, 0) is { } source
                        ? Address(source, depth + 1)
                        : null;
                case ILOpCode.Add:
                case ILOpCode.Add_ovf_un:
                {
                    if (ArgumentAt(stack, at, 2, 0) is not { } left
                        || ArgumentAt(stack, at, 2, 1) is not { } right)
                    {
                        return null;
                    }
                    if (Address(left, depth + 1) is (int allocation, Linear start)
                        && Evaluate(right, depth + 1) is { } delta
                        && Linear.Add(start, delta) is { } displacement)
                    {
                        return (allocation, displacement);
                    }
                    if (Address(right, depth + 1) is (int other, Linear otherStart)
                        && Evaluate(left, depth + 1) is { } otherDelta
                        && Linear.Add(otherStart, otherDelta)
                            is { } otherDisplacement)
                    {
                        return (other, otherDisplacement);
                    }
                    return null;
                }
                default:
                    return null;
            }
        }

        // IL arithmetic wraps while Linear is exact. Every quantity is therefore
        // non-negative, so each term that reaches a non-zero result is at most that
        // result: when the final extent is within a real allocation, no
        // intermediate wrapped. The allocation size itself must be computed
        // exactly (`exact`): unscaled constants that fit in int32, or sizeof-scaled
        // arithmetic only through overflow-trapping operators, as Roslyn emits.
        internal Linear? Evaluate(
            StackValue value,
            int depth,
            bool exact = false)
        {
            if (depth > MaxTraceDepth
                || !instructions.TryGetValue(
                    value.ProducerOffset,
                    out DecodedInstruction? producer))
            {
                return null;
            }
            Linear? result = EvaluateProducer(producer, depth, exact);
            return result is { Constant: >= 0, Scale: >= 0 } quantity
                && (!exact
                    || quantity.Scale != 0
                    || quantity.Constant <= int.MaxValue)
                    ? quantity
                    : null;
        }

        Linear? EvaluateProducer(
            DecodedInstruction producer,
            int depth,
            bool exact)
        {
            int at = producer.Offset;
            switch (producer.OpCode)
            {
                case >= ILOpCode.Ldc_i4_0 and <= ILOpCode.Ldc_i4_8:
                    return Linear.Of(
                        producer.OpCode - ILOpCode.Ldc_i4_0);
                case ILOpCode.Ldc_i4_s:
                    return Linear.Of((sbyte)producer.OperandValue);
                case ILOpCode.Ldc_i4:
                    return Linear.Of((int)producer.OperandValue);
                case ILOpCode.Ldc_i8:
                    return Linear.Of(producer.OperandValue);
                case ILOpCode.Sizeof:
                    return Linear.SizeOf(
                        checked((int)producer.OperandValue));
                // A native-int conversion can truncate on a 32-bit target, so an
                // exact size converts only an int32-sized constant.
                case ILOpCode.Conv_i:
                case ILOpCode.Conv_u:
                case ILOpCode.Conv_i8:
                case ILOpCode.Conv_u8:
                {
                    if (ArgumentAt(stack, at, 1, 0) is not { } source
                        || Evaluate(source, depth + 1, exact)
                            is not { } converted)
                    {
                        return null;
                    }
                    return exact
                        && producer.OpCode is ILOpCode.Conv_i
                            or ILOpCode.Conv_u
                        && converted.Scale != 0
                            ? null
                            : converted;
                }
                case ILOpCode.Add:
                case ILOpCode.Add_ovf_un:
                case ILOpCode.Mul:
                case ILOpCode.Mul_ovf:
                case ILOpCode.Mul_ovf_un:
                {
                    if (ArgumentAt(stack, at, 2, 0) is not { } left
                        || ArgumentAt(stack, at, 2, 1) is not { } right
                        || Evaluate(left, depth + 1, exact) is not { } a
                        || Evaluate(right, depth + 1, exact) is not { } b)
                    {
                        return null;
                    }
                    Linear? combined = producer.OpCode is ILOpCode.Add
                        or ILOpCode.Add_ovf_un
                        ? Linear.Add(a, b)
                        : Linear.Multiply(a, b);
                    bool traps = producer.OpCode is ILOpCode.Add_ovf_un
                        or ILOpCode.Mul_ovf
                        or ILOpCode.Mul_ovf_un;
                    return exact
                        && !traps
                        && combined is { Scale: not 0 }
                            ? null
                            : combined;
                }
                default:
                    return null;
            }
        }
    }

    // Call shapes come from the same member resolver the safety scan uses; a call
    // it cannot shape leaves the typed stack incomplete, which recognizes nothing.
    sealed class MemberStackTypeResolver(Func<int, MemberRef> resolveMember)
        : IStackTypeResolver
    {
        public bool TryResolveCall(
            int methodToken,
            bool isNewObj,
            out int popCount,
            out bool pushes,
            out StackType pushType)
        {
            popCount = -1;
            pushes = false;
            pushType = StackType.Unknown;
            MemberRef member = resolveMember(methodToken);
            if (member.Kind == MemberKind.Unsupported
                || (member.SignatureHeader & 0x0F) == 0x05)
            {
                return false;
            }
            popCount = member.ParameterTypes.Length
                + (isNewObj || !member.HasThis ? 0 : 1);
            pushes = isNewObj
                || !FrameworkIdentity.IsCoreLibraryType(
                    member.ReturnType,
                    "System",
                    "Void");
            return true;
        }
    }
}
