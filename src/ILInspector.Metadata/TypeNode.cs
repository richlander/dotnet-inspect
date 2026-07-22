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

    /// <summary>Renders this type to a C# type string with nullability annotations.</summary>
    public abstract string Render();

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
        byte flag = ConsumeByte(flags, ref position, 0);
        if (canBeDynamic && flag != 0) IsDynamic = true;
    }

    /// <summary>Whether a rendered type name denotes <c>System.Object</c>.</summary>
    private protected static bool IsObjectName(string name) =>
        name is "object" or "System.Object";
}

/// <summary>A visible fail-closed substitute for a rejected signature type.</summary>
internal sealed class DegradedTypeNode : TypeNode
{
    public override bool IsReferenceType => true;
    public override bool IsDegraded => true;

    public override string Render() => IsDynamic
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

    public override string Render()
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

    public override string Render()
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
    public override bool IsReferenceType => isReferenceType;
    public override bool IsDegraded => degradedGenericType || arguments.Any(argument => argument.IsDegraded);

    public override string Render()
    {
        var argsStr = string.Join(", ", arguments.Select(a => a.Render()));
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
    public override bool IsReferenceType => true;
    public override bool IsDegraded => elementType.IsDegraded;

    public override string Render()
    {
        var result = $"{elementType.Render()}[]";
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
    public override bool IsReferenceType => true;
    public override bool IsDegraded => elementType.IsDegraded;

    public override string Render()
    {
        var result = $"{elementType.Render()}[{new string(',', rank - 1)}]";
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
    public override bool IsReferenceType => false;
    public override bool IsDegraded => elementType.IsDegraded;

    public override string Render() => $"{elementType.Render()}*";

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
    public override bool IsReferenceType => false;
    public override bool IsDegraded => elementType.IsDegraded;

    public override string Render() => $"ref {elementType.Render()}";

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
internal sealed class GenericParameterNode(string name) : TypeNode
{
    public override bool IsReferenceType => false; // unknown; annotation still applies

    public override string Render() => IsNullableAnnotated ? $"{name}?" : name;

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        byte b = ConsumeByte(bytes, ref position, defaultByte);
        if (b == 2) IsNullableAnnotated = true;
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
        => ConsumeDynamicFlag(flags, ref position, canBeDynamic: false);
}

/// <summary>Function pointer types (delegate*&lt;...&gt;).</summary>
internal sealed class FunctionPointerTypeNode(MethodSignature<TypeNode> signature) : TypeNode
{
    public override bool IsReferenceType => false;
    public override bool IsDegraded => signature.ReturnType.IsDegraded
        || signature.ParameterTypes.Any(parameter => parameter.IsDegraded);

    public override string Render()
    {
        var types = signature.ParameterTypes.Select(t => t.Render()).Append(signature.ReturnType.Render());
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
    public override bool IsReferenceType => inner.IsReferenceType;
    public override bool IsDegraded => inner.IsDegraded;

    public override string Render() => inner.Render();

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
