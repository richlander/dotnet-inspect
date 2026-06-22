using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the compiler's constant-span lowering back into a <see cref="SpanLiteral"/>
/// — a <c>new T[] { ... }</c> array literal in a <see cref="System.ReadOnlySpan{T}"/>
/// context. A constant array/span initializer like
/// <c>static ReadOnlySpan&lt;uint&gt; Powers => new uint[] { 1, 10, 100, ... };</c>
/// is lowered by Roslyn to a <c>&lt;PrivateImplementationDetails&gt;</c> field whose
/// RVA maps the raw little-endian element bytes, loaded through the exact BCL
/// <c>RuntimeHelpers.CreateSpan&lt;T&gt;(ldtoken field)</c>. Left as-is that renders
/// as <c>RuntimeHelpers.CreateSpan&lt;uint&gt;(/* LoadToken Field ... */)</c> — the
/// unspellable <c>ldtoken</c> of a compiler-internal field name with angle brackets,
/// which never parses.
///
/// <para>The pass decodes the field's mapped RVA blob (captured on the
/// <see cref="LoadToken"/> at import) as the call's element type and rebuilds the
/// element constants. The compiler re-lowers the reconstructed array literal to the
/// same content-addressed blob, so the round-trip is opcode-exact. Scoped to
/// primitive element types whose bytes decode unambiguously, plus same-assembly
/// enums when an explicit span length lets the underlying width be inferred.
/// Other element types (struct layouts) are left untouched.</para>
/// </summary>
public sealed class RvaSpanPass : IIrPass
{
    public string Name => "rva-span";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var call in function.Descendants.OfType<Call>().ToList())
        {
            if (!MemberIdentity.IsRuntimeHelpersCreateSpan(call))
                continue;
            if (call.Arguments is not [LoadToken { Kind: RuntimeTokenKind.Field, FieldRvaData: { } data }])
                continue;
            if (call.Callee.TypeArguments is not [var element])
                continue;
            if (call.ResultType is not { } spanType)
                continue;

            var elements = DecodeElements(function, element, data);
            if (elements is null)
                continue;

            var literal = new SpanLiteral(element, spanType, elements);
            context.Stepper.StepOver("raise CreateSpan RVA blob to span literal", call);
            call.ReplaceWith(literal);
        }

        foreach (var construction in function.Descendants.OfType<NewObject>().ToList())
        {
            // csc's 1-byte-element optimization builds a constant
            // `ReadOnlySpan<byte>` directly as `new ReadOnlySpan<byte>(ref
            // <PrivateImplementationDetails>.HASH, length)` — a ref to the mapped
            // RVA blob plus its length — rather than through CreateSpan. The field
            // name has angle brackets, so left as-is it never parses; decode the
            // blob and rebuild the `new byte[] { ... }` literal the optimization
            // came from (csc re-lowers it to the same content-addressed blob).
            // The backing field's holder struct is sized one byte past the span's
            // length (a trailing pad), so the captured blob runs longer than the
            // data the span reads — decode exactly `rvaLength` elements from the
            // start, the span's own authoritative length.
            if (construction.Constructor.DeclaringType is not
                { Kind: TypeRefKind.GenericInstance, ElementType: { Namespace: "System", Name: "ReadOnlySpan`1" }, TypeArguments: [var spanElement] } spanInstance)
                continue;
            if (construction.Arguments is not [LoadFieldAddress { FieldRvaData: { } rvaData }, Constant { Value: int rvaLength }])
                continue;

            var spanElements = DecodeElements(function, spanElement, rvaData, rvaLength);
            if (spanElements is null)
                continue;

            var spanLiteral = new SpanLiteral(spanElement, spanInstance, spanElements);
            context.Stepper.StepOver("raise ReadOnlySpan RVA field to span literal", construction);
            construction.ReplaceWith(spanLiteral);
        }

        foreach (var call in function.Descendants.OfType<Call>().ToList())
        {
            // `new T[] { ... }` with constant elements lowers to `newarr T; dup;
            // ldtoken <PrivateImplementationDetails>.blob; call InitializeArray` — the
            // array creation feeds InitializeArray, whose RVA field name has angle
            // brackets and never parses. Decode the blob and fold it back onto the
            // array creation as an element initializer, then drop the InitializeArray
            // statement (csc re-lowers the literal to the same blob, opcode-exact).
            if (!MemberIdentity.IsRuntimeHelpersInitializeArray(call)
                || call.Parent is not ExpressionStatement statement
                || call.Arguments is not [{ } arrayArg, LoadToken { Kind: RuntimeTokenKind.Field, FieldRvaData: { } data }])
            {
                continue;
            }
            if (ResolveArrayCreation(arrayArg, function) is not { } creation)
                continue;

            var arrayElements = DecodeElements(function, creation.ElementType, data);
            if (arrayElements is null)
                continue;
            // The blob length is the array's byte size; honour an explicit element
            // count when the creation carries a constant length.
            if (creation.Length is Constant { Value: int count } && count != arrayElements.Count)
                continue;

            var arrayLiteral = new ArrayLiteral(creation.ElementType, TypeRef.SzArray(creation.ElementType), arrayElements);
            context.Stepper.StepOver("raise InitializeArray RVA blob to array literal", creation);
            creation.ReplaceWith(arrayLiteral);
            statement.Detach();
        }
    }

    /// <summary>
    /// Resolves the <see cref="NewArray"/> that initialized an InitializeArray target:
    /// the argument inline, or the unique array-creating store of the slot/local it
    /// loads. Null when the creation can't be pinned to a single array allocation.
    /// </summary>
    static NewArray? ResolveArrayCreation(IrExpression arrayArg, IrFunction function)
    {
        if (arrayArg is NewArray inline)
            return inline;

        IEnumerable<NewArray> defs = arrayArg switch
        {
            LoadStackSlot { Slot: var slot } => function.Descendants
                .OfType<StoreStackSlot>()
                .Where(store => store.Slot == slot)
                .Select(store => store.Value)
                .OfType<NewArray>(),
            LoadLocal { Index: var index } => function.Descendants
                .OfType<StoreLocal>()
                .Where(store => store.Index == index)
                .Select(store => store.Value)
                .OfType<NewArray>(),
            _ => [],
        };

        return defs.ToList() is [var single] ? single : null;
    }

    /// <summary>
    /// Decodes the little-endian RVA bytes as an array of the element type, or null
    /// when the element type is not a fixed-size primitive (the bytes carry no
    /// unambiguous reading without resolving an enum's underlying type or a struct's
    /// layout).
    /// <para>When <paramref name="elementCount"/> is given, decodes exactly that many
    /// elements from the start of the blob (and requires the blob to hold at least
    /// that many): the constant-<c>ReadOnlySpan&lt;byte&gt;</c> optimization sizes its
    /// backing field to the element bytes plus a trailing pad, so the holder struct's
    /// layout size — the blob length captured at import — runs one byte past the
    /// span's own length. The span's length is authoritative for what it reads.</para>
    /// </summary>
    static List<IrExpression>? DecodeElements(IrFunction function, TypeRef element, byte[] data, int? elementCount = null)
    {
        if (element.Kind != TypeRefKind.Definition)
            return null;

        if (PrimitiveElementWidth(element) is { } primitiveWidth)
            return DecodePrimitiveElements(element, data, primitiveWidth, elementCount);

        if (function.TypeShapes.GetValueOrDefault(element) != TypeShape.Enum || elementCount is not { } count)
            return null;
        var enumWidth = InferredEnumWidth(data.Length, count);
        if (enumWidth is null or > 4)
            return null;
        return DecodeEnumElements(element, data, enumWidth.Value, count, function.EnumMembers.GetValueOrDefault(element));
    }

    static int? PrimitiveElementWidth(TypeRef element)
    {
        if (element.Namespace != "System")
            return null;
        return element.Name switch
        {
            "Boolean" or "SByte" or "Byte" => 1,
            "Int16" or "UInt16" or "Char" => 2,
            "Int32" or "UInt32" or "Single" => 4,
            "Int64" or "UInt64" or "Double" => 8,
            _ => null,
        };
    }

    static List<IrExpression>? DecodePrimitiveElements(TypeRef element, byte[] data, int width, int? elementCount)
    {
        int length;
        if (elementCount is { } count)
        {
            if (count < 0 || count > data.Length / width)
                return null;
            length = count * width;
        }
        else
        {
            length = data.Length;
        }
        if (length > data.Length || length % width != 0)
            return null;

        var span = data.AsSpan();
        var result = new List<IrExpression>(length / width);
        for (int offset = 0; offset < length; offset += width)
        {
            var chunk = span.Slice(offset, width);
            object value = element.Name switch
            {
                "Boolean" => chunk[0] != 0,
                "SByte" => (sbyte)chunk[0],
                "Byte" => chunk[0],
                "Int16" => BitConverter.ToInt16(chunk),
                "UInt16" => BitConverter.ToUInt16(chunk),
                "Char" => (char)BitConverter.ToUInt16(chunk),
                "Int32" => BitConverter.ToInt32(chunk),
                "UInt32" => BitConverter.ToUInt32(chunk),
                "Single" => BitConverter.ToSingle(chunk),
                "Int64" => BitConverter.ToInt64(chunk),
                "UInt64" => BitConverter.ToUInt64(chunk),
                "Double" => BitConverter.ToDouble(chunk),
                _ => throw new InvalidOperationException(),
            };
            result.Add(new Constant(value, element));
        }
        return result;
    }

    static int? InferredEnumWidth(int byteLength, int count)
    {
        if (count <= 0 || byteLength % count != 0)
            return null;
        int width = byteLength / count;
        return width is 1 or 2 or 4 or 8 ? width : null;
    }

    static List<IrExpression>? DecodeEnumElements(
        TypeRef enumType,
        byte[] data,
        int width,
        int count,
        IReadOnlyDictionary<long, string>? members)
    {
        if (count < 0 || data.Length < count * width)
            return null;

        var span = data.AsSpan();
        var result = new List<IrExpression>(count);
        for (int i = 0; i < count; i++)
        {
            var chunk = span.Slice(i * width, width);
            int value = width switch
            {
                1 => EnumValue(chunk[0], unchecked((sbyte)chunk[0]), members),
                2 => EnumValue(BitConverter.ToUInt16(chunk), BitConverter.ToInt16(chunk), members),
                4 => BitConverter.ToInt32(chunk),
                _ => 0,
            };
            result.Add(new Constant(value, enumType));
        }
        return result;
    }

    static int EnumValue(long unsignedValue, long signedValue, IReadOnlyDictionary<long, string>? members)
    {
        if (members is not null && members.ContainsKey(signedValue))
            return checked((int)signedValue);
        return checked((int)unsignedValue);
    }
}
