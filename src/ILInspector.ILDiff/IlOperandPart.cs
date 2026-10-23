using ILInspector.Metadata;

namespace ILInspector.ILDiff;

/// <summary>
/// The IL census cursor's key domain: the shared metadata row vocabulary
/// (<c>docs/design/symbol-key.md#metadata-rows</c>) under this domain's names,
/// so a cursor's operand bytes are Finding Diff Any's for the same row under
/// the same scope, and one marker of its own for the <c>calli</c> signature,
/// which compares by its bytes as Finding Diff Any compares it.
/// </summary>
public enum IlOperandPart : byte
{
    /// <summary>A <c>calli</c> stand-alone signature as its blob bytes.</summary>
    CalliSignature = SymbolKeyDomain.FirstMarker,

    Namespace = MetadataSymbolPart.Namespace,
    Vararg = MetadataSymbolPart.Vararg,
    Return = MetadataSymbolPart.Return,
    Type = MetadataSymbolPart.Type,
    Method = MetadataSymbolPart.Method,
    Field = MetadataSymbolPart.Field,
    MethodSpecification = MetadataSymbolPart.MethodSpecification,
    LocalSignature = MetadataSymbolPart.LocalSignature,
    StandaloneSignature = MetadataSymbolPart.StandaloneSignature,
    Primitive = MetadataSymbolPart.Primitive,
    SZArray = MetadataSymbolPart.SZArray,
    Array = MetadataSymbolPart.Array,
    ByReference = MetadataSymbolPart.ByReference,
    Pointer = MetadataSymbolPart.Pointer,
    FunctionPointer = MetadataSymbolPart.FunctionPointer,
    GenericInstance = MetadataSymbolPart.GenericInstance,
    TypeParameter = MetadataSymbolPart.TypeParameter,
    MethodParameter = MetadataSymbolPart.MethodParameter,
    RequiredModifier = MetadataSymbolPart.RequiredModifier,
    OptionalModifier = MetadataSymbolPart.OptionalModifier,
    Pinned = MetadataSymbolPart.Pinned,
    Scope = MetadataSymbolPart.Scope,
}
