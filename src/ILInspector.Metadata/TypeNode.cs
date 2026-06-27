using System.Collections.Immutable;

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

    /// <summary>Set to true when nullability byte is 2.</summary>
    public bool IsNullableAnnotated { get; set; }

    /// <summary>Renders this type to a C# type string with nullability annotations.</summary>
    public abstract string Render();

    public virtual bool HasRequiredModifier(string ns, string name) => false;

    /// <summary>
    /// Walks the type tree in preorder, consuming bytes from the NullableAttribute array
    /// and setting <see cref="IsNullableAnnotated"/> where byte == 2.
    /// </summary>
    public abstract void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte);

    protected static byte ConsumeByte(byte[]? bytes, ref int position, byte defaultByte)
    {
        if (bytes == null) return defaultByte;
        // Single-byte attribute: all positions use the same value
        if (bytes.Length == 1) { position++; return bytes[0]; }
        return position < bytes.Length ? bytes[position++] : defaultByte;
    }
}

/// <summary>C# primitive types (int, string, object, etc.).</summary>
internal sealed class PrimitiveTypeNode(string name, bool isReferenceType) : TypeNode
{
    public override bool IsReferenceType => isReferenceType;

    public override string Render() => IsReferenceType && IsNullableAnnotated ? $"{name}?" : name;

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        byte b = ConsumeByte(bytes, ref position, defaultByte);
        if (IsReferenceType && b == 2) IsNullableAnnotated = true;
    }
}

/// <summary>Non-generic named types (JsonSerializer, Stream, etc.).</summary>
internal sealed class NamedTypeNode(string name, bool isReferenceType) : TypeNode
{
    public string Name => name;
    public override bool IsReferenceType => isReferenceType;

    public override string Render() => IsReferenceType && IsNullableAnnotated ? $"{name}?" : name;

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        byte b = ConsumeByte(bytes, ref position, defaultByte);
        if (IsReferenceType && b == 2) IsNullableAnnotated = true;
    }
}

/// <summary>Generic instantiations (Dictionary&lt;K,V&gt;, Task&lt;T&gt;, etc.).</summary>
internal sealed class GenericTypeNode(string baseName, bool isReferenceType, ImmutableArray<TypeNode> arguments, string nestedSuffix = "") : TypeNode
{
    public override bool IsReferenceType => isReferenceType;

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
}

/// <summary>Single-dimensional arrays (string[], int[], etc.).</summary>
internal sealed class SZArrayTypeNode(TypeNode elementType) : TypeNode
{
    public override bool IsReferenceType => true;

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
}

/// <summary>Multi-dimensional arrays (int[,], etc.).</summary>
internal sealed class MDArrayTypeNode(TypeNode elementType, int rank) : TypeNode
{
    public override bool IsReferenceType => true;

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
}

/// <summary>Pointer types (int*, void*, etc.).</summary>
internal sealed class PointerTypeNode(TypeNode elementType) : TypeNode
{
    public override bool IsReferenceType => false;

    public override string Render() => $"{elementType.Render()}*";

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        ConsumeByte(bytes, ref position, defaultByte);
        elementType.ApplyNullability(bytes, ref position, defaultByte);
    }
}

/// <summary>By-reference types (ref T, out T, in T). Does not consume a nullability byte.</summary>
internal sealed class ByRefTypeNode(TypeNode elementType) : TypeNode
{
    public override bool IsReferenceType => false;

    public override string Render() => $"ref {elementType.Render()}";

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        // ByRef is a modifier, not a type—does not consume a nullability byte.
        elementType.ApplyNullability(bytes, ref position, defaultByte);
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
}

/// <summary>Function pointer types (delegate*).</summary>
internal sealed class FunctionPointerTypeNode : TypeNode
{
    public override bool IsReferenceType => false;

    public override string Render() => "delegate*";

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        ConsumeByte(bytes, ref position, defaultByte);
    }
}

/// <summary>Modified or pinned types—pass through to the underlying type.</summary>
internal class PassthroughTypeNode(TypeNode inner) : TypeNode
{
    public override bool IsReferenceType => inner.IsReferenceType;

    public override string Render() => inner.Render();

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        inner.ApplyNullability(bytes, ref position, defaultByte);
    }

    public override bool HasRequiredModifier(string ns, string name)
        => inner.HasRequiredModifier(ns, name);
}

/// <summary>Custom-modified types pass through for rendering while preserving declaration-site evidence.</summary>
internal sealed class ModifiedTypeNode(TypeNode modifier, TypeNode inner, bool isRequired) : PassthroughTypeNode(inner)
{
    public override bool HasRequiredModifier(string ns, string name)
        => (isRequired && ModifierMatches(ns, name)) || base.HasRequiredModifier(ns, name);

    bool ModifierMatches(string ns, string name)
    {
        var rendered = modifier.Render();
        return rendered == $"{ns}.{name}" || rendered == name || rendered.EndsWith("." + name, StringComparison.Ordinal);
    }
}
