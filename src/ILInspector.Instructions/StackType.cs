namespace ILInspector.Instructions;

/// <summary>
/// The simplified evaluation-stack type lattice from ECMA-335 Partition III §1.1.4
/// / §1.5 (the set of types a value can have while on the CLI evaluation stack).
/// This is deliberately coarser than the full type system: it is exactly the
/// information a typed-stack model supplies that raw-opcode heuristics cannot
/// (stack element kind, receiver kind, managed-pointer vs object-reference
/// distinction), and nothing more. It stays SRM-only and metadata-light: only
/// token-dependent results (field/element/return types) need a resolver, and
/// those degrade to <see cref="Unknown"/> rather than loading anything.
/// </summary>
public enum StackType
{
    /// <summary>No type known yet / merge of incompatible producers (lattice top).</summary>
    Unknown = 0,

    /// <summary>ECMA <c>int32</c> — also the home of <c>bool</c>, <c>char</c>, and sub-int integers once on the stack.</summary>
    Int32,

    /// <summary>ECMA <c>int64</c>.</summary>
    Int64,

    /// <summary>ECMA <c>native int</c> (<c>I</c>).</summary>
    NativeInt,

    /// <summary>ECMA <c>F</c> — native floating point (covers <c>float32</c>/<c>float64</c> on the stack).</summary>
    Float,

    /// <summary>ECMA <c>&amp;</c> — managed pointer (by-ref).</summary>
    ManagedPointer,

    /// <summary>ECMA <c>*</c> — unmanaged/transient pointer.</summary>
    UnmanagedPointer,

    /// <summary>ECMA <c>O</c> — object reference.</summary>
    ObjectReference,

    /// <summary>A value type instance carried on the stack (boxed shape is <see cref="ObjectReference"/>).</summary>
    ValueType,

    /// <summary>ECMA typed reference (<c>typedref</c> / <c>System.TypedReference</c>).</summary>
    TypedReference,
}

public static class StackTypeExtensions
{
    /// <summary>
    /// Lattice join used when two control-flow predecessors disagree about a slot.
    /// Equal types are preserved; anything else widens to <see cref="StackType.Unknown"/>
    /// (the conservative top), which is how the interpreter records "this slot is
    /// not uniformly typed across all paths" without guessing.
    /// </summary>
    public static StackType Merge(this StackType left, StackType right)
        => left == right ? left : StackType.Unknown;

    /// <summary>The short ECMA glyph for compact dumps (<c>i4</c>, <c>O</c>, <c>&amp;</c>, ...).</summary>
    public static string ToGlyph(this StackType type) => type switch
    {
        StackType.Int32 => "i4",
        StackType.Int64 => "i8",
        StackType.NativeInt => "i",
        StackType.Float => "F",
        StackType.ManagedPointer => "&",
        StackType.UnmanagedPointer => "*",
        StackType.ObjectReference => "O",
        StackType.ValueType => "vt",
        StackType.TypedReference => "typedref",
        _ => "?",
    };
}
