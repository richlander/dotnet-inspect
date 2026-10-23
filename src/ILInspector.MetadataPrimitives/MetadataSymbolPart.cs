using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

/// <summary>
/// The shared marker vocabulary for metadata rows
/// (<c>docs/design/symbol-key.md#metadata-rows</c>): the parts the
/// <see cref="MetadataSymbolSpeller{TDomain,TRow}"/> writes for a Type definition,
/// reference, or specification, a method, field, member reference, method
/// specification, standalone signature, primitive, or signature composite. A
/// consumer's domain declares these values under its own names, so two
/// domains spell one row with the same bytes.
/// </summary>
public enum MetadataSymbolPart : byte
{
    Namespace = 0x15,
    Vararg = 0x16,
    Return = 0x17,
    Type = 0x18,
    Method = 0x19,
    Field = 0x1A,
    MethodSpecification = 0x1F,
    LocalSignature = 0x20,
    StandaloneSignature = 0x21,
    Primitive = 0x22,
    SZArray = 0x23,
    Array = 0x24,
    ByReference = 0x25,
    Pointer = 0x26,
    FunctionPointer = 0x27,
    GenericInstance = 0x28,
    TypeParameter = 0x29,
    MethodParameter = 0x2A,
    RequiredModifier = 0x2B,
    OptionalModifier = 0x2C,
    Pinned = 0x2D,

    /// <summary>A Type's resolution scope, written only under <see cref="MetadataSymbolScope.Declared"/>.</summary>
    Scope = 0x3E,
}

/// <summary>How a Type's resolution scope enters its spelling.</summary>
public enum MetadataSymbolScope
{
    /// <summary>
    /// No scope: a Type spells as its namespace, declaring chain, and names, the
    /// identity Finding Diff Any's Body policy compares, under which a
    /// dependency's version is not a difference.
    /// </summary>
    Omitted,

    /// <summary>
    /// The declared scope, after the names: a Type reference spells the kind
    /// of its scope and then its assembly reference's name, version, culture,
    /// and public key or token, or its module reference's name, or the
    /// current assembly's name; a Type definition spells the current
    /// assembly's name.
    /// </summary>
    Declared,
}

/// <summary>Why a spelling embeds another row's key.</summary>
public enum MetadataRowRole
{
    /// <summary>The declaring Type of a method or field.</summary>
    Declaring,

    /// <summary>A Type or member a signature, specification, or member reference names.</summary>
    Referenced,
}

/// <summary>
/// A signature type as the speller's provider yields it: a composite staged in
/// the speller's scratch population (its generation at or above zero), a row
/// the consumer resolves when it is embedded (generation −1, the row's token
/// in <see cref="Length"/>), or a primitive the consumer resolves (generation
/// −2, the type code in <see cref="Length"/>). Twelve bytes, so the decoder
/// moves it in registers.
/// </summary>
public readonly struct MetadataSignatureKey
{
    const int RowGeneration = -1;
    const int PrimitiveGeneration = -2;

    readonly int _generation;
    readonly int _offset;
    readonly int _length;

    internal MetadataSignatureKey(EntityHandle row)
    {
        _generation = RowGeneration;
        _offset = 0;
        _length = MetadataTokens.GetToken(row);
    }

    internal MetadataSignatureKey(PrimitiveTypeCode typeCode)
    {
        _generation = PrimitiveGeneration;
        _offset = 0;
        _length = (int)typeCode;
    }

    internal MetadataSignatureKey(int scratchGeneration, int offset, int length)
    {
        _generation = scratchGeneration;
        _offset = offset;
        _length = length;
    }

    /// <summary>Whether this is a row the consumer resolves rather than a scratch composite.</summary>
    public bool IsRow => _generation == RowGeneration;

    /// <summary>Whether this is a primitive the consumer resolves.</summary>
    public bool IsPrimitive => _generation == PrimitiveGeneration;

    /// <summary>The row, when <see cref="IsRow"/>.</summary>
    public EntityHandle Row => MetadataTokens.EntityHandle(_length);

    /// <summary>The primitive, when <see cref="IsPrimitive"/>.</summary>
    public PrimitiveTypeCode PrimitiveType => (PrimitiveTypeCode)_length;

    internal int Generation => _generation;

    internal int Offset => _offset;

    internal int Length => _length;
}
