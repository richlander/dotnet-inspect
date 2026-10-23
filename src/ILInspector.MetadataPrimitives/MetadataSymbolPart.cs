using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

/// <summary>
/// The shared marker vocabulary for metadata rows
/// (<c>docs/design/symbol-key.md#metadata-rows</c>): the parts the
/// <see cref="MetadataSymbolSpeller{TDomain}"/> writes for a Type definition,
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
    /// The declared scope: a Type reference spells its assembly reference's
    /// name, version, culture, and public key token after its names, and a
    /// Type definition spells its own assembly's name.
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
/// Writes <paramref name="row"/>'s key as a nested Key part of the key open
/// in <paramref name="into"/>. The consumer owns where rows are spelled and
/// memoized; it must not open a key in <paramref name="into"/> itself.
/// </summary>
public delegate void MetadataRowEmbedder<TDomain>(SymbolKeyBuilder<TDomain> into, EntityHandle row, MetadataRowRole role)
    where TDomain : unmanaged, Enum;

/// <summary>
/// A signature type as the speller's provider yields it: a composite staged in
/// the speller's scratch population (its generation at or above zero), or a
/// row the consumer resolved (generation below zero; the index of that
/// resolution in <see cref="Offset"/> and the row's token in
/// <see cref="Length"/>). Twelve bytes, so the decoder moves it in registers.
/// </summary>
public readonly struct MetadataSignatureKey
{
    readonly int _generation;
    readonly int _offset;
    readonly int _length;

    internal MetadataSignatureKey(EntityHandle row, int resolvedIndex)
    {
        _generation = -1;
        _offset = resolvedIndex;
        _length = MetadataTokens.GetToken(row);
    }

    internal MetadataSignatureKey(int scratchGeneration, int offset, int length)
    {
        _generation = scratchGeneration;
        _offset = offset;
        _length = length;
    }

    /// <summary>Whether this is a row the consumer embeds rather than a scratch composite.</summary>
    public bool IsRow => _generation < 0;

    /// <summary>The row, when <see cref="IsRow"/>.</summary>
    public EntityHandle Row => MetadataTokens.EntityHandle(_length);

    internal int Generation => _generation;

    internal int Offset => _offset;

    internal int Length => _length;
}
