using System.Collections.Immutable;
using System.Reflection.Metadata;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Guarded wrappers around SRM's signature-decode entry points that use <see cref="TypeRefDecoder"/>.
///
/// Attacker-controlled metadata can encode a signature whose structural nesting is deep enough to
/// overflow the native stack inside SRM's <c>SignatureDecoder</c> (an uncatchable
/// <c>StackOverflowException</c>) or whose declared counts trigger a multi-gigabyte pre-allocation.
/// <see cref="SignatureBlobGuard"/> detects both cheaply before decoding, so these helpers fail
/// closed to an unresolved shape instead of crashing. Real signatures are shallow, so the guard
/// only trips on malformed input. Every Decompiler decode of untrusted metadata should route through
/// here rather than calling <c>Decode*Signature</c> directly.
/// </summary>
internal static class GuardedDecode
{
    public static MethodSignature<TypeRef> MethodSignature(MetadataReader reader, MethodDefinition method, GenericScope scope)
        => SignatureBlobGuard.IsSafeToDecode(reader, method.Signature, SignatureBlobGuard.Kind.Method)
            ? method.DecodeSignature(TypeRefDecoder.Instance, scope)
            : UnsupportedMethodSignature("method");

    public static MethodSignature<TypeRef> MethodSignature(MetadataReader reader, MemberReference member, GenericScope scope)
        => SignatureBlobGuard.IsSafeToDecode(reader, member.Signature, SignatureBlobGuard.Kind.Method)
            ? member.DecodeMethodSignature(TypeRefDecoder.Instance, scope)
            : UnsupportedMethodSignature("member-reference");

    public static MethodSignature<TypeRef> MethodSignature(MetadataReader reader, StandaloneSignature signature, GenericScope scope)
        => SignatureBlobGuard.IsSafeToDecode(reader, signature.Signature, SignatureBlobGuard.Kind.Method)
            ? signature.DecodeMethodSignature(TypeRefDecoder.Instance, scope)
            : UnsupportedMethodSignature("standalone");

    public static ImmutableArray<TypeRef> MethodSpecArguments(MetadataReader reader, MethodSpecification spec, GenericScope scope)
        => SignatureBlobGuard.IsSafeToDecode(reader, spec.Signature, SignatureBlobGuard.Kind.MethodSpecification)
            ? spec.DecodeSignature(TypeRefDecoder.Instance, scope)
            : [];

    public static ImmutableArray<TypeRef> LocalTypes(MetadataReader reader, StandaloneSignature signature, GenericScope scope)
        => SignatureBlobGuard.IsSafeToDecode(reader, signature.Signature, SignatureBlobGuard.Kind.LocalVariables)
            ? signature.DecodeLocalSignature(TypeRefDecoder.Instance, scope)
            : [];

    public static TypeRef FieldType(MetadataReader reader, FieldDefinition field, GenericScope scope)
        => SignatureBlobGuard.IsSafeToDecode(reader, field.Signature, SignatureBlobGuard.Kind.Field)
            ? field.DecodeSignature(TypeRefDecoder.Instance, scope)
            : TypeRef.Unsupported("field signature nesting depth exceeded");

    public static TypeRef FieldType(MetadataReader reader, MemberReference member, GenericScope scope)
        => SignatureBlobGuard.IsSafeToDecode(reader, member.Signature, SignatureBlobGuard.Kind.Field)
            ? member.DecodeFieldSignature(TypeRefDecoder.Instance, scope)
            : TypeRef.Unsupported("field-reference signature nesting depth exceeded");

    public static TypeRef TypeSpecification(MetadataReader reader, TypeSpecificationHandle handle, GenericScope scope)
    {
        var spec = reader.GetTypeSpecification(handle);
        return SignatureBlobGuard.IsSafeToDecode(reader, spec.Signature, SignatureBlobGuard.Kind.TypeSpecification)
            ? spec.DecodeSignature(TypeRefDecoder.Instance, scope)
            : TypeRef.Unsupported("type-specification nesting depth exceeded");
    }

    static MethodSignature<TypeRef> UnsupportedMethodSignature(string what)
        => new(default, TypeRef.Unsupported($"{what} signature nesting depth exceeded"), 0, 0, []);
}
