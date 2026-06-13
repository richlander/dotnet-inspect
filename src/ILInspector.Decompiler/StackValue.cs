namespace ILInspector.Decompiler;

/// <summary>
/// Represents a value on the IL evaluation stack, tracking both its
/// <see cref="StackValueKind"/> category and an optional type name for
/// object references and value types.
/// </summary>
public readonly record struct StackValue
{
    /// <summary>The ECMA-335 stack type category.</summary>
    public StackValueKind Kind { get; }

    /// <summary>
    /// The resolved type name (e.g., "System.String", "int") for ObjRef and
    /// ValueType kinds. Null for primitive kinds (Int32, Int64, Float, NativeInt).
    /// For ByRef, this is the referent type name.
    /// </summary>
    public string? TypeName { get; }

    StackValue(StackValueKind kind, string? typeName = null)
    {
        Kind = kind;
        TypeName = typeName;
    }

    public static StackValue CreatePrimitive(StackValueKind kind, string? typeName = null) => new(kind, typeName);
    public static StackValue CreateObjRef(string? typeName = null) => new(StackValueKind.ObjRef, typeName);
    public static StackValue CreateValueType(string typeName) => new(StackValueKind.ValueType, typeName);
    public static StackValue CreateByRef(string? referentType = null) => new(StackValueKind.ByRef, referentType);
    public static StackValue CreateUnknown() => new(StackValueKind.Unknown);

    /// <summary>
    /// Creates a <see cref="StackValue"/> from a decoded type name string.
    /// Maps primitive type names to the appropriate <see cref="StackValueKind"/>.
    /// </summary>
    public static StackValue FromTypeName(string typeName) => typeName switch
    {
        // The type name is preserved so the emitter can keep C#-level
        // distinctions (char vs int literals, bool contexts) that the stack
        // category erases.
        "bool" or "System.Boolean" or
        "char" or "System.Char" or
        "sbyte" or "System.SByte" or
        "byte" or "System.Byte" or
        "short" or "System.Int16" or
        "ushort" or "System.UInt16" or
        "int" or "System.Int32" or
        "uint" or "System.UInt32"
            => CreatePrimitive(StackValueKind.Int32, typeName),

        "long" or "System.Int64" or
        "ulong" or "System.UInt64"
            => CreatePrimitive(StackValueKind.Int64, typeName),

        "float" or "System.Single" or
        "double" or "System.Double"
            => CreatePrimitive(StackValueKind.Float, typeName),

        "nint" or "System.IntPtr" or
        "nuint" or "System.UIntPtr"
            => CreatePrimitive(StackValueKind.NativeInt),

        "void" or "System.Void"
            => CreateUnknown(),

        "string" or "System.String" or "object" or "System.Object"
            => CreateObjRef(typeName),

        _ when typeName.EndsWith('*') => CreatePrimitive(StackValueKind.NativeInt),
        _ when typeName.EndsWith('&') => CreateByRef(typeName[..^1]),
        _ when typeName.EndsWith("[]") => CreateObjRef(typeName),

        // Default: assume object reference (classes are more common than structs
        // in general IL; the caller can refine if they know better)
        _ => CreateObjRef(typeName)
    };

    /// <summary>
    /// Merges two stack values per ECMA-335 III.1.8.1.3. Returns the merged
    /// value, or <see cref="StackValueKind.Unknown"/> if the kinds are incompatible.
    /// </summary>
    public static StackValue Merge(StackValue a, StackValue b)
    {
        if (a.Kind == b.Kind)
        {
            if (a.TypeName == b.TypeName)
                return a;

            // Same kind, different types — widen
            return a.Kind switch
            {
                StackValueKind.ObjRef => CreateObjRef("object"),
                StackValueKind.ValueType => CreateUnknown(),
                _ => a // primitive kinds don't carry type names
            };
        }

        // Int32 and NativeInt merge to NativeInt
        if ((a.Kind is StackValueKind.Int32 && b.Kind is StackValueKind.NativeInt) ||
            (a.Kind is StackValueKind.NativeInt && b.Kind is StackValueKind.Int32))
            return CreatePrimitive(StackValueKind.NativeInt);

        return CreateUnknown();
    }

    public override string ToString() =>
        TypeName is not null ? $"{Kind}({TypeName})" : Kind.ToString();
}
