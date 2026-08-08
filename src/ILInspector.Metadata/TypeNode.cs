using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Represents a type as a tree structure for applying nullability annotations.
/// Built by <see cref="TypeNodeProvider"/> during signature decoding, then
/// nullability bytes are applied via <see cref="ApplyNullability"/> before
/// rendering to a C# string with <see cref="Render"/>.
/// </summary>
internal abstract class TypeNode
{
    /// <summary>Whether this node is a reference type (can be annotated with ?).</summary>
    public abstract bool IsReferenceType { get; }

    /// <summary>Whether guarded decoding substituted any part of this type tree.</summary>
    public virtual bool IsDegraded => false;

    /// <summary>Set to true when nullability byte is 2.</summary>
    public bool IsNullableAnnotated { get; set; }

    /// <summary>
    /// Set to true when a <c>DynamicAttribute</c> transform flag marks this
    /// (<c>System.Object</c>) position as <c>dynamic</c>.
    /// </summary>
    public bool IsDynamic { get; set; }

    /// <summary>
    /// The C# element name for this node when it is a positional element of an
    /// enclosing <c>System.ValueTuple</c> instantiation authored with a named
    /// element (e.g. <c>count</c> in <c>(int count, string name)</c>). Null when
    /// the element is unnamed or this node is not a tuple element. Set by
    /// <see cref="ApplyTupleNames"/>.
    /// </summary>
    public string? TupleElementName { get; set; }

    /// <summary>Renders this type to a C# display string with nullability annotations,
    /// including C# tuple syntax (<c>(int count, string name)</c>) for
    /// <c>System.ValueTuple</c> instantiations.</summary>
    public string Render() => Render(canonicalTuples: false);

    /// <summary>
    /// Renders the structural, presentation-independent spelling used for member
    /// identity and correspondence: <c>System.ValueTuple&lt;int, string&gt;</c> rather
    /// than tuple syntax, and element names erased. Every other facet (nullability,
    /// <c>dynamic</c>, modifiers) is identical to <see cref="Render()"/>, so a non-tuple
    /// member's canonical spelling equals its display spelling byte-for-byte.
    /// </summary>
    public string RenderCanonical() => Render(canonicalTuples: true);

    /// <summary>Core renderer. When <paramref name="canonicalTuples"/> is true, a
    /// <c>System.ValueTuple</c> instantiation renders in generic form with no element
    /// names; otherwise it renders as C# tuple syntax.</summary>
    public abstract string Render(bool canonicalTuples);

    public virtual bool HasRequiredModifier(string ns, string name) => false;

    /// <summary>
    /// Walks the type tree in preorder, consuming bytes from the NullableAttribute array
    /// and setting <see cref="IsNullableAnnotated"/> where byte == 2.
    /// </summary>
    public abstract void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte);

    /// <summary>
    /// Walks the type tree in preorder, consuming flags from the DynamicAttribute
    /// transform-flags array and setting <see cref="IsDynamic"/> on
    /// <c>System.Object</c> positions whose flag is set. Mirrors the
    /// <see cref="ApplyNullability"/> traversal so the two annotation streams stay
    /// position-aligned. Absent flags (null) leave every position as authored.
    /// </summary>
    public abstract void ApplyDynamic(byte[]? flags, ref int position);

    protected static byte ConsumeByte(byte[]? bytes, ref int position, byte defaultByte)
    {
        if (bytes == null) return defaultByte;
        // Single-byte attribute: all positions use the same value
        if (bytes.Length == 1) { position++; return bytes[0]; }
        return position < bytes.Length ? bytes[position++] : defaultByte;
    }

    /// <summary>
    /// Consumes one DynamicAttribute flag for this node, marking it dynamic when
    /// the flag is set and the node is an <c>object</c> position that can spell
    /// <c>dynamic</c>. Non-object positions still consume their flag (always 0) to
    /// keep the preorder walk aligned.
    /// </summary>
    protected void ConsumeDynamicFlag(byte[]? flags, ref int position, bool canBeDynamic)
    {
        if (flags is null)
            return;
        // Unlike NullableAttribute, a single-element (marker-form) DynamicAttribute
        // describes only the bare top-level object; it must NOT broadcast into inner
        // nodes. Index strictly and treat positions past the end as non-dynamic.
        byte flag = position < flags.Length ? flags[position] : (byte)0;
        position++;
        if (canBeDynamic && flag != 0) IsDynamic = true;
    }

    /// <summary>Whether a rendered type name denotes <c>System.Object</c>.</summary>
    private protected static bool IsObjectName(string name) =>
        name is "object" or "System.Object";

    /// <summary>The fully qualified base name of a <c>System.ValueTuple</c> instantiation.</summary>
    private const string ValueTupleBaseName = "System.ValueTuple";

    /// <summary>
    /// When <paramref name="node"/> is a C# tuple — a <c>System.ValueTuple</c>
    /// instantiation of flattened arity &gt;= 2 — returns its logical element
    /// nodes in source order, absorbing the 8th "Rest" argument's nested
    /// <c>ValueTuple</c> chain (<c>ValueTuple&lt;T1..T7, ValueTuple&lt;T8..&gt;&gt;</c>
    /// flattens to <c>(T1..Tn)</c>). Returns null otherwise. A 1-arity
    /// <c>ValueTuple&lt;T&gt;</c> is intentionally not a tuple: C# <c>(T)</c> is
    /// parenthesization, not a one-element tuple, so it keeps generic spelling.
    /// </summary>
    internal static List<TypeNode>? TryGetTupleElements(TypeNode node)
    {
        if (node is not GenericTypeNode { BaseName: ValueTupleBaseName } tuple)
            return null;

        var elements = new List<TypeNode>();
        var current = tuple;
        while (true)
        {
            var args = current.Arguments;
            // A well-formed C# tuple of arity > 7 is ValueTuple<T1..T7, TRest>
            // where TRest is itself a ValueTuple holding the remaining elements.
            if (args.Length == 8)
            {
                if (args[7] is GenericTypeNode { BaseName: ValueTupleBaseName } rest)
                {
                    for (int i = 0; i < 7; i++)
                        elements.Add(args[i]);
                    current = rest;
                    continue;
                }

                // An 8-argument ValueTuple whose 8th ("Rest") argument is not itself a
                // ValueTuple cannot arise from C# tuple syntax (TRest must be a ValueTuple).
                // Treat it as an ordinary generic instantiation so it keeps its
                // System.ValueTuple<...> spelling and never collides with a genuine
                // eight-element tuple.
                return null;
            }
            elements.AddRange(args);
            break;
        }
        return elements.Count >= 2 ? elements : null;
    }

    /// <summary>
    /// The count of trailing null padding entries a tuple of the given flattened
    /// arity contributes to a <c>TupleElementNamesAttribute</c> names stream,
    /// accounting for its nested "Rest" <c>ValueTuple</c> containers:
    /// <c>f(n) = (n-7) + f(n-7)</c> for <c>n &gt; 7</c>, else 0. Empirically
    /// verified against the Roslyn encoding across arities 7..29.
    /// </summary>
    private static int TupleNamePadding(int arity)
        => arity <= 7 ? 0 : (arity - 7) + TupleNamePadding(arity - 7);

    /// <summary>
    /// Applies <c>TupleElementNamesAttribute</c> element names to the tuple
    /// positions in this type tree. The names are a flat stream ordered
    /// per tuple — a tuple's own element names first, then recursing into each
    /// element — with trailing null padding per tuple for its 8+ arity "Rest"
    /// nesting (see <see cref="TupleNamePadding"/>). Only tuple element positions
    /// consume the stream; non-tuple structure (arrays, generics, by-ref, etc.)
    /// consumes nothing. A null stream (attribute absent) leaves every tuple
    /// unnamed; unnamed tuples still render with positional <c>(...)</c> syntax.
    /// </summary>
    public void ApplyTupleNames(string?[]? names)
    {
        if (names is null) return;
        int position = 0;
        DecodeTupleNames(this, names, ref position);
    }

    private static void DecodeTupleNames(TypeNode node, string?[] names, ref int position)
    {
        if (TryGetTupleElements(node) is { } elements)
        {
            foreach (var element in elements)
            {
                element.TupleElementName = position < names.Length ? names[position] : null;
                position++;
            }
            position += TupleNamePadding(elements.Count);
            foreach (var element in elements)
                DecodeTupleNames(element, names, ref position);
        }
        else
        {
            foreach (var child in TypeChildren(node))
                DecodeTupleNames(child, names, ref position);
        }
    }

    /// <summary>
    /// The immediate type-node children of a non-tuple node, in preorder, used to
    /// find tuples nested inside non-tuple structure (e.g. <c>Func&lt;(int a,int b)&gt;</c>).
    /// </summary>
    private static IEnumerable<TypeNode> TypeChildren(TypeNode node) => node switch
    {
        GenericTypeNode generic => generic.Arguments,
        SZArrayTypeNode array => [array.ElementType],
        MDArrayTypeNode array => [array.ElementType],
        PointerTypeNode pointer => [pointer.ElementType],
        ByRefTypeNode byRef => [byRef.ElementType],
        FunctionPointerTypeNode fnptr => fnptr.ChildTypes,
        PassthroughTypeNode passthrough => [passthrough.Inner],
        _ => [],
    };
}

/// <summary>A visible fail-closed substitute for a rejected signature type.</summary>
internal sealed class DegradedTypeNode : TypeNode
{
    public override bool IsReferenceType => true;
    public override bool IsDegraded => true;

    public override string Render(bool canonicalTuples) => IsDynamic
        ? (IsNullableAnnotated ? "dynamic?" : "dynamic")
        : (IsNullableAnnotated ? "object?" : "object");

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        byte b = ConsumeByte(bytes, ref position, defaultByte);
        if (b == 2) IsNullableAnnotated = true;
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
        => ConsumeDynamicFlag(flags, ref position, canBeDynamic: true);
}

/// <summary>C# primitive types (int, string, object, etc.).</summary>
internal sealed class PrimitiveTypeNode(string name, bool isReferenceType) : TypeNode
{
    public override bool IsReferenceType => isReferenceType;

    public override string Render(bool canonicalTuples)
    {
        string effective = IsDynamic ? "dynamic" : name;
        return IsReferenceType && IsNullableAnnotated ? $"{effective}?" : effective;
    }

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        byte b = ConsumeByte(bytes, ref position, defaultByte);
        if (IsReferenceType && b == 2) IsNullableAnnotated = true;
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
        => ConsumeDynamicFlag(flags, ref position, canBeDynamic: IsObjectName(name));
}

/// <summary>Non-generic named types (JsonSerializer, Stream, etc.).</summary>
internal sealed class NamedTypeNode(string name, bool isReferenceType) : TypeNode
{
    public string Name => name;
    public override bool IsReferenceType => isReferenceType;

    public override string Render(bool canonicalTuples)
    {
        string effective = IsDynamic ? "dynamic" : name;
        return IsReferenceType && IsNullableAnnotated ? $"{effective}?" : effective;
    }

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        byte b = ConsumeByte(bytes, ref position, defaultByte);
        if (IsReferenceType && b == 2) IsNullableAnnotated = true;
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
        => ConsumeDynamicFlag(flags, ref position, canBeDynamic: IsObjectName(name));
}

/// <summary>Generic instantiations (Dictionary&lt;K,V&gt;, Task&lt;T&gt;, etc.).</summary>
internal sealed class GenericTypeNode(
    string baseName,
    bool isReferenceType,
    ImmutableArray<TypeNode> arguments,
    string nestedSuffix = "",
    bool degradedGenericType = false) : TypeNode
{
    public string BaseName => baseName;
    public ImmutableArray<TypeNode> Arguments => arguments;
    public override bool IsReferenceType => isReferenceType;
    public override bool IsDegraded => degradedGenericType || arguments.Any(argument => argument.IsDegraded);

    public override string Render(bool canonicalTuples)
    {
        // A System.ValueTuple instantiation spells as C# tuple syntax
        // (int count, string name) rather than System.ValueTuple<int, string>,
        // flattening 8+ arity "Rest" nesting and applying any element names.
        // The canonical (identity) spelling keeps the generic form and drops names.
        if (!canonicalTuples && TryGetTupleElements(this) is { } elements)
        {
            var parts = elements.Select(element =>
                element.TupleElementName is { Length: > 0 } elementName
                    ? $"{element.Render()} {elementName}"
                    : element.Render());
            var tuple = $"({string.Join(", ", parts)})";
            return IsReferenceType && IsNullableAnnotated ? $"{tuple}?" : tuple;
        }

        var argsStr = string.Join(", ", arguments.Select(a => a.Render(canonicalTuples)));
        var result = $"{baseName}<{argsStr}>{nestedSuffix}";
        return IsReferenceType && IsNullableAnnotated ? $"{result}?" : result;
    }

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        byte b = ConsumeByte(bytes, ref position, defaultByte);
        if (IsReferenceType && b == 2) IsNullableAnnotated = true;
        foreach (var arg in arguments)
            arg.ApplyNullability(bytes, ref position, defaultByte);
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
    {
        // The generic-type head is never object; it still consumes a flag.
        ConsumeDynamicFlag(flags, ref position, canBeDynamic: false);
        foreach (var arg in arguments)
            arg.ApplyDynamic(flags, ref position);
    }
}

/// <summary>Single-dimensional arrays (string[], int[], etc.).</summary>
internal sealed class SZArrayTypeNode(TypeNode elementType) : TypeNode
{
    public TypeNode ElementType => elementType;
    public override bool IsReferenceType => true;
    public override bool IsDegraded => elementType.IsDegraded;

    public override string Render(bool canonicalTuples)
    {
        var result = $"{elementType.Render(canonicalTuples)}[]";
        return IsNullableAnnotated ? $"{result}?" : result;
    }

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        byte b = ConsumeByte(bytes, ref position, defaultByte);
        if (b == 2) IsNullableAnnotated = true;
        elementType.ApplyNullability(bytes, ref position, defaultByte);
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
    {
        ConsumeDynamicFlag(flags, ref position, canBeDynamic: false);
        elementType.ApplyDynamic(flags, ref position);
    }
}

/// <summary>Multi-dimensional arrays (int[,], etc.).</summary>
internal sealed class MDArrayTypeNode(TypeNode elementType, int rank) : TypeNode
{
    public TypeNode ElementType => elementType;
    public override bool IsReferenceType => true;
    public override bool IsDegraded => elementType.IsDegraded;

    public override string Render(bool canonicalTuples)
    {
        var result = $"{elementType.Render(canonicalTuples)}[{new string(',', rank - 1)}]";
        return IsNullableAnnotated ? $"{result}?" : result;
    }

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        byte b = ConsumeByte(bytes, ref position, defaultByte);
        if (b == 2) IsNullableAnnotated = true;
        elementType.ApplyNullability(bytes, ref position, defaultByte);
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
    {
        ConsumeDynamicFlag(flags, ref position, canBeDynamic: false);
        elementType.ApplyDynamic(flags, ref position);
    }
}

/// <summary>Pointer types (int*, void*, etc.).</summary>
internal sealed class PointerTypeNode(TypeNode elementType) : TypeNode
{
    public TypeNode ElementType => elementType;
    public override bool IsReferenceType => false;
    public override bool IsDegraded => elementType.IsDegraded;

    public override string Render(bool canonicalTuples) => $"{elementType.Render(canonicalTuples)}*";

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        ConsumeByte(bytes, ref position, defaultByte);
        elementType.ApplyNullability(bytes, ref position, defaultByte);
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
    {
        ConsumeDynamicFlag(flags, ref position, canBeDynamic: false);
        elementType.ApplyDynamic(flags, ref position);
    }
}

/// <summary>By-reference types (ref T, out T, in T). Does not consume a nullability byte.</summary>
internal sealed class ByRefTypeNode(TypeNode elementType) : TypeNode
{
    public TypeNode ElementType => elementType;
    public override bool IsReferenceType => false;
    public override bool IsDegraded => elementType.IsDegraded;

    public override string Render(bool canonicalTuples) => $"ref {elementType.Render(canonicalTuples)}";

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        // ByRef is a modifier, not a type—does not consume a nullability byte.
        elementType.ApplyNullability(bytes, ref position, defaultByte);
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
    {
        // Unlike nullability, the DynamicAttribute transform-flags array reserves a
        // (always-false) slot for the by-ref itself, so consume one before the element.
        ConsumeDynamicFlag(flags, ref position, canBeDynamic: false);
        elementType.ApplyDynamic(flags, ref position);
    }

    public override bool HasRequiredModifier(string ns, string name)
        => elementType.HasRequiredModifier(ns, name);
}

/// <summary>Generic type or method parameters (T, TKey, etc.).</summary>
internal sealed class GenericParameterNode(string name, bool hasValueTypeConstraint) : TypeNode
{
    public override bool IsReferenceType => false;

    public override string Render(bool canonicalTuples) => IsNullableAnnotated ? $"{name}?" : name;

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        byte b = ConsumeByte(bytes, ref position, defaultByte);
        if (!hasValueTypeConstraint && b == 2) IsNullableAnnotated = true;
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
        => ConsumeDynamicFlag(flags, ref position, canBeDynamic: false);
}

/// <summary>Function pointer types (delegate*&lt;...&gt;).</summary>
internal sealed class FunctionPointerTypeNode(MethodSignature<TypeNode> signature) : TypeNode
{
    public IEnumerable<TypeNode> ChildTypes => signature.ParameterTypes.Prepend(signature.ReturnType);
    public override bool IsReferenceType => false;
    public override bool IsDegraded => signature.ReturnType.IsDegraded
        || signature.ParameterTypes.Any(parameter => parameter.IsDegraded);

    public override string Render(bool canonicalTuples)
    {
        var types = signature.ParameterTypes.Select(t => t.Render(canonicalTuples)).Append(signature.ReturnType.Render(canonicalTuples));
        string arguments = string.Join(", ", types);
        string convention = ConventionText(signature.Header.CallingConvention);
        return convention.Length == 0
            ? $"delegate*<{arguments}>"
            : $"delegate* {convention}<{arguments}>";
    }

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        ConsumeByte(bytes, ref position, defaultByte);
        signature.ReturnType.ApplyNullability(bytes, ref position, defaultByte);
        foreach (var parameter in signature.ParameterTypes)
            parameter.ApplyNullability(bytes, ref position, defaultByte);
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
    {
        ConsumeDynamicFlag(flags, ref position, canBeDynamic: false);
        signature.ReturnType.ApplyDynamic(flags, ref position);
        foreach (var parameter in signature.ParameterTypes)
            parameter.ApplyDynamic(flags, ref position);
    }

    static string ConventionText(SignatureCallingConvention convention) => convention switch
    {
        SignatureCallingConvention.Default => "",
        SignatureCallingConvention.CDecl => "unmanaged[Cdecl]",
        SignatureCallingConvention.StdCall => "unmanaged[Stdcall]",
        SignatureCallingConvention.ThisCall => "unmanaged[Thiscall]",
        SignatureCallingConvention.FastCall => "unmanaged[Fastcall]",
        _ => "unmanaged",
    };
}

/// <summary>Modified or pinned types—pass through to the underlying type.</summary>
internal class PassthroughTypeNode(TypeNode inner) : TypeNode
{
    public TypeNode Inner => inner;
    public override bool IsReferenceType => inner.IsReferenceType;
    public override bool IsDegraded => inner.IsDegraded;

    public override string Render(bool canonicalTuples) => inner.Render(canonicalTuples);

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        inner.ApplyNullability(bytes, ref position, defaultByte);
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
    {
        inner.ApplyDynamic(flags, ref position);
    }

    public override bool HasRequiredModifier(string ns, string name)
        => inner.HasRequiredModifier(ns, name);
}

/// <summary>Custom-modified types pass through for rendering while preserving declaration-site evidence.</summary>
internal sealed class ModifiedTypeNode(TypeNode modifier, TypeNode inner, bool isRequired) : PassthroughTypeNode(inner)
{
    public override bool IsDegraded => modifier.IsDegraded || base.IsDegraded;

    public override void ApplyDynamic(byte[]? flags, ref int position)
    {
        // Roslyn reserves one (always-false) DynamicAttribute slot per custom
        // modifier. Consume this modifier's slot before the modified type so the
        // flag stream stays aligned (e.g. a `ref readonly dynamic` return encodes
        // [byref, modreq(In), object] = [false, false, true]).
        ConsumeDynamicFlag(flags, ref position, canBeDynamic: false);
        base.ApplyDynamic(flags, ref position);
    }

    public override bool HasRequiredModifier(string ns, string name)
        => (isRequired && ModifierMatches(ns, name)) || base.HasRequiredModifier(ns, name);

    bool ModifierMatches(string ns, string name)
    {
        // A custom modifier is identified by its exact full type name. IL TypeReferences
        // always carry their namespace, so a real modreq renders fully qualified; a bare
        // render is the global namespace — a different type. No suffix or bare-name fallback:
        // Foo.IsVolatile and (global) IsVolatile are not System.Runtime.CompilerServices.IsVolatile.
        return modifier.Render() == (string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}");
    }
}
