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
/// same content-addressed blob, so the round-trip is opcode-exact. Scoped to the
/// primitive element types whose bytes decode unambiguously; any other element type
/// (an enum, a struct) is left untouched.</para>
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

            var elements = DecodeElements(element, data);
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
            // came from (csc re-lowers it to the same content-addressed blob, so
            // the round-trip is opcode-exact).
            if (construction.Constructor.DeclaringType is not
                { Kind: TypeRefKind.GenericInstance, ElementType: { Namespace: "System", Name: "ReadOnlySpan`1" }, TypeArguments: [var spanElement] } spanInstance)
                continue;
            if (construction.Arguments is not [LoadFieldAddress { FieldRvaData: { } rvaData }, Constant { Value: int rvaLength }])
                continue;

            var spanElements = DecodeElements(spanElement, rvaData);
            if (spanElements is null || spanElements.Count != rvaLength)
                continue;

            var spanLiteral = new SpanLiteral(spanElement, spanInstance, spanElements);
            context.Stepper.StepOver("raise ReadOnlySpan RVA field to span literal", construction);
            construction.ReplaceWith(spanLiteral);
        }
    }

    /// <summary>
    /// Decodes the little-endian RVA bytes as an array of the element type, or null
    /// when the element type is not a fixed-size primitive (the bytes carry no
    /// unambiguous reading without resolving an enum's underlying type or a struct's
    /// layout).
    /// </summary>
    static List<IrExpression>? DecodeElements(TypeRef element, byte[] data)
    {
        if (element.Kind != TypeRefKind.Definition || element.Namespace != "System")
            return null;

        int width = element.Name switch
        {
            "Boolean" or "SByte" or "Byte" => 1,
            "Int16" or "UInt16" or "Char" => 2,
            "Int32" or "UInt32" or "Single" => 4,
            "Int64" or "UInt64" or "Double" => 8,
            _ => 0,
        };
        if (width == 0 || data.Length % width != 0)
            return null;

        var span = data.AsSpan();
        var result = new List<IrExpression>(data.Length / width);
        for (int offset = 0; offset < data.Length; offset += width)
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
}
